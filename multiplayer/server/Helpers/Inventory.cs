using Mmorpg.Network;
using Server.World;
using Server.Utils;
using Server.World.Game;

namespace Server.Helpers;

/// <summary>Server-authoritative inventory request handling, self bag/equipment deltas, predictive equip rollback, and nearby visible-equipment broadcasts for one <see cref="GameWorld"/>.</summary>
public static class Inventory {
    /// <summary>Applies a server-authoritative create-item request and sends the resulting self inventory delta.</summary>
    public static void HandleCreateItemRequest(GameWorldRef wr, GameWorldPlayer player, CreateItemRequest request) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);

        if (!player.InventoryManager.TryCreateItem(request.ItemId, ToEffectOverrides(request.EffectOverrides), out var result)) {
            return;
        }

        ApplyInventoryMutation(wr, player, result);
    }

    /// <summary>Updates one bagged item's UI position and server-owned z-order; no nearby-player broadcast is needed.</summary>
    public static void HandleMoveItemInBagRequest(GameWorldRef wr, GameWorldPlayer player, MoveItemInBagRequest request) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);

        var bagX = request.HasBagX ? request.BagX : (int?)null;
        var bagY = request.HasBagY ? request.BagY : (int?)null;
        if (!player.InventoryManager.TryMoveItemInBag(request.ItemUid, bagX, bagY, out var result)) {
            return;
        }

        ApplyInventoryMutation(wr, player, result);
    }

    /// <summary>Equips one bag item, then sends self inventory deltas and nearby visible-slot appearance updates.</summary>
    public static void HandleEquipItemRequest(GameWorldRef wr, GameWorldPlayer player, EquipItemRequest request) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);

        var targetSlot = request.HasTargetSlot ? request.TargetSlot : null;
        if (!player.InventoryManager.TryEquipItem(request.ItemUid, targetSlot, player.GenderValue, out var result)) {
            SendEquipRollbackIfNeeded(wr, player, request.ItemUid, targetSlot);
            return;
        }

        ApplyInventoryMutation(wr, player, result);
    }

    /// <summary>After the player’s gender changes, removes equipped items that are restricted to another gender; notifies the player and nearby observers for visible slots.</summary>
    public static void UnequipItemsInvalidForCurrentGender(GameWorldRef wr, GameWorldPlayer player) {
        ArgumentNullException.ThrowIfNull(player);
        if (!player.InventoryManager.TryUnequipAllGenderMismatchedEquipment(player.GenderValue, out var result)) {
            return;
        }

        ApplyInventoryMutation(wr, player, result);
    }

    /// <summary>Unequips one slot back into the bag, optionally honoring the bag coordinates provided by the client drop.</summary>
    public static void HandleUnequipItemRequest(GameWorldRef wr, GameWorldPlayer player, UnequipItemRequest request) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);

        var bagX = request.HasBagX ? request.BagX : (int?)null;
        var bagY = request.HasBagY ? request.BagY : (int?)null;
        if (!player.InventoryManager.TryUnequipItem(request.Slot, request.ItemUid, bagX, bagY, out var result)) {
            return;
        }

        ApplyInventoryMutation(wr, player, result);
    }

    /// <summary>Consumes one bagged consumable item and sends the resulting self inventory delta.</summary>
    public static void HandleConsumeItemRequest(GameWorldRef wr, GameWorldPlayer player, ConsumeItemRequest request) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);

        if (!player.InventoryManager.TryConsumeItem(request.ItemUid, out var result)) {
            return;
        }

        ApplyInventoryMutation(wr, player, result);
    }

    /// <summary>Removes one bag entry so the world can drop it onto the current cell as an authoritative ground-item stack entry.</summary>
    public static bool TryRemoveBagItemForGroundDrop(GameWorldRef wr, GameWorldPlayer player, long itemUid, out InventoryItemState? droppedItem) {
        ArgumentNullException.ThrowIfNull(player);
        droppedItem = null;
        if (!player.InventoryManager.TryRemoveItemFromBagForGroundDrop(itemUid, out droppedItem, out var result)) {
            return false;
        }

        ApplyInventoryMutation(wr, player, result);
        return true;
    }

    /// <summary>Removes <paramref name="quantity"/> of an item id from the bag (all or nothing) and sends the resulting self delta.</summary>
    public static bool TryRemoveBagItemsById(GameWorldRef wr, GameWorldPlayer player, int itemId, int quantity) {
        ArgumentNullException.ThrowIfNull(player);
        if (!player.InventoryManager.TryRemoveItemsById(itemId, quantity, out var result)) {
            return false;
        }

        ApplyInventoryMutation(wr, player, result);
        return true;
    }

    /// <summary>Adds one authoritative ground item into the player's bag, applying normal stack-merge rules and sending the resulting self delta.</summary>
    public static bool TryAddGroundItemToBag(GameWorldRef wr, GameWorldPlayer player, GroundItemState groundItem) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(groundItem);
        if (!player.InventoryManager.TryAddGroundItemToBag(groundItem, out var result)) {
            return false;
        }

        ApplyInventoryMutation(wr, player, result);
        return true;
    }

    /// <summary>Sends the self inventory delta and any nearby-player visible-slot appearance updates produced by one inventory mutation.</summary>
    public static void ApplyInventoryMutation(GameWorldRef wr, GameWorldPlayer player, InventoryMutationResult result) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(result);

        foreach (var removedItemUid in result.RemovedFromBagItemUids) {
            NetworkManager.SendToPlayer(player, NetworkManager.CreateItemRemovedFromBag(removedItemUid));
        }
        foreach (var unequipped in result.Unequipped) {
            var selfUnequippedMessage = NetworkManager.CreateItemUnequipped(player.PlayerId, unequipped.Slot, unequipped.ItemUid);
            NetworkManager.SendToPlayer(player, selfUnequippedMessage);
            if (!InventoryManager.IsVisibleAppearanceSlot(unequipped.Slot)) {
                continue;
            }

            foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId, excludeDisconnected: true)) {
                NetworkManager.SendToPlayer(nearbyPlayer, selfUnequippedMessage);
            }
        }
        foreach (var addedItem in result.AddedToBag) {
            NetworkManager.SendToPlayer(player, NetworkManager.CreateItemAddedToBag(addedItem));
        }
        foreach (var movedItem in result.MovedInBag) {
            NetworkManager.SendToPlayer(player, NetworkManager.CreateItemMovedInBag(movedItem));
        }
        foreach (var equipped in result.Equipped) {
            var equippedMessage = NetworkManager.CreateItemEquipped(player.PlayerId, equipped.Slot, equipped.Item);
            NetworkManager.SendToPlayer(player, equippedMessage);
            if (!InventoryManager.IsVisibleAppearanceSlot(equipped.Slot)) {
                continue;
            }

            foreach (var nearbyPlayer in wr.PlayerSpatialGrid.GetNearbyPlayers(player.PosX, player.PosY, player.SessionId, excludeDisconnected: true)) {
                NetworkManager.SendToPlayer(nearbyPlayer, equippedMessage);
            }
        }
    }

    /// <summary>When the client predicted an equip locally but the server rejected it, clear the slot and restore the still-bagged item.</summary>
    private static void SendEquipRollbackIfNeeded(GameWorldRef wr, GameWorldPlayer player, long itemUid, string? requestedTargetSlot) {
        ArgumentNullException.ThrowIfNull(player);

        InventoryItemState? item = null;
        foreach (var bagItem in player.InventoryManager.BagItems) {
            if (bagItem.ItemUid == itemUid) {
                item = bagItem;
                break;
            }
        }
        if (item is null) {
            return;
        }
        if (!wr.ItemsById.TryGetValue(item.ItemId, out var itemDef)) {
            return;
        }

        var predictedSlot = ResolvePredictedEquipSlot(player, itemDef.ItemType, requestedTargetSlot);
        NetworkManager.SendToPlayer(player, NetworkManager.CreateItemUnequipped(player.PlayerId, predictedSlot, item.ItemUid));
        NetworkManager.SendToPlayer(player, NetworkManager.CreateItemAddedToBag(item.Clone()));
    }

    /// <summary>Matches the client-side ring-target resolution so rejected equip requests can roll back the same predicted slot.</summary>
    private static string ResolvePredictedEquipSlot(GameWorldPlayer player, string itemType, string? requestedTargetSlot) {
        ArgumentNullException.ThrowIfNull(player);

        if (!string.Equals(itemType, "ring", StringComparison.Ordinal)) {
            return itemType;
        }
        if (string.Equals(requestedTargetSlot, "ring-left", StringComparison.Ordinal) ||
            string.Equals(requestedTargetSlot, "ring-right", StringComparison.Ordinal)) {
            return requestedTargetSlot!;
        }
        if (!player.InventoryManager.EquippedItems.ContainsKey("ring-left")) {
            return "ring-left";
        }
        if (!player.InventoryManager.EquippedItems.ContainsKey("ring-right")) {
            return "ring-right";
        }
        return "ring-left";
    }

    /// <summary>Copies protobuf effect override rows into the same record shape used by item config and persistence.</summary>
    private static ItemEffectConfig[]? ToEffectOverrides(IEnumerable<ItemEffectEntry> effectOverrides) {
        ArgumentNullException.ThrowIfNull(effectOverrides);

        var rows = new List<ItemEffectConfig>();
        foreach (var effectOverride in effectOverrides) {
            rows.Add(new ItemEffectConfig(
                effectOverride.Effect,
                effectOverride.HasEffectColor ? (int)effectOverride.EffectColor : null));
        }
        return rows.Count == 0 ? null : rows.ToArray();
    }
}

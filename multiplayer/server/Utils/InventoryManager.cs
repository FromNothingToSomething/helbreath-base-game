using Server.World;
using Server.World.Game;

namespace Server.Utils;

/// <summary>Mutable per-instance item state owned by one player's inventory.</summary>
public sealed class InventoryItemState {
    /// <summary>Stable catalog row id from <c>Items.json</c>.</summary>
    public int ItemId { get; private set; }
    /// <summary>Authoritative runtime instance id generated server-side.</summary>
    public long ItemUid { get; private set; }
    /// <summary>Bag X position in the client inventory UI when the item is bagged; preserved while equipped for future unequip.</summary>
    public int? BagX { get; set; }
    /// <summary>Bag Y position in the client inventory UI when the item is bagged; preserved while equipped for future unequip.</summary>
    public int? BagY { get; set; }
    /// <summary>Authoritative quantity for stackable items; otherwise 1.</summary>
    public int Quantity { get; set; }
    /// <summary>Authoritative bag z-order index; bag rendering is sorted ascending by this value.</summary>
    public int BagZIndex { get; set; }
    /// <summary>Per-instance effect overrides requested by the client (for example custom tint/glow in the item dialog).</summary>
    public ItemEffectConfig[]? EffectOverrides { get; set; }

    public InventoryItemState(
        int itemId,
        long itemUid,
        int? bagX,
        int? bagY,
        int quantity,
        int bagZIndex,
        ItemEffectConfig[]? effectOverrides) {
        ItemId = itemId;
        ItemUid = itemUid;
        BagX = bagX;
        BagY = bagY;
        Quantity = quantity;
        BagZIndex = bagZIndex;
        EffectOverrides = CloneEffectOverrides(effectOverrides);
    }

    /// <summary>Creates a detached copy for outgoing messages and mutation results.</summary>
    public InventoryItemState Clone() {
        return new InventoryItemState(ItemId, ItemUid, BagX, BagY, Quantity, BagZIndex, EffectOverrides);
    }

    /// <summary>Converts live state into the persistence record stored on disk and world transfers.</summary>
    public PersistedInventoryItem ToPersistedItem() {
        return new PersistedInventoryItem(ItemId, ItemUid, BagX, BagY, Quantity, BagZIndex, CloneEffectOverrides(EffectOverrides));
    }

    /// <summary>Converts equipped live state into the slimmer persistence record that omits bag-only runtime fields.</summary>
    public PersistedEquippedItem ToPersistedEquippedItem() {
        return new PersistedEquippedItem(ItemId, ItemUid, BagX, BagY, CloneEffectOverrides(EffectOverrides));
    }

    /// <summary>Rehydrates live inventory state from persisted storage.</summary>
    public static InventoryItemState FromPersistedItem(PersistedInventoryItem item) {
        ArgumentNullException.ThrowIfNull(item);
        return new InventoryItemState(item.ItemId, item.ItemUid, item.BagX, item.BagY, item.Quantity, item.BagZIndex, item.EffectOverrides);
    }

    /// <summary>Rehydrates equipped live state from persisted storage, restoring only fields that matter while the item is equipped.</summary>
    public static InventoryItemState FromPersistedEquippedItem(PersistedEquippedItem item) {
        ArgumentNullException.ThrowIfNull(item);
        return new InventoryItemState(item.ItemId, item.ItemUid, item.BagX, item.BagY, quantity: 1, bagZIndex: 0, item.EffectOverrides);
    }

    private static ItemEffectConfig[]? CloneEffectOverrides(ItemEffectConfig[]? effectOverrides) {
        if (effectOverrides is null || effectOverrides.Length == 0) {
            return null;
        }

        var copy = new ItemEffectConfig[effectOverrides.Length];
        Array.Copy(effectOverrides, copy, effectOverrides.Length);
        return copy;
    }
}

/// <summary>One equip mutation emitted by <see cref="InventoryManager"/> after an accepted equip request.</summary>
public sealed record InventoryEquippedItemChange(string Slot, InventoryItemState Item);

/// <summary>One slot cleared by <see cref="InventoryManager"/> after an accepted unequip or blocker removal.</summary>
public sealed record InventoryUnequippedItemChange(string Slot, long ItemUid);

/// <summary>Batch of authoritative item mutations produced by one inventory request.</summary>
public sealed class InventoryMutationResult {
    public List<InventoryItemState> AddedToBag { get; } = new();
    public List<long> RemovedFromBagItemUids { get; } = new();
    public List<InventoryItemState> MovedInBag { get; } = new();
    public List<InventoryEquippedItemChange> Equipped { get; } = new();
    public List<InventoryUnequippedItemChange> Unequipped { get; } = new();
}

/// <summary>Server-authoritative bag/equipment state plus equip/unequip/move rules for one <see cref="GameWorldPlayer"/>.</summary>
public sealed class InventoryManager {
    private static readonly int[] InitialEquippedItemIds = { 1, 8, 10, 13, 15, 23 };

    private const string WeaponSlot = "weapon";
    private const string ShieldSlot = "shield";
    private const string ArmorSlot = "armor";
    private const string HauberkSlot = "hauberk";
    private const string LeggingsSlot = "leggings";
    private const string BootsSlot = "boots";
    private const string HelmetSlot = "helmet";
    private const string CapeSlot = "cape";
    private const string AccessorySlot = "accessory";
    private const string RingItemType = "ring";
    private const string MiscItemType = "misc";
    private const string RingLeftSlot = "ring-left";
    private const string RingRightSlot = "ring-right";

    private static readonly HashSet<string> VisibleAppearanceSlots = new(StringComparer.Ordinal) {
        WeaponSlot,
        ShieldSlot,
        ArmorSlot,
        HauberkSlot,
        LeggingsSlot,
        BootsSlot,
        HelmetSlot,
        CapeSlot,
        AccessorySlot,
    };

    /// <summary>Adds two stack quantities without silent int wrap; result saturates at <see cref="int.MaxValue"/>.</summary>
    private static int AddStackQuantitiesSaturating(int current, int delta) {
        var sum = (long)current + delta;
        if (sum >= int.MaxValue) {
            return int.MaxValue;
        }
        if (sum <= int.MinValue) {
            return int.MinValue;
        }
        return (int)sum;
    }

    private readonly IReadOnlyDictionary<int, ItemConfig> itemsById;
    private readonly Dictionary<string, InventoryItemState> equippedItems = new(StringComparer.Ordinal);
    private readonly List<InventoryItemState> bagItems = new();

    public IReadOnlyDictionary<string, InventoryItemState> EquippedItems => equippedItems;
    public IReadOnlyList<InventoryItemState> BagItems => bagItems;

    public InventoryManager(IReadOnlyDictionary<int, ItemConfig> itemsById) {
        ArgumentNullException.ThrowIfNull(itemsById);
        this.itemsById = itemsById;
        SeedInitialLoadout();
    }

    /// <summary>True when the slot should be sent to nearby players for visible appearance sync.</summary>
    public static bool IsVisibleAppearanceSlot(string slot) {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        return VisibleAppearanceSlots.Contains(slot);
    }

    /// <summary>Loads persisted bag/equipment state, replacing the current contents entirely.</summary>
    public void LoadFromPersistence(
        PersistedInventoryItem[]? persistedBagItems,
        PersistedEquippedInventoryItem[]? persistedEquippedItems) {
        equippedItems.Clear();
        bagItems.Clear();

        if (persistedEquippedItems is not null) {
            foreach (var persisted in persistedEquippedItems) {
                if (string.IsNullOrWhiteSpace(persisted.Slot)) {
                    continue;
                }
                if (!itemsById.ContainsKey(persisted.Item.ItemId)) {
                    continue;
                }

                equippedItems[persisted.Slot] = InventoryItemState.FromPersistedEquippedItem(persisted.Item);
            }
        }

        if (persistedBagItems is not null) {
            foreach (var persisted in persistedBagItems) {
                if (!itemsById.ContainsKey(persisted.ItemId)) {
                    continue;
                }

                bagItems.Add(InventoryItemState.FromPersistedItem(persisted));
            }
        }

        bagItems.Sort((a, b) => a.BagZIndex.CompareTo(b.BagZIndex));
        NormalizeBagZIndices();
    }

    /// <summary>Serializes the current bag state for persistence.</summary>
    public PersistedInventoryItem[] CreatePersistedBagItems() {
        var rows = new PersistedInventoryItem[bagItems.Count];
        for (var i = 0; i < bagItems.Count; i++) {
            rows[i] = bagItems[i].ToPersistedItem();
        }
        return rows;
    }

    /// <summary>Serializes the current equipped state for persistence.</summary>
    public PersistedEquippedInventoryItem[] CreatePersistedEquippedItems() {
        var rows = new PersistedEquippedInventoryItem[equippedItems.Count];
        var index = 0;
        foreach (var entry in equippedItems) {
            rows[index++] = new PersistedEquippedInventoryItem(entry.Key, entry.Value.ToPersistedEquippedItem());
        }
        return rows;
    }

    /// <summary>Creates one item instance or increases an existing stack, mirroring the current client-side create behavior.</summary>
    public bool TryCreateItem(int itemId, ItemEffectConfig[]? effectOverrides, out InventoryMutationResult result) {
        result = new InventoryMutationResult();
        if (!itemsById.TryGetValue(itemId, out var itemDef)) {
            return false;
        }

        if (itemDef.Stackable == true) {
            for (var i = 0; i < bagItems.Count; i++) {
                var existing = bagItems[i];
                if (existing.ItemId != itemId) {
                    continue;
                }

                if (existing.Quantity >= int.MaxValue) {
                    continue;
                }

                existing.Quantity += 1;
                result.AddedToBag.Add(existing.Clone());
                return true;
            }
        }

        var newItem = new InventoryItemState(
            itemId,
            CreateItemUid(),
            bagX: null,
            bagY: null,
            quantity: 1,
            bagZIndex: bagItems.Count,
            effectOverrides: effectOverrides);
        bagItems.Add(newItem);
        NormalizeBagZIndices();
        result.AddedToBag.Add(newItem.Clone());
        return true;
    }

    /// <summary>
    /// Creates <paramref name="quantity"/> units of an item: one stack (merged into an existing one) when the item is stackable, otherwise one row per unit.
    /// False (nothing changed) for an unknown item or a quantity below 1. The caller bounds the quantity.
    /// </summary>
    public bool TryCreateItems(int itemId, int quantity, out InventoryMutationResult result) {
        result = new InventoryMutationResult();
        if (quantity < 1 || !itemsById.TryGetValue(itemId, out var itemDef)) {
            return false;
        }

        if (itemDef.Stackable == true) {
            for (var i = 0; i < bagItems.Count; i++) {
                var existing = bagItems[i];
                if (existing.ItemId != itemId || existing.Quantity >= int.MaxValue) {
                    continue;
                }

                existing.Quantity = AddStackQuantitiesSaturating(existing.Quantity, quantity);
                result.AddedToBag.Add(existing.Clone());
                return true;
            }
        }

        var rows = itemDef.Stackable == true ? 1 : quantity;
        for (var i = 0; i < rows; i++) {
            var newItem = new InventoryItemState(
                itemId, CreateItemUid(), bagX: null, bagY: null, quantity: itemDef.Stackable == true ? quantity : 1, bagZIndex: bagItems.Count, effectOverrides: null);
            bagItems.Add(newItem);
            result.AddedToBag.Add(newItem.Clone());
        }

        NormalizeBagZIndices();
        return true;
    }

    /// <summary>Moves a bag item to new UI coordinates when provided and always brings it to the front of the bag z-order.</summary>
    public bool TryMoveItemInBag(long itemUid, int? bagX, int? bagY, out InventoryMutationResult result) {
        result = new InventoryMutationResult();
        var index = GetBagIndex(itemUid);
        if (index < 0) {
            return false;
        }

        var item = bagItems[index];
        if (bagX.HasValue) {
            item.BagX = bagX.Value;
        }
        if (bagY.HasValue) {
            item.BagY = bagY.Value;
        }
        bagItems.RemoveAt(index);
        bagItems.Add(item);
        NormalizeBagZIndices();
        result.MovedInBag.Add(item.Clone());
        return true;
    }

    /// <summary>Equips a bag item into its target slot, unequipping blockers and slot conflicts back into the bag.</summary>
    /// <param name="playerGenderValue">0 = male, 1 = female; catalog rows with a gender restriction must match.</param>
    public bool TryEquipItem(long itemUid, string? requestedTargetSlot, int playerGenderValue, out InventoryMutationResult result) {
        result = new InventoryMutationResult();
        var bagIndex = GetBagIndex(itemUid);
        if (bagIndex < 0) {
            return false;
        }

        var newItem = bagItems[bagIndex];
        if (!itemsById.TryGetValue(newItem.ItemId, out var itemDef)) {
            return false;
        }
        if (itemDef.Gender.HasValue && itemDef.Gender.Value != playerGenderValue) {
            return false;
        }
        if (string.Equals(itemDef.ItemType, MiscItemType, StringComparison.Ordinal)) {
            return false;
        }

        var itemType = itemDef.ItemType;
        var targetSlot = ResolveTargetSlot(itemType, requestedTargetSlot);
        var blockedSlots = itemDef.BlockedItemSlots ?? Array.Empty<string>();

        var equippedSlotKeys = new List<string>(equippedItems.Keys);
        for (var i = 0; i < equippedSlotKeys.Count; i++) {
            var slot = equippedSlotKeys[i];
            var equipped = equippedItems[slot];
            if (!itemsById.TryGetValue(equipped.ItemId, out var equippedDef)) {
                continue;
            }
            if (!ContainsString(equippedDef.BlockedItemSlots, itemType)) {
                continue;
            }

            UnequipToBag(slot, result, emitUnequipped: true);
        }

        for (var i = 0; i < blockedSlots.Length; i++) {
            UnequipToBag(blockedSlots[i], result, emitUnequipped: true);
        }

        bagItems.RemoveAt(bagIndex);
        result.RemovedFromBagItemUids.Add(newItem.ItemUid);

        if (equippedItems.TryGetValue(targetSlot, out var previouslyEquipped)) {
            equippedItems.Remove(targetSlot);
            bagItems.Add(previouslyEquipped);
            result.AddedToBag.Add(previouslyEquipped.Clone());
        }

        equippedItems[targetSlot] = newItem;
        NormalizeBagZIndices();
        result.Equipped.Add(new InventoryEquippedItemChange(targetSlot, newItem.Clone()));
        return true;
    }

    /// <summary>Moves one equipped item back into the bag, optionally overriding its remembered bag position.</summary>
    public bool TryUnequipItem(string slot, long itemUid, int? bagX, int? bagY, out InventoryMutationResult result) {
        result = new InventoryMutationResult();
        if (!equippedItems.TryGetValue(slot, out var equipped) || equipped.ItemUid != itemUid) {
            return false;
        }

        equippedItems.Remove(slot);
        if (bagX.HasValue) {
            equipped.BagX = bagX.Value;
        }
        if (bagY.HasValue) {
            equipped.BagY = bagY.Value;
        }

        bagItems.Add(equipped);
        NormalizeBagZIndices();
        result.Unequipped.Add(new InventoryUnequippedItemChange(slot, equipped.ItemUid));
        result.AddedToBag.Add(equipped.Clone());
        return true;
    }

    /// <summary>Moves every equipped item whose catalog row is gender-locked and does not match <paramref name="playerGenderValue"/> back into the bag.</summary>
    /// <returns><see langword="true"/> when at least one slot was cleared.</returns>
    public bool TryUnequipAllGenderMismatchedEquipment(int playerGenderValue, out InventoryMutationResult result) {
        result = new InventoryMutationResult();
        var slots = new List<string>();
        foreach (var entry in equippedItems) {
            if (!itemsById.TryGetValue(entry.Value.ItemId, out var def)) {
                continue;
            }
            if (!def.Gender.HasValue || def.Gender.Value == playerGenderValue) {
                continue;
            }
            slots.Add(entry.Key);
        }
        if (slots.Count == 0) {
            return false;
        }
        for (var i = 0; i < slots.Count; i++) {
            UnequipToBag(slots[i], result, emitUnequipped: true);
        }
        NormalizeBagZIndices();
        return true;
    }

    /// <summary>Consumes one bagged consumable item, decrementing a stack in place or removing the item entirely when it is exhausted.</summary>
    public bool TryConsumeItem(long itemUid, out InventoryMutationResult result) {
        result = new InventoryMutationResult();
        var bagIndex = GetBagIndex(itemUid);
        if (bagIndex < 0) {
            return false;
        }

        var item = bagItems[bagIndex];
        if (!itemsById.TryGetValue(item.ItemId, out var itemDef)) {
            return false;
        }
        if (!string.Equals(itemDef.ItemType, MiscItemType, StringComparison.Ordinal) || itemDef.Consumable != true) {
            return false;
        }

        if (itemDef.Stackable == true && item.Quantity > 1) {
            item.Quantity -= 1;
            result.AddedToBag.Add(item.Clone());
            return true;
        }

        bagItems.RemoveAt(bagIndex);
        NormalizeBagZIndices();
        result.RemovedFromBagItemUids.Add(item.ItemUid);
        return true;
    }

    /// <summary>Removes one bag entry intact so it can be dropped to the ground as a single authoritative stack entry.</summary>
    public bool TryRemoveItemFromBagForGroundDrop(long itemUid, out InventoryItemState? droppedItem, out InventoryMutationResult result) {
        result = new InventoryMutationResult();
        droppedItem = null;
        var bagIndex = GetBagIndex(itemUid);
        if (bagIndex < 0) {
            return false;
        }

        var item = bagItems[bagIndex];
        bagItems.RemoveAt(bagIndex);
        NormalizeBagZIndices();
        result.RemovedFromBagItemUids.Add(item.ItemUid);
        droppedItem = new InventoryItemState(
            item.ItemId,
            item.ItemUid,
            bagX: null,
            bagY: null,
            quantity: item.Quantity,
            bagZIndex: 0,
            effectOverrides: item.EffectOverrides);
        return true;
    }

    /// <summary>Total quantity of an item id across the bag (equipped items do not count).</summary>
    public int CountItem(int itemId) {
        long total = 0;
        for (var i = 0; i < bagItems.Count; i++) {
            if (bagItems[i].ItemId == itemId) {
                total += bagItems[i].Quantity;
            }
        }

        return (int)Math.Min(int.MaxValue, total);
    }

    /// <summary>
    /// Removes <paramref name="quantity"/> of an item id from the bag, across stacks: all or nothing.
    /// A stack that still has some left is reported in <see cref="InventoryMutationResult.AddedToBag"/> with its new quantity, an exhausted one in <see cref="InventoryMutationResult.RemovedFromBagItemUids"/>.
    /// </summary>
    public bool TryRemoveItemsById(int itemId, int quantity, out InventoryMutationResult result) {
        result = new InventoryMutationResult();
        if (quantity <= 0 || CountItem(itemId) < quantity) {
            return false;
        }

        var left = quantity;
        for (var i = bagItems.Count - 1; i >= 0 && left > 0; i--) {
            var item = bagItems[i];
            if (item.ItemId != itemId) {
                continue;
            }

            if (item.Quantity > left) {
                item.Quantity -= left;
                result.AddedToBag.Add(item.Clone());
                left = 0;
            } else {
                left -= item.Quantity;
                bagItems.RemoveAt(i);
                result.RemovedFromBagItemUids.Add(item.ItemUid);
            }
        }

        NormalizeBagZIndices();
        return true;
    }

    /// <summary>
    /// Whether the bag would hold at most <paramref name="maxRows"/> rows after taking <paramref name="removals"/> out and putting <paramref name="additions"/> in, in that order,
    /// with the same stacking rules as <see cref="TryRemoveItemsById"/> and <see cref="TryCreateItems"/> (a stackable item shares one row, anything else takes one row per unit).
    /// False when a removal is not available or an added item is unknown. Changes nothing; equipped items do not count.
    /// </summary>
    public bool CanHold(IEnumerable<(int ItemId, int Quantity)> removals, IEnumerable<(int ItemId, int Quantity)> additions, int maxRows) {
        ArgumentNullException.ThrowIfNull(removals);
        ArgumentNullException.ThrowIfNull(additions);
        var rows = bagItems.Select(item => (item.ItemId, Quantity: (long)item.Quantity)).ToList();
        foreach (var (itemId, quantity) in removals) {
            if (quantity <= 0 || rows.Where(r => r.ItemId == itemId).Sum(r => r.Quantity) < quantity) {
                return false;
            }

            var left = (long)quantity;
            for (var i = rows.Count - 1; i >= 0 && left > 0; i--) {
                if (rows[i].ItemId != itemId) {
                    continue;
                }

                if (rows[i].Quantity > left) {
                    rows[i] = (itemId, rows[i].Quantity - left);
                    left = 0;
                } else {
                    left -= rows[i].Quantity;
                    rows.RemoveAt(i);
                }
            }
        }

        foreach (var (itemId, quantity) in additions) {
            if (quantity < 1 || !itemsById.TryGetValue(itemId, out var itemDef)) {
                return false;
            }

            if (itemDef.Stackable == true) {
                if (!rows.Any(r => r.ItemId == itemId)) {
                    rows.Add((itemId, quantity));
                }
            } else {
                for (var i = 0; i < quantity; i++) {
                    rows.Add((itemId, 1));
                }
            }

            if (rows.Count > maxRows) {
                return false;
            }
        }

        return rows.Count <= maxRows;
    }

    /// <summary>Adds a ground item back into the bag, preserving quantity/effects and merging with existing stackable rows by item id.</summary>
    public bool TryAddGroundItemToBag(GroundItemState groundItem, out InventoryMutationResult result) {
        ArgumentNullException.ThrowIfNull(groundItem);
        result = new InventoryMutationResult();
        if (!itemsById.TryGetValue(groundItem.ItemId, out var itemDef)) {
            return false;
        }

        if (itemDef.Stackable == true) {
            for (var i = 0; i < bagItems.Count; i++) {
                var existing = bagItems[i];
                if (existing.ItemId != groundItem.ItemId) {
                    continue;
                }

                if (existing.Quantity >= int.MaxValue) {
                    continue;
                }

                existing.Quantity = AddStackQuantitiesSaturating(existing.Quantity, groundItem.Quantity);
                result.AddedToBag.Add(existing.Clone());
                return true;
            }
        }

        var newItem = new InventoryItemState(
            groundItem.ItemId,
            groundItem.ItemUid,
            bagX: null,
            bagY: null,
            quantity: groundItem.Quantity,
            bagZIndex: bagItems.Count,
            effectOverrides: groundItem.EffectOverrides);
        bagItems.Add(newItem);
        NormalizeBagZIndices();
        result.AddedToBag.Add(newItem.Clone());
        return true;
    }

    private void SeedInitialLoadout() {
        for (var i = 0; i < InitialEquippedItemIds.Length; i++) {
            var itemId = InitialEquippedItemIds[i];
            if (!itemsById.TryGetValue(itemId, out var itemDef)) {
                continue;
            }

            var slot = itemDef.ItemType;
            equippedItems[slot] = new InventoryItemState(
                itemId,
                CreateItemUid(),
                bagX: null,
                bagY: null,
                quantity: 1,
                bagZIndex: 0,
                effectOverrides: null);
        }
    }

    private void UnequipToBag(string slot, InventoryMutationResult result, bool emitUnequipped) {
        if (!equippedItems.TryGetValue(slot, out var equipped)) {
            return;
        }

        equippedItems.Remove(slot);
        bagItems.Add(equipped);
        if (emitUnequipped) {
            result.Unequipped.Add(new InventoryUnequippedItemChange(slot, equipped.ItemUid));
        }
        result.AddedToBag.Add(equipped.Clone());
    }

    internal static long CreateItemUid() {
        return BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
    }

    private int GetBagIndex(long itemUid) {
        for (var i = 0; i < bagItems.Count; i++) {
            if (bagItems[i].ItemUid == itemUid) {
                return i;
            }
        }
        return -1;
    }

    private string ResolveTargetSlot(string itemType, string? requestedTargetSlot) {
        if (!string.Equals(itemType, RingItemType, StringComparison.Ordinal)) {
            return itemType;
        }

        if (string.Equals(requestedTargetSlot, RingLeftSlot, StringComparison.Ordinal) ||
            string.Equals(requestedTargetSlot, RingRightSlot, StringComparison.Ordinal)) {
            return requestedTargetSlot!;
        }

        if (!equippedItems.ContainsKey(RingLeftSlot)) {
            return RingLeftSlot;
        }
        if (!equippedItems.ContainsKey(RingRightSlot)) {
            return RingRightSlot;
        }
        return RingLeftSlot;
    }

    private static bool ContainsString(string[]? values, string value) {
        if (values is null || values.Length == 0) {
            return false;
        }

        for (var i = 0; i < values.Length; i++) {
            if (string.Equals(values[i], value, StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }

    private void NormalizeBagZIndices() {
        for (var i = 0; i < bagItems.Count; i++) {
            bagItems[i].BagZIndex = i;
        }
    }
}

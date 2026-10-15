using Server.Utils;
using Server.World.Game;

namespace Server.Nocthemar.Net;

/// <summary>Sends the bag part of an inventory mutation to its owner (removals first, then additions). NOCTHEMAR only touches the bag, so nobody else needs to be told.</summary>
internal static class NocInventorySync {
    public static void SendBagDelta(GameWorldPlayer player, InventoryMutationResult result) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(result);
        foreach (var removedItemUid in result.RemovedFromBagItemUids) {
            NetworkManager.SendToPlayer(player, NetworkManager.CreateItemRemovedFromBag(removedItemUid));
        }

        foreach (var addedItem in result.AddedToBag) {
            NetworkManager.SendToPlayer(player, NetworkManager.CreateItemAddedToBag(addedItem));
        }
    }
}

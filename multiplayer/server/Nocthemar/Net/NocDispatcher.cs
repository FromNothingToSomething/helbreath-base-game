using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Utils;
using Server.World.Game;

namespace Server.Nocthemar.Net;

/// <summary>
/// Handles the client payloads that belong to NOCTHEMAR (<c>ClientMessage</c> oneof numbers 100 and up), so <c>GameWorld.HandleClientPacket</c> only needs one case label per payload.
/// Payloads it does not own are not touched: <see cref="Handle"/> returns false and the world's own switch (including its default) stays in charge.
/// Used from the world thread only.
/// </summary>
public sealed class NocDispatcher {
    private readonly bool debugEnabled;
    private readonly Action<string> log;
    private readonly Func<GameWorldPlayer, bool> soulForgeInRange;
    private readonly Func<GameWorldPlayer, int, bool> factionMasterInRange;
    private readonly Action<GameWorldPlayer> factionChanged;
    private readonly HashSet<(long PlayerId, string Request)> playersAlreadyRefused = new();

    /// <summary>Largest quantity one debug item request may create.</summary>
    public const int MaxDebugQuantity = 100;

    /// <param name="soulForgeInRange">Tells whether a Soul Forge NPC of the player's world is within <see cref="SoulForge.RangeCells"/> of the player; none given = there is no forge, consume and fuse requests are refused.</param>
    /// <param name="factionMasterInRange">Tells whether a master of the given faction stands within range of the player; none given = no master, join requests are refused.</param>
    /// <param name="factionChanged">Called after a player joined a faction (the world tells the players in range).</param>
    public NocDispatcher(
        bool debugEnabled,
        Action<string>? log = null,
        Func<GameWorldPlayer, bool>? soulForgeInRange = null,
        Func<GameWorldPlayer, int, bool>? factionMasterInRange = null,
        Action<GameWorldPlayer>? factionChanged = null) {
        this.debugEnabled = debugEnabled;
        this.log = log ?? Console.Error.WriteLine;
        this.soulForgeInRange = soulForgeInRange ?? (_ => false);
        this.factionMasterInRange = factionMasterInRange ?? ((_, _) => false);
        this.factionChanged = factionChanged ?? (_ => { });
    }

    /// <summary>Payload cases this dispatcher handles; every NOCTHEMAR payload (field number 100 or more) must be listed here.</summary>
    public static bool Owns(ClientMessage.PayloadOneofCase payload) =>
        payload is ClientMessage.PayloadOneofCase.NocGrantXpRequest
            or ClientMessage.PayloadOneofCase.NocChooseClassRequest
            or ClientMessage.PayloadOneofCase.NocLearnSkillRequest
            or ClientMessage.PayloadOneofCase.NocRespecRequest
            or ClientMessage.PayloadOneofCase.NocCreateItemRequest
            or ClientMessage.PayloadOneofCase.NocForgeConsumeRequest
            or ClientMessage.PayloadOneofCase.NocForgeFuseRequest
            or ClientMessage.PayloadOneofCase.NocMimicEquipRequest
            or ClientMessage.PayloadOneofCase.NocFactionJoinRequest
            or ClientMessage.PayloadOneofCase.NocReputationAddRequest;

    /// <summary>True when the payload belongs to NOCTHEMAR (executed or refused), false when it is not ours.</summary>
    public bool Handle(GameWorldPlayer player, ClientMessage message) {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(message);
        switch (message.PayloadCase) {
            case ClientMessage.PayloadOneofCase.NocGrantXpRequest:
                HandleGrantXp(player, message.NocGrantXpRequest);
                return true;
            case ClientMessage.PayloadOneofCase.NocChooseClassRequest:
                Answer(player, player.TryChooseClass(message.NocChooseClassRequest.ClassId));
                return true;
            case ClientMessage.PayloadOneofCase.NocLearnSkillRequest:
                Answer(player, player.TryLearnSkill(message.NocLearnSkillRequest.SkillId));
                return true;
            case ClientMessage.PayloadOneofCase.NocRespecRequest:
                HandleRespec(player);
                return true;
            case ClientMessage.PayloadOneofCase.NocCreateItemRequest:
                HandleCreateItem(player, message.NocCreateItemRequest);
                return true;
            case ClientMessage.PayloadOneofCase.NocForgeConsumeRequest:
                HandleForge(player, (GameWorldPlayer p, out InventoryMutationResult? inventory) => p.ForgeConsume(message.NocForgeConsumeRequest.AbilityId, out inventory));
                return true;
            case ClientMessage.PayloadOneofCase.NocForgeFuseRequest:
                HandleForge(player, (GameWorldPlayer p, out InventoryMutationResult? inventory) => p.ForgeFuse(message.NocForgeFuseRequest.AbilityA, message.NocForgeFuseRequest.AbilityB, out inventory));
                return true;
            case ClientMessage.PayloadOneofCase.NocMimicEquipRequest:
                Answer(player, message.NocMimicEquipRequest.Equipped
                    ? player.TryEquipMimic(message.NocMimicEquipRequest.AbilityId)
                    : player.TryUnequipMimic(message.NocMimicEquipRequest.AbilityId));
                return true;
            case ClientMessage.PayloadOneofCase.NocFactionJoinRequest:
                HandleFactionJoin(player, message.NocFactionJoinRequest);
                return true;
            case ClientMessage.PayloadOneofCase.NocReputationAddRequest:
                HandleReputationAdd(player, message.NocReputationAddRequest);
                return true;
            default:
                return false;
        }
    }

    /// <summary>A refused class or skill request is answered to the sender only; an accepted one is answered by the next <see cref="NocStateUpdated"/> of the tick.</summary>
    private static void Answer(GameWorldPlayer player, string? refusal) {
        if (refusal is not null) {
            NetworkManager.SendToPlayer(player, NetworkManager.CreateNocActionRefused(refusal));
        }
    }

    /// <summary>Debug only: forgets the sender's skills. Refused (ignored, logged once per player) unless <c>nocthemar.debug</c> is true.</summary>
    private void HandleRespec(GameWorldPlayer player) {
        if (!debugEnabled) {
            LogDebugRefusal(player, nameof(NocRespecRequest));
            return;
        }

        player.Respec();
    }

    /// <summary>Debug only: grants XP to the sender. Refused (ignored, logged once per player) unless <c>nocthemar.debug</c> is true; non-positive amounts are ignored.</summary>
    private void HandleGrantXp(GameWorldPlayer player, NocGrantXpRequest request) {
        if (!debugEnabled) {
            LogDebugRefusal(player, nameof(NocGrantXpRequest));
            return;
        }

        if (request.Amount <= 0) {
            return;
        }

        player.GrantXp(request.Amount);
    }

    /// <summary>Debug only: puts <see cref="NocCreateItemRequest.Quantity"/> units of an item in the sender's bag. Refused (ignored, logged once per player) unless <c>nocthemar.debug</c> is true; an unknown item or a quantity outside [1, <see cref="MaxDebugQuantity"/>] is answered with <see cref="NocActionRefused"/>.</summary>
    private void HandleCreateItem(GameWorldPlayer player, NocCreateItemRequest request) {
        if (!debugEnabled) {
            LogDebugRefusal(player, nameof(NocCreateItemRequest));
            return;
        }

        if (request.Quantity < 1 || request.Quantity > MaxDebugQuantity) {
            Answer(player, $"Quantity must be between 1 and {MaxDebugQuantity}.");
            return;
        }

        if (!player.InventoryManager.TryCreateItems(request.ItemId, request.Quantity, out var result)) {
            Answer(player, "Unknown item.");
            return;
        }

        NocInventorySync.SendBagDelta(player, result);
    }

    /// <summary>Join a faction: refused unless the player is alive and a master of that faction is in range, then by <see cref="FactionService"/>'s rules. Accepted: the world is told so it can notify the players in range.</summary>
    private void HandleFactionJoin(GameWorldPlayer player, NocFactionJoinRequest request) {
        if (player.IsDead) {
            Answer(player, "You are dead.");
            return;
        }

        if (!factionMasterInRange(player, request.FactionId)) {
            Answer(player, FactionService.NoMasterInRange);
            return;
        }

        var refusal = player.TryJoinFaction(request.FactionId);
        Answer(player, refusal);
        if (refusal is null) {
            factionChanged(player);
        }
    }

    /// <summary>Debug only: adds to the sender's reputation. Refused (ignored, logged once per player) unless <c>nocthemar.debug</c> is true; an unknown faction is answered with <see cref="NocActionRefused"/>.</summary>
    private void HandleReputationAdd(GameWorldPlayer player, NocReputationAddRequest request) {
        if (!debugEnabled) {
            LogDebugRefusal(player, nameof(NocReputationAddRequest));
            return;
        }

        if (!player.TryAdjustReputation(request.FactionId, request.Delta)) {
            Answer(player, FactionService.UnknownFaction);
        }
    }

    private delegate string? ForgeAction(GameWorldPlayer player, out InventoryMutationResult? inventory);

    /// <summary>Consume or fuse at the Soul Forge: refused unless the player is alive and a forge is in range. Accepted: the bag delta is sent now, the new abilities with the next <see cref="NocStateUpdated"/>.</summary>
    private void HandleForge(GameWorldPlayer player, ForgeAction action) {
        if (player.IsDead) {
            Answer(player, "You are dead.");
            return;
        }

        if (!soulForgeInRange(player)) {
            Answer(player, SoulForge.NoForgeInRange);
            return;
        }

        var refusal = action(player, out var inventory);
        Answer(player, refusal);
        if (refusal is null && inventory is not null) {
            NocInventorySync.SendBagDelta(player, inventory);
        }
    }

    private void LogDebugRefusal(GameWorldPlayer player, string request) {
        if (playersAlreadyRefused.Add((player.PlayerId, request))) {
            log($"[Noc] Refused {request} from player {player.PlayerId}: nocthemar.debug is false.");
        }
    }
}

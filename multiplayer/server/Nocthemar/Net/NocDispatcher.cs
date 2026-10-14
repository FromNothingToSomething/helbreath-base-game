using Mmorpg.Network;
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
    private readonly HashSet<long> playersAlreadyRefused = new();

    public NocDispatcher(bool debugEnabled, Action<string>? log = null) {
        this.debugEnabled = debugEnabled;
        this.log = log ?? Console.Error.WriteLine;
    }

    /// <summary>Payload cases this dispatcher handles; every NOCTHEMAR payload (field number 100 or more) must be listed here.</summary>
    public static bool Owns(ClientMessage.PayloadOneofCase payload) =>
        payload is ClientMessage.PayloadOneofCase.NocGrantXpRequest
            or ClientMessage.PayloadOneofCase.NocChooseClassRequest
            or ClientMessage.PayloadOneofCase.NocLearnSkillRequest
            or ClientMessage.PayloadOneofCase.NocRespecRequest;

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
            if (playersAlreadyRefused.Add(player.PlayerId)) {
                log($"[Noc] Refused NocRespecRequest from player {player.PlayerId}: nocthemar.debug is false.");
            }

            return;
        }

        player.Respec();
    }

    /// <summary>Debug only: grants XP to the sender. Refused (ignored, logged once per player) unless <c>nocthemar.debug</c> is true; non-positive amounts are ignored.</summary>
    private void HandleGrantXp(GameWorldPlayer player, NocGrantXpRequest request) {
        if (!debugEnabled) {
            if (playersAlreadyRefused.Add(player.PlayerId)) {
                log($"[Noc] Refused NocGrantXpRequest from player {player.PlayerId}: nocthemar.debug is false.");
            }

            return;
        }

        if (request.Amount <= 0) {
            return;
        }

        player.GrantXp(request.Amount);
    }
}

using Server.World.Game;

namespace Server.Nocthemar.Core;

/// <summary>Operator-facing log lines about character state.</summary>
public static class CharacterLog {
    /// <summary>One line per character written at shutdown: id, level, XP and mana, so progress loss is visible in the console.</summary>
    public static string FormatShutdownLine(string networkId, PlayerPersistenceState state, bool saved) {
        ArgumentNullException.ThrowIfNull(state);
        var noc = state.Noc is null
            ? "noc=none"
            : FormattableString.Invariant($"level={state.Noc.Level} xp={state.Noc.Xp} mana={state.Noc.Mana}");
        return $"[Server] Shutdown save: id={networkId} {noc} saved={(saved ? "yes" : "no")}";
    }
}

using Server.World.Game;

namespace Server.Nocthemar.Core;

/// <summary>Operator-facing log lines about character state.</summary>
public static class CharacterLog {
    /// <summary>Warning for a save whose race id is not in Races.json: the session uses the default race, the file is not touched, and the original id is here for the administrator.</summary>
    public static string FormatUnknownRaceWarning(string networkId, int originalRaceId, int fallbackRaceId) =>
        $"[Server] WARN character '{networkId}': RaceId {originalRaceId} is not in Races.json; using race {fallbackRaceId} for this session. " +
        "The save file is unchanged until the next regular save, which will store the fallback race: restore the original RaceId by hand if it was valid.";

    /// <summary>One line per character written at shutdown: id, level, XP and mana, so progress loss is visible in the console.</summary>
    public static string FormatShutdownLine(string networkId, PlayerPersistenceState state, bool saved) {
        ArgumentNullException.ThrowIfNull(state);
        var noc = state.Noc is null
            ? "noc=none"
            : FormattableString.Invariant($"level={state.Noc.Level} xp={state.Noc.Xp} mana={state.Noc.Mana}");
        return $"[Server] Shutdown save: id={networkId} {noc} saved={(saved ? "yes" : "no")}";
    }
}

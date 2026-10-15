namespace Server.Nocthemar.Core;

/// <summary>Who may hurt whom between players; chosen once per server with <c>nocthemar.pvpMode</c> in <c>Settings.json</c>.</summary>
public enum PvpPolicy : byte {
    /// <summary>The Helbreath rules as they were: any player may hurt any other (default).</summary>
    Legacy = 0,

    /// <summary>Members of the same faction cannot hurt each other; everyone else (unaffiliated players, other and rival factions) can.</summary>
    Factions = 1,
}

public static class PvpRules {
    public const string LegacyMode = "legacy";
    public const string FactionsMode = "factions";

    /// <summary>Reads the <c>pvpMode</c> setting: absent or "legacy" = <see cref="PvpPolicy.Legacy"/>, "factions" (any case) = <see cref="PvpPolicy.Factions"/>; anything else is a configuration error.</summary>
    public static PvpPolicy Parse(string? mode) {
        if (mode is null || string.Equals(mode, LegacyMode, StringComparison.OrdinalIgnoreCase)) {
            return PvpPolicy.Legacy;
        }

        if (string.Equals(mode, FactionsMode, StringComparison.OrdinalIgnoreCase)) {
            return PvpPolicy.Factions;
        }

        throw new InvalidOperationException($"Unknown nocthemar.pvpMode '{mode}': expected \"{LegacyMode}\" or \"{FactionsMode}\".");
    }

    /// <summary>True when an attacker of <paramref name="attackerFaction"/> may hurt a target of <paramref name="targetFaction"/> (null = no faction).</summary>
    public static bool CanHarm(PvpPolicy policy, int? attackerFaction, int? targetFaction) =>
        policy != PvpPolicy.Factions || attackerFaction is null || attackerFaction != targetFaction;
}

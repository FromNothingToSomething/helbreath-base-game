namespace Server.Nocthemar.Data;

/// <summary>NOCTHEMAR switches; JSON <c>nocthemar</c> in <c>Settings.json</c> (optional, everything off when absent).</summary>
/// <param name="Debug">Allows debug-only client requests such as <c>NocGrantXpRequest</c>. Keep false outside development.</param>
/// <param name="PvpMode">Player-versus-player rules: absent or "legacy" (anyone may hurt anyone, the Helbreath behavior) or "factions" (members of one faction cannot hurt each other).</param>
public sealed record NocthemarSettings(bool Debug = false, string? PvpMode = null) {
    /// <summary>The policy <see cref="PvpMode"/> names; throws for an unknown mode.</summary>
    public Server.Nocthemar.Core.PvpPolicy ResolvePvpPolicy() => Server.Nocthemar.Core.PvpRules.Parse(PvpMode);
}

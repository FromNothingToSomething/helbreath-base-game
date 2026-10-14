namespace Server.Nocthemar.Data;

/// <summary>NOCTHEMAR switches; JSON <c>nocthemar</c> in <c>Settings.json</c> (optional, everything off when absent).</summary>
/// <param name="Debug">Allows debug-only client requests such as <c>NocGrantXpRequest</c>. Keep false outside development.</param>
public sealed record NocthemarSettings(bool Debug = false);

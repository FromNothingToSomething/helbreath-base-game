namespace Server.Nocthemar.Core;

/// <summary>
/// NOCTHEMAR per-character persisted block, nested in <c>PlayerPersistenceState.Noc</c> (optional).
/// Later-module fields are nullable and reserved now so saves stay forward compatible; unknown JSON members are ignored on load.
/// </summary>
/// <param name="V">Version of this block's own layout.</param>
/// <param name="Level">Character level, 1-based.</param>
/// <param name="Xp">XP accumulated toward the next level (resets on level-up).</param>
/// <param name="Mana">Current mana.</param>
/// <param name="RaceId">Reserved (module 2).</param>
/// <param name="ClassId">Reserved (module 4).</param>
/// <param name="FactionId">Reserved (module 7).</param>
public sealed record NocState(
    int V = NocState.CurrentV,
    int Level = 1,
    long Xp = 0,
    int Mana = 0,
    int? RaceId = null,
    int? ClassId = null,
    int? FactionId = null) {

    public const int CurrentV = 1;

    /// <summary>Fresh level-1 character with mana at <paramref name="maxMana"/>.</summary>
    public static NocState CreateDefault(int maxMana) => new(CurrentV, 1, 0, Math.Max(0, maxMana));
}

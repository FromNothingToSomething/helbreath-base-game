namespace Server.Nocthemar.Core;

/// <summary>
/// NOCTHEMAR per-character persisted block, nested in <c>PlayerPersistenceState.Noc</c> (optional).
/// Later-module fields are nullable and reserved now so saves stay forward compatible; unknown JSON members are ignored on load.
/// </summary>
/// <param name="V">Version of this block's own layout.</param>
/// <param name="Level">Character level, 1-based.</param>
/// <param name="Xp">XP accumulated toward the next level (resets on level-up).</param>
/// <param name="Mana">Current mana.</param>
/// <param name="RaceId">Id of the race in <c>Races.json</c>; set from schema 2 on (null only in older saves, before migration). An id that is not in <c>Races.json</c> is replaced by the default race when the save is loaded, never refused.</param>
/// <param name="ClassId">Id of the class in <c>Classes.json</c>; null = no class chosen yet. Chosen once, never changed (except by the debug respec). An id that is not listed is dropped with a warning when the save is loaded.</param>
/// <param name="FactionId">Reserved (module 7).</param>
/// <param name="Skills">Ids of the learned class skills (<c>Skills.json</c>), ascending; null (not an empty list) when none, so a character without skills has the same value as before skills existed. Unknown, unlearnable or rule-breaking ids are dropped when the save is loaded.</param>
/// <param name="Kills">Monsters killed by the character (saturating); counter for skills that scale with kills.</param>
/// <param name="Mimics">Learned mimicry abilities (<c>Mimic.json</c>) with their experience, ascending by id; null (not an empty list) when none. Unknown, class-forbidden or duplicate ids are dropped when the save is loaded.</param>
/// <param name="MimicEquipped">Ids of the equipped mimicry abilities, in bar order (position = slot); null when none. Only learned abilities fit, up to the number of slots at the character's level.</param>
public sealed record NocState(
    int V = NocState.CurrentV,
    int Level = 1,
    long Xp = 0,
    int Mana = 0,
    int? RaceId = null,
    int? ClassId = null,
    int? FactionId = null,
    int[]? Skills = null,
    int Kills = 0,
    MimicEntry[]? Mimics = null,
    int[]? MimicEquipped = null) {

    /// <summary>Layout 2 adds <see cref="Skills"/> and <see cref="Kills"/>; layout 3 adds <see cref="Mimics"/> and <see cref="MimicEquipped"/>. An older block reads as "none of these".</summary>
    public const int CurrentV = 3;

    /// <summary>Race given to characters saved before races existed (migration 1 -> 2): the first race, Valdorniens.</summary>
    public const int LegacyRaceId = 0;

    /// <summary>Fresh level-1 character with mana at <paramref name="maxMana"/>.</summary>
    public static NocState CreateDefault(int maxMana) => new(CurrentV, 1, 0, Math.Max(0, maxMana));
}

/// <summary>A learned mimicry ability: its id in <c>Mimic.json</c> and the experience gained by casting it (the level is derived from the xp table).</summary>
public sealed record MimicEntry(int Id, long Xp = 0);

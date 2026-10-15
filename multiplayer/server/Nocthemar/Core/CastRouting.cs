namespace Server.Nocthemar.Core;

/// <summary>Which resolver a cast request id goes to; the ranges are disjoint (see <see cref="MimicIds"/> and <see cref="SkillIds"/>).</summary>
public enum CastRoute : byte {
    /// <summary>Legacy <c>Spells.json</c> spell (id below 1000).</summary>
    Spell = 0,

    /// <summary>Class skill (1000 and above, below 2000).</summary>
    Skill = 1,

    /// <summary>Mimicry ability (2000 and above): tested before the skill range, which it would otherwise match.</summary>
    Mimic = 2,
}

public static class CastRouting {
    public static CastRoute Of(int id) => id >= MimicIds.Base ? CastRoute.Mimic : id >= SkillIds.Base ? CastRoute.Skill : CastRoute.Spell;
}

namespace Server.Nocthemar.Core;

/// <summary>Owner families of <see cref="StatModifier.SourceId"/>; the category lives in the high 16 bits.</summary>
public enum ModifierSourceCategory : byte {
    None = 0,
    Race = 1,
    Class = 2,
    Faction = 3,
    Item = 4,
    Buff = 5,
    Skill = 6,
}

/// <summary>
/// Builds <see cref="StatModifier.SourceId"/> as <c>(category &lt;&lt; 16) | id</c> so that a race, a class passive or a buff with the same numeric id never collide,
/// and one <see cref="StatPipeline.RemoveSource"/> call removes everything a given owner added. Category 0 is left to ad-hoc ids (tests, tools).
/// </summary>
public static class ModifierSources {
    public const int MaxId = ushort.MaxValue;

    public static int Race(int id) => Make(ModifierSourceCategory.Race, id);

    public static int Class(int id) => Make(ModifierSourceCategory.Class, id);

    public static int Faction(int id) => Make(ModifierSourceCategory.Faction, id);

    public static int Item(int id) => Make(ModifierSourceCategory.Item, id);

    public static int Buff(int id) => Make(ModifierSourceCategory.Buff, id);

    /// <summary>Owner of the modifiers of one learned class skill (<paramref name="skillId"/> = <see cref="SkillIds"/> value).</summary>
    public static int Skill(int skillId) => Make(ModifierSourceCategory.Skill, skillId);

    public static int Make(ModifierSourceCategory category, int id) {
        if (id < 0 || id > MaxId) {
            throw new ArgumentOutOfRangeException(nameof(id), id, $"Source id must be in [0, {MaxId}].");
        }

        return ((int)category << 16) | id;
    }

    public static ModifierSourceCategory CategoryOf(int sourceId) => (ModifierSourceCategory)(sourceId >> 16);

    public static int IdOf(int sourceId) => sourceId & MaxId;
}

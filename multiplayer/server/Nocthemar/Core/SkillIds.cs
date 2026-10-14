namespace Server.Nocthemar.Core;

/// <summary>
/// Class skill id scheme: <c>1000 + classId * 100 + branch * 10 + slot</c> (class 0-9, branch 0-2, slot 0-7), so ids are stable, decodable and below <see cref="ModifierSources.MaxId"/>.
/// Ids under <see cref="Base"/> are the legacy <c>Spells.json</c> spells and are never class skills. An active class skill is cast through the spell id equal to its skill id.
/// </summary>
public static class SkillIds {
    public const int Base = 1000;
    public const int MaxClasses = 10;
    public const int BranchesPerClass = 3;
    public const int SlotsPerBranch = 8;
    public const int SkillsPerClass = BranchesPerClass * SlotsPerBranch;

    /// <summary>Character level required by each slot of a branch (slot 7 is the ultimate).</summary>
    private static readonly int[] SlotLevelTable = [1, 3, 6, 10, 14, 18, 24, 30];

    public static IReadOnlyList<int> SlotLevels => SlotLevelTable;

    public static int Make(int classId, int branch, int slot) {
        if (!IsValidPosition(classId, branch, slot)) {
            throw new ArgumentOutOfRangeException(nameof(slot), $"Invalid skill position class={classId} branch={branch} slot={slot}.");
        }

        return Base + classId * 100 + branch * 10 + slot;
    }

    public static bool IsValidPosition(int classId, int branch, int slot) =>
        classId is >= 0 and < MaxClasses && branch is >= 0 and < BranchesPerClass && slot is >= 0 and < SlotsPerBranch;

    /// <summary>False for legacy spell ids and for ids that do not decode to a valid position.</summary>
    public static bool TryDecompose(int skillId, out int classId, out int branch, out int slot) {
        classId = branch = slot = 0;
        var offset = skillId - Base;
        if (offset < 0) {
            return false;
        }

        classId = offset / 100;
        branch = offset % 100 / 10;
        slot = offset % 10;
        return IsValidPosition(classId, branch, slot) && offset % 100 % 10 < SlotsPerBranch && offset % 100 < BranchesPerClass * 10;
    }

    public static bool IsClassSkill(int id) => TryDecompose(id, out _, out _, out _);

    public static int RequiredLevel(int slot) => SlotLevelTable[slot];

    /// <summary>Slots 0, 2, 4, 6 are passives, 1, 3, 5 actives and 7 the ultimate.</summary>
    public static bool IsPassiveSlot(int slot) => slot is 0 or 2 or 4 or 6;

    public static bool IsUltimateSlot(int slot) => slot == SlotsPerBranch - 1;
}

using Server.Nocthemar.Data;

namespace Server.Nocthemar.Core;

/// <summary>Outcome of a class / skill action: the new state, or a short player-facing refusal (the state is then the unchanged input).</summary>
public readonly record struct SkillActionResult(NocState State, string? Refusal) {
    public bool Ok => Refusal is null;
}

/// <summary>
/// Pure class and skill-tree rules over <see cref="NocState"/>; holds no per-player state, so one instance is shared by every world.
/// Rules: the class is chosen once; 1 point per level from level 2 (<see cref="ProgressionConfig.SkillPointsAt"/>); a skill costs its <see cref="SkillDefinition.Cost"/>;
/// it needs its level, a ready catalog entry, the nearest lower ready skill of its branch, and at most two branches may hold skills (hybrid).
/// </summary>
public sealed class SkillService {
    public const int MaxBranchesWithSkills = 2;

    private readonly ClassConfig classes;
    private readonly SkillConfig skills;
    private readonly ProgressionConfig progression;

    public SkillService(ClassConfig classes, SkillConfig skills, ProgressionConfig progression) {
        this.classes = classes ?? throw new ArgumentNullException(nameof(classes));
        this.skills = skills ?? throw new ArgumentNullException(nameof(skills));
        this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
    }

    public ClassConfig Classes => classes;

    public SkillConfig Skills => skills;

    public bool IsLearned(NocState state, int skillId) => state.Skills is { } learned && Array.IndexOf(learned, skillId) >= 0;

    /// <summary>Skill points spent by the learned skills (unknown ids cost nothing).</summary>
    public int PointsSpent(NocState state) {
        var spent = 0;
        foreach (var id in state.Skills ?? []) {
            if (skills.TryGet(id, out var skill)) {
                spent += skill.Cost;
            }
        }

        return spent;
    }

    /// <summary>Points earned at the state's level minus the points spent; never negative.</summary>
    public int PointsAvailable(NocState state) => Math.Max(0, progression.SkillPointsAt(state.Level) - PointsSpent(state));

    /// <summary>The learned skills as catalog entries, in id order; ids missing from the catalog are skipped.</summary>
    public IReadOnlyList<SkillDefinition> Learned(NocState state) {
        var result = new List<SkillDefinition>();
        foreach (var id in state.Skills ?? []) {
            if (skills.TryGet(id, out var skill)) {
                result.Add(skill);
            }
        }

        return result;
    }

    /// <summary>The skill that must be learned before <paramref name="skill"/>: the nearest lower slot of its branch that is ready (catalog-only skills are skipped); null when none.</summary>
    public SkillDefinition? PrerequisiteOf(SkillDefinition skill) {
        ArgumentNullException.ThrowIfNull(skill);
        for (var slot = skill.Slot - 1; slot >= 0; slot--) {
            if (skills.TryGet(SkillIds.Make(skill.ClassId, skill.Branch, slot), out var lower) && lower.IsReady) {
                return lower;
            }
        }

        return null;
    }

    /// <summary>Picks the class. Only possible once: a character that already has one keeps it.</summary>
    public SkillActionResult ChooseClass(NocState state, int classId) {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ClassId is not null) {
            return new(state, "A class has already been chosen.");
        }

        if (!classes.Contains(classId)) {
            return new(state, "Unknown class.");
        }

        return new(state with { ClassId = classId }, null);
    }

    public SkillActionResult Learn(NocState state, int skillId) {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ClassId is not int classId) {
            return new(state, "Choose a class first.");
        }

        if (!skills.TryGet(skillId, out var skill)) {
            return new(state, "Unknown skill.");
        }

        if (skill.ClassId != classId) {
            return new(state, "This skill belongs to another class.");
        }

        if (!skill.IsReady) {
            return new(state, "This skill is not available yet.");
        }

        if (IsLearned(state, skillId)) {
            return new(state, "Skill already learned.");
        }

        if (state.Level < skill.Level) {
            return new(state, $"Requires level {skill.Level}.");
        }

        var prerequisite = PrerequisiteOf(skill);
        if (prerequisite is not null && !IsLearned(state, prerequisite.Id)) {
            return new(state, $"Learn {prerequisite.Name} first.");
        }

        if (!BranchAllowed(state, skill)) {
            return new(state, "Only two branches can be combined.");
        }

        if (PointsAvailable(state) < skill.Cost) {
            return new(state, "Not enough skill points.");
        }

        return new(state with { Skills = Sorted([.. state.Skills ?? [], skillId]) }, null);
    }

    /// <summary>Forgets every learned skill and gives the points back; the class is kept. Debug only (module 3).</summary>
    public NocState Respec(NocState state) {
        ArgumentNullException.ThrowIfNull(state);
        return state with { Skills = null };
    }

    /// <summary>
    /// Makes a loaded (possibly hand-edited, or older than the current catalog) state valid, never refusing it:
    /// an unknown class id is dropped (and with it the skills), and skills are kept in id order only while they exist, are ready, belong to the class,
    /// fit the level, follow their prerequisite, stay within two branches and within the points earned. Everything else is listed in <paramref name="warnings"/>.
    /// </summary>
    public NocState Sanitize(NocState state, out IReadOnlyList<string> warnings) {
        ArgumentNullException.ThrowIfNull(state);
        var notes = new List<string>();
        warnings = notes;
        var classId = state.ClassId;
        if (classId is int declared && !classes.Contains(declared)) {
            notes.Add($"unknown class {declared} dropped");
            classId = null;
        }

        var requested = (state.Skills ?? []).Distinct().Order().ToArray();
        if (classId is null) {
            if (requested.Length > 0) {
                notes.Add($"{requested.Length} skill(s) dropped: no class");
            }

            return Rebuild(state, classId, []);
        }

        var kept = new List<int>();
        var probe = state with { ClassId = classId, Skills = null };
        var points = progression.SkillPointsAt(state.Level);
        foreach (var id in requested) {
            var reason = RejectionOnLoad(probe, id, points);
            if (reason is not null) {
                notes.Add($"skill {id} dropped: {reason}");
                continue;
            }

            kept.Add(id);
            probe = probe with { Skills = [.. kept] };
            points = progression.SkillPointsAt(state.Level) - PointsSpent(probe);
        }

        return Rebuild(state, classId, kept);
    }

    private string? RejectionOnLoad(NocState probe, int skillId, int pointsLeft) {
        if (!skills.TryGet(skillId, out var skill)) {
            return "not in the catalog";
        }

        if (skill.ClassId != probe.ClassId) {
            return "belongs to another class";
        }

        if (!skill.IsReady) {
            return "not available";
        }

        if (probe.Level < skill.Level) {
            return $"requires level {skill.Level}";
        }

        var prerequisite = PrerequisiteOf(skill);
        if (prerequisite is not null && !IsLearned(probe, prerequisite.Id)) {
            return $"prerequisite {prerequisite.Id} missing";
        }

        if (!BranchAllowed(probe, skill)) {
            return "more than two branches";
        }

        return pointsLeft < skill.Cost ? "not enough points" : null;
    }

    private bool BranchAllowed(NocState state, SkillDefinition skill) {
        var branches = new HashSet<int>();
        foreach (var learned in Learned(state)) {
            branches.Add(learned.Branch);
        }

        return branches.Contains(skill.Branch) || branches.Count < MaxBranchesWithSkills;
    }

    private static NocState Rebuild(NocState state, int? classId, IReadOnlyCollection<int> kept) =>
        state with { ClassId = classId, Skills = kept.Count == 0 ? null : Sorted([.. kept]) };

    private static int[] Sorted(int[] ids) {
        Array.Sort(ids);
        return ids;
    }
}

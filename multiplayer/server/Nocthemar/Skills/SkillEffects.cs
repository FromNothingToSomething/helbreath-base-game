using Server.Nocthemar.Core;
using Server.Nocthemar.Data;

namespace Server.Nocthemar.Skills;

/// <summary>
/// Applies the learned skills of one player to its <see cref="StatPipeline"/> (passive modifiers, one source per skill) and to its <see cref="SkillHookTable"/> (handlers).
/// <see cref="Apply"/> is idempotent: it first removes whatever it applied before, so calling it again with the same skills changes nothing and with fewer skills undoes the rest.
/// World thread only.
/// </summary>
public sealed class SkillEffects {
    private readonly StatPipeline stats;
    private readonly List<int> applied = [];

    public SkillEffects(StatPipeline stats, SkillHookTable hooks) {
        this.stats = stats ?? throw new ArgumentNullException(nameof(stats));
        Hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
    }

    public SkillHookTable Hooks { get; }

    /// <summary>Ids currently applied, in the order they were applied.</summary>
    public IReadOnlyList<int> AppliedIds => applied;

    public void Apply(IReadOnlyList<SkillDefinition> learned) {
        ArgumentNullException.ThrowIfNull(learned);
        Clear();
        foreach (var skill in learned) {
            foreach (var modifier in skill.Modifiers) {
                stats.Add(modifier);
            }

            Hooks.Add(skill);
            applied.Add(skill.Id);
        }
    }

    public void Clear() {
        foreach (var id in applied) {
            stats.RemoveSource(ModifierSources.Skill(id));
            Hooks.Remove(id);
        }

        applied.Clear();
    }
}

using Server.Nocthemar.Core;

namespace Server.Nocthemar.Skills;

/// <summary>Cast: timed stat modifiers on the caster (a re-cast refreshes the duration).</summary>
public sealed class SelfBuffHandler(string id, int durationMs, params StatModifier[] modifiers) : ISkillHandler {
    public string Id { get; } = id;
    public SkillHook Hooks => SkillHook.OnCast;

    public SkillResult Execute(in SkillContext context) {
        if (context.Actor is null) {
            return SkillResult.Refuse("The skill failed.");
        }

        context.Actor.ApplyBuff(context.Skill.Id, modifiers, context.NowMs + durationMs);
        return SkillResult.Ok;
    }
}

/// <summary>Cast: heals the caster; refused (nothing spent) when already at full HP.</summary>
public sealed class SelfHealHandler(string id, int amount) : ISkillHandler {
    public string Id { get; } = id;
    public SkillHook Hooks => SkillHook.OnCast;

    public SkillResult Execute(in SkillContext context) =>
        context.Actor is { } actor && actor.Heal(amount) > 0 ? SkillResult.Ok : SkillResult.Refuse("Nothing to heal.");
}

/// <summary>The handlers shipped with the game; the keys are the <c>handler</c> values of <c>Skills.json</c>.</summary>
public static class BuiltinSkillHandlers {
    public const string BoneShield = "buff_bone_shield";
    public const string BlackBlessing = "heal_black_blessing";

    public static SkillHandlerRegistry Register(SkillHandlerRegistry registry) {
        ArgumentNullException.ThrowIfNull(registry);
        return registry
            .Register(new SelfBuffHandler(BoneShield, 8000,
                new StatModifier(StatId.ResistPhysical, ModifierKind.Flat, 0.30, 0),
                new StatModifier(StatId.ResistMagic, ModifierKind.Flat, 0.30, 0)))
            .Register(new SelfHealHandler(BlackBlessing, 250));
    }
}

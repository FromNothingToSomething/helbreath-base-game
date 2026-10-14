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

/// <summary>Passive: +1 % armor per monster killed, up to <see cref="MaxBonus"/> (rebuilt from the persisted kill count on every apply).</summary>
public sealed class KillArmorHandler(string id, double perKill, double maxBonus) : ISkillHandler {
    public string Id { get; } = id;
    public SkillHook Hooks => SkillHook.OnKill | SkillHook.OnApply;
    public double MaxBonus { get; } = maxBonus;

    public SkillResult Execute(in SkillContext context) {
        if (context.Actor is null) {
            return SkillResult.Refuse("The skill failed.");
        }

        var bonus = Math.Min(MaxBonus, context.Actor.Kills * perKill);
        context.Actor.SetSkillModifiers(context.Skill.Id, bonus > 0 ? [new StatModifier(StatId.ArmorMult, ModifierKind.PercentAdd, bonus, 0)] : []);
        return SkillResult.Ok;
    }
}

/// <summary>The handlers shipped with the game; the keys are the <c>handler</c> values of <c>Skills.json</c>.</summary>
public static class BuiltinSkillHandlers {
    public const string BoneShield = "buff_bone_shield";
    public const string BlackBlessing = "heal_black_blessing";
    public const string AbyssalHeritage = "passive_abyssal_heritage";

    public static SkillHandlerRegistry Register(SkillHandlerRegistry registry) {
        ArgumentNullException.ThrowIfNull(registry);
        return registry
            .Register(new SelfBuffHandler(BoneShield, 8000,
                new StatModifier(StatId.ResistPhysical, ModifierKind.Flat, 0.30, 0),
                new StatModifier(StatId.ResistMagic, ModifierKind.Flat, 0.30, 0)))
            .Register(new SelfHealHandler(BlackBlessing, 250))
            .Register(new KillArmorHandler(AbyssalHeritage, 0.01, 0.20));
    }
}

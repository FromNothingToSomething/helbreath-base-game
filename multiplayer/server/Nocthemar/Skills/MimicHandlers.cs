using System.Text.RegularExpressions;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;

namespace Server.Nocthemar.Skills;

/// <summary>What a mimicry handler is told about a cast.</summary>
/// <param name="Ability">The ability being cast.</param>
/// <param name="Level">Ability level (1..max).</param>
/// <param name="Power">Level multiplier (1 + 0.05 per level above 1) times the caster's <c>MimicPowerMult</c>.</param>
/// <param name="DurationMult">The caster's <c>MimicDurationMult</c>.</param>
/// <param name="CasterId">Network id of the caster, as for skills.</param>
/// <param name="TargetId">Network id of the target, when there is one.</param>
/// <param name="NowMs">World clock in milliseconds.</param>
/// <param name="Actor">The caster, when the cast comes from the world.</param>
public readonly record struct MimicContext(MimicAbility Ability, int Level, double Power, double DurationMult, int CasterId, int? TargetId, long NowMs, ISkillActor? Actor);

/// <summary>One behaviour shared by every ability that names it in <c>Mimic.json</c> (<c>handler</c>). Stateless.</summary>
public interface IMimicHandler {
    /// <summary>Key matched against <c>Mimic.json</c> (<c>^[a-z][a-z0-9_]{1,47}$</c>).</summary>
    string Id { get; }

    SkillResult Execute(in MimicContext context);
}

/// <summary>Handler registry for mimicry; <see cref="ValidateCatalog"/> makes a ready ability that names a missing handler fail at startup.</summary>
public sealed class MimicHandlerRegistry {
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9_]{1,47}$", RegexOptions.Compiled);

    private readonly Dictionary<string, IMimicHandler> handlers = new(StringComparer.Ordinal);

    public int Count => handlers.Count;

    public IEnumerable<string> Ids => handlers.Keys;

    public MimicHandlerRegistry Register(IMimicHandler handler) {
        ArgumentNullException.ThrowIfNull(handler);
        var id = handler.Id;
        if (id is null || !IdPattern.IsMatch(id)) {
            throw new ArgumentException($"Handler id '{id}' must match {IdPattern}.", nameof(handler));
        }

        if (!handlers.TryAdd(id, handler)) {
            throw new ArgumentException($"Handler '{id}' is registered twice.", nameof(handler));
        }

        return this;
    }

    public bool TryGet(string id, out IMimicHandler handler) => handlers.TryGetValue(id, out handler!);

    /// <summary>Throws listing every ready ability whose <c>handler</c> is not registered (an ability with only a <c>spellId</c> needs none).</summary>
    public void ValidateCatalog(MimicConfig mimic) {
        ArgumentNullException.ThrowIfNull(mimic);
        var missing = mimic.Abilities
            .Where(a => a.IsReady && a.Handler is not null && !handlers.ContainsKey(a.Handler))
            .Select(a => $"{a.Key} (id {a.Id}) -> '{a.Handler}'")
            .ToArray();
        if (missing.Length > 0) {
            throw new InvalidOperationException($"Ready mimic abilities reference unregistered handlers: {string.Join("; ", missing)}.");
        }
    }
}

/// <summary>One stat change of a self buff; <see cref="Value"/> is scaled by the cast's power and never goes past <see cref="Max"/>.</summary>
public readonly record struct MimicBuffEntry(StatId Stat, ModifierKind Kind, double Value, double Max = double.MaxValue);

/// <summary>Cast: timed stat modifiers on the caster, scaled by the ability's power; the duration is scaled by <c>MimicDurationMult</c>. A re-cast refreshes instead of stacking.</summary>
public sealed class MimicSelfBuffHandler(string id, int durationMs, params MimicBuffEntry[] entries) : IMimicHandler {
    public string Id { get; } = id;

    public SkillResult Execute(in MimicContext context) {
        if (context.Actor is null) {
            return SkillResult.Refuse("The ability failed.");
        }

        var modifiers = new StatModifier[entries.Length];
        for (var i = 0; i < entries.Length; i++) {
            var e = entries[i];
            modifiers[i] = new StatModifier(e.Stat, e.Kind, Math.Min(e.Max, e.Value * context.Power), 0);
        }

        var duration = (long)Math.Round(durationMs * Math.Max(0.0, context.DurationMult), MidpointRounding.AwayFromZero);
        context.Actor.ApplyBuff(context.Ability.Id, modifiers, context.NowMs + duration);
        return SkillResult.Ok;
    }
}

/// <summary>The mimicry handlers shipped with the game; the keys are the <c>handler</c> values of <c>Mimic.json</c>.</summary>
public static class BuiltinMimicHandlers {
    public const string IronSkin = "buff_iron_skin";
    public const string BlackShield = "buff_black_shield";

    public static MimicHandlerRegistry Register(MimicHandlerRegistry registry) {
        ArgumentNullException.ThrowIfNull(registry);
        return registry
            .Register(new MimicSelfBuffHandler(IronSkin, 10000, new MimicBuffEntry(StatId.ArmorMult, ModifierKind.PercentAdd, 0.30)))
            .Register(new MimicSelfBuffHandler(BlackShield, 8000,
                new MimicBuffEntry(StatId.ResistPhysical, ModifierKind.Flat, 0.50, 0.90),
                new MimicBuffEntry(StatId.ResistMagic, ModifierKind.Flat, 0.50, 0.90)));
    }
}

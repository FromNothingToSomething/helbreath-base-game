using System.Text.RegularExpressions;
using Server.Nocthemar.Data;

namespace Server.Nocthemar.Skills;

/// <summary>Moments of the game flow at which a learned skill's handler is invoked; a handler declares the ones it needs.</summary>
[Flags]
public enum SkillHook {
    None = 0,

    /// <summary>The player casts the skill (active skills).</summary>
    OnCast = 1,

    /// <summary>The player's attack landed on a target.</summary>
    OnHitDealt = 2,

    /// <summary>The player took damage.</summary>
    OnDamageTaken = 4,

    /// <summary>The player killed a monster or a player.</summary>
    OnKill = 8,

    /// <summary>Every world tick of the player (kept to a minimum: it is the hot path).</summary>
    OnTick = 16,
}

public static class SkillHooks {
    /// <summary>Number of single-bit hooks; the hook table is indexed by bit position.</summary>
    public const int Count = 5;

    public const SkillHook All = SkillHook.OnCast | SkillHook.OnHitDealt | SkillHook.OnDamageTaken | SkillHook.OnKill | SkillHook.OnTick;

    /// <summary>Bit position of a single hook (OnCast = 0 ... OnTick = 4).</summary>
    public static int IndexOf(SkillHook hook) {
        var value = (int)hook;
        if (value == 0 || (value & (value - 1)) != 0 || (hook & ~All) != 0) {
            throw new ArgumentException($"{hook} is not a single known hook.", nameof(hook));
        }

        return System.Numerics.BitOperations.TrailingZeroCount(value);
    }
}

/// <summary>What a handler is told about the invocation. World-side details (player, target) are added by the integration tasks; handlers stay unit-testable without a GameWorld.</summary>
/// <param name="Skill">The skill being run.</param>
/// <param name="Hook">The moment that fired it.</param>
/// <param name="CasterId">Network id of the player who owns the skill.</param>
/// <param name="TargetId">Network id of the target, when the hook has one.</param>
/// <param name="Amount">Damage dealt / taken for the damage hooks, otherwise 0.</param>
/// <param name="NowMs">World clock in milliseconds.</param>
/// <param name="Actor">The player running the skill, when the invocation comes from the world; null in unit tests that do not need one.</param>
public readonly record struct SkillContext(SkillDefinition Skill, SkillHook Hook, int CasterId, int? TargetId, int Amount, long NowMs, ISkillActor? Actor = null);

/// <summary>What a handler may do to the player that owns the skill; implemented by the world player so handlers stay free of world types.</summary>
public interface ISkillActor {
    /// <summary>Heals (scaled by the player's heal-received stat); returns the HP actually regained.</summary>
    int Heal(int amount);

    /// <summary>Applies timed stat modifiers owned by the skill until <paramref name="untilMs"/>; casting again refreshes them instead of stacking.</summary>
    void ApplyBuff(int skillId, IReadOnlyList<Server.Nocthemar.Core.StatModifier> modifiers, long untilMs);
}

/// <summary>Outcome of a handler. <c>Refusal</c> is a short player-facing reason when <c>Success</c> is false (nothing is spent).</summary>
public readonly record struct SkillResult(bool Success, string? Refusal = null) {
    public static SkillResult Ok { get; } = new(true);

    public static SkillResult Refuse(string reason) => new(false, reason);
}

/// <summary>One behaviour, shared by every skill that names it in <c>Skills.json</c> (<c>handler</c>). Stateless: per-player state belongs to the world, not to the handler.</summary>
public interface ISkillHandler {
    /// <summary>Key matched against <c>Skills.json</c> (<c>^[a-z][a-z0-9_]{1,47}$</c>).</summary>
    string Id { get; }

    /// <summary>The hooks this handler wants; <see cref="SkillHook.None"/> is invalid.</summary>
    SkillHook Hooks { get; }

    SkillResult Execute(in SkillContext context);
}

/// <summary>
/// Handler registry: the dispatch is a dictionary lookup by handler key, one lookup per handler key, no branching per skill id.
/// Build it once at startup, then <see cref="ValidateCatalog"/> so that a ready skill naming a missing handler fails fast.
/// </summary>
public sealed class SkillHandlerRegistry {
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9_]{1,47}$", RegexOptions.Compiled);

    private readonly Dictionary<string, ISkillHandler> handlers = new(StringComparer.Ordinal);

    public int Count => handlers.Count;

    public IEnumerable<string> Ids => handlers.Keys;

    public SkillHandlerRegistry Register(ISkillHandler handler) {
        ArgumentNullException.ThrowIfNull(handler);
        var id = handler.Id;
        if (id is null || !IdPattern.IsMatch(id)) {
            throw new ArgumentException($"Handler id '{id}' must match {IdPattern}.", nameof(handler));
        }

        if (handler.Hooks == SkillHook.None || (handler.Hooks & ~SkillHooks.All) != 0) {
            throw new ArgumentException($"Handler '{id}' declares invalid hooks {handler.Hooks}.", nameof(handler));
        }

        if (!handlers.TryAdd(id, handler)) {
            throw new ArgumentException($"Handler '{id}' is registered twice.", nameof(handler));
        }

        return this;
    }

    public bool TryGet(string id, out ISkillHandler handler) => handlers.TryGetValue(id, out handler!);

    /// <summary>Throws listing every ready skill whose <c>handler</c> is not registered (planned skills are ignored).</summary>
    public void ValidateCatalog(SkillConfig skills) {
        ArgumentNullException.ThrowIfNull(skills);
        var missing = skills.Skills
            .Where(s => s.IsReady && s.Handler is not null && !handlers.ContainsKey(s.Handler))
            .Select(s => $"{s.Key} (id {s.Id}) -> '{s.Handler}'")
            .ToArray();
        if (missing.Length > 0) {
            throw new InvalidOperationException($"Ready skills reference unregistered handlers: {string.Join("; ", missing)}.");
        }
    }
}

/// <summary>
/// Per-player table of the handlers of the skills the player has learned, grouped by hook, so a hook point does one array read and one loop (no branching per skill, no allocation).
/// Owned and used by the world thread only. Passives that are modifiers-only have no handler and never appear here.
/// </summary>
public sealed class SkillHookTable {
    private static readonly Entry[] None = [];

    private readonly SkillHandlerRegistry registry;
    private readonly Entry[][] byHook = new Entry[SkillHooks.Count][];

    public SkillHookTable(SkillHandlerRegistry registry) {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Array.Fill(byHook, None);
    }

    public int Count { get; private set; }

    /// <summary>Subscribes the skill's handler to the hooks it declared. No-op for a skill without handler; throws for an unregistered one (the catalog is validated at startup, so this is a programming error).</summary>
    public void Add(SkillDefinition skill) {
        ArgumentNullException.ThrowIfNull(skill);
        if (skill.Handler is null) {
            return;
        }

        if (!registry.TryGet(skill.Handler, out var handler)) {
            throw new InvalidOperationException($"Skill {skill.Key} references unregistered handler '{skill.Handler}'.");
        }

        if (Contains(skill.Id)) {
            return;
        }

        for (var i = 0; i < SkillHooks.Count; i++) {
            if (((int)handler.Hooks & (1 << i)) == 0) {
                continue;
            }

            var list = byHook[i];
            var grown = new Entry[list.Length + 1];
            Array.Copy(list, grown, list.Length);
            grown[^1] = new Entry(skill, handler);
            byHook[i] = grown;
        }

        Count++;
    }

    /// <summary>Unsubscribes a skill; false when it was not subscribed.</summary>
    public bool Remove(int skillId) {
        var removed = false;
        for (var i = 0; i < SkillHooks.Count; i++) {
            var list = byHook[i];
            var index = Array.FindIndex(list, e => e.Skill.Id == skillId);
            if (index < 0) {
                continue;
            }

            removed = true;
            if (list.Length == 1) {
                byHook[i] = None;
                continue;
            }

            var shrunk = new Entry[list.Length - 1];
            Array.Copy(list, 0, shrunk, 0, index);
            Array.Copy(list, index + 1, shrunk, index, list.Length - index - 1);
            byHook[i] = shrunk;
        }

        if (removed) {
            Count--;
        }

        return removed;
    }

    public bool Contains(int skillId) {
        foreach (var list in byHook) {
            foreach (var entry in list) {
                if (entry.Skill.Id == skillId) {
                    return true;
                }
            }
        }

        return false;
    }

    public void Clear() {
        Array.Fill(byHook, None);
        Count = 0;
    }

    /// <summary>Number of subscribers of one hook.</summary>
    public int SubscribersOf(SkillHook hook) => byHook[SkillHooks.IndexOf(hook)].Length;

    /// <summary>
    /// Runs every subscriber of <paramref name="hook"/> in subscription order and returns how many succeeded.
    /// A throwing handler is isolated: <paramref name="onError"/> receives it and the others still run.
    /// </summary>
    public int Run(SkillHook hook, int casterId, int? targetId, int amount, long nowMs, Action<SkillDefinition, Exception>? onError = null) {
        var list = byHook[SkillHooks.IndexOf(hook)];
        var succeeded = 0;
        foreach (var entry in list) {
            var context = new SkillContext(entry.Skill, hook, casterId, targetId, amount, nowMs);
            try {
                if (entry.Handler.Execute(in context).Success) {
                    succeeded++;
                }
            } catch (Exception ex) {
                onError?.Invoke(entry.Skill, ex);
            }
        }

        return succeeded;
    }

    /// <summary>Runs only the subscription of one skill (used by the cast path: the cast request names the skill).</summary>
    public SkillResult RunSkill(int skillId, SkillHook hook, int casterId, int? targetId, long nowMs, ISkillActor? actor = null) {
        foreach (var entry in byHook[SkillHooks.IndexOf(hook)]) {
            if (entry.Skill.Id == skillId) {
                var context = new SkillContext(entry.Skill, hook, casterId, targetId, 0, nowMs, actor);
                return entry.Handler.Execute(in context);
            }
        }

        return SkillResult.Refuse("skill not available");
    }

    private readonly record struct Entry(SkillDefinition Skill, ISkillHandler Handler);
}

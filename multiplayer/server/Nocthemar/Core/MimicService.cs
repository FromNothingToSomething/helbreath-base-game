using Server.Nocthemar.Data;

namespace Server.Nocthemar.Core;

/// <summary>Outcome of a mimicry action: the new state, or a short player-facing refusal (the state is then the unchanged input).</summary>
public readonly record struct MimicActionResult(NocState State, string? Refusal) {
    public bool Ok => Refusal is null;
}

/// <summary>
/// Pure mimicry rules over <see cref="NocState"/>; holds no per-player state, so one instance is shared by every world.
/// Essence items are the caller's business (inventory, ground, Forge d'Âmes): this service only decides what an essence does to the state.
/// Rules: one ability per monster (a duplicate is refused, it can only be resold); a class-reserved ability can be learned and equipped by that class only;
/// a fusion result is obtained by fusing two monster abilities and nothing else, and fusing does not bypass the duplicate and class rules;
/// the bar holds at most as many abilities as the character has slots; each cast gives experience up to the maximum ability level.
/// </summary>
public sealed class MimicService {
    /// <summary>Absolute ceiling on slots, bonus included (the base table stops at 9, see <see cref="MimicSlotTable"/>).</summary>
    public const int MaxTotalSlots = 12;

    private readonly MimicConfig mimic;

    public MimicService(MimicConfig mimic) {
        this.mimic = mimic ?? throw new ArgumentNullException(nameof(mimic));
    }

    public MimicConfig Config => mimic;

    /// <summary>Slots at <paramref name="level"/>: <c>min(9, 1 + (level - 1) / 10)</c> from the config, plus <paramref name="bonus"/> (the MimicSlots stat), at most <see cref="MaxTotalSlots"/>.</summary>
    public int SlotsAt(int level, int bonus = 0) => Math.Clamp(mimic.Slots.SlotsAt(level) + Math.Max(0, bonus), 1, MaxTotalSlots);

    public bool IsLearned(NocState state, int abilityId) => Find(state, abilityId) >= 0;

    public bool IsEquipped(NocState state, int abilityId) => state.MimicEquipped is { } bar && Array.IndexOf(bar, abilityId) >= 0;

    /// <summary>Experience of a learned ability; 0 when it is not learned.</summary>
    public long XpOf(NocState state, int abilityId) {
        var index = Find(state, abilityId);
        return index < 0 ? 0 : state.Mimics![index].Xp;
    }

    /// <summary>Level (1..max) of a learned ability; 0 when it is not learned.</summary>
    public int LevelOf(NocState state, int abilityId) => IsLearned(state, abilityId) ? mimic.Xp.LevelOf(XpOf(state, abilityId)) : 0;

    /// <summary>Power multiplier of a learned ability from its level, 1.0 when it is not learned.</summary>
    public double PowerOf(NocState state, int abilityId) => IsLearned(state, abilityId) ? mimic.Xp.PowerMultiplier(LevelOf(state, abilityId)) : 1.0;

    /// <summary>The learned abilities as catalog entries, in id order; ids missing from the catalog are skipped.</summary>
    public IReadOnlyList<MimicAbility> Learned(NocState state) {
        var result = new List<MimicAbility>();
        foreach (var entry in state.Mimics ?? []) {
            if (mimic.TryGet(entry.Id, out var ability)) {
                result.Add(ability);
            }
        }

        return result;
    }

    /// <summary>The equipped abilities as catalog entries, in bar order; ids missing from the catalog are skipped.</summary>
    public IReadOnlyList<MimicAbility> Equipped(NocState state) {
        var result = new List<MimicAbility>();
        foreach (var id in state.MimicEquipped ?? []) {
            if (mimic.TryGet(id, out var ability)) {
                result.Add(ability);
            }
        }

        return result;
    }

    /// <summary>Forge, "Consommer": the essence of a monster ability teaches it permanently (level 1, no experience).</summary>
    public MimicActionResult Consume(NocState state, int abilityId) {
        ArgumentNullException.ThrowIfNull(state);
        var refusal = RefuseLearning(state, abilityId, byFusion: false);
        return refusal is not null ? new(state, refusal) : new(Add(state, abilityId), null);
    }

    /// <summary>
    /// Forge, "Fusionner": two different monster essences give the ability of their recipe, in either order. Both essences are consumed by the caller on success.
    /// The result is subject to the same duplicate and class rules as <see cref="Consume"/>.
    /// </summary>
    public MimicActionResult Fuse(NocState state, int essenceA, int essenceB) {
        ArgumentNullException.ThrowIfNull(state);
        if (essenceA == essenceB) {
            return new(state, "Choose two different essences.");
        }

        if (!mimic.TryGet(essenceA, out var a) || !mimic.TryGet(essenceB, out var b)) {
            return new(state, "Unknown essence.");
        }

        if (a.IsFusion || b.IsFusion) {
            return new(state, "A fused ability cannot be fused again.");
        }

        if (!mimic.TryFindFusion(essenceA, essenceB, out var recipe)) {
            return new(state, "These essences do not fuse.");
        }

        var refusal = RefuseLearning(state, recipe.Result, byFusion: true);
        return refusal is not null ? new(state, refusal) : new(Add(state, recipe.Result), null);
    }

    /// <summary>Puts a learned ability on the first free place of the bar; <paramref name="slotLimit"/> is the number of slots the character has (see <see cref="SlotsAt"/>).</summary>
    public MimicActionResult Equip(NocState state, int abilityId, int slotLimit) {
        ArgumentNullException.ThrowIfNull(state);
        if (!mimic.TryGet(abilityId, out var ability)) {
            return new(state, "Unknown ability.");
        }

        if (!IsLearned(state, abilityId)) {
            return new(state, "Ability not learned.");
        }

        if (!ability.AllowsClass(state.ClassId)) {
            return new(state, "This ability is reserved to another class.");
        }

        if (IsEquipped(state, abilityId)) {
            return new(state, "Ability already equipped.");
        }

        var bar = state.MimicEquipped ?? [];
        if (bar.Length >= Math.Clamp(slotLimit, 0, MaxTotalSlots)) {
            return new(state, "No free mimicry slot.");
        }

        return new(state with { MimicEquipped = [.. bar, abilityId] }, null);
    }

    /// <summary>Removes an ability from the bar; the abilities after it move up one place.</summary>
    public MimicActionResult Unequip(NocState state, int abilityId) {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsEquipped(state, abilityId)) {
            return new(state, "Ability not equipped.");
        }

        var bar = state.MimicEquipped!.Where(id => id != abilityId).ToArray();
        return new(state with { MimicEquipped = bar.Length == 0 ? null : bar }, null);
    }

    /// <summary>One successful cast: +<see cref="MimicXpTable.PerCast"/> experience, capped at the maximum level's threshold. Not learned: unchanged.</summary>
    public NocState AddCastXp(NocState state, int abilityId) {
        ArgumentNullException.ThrowIfNull(state);
        var index = Find(state, abilityId);
        if (index < 0) {
            return state;
        }

        var current = state.Mimics![index];
        var xp = Math.Min(mimic.Xp.MaxXp, current.Xp + mimic.Xp.PerCast);
        if (xp <= current.Xp) {
            return state;
        }

        var entries = (MimicEntry[])state.Mimics.Clone();
        entries[index] = current with { Xp = xp };
        return state with { Mimics = entries };
    }

    /// <summary>
    /// Makes a loaded (possibly hand-edited, or older than the current catalog) state valid, never refusing it:
    /// unknown, duplicate and class-forbidden abilities are dropped, experience is brought into [0, max], and the bar keeps only learned and allowed
    /// abilities, once each, in order, up to <paramref name="slotLimit"/>. Everything dropped or changed is listed in <paramref name="warnings"/>.
    /// Call it after the class has been sanitized.
    /// </summary>
    public NocState Sanitize(NocState state, int slotLimit, out IReadOnlyList<string> warnings) {
        ArgumentNullException.ThrowIfNull(state);
        var notes = new List<string>();
        warnings = notes;
        var kept = new SortedDictionary<int, long>();
        foreach (var entry in state.Mimics ?? []) {
            if (!mimic.TryGet(entry.Id, out var ability)) {
                notes.Add($"mimic ability {entry.Id} dropped: not in the catalog");
                continue;
            }

            if (!ability.AllowsClass(state.ClassId)) {
                notes.Add($"mimic ability {entry.Id} dropped: reserved to another class");
                continue;
            }

            var xp = Math.Clamp(entry.Xp, 0, mimic.Xp.MaxXp);
            if (xp != entry.Xp) {
                notes.Add($"mimic ability {entry.Id} experience set to {xp}");
            }

            if (kept.TryGetValue(entry.Id, out var previous)) {
                notes.Add($"mimic ability {entry.Id} listed twice: kept the highest experience");
                xp = Math.Max(previous, xp);
            }

            kept[entry.Id] = xp;
        }

        var bar = new List<int>();
        var limit = Math.Clamp(slotLimit, 0, MaxTotalSlots);
        foreach (var id in state.MimicEquipped ?? []) {
            if (!kept.ContainsKey(id)) {
                notes.Add($"equipped mimic ability {id} dropped: not learned");
            } else if (bar.Contains(id)) {
                notes.Add($"equipped mimic ability {id} dropped: listed twice");
            } else if (bar.Count >= limit) {
                notes.Add($"equipped mimic ability {id} dropped: no free slot");
            } else {
                bar.Add(id);
            }
        }

        return state with {
            Mimics = kept.Count == 0 ? null : [.. kept.Select(p => new MimicEntry(p.Key, p.Value))],
            MimicEquipped = bar.Count == 0 ? null : [.. bar],
        };
    }

    private string? RefuseLearning(NocState state, int abilityId, bool byFusion) {
        if (!mimic.TryGet(abilityId, out var ability)) {
            return "Unknown ability.";
        }

        if (ability.IsFusion != byFusion) {
            return byFusion ? "Unknown ability." : "This ability can only be obtained by fusion.";
        }

        if (!ability.AllowsClass(state.ClassId)) {
            return "This ability is reserved to another class.";
        }

        return IsLearned(state, abilityId) ? "Ability already learned." : null;
    }

    private static NocState Add(NocState state, int abilityId) =>
        state with { Mimics = [.. (state.Mimics ?? []).Append(new MimicEntry(abilityId)).OrderBy(e => e.Id)] };

    private static int Find(NocState state, int abilityId) {
        var entries = state.Mimics;
        if (entries is null) {
            return -1;
        }

        for (var i = 0; i < entries.Length; i++) {
            if (entries[i].Id == abilityId) {
                return i;
            }
        }

        return -1;
    }
}

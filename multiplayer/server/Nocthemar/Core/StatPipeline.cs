using Server.Nocthemar.Data;

namespace Server.Nocthemar.Core;

/// <summary>Stats resolved by <see cref="StatPipeline"/>.</summary>
public enum StatId : byte {
    MaxHp = 0,
    MaxMana = 1,
    ManaRegen = 2,
    DamageMult = 3,
}

/// <summary>How a <see cref="StatModifier"/> combines with the base value.</summary>
public enum ModifierKind : byte {
    /// <summary>Added to the base value.</summary>
    Flat = 0,

    /// <summary>Fraction summed with the other additive percents (0.2 = +20%); two +50% give x2.0.</summary>
    PercentAdd = 1,

    /// <summary>Fraction applied as its own factor (0.1 = x1.1); two +50% give x2.25.</summary>
    PercentMult = 2,
}

/// <summary>One declarative stat change. <see cref="SourceId"/> identifies the owner (race, class passive, buff...) so it can be removed in one call.</summary>
public readonly record struct StatModifier(StatId Stat, ModifierKind Kind, double Value, int SourceId);

/// <summary>
/// Per-player stat resolution. Order is fixed: base (from level) -> flat -> additive percents (summed) -> multiplicative percents (multiplied).
/// Allocation-free on the hot path: modifiers live in a pre-sized array, results are cached and recomputed lazily after a change.
/// Not thread-safe: use it from the owning world thread only.
/// </summary>
public sealed class StatPipeline {
    private const int StatCount = 4;

    private readonly ProgressionConfig progression;
    private readonly double[] baseValues = new double[StatCount];
    private readonly double[] values = new double[StatCount];
    private StatModifier[] modifiers;
    private int count;
    private bool dirty = true;

    public StatPipeline(ProgressionConfig progression, int level = 1, int initialCapacity = 16) {
        this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
        modifiers = new StatModifier[Math.Max(1, initialCapacity)];
        SetLevel(level);
    }

    public int Level { get; private set; }

    public int ModifierCount => count;

    /// <summary>Resets the level-derived base values (HP, mana, regen); modifiers are kept.</summary>
    public void SetLevel(int level) {
        Level = progression.ClampLevel(level);
        baseValues[(int)StatId.MaxHp] = progression.MaxHpAt(Level);
        baseValues[(int)StatId.MaxMana] = progression.MaxManaAt(Level);
        baseValues[(int)StatId.ManaRegen] = progression.ManaRegenPerSecond;
        baseValues[(int)StatId.DamageMult] = 1.0;
        dirty = true;
    }

    public void Add(in StatModifier modifier) {
        if (!Enum.IsDefined(modifier.Stat) || !Enum.IsDefined(modifier.Kind)) {
            throw new ArgumentException("Unknown stat or modifier kind.", nameof(modifier));
        }

        if (!double.IsFinite(modifier.Value)) {
            throw new ArgumentException("Modifier value must be finite.", nameof(modifier));
        }

        if (count == modifiers.Length) {
            Array.Resize(ref modifiers, modifiers.Length * 2);
        }

        modifiers[count++] = modifier;
        dirty = true;
    }

    /// <summary>Removes every modifier owned by <paramref name="sourceId"/>; returns how many were removed.</summary>
    public int RemoveSource(int sourceId) {
        var write = 0;
        for (var read = 0; read < count; read++) {
            if (modifiers[read].SourceId != sourceId) {
                modifiers[write++] = modifiers[read];
            }
        }

        var removed = count - write;
        count = write;
        if (removed > 0) {
            dirty = true;
        }

        return removed;
    }

    public void Clear() {
        count = 0;
        dirty = true;
    }

    public int MaxHp => ToInt(Get(StatId.MaxHp), minimum: 1);

    public int MaxMana => ToInt(Get(StatId.MaxMana), minimum: 0);

    public double ManaRegen => Get(StatId.ManaRegen);

    public double DamageMult => Get(StatId.DamageMult);

    /// <summary>Resolved value of <paramref name="stat"/> (never negative, never NaN).</summary>
    public double Get(StatId stat) {
        Refresh();
        return values[(int)stat];
    }

    /// <summary>Applies <see cref="DamageMult"/> to <paramref name="baseDamage"/>; 0 stays 0, any positive base deals at least 1.</summary>
    public int ComputeDamage(int baseDamage) {
        if (baseDamage <= 0) {
            return 0;
        }

        Refresh();
        return ToInt(baseDamage * values[(int)StatId.DamageMult], minimum: 1);
    }

    private void Refresh() {
        if (!dirty) {
            return;
        }

        Span<double> flat = stackalloc double[StatCount];
        Span<double> add = stackalloc double[StatCount];
        Span<double> mult = stackalloc double[StatCount];
        mult.Fill(1.0);

        for (var i = 0; i < count; i++) {
            var stat = (int)modifiers[i].Stat;
            var value = modifiers[i].Value;
            switch (modifiers[i].Kind) {
                case ModifierKind.Flat:
                    flat[stat] += value;
                    break;
                case ModifierKind.PercentAdd:
                    add[stat] += value;
                    break;
                default:
                    mult[stat] *= Math.Max(0.0, 1.0 + value);
                    break;
            }
        }

        for (var s = 0; s < StatCount; s++) {
            var resolved = (baseValues[s] + flat[s]) * Math.Max(0.0, 1.0 + add[s]) * mult[s];
            values[s] = double.IsNaN(resolved) ? 0.0 : Math.Max(0.0, resolved);
        }

        dirty = false;
    }

    private static int ToInt(double value, int minimum) {
        if (value >= int.MaxValue) {
            return int.MaxValue;
        }

        return Math.Max(minimum, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }
}

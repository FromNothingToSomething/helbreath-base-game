using Server.Nocthemar.Core;
using Xunit;

namespace NocthemarTests;

/// <summary>T9: stat resolution order, source removal, clamping, and allocation-free hot path.</summary>
public class StatPipelineTests {
    private static StatPipeline Create(int level = 1) => new(TestSupport.Progression(), level);

    [Fact]
    public void Base_values_come_from_the_level() {
        var p = Create(1);

        Assert.Equal(1000, p.MaxHp);
        Assert.Equal(100, p.MaxMana);
        Assert.Equal(2.0, p.ManaRegen);
        Assert.Equal(1.0, p.DamageMult);

        p.SetLevel(10);
        Assert.Equal(1450, p.MaxHp);
        Assert.Equal(190, p.MaxMana);
    }

    [Fact]
    public void Order_is_base_then_flat_then_additive_percents_then_multiplicative_percents() {
        var p = Create();
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentMult, 0.1, 1));
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentAdd, 0.2, 2));
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.Flat, 100, 3));
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentAdd, 0.3, 4));

        // (1000 + 100) * (1 + 0.2 + 0.3) * 1.1 = 1815, independent of insertion order.
        Assert.Equal(1815, p.MaxHp);
    }

    [Fact]
    public void Additive_percents_sum_and_multiplicative_percents_multiply() {
        var add = Create();
        add.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentAdd, 0.5, 1));
        add.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentAdd, 0.5, 2));
        Assert.Equal(2000, add.MaxHp);

        var mult = Create();
        mult.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentMult, 0.5, 1));
        mult.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentMult, 0.5, 2));
        Assert.Equal(2250, mult.MaxHp);
    }

    [Fact]
    public void Modifiers_only_affect_their_own_stat() {
        var p = Create();
        p.Add(new StatModifier(StatId.MaxMana, ModifierKind.Flat, 50, 1));

        Assert.Equal(1000, p.MaxHp);
        Assert.Equal(150, p.MaxMana);
    }

    [Fact]
    public void RemoveSource_removes_every_modifier_of_that_source_and_only_those() {
        var p = Create();
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.Flat, 100, 7));
        p.Add(new StatModifier(StatId.MaxMana, ModifierKind.Flat, 20, 7));
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.Flat, 5, 8));

        Assert.Equal(2, p.RemoveSource(7));

        Assert.Equal(1005, p.MaxHp);
        Assert.Equal(100, p.MaxMana);
        Assert.Equal(1, p.ModifierCount);
        Assert.Equal(0, p.RemoveSource(7));
        Assert.Equal(0, p.RemoveSource(12345));
    }

    [Fact]
    public void Level_change_recomputes_the_base_and_keeps_modifiers() {
        var p = Create();
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.Flat, 100, 1));

        p.SetLevel(10);

        Assert.Equal(1550, p.MaxHp);
    }

    [Fact]
    public void Levels_are_clamped_to_the_progression_range() {
        var p = Create();

        p.SetLevel(5000);
        Assert.Equal(100, p.Level);
        p.SetLevel(-4);
        Assert.Equal(1, p.Level);
    }

    [Fact]
    public void Results_are_clamped_never_negative() {
        var p = Create();
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentAdd, -5, 1));
        p.Add(new StatModifier(StatId.MaxMana, ModifierKind.Flat, -10_000, 1));
        p.Add(new StatModifier(StatId.ManaRegen, ModifierKind.PercentMult, -3, 1));
        p.Add(new StatModifier(StatId.DamageMult, ModifierKind.PercentAdd, -5, 1));

        Assert.Equal(1, p.MaxHp); // at least 1 HP
        Assert.Equal(0, p.MaxMana);
        Assert.Equal(0.0, p.ManaRegen);
        Assert.Equal(0.0, p.DamageMult);
    }

    [Fact]
    public void Huge_values_saturate_instead_of_overflowing() {
        var p = Create();
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.Flat, 1e300, 1));

        Assert.Equal(int.MaxValue, p.MaxHp);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_modifier_values_are_rejected(double value) {
        var p = Create();

        Assert.Throws<ArgumentException>(() => p.Add(new StatModifier(StatId.MaxHp, ModifierKind.Flat, value, 1)));
        Assert.Equal(0, p.ModifierCount);
    }

    [Fact]
    public void Unknown_stat_or_kind_is_rejected() {
        var p = Create();

        Assert.Throws<ArgumentException>(() => p.Add(new StatModifier((StatId)99, ModifierKind.Flat, 1, 1)));
        Assert.Throws<ArgumentException>(() => p.Add(new StatModifier(StatId.MaxHp, (ModifierKind)99, 1, 1)));
    }

    [Fact]
    public void ComputeDamage_applies_the_multiplier_with_a_floor_of_one() {
        var p = Create();
        Assert.Equal(20, p.ComputeDamage(20));

        p.Add(new StatModifier(StatId.DamageMult, ModifierKind.PercentAdd, 0.2, 1));
        Assert.Equal(24, p.ComputeDamage(20));

        p.Add(new StatModifier(StatId.DamageMult, ModifierKind.PercentAdd, -5, 2));
        Assert.Equal(1, p.ComputeDamage(20));
        Assert.Equal(0, p.ComputeDamage(0));
        Assert.Equal(0, p.ComputeDamage(-3));
    }

    [Fact]
    public void Clear_drops_every_modifier() {
        var p = Create();
        p.Add(new StatModifier(StatId.MaxHp, ModifierKind.Flat, 100, 1));

        p.Clear();

        Assert.Equal(0, p.ModifierCount);
        Assert.Equal(1000, p.MaxHp);
    }

    [Fact]
    public void Capacity_grows_past_the_initial_size() {
        var p = new StatPipeline(TestSupport.Progression(), 1, initialCapacity: 1);

        for (var i = 0; i < 100; i++) {
            p.Add(new StatModifier(StatId.MaxHp, ModifierKind.Flat, 1, i));
        }

        Assert.Equal(100, p.ModifierCount);
        Assert.Equal(1100, p.MaxHp);
    }

    // GC.GetAllocatedBytesForCurrentThread advances in allocation-context chunks, so the bound is 16 KiB over 1,000,000 calls:
    // any per-call allocation (24 bytes or more) would exceed it by three orders of magnitude.
    private const long AllocationToleranceBytes = 16 * 1024;

    private static long Hot(StatPipeline p, int i) {
        p.SetLevel(i % 100 + 1); // dirties the cache so the recompute path runs every call
        return p.MaxHp + p.MaxMana + p.ComputeDamage(20) + (long)p.ManaRegen;
    }

    [Fact]
    public void Hot_path_does_not_allocate() {
        var p = Create(5);
        for (var i = 0; i < 8; i++) {
            p.Add(new StatModifier((StatId)(i % 4), (ModifierKind)(i % 3), 0.05 * (i + 1), 100 + i));
        }

        long sink = 0;
        for (var i = 0; i < 20_000; i++) {
            sink += Hot(p, i); // warm-up: JIT and first-use initialization
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000_000; i++) {
            sink += Hot(p, i);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(sink != 0);
        Assert.True(allocated <= AllocationToleranceBytes, $"hot path allocated {allocated} bytes over 1,000,000 calls");
    }

    [Fact]
    public void Adding_and_removing_modifiers_within_capacity_does_not_allocate() {
        var p = Create();
        long sink = 0;
        for (var i = 0; i < 20_000; i++) {
            p.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentAdd, 0.1, 1));
            sink += p.MaxHp;
            sink += p.RemoveSource(1);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000_000; i++) {
            p.Add(new StatModifier(StatId.MaxHp, ModifierKind.PercentAdd, 0.1, 1));
            sink += p.MaxHp;
            sink += p.RemoveSource(1);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(sink != 0);
        Assert.True(allocated <= AllocationToleranceBytes, $"add/remove allocated {allocated} bytes over 1,000,000 cycles");
    }
}

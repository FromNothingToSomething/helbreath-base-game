using Server.Nocthemar.Core;
using Xunit;

namespace NocthemarTests;

/// <summary>T12: delta-time mana regeneration, independent of tick rate, allocation-free.</summary>
public class ManaRegenTests {
    private const long T0 = 1_700_000_000_000;

    [Fact]
    public void First_call_only_sets_the_baseline() {
        var p = TestSupport.NewPlayer(1, 0, 0);

        Assert.False(p.TickMana(T0));
        Assert.Equal(0, p.Mana);
    }

    [Fact]
    public void Regen_follows_elapsed_time_whatever_the_tick_rate() {
        var fine = TestSupport.NewPlayer(1, 0, 0);
        var coarse = TestSupport.NewPlayer(1, 0, 0);
        fine.TickMana(T0);
        coarse.TickMana(T0);

        for (var i = 1; i <= 8; i++) {
            fine.TickMana(T0 + i * 125); // 2.0/s * 0.125 s = 0.25 (exact in binary)
        }
        coarse.TickMana(T0 + 1000);

        Assert.Equal(2, fine.Mana);
        Assert.Equal(2, coarse.Mana);
    }

    [Fact]
    public void Real_world_50ms_ticks_regenerate_about_two_per_second() {
        var p = TestSupport.NewPlayer(1, 0, 0);
        p.TickMana(T0);

        for (var i = 1; i <= 200; i++) {
            p.TickMana(T0 + i * 50); // 10 s
        }

        Assert.InRange(p.Mana, 19, 20);
    }

    [Fact]
    public void Sub_point_remainders_are_carried_not_lost() {
        var p = TestSupport.NewPlayer(1, 0, 0);
        p.TickMana(T0);

        Assert.False(p.TickMana(T0 + 250)); // 0.5
        Assert.Equal(0, p.Mana);
        Assert.True(p.TickMana(T0 + 500)); // 1.0
        Assert.Equal(1, p.Mana);
    }

    [Fact]
    public void Regen_stops_at_the_maximum() {
        var p = TestSupport.NewPlayer(1, 0, 99);
        p.TickMana(T0);

        p.TickMana(T0 + 4000);

        Assert.Equal(100, p.Mana);
        Assert.False(p.TickMana(T0 + 8000));
        Assert.Equal(100, p.Mana);
    }

    [Fact]
    public void One_call_never_credits_more_than_five_seconds() {
        var p = TestSupport.NewPlayer(1, 0, 0);
        p.TickMana(T0);

        p.TickMana(T0 + 3_600_000);

        Assert.Equal(10, p.Mana); // 2.0/s * 5 s
    }

    [Fact]
    public void A_clock_that_does_not_advance_changes_nothing() {
        var p = TestSupport.NewPlayer(1, 0, 0);
        p.TickMana(T0 + 1000);

        Assert.False(p.TickMana(T0 + 1000));
        Assert.False(p.TickMana(T0));
        Assert.Equal(0, p.Mana);
    }

    [Fact]
    public void Dead_players_do_not_regenerate() {
        var p = TestSupport.NewPlayer(1, 0, 0);
        p.TickMana(T0);
        p.ApplyDamage(100_000);

        Assert.False(p.TickMana(T0 + 2000));
        Assert.Equal(0, p.Mana);
    }

    [Fact]
    public void Regen_modifiers_apply() {
        var p = TestSupport.NewPlayer(1, 0, 0);
        p.Stats.Add(new StatModifier(StatId.ManaRegen, ModifierKind.PercentAdd, 1.0, 1)); // +100% -> 4/s
        p.TickMana(T0);

        p.TickMana(T0 + 1000);

        Assert.Equal(4, p.Mana);
    }

    [Fact]
    public void Mana_above_a_lowered_maximum_is_clamped() {
        var p = TestSupport.NewPlayer(1, 0, 100);
        p.TickMana(T0);
        p.Stats.Add(new StatModifier(StatId.MaxMana, ModifierKind.Flat, -50, 1));

        Assert.True(p.TickMana(T0 + 50));
        Assert.Equal(50, p.Mana);
    }

    // GC.GetAllocatedBytesForCurrentThread advances in chunks: 16 KiB over 1,000,000 calls means no per-call allocation (same method as StatPipelineTests).
    [Fact]
    public void Tick_does_not_allocate() {
        var p = TestSupport.NewPlayer(1, 0, 0);
        long now = T0;
        long sink = 0;
        for (var i = 0; i < 20_000; i++) {
            p.TickMana(now += 50);
            sink += p.Mana;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000_000; i++) {
            p.TickMana(now += 50);
            sink += p.Mana;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(sink > 0);
        Assert.True(allocated <= 16 * 1024, $"TickMana allocated {allocated} bytes over 1,000,000 ticks");
    }
}

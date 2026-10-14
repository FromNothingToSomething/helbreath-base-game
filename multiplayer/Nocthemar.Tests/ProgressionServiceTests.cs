using Server.Nocthemar.Core;
using Xunit;

namespace NocthemarTests;

/// <summary>T10: XP grants, chained level-ups, max-level cap, normalization of loaded state.</summary>
public class ProgressionServiceTests {
    private static readonly Server.Nocthemar.Data.ProgressionConfig Config = TestSupport.Progression();
    private static readonly ProgressionService Service = new(Config);

    private static long Needed(int level) {
        Assert.True(Config.TryGetXpToNext(level, out var xp));
        return xp;
    }

    private static NocState At(int level, long xp = 0, int mana = 0) => new(NocState.CurrentV, level, xp, mana);

    [Fact]
    public void Zero_and_negative_grants_change_nothing() {
        var state = At(3, 5, 7);

        Assert.Equal(new XpGrantResult(state, 0), Service.GrantXp(state, 0));
        Assert.Equal(new XpGrantResult(state, 0), Service.GrantXp(state, -50));
    }

    [Fact]
    public void Grant_below_threshold_adds_xp_only() {
        var result = Service.GrantXp(At(1, 0, 10), Needed(1) - 1);

        Assert.Equal(0, result.LevelsGained);
        Assert.Equal(1, result.State.Level);
        Assert.Equal(Needed(1) - 1, result.State.Xp);
        Assert.Equal(10, result.State.Mana);
    }

    [Fact]
    public void Exact_threshold_levels_up_with_zero_carry_and_refills_mana() {
        var result = Service.GrantXp(At(1, 0, 3), Needed(1));

        Assert.Equal(1, result.LevelsGained);
        Assert.Equal(2, result.State.Level);
        Assert.Equal(0L, result.State.Xp);
        Assert.Equal(Config.MaxManaAt(2), result.State.Mana);
    }

    [Fact]
    public void Surplus_xp_carries_over() {
        var result = Service.GrantXp(At(1), Needed(1) + 7);

        Assert.Equal(2, result.State.Level);
        Assert.Equal(7L, result.State.Xp);
    }

    [Fact]
    public void Large_grant_chains_several_levels() {
        var result = Service.GrantXp(At(1), Needed(1) + Needed(2) + Needed(3) + 4);

        Assert.Equal(3, result.LevelsGained);
        Assert.Equal(4, result.State.Level);
        Assert.Equal(4L, result.State.Xp);
        Assert.Equal(Config.MaxManaAt(4), result.State.Mana);
    }

    [Fact]
    public void Existing_xp_counts_toward_the_threshold() {
        var result = Service.GrantXp(At(2, Needed(2) - 1), 1);

        Assert.Equal(3, result.State.Level);
        Assert.Equal(0L, result.State.Xp);
    }

    [Fact]
    public void Max_level_is_a_hard_cap_and_xp_is_discarded() {
        var max = Config.MaxLevel;

        var reached = Service.GrantXp(At(max - 1), Needed(max - 1) + 999);
        Assert.Equal(max, reached.State.Level);
        Assert.Equal(0L, reached.State.Xp);

        var beyond = Service.GrantXp(At(max, 0, 5), 1_000_000);
        Assert.Equal(0, beyond.LevelsGained);
        Assert.Equal(max, beyond.State.Level);
        Assert.Equal(0L, beyond.State.Xp);
        Assert.Equal(5, beyond.State.Mana);
    }

    [Fact]
    public void Huge_grant_reaches_max_level_without_overflow() {
        var result = Service.GrantXp(At(1, 10), long.MaxValue);

        Assert.Equal(Config.MaxLevel, result.State.Level);
        Assert.Equal(Config.MaxLevel - 1, result.LevelsGained);
        Assert.Equal(0L, result.State.Xp);
    }

    [Fact]
    public void Grant_preserves_reserved_ids() {
        var state = new NocState(NocState.CurrentV, 1, 0, 0, RaceId: 2, ClassId: 3, FactionId: 4);

        var result = Service.GrantXp(state, Needed(1));

        Assert.Equal(2, result.State.RaceId);
        Assert.Equal(3, result.State.ClassId);
        Assert.Equal(4, result.State.FactionId);
    }

    [Fact]
    public void Normalize_null_gives_a_fresh_level_one_character_with_full_mana() {
        var state = Service.Normalize(null);

        Assert.Equal(NocState.CreateDefault(Config.MaxManaAt(1)), state);
    }

    [Fact]
    public void Normalize_clamps_level_xp_and_mana() {
        Assert.Equal(1, Service.Normalize(At(-5)).Level);
        Assert.Equal(Config.MaxLevel, Service.Normalize(At(100_000)).Level);
        Assert.Equal(0L, Service.Normalize(At(2, -10)).Xp);
        Assert.Equal(0, Service.Normalize(At(1, 0, -9)).Mana);
        Assert.Equal(Config.MaxManaAt(1), Service.Normalize(At(1, 0, 99_999)).Mana);
    }

    [Fact]
    public void Normalize_converts_excess_xp_into_levels_without_refilling_mana() {
        var state = Service.Normalize(At(1, Needed(1) + 5, 12));

        Assert.Equal(2, state.Level);
        Assert.Equal(5L, state.Xp);
        Assert.Equal(12, state.Mana);
    }

    [Fact]
    public void Normalize_drops_xp_at_max_level_and_keeps_reserved_ids() {
        var state = Service.Normalize(new NocState(0, Config.MaxLevel, 500, 1, RaceId: 9));

        Assert.Equal(0L, state.Xp);
        Assert.Equal(NocState.CurrentV, state.V);
        Assert.Equal(9, state.RaceId);
    }

    [Fact]
    public void Normalize_is_idempotent() {
        var once = Service.Normalize(At(7, Needed(7) * 3, 1_000_000));

        Assert.Equal(once, Service.Normalize(once));
    }

    [Fact]
    public void Null_arguments_are_rejected() {
        Assert.Throws<ArgumentNullException>(() => new ProgressionService(null!));
        Assert.Throws<ArgumentNullException>(() => Service.GrantXp(null!, 1));
    }
}

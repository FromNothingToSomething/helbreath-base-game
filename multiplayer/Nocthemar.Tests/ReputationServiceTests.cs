using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T74: reputation values, tiers, kills (affiliation gate, victim and rivals), gain multiplier and load-time cleaning.</summary>
public class ReputationServiceTests {
    private const int ChantDeFer = 0;
    private const int Choeur = 1;
    private const int Meutes = 2;
    private const int Forgeurs = 3;

    private static ReputationService Service() => new(TestSupport.ShippedFactions());

    private static NocState Member(int? factionId = ChantDeFer, FactionRep[]? reputations = null) =>
        new(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: factionId, Reputations: reputations);

    [Fact]
    public void A_fresh_character_has_no_reputation_and_is_neutral() {
        var svc = Service();
        var state = Member();

        Assert.Equal(0, svc.ValueOf(state, Choeur));
        Assert.Equal(ReputationTier.Neutral, svc.TierOf(state, Choeur));
        Assert.Null(state.Reputations);
    }

    [Fact]
    public void Adjust_adds_and_keeps_the_entries_sorted_without_zero() {
        var svc = Service();

        var first = svc.Adjust(Member(), Forgeurs, 40);
        var second = svc.Adjust(first.State, ChantDeFer, -15);

        Assert.Equal(new[] { new FactionRep(ChantDeFer, -15), new FactionRep(Forgeurs, 40) }, second.State.Reputations);
        Assert.Equal(new[] { new ReputationChange(ChantDeFer, 0, -15) }, second.Changes.ToArray());
        Assert.Equal(-15, second.Changes[0].Delta);
    }

    [Fact]
    public void Returning_to_zero_removes_the_entry() {
        var svc = Service();

        var up = svc.Adjust(Member(), Meutes, 25);
        var back = svc.Adjust(up.State, Meutes, -25);

        Assert.Null(back.State.Reputations);
    }

    [Fact]
    public void Adjust_does_not_modify_its_input() {
        var svc = Service();
        var state = Member();

        svc.Adjust(state, Choeur, 100);

        Assert.Null(state.Reputations);
    }

    [Theory]
    [InlineData(15000, 10000)]
    [InlineData(-15000, -10000)]
    public void Values_stop_at_the_range(int delta, int expected) {
        var svc = Service();

        var result = svc.Adjust(Member(), Choeur, delta);

        Assert.Equal(expected, svc.ValueOf(result.State, Choeur));
        Assert.Equal(expected, result.Changes[0].After);
    }

    [Fact]
    public void A_move_blocked_by_the_range_changes_nothing() {
        var svc = Service();
        var capped = Member(reputations: [new FactionRep(Choeur, 10000)]);

        var result = svc.Adjust(capped, Choeur, 50);

        Assert.Same(capped, result.State);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void An_unknown_faction_or_a_null_move_changes_nothing() {
        var svc = Service();
        var state = Member();

        Assert.Same(state, svc.Adjust(state, 9, 10).State);
        Assert.Same(state, svc.Adjust(state, -1, 10).State);
        Assert.Same(state, svc.Adjust(state, Choeur, 0).State);
    }

    [Fact]
    public void The_gain_multiplier_scales_gains_only() {
        var svc = Service();

        var gain = svc.Adjust(Member(), Choeur, 10, gainMult: 1.5);
        var loss = svc.Adjust(Member(), Choeur, -10, gainMult: 1.5);
        var none = svc.Adjust(Member(), Choeur, 10, gainMult: 0.0);

        Assert.Equal(15, svc.ValueOf(gain.State, Choeur));
        Assert.Equal(-10, svc.ValueOf(loss.State, Choeur));
        Assert.Empty(none.Changes);
    }

    [Fact]
    public void A_character_without_a_faction_earns_nothing_from_a_kill() {
        var svc = Service();
        var state = Member(factionId: null);

        var result = svc.ApplyKill(state, Choeur);

        Assert.Same(state, result.State);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void A_kill_costs_the_monsters_faction_and_pays_its_rival() {
        var svc = Service();

        var result = svc.ApplyKill(Member(), Choeur);

        // killBase 5: the victim loses round(5 x 0.5) = 3 (away from zero), the rival (chant_de_fer) gains 5 x 1.0.
        Assert.Equal(-3, svc.ValueOf(result.State, Choeur));
        Assert.Equal(5, svc.ValueOf(result.State, ChantDeFer));
        Assert.Equal(0, svc.ValueOf(result.State, Meutes));
        Assert.Equal(0, svc.ValueOf(result.State, Forgeurs));
        Assert.Equal(2, result.Changes.Count);
    }

    [Fact]
    public void A_kill_uses_the_monsters_own_amount() {
        var svc = Service();

        var result = svc.ApplyKill(Member(), Forgeurs, repOnKill: 20);

        Assert.Equal(-10, svc.ValueOf(result.State, Forgeurs));
        Assert.Equal(20, svc.ValueOf(result.State, Meutes));
    }

    [Fact]
    public void The_gain_multiplier_applies_to_the_rivals_gain_not_to_the_loss() {
        var svc = Service();

        var result = svc.ApplyKill(Member(), Choeur, gainMult: 1.5);

        Assert.Equal(-3, svc.ValueOf(result.State, Choeur));
        Assert.Equal(8, svc.ValueOf(result.State, ChantDeFer)); // 5 x 1.5 = 7.5, rounded away from zero
    }

    [Fact]
    public void A_kill_of_an_unknown_faction_or_with_no_reputation_changes_nothing() {
        var svc = Service();
        var state = Member();

        Assert.Same(state, svc.ApplyKill(state, 9).State);
        Assert.Same(state, svc.ApplyKill(state, Choeur, repOnKill: 0).State);
    }

    [Fact]
    public void A_kill_accumulates_across_calls() {
        var svc = Service();
        var state = Member();

        for (var i = 0; i < 4; i++) {
            state = svc.ApplyKill(state, Choeur).State;
        }

        Assert.Equal(-12, svc.ValueOf(state, Choeur));
        Assert.Equal(20, svc.ValueOf(state, ChantDeFer));
    }

    [Fact]
    public void Sanitize_leaves_a_clean_block_untouched_in_content() {
        var svc = Service();
        var clean = Member(reputations: [new FactionRep(ChantDeFer, 5), new FactionRep(Forgeurs, -40)]);

        var result = svc.Sanitize(clean, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(clean.Reputations, result.Reputations);
        Assert.Equal(ChantDeFer, result.FactionId);
    }

    [Fact]
    public void Sanitize_drops_unknown_factions_and_clamps_values() {
        var svc = Service();
        var dirty = Member(reputations: [new FactionRep(7, 100), new FactionRep(Choeur, 99999), new FactionRep(Meutes, -99999)]);

        var result = svc.Sanitize(dirty, out var warnings);

        Assert.Equal(new[] { new FactionRep(Choeur, 10000), new FactionRep(Meutes, -10000) }, result.Reputations);
        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("faction 7 dropped"));
    }

    [Fact]
    public void Sanitize_keeps_the_last_of_a_duplicated_faction_sorts_and_drops_zeros() {
        var svc = Service();
        var dirty = Member(reputations: [new FactionRep(Forgeurs, 4), new FactionRep(Choeur, 9), new FactionRep(Forgeurs, 8), new FactionRep(Meutes, 0)]);

        var result = svc.Sanitize(dirty, out var warnings);

        Assert.Equal(new[] { new FactionRep(Choeur, 9), new FactionRep(Forgeurs, 8) }, result.Reputations);
        Assert.Single(warnings, w => w.Contains("listed twice"));
    }

    [Fact]
    public void Sanitize_empties_a_block_with_only_zeros_to_null() {
        var svc = Service();

        var result = svc.Sanitize(Member(reputations: [new FactionRep(Choeur, 0)]), out var warnings);

        Assert.Null(result.Reputations);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Sanitize_drops_an_unknown_affiliation_with_a_warning() {
        var svc = Service();

        var result = svc.Sanitize(Member(factionId: 12), out var warnings);

        Assert.Null(result.FactionId);
        Assert.Single(warnings, w => w.Contains("faction 12 dropped"));
    }

    [Fact]
    public void The_gain_multiplier_stat_starts_neutral_and_follows_modifiers() {
        var stats = new StatPipeline(TestSupport.Progression(), 1);

        Assert.Equal(1.0, stats.Get(StatId.ReputationGainMult));

        stats.Add(new StatModifier(StatId.ReputationGainMult, ModifierKind.PercentAdd, 0.25, ModifierSources.Faction(0)));

        Assert.Equal(1.25, stats.Get(StatId.ReputationGainMult));
    }
}

using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T77: joining a faction (once, from the join level), the perks of the standing with it, and their application to a player.</summary>
public class FactionServiceTests {
    private const int ChantDeFer = 0;
    private const int Choeur = 1;
    private const int Meutes = 2;

    private static FactionService Service() => new(new ReputationService(TestSupport.ShippedFactions()));

    private static NocState AtLevel(int level, int? factionId = null, FactionRep[]? reputations = null) =>
        new(NocState.CurrentV, level, 0, 0, RaceId: 0, FactionId: factionId, Reputations: reputations);

    private static GameWorldPlayer Player(int level = 10, int? factionId = null) {
        var player = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent());
        TestSupport.Restore(player, AtLevel(level, factionId));
        return player;
    }

    [Fact]
    public void Joining_sets_the_faction_and_changes_nothing_else() {
        var before = AtLevel(10, reputations: [new FactionRep(Choeur, 7)]);

        var result = Service().Join(before, Choeur);

        Assert.True(result.Ok);
        Assert.Null(result.Refusal);
        Assert.Equal(before with { FactionId = Choeur }, result.State);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(1)]
    public void Below_the_join_level_it_is_refused(int level) {
        var state = AtLevel(level);

        var result = Service().Join(state, Meutes);

        Assert.False(result.Ok);
        Assert.Equal("You must reach level 10 to join a faction.", result.Refusal);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void The_join_level_itself_is_enough() {
        Assert.True(Service().Join(AtLevel(10), ChantDeFer).Ok);
        Assert.True(Service().Join(AtLevel(100), ChantDeFer).Ok);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void An_unknown_faction_is_refused_before_anything_else(int factionId) {
        var result = Service().Join(AtLevel(1, factionId: ChantDeFer), factionId);

        Assert.Equal(FactionService.UnknownFaction, result.Refusal);
    }

    [Fact]
    public void The_choice_is_final_and_is_checked_before_the_level() {
        var state = AtLevel(1, factionId: Choeur);

        Assert.Equal(FactionService.AlreadyJoined, Service().Join(state, Choeur).Refusal);
        Assert.Equal(FactionService.AlreadyJoined, Service().Join(state, Meutes).Refusal);
        Assert.Same(state, Service().Join(state, Meutes).State);
    }

    [Fact]
    public void There_is_one_source_per_faction() {
        var sources = Service().SourceIds;

        Assert.Equal(4, sources.Count);
        Assert.Equal(4, sources.Distinct().Count());
    }

    [Fact]
    public void Without_a_faction_there_is_no_perk_whatever_the_reputation() {
        var state = AtLevel(20, reputations: [new FactionRep(ChantDeFer, 9000)]);

        Assert.Empty(Service().PerksOf(state));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(999, 0)]
    [InlineData(1000, 1)]
    [InlineData(3000, 2)]
    [InlineData(6000, 3)]
    [InlineData(10000, 3)]
    [InlineData(-5000, 0)]
    public void Perks_follow_the_tier_of_the_own_faction(int reputation, int count) {
        var state = AtLevel(20, ChantDeFer, [new FactionRep(ChantDeFer, reputation)]);

        Assert.Equal(count, Service().PerksOf(state).Length);
    }

    [Fact]
    public void Reputation_with_another_faction_gives_no_perk() {
        var state = AtLevel(20, ChantDeFer, [new FactionRep(Choeur, 9000)]);

        Assert.Empty(Service().PerksOf(state));
    }

    [Fact]
    public void A_player_below_the_level_cannot_join_and_keeps_no_faction() {
        var player = Player(level: 9);

        Assert.Equal("You must reach level 10 to join a faction.", player.TryJoinFaction(Choeur));
        Assert.Null(player.FactionId);
    }

    [Fact]
    public void A_player_joins_once() {
        var player = Player();

        Assert.Null(player.TryJoinFaction(Choeur));
        Assert.Equal(Choeur, player.FactionId);
        Assert.Equal(FactionService.AlreadyJoined, player.TryJoinFaction(Meutes));
        Assert.Equal(Choeur, player.FactionId);
    }

    [Fact]
    public void A_world_without_a_faction_catalog_refuses_to_join() {
        var player = TestSupport.NewPlayer();

        Assert.Equal("Factions are not available.", player.TryJoinFaction(Choeur));
    }

    [Fact]
    public void Perks_appear_and_disappear_with_the_tier() {
        var player = Player(factionId: ChantDeFer);
        Assert.Equal(0.0, player.Stats.Get(StatId.ResistMagic));

        Assert.True(player.TryAdjustReputation(ChantDeFer, 1000));
        Assert.Equal(0.03, player.Stats.Get(StatId.ResistMagic));
        Assert.Equal(1.0, player.Stats.Get(StatId.ArmorMult));

        Assert.True(player.TryAdjustReputation(ChantDeFer, 2000));
        Assert.Equal(0.03, player.Stats.Get(StatId.ResistMagic));
        Assert.Equal(1.05, player.Stats.Get(StatId.ArmorMult));

        Assert.True(player.TryAdjustReputation(ChantDeFer, -5000));
        Assert.Equal(0.0, player.Stats.Get(StatId.ResistMagic));
        Assert.Equal(1.0, player.Stats.Get(StatId.ArmorMult));
    }

    [Fact]
    public void Joining_applies_the_perks_of_the_reputation_already_earned() {
        var player = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent());
        TestSupport.Restore(player, AtLevel(10, reputations: [new FactionRep(Choeur, 1500)]));
        Assert.Equal(0.0, player.Stats.Get(StatId.ResistDeath));

        Assert.Null(player.TryJoinFaction(Choeur));

        Assert.Equal(0.05, player.Stats.Get(StatId.ResistDeath));
    }

    [Fact]
    public void Reputation_with_another_faction_does_not_touch_the_stats() {
        var player = Player(factionId: ChantDeFer);

        player.TryAdjustReputation(Choeur, 9000);

        Assert.Equal(0.0, player.Stats.Get(StatId.ResistDeath));
        Assert.Equal(1.0, player.Stats.Get(StatId.MagicDamageMult));
    }

    [Fact]
    public void The_exalted_perk_raises_max_hp() {
        var player = Player(factionId: ChantDeFer);
        var before = player.Stats.MaxHp;

        player.TryAdjustReputation(ChantDeFer, 6000);

        Assert.True(player.Stats.MaxHp > before);
        Assert.Equal(player.Stats.MaxHp, player.MaxHp);
    }

    [Fact]
    public void A_kill_that_crosses_a_tier_applies_the_perk() {
        var player = Player(factionId: ChantDeFer);

        player.RegisterFactionKill(Choeur, 1000); // rival of the own faction: +1000 -> Friendly

        Assert.Equal(1000, player.ReputationWith(ChantDeFer));
        Assert.Equal(0.03, player.Stats.Get(StatId.ResistMagic));
    }

    [Fact]
    public void Perks_come_back_when_the_save_is_loaded() {
        var first = Player(factionId: Choeur);
        first.TryAdjustReputation(Choeur, 3500);

        var second = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent());
        second.ApplyPersistedState(first.CreatePersistenceState("aresden"));

        Assert.Equal(Choeur, second.FactionId);
        Assert.Equal(0.05, second.Stats.Get(StatId.ResistDeath));
        Assert.Equal(1.05, second.Stats.Get(StatId.MagicDamageMult));
    }

    [Fact]
    public void A_debug_adjustment_of_an_unknown_faction_is_refused() {
        var player = Player();

        Assert.False(player.TryAdjustReputation(9, 100));
        Assert.False(TestSupport.NewPlayer().TryAdjustReputation(Choeur, 100));
    }

    [Fact]
    public void The_stat_state_is_sent_with_the_next_update_after_a_join() {
        var sent = new List<Mmorpg.Network.ServerMessage>();
        var player = TestSupport.NewPlayer(sent, null, TestSupport.ShippedContent());
        TestSupport.Restore(player, AtLevel(10));
        player.TryFlushNocUpdates();
        sent.Clear();

        player.TryJoinFaction(Meutes);

        Assert.True(player.TryFlushNocUpdates());
        Assert.Contains(sent, m => m.PayloadCase == Mmorpg.Network.ServerMessage.PayloadOneofCase.NocStateUpdated);
    }
}

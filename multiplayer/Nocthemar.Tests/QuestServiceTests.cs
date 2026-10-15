using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T82: quest rules on a state: accepting (and its refusals in order), progress, turning in and cleaning a loaded block.</summary>
public class QuestServiceTests {
    private const int ChantDeFer = 0;
    private const int Choeur = 1;

    // Shipped quests: 0 / 1 / 2 = chant_de_fer Neutral kill 51 x10 / Friendly deliver 5100 x3 / Honored kill 52 x5; 3..5 = choeur; ...
    private static QuestService Service() => new(TestSupport.ShippedQuests(), new ReputationService(TestSupport.ShippedFactions()));

    private static NocState Member(int? faction = ChantDeFer, int level = 20, FactionRep[]? rep = null, QuestEntry[]? quests = null, int[]? done = null) =>
        new(NocState.CurrentV, level, 0, 0, RaceId: 0, FactionId: faction, Reputations: rep, Quests: quests, QuestsDone: done);

    private static QuestService Small(int maxActive) {
        // Not an interpolated raw string: its content has "}}" runs, which would close a hole.
        const string template = """
            {"schemaVersion":1,"rules":{"maxActive":MAX,"bagSlots":40},"quests":[
            {"id":0,"key":"a","name":"A","factionId":0,"minTier":"Neutral","kind":"kill","monsterId":51,"count":2,"rewards":{"xp":1}},
            {"id":1,"key":"b","name":"B","factionId":0,"minTier":"Neutral","kind":"kill","monsterId":61,"count":2,"rewards":{"xp":1}},
            {"id":2,"key":"c","name":"C","factionId":0,"minTier":"Neutral","kind":"kill","monsterId":52,"count":2,"rewards":{"xp":1}}]}
            """;
        var json = template.Replace("MAX", maxActive.ToString());
        return new QuestService(QuestConfig.Parse(json, TestSupport.ShippedFactions()), new ReputationService(TestSupport.ShippedFactions()));
    }

    [Fact]
    public void Accepting_adds_the_quest_with_no_progress() {
        var result = Service().Accept(Member(), 0);

        Assert.True(result.Ok);
        Assert.Null(result.Refusal);
        Assert.Equal(0, result.Quest!.Id);
        Assert.Equal(new[] { new QuestEntry(0, 0) }, result.State.Quests);
        Assert.Null(result.State.QuestsDone);
    }

    [Fact]
    public void Active_quests_stay_sorted_by_id() {
        var service = Service();
        var state = service.Accept(Member(rep: [new FactionRep(ChantDeFer, 3000)]), 2).State;

        state = service.Accept(state, 0).State;
        state = service.Accept(state, 1).State;

        Assert.Equal(new[] { 0, 1, 2 }, state.Quests!.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Refusals_follow_the_documented_order() {
        var service = Service();
        var doneAndActive = Member(quests: [new QuestEntry(0, 0)], done: [0]);

        Assert.Equal(QuestService.UnknownQuest, service.Accept(Member(null), 99).Refusal);
        Assert.Equal(QuestService.UnknownQuest, service.Accept(Member(), -1).Refusal);
        Assert.Equal(QuestService.NoFaction, service.Accept(Member(null), 0).Refusal);
        Assert.Equal(QuestService.OtherFaction, service.Accept(Member(), 3).Refusal);
        Assert.Equal(QuestService.AlreadyDone, service.Accept(doneAndActive, 0).Refusal);
        Assert.Equal(QuestService.AlreadyActive, service.Accept(Member(quests: [new QuestEntry(0, 3)]), 0).Refusal);
        Assert.Equal(QuestService.TierTooLow(ReputationTier.Friendly), service.Accept(Member(), 1).Refusal);
    }

    [Fact]
    public void A_refusal_leaves_the_state_untouched() {
        var state = Member();

        var result = Service().Accept(state, 1);

        Assert.False(result.Ok);
        Assert.Same(state, result.State);
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    public void The_tier_is_checked_at_its_exact_threshold(int reputation, bool accepted) {
        var result = Service().Accept(Member(rep: [new FactionRep(ChantDeFer, reputation)]), 1);

        Assert.Equal(accepted, result.Ok);
    }

    [Fact]
    public void The_reputation_with_another_faction_does_not_open_a_quest() {
        var result = Service().Accept(Member(rep: [new FactionRep(Choeur, 9000)]), 1);

        Assert.Equal(QuestService.TierTooLow(ReputationTier.Friendly), result.Refusal);
    }

    [Fact]
    public void The_active_limit_is_enforced_last_and_a_free_slot_comes_back_after_abandoning() {
        var service = Small(2);
        var state = service.Accept(service.Accept(Member(), 0).State, 1).State;

        var refused = service.Accept(state, 2);
        Assert.Equal(QuestService.TooManyQuests(2), refused.Refusal);

        var freed = service.Abandon(state, 0).State;
        Assert.True(service.Accept(freed, 2).Ok);
    }

    [Fact]
    public void The_shipped_limit_is_five() => Assert.Equal(5, Service().Config.MaxActive);

    [Fact]
    public void Abandoning_removes_the_quest_and_it_can_be_taken_again() {
        var service = Service();
        var state = service.Accept(Member(), 0).State;

        var abandoned = service.Abandon(state, 0);

        Assert.True(abandoned.Ok);
        Assert.Null(abandoned.State.Quests);
        Assert.Null(abandoned.State.QuestsDone);
        Assert.True(service.Accept(abandoned.State, 0).Ok);
        Assert.Equal(QuestService.NotActive, service.Abandon(abandoned.State, 0).Refusal);
        Assert.Equal(QuestService.UnknownQuest, service.Abandon(state, 50).Refusal);
    }

    [Fact]
    public void A_kill_counts_for_the_quests_that_ask_for_that_monster_only() {
        var service = Service();
        var state = Member(rep: [new FactionRep(ChantDeFer, 3000)]);
        state = service.Accept(service.Accept(service.Accept(state, 0).State, 1).State, 2).State;

        var after = service.RecordKill(state, 51);

        Assert.Equal(new[] { new QuestEntry(0, 1), new QuestEntry(1, 0), new QuestEntry(2, 0) }, after.Quests);
        Assert.Same(state, service.RecordKill(state, 77));
        Assert.Same(state, service.RecordKill(Member(), 51)); // no quest at all
    }

    [Fact]
    public void Kill_progress_stops_at_the_count() {
        var service = Service();
        var state = service.Accept(Member(), 0).State;

        for (var i = 0; i < 15; i++) {
            state = service.RecordKill(state, 51);
        }

        Assert.Equal(new[] { new QuestEntry(0, 10) }, state.Quests);
        Assert.Same(state, service.RecordKill(state, 51));
        Assert.True(service.IsComplete(state, 0));
    }

    [Fact]
    public void Deliver_progress_follows_the_bag_and_is_capped() {
        var service = Service();
        var state = service.Accept(Member(rep: [new FactionRep(ChantDeFer, 1000)]), 1).State;
        var bag = 2;

        Assert.Equal(new[] { new QuestEntry(1, 2) }, service.SyncDeliver(state, id => id == 5100 ? bag : 0).Quests);
        bag = 50;
        var full = service.SyncDeliver(state, id => id == 5100 ? bag : 0);
        Assert.Equal(new[] { new QuestEntry(1, 3) }, full.Quests);
        Assert.Same(full, service.SyncDeliver(full, id => id == 5100 ? bag : 0));
        bag = 1; // items dropped: progress goes down too
        Assert.Equal(new[] { new QuestEntry(1, 1) }, service.SyncDeliver(full, id => id == 5100 ? bag : 0).Quests);
    }

    [Fact]
    public void Sync_does_not_touch_kill_quests() {
        var service = Service();
        var state = service.Accept(Member(), 0).State;

        Assert.Same(state, service.SyncDeliver(state, _ => 99));
    }

    [Fact]
    public void Turning_in_needs_an_active_complete_quest() {
        var service = Service();
        var active = service.Accept(Member(), 0).State;

        Assert.Equal(QuestService.UnknownQuest, service.CheckTurnIn(active, 99));
        Assert.Equal(QuestService.NotActive, service.CheckTurnIn(Member(), 0));
        Assert.Equal(QuestService.NotComplete, service.CheckTurnIn(active, 0));
        Assert.Equal(QuestService.NotComplete, service.TurnIn(active, 0).Refusal);
        Assert.Null(service.CheckTurnIn(Member(quests: [new QuestEntry(0, 10)]), 0));
    }

    [Fact]
    public void Turning_in_records_the_quest_and_pays_the_reputation() {
        var service = Service();
        var state = Member(quests: [new QuestEntry(0, 10), new QuestEntry(2, 1)], done: [5], rep: [new FactionRep(ChantDeFer, 40)]);

        var result = service.TurnIn(state, 0);

        Assert.True(result.Ok);
        Assert.Equal(new[] { new QuestEntry(2, 1) }, result.State.Quests);
        Assert.Equal(new[] { 0, 5 }, result.State.QuestsDone);
        Assert.Equal(140, new ReputationService(TestSupport.ShippedFactions()).ValueOf(result.State, ChantDeFer));
        var change = Assert.Single(result.Changes);
        Assert.Equal((ChantDeFer, 40, 140), (change.FactionId, change.Before, change.After));
        Assert.Equal(0, result.Quest!.Id);
    }

    [Fact]
    public void The_reputation_reward_is_scaled_by_the_gain_multiplier_and_never_leaves_the_range() {
        var service = Service();
        var reputation = new ReputationService(TestSupport.ShippedFactions());
        var done = Member(quests: [new QuestEntry(0, 10)]);

        Assert.Equal(150, reputation.ValueOf(service.TurnIn(done, 0, 1.5).State, ChantDeFer));

        var capped = Member(quests: [new QuestEntry(0, 10)], rep: [new FactionRep(ChantDeFer, 9990)]);
        Assert.Equal(10000, reputation.ValueOf(service.TurnIn(capped, 0).State, ChantDeFer));
    }

    [Fact]
    public void A_quest_cannot_be_turned_in_twice() {
        var service = Service();
        var state = service.TurnIn(Member(quests: [new QuestEntry(0, 10)]), 0).State;

        Assert.Equal(QuestService.NotActive, service.TurnIn(state, 0).Refusal);
        Assert.Equal(QuestService.AlreadyDone, service.Accept(state, 0).Refusal);
    }

    [Fact]
    public void Sanitize_cleans_a_loaded_block_and_reports_every_change() {
        var state = Member(
            quests: [new QuestEntry(2, 99), new QuestEntry(0, -4), new QuestEntry(0, 5), new QuestEntry(77, 1), new QuestEntry(4, 1)],
            done: [4, 4, 88, 6, 1]);

        var clean = Service().Sanitize(state, out var warnings);

        // quest 4 is both active and done -> dropped from the active list; 88 unknown; 4 twice
        Assert.Equal(new[] { new QuestEntry(0, 0), new QuestEntry(2, 5) }, clean.Quests);
        Assert.Equal(new[] { 1, 4, 6 }, clean.QuestsDone);
        Assert.Equal(7, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("completed quest 4 listed twice"));
        Assert.Contains(warnings, w => w.Contains("completed quest 88 dropped"));
        Assert.Contains(warnings, w => w.Contains("quest 4 dropped: already completed"));
        Assert.Contains(warnings, w => w.Contains("quest 77 dropped"));
        Assert.Contains(warnings, w => w.Contains("quest 0 listed twice"));
        Assert.Contains(warnings, w => w.Contains("quest 2 progress set to 5"));
        Assert.Contains(warnings, w => w.Contains("quest 0 progress set to 0"));
    }

    [Fact]
    public void Sanitize_keeps_a_clean_block_and_turns_empty_lists_into_null() {
        var service = Service();
        var clean = Member(quests: [new QuestEntry(0, 3)], done: [1]);

        Assert.Equal(clean, service.Sanitize(clean, out var none));
        Assert.Empty(none);

        var empty = service.Sanitize(Member(quests: [], done: []), out _);
        Assert.Null(empty.Quests);
        Assert.Null(empty.QuestsDone);
    }

    [Fact]
    public void Sanitize_drops_the_surplus_beyond_the_active_limit() {
        var service = Small(2);

        var clean = service.Sanitize(Member(quests: [new QuestEntry(0, 1), new QuestEntry(1, 1), new QuestEntry(2, 1)]), out var warnings);

        Assert.Equal(new[] { 0, 1 }, clean.Quests!.Select(e => e.Id).ToArray());
        Assert.Contains(warnings, w => w.Contains("quest 2 dropped: more than 2"));
    }

    [Fact]
    public void Quest_fields_round_trip_through_json_and_an_older_block_reads_as_none() {
        var original = Member(quests: [new QuestEntry(0, 4), new QuestEntry(2, 1)], done: [1, 5]);

        var back = System.Text.Json.JsonSerializer.Deserialize<NocState>(System.Text.Json.JsonSerializer.Serialize(original))!;
        var older = System.Text.Json.JsonSerializer.Deserialize<NocState>("""{"V":4,"Level":9,"Xp":3,"Mana":4,"FactionId":1,"Reputations":[{"Id":1,"Value":30}]}""")!;

        Assert.Equal(new[] { new QuestEntry(0, 4), new QuestEntry(2, 1) }, back.Quests);
        Assert.Equal(new[] { 1, 5 }, back.QuestsDone);
        Assert.Null(older.Quests);
        Assert.Null(older.QuestsDone);
        Assert.Equal(1, older.FactionId);
    }
}

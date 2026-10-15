using Google.Protobuf;
using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Net;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T84: quests on the wire (requests 109-111, snapshot 15-16, catalog 12-13) and their handling by the dispatcher.</summary>
public class QuestProtocolTests {
    private const int ChantDeFer = 0;
    private const int Hammer = 1;

    private static NocDispatcher Dispatcher(Func<GameWorldPlayer, int, bool>? master = null, bool debug = false) =>
        new(debug, _ => { }, null, master ?? ((_, _) => true));

    private static (GameWorldPlayer Player, List<ServerMessage> Sent) NewPlayer(int? faction = ChantDeFer, FactionRep[]? rep = null, QuestEntry[]? quests = null, int[]? done = null) {
        var sent = new List<ServerMessage>();
        var player = TestSupport.NewPlayer(sent, null, TestSupport.ShippedContent(), TestSupport.ShippedItems());
        TestSupport.Restore(player, new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: faction, Reputations: rep, Quests: quests, QuestsDone: done));
        return (player, sent);
    }

    private static ClientMessage Accept(int id) => new() { NocQuestAcceptRequest = new NocQuestAcceptRequest { QuestId = id } };

    private static ClientMessage Abandon(int id) => new() { NocQuestAbandonRequest = new NocQuestAbandonRequest { QuestId = id } };

    private static ClientMessage TurnIn(int id) => new() { NocQuestTurnInRequest = new NocQuestTurnInRequest { QuestId = id } };

    private static string[] Refusals(List<ServerMessage> sent) =>
        [.. sent.Where(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocActionRefused).Select(m => m.NocActionRefused.Reason)];

    private static NocSnapshot State(GameWorldPlayer player, List<ServerMessage> sent) {
        sent.Clear();
        Assert.True(player.TryFlushNocUpdates());
        return sent.Last(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated).NocStateUpdated.State;
    }

    [Fact]
    public void Accept_near_the_factions_master_adds_the_quest_and_the_snapshot_shows_it() {
        var (player, sent) = NewPlayer();

        Assert.True(Dispatcher().Handle(player, Accept(0)));

        Assert.Empty(Refusals(sent));
        var state = State(player, sent);
        Assert.Equal(new[] { (0, 0) }, state.Quests.Select(q => (q.QuestId, q.Progress)).ToArray());
        Assert.Empty(state.QuestsDone);
    }

    [Fact]
    public void The_master_asked_for_is_the_one_of_the_quests_faction() {
        var asked = new List<int>();
        var (player, _) = NewPlayer();

        Dispatcher((_, faction) => { asked.Add(faction); return true; }).Handle(player, Accept(0));
        Dispatcher((_, faction) => { asked.Add(faction); return true; }).Handle(player, TurnIn(0));

        Assert.Equal(new[] { 0, 0 }, asked);
    }

    [Fact]
    public void Refusals_come_in_the_documented_order() {
        var (player, sent) = NewPlayer();
        var (dead, deadSent) = NewPlayer();
        var (loner, lonerSent) = NewPlayer(faction: null);
        dead.ApplyDamage(dead.MaxHp);

        Dispatcher().Handle(dead, Accept(99)); // dead comes before unknown
        Dispatcher().Handle(player, Accept(99));
        Dispatcher((_, _) => false).Handle(player, Accept(0)); // out of range comes before the rules
        Dispatcher((_, _) => false).Handle(loner, Accept(0));
        Dispatcher().Handle(loner, Accept(0)); // in range: now the rules say no faction

        Assert.Equal(new[] { "You are dead." }, Refusals(deadSent));
        Assert.Equal(new[] { QuestService.UnknownQuest, QuestService.NoMasterInRange }, Refusals(sent));
        Assert.Equal(new[] { QuestService.NoMasterInRange, QuestService.NoFaction }, Refusals(lonerSent));
        Assert.Empty(player.ActiveQuests);
    }

    [Fact]
    public void The_service_rules_are_answered_as_refusals() {
        var (player, sent) = NewPlayer(done: [0]);
        var dispatcher = Dispatcher();

        dispatcher.Handle(player, Accept(0)); // done
        dispatcher.Handle(player, Accept(3)); // other faction
        dispatcher.Handle(player, Accept(1)); // tier
        dispatcher.Handle(player, TurnIn(2)); // not active

        Assert.Equal(
            new[] { QuestService.AlreadyDone, QuestService.OtherFaction, QuestService.TierTooLow(ReputationTier.Friendly), QuestService.NotActive },
            Refusals(sent));
    }

    [Fact]
    public void Abandon_needs_no_master() {
        var (player, sent) = NewPlayer(quests: [new QuestEntry(0, 3)]);

        Dispatcher((_, _) => false).Handle(player, Abandon(0));
        Dispatcher((_, _) => false).Handle(player, Abandon(0));

        Assert.Empty(player.ActiveQuests);
        Assert.Equal(new[] { QuestService.NotActive }, Refusals(sent));
    }

    [Fact]
    public void Turning_in_sends_the_bag_changes_and_the_snapshot_shows_the_quest_done() {
        var (player, sent) = NewPlayer(quests: [new QuestEntry(0, 10)]);
        sent.Clear();

        Dispatcher().Handle(player, TurnIn(0));

        Assert.Empty(Refusals(sent));
        Assert.Single(sent, m => m.PayloadCase == ServerMessage.PayloadOneofCase.ItemAddedToBag);
        var state = State(player, sent);
        Assert.Empty(state.Quests);
        Assert.Equal(new[] { 0 }, state.QuestsDone);
        Assert.Equal(new[] { (ChantDeFer, 100) }, state.Reputations.Select(r => (r.FactionId, r.Value)).ToArray());
        Assert.Equal(200, state.Xp);
    }

    [Fact]
    public void A_deliver_turn_in_sends_the_removal_before_the_reward() {
        var (player, sent) = NewPlayer(rep: [new FactionRep(ChantDeFer, 1000)], quests: [new QuestEntry(1, 0)]);
        Assert.True(player.InventoryManager.TryCreateItems(5100, 3, out _));
        sent.Clear();

        Dispatcher().Handle(player, TurnIn(1));

        Assert.Empty(Refusals(sent));
        var bag = sent.Where(m => m.PayloadCase is ServerMessage.PayloadOneofCase.ItemRemovedFromBag or ServerMessage.PayloadOneofCase.ItemAddedToBag).Select(m => m.PayloadCase).ToArray();
        Assert.Equal(new[] { ServerMessage.PayloadOneofCase.ItemRemovedFromBag, ServerMessage.PayloadOneofCase.ItemAddedToBag }, bag);
    }

    [Fact]
    public void A_full_bag_is_a_refusal_and_sends_no_bag_change() {
        var (player, sent) = NewPlayer(quests: [new QuestEntry(0, 10)]);
        Assert.True(player.InventoryManager.TryCreateItems(Hammer, 40, out _));
        sent.Clear();

        Dispatcher().Handle(player, TurnIn(0));

        Assert.Equal(new[] { QuestService.BagFull }, Refusals(sent));
        Assert.DoesNotContain(sent, m => m.PayloadCase is ServerMessage.PayloadOneofCase.ItemAddedToBag or ServerMessage.PayloadOneofCase.ItemRemovedFromBag);
        Assert.Equal(new[] { new QuestEntry(0, 10) }, player.ActiveQuests);
    }

    [Fact]
    public void A_dispatcher_without_a_master_locator_refuses_accept_and_turn_in() {
        var (player, sent) = NewPlayer(quests: [new QuestEntry(0, 10)]);

        var dispatcher = new NocDispatcher(false, _ => { });
        dispatcher.Handle(player, Accept(1));
        dispatcher.Handle(player, TurnIn(0));

        Assert.Equal(new[] { QuestService.NoMasterInRange, QuestService.NoMasterInRange }, Refusals(sent));
    }

    [Fact]
    public void The_catalog_lists_the_quests_with_their_rewards() {
        var catalog = TestSupport.ShippedContent().Catalog;
        var shipped = TestSupport.ShippedQuests();

        Assert.Equal(shipped.Count, catalog.Quests.Count);
        Assert.Equal(shipped.MaxActive, catalog.QuestMaxActive);
        foreach (var info in catalog.Quests) {
            var source = shipped.Quests[info.Id];
            Assert.Equal((source.Key, source.Name, source.FactionId, (int)source.MinTier, (int)source.Kind, source.TargetId, source.Count), (info.Key, info.Name, info.FactionId, info.MinTier, info.Kind, info.TargetId, info.Count));
            Assert.Equal((source.RewardXp, source.RewardRep, source.Desc), (info.RewardXp, info.RewardRep, info.Desc));
            Assert.Equal(source.RewardItems.Select(i => (i.ItemId, i.Quantity)).ToArray(), info.RewardItems.Select(i => (i.ItemId, i.Quantity)).ToArray());
        }
    }

    [Fact]
    public void Without_a_quest_catalog_nothing_quest_related_is_sent() {
        var classes = TestSupport.ShippedClasses();
        var skills = TestSupport.ShippedSkills();
        var catalog = NocCatalogBuilder.Build(classes, skills, new SkillService(classes, skills, TestSupport.Progression()), null, TestSupport.ShippedFactions());

        Assert.Empty(catalog.Quests);
        Assert.Equal(0, catalog.QuestMaxActive);
        Assert.NotEmpty(catalog.Factions);
    }

    [Fact]
    public void The_snapshot_lists_quests_and_done_ids_ascending() {
        var (player, sent) = NewPlayer(quests: [new QuestEntry(2, 1), new QuestEntry(0, 4)], done: [5, 1]);

        var state = State(player, sent);

        Assert.Equal(new[] { (0, 4), (2, 1) }, state.Quests.Select(q => (q.QuestId, q.Progress)).ToArray());
        Assert.Equal(new[] { 1, 5 }, state.QuestsDone);
    }

    [Fact]
    public void The_new_fields_have_their_frozen_numbers() {
        Assert.Equal(109, ClientMessage.Descriptor.FindFieldByName("noc_quest_accept_request").FieldNumber);
        Assert.Equal(110, ClientMessage.Descriptor.FindFieldByName("noc_quest_abandon_request").FieldNumber);
        Assert.Equal(111, ClientMessage.Descriptor.FindFieldByName("noc_quest_turn_in_request").FieldNumber);
        Assert.Equal(15, NocSnapshot.Descriptor.FindFieldByName("quests").FieldNumber);
        Assert.Equal(16, NocSnapshot.Descriptor.FindFieldByName("quests_done").FieldNumber);
        Assert.Equal(12, NocCatalog.Descriptor.FindFieldByName("quests").FieldNumber);
        Assert.Equal(13, NocCatalog.Descriptor.FindFieldByName("quest_max_active").FieldNumber);
    }

    [Fact]
    public void The_requests_round_trip_on_the_wire() {
        var bytes = TurnIn(7).ToByteArray();

        Assert.Equal(0xFA, bytes[0]); // (111 << 3) | 2 = 890 -> FA 06
        Assert.Equal(0x06, bytes[1]);
        Assert.Equal(7, ClientMessage.Parser.ParseFrom(bytes).NocQuestTurnInRequest.QuestId);
        Assert.Equal(3, ClientMessage.Parser.ParseFrom(Accept(3).ToByteArray()).NocQuestAcceptRequest.QuestId);
        Assert.Equal(4, ClientMessage.Parser.ParseFrom(Abandon(4).ToByteArray()).NocQuestAbandonRequest.QuestId);
    }

    [Fact]
    public void The_dispatcher_owns_the_three_quest_payloads() {
        Assert.True(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.NocQuestAcceptRequest));
        Assert.True(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.NocQuestAbandonRequest));
        Assert.True(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.NocQuestTurnInRequest));
    }
}

using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Skills;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T82: quests on a player: accept, kill and bag progress, all-or-nothing turn-in (bag room, experience, reputation, items), reload clean-up.</summary>
public class QuestPlayerTests {
    private const int ChantDeFer = 0;
    private const int RedPotion = 36;
    private const int Gold = 35;
    private const int Vial = 5100;
    private const int Hammer = 1; // not stackable

    private static (GameWorldPlayer Player, List<ServerMessage> Sent) NewPlayer(int? faction = ChantDeFer, FactionRep[]? rep = null, NocState? state = null) {
        var sent = new List<ServerMessage>();
        var player = TestSupport.NewPlayer(sent, null, TestSupport.ShippedContent(), TestSupport.ShippedItems());
        TestSupport.Restore(player, state ?? new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: faction, Reputations: rep));
        return (player, sent);
    }

    private static void Kills(GameWorldPlayer player, int monsterId, int times) {
        for (var i = 0; i < times; i++) {
            player.RegisterQuestKill(monsterId);
        }
    }

    private static void Give(GameWorldPlayer player, int itemId, int quantity) => Assert.True(player.InventoryManager.TryCreateItems(itemId, quantity, out _));

    private static NocSnapshot Flushed(GameWorldPlayer player, List<ServerMessage> sent) {
        sent.Clear();
        Assert.True(player.TryFlushNocUpdates());
        return sent.Last(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated).NocStateUpdated.State;
    }

    [Fact]
    public void Accepting_marks_the_state_and_the_quest_is_listed() {
        var (player, sent) = NewPlayer();

        Assert.Null(player.TryAcceptQuest(0));

        Assert.Equal(new[] { new QuestEntry(0, 0) }, player.ActiveQuests);
        Assert.True(player.TryGetQuest(0, out var quest));
        Assert.Equal(ChantDeFer, quest.FactionId);
        Assert.True(player.TryFlushNocUpdates());
        Assert.Contains(sent, m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated);
    }

    [Fact]
    public void Refusals_are_returned_as_text_and_change_nothing() {
        var (player, _) = NewPlayer();
        var (loner, _) = NewPlayer(faction: null);

        Assert.Equal(QuestService.UnknownQuest, player.TryAcceptQuest(99));
        Assert.Equal(QuestService.OtherFaction, player.TryAcceptQuest(3));
        Assert.Equal(QuestService.NoFaction, loner.TryAcceptQuest(0));
        Assert.Equal(QuestService.NotActive, player.TryAbandonQuest(0));
        Assert.Empty(player.ActiveQuests);
        Assert.Empty(loner.ActiveQuests);
        Assert.False(player.TryGetQuest(99, out _));
    }

    [Fact]
    public void Abandoning_gives_the_quest_up() {
        var (player, _) = NewPlayer();
        player.TryAcceptQuest(0);

        Assert.Null(player.TryAbandonQuest(0));

        Assert.Empty(player.ActiveQuests);
        Assert.Null(player.TryAcceptQuest(0));
    }

    [Fact]
    public void A_kill_quest_goes_from_accepted_to_turned_in_with_every_reward() {
        var (player, sent) = NewPlayer();
        player.TryAcceptQuest(0);
        Kills(player, 61, 4); // another monster: nothing
        Kills(player, 51, 9);
        sent.Clear();

        Assert.Equal(QuestService.NotComplete, player.TryTurnInQuest(0, out var none));
        Assert.Empty(none);
        Kills(player, 51, 1);
        Assert.Null(player.TryTurnInQuest(0, out var inventory));

        Assert.Empty(player.ActiveQuests);
        Assert.Equal(new[] { 0 }, player.CompletedQuestIds);
        Assert.Equal(100, player.ReputationWith(ChantDeFer));
        Assert.Equal(200, player.CreateNocState().Xp);
        Assert.Equal(5, player.InventoryManager.CountItem(RedPotion));
        Assert.Single(inventory);
        Assert.Equal(5, inventory[0].AddedToBag.Sum(i => i.Quantity));
        Assert.Equal(QuestService.NotActive, player.TryTurnInQuest(0, out _));
        Assert.Equal(QuestService.AlreadyDone, player.TryAcceptQuest(0));
    }

    [Fact]
    public void Kills_only_count_while_the_quest_is_active() {
        var (player, _) = NewPlayer();

        Kills(player, 51, 3);
        player.TryAcceptQuest(0);
        Kills(player, 51, 2);

        Assert.Equal(new[] { new QuestEntry(0, 2) }, player.ActiveQuests);
    }

    [Fact]
    public void A_deliver_quest_follows_the_bag_and_takes_exactly_the_requested_items() {
        var (player, sent) = NewPlayer(rep: [new FactionRep(ChantDeFer, 1000)]);
        Give(player, Vial, 2);
        Assert.Null(player.TryAcceptQuest(1));
        Assert.Equal(new[] { new QuestEntry(1, 2) }, player.ActiveQuests); // the bag is counted at once

        Give(player, Vial, 4);
        Assert.Equal(3, Flushed(player, sent).Quests.Single().Progress);

        player.InventoryManager.TryRemoveItemsById(Vial, 5, out _);
        Assert.Equal(1, Flushed(player, sent).Quests.Single().Progress);
        Assert.Equal(QuestService.NotComplete, player.TryTurnInQuest(1, out _));

        Give(player, Vial, 4); // 5 in the bag
        Assert.Null(player.TryTurnInQuest(1, out var inventory));

        Assert.Equal(2, player.InventoryManager.CountItem(Vial));
        Assert.Equal(50, player.InventoryManager.CountItem(Gold));
        Assert.Equal(1150, player.ReputationWith(ChantDeFer));
        Assert.Equal(400, player.CreateNocState().Xp);
        Assert.Equal(2, inventory.Count);
    }

    [Fact]
    public void A_full_bag_refuses_the_turn_in_and_nothing_at_all_changes() {
        var (player, _) = NewPlayer();
        player.TryAcceptQuest(0);
        Kills(player, 51, 10);
        Give(player, Hammer, 40); // 40 rows: the potions would need a 41st

        Assert.Equal(QuestService.BagFull, player.TryTurnInQuest(0, out var inventory));

        Assert.Empty(inventory);
        Assert.Equal(new[] { new QuestEntry(0, 10) }, player.ActiveQuests);
        Assert.Empty(player.CompletedQuestIds);
        Assert.Equal(0, player.ReputationWith(ChantDeFer));
        Assert.Equal(0, player.CreateNocState().Xp);
        Assert.Equal(0, player.InventoryManager.CountItem(RedPotion));

        player.InventoryManager.TryRemoveItemsById(Hammer, 1, out _);
        Assert.Null(player.TryTurnInQuest(0, out _));
        Assert.Equal(5, player.InventoryManager.CountItem(RedPotion));
    }

    [Fact]
    public void Delivered_items_free_their_room_before_the_reward_is_counted() {
        var (player, _) = NewPlayer(rep: [new FactionRep(ChantDeFer, 1000)]);
        Give(player, Hammer, 39);
        Give(player, Vial, 3); // 40 rows, the vial row empties on turn-in, the gold takes its place
        player.TryAcceptQuest(1);

        Assert.Null(player.TryTurnInQuest(1, out _));

        Assert.Equal(0, player.InventoryManager.CountItem(Vial));
        Assert.Equal(50, player.InventoryManager.CountItem(Gold));
    }

    [Fact]
    public void Reputation_earned_by_a_quest_can_change_the_tier_and_its_perks() {
        var (player, _) = NewPlayer(rep: [new FactionRep(ChantDeFer, 950)]);
        var before = player.MaxHp;
        player.TryAcceptQuest(0);
        Kills(player, 51, 10);

        Assert.Null(player.TryTurnInQuest(0, out _));

        Assert.Equal(1050, player.ReputationWith(ChantDeFer));
        Assert.Null(player.TryAcceptQuest(1)); // Friendly now
        Assert.True(player.MaxHp >= before);
    }

    [Fact]
    public void A_loaded_block_is_cleaned_with_warnings_never_refused() {
        var state = new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: ChantDeFer,
            Quests: [new QuestEntry(0, 500), new QuestEntry(99, 1), new QuestEntry(0, 2)], QuestsDone: [3, 3, 77]);

        var (player, _) = NewPlayer(state: state);

        Assert.Equal(new[] { new QuestEntry(0, 10) }, player.ActiveQuests);
        Assert.Equal(new[] { 3 }, player.CompletedQuestIds);
    }

    [Fact]
    public void Quests_survive_a_save_and_a_reload() {
        var (player, _) = NewPlayer();
        player.TryAcceptQuest(0);
        Kills(player, 51, 4);

        var saved = player.CreateNocState();
        var (again, _) = NewPlayer(state: saved);

        Assert.Equal(new[] { new QuestEntry(0, 4) }, again.ActiveQuests);
    }

    [Fact]
    public void Without_quest_content_every_request_is_unavailable_and_kills_do_nothing() {
        var content = new NocContent(
            TestSupport.ShippedClasses(), TestSupport.ShippedSkills(), TestSupport.Progression(), BuiltinSkillHandlers.Register(new SkillHandlerRegistry()),
            TestSupport.ShippedMimic(), BuiltinMimicHandlers.Register(new MimicHandlerRegistry()), TestSupport.ShippedFactions());
        var player = TestSupport.NewPlayer(null, null, content, TestSupport.ShippedItems());

        Assert.Equal("Quests are not available.", player.TryAcceptQuest(0));
        Assert.Equal("Quests are not available.", player.TryAbandonQuest(0));
        Assert.Equal("Quests are not available.", player.TryTurnInQuest(0, out var inventory));
        Assert.Empty(inventory);
        Assert.False(player.TryGetQuest(0, out _));
        player.RegisterQuestKill(51);
        Assert.Empty(player.ActiveQuests);
    }

    [Fact]
    public void CanHold_counts_rows_with_the_stacking_rules() {
        var (player, _) = NewPlayer();
        var bag = player.InventoryManager;
        Give(player, Hammer, 3);
        Give(player, RedPotion, 5);

        Assert.True(bag.CanHold([], [(RedPotion, 99)], 4)); // joins the existing stack
        Assert.False(bag.CanHold([], [(Gold, 1)], 4));
        Assert.True(bag.CanHold([(RedPotion, 5)], [(Gold, 1)], 4)); // the potion row empties first
        Assert.True(bag.CanHold([(RedPotion, 2)], [(Gold, 1)], 5));
        Assert.False(bag.CanHold([], [(Hammer, 2)], 4)); // one row per unit
        Assert.False(bag.CanHold([(RedPotion, 6)], [], 40)); // not enough to take
        Assert.False(bag.CanHold([], [(123456, 1)], 40)); // unknown item
        Assert.False(bag.CanHold([], [(Gold, 0)], 40));
        Assert.Equal(5, bag.CountItem(RedPotion)); // nothing was changed
    }
}

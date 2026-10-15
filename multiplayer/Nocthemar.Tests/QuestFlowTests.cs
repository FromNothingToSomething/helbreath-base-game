using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T83: the shipped quests from end to end on a player (kill or bag progress, turn-in with experience, reputation and items) and the kill hook of the world.</summary>
public class QuestFlowTests {
    public static TheoryData<int> QuestIds() {
        var data = new TheoryData<int>();
        foreach (var quest in TestSupport.ShippedQuests().Quests) {
            data.Add(quest.Id);
        }

        return data;
    }

    private static GameWorldPlayer Player(QuestDefinition quest, List<ServerMessage> sent) {
        var player = TestSupport.NewPlayer(sent, null, TestSupport.ShippedContent(), TestSupport.ShippedItems());
        var floor = quest.MinTier == ReputationTier.Neutral ? 0 : TestSupport.ShippedFactions().Rules.FloorOf(quest.MinTier);
        TestSupport.Restore(player, new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: quest.FactionId, Reputations: floor == 0 ? null : [new FactionRep(quest.FactionId, floor)]));
        return player;
    }

    [Theory]
    [MemberData(nameof(QuestIds))]
    public void Every_shipped_quest_can_be_accepted_completed_and_turned_in(int questId) {
        var quest = TestSupport.ShippedQuests().Quests[questId];
        var sent = new List<ServerMessage>();
        var player = Player(quest, sent);
        var before = player.ReputationWith(quest.FactionId);

        Assert.Null(player.TryAcceptQuest(questId));
        if (quest.Kind == QuestKind.Kill) {
            for (var i = 0; i < quest.Count - 1; i++) {
                player.RegisterQuestKill(quest.TargetId);
            }

            Assert.Equal(QuestService.NotComplete, player.TryTurnInQuest(questId, out _));
            player.RegisterQuestKill(quest.TargetId);
        } else {
            Assert.True(player.InventoryManager.TryCreateItems(quest.TargetId, quest.Count - 1, out _));
            player.TryFlushNocUpdates();
            Assert.Equal(QuestService.NotComplete, player.TryTurnInQuest(questId, out _));
            Assert.True(player.InventoryManager.TryCreateItems(quest.TargetId, 1, out _));
        }

        Assert.Null(player.TryTurnInQuest(questId, out var inventory));

        Assert.Empty(player.ActiveQuests);
        Assert.Equal(new[] { questId }, player.CompletedQuestIds);
        Assert.Equal(before + quest.RewardRep, player.ReputationWith(quest.FactionId));
        Assert.Equal(quest.RewardXp, player.CreateNocState().Xp);
        foreach (var reward in quest.RewardItems) {
            Assert.Equal(reward.Quantity, player.InventoryManager.CountItem(reward.ItemId));
        }

        if (quest.Kind == QuestKind.Deliver) {
            Assert.Equal(0, player.InventoryManager.CountItem(quest.TargetId) - quest.RewardItems.Where(r => r.ItemId == quest.TargetId).Sum(r => r.Quantity));
        }

        Assert.NotEmpty(inventory);
    }

    [Fact]
    public void The_three_quests_of_a_faction_open_one_tier_at_a_time() {
        var player = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent(), TestSupport.ShippedItems());
        TestSupport.Restore(player, new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: 2));

        Assert.Null(player.TryAcceptQuest(6));
        Assert.NotNull(player.TryAcceptQuest(7));
        Assert.NotNull(player.TryAcceptQuest(8));
        Assert.True(player.TryAdjustReputation(2, 1000));
        Assert.Null(player.TryAcceptQuest(7));
        Assert.NotNull(player.TryAcceptQuest(8));
        Assert.True(player.TryAdjustReputation(2, 2000));
        Assert.Null(player.TryAcceptQuest(8));
    }

    [Fact]
    public void Killing_a_rival_monster_advances_the_quest_and_earns_the_kill_reputation_too() {
        var player = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent(), TestSupport.ShippedItems());
        TestSupport.Restore(player, new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: 0));
        player.TryAcceptQuest(0);

        // What GameWorld.OnMonsterKilledByPlayer does for a Skeleton (monster 51, Choeur Abyssal)
        player.RegisterKill();
        player.RegisterFactionKill(1, null);
        player.RegisterQuestKill(51);

        Assert.Equal(new[] { new QuestEntry(0, 1) }, player.ActiveQuests);
        Assert.True(player.ReputationWith(0) > 0);
        Assert.True(player.ReputationWith(1) < 0);
    }

    [Fact]
    public void Another_player_does_not_progress_from_a_kill_he_did_not_make() {
        var killer = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent(), TestSupport.ShippedItems());
        var bystander = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent(), TestSupport.ShippedItems());
        foreach (var p in new[] { killer, bystander }) {
            TestSupport.Restore(p, new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: 0));
            p.TryAcceptQuest(0);
        }

        killer.RegisterQuestKill(51);

        Assert.Equal(1, killer.ActiveQuests.Single().Progress);
        Assert.Equal(0, bystander.ActiveQuests.Single().Progress);
    }

    [Fact]
    public void The_progress_reaches_the_client_with_the_next_state_update() {
        var sent = new List<ServerMessage>();
        var player = TestSupport.NewPlayer(sent, null, TestSupport.ShippedContent(), TestSupport.ShippedItems());
        TestSupport.Restore(player, new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: 0));
        player.TryAcceptQuest(0);
        player.TryFlushNocUpdates();
        sent.Clear();

        Assert.False(player.TryFlushNocUpdates()); // nothing moved
        player.RegisterQuestKill(51);
        Assert.True(player.TryFlushNocUpdates());
        Assert.Single(sent, m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated);
    }

    [Fact]
    public void The_kill_hook_of_the_world_progresses_quests_for_the_killer_only() {
        var source = File.ReadAllText(FindSource("World", "Game", "GameWorld.cs"));
        var start = source.IndexOf("public void OnMonsterKilledByPlayer", StringComparison.Ordinal);
        Assert.True(start > 0, "OnMonsterKilledByPlayer not found");
        var body = source.Substring(start, source.IndexOf("TryGetPlayerBySessionId", start, StringComparison.Ordinal) - start);

        Assert.Contains("killer.RegisterQuestKill(monster.CatalogMonsterId);", body);
        Assert.Equal(1, body.Split("RegisterQuestKill").Length - 1);
        Assert.True(body.IndexOf("killer.RegisterFactionKill(", StringComparison.Ordinal) < body.IndexOf("killer.RegisterQuestKill(", StringComparison.Ordinal));
    }

    private static string FindSource(params string[] relative) {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine(new[] { dir.FullName, "server" }.Concat(relative).ToArray());
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        throw new FileNotFoundException("server/" + string.Join('/', relative) + " not found above " + AppContext.BaseDirectory);
    }
}

using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T81: Quests.json parsing and validation (factions, tiers, kinds, rewards) and its check against the monster and item catalogs.</summary>
public class QuestConfigTests {
    private static string Json(string quests, string rules = """{"maxActive":5,"bagSlots":40}""") =>
        $$"""{"schemaVersion":1,"rules":{{rules}},"quests":[{{quests}}]}""";

    private static string Kill(int id = 0, int faction = 0, string tier = "Neutral", int monster = 51, int count = 10, string rewards = """{"xp":100,"rep":50,"items":[]}""", string key = "")
        => $$"""{"id":{{id}},"key":"{{(key == "" ? "q" + id : key)}}","name":"Q{{id}}","factionId":{{faction}},"minTier":"{{tier}}","kind":"kill","monsterId":{{monster}},"count":{{count}},"rewards":{{rewards}} }""";

    private static string Deliver(int id = 0, int faction = 0, string tier = "Neutral", int item = 36, int count = 3, string rewards = """{"xp":100,"rep":50,"items":[]}""")
        => $$"""{"id":{{id}},"key":"d{{id}}","name":"D{{id}}","factionId":{{faction}},"minTier":"{{tier}}","kind":"deliver","itemId":{{item}},"count":{{count}},"rewards":{{rewards}} }""";

    private static void Rejects(string json, string expected) {
        var ex = Assert.Throws<InvalidOperationException>(() => QuestConfig.Parse(json, TestSupport.ShippedFactions()));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void The_shipped_file_has_12_quests_3_per_faction_and_loads() {
        var quests = TestSupport.ShippedQuests();

        Assert.Equal(12, quests.Count);
        Assert.Equal(5, quests.MaxActive);
        Assert.Equal(40, quests.BagSlots);
        foreach (var faction in TestSupport.ShippedFactions().Factions) {
            var own = quests.Quests.Where(q => q.FactionId == faction.Id).OrderBy(q => q.Id).ToArray();
            Assert.Equal(new[] { ReputationTier.Neutral, ReputationTier.Friendly, ReputationTier.Honored }, own.Select(q => q.MinTier).ToArray());
            Assert.Contains(own, q => q.Kind == QuestKind.Kill);
            Assert.Contains(own, q => q.Kind == QuestKind.Deliver);
        }
    }

    [Fact]
    public void A_valid_quest_exposes_its_fields() {
        var config = QuestConfig.Parse(Json(Kill(rewards: """{"xp":300,"rep":120,"items":[{"itemId":36,"qty":4},{"itemId":35,"qty":2}]}""")), TestSupport.ShippedFactions());

        var quest = Assert.Single(config.Quests);
        Assert.Equal((0, "q0", 0, ReputationTier.Neutral, QuestKind.Kill, 51, 10), (quest.Id, quest.Key, quest.FactionId, quest.MinTier, quest.Kind, quest.TargetId, quest.Count));
        Assert.Equal((300L, 120), (quest.RewardXp, quest.RewardRep));
        Assert.Equal(new[] { new QuestItem(36, 4), new QuestItem(35, 2) }, quest.RewardItems.ToArray());
        Assert.True(config.TryGet(0, out _));
        Assert.False(config.TryGet(1, out _));
    }

    [Fact]
    public void A_deliver_quest_targets_an_item() {
        var quest = QuestConfig.Parse(Json(Deliver(item: 5100, count: 3)), TestSupport.ShippedFactions()).Quests.Single();

        Assert.Equal((QuestKind.Deliver, 5100, 3), (quest.Kind, quest.TargetId, quest.Count));
    }

    [Theory]
    [InlineData("""{"schemaVersion":2,"rules":{"maxActive":5,"bagSlots":40},"quests":[]}""", "schemaVersion must be 1")]
    [InlineData("""{"schemaVersion":1,"quests":[]}""", "rules.maxActive")]
    [InlineData("""{"schemaVersion":1,"rules":{"maxActive":0,"bagSlots":40},"quests":[]}""", "rules.maxActive")]
    [InlineData("""{"schemaVersion":1,"rules":{"maxActive":5,"bagSlots":0},"quests":[]}""", "rules.bagSlots")]
    [InlineData("""{"schemaVersion":1,"rules":{"maxActive":5,"bagSlots":40},"quests":[]}""", "at least one quest")]
    [InlineData("""{"schemaVersion":1,"rules":{"maxActive":5,"bagSlots":40},"quests":[],"extra":1}""", "extra")]
    public void The_document_is_checked(string json, string expected) => Rejects(json, expected);

    [Fact]
    public void Ids_are_contiguous_and_keys_unique() {
        Rejects(Json(Kill(id: 1)), "id must be 0");
        Rejects(Json(Kill(id: 0, key: "same") + "," + Kill(id: 1, key: "same")), "used twice");
        Rejects(Json(Kill(key: "Bad Key")), "key");
    }

    [Fact]
    public void The_faction_and_the_tier_must_exist() {
        Rejects(Json(Kill(faction: 9)), "factionId 9");
        Rejects(Json(Kill(tier: "Hostile")), "minTier");
        Rejects(Json(Kill(tier: "Legendary")), "minTier");
        Assert.Single(QuestConfig.Parse(Json(Kill(tier: "exalted")), TestSupport.ShippedFactions()).Quests);
    }

    [Fact]
    public void The_objective_matches_the_kind() {
        Rejects(Json(Kill().Replace("\"monsterId\":51,", "")), "monsterId is required");
        Rejects(Json(Deliver().Replace("\"itemId\":36,", "")), "itemId is required");
        Rejects(Json(Kill().Replace("\"count\":10", "\"itemId\":36,\"count\":10")), "itemId is only for a deliver quest");
        Rejects(Json(Deliver().Replace("\"count\":3", "\"monsterId\":1,\"count\":3")), "monsterId is only for a kill quest");
        Rejects(Json(Kill().Replace("\"kind\":\"kill\"", "\"kind\":\"escort\"")), "kind");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void The_count_is_bounded(int count) => Rejects(Json(Kill(count: count)), "count must be in [1, 1000]");

    [Fact]
    public void Rewards_are_bounded_and_not_empty() {
        Rejects(Json(Kill(rewards: """{"xp":0,"rep":0,"items":[]}""")), "must give something");
        Rejects(Json(Kill(rewards: """{"xp":-1,"rep":0,"items":[]}""")), "rewards.xp");
        Rejects(Json(Kill(rewards: """{"xp":10,"rep":10001,"items":[]}""")), "rewards.rep");
        Rejects(Json(Kill(rewards: """{"xp":10,"rep":-1,"items":[]}""")), "rewards.rep");
        Rejects(Json(Kill(rewards: """{"xp":10,"items":[{"itemId":36,"qty":0}]}""")), "qty");
        Rejects(Json(Kill(rewards: """{"xp":10,"items":[{"itemId":36,"qty":1},{"itemId":36,"qty":2}]}""")), "twice");
        Rejects(Json(Kill(rewards: """{"xp":10,"items":[{"itemId":1,"qty":1},{"itemId":2,"qty":1},{"itemId":3,"qty":1},{"itemId":4,"qty":1},{"itemId":5,"qty":1}]}""")), "max 4");
        Rejects(Json(Kill().Replace(",\"rewards\":{\"xp\":100,\"rep\":50,\"items\":[]}", "")), "rewards is missing");
    }

    [Fact]
    public void A_higher_tier_never_pays_less_xp_than_a_lower_one() {
        Rejects(
            Json(Kill(id: 0, tier: "Neutral", rewards: """{"xp":500,"rep":10}""") + "," + Kill(id: 1, tier: "Honored", rewards: """{"xp":100,"rep":10}""")),
            "pays less xp");
    }

    [Fact]
    public void Every_problem_is_reported_at_once() {
        var ex = Assert.Throws<InvalidOperationException>(() => QuestConfig.Parse(Json(Kill(faction: 9, count: 0)), TestSupport.ShippedFactions()));

        Assert.Contains("factionId 9", ex.Message);
        Assert.Contains("count must be", ex.Message);
    }

    [Fact]
    public void Monsters_must_exist_and_not_belong_to_the_quests_own_faction() {
        var config = QuestConfig.Parse(Json(Kill(faction: 0, monster: 51)), TestSupport.ShippedFactions());
        var monsters = new Dictionary<int, int?> { [51] = 1, [22] = 0, [99] = null };

        config.ValidateMonsters(monsters);

        var unknown = QuestConfig.Parse(Json(Kill(monster: 77)), TestSupport.ShippedFactions());
        Assert.Contains("unknown monsterId 77", Assert.Throws<InvalidOperationException>(() => unknown.ValidateMonsters(monsters)).Message);
        var own = QuestConfig.Parse(Json(Kill(faction: 0, monster: 22)), TestSupport.ShippedFactions());
        Assert.Contains("own faction", Assert.Throws<InvalidOperationException>(() => own.ValidateMonsters(monsters)).Message);
        QuestConfig.Parse(Json(Kill(faction: 0, monster: 99)), TestSupport.ShippedFactions()).ValidateMonsters(monsters); // no faction: fine
    }

    [Fact]
    public void Items_must_exist() {
        var config = QuestConfig.Parse(
            Json(Deliver(item: 36, rewards: """{"xp":1,"items":[{"itemId":35,"qty":1}]}""")), TestSupport.ShippedFactions());

        config.ValidateItems(new[] { 35, 36 });

        Assert.Contains("unknown itemId 36", Assert.Throws<InvalidOperationException>(() => config.ValidateItems(new[] { 35 })).Message);
        Assert.Contains("rewards unknown itemId 35", Assert.Throws<InvalidOperationException>(() => config.ValidateItems(new[] { 36 })).Message);
    }

    [Fact]
    public void The_shipped_quests_target_the_rival_factions_monsters() {
        var quests = TestSupport.ShippedQuests();
        var monsters = TestSupport.ShippedMonsterFactions();
        var factions = TestSupport.ShippedFactions();

        foreach (var quest in quests.Quests.Where(q => q.Kind == QuestKind.Kill)) {
            Assert.Contains(monsters[quest.TargetId]!.Value, factions.Factions.Single(f => f.Id == quest.FactionId).Rivals);
        }
    }

    [Fact]
    public void Server_loads_and_checks_the_quests_once_the_catalogs_exist() {
        var source = File.ReadAllText(FindServer("Server.cs"));

        Assert.Contains("QuestConfig.LoadAsync(factionConfig)", source);
        Assert.Contains("questConfig.ValidateMonsters(", source);
        Assert.Contains("questConfig.ValidateItems(", source);
    }

    private static string FindServer(string file) {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine(dir.FullName, "multiplayer", "server", file);
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        throw new FileNotFoundException(file);
    }
}

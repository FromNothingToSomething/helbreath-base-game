using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Skills;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T66: the Forge on the player: consume and fuse essences from the bag, one ability per monster, class exclusivity, nothing consumed on a refusal.</summary>
public class MimicForgeTests {
    private static GameWorldPlayer Player(int? classId = null, MimicEntry[]? mimics = null, NocContent? content = null) {
        var player = TestSupport.NewPlayer(null, null, content ?? TestSupport.ShippedContent(), TestSupport.EssenceItems());
        if (classId is not null || mimics is not null) {
            TestSupport.Restore(player, new NocState(NocState.CurrentV, 11, 0, 0, RaceId: 0, ClassId: classId, Mimics: mimics));
        }

        return player;
    }

    private static void Give(GameWorldPlayer player, int abilityId, int quantity = 1) {
        for (var i = 0; i < quantity; i++) {
            Assert.True(player.InventoryManager.TryCreateItem(MimicIds.EssenceItemId(abilityId), null, out _));
        }
    }

    private static int Held(GameWorldPlayer player, int abilityId) => player.InventoryManager.CountItem(MimicIds.EssenceItemId(abilityId));

    // Shipped: 2001 Morsure infectée, 2005 Frénésie, 2008 Putréfaction (open); 2000 Nuée (class 3); 2005 + 2008 -> 2102 Bête Immortelle.

    [Fact]
    public void Consuming_an_essence_teaches_the_ability_and_removes_the_essence() {
        var player = Player();
        Give(player, 2001, 2);

        var refusal = player.ForgeConsume(2001, out var inventory);

        Assert.Null(refusal);
        Assert.Equal(2001, player.LearnedMimics.Single().Id);
        Assert.Equal(1, Held(player, 2001));
        Assert.Single(inventory!.AddedToBag);
        Assert.Equal(1, inventory.AddedToBag[0].Quantity);
    }

    [Fact]
    public void The_last_essence_removes_its_row_from_the_bag() {
        var player = Player();
        Give(player, 2001);

        Assert.Null(player.ForgeConsume(2001, out var inventory));

        Assert.Equal(0, Held(player, 2001));
        Assert.Single(inventory!.RemovedFromBagItemUids);
    }

    [Fact]
    public void Consuming_without_the_essence_is_refused_and_learns_nothing() {
        var player = Player();

        Assert.Equal("You do not have this essence.", player.ForgeConsume(2001, out var inventory));

        Assert.Null(inventory);
        Assert.Empty(player.LearnedMimics);
    }

    [Fact]
    public void A_duplicate_is_refused_and_the_essence_is_kept() {
        var player = Player(mimics: [new MimicEntry(2001, 4)]);
        Give(player, 2001);

        Assert.Contains("already learned", player.ForgeConsume(2001, out var inventory));

        Assert.Null(inventory);
        Assert.Equal(1, Held(player, 2001));
        Assert.Equal(4, player.LearnedMimics.Single().Xp);
    }

    [Fact]
    public void A_class_reserved_essence_is_refused_to_another_class_and_kept() {
        var player = Player(classId: 0, mimics: []);
        Give(player, 2000);

        Assert.Contains("reserved to another class", player.ForgeConsume(2000, out _));

        Assert.Equal(1, Held(player, 2000));
        Assert.Empty(player.LearnedMimics);
    }

    [Fact]
    public void A_fusion_result_cannot_be_consumed_and_unknown_ids_are_refused() {
        var player = Player();

        Assert.Contains("only be obtained by fusion", player.ForgeConsume(2102, out _));
        Assert.Contains("Unknown ability", player.ForgeConsume(7, out _));
        Assert.Equal("Mimicry is not available.", TestSupport.NewPlayer().ForgeConsume(2001, out _));
    }

    [Fact]
    public void Fusing_two_essences_in_either_order_teaches_the_result_and_consumes_both() {
        foreach (var (a, b) in new[] { (2005, 2008), (2008, 2005) }) {
            var player = Player();
            Give(player, 2005);
            Give(player, 2008, 2);

            Assert.Null(player.ForgeFuse(a, b, out var inventory));

            Assert.Equal(2102, player.LearnedMimics.Single().Id);
            Assert.Equal(0, Held(player, 2005));
            Assert.Equal(1, Held(player, 2008));
            Assert.Equal(2, inventory!.AddedToBag.Count + inventory.RemovedFromBagItemUids.Count);
        }
    }

    [Fact]
    public void Fusing_with_a_missing_essence_consumes_nothing() {
        var player = Player();
        Give(player, 2005);

        Assert.Equal("You do not have this essence.", player.ForgeFuse(2005, 2008, out var inventory));

        Assert.Null(inventory);
        Assert.Equal(1, Held(player, 2005));
        Assert.Empty(player.LearnedMimics);
    }

    [Fact]
    public void Fusing_without_a_recipe_or_the_same_essence_twice_is_refused_and_keeps_the_essences() {
        var player = Player();
        Give(player, 2001);
        Give(player, 2005);

        Assert.Contains("do not fuse", player.ForgeFuse(2001, 2005, out _));
        Assert.Contains("two different essences", player.ForgeFuse(2001, 2001, out _));
        Assert.Contains("cannot be fused again", player.ForgeFuse(2102, 2001, out _));

        Assert.Equal(1, Held(player, 2001));
        Assert.Equal(1, Held(player, 2005));
    }

    [Fact]
    public void Fusion_does_not_bypass_one_ability_per_monster() {
        var player = Player(mimics: [new MimicEntry(2102, 6)]);
        Give(player, 2005);
        Give(player, 2008);

        Assert.Contains("already learned", player.ForgeFuse(2005, 2008, out var inventory));

        Assert.Null(inventory);
        Assert.Equal(1, Held(player, 2005));
        Assert.Equal(1, Held(player, 2008));
        Assert.Equal(6, player.LearnedMimics.Single().Xp);
    }

    [Fact]
    public void Fusion_does_not_bypass_class_exclusivity() {
        var classes = TestSupport.ShippedClasses();
        const string json = """
            {"schemaVersion":1,"slots":{"first":1,"levelsPerSlot":10,"max":9},
             "xp":{"maxLevel":2,"perCast":1,"thresholds":[5],"powerPerLevel":0.05},
             "abilities":[
              {"id":2000,"key":"alpha","name":"Alpha","kind":"offensive","rarity":"common","monsterId":0,"monsterName":"A","dropChance":0.1,"cooldownMs":30000,"manaCost":1,"desc":"d","status":"planned"},
              {"id":2001,"key":"bravo","name":"Bravo","kind":"offensive","rarity":"common","monsterId":1,"monsterName":"B","dropChance":0.1,"cooldownMs":30000,"manaCost":1,"desc":"d","status":"planned"},
              {"id":2100,"key":"both","name":"Both","kind":"offensive","rarity":"epic","classIds":[0],"cooldownMs":30000,"manaCost":1,"desc":"d","status":"planned"}],
             "fusions":[{"a":2000,"b":2001,"result":2100}]}
            """;
        var content = new NocContent(classes, TestSupport.ShippedSkills(), TestSupport.Progression(), BuiltinSkillHandlers.Register(new SkillHandlerRegistry()), MimicConfig.Parse(json, classes));
        var outsider = Player(classId: 1, mimics: [], content: content);
        var insider = Player(classId: 0, mimics: [], content: content);
        foreach (var p in new[] { outsider, insider }) {
            Give(p, 2000);
            Give(p, 2001);
        }

        Assert.Contains("reserved to another class", outsider.ForgeFuse(2000, 2001, out _));
        Assert.Equal(1, Held(outsider, 2000));
        Assert.Empty(outsider.LearnedMimics);
        Assert.Null(insider.ForgeFuse(2000, 2001, out _));
        Assert.Equal(2100, insider.LearnedMimics.Single().Id);
        Assert.Equal(0, Held(insider, 2000));
    }

    [Fact]
    public void What_the_forge_taught_survives_a_save() {
        var player = Player();
        Give(player, 2001);
        player.ForgeConsume(2001, out _);

        var state = player.CreatePersistenceState("aresden");

        Assert.Equal(2001, state.Noc!.Mimics!.Single().Id);
    }
}

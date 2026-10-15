using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T59: slots, forge (consume / fuse), equipment, ability experience and load-time sanitizing, on the shipped catalog.</summary>
public class MimicServiceTests {
    // Shipped abilities used below: 2000 Nuée (class 3), 2001 Morsure infectée (open), 2002 Traversée (classes 2, 3), 2003 Peau de fer (classes 0, 4),
    // 2004 Brasier (class 4), 2005 Frénésie (open), 2008 Putréfaction (open); fusions 2003 + 2004 -> 2100 (Drake de Fer), 2002 + 2013 -> 2101, 2005 + 2008 -> 2102.
    private static MimicService Service() => new(TestSupport.ShippedMimic());

    private static NocState Char(int level = 1, int? classId = null, MimicEntry[]? mimics = null, int[]? equipped = null) =>
        new(NocState.CurrentV, level, 0, 0, RaceId: 0, ClassId: classId, Mimics: mimics, MimicEquipped: equipped);

    private static void Refused(MimicActionResult result, string expected) {
        Assert.False(result.Ok);
        Assert.Contains(expected, result.Refusal);
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(11, 0, 2)]
    [InlineData(21, 0, 3)]
    [InlineData(81, 0, 9)]
    [InlineData(100, 0, 9)]
    [InlineData(1, 2, 3)]
    [InlineData(81, 2, 11)]
    [InlineData(81, 9, 12)]
    [InlineData(1, -3, 1)]
    public void Slots_follow_the_level_plus_the_stat_bonus(int level, int bonus, int expected) {
        Assert.Equal(expected, Service().SlotsAt(level, bonus));
    }

    [Fact]
    public void Consuming_an_essence_teaches_the_ability_at_level_1() {
        var service = Service();

        var result = service.Consume(Char(), 2001);

        Assert.True(result.Ok);
        Assert.True(service.IsLearned(result.State, 2001));
        Assert.Equal(1, service.LevelOf(result.State, 2001));
        Assert.Equal(0, service.XpOf(result.State, 2001));
        Assert.Equal(1.0, service.PowerOf(result.State, 2001), 9);
        Assert.False(service.IsLearned(result.State, 2005));
        Assert.Equal(0, service.LevelOf(result.State, 2005));
    }

    [Fact]
    public void Learned_abilities_are_kept_in_id_order() {
        var service = Service();
        var state = service.Consume(service.Consume(service.Consume(Char(), 2008).State, 2001).State, 2005).State;

        Assert.True(new[] { 2001, 2005, 2008 }.SequenceEqual(state.Mimics!.Select(m => m.Id)));
        Assert.True(new[] { 2001, 2005, 2008 }.SequenceEqual(service.Learned(state).Select(a => a.Id)));
    }

    [Fact]
    public void Consume_refuses_in_the_documented_order() {
        var service = Service();

        Refused(service.Consume(Char(), 1), "Unknown ability");
        Refused(service.Consume(Char(), 2100), "only be obtained by fusion");
        Refused(service.Consume(Char(classId: 0), 2000), "reserved to another class");
        Refused(service.Consume(Char(), 2000), "reserved to another class");
        Refused(service.Consume(Char(mimics: [new MimicEntry(2001)]), 2001), "already learned");
        Assert.True(service.Consume(Char(classId: 3), 2000).Ok);
    }

    [Fact]
    public void A_refusal_leaves_the_state_unchanged() {
        var service = Service();
        var state = Char(mimics: [new MimicEntry(2001, 4)]);

        Assert.Same(state, service.Consume(state, 2001).State);
        Assert.Same(state, service.Fuse(state, 2000, 2000).State);
        Assert.Same(state, service.Equip(state, 2005, 3).State);
        Assert.Same(state, service.Unequip(state, 2001).State);
    }

    [Fact]
    public void Fusing_two_essences_teaches_the_recipe_result_in_either_order() {
        var service = Service();

        var forward = service.Fuse(Char(classId: 4), 2003, 2004);
        var backward = service.Fuse(Char(classId: 4), 2004, 2003);

        Assert.True(forward.Ok);
        Assert.True(service.IsLearned(forward.State, 2100));
        Assert.False(service.IsLearned(forward.State, 2003));
        Assert.Equal(forward.State.Mimics, backward.State.Mimics);
    }

    [Fact]
    public void Fuse_refuses_bad_inputs_missing_recipes_duplicates_and_classes() {
        var service = Service();

        Refused(service.Fuse(Char(), 2001, 2001), "two different essences");
        Refused(service.Fuse(Char(), 2001, 1), "Unknown essence");
        Refused(service.Fuse(Char(), 1, 2001), "Unknown essence");
        Refused(service.Fuse(Char(), 2100, 2001), "cannot be fused again");
        Refused(service.Fuse(Char(), 2001, 2005), "do not fuse");
        Refused(service.Fuse(Char(mimics: [new MimicEntry(2102)]), 2005, 2008), "already learned");
        Assert.True(service.Fuse(Char(), 2005, 2008).Ok);
    }

    [Fact]
    public void Fusing_does_not_bypass_class_exclusivity_when_the_result_is_reserved() {
        var classes = ClassConfig.Parse(
            """{"schemaVersion":1,"classes":[{"id":0,"key":"ka","name":"A","role":"r","branches":[{"id":0,"key":"aa","name":"A","role":"r"},{"id":1,"key":"bb","name":"B","role":"r"},{"id":2,"key":"cc","name":"C","role":"r"}]},{"id":1,"key":"kb","name":"B","role":"r","branches":[{"id":0,"key":"aa","name":"A","role":"r"},{"id":1,"key":"bb","name":"B","role":"r"},{"id":2,"key":"cc","name":"C","role":"r"}]}]}""");
        const string json = """
            {"schemaVersion":1,"slots":{"first":1,"levelsPerSlot":10,"max":9},
             "xp":{"maxLevel":2,"perCast":1,"thresholds":[5],"powerPerLevel":0.05},
             "abilities":[
              {"id":2000,"key":"alpha","name":"Alpha","kind":"offensive","rarity":"common","monsterId":0,"monsterName":"A","dropChance":0.1,"cooldownMs":30000,"manaCost":1,"desc":"d","status":"planned"},
              {"id":2001,"key":"bravo","name":"Bravo","kind":"offensive","rarity":"common","monsterId":1,"monsterName":"B","dropChance":0.1,"cooldownMs":30000,"manaCost":1,"desc":"d","status":"planned"},
              {"id":2100,"key":"both","name":"Both","kind":"offensive","rarity":"epic","classIds":[0],"cooldownMs":30000,"manaCost":1,"desc":"d","status":"planned"}],
             "fusions":[{"a":2000,"b":2001,"result":2100}]}
            """;
        var service = new MimicService(MimicConfig.Parse(json, classes));

        Refused(service.Fuse(Char(classId: 1), 2000, 2001), "reserved to another class");
        Refused(service.Fuse(Char(), 2001, 2000), "reserved to another class");
        Assert.True(service.Fuse(Char(classId: 0), 2000, 2001).Ok);
    }

    [Fact]
    public void Equip_fills_the_bar_in_order_and_respects_the_slot_limit() {
        var service = Service();
        var state = Char(mimics: [new MimicEntry(2001), new MimicEntry(2005), new MimicEntry(2008)]);

        state = service.Equip(state, 2005, 2).State;
        state = service.Equip(state, 2001, 2).State;

        Assert.True(new[] { 2005, 2001 }.SequenceEqual(state.MimicEquipped!));
        Refused(service.Equip(state, 2008, 2), "No free mimicry slot");
        Assert.True(service.Equip(state, 2008, 3).Ok);
        Assert.True(service.IsEquipped(state, 2005));
        Assert.True(new[] { 2005, 2001 }.SequenceEqual(service.Equipped(state).Select(a => a.Id)));
    }

    [Fact]
    public void Equip_refuses_in_order_unknown_not_learned_class_and_duplicate() {
        var service = Service();
        var learned = Char(classId: 0, mimics: [new MimicEntry(2001), new MimicEntry(2000)], equipped: [2001]);

        Refused(service.Equip(learned, 1, 5), "Unknown ability");
        Refused(service.Equip(learned, 2005, 5), "not learned");
        Refused(service.Equip(learned, 2000, 5), "reserved to another class");
        Refused(service.Equip(learned, 2001, 5), "already equipped");
        Refused(service.Equip(Char(mimics: [new MimicEntry(2001)]), 2001, 0), "No free mimicry slot");
    }

    [Fact]
    public void Unequip_moves_the_following_abilities_up_and_clears_an_empty_bar() {
        var service = Service();
        var state = Char(mimics: [new MimicEntry(2001), new MimicEntry(2005), new MimicEntry(2008)], equipped: [2008, 2001, 2005]);

        var removed = service.Unequip(state, 2001);

        Assert.True(removed.Ok);
        Assert.True(new[] { 2008, 2005 }.SequenceEqual(removed.State.MimicEquipped!));
        Assert.True(service.IsLearned(removed.State, 2001));
        Refused(service.Unequip(removed.State, 2001), "not equipped");
        Assert.Null(service.Unequip(Char(mimics: [new MimicEntry(2001)], equipped: [2001]), 2001).State.MimicEquipped);
    }

    [Fact]
    public void Each_cast_gives_xp_up_to_the_maximum_level() {
        var service = Service();
        var xp = service.Config.Xp;
        var state = Char(mimics: [new MimicEntry(2001), new MimicEntry(2005)]);

        for (var i = 0; i < xp.Thresholds[0]; i++) {
            state = service.AddCastXp(state, 2001);
        }

        Assert.Equal(xp.Thresholds[0], service.XpOf(state, 2001));
        Assert.Equal(2, service.LevelOf(state, 2001));
        Assert.Equal(1.05, service.PowerOf(state, 2001), 9);
        Assert.Equal(0, service.XpOf(state, 2005));

        for (var i = 0; i < 1000; i++) {
            state = service.AddCastXp(state, 2001);
        }

        Assert.Equal(xp.MaxXp, service.XpOf(state, 2001));
        Assert.Equal(xp.MaxLevel, service.LevelOf(state, 2001));
        Assert.Equal(1.45, service.PowerOf(state, 2001), 9);
        Assert.Same(state, service.AddCastXp(state, 2001));
    }

    [Fact]
    public void A_cast_of_an_unlearned_ability_changes_nothing_and_does_not_mutate_the_input() {
        var service = Service();
        var state = Char(mimics: [new MimicEntry(2001, 3)]);

        Assert.Same(state, service.AddCastXp(state, 2005));
        var next = service.AddCastXp(state, 2001);

        Assert.Equal(3, state.Mimics![0].Xp);
        Assert.Equal(4, next.Mimics![0].Xp);
    }

    [Fact]
    public void Sanitize_keeps_a_valid_state_unchanged() {
        var service = Service();
        var state = Char(classId: 3, mimics: [new MimicEntry(2000, 5), new MimicEntry(2001)], equipped: [2001, 2000]);

        var clean = service.Sanitize(state, 2, out var warnings);

        Assert.Empty(warnings);
        Assert.True(state.Mimics!.SequenceEqual(clean.Mimics!));
        Assert.True(state.MimicEquipped!.SequenceEqual(clean.MimicEquipped!));
    }

    [Fact]
    public void Sanitize_drops_unknown_forbidden_and_duplicate_abilities() {
        var service = Service();
        var state = Char(classId: 0, mimics: [new MimicEntry(9999), new MimicEntry(2000, 1), new MimicEntry(2005, 2), new MimicEntry(2005, 7), new MimicEntry(2001, -4)]);

        var clean = service.Sanitize(state, 3, out var warnings);

        Assert.True(new[] { 2001, 2005 }.SequenceEqual(clean.Mimics!.Select(m => m.Id)));
        Assert.Equal(0, clean.Mimics![0].Xp);
        Assert.Equal(7, clean.Mimics[1].Xp);
        Assert.Contains(warnings, w => w.Contains("9999") && w.Contains("catalog"));
        Assert.Contains(warnings, w => w.Contains("2000") && w.Contains("class"));
        Assert.Contains(warnings, w => w.Contains("2005") && w.Contains("twice"));
        Assert.Contains(warnings, w => w.Contains("2001") && w.Contains("experience"));
    }

    [Fact]
    public void Sanitize_caps_experience_at_the_maximum() {
        var service = Service();

        var clean = service.Sanitize(Char(mimics: [new MimicEntry(2001, 1_000_000)]), 1, out var warnings);

        Assert.Equal(service.Config.Xp.MaxXp, clean.Mimics![0].Xp);
        Assert.Single(warnings);
    }

    [Fact]
    public void Sanitize_trims_the_bar_to_learned_unique_abilities_within_the_slot_limit() {
        var service = Service();
        var state = Char(mimics: [new MimicEntry(2001), new MimicEntry(2005), new MimicEntry(2008)], equipped: [2005, 2008, 2005, 2002, 2001]);

        var clean = service.Sanitize(state, 2, out var warnings);

        Assert.True(new[] { 2005, 2008 }.SequenceEqual(clean.MimicEquipped!));
        Assert.Contains(warnings, w => w.Contains("2005") && w.Contains("twice"));
        Assert.Contains(warnings, w => w.Contains("2002") && w.Contains("not learned"));
        Assert.Contains(warnings, w => w.Contains("2001") && w.Contains("no free slot"));
    }

    [Fact]
    public void Sanitize_turns_empty_results_into_null() {
        var service = Service();

        var clean = service.Sanitize(Char(mimics: [new MimicEntry(9999)], equipped: [9999]), 3, out _);

        Assert.Null(clean.Mimics);
        Assert.Null(clean.MimicEquipped);
        Assert.Null(service.Sanitize(Char(), 3, out var warnings).Mimics);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Sanitize_does_not_touch_the_rest_of_the_state() {
        var service = Service();
        var state = new NocState(NocState.CurrentV, 12, 5, 40, RaceId: 3, ClassId: 3, Skills: [1300], Kills: 9, Mimics: [new MimicEntry(9999)]);

        var clean = service.Sanitize(state, 2, out _);

        Assert.Equal(state with { Mimics = null }, clean);
    }
}

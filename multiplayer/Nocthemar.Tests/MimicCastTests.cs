using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Skills;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T63-T65: cast routing, mimicry cast checks and costs, the buff handlers and the castable catalog.</summary>
public class MimicCastTests {
    private const long Now = 1_000_000;

    // Shipped abilities used below: 2003 Peau de fer (handler buff_iron_skin, classes 0 and 4, rare: 50 mana, 40 s), 2015 Bouclier noir (handler buff_black_shield, class 0),
    // 2004 Brasier (legacy spell 8, class 4), 2001 Morsure infectée (planned), 2000 Nuée (planned, class 3).
    private static GameWorldPlayer Knight(int level = 11, MimicEntry[]? mimics = null, int[]? equipped = null, int mana = 200, int classId = 0) {
        var player = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent());
        TestSupport.Restore(player, new NocState(NocState.CurrentV, level, 0, mana, RaceId: 0, ClassId: classId,
            Mimics: mimics ?? [new MimicEntry(2003)], MimicEquipped: equipped ?? [2003]));
        return player;
    }

    private static MimicAbility Ability(int id) => TestSupport.ShippedMimic().Abilities.Single(a => a.Id == id);

    [Theory]
    [InlineData(-1, CastRoute.Spell)]
    [InlineData(0, CastRoute.Spell)]
    [InlineData(25, CastRoute.Spell)]
    [InlineData(999, CastRoute.Spell)]
    [InlineData(1000, CastRoute.Skill)]
    [InlineData(1999, CastRoute.Skill)]
    [InlineData(2000, CastRoute.Mimic)]
    [InlineData(2199, CastRoute.Mimic)]
    [InlineData(2200, CastRoute.Mimic)]
    [InlineData(9999, CastRoute.Mimic)]
    public void Cast_requests_are_routed_by_range_with_mimicry_before_skills(int id, CastRoute expected) {
        Assert.Equal(expected, CastRouting.Of(id));
    }

    [Fact]
    public void Refusals_follow_the_documented_order() {
        Assert.Equal("Mimicry is not available.", TestSupport.NewPlayer().MimicCastRefusal(2003, Now));
        Assert.Equal("Unknown ability.", Knight().MimicCastRefusal(1, Now));
        Assert.Equal("Ability not learned.", Knight(mimics: [new MimicEntry(2001)], equipped: [2001]).MimicCastRefusal(2003, Now));
        Assert.Equal("Ability not equipped.", Knight(equipped: []).MimicCastRefusal(2003, Now));
        Assert.Equal("This ability cannot be cast.", Knight(mimics: [new MimicEntry(2001)], equipped: [2001]).MimicCastRefusal(2001, Now));
        Assert.Equal("Not enough mana.", Knight(mana: 10).MimicCastRefusal(2003, Now));
        Assert.Null(Knight().MimicCastRefusal(2003, Now));
    }

    [Fact]
    public void A_dead_player_cannot_cast() {
        var player = Knight();
        player.ApplyDamage(1_000_000);

        Assert.Equal("You are dead.", player.MimicCastRefusal(2003, Now));
    }

    [Fact]
    public void A_cast_spends_mana_starts_the_cooldown_and_gives_experience() {
        var player = Knight();

        Assert.Null(player.TryBeginMimicCast(2003, Now));

        Assert.Equal(150, player.Mana);
        Assert.Equal(1, player.LearnedMimics.Single().Xp);
        Assert.Equal("Ability is on cooldown.", player.MimicCastRefusal(2003, Now + 39_999));
        Assert.Null(player.MimicCastRefusal(2003, Now + 40_000));
    }

    [Fact]
    public void A_refused_cast_spends_nothing() {
        var player = Knight(equipped: []);

        Assert.Equal("Ability not equipped.", player.TryBeginMimicCast(2003, Now));

        Assert.Equal(200, player.Mana);
        Assert.Equal(0, player.LearnedMimics.Single().Xp);
    }

    [Fact]
    public void The_cost_follows_the_mimic_cost_stat() {
        var player = Knight();
        player.Stats.Add(new StatModifier(StatId.MimicCostMult, ModifierKind.PercentAdd, -0.5, ModifierSources.Buff(1)));

        Assert.Equal(25, player.MimicManaCostOf(Ability(2003)));
        Assert.Null(player.TryBeginMimicCast(2003, Now));
        Assert.Equal(175, player.Mana);
    }

    [Fact]
    public void Iron_skin_gives_thirty_percent_armor_for_ten_seconds() {
        var player = Knight();

        Assert.Null(player.TryBeginMimicCast(2003, Now));

        Assert.Equal(1.30, player.Stats.Get(StatId.ArmorMult), 9);
        Assert.Equal(1, player.ActiveBuffCount);
        Assert.False(player.TickBuffs(Now + 9_999));
        Assert.True(player.TickBuffs(Now + 10_000));
        Assert.Equal(1.0, player.Stats.Get(StatId.ArmorMult), 9);
    }

    [Fact]
    public void The_duration_follows_the_mimic_duration_stat() {
        var player = Knight();
        player.Stats.Add(new StatModifier(StatId.MimicDurationMult, ModifierKind.PercentAdd, 1.0, ModifierSources.Buff(2)));

        Assert.Null(player.TryBeginMimicCast(2003, Now));

        Assert.False(player.TickBuffs(Now + 19_999));
        Assert.True(player.TickBuffs(Now + 20_000));
    }

    [Fact]
    public void The_power_follows_the_ability_level_and_the_mimic_power_stat() {
        var level2 = Knight(mimics: [new MimicEntry(2003, TestSupport.ShippedMimic().Xp.Thresholds[0])]);
        Assert.Equal(2, level2.MimicLevel(2003));
        Assert.Equal(1.05, level2.MimicPowerOf(2003), 9);
        Assert.Null(level2.TryBeginMimicCast(2003, Now));
        Assert.Equal(1.0 + 0.30 * 1.05, level2.Stats.Get(StatId.ArmorMult), 9);

        var boosted = Knight();
        boosted.Stats.Add(new StatModifier(StatId.MimicPowerMult, ModifierKind.PercentAdd, 1.0, ModifierSources.Buff(3)));
        Assert.Null(boosted.TryBeginMimicCast(2003, Now));
        Assert.Equal(1.60, boosted.Stats.Get(StatId.ArmorMult), 9);
    }

    [Fact]
    public void Black_shield_absorbs_half_of_physical_and_magic_damage_and_is_capped() {
        var shield = Knight(mimics: [new MimicEntry(2015)], equipped: [2015]);
        Assert.Null(shield.TryBeginMimicCast(2015, Now));
        Assert.Equal(0.50, shield.Stats.Get(StatId.ResistPhysical), 9);
        Assert.Equal(0.50, shield.Stats.Get(StatId.ResistMagic), 9);

        var boosted = Knight(mimics: [new MimicEntry(2015)], equipped: [2015]);
        boosted.Stats.Add(new StatModifier(StatId.MimicPowerMult, ModifierKind.PercentAdd, 10.0, ModifierSources.Buff(3)));
        Assert.Null(boosted.TryBeginMimicCast(2015, Now));
        Assert.Equal(0.90, boosted.Stats.Get(StatId.ResistPhysical), 9);
    }

    [Fact]
    public void An_ability_that_runs_a_spell_is_cast_at_a_target_and_only_committed_afterwards() {
        var player = Knight(mimics: [new MimicEntry(2004)], equipped: [2004], classId: 4);

        Assert.Null(player.MimicCastRefusal(2004, Now));
        Assert.Equal("This ability must be cast at a target.", player.TryBeginMimicCast(2004, Now));
        Assert.Equal(200, player.Mana);

        player.CommitMimicCast(Ability(2004), Now);

        Assert.Equal(150, player.Mana);
        Assert.Equal(1, player.LearnedMimics.Single().Xp);
        Assert.Equal("Ability is on cooldown.", player.MimicCastRefusal(2004, Now + 1));
    }

    [Fact]
    public void Experience_stops_at_the_maximum_level() {
        var xp = TestSupport.ShippedMimic().Xp;
        var player = Knight(mimics: [new MimicEntry(2003, xp.MaxXp)]);

        Assert.Null(player.TryBeginMimicCast(2003, Now));

        Assert.Equal(xp.MaxXp, player.LearnedMimics.Single().Xp);
        Assert.Equal(xp.MaxLevel, player.MimicLevel(2003));
    }

    [Fact]
    public void Every_shipped_ability_is_castable_through_a_registered_handler_or_a_real_spell_or_planned() {
        var cfg = TestSupport.ShippedMimic();
        var handlers = BuiltinMimicHandlers.Register(new MimicHandlerRegistry());
        var spellIds = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonArray>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "Spells.json")))!
            .Select(s => s!["id"]!.GetValue<int>()).ToHashSet();

        handlers.ValidateCatalog(cfg);
        foreach (var ability in cfg.Abilities.Where(a => a.IsReady)) {
            Assert.True(ability.Handler is not null ? handlers.TryGet(ability.Handler, out _) : spellIds.Contains(ability.SpellId!.Value), ability.Key);
        }

        Assert.Equal(16, cfg.Abilities.Count(a => a.IsReady));
        Assert.Equal(11, cfg.Abilities.Count(a => !a.IsReady));
    }

    [Fact]
    public void A_ready_ability_naming_a_missing_handler_fails_at_startup() {
        var classes = TestSupport.ShippedClasses();
        const string json = """
            {"schemaVersion":1,"slots":{"first":1,"levelsPerSlot":10,"max":9},
             "xp":{"maxLevel":2,"perCast":1,"thresholds":[5],"powerPerLevel":0.05},
             "abilities":[{"id":2000,"key":"alpha","name":"Alpha","kind":"defensive","rarity":"common","monsterId":0,"monsterName":"A","dropChance":0.1,"cooldownMs":30000,"manaCost":1,"desc":"d","status":"ready","handler":"missing_one"}]}
            """;
        var cfg = MimicConfig.Parse(json, classes);

        var ex = Assert.Throws<InvalidOperationException>(() => new MimicHandlerRegistry().ValidateCatalog(cfg));
        Assert.Contains("missing_one", ex.Message);
        Assert.Throws<InvalidOperationException>(() => new NocContent(classes, TestSupport.ShippedSkills(), TestSupport.Progression(), BuiltinSkillHandlers.Register(new SkillHandlerRegistry()), cfg));
    }

    [Fact]
    public void The_handler_registry_refuses_bad_and_duplicate_ids() {
        var registry = new MimicHandlerRegistry().Register(new MimicSelfBuffHandler("ok_one", 1000));

        Assert.Throws<ArgumentException>(() => registry.Register(new MimicSelfBuffHandler("ok_one", 1000)));
        Assert.Throws<ArgumentException>(() => registry.Register(new MimicSelfBuffHandler("Bad Id", 1000)));
        Assert.True(registry.TryGet("ok_one", out _));
        Assert.Equal(1, registry.Count);
    }
}

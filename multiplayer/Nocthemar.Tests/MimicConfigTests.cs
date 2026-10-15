using System.Text.Json;
using System.Text.Json.Nodes;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T57: Mimic.json catalog (22 monster abilities, 5 fusions, slot and xp tables) and its validation.</summary>
public class MimicConfigTests {
    private static string Config(string file) => Path.Combine(AppContext.BaseDirectory, "Config", file);

    private static ClassConfig OneClass() => ClassConfig.Parse(
        """{"schemaVersion":1,"classes":[{"id":0,"key":"knight","name":"Knight","role":"r","branches":[{"id":0,"key":"aa","name":"A","role":"r"},{"id":1,"key":"bb","name":"B","role":"r"},{"id":2,"key":"cc","name":"C","role":"r"}]}]}""");

    private static MimicConfig Shipped() => MimicConfig.LoadAsync(TestSupport.ShippedClasses(), Config("Nocthemar/Mimic.json")).GetAwaiter().GetResult();

    private static JsonObject Ability(int id, string key, int? monsterId = null) {
        var a = new JsonObject {
            ["id"] = id, ["key"] = key, ["name"] = key, ["kind"] = "offensive", ["rarity"] = "common", ["cooldownMs"] = 30000, ["manaCost"] = 10,
            ["desc"] = "d", ["status"] = "planned",
        };
        if (MimicIds.IsMonsterAbility(id)) {
            a["monsterId"] = monsterId ?? id - MimicIds.Base;
            a["monsterName"] = "M" + key;
            a["dropChance"] = 0.1;
        }

        return a;
    }

    /// <summary>Two monster abilities and the fusion of both.</summary>
    private static JsonObject ValidDoc() => new() {
        ["schemaVersion"] = 1,
        ["slots"] = new JsonObject { ["first"] = 1, ["levelsPerSlot"] = 10, ["max"] = 9 },
        ["xp"] = new JsonObject { ["maxLevel"] = 3, ["perCast"] = 1, ["thresholds"] = new JsonArray(10, 30), ["powerPerLevel"] = 0.05 },
        ["abilities"] = new JsonArray(Ability(2000, "alpha"), Ability(2001, "bravo"), Ability(2100, "alpha_bravo")),
        ["fusions"] = new JsonArray(new JsonObject { ["a"] = 2001, ["b"] = 2000, ["result"] = 2100 }),
    };

    private static JsonObject At(JsonObject doc, int index) => doc["abilities"]![index]!.AsObject();

    private static void Rejects(JsonObject doc, string expected) {
        var ex = Assert.Throws<InvalidOperationException>(() => MimicConfig.Parse(doc.ToJsonString(), OneClass()));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void A_valid_document_parses() {
        var cfg = MimicConfig.Parse(ValidDoc().ToJsonString(), OneClass());

        Assert.Equal(3, cfg.Abilities.Count);
        Assert.True(cfg.TryGet(2100, out var fusion));
        Assert.True(fusion.IsFusion);
        Assert.Null(fusion.EssenceItemId);
        Assert.True(cfg.TryGet(2000, out var a));
        Assert.Equal(5000, a.EssenceItemId);
        Assert.False(a.IsReady);
        Assert.False(cfg.Contains(2002));
    }

    [Fact]
    public void Fusion_lookup_ignores_the_order_and_normalizes_the_pair() {
        var cfg = MimicConfig.Parse(ValidDoc().ToJsonString(), OneClass());

        Assert.True(cfg.TryFindFusion(2000, 2001, out var f1));
        Assert.True(cfg.TryFindFusion(2001, 2000, out var f2));
        Assert.Equal(f1, f2);
        Assert.Equal(new MimicFusion(2000, 2001, 2100), f1);
        Assert.False(cfg.TryFindFusion(2000, 2000, out _));
    }

    [Fact]
    public void Monster_lookup_finds_the_ability_of_a_monster() {
        var cfg = MimicConfig.Parse(ValidDoc().ToJsonString(), OneClass());

        Assert.True(cfg.TryGetForMonster(1, out var a));
        Assert.Equal(2001, a.Id);
        Assert.False(cfg.TryGetForMonster(99, out _));
    }

    [Fact]
    public void Xp_table_gives_levels_and_power() {
        var xp = MimicConfig.Parse(ValidDoc().ToJsonString(), OneClass()).Xp;

        Assert.Equal(1, xp.LevelOf(0));
        Assert.Equal(1, xp.LevelOf(9));
        Assert.Equal(2, xp.LevelOf(10));
        Assert.Equal(3, xp.LevelOf(30));
        Assert.Equal(3, xp.LevelOf(10_000));
        Assert.Equal(30, xp.MaxXp);
        Assert.Equal(1.0, xp.PowerMultiplier(1), 9);
        Assert.Equal(1.1, xp.PowerMultiplier(3), 9);
        Assert.Equal(1.1, xp.PowerMultiplier(99), 9);
    }

    [Fact]
    public void Slot_table_follows_the_level_formula() {
        var slots = MimicConfig.Parse(ValidDoc().ToJsonString(), OneClass()).Slots;

        Assert.Equal(1, slots.SlotsAt(1));
        Assert.Equal(1, slots.SlotsAt(10));
        Assert.Equal(2, slots.SlotsAt(11));
        Assert.Equal(5, slots.SlotsAt(41));
        Assert.Equal(9, slots.SlotsAt(81));
        Assert.Equal(9, slots.SlotsAt(100));
        Assert.Equal(1, slots.SlotsAt(0));
    }

    [Fact]
    public void Shipped_catalog_has_22_monster_abilities_and_5_fusions() {
        var cfg = Shipped();

        Assert.Equal(27, cfg.Abilities.Count);
        Assert.Equal(22, cfg.Abilities.Count(a => !a.IsFusion));
        Assert.Equal(5, cfg.Abilities.Count(a => a.IsFusion));
        Assert.Equal(5, cfg.Fusions.Count);
        Assert.True(Enumerable.Range(2000, 22).SequenceEqual(cfg.Abilities.Where(a => !a.IsFusion).Select(a => a.Id)));
        Assert.True(Enumerable.Range(2100, 5).SequenceEqual(cfg.Abilities.Where(a => a.IsFusion).Select(a => a.Id)));
        Assert.Equal(22, cfg.Abilities.Select(a => a.MonsterId).Where(m => m is not null).Distinct().Count());
        Assert.All(cfg.Abilities, a => Assert.True(a.CooldownMs >= MimicConfig.MinCooldownMs));
    }

    [Fact]
    public void Shipped_catalog_has_16_castable_abilities_and_6_planned_monster_abilities() {
        var cfg = Shipped();
        var monster = cfg.Abilities.Where(a => !a.IsFusion).ToArray();

        Assert.Equal(16, monster.Count(a => a.IsReady));
        Assert.Equal(new[] { "chant_de_la_fin", "folie", "jugement", "morsure_infectee", "nuee", "putrefaction" }, monster.Where(a => !a.IsReady).Select(a => a.Key).Order().ToArray());
        Assert.All(monster.Where(a => a.IsReady), a => Assert.True(a.Handler is not null ^ a.SpellId is not null));
        Assert.All(cfg.Abilities.Where(a => a.IsFusion), a => Assert.False(a.IsReady));
        Assert.Equal(new[] { "buff_black_shield", "buff_iron_skin" }, monster.Where(a => a.Handler is not null).Select(a => a.Handler!).Order().ToArray());
    }

    [Fact]
    public void Shipped_rarities_follow_the_design_document() {
        var cfg = Shipped();

        Assert.Equal(MimicRarity.Common, cfg.Abilities.Single(a => a.Key == "nuee").Rarity);
        Assert.Equal(MimicRarity.Common, cfg.Abilities.Single(a => a.Key == "morsure_infectee").Rarity);
        Assert.Equal(MimicRarity.Rare, cfg.Abilities.Single(a => a.Key == "peau_de_fer").Rarity);
        Assert.Equal(MimicRarity.Rare, cfg.Abilities.Single(a => a.Key == "frenesie").Rarity);
        Assert.Equal(MimicRarity.Epic, cfg.Abilities.Single(a => a.Key == "cri_mortel").Rarity);
        var mythic = cfg.Abilities.Where(a => a.Rarity == MimicRarity.Mythic).Select(a => a.Key).OrderBy(k => k).ToArray();
        Assert.Equal(new[] { "chant_de_la_fin", "jugement" }, mythic);
        Assert.All(cfg.Abilities.Where(a => a.Rarity == MimicRarity.Mythic), a => Assert.True(a.RequiresRaid));
        Assert.All(cfg.Abilities.Where(a => a.RequiresRaid), a => Assert.Equal(MimicRarity.Mythic, a.Rarity));
    }

    [Fact]
    public void Shipped_fusions_combine_distinct_monster_abilities() {
        var cfg = Shipped();

        Assert.All(cfg.Fusions, f => {
            Assert.True(f.A < f.B);
            Assert.True(MimicIds.IsMonsterAbility(f.A));
            Assert.True(MimicIds.IsMonsterAbility(f.B));
            Assert.True(MimicIds.IsFusion(f.Result));
        });
        Assert.True(cfg.TryFindFusion(2013, 2002, out var queen));
        Assert.Equal(2101, queen.Result);
    }

    [Fact]
    public void Shipped_slots_and_xp_match_the_decisions() {
        var cfg = Shipped();

        Assert.Equal(1, cfg.Slots.SlotsAt(1));
        Assert.Equal(9, cfg.Slots.SlotsAt(81));
        Assert.Equal(10, cfg.Xp.MaxLevel);
        Assert.Equal(1, cfg.Xp.PerCast);
        Assert.Equal(1.45, cfg.Xp.PowerMultiplier(10), 9);
    }

    [Fact]
    public void Shipped_abilities_match_Monsters_json_and_Items_json_and_Spells_json() {
        var cfg = Shipped();
        var monsters = JsonSerializer.Deserialize<JsonArray>(File.ReadAllText(Config("Monsters.json")))!
            .ToDictionary(m => m!["id"]!.GetValue<int>(), m => m!["name"]!.GetValue<string>());
        var items = JsonSerializer.Deserialize<JsonArray>(File.ReadAllText(Config("Items.json")))!.Select(i => i!["id"]!.GetValue<int>()).ToList();
        var spells = JsonSerializer.Deserialize<JsonArray>(File.ReadAllText(Config("Spells.json")))!.Select(s => s!["id"]!.GetValue<int>()).ToList();

        cfg.ValidateMonsters(monsters);
        cfg.ValidateEssenceItems(items);
        cfg.ValidateSpells(spells);
        Assert.Equal(items.Count, items.Distinct().Count());
        Assert.Equal(22, items.Count(i => i >= MimicIds.EssenceItemBase && i < MimicIds.EssenceItemBase + MimicIds.MonsterCount));
        Assert.Contains(MimicIds.EssenceVialItemId, items);
    }

    [Fact]
    public void Shipped_essence_items_are_stackable_misc_items() {
        var items = JsonSerializer.Deserialize<JsonArray>(File.ReadAllText(Config("Items.json")))!
            .Where(i => i!["id"]!.GetValue<int>() is >= MimicIds.EssenceItemBase and < MimicIds.EssenceItemBase + MimicIds.MonsterCount).ToList();

        Assert.Equal(22, items.Count);
        Assert.All(items, i => {
            Assert.Equal("misc", i!["itemType"]!.GetValue<string>());
            Assert.True(i["stackable"]!.GetValue<bool>());
            Assert.StartsWith("Essence: ", i["name"]!.GetValue<string>());
        });
    }

    [Fact]
    public void Validate_methods_report_unknown_references() {
        var cfg = MimicConfig.Parse(ValidDoc().ToJsonString(), OneClass());

        Assert.Contains("monster 0 is not in Monsters.json", Assert.Throws<InvalidOperationException>(() => cfg.ValidateMonsters(new Dictionary<int, string> { [1] = "Mbravo" })).Message);
        Assert.Contains("not 'Malpha'", Assert.Throws<InvalidOperationException>(() => cfg.ValidateMonsters(new Dictionary<int, string> { [0] = "Other", [1] = "Mbravo" })).Message);
        Assert.Contains("essence item 5001 is missing", Assert.Throws<InvalidOperationException>(() => cfg.ValidateEssenceItems(new[] { 5000, 5100 })).Message);
        Assert.Contains("Essence Vial item 5100 is missing", Assert.Throws<InvalidOperationException>(() => cfg.ValidateEssenceItems(new[] { 5000, 5001 })).Message);
        cfg.ValidateEssenceItems(new[] { 5000, 5001, 5100 });
        cfg.ValidateMonsters(new Dictionary<int, string> { [0] = "Malpha", [1] = "Mbravo" });
    }

    [Fact]
    public void Rejects_a_bad_schema_version_and_empty_or_unknown_content() {
        var doc = ValidDoc();
        doc["schemaVersion"] = 2;
        Rejects(doc, "schemaVersion must be 1");

        doc = ValidDoc();
        doc["abilities"] = new JsonArray();
        Rejects(doc, "abilities must not be empty");

        doc = ValidDoc();
        doc["extra"] = 1;
        Assert.Throws<InvalidOperationException>(() => MimicConfig.Parse(doc.ToJsonString(), OneClass()));
        Assert.Throws<InvalidOperationException>(() => MimicConfig.Parse("null", OneClass()));
        Assert.Throws<InvalidOperationException>(() => MimicConfig.Parse("{", OneClass()));
    }

    [Fact]
    public void Rejects_bad_ids_keys_and_duplicates() {
        var doc = ValidDoc();
        At(doc, 0)["id"] = 1999;
        Rejects(doc, "id must be in [2000, 2199]");

        doc = ValidDoc();
        At(doc, 1)["id"] = 2000;
        Rejects(doc, "id is duplicated");

        doc = ValidDoc();
        At(doc, 1)["key"] = "alpha";
        Rejects(doc, "key 'alpha' is duplicated");

        doc = ValidDoc();
        At(doc, 0)["key"] = "Bad Key";
        Rejects(doc, "key must match");

        doc = ValidDoc();
        At(doc, 1)["monsterId"] = 0;
        Rejects(doc, "already has an ability");
    }

    [Fact]
    public void Rejects_a_monster_ability_without_monster_and_a_fusion_with_one() {
        var doc = ValidDoc();
        At(doc, 0).Remove("monsterId");
        Rejects(doc, "monsterId is required");

        doc = ValidDoc();
        At(doc, 0).Remove("monsterName");
        Rejects(doc, "monsterName is required");

        doc = ValidDoc();
        At(doc, 2)["monsterId"] = 5;
        Rejects(doc, "a fusion result has no monster");

        doc = ValidDoc();
        At(doc, 2)["dropChance"] = 0.1;
        Rejects(doc, "a fusion result does not drop");

        doc = ValidDoc();
        At(doc, 0)["dropChance"] = 1.5;
        Rejects(doc, "dropChance must be in [0, 1]");
    }

    [Fact]
    public void Rejects_a_short_cooldown_and_bad_costs() {
        var doc = ValidDoc();
        At(doc, 0)["cooldownMs"] = 29_999;
        Rejects(doc, "cooldownMs must be in [30000, 600000]");

        doc = ValidDoc();
        At(doc, 0)["cooldownMs"] = 600_001;
        Rejects(doc, "cooldownMs must be in");

        doc = ValidDoc();
        At(doc, 0).Remove("manaCost");
        Rejects(doc, "manaCost must be in");

        doc = ValidDoc();
        At(doc, 0)["spellId"] = 1000;
        Rejects(doc, "spellId must be a legacy Spells.json id");
    }

    [Fact]
    public void Rejects_unknown_kind_rarity_and_status() {
        var doc = ValidDoc();
        At(doc, 0)["kind"] = "mystery";
        Rejects(doc, "kind 'mystery'");

        doc = ValidDoc();
        At(doc, 0)["kind"] = "3";
        Rejects(doc, "kind '3'");

        doc = ValidDoc();
        At(doc, 0)["rarity"] = "godly";
        Rejects(doc, "rarity 'godly'");

        doc = ValidDoc();
        At(doc, 0)["status"] = "soon";
        Rejects(doc, "status must be 'ready' or 'planned'");

        doc = ValidDoc();
        At(doc, 0)["status"] = "ready";
        Rejects(doc, "a ready ability needs a handler");

        doc = ValidDoc();
        At(doc, 0)["handler"] = "do_it";
        Rejects(doc, "a planned ability has no handler");
    }

    [Fact]
    public void A_ready_ability_with_only_a_spell_is_castable_and_a_ready_one_with_neither_is_not() {
        var doc = ValidDoc();
        At(doc, 0)["status"] = "ready";
        Rejects(doc, "a ready ability needs a handler or a spellId");

        At(doc, 0)["spellId"] = 21;
        var cfg = MimicConfig.Parse(doc.ToJsonString(), OneClass());

        Assert.True(cfg.TryGet(2000, out var a));
        Assert.True(a.IsReady);
        Assert.Null(a.Handler);
        Assert.Equal(21, a.SpellId);
    }

    [Fact]
    public void A_ready_ability_with_a_handler_is_castable() {
        var doc = ValidDoc();
        At(doc, 0)["status"] = "ready";
        At(doc, 0)["handler"] = "buff_iron_skin";

        var cfg = MimicConfig.Parse(doc.ToJsonString(), OneClass());

        Assert.True(cfg.TryGet(2000, out var a));
        Assert.True(a.IsReady);
        Assert.Equal("buff_iron_skin", a.Handler);
    }

    [Fact]
    public void Mythic_requires_a_raid() {
        var doc = ValidDoc();
        At(doc, 0)["rarity"] = "mythic";
        Rejects(doc, "a mythic ability requires a raid");

        At(doc, 0)["requiresRaid"] = true;
        Assert.True(MimicConfig.Parse(doc.ToJsonString(), OneClass()).TryGet(2000, out var a));
        Assert.True(a.RequiresRaid);
    }

    [Fact]
    public void Class_exclusivity_must_name_known_distinct_classes() {
        var doc = ValidDoc();
        At(doc, 0)["classIds"] = new JsonArray { 0 };
        var cfg = MimicConfig.Parse(doc.ToJsonString(), OneClass());
        Assert.True(cfg.TryGet(2000, out var a));
        Assert.True(a.AllowsClass(0));
        Assert.False(a.AllowsClass(1));
        Assert.False(a.AllowsClass(null));
        Assert.True(cfg.TryGet(2001, out var open));
        Assert.True(open.AllowsClass(null));
        Assert.True(open.AllowsClass(7));

        doc = ValidDoc();
        At(doc, 0)["classIds"] = new JsonArray { 9 };
        Rejects(doc, "classIds must be distinct ids of Classes.json");

        doc = ValidDoc();
        At(doc, 0)["classIds"] = new JsonArray(0, 0);
        Rejects(doc, "classIds must be distinct");
    }

    [Fact]
    public void Rejects_incoherent_fusion_recipes() {
        var doc = ValidDoc();
        doc["fusions"]![0]!["a"] = 2000;
        Rejects(doc, "the two essences must differ");

        doc = ValidDoc();
        doc["fusions"]![0]!["a"] = 2100;
        Rejects(doc, "must be a monster ability of the file");

        doc = ValidDoc();
        doc["fusions"]![0]!["result"] = 2001;
        Rejects(doc, "must be a fusion ability of the file");

        doc = ValidDoc();
        doc["fusions"]![0]!["result"] = 2101;
        Rejects(doc, "must be a fusion ability of the file");

        doc = ValidDoc();
        doc["fusions"] = new JsonArray();
        Rejects(doc, "has no recipe");

        doc = ValidDoc();
        doc["abilities"]!.AsArray().Add(Ability(2101, "other"));
        doc["fusions"]!.AsArray().Add(new JsonObject { ["a"] = 2000, ["b"] = 2001, ["result"] = 2101 });
        Rejects(doc, "is already a recipe");

        doc = ValidDoc();
        doc["abilities"]!.AsArray().Add(Ability(2002, "charlie"));
        doc["fusions"]!.AsArray().Add(new JsonObject { ["a"] = 2000, ["b"] = 2002, ["result"] = 2100 });
        Rejects(doc, "already has a recipe");
    }

    [Fact]
    public void Rejects_bad_slot_and_xp_tables() {
        var doc = ValidDoc();
        doc.Remove("slots");
        Rejects(doc, "slots section missing");

        doc = ValidDoc();
        doc["slots"]!["max"] = 10;
        Rejects(doc, "slots.max must be in");

        doc = ValidDoc();
        doc["slots"]!["levelsPerSlot"] = 0;
        Rejects(doc, "slots.levelsPerSlot");

        doc = ValidDoc();
        doc.Remove("xp");
        Rejects(doc, "xp section missing");

        doc = ValidDoc();
        doc["xp"]!["maxLevel"] = 11;
        Rejects(doc, "xp.maxLevel must be in [1, 10]");

        doc = ValidDoc();
        doc["xp"]!["thresholds"] = new JsonArray { 10 };
        Rejects(doc, "xp.thresholds must have maxLevel - 1 = 2 entries");

        doc = ValidDoc();
        doc["xp"]!["thresholds"] = new JsonArray(10, 10);
        Rejects(doc, "strictly increasing");

        doc = ValidDoc();
        doc["xp"]!["powerPerLevel"] = 2;
        Rejects(doc, "xp.powerPerLevel");
    }

    [Fact]
    public void Reports_every_problem_in_one_exception() {
        var doc = ValidDoc();
        At(doc, 0)["cooldownMs"] = 1;
        At(doc, 1)["key"] = "Bad";

        var ex = Assert.Throws<InvalidOperationException>(() => MimicConfig.Parse(doc.ToJsonString(), OneClass()));

        Assert.Contains("cooldownMs", ex.Message);
        Assert.Contains("key must match", ex.Message);
    }
}

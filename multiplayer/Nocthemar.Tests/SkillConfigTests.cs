using System.Text.Json.Nodes;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T38: Skills.json catalog (complete class x branch x slot grid) and its validation.</summary>
public class SkillConfigTests {
    private static readonly string[] Names = ["Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel"];

    private static ClassConfig OneClass() => ClassConfig.Parse(
        """{"schemaVersion":1,"classes":[{"id":0,"key":"knight","name":"Knight","role":"r","branches":[{"id":0,"key":"aa","name":"A","role":"r"},{"id":1,"key":"bb","name":"B","role":"r"},{"id":2,"key":"cc","name":"C","role":"r"}]}]}""");

    /// <summary>A valid document for <see cref="OneClass"/>: 24 planned skills.</summary>
    private static JsonObject ValidDoc() {
        var skills = new JsonArray();
        var kinds = new[] { "passive", "active", "passive", "active", "passive", "active", "passive", "ultimate" };
        for (var b = 0; b < 3; b++) {
            for (var s = 0; s < 8; s++) {
                skills.Add(new JsonObject {
                    ["id"] = SkillIds.Make(0, b, s), ["key"] = $"skill_{b}_{Names[s].ToLowerInvariant()}", ["name"] = Names[s], ["class"] = 0, ["branch"] = b, ["slot"] = s,
                    ["level"] = SkillIds.RequiredLevel(s), ["kind"] = kinds[s], ["cost"] = s == 7 ? 2 : 1, ["desc"] = "d", ["status"] = "planned",
                });
            }
        }

        return new JsonObject { ["schemaVersion"] = 1, ["skills"] = skills };
    }

    private static JsonObject Skill(JsonObject doc, int index) => doc["skills"]![index]!.AsObject();

    private static void Rejects(JsonObject doc, string expected) {
        var ex = Assert.Throws<InvalidOperationException>(() => SkillConfig.Parse(doc.ToJsonString(), OneClass()));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void A_valid_document_parses() {
        var cfg = SkillConfig.Parse(ValidDoc().ToJsonString(), OneClass());

        Assert.Equal(24, cfg.Skills.Count);
        Assert.Equal(24, cfg.ForClass(0).Count);
        Assert.Empty(cfg.ForClass(1));
        Assert.Empty(cfg.ForClass(-1));
    }

    [Fact]
    public void Shipped_catalog_is_the_complete_grid_of_120_skills() {
        var classes = TestSupport.ShippedClasses();
        var cfg = TestSupport.ShippedSkills();

        Assert.Equal(120, cfg.Skills.Count);
        foreach (var c in classes.Classes) {
            Assert.Equal(24, cfg.ForClass(c.Id).Count);
            for (var b = 0; b < 3; b++) {
                var branch = cfg.ForClass(c.Id).Where(s => s.Branch == b).OrderBy(s => s.Slot).ToArray();
                Assert.Equal(8, branch.Length);
                Assert.True(new[] { 1, 3, 6, 10, 14, 18, 24, 30 }.SequenceEqual(branch.Select(s => s.Level)));
                Assert.True(new[] { SkillKind.Passive, SkillKind.Active, SkillKind.Passive, SkillKind.Active, SkillKind.Passive, SkillKind.Active, SkillKind.Passive, SkillKind.Ultimate }.SequenceEqual(branch.Select(s => s.Kind)));
                Assert.Equal(7, branch.Count(s => s.Cost == 1));
                Assert.Equal(2, branch[7].Cost);
            }
        }
    }

    [Fact]
    public void Shipped_catalog_counts_per_kind_and_ids() {
        var cfg = TestSupport.ShippedSkills();

        Assert.Equal(60, cfg.Skills.Count(s => s.Kind == SkillKind.Passive));
        Assert.Equal(45, cfg.Skills.Count(s => s.Kind == SkillKind.Active));
        Assert.Equal(15, cfg.Skills.Count(s => s.Kind == SkillKind.Ultimate));
        Assert.Equal(120, cfg.Skills.Select(s => s.Id).Distinct().Count());
        Assert.Equal(cfg.Skills.Select(s => s.Id).Order(), cfg.Skills.Select(s => s.Id));
        Assert.All(cfg.Skills, s => Assert.True(SkillIds.IsClassSkill(s.Id)));
    }

    [Fact]
    public void Shipped_ready_passives_carry_modifiers_owned_by_the_skill_source() {
        var cfg = TestSupport.ShippedSkills();

        Assert.True(cfg.TryGet(1000, out var posture));
        Assert.Equal("posture_de_fer", posture.Key);
        Assert.True(posture.IsReady);
        Assert.Equal(2, posture.Modifiers.Count);
        Assert.All(posture.Modifiers, m => Assert.Equal(ModifierSources.Skill(1000), m.SourceId));
        Assert.Contains(posture.Modifiers, m => m is { Stat: StatId.ArmorMult, Kind: ModifierKind.PercentAdd, Value: 0.15 });
        Assert.Contains(posture.Modifiers, m => m is { Stat: StatId.MoveSpeedMult, Value: -0.10 });

        var ready = cfg.Skills.Where(s => s.IsReady).ToArray();
        Assert.All(ready, s => Assert.Equal(SkillKind.Passive, s.Kind));
        Assert.All(ready, s => Assert.NotEmpty(s.Modifiers));
        Assert.All(cfg.Skills.Where(s => !s.IsReady), s => Assert.Empty(s.Modifiers));
    }

    [Fact]
    public void Shipped_ready_passives_apply_through_the_stat_pipeline() {
        var cfg = TestSupport.ShippedSkills();
        var pipe = new StatPipeline(TestSupport.Progression());
        var scaly = cfg.Skills.Single(s => s.Key == "peau_ecailleuse");

        foreach (var m in scaly.Modifiers) {
            pipe.Add(m);
        }

        Assert.Equal(1.10, pipe.Get(StatId.ArmorMult), 6);
        Assert.Equal(1, pipe.RemoveSource(scaly.SourceId));
        Assert.Equal(1.0, pipe.Get(StatId.ArmorMult), 6);
    }

    [Fact]
    public void Shipped_skill_keys_are_unique_and_every_name_and_description_is_filled() {
        var cfg = TestSupport.ShippedSkills();

        Assert.Equal(cfg.Skills.Count, cfg.Skills.Select(s => s.Key).Distinct().Count());
        Assert.All(cfg.Skills, s => Assert.False(string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.Description)));
    }

    [Fact]
    public void Missing_positions_are_refused() {
        var doc = ValidDoc();
        doc["skills"]!.AsArray().RemoveAt(23);

        Rejects(doc, "expected 24 skills");
    }

    [Fact]
    public void Duplicated_ids_and_keys_are_refused() {
        var doc = ValidDoc();
        Skill(doc, 1)["key"] = Skill(doc, 0)["key"]!.GetValue<string>();
        Rejects(doc, "is duplicated");

        var dup = ValidDoc();
        dup["skills"]!.AsArray()[1] = JsonNode.Parse(Skill(dup, 0).ToJsonString());
        Rejects(dup, "is duplicated");
    }

    [Fact]
    public void Id_position_level_kind_and_cost_must_agree() {
        var d1 = ValidDoc();
        Skill(d1, 3)["id"] = 1999;
        Rejects(d1, "id must be 1003");

        var d2 = ValidDoc();
        Skill(d2, 3)["level"] = 4;
        Rejects(d2, "level must be 10");

        var d3 = ValidDoc();
        Skill(d3, 3)["kind"] = "passive";
        Rejects(d3, "kind must be active");

        var d4 = ValidDoc();
        Skill(d4, 0)["cost"] = 0;
        Rejects(d4, "cost must be in [1, 5]");

        var d5 = ValidDoc();
        Skill(d5, 0)["class"] = 3;
        Rejects(d5, "not a position of a listed class");

        var d6 = ValidDoc();
        Skill(d6, 0)["slot"] = 8;
        Rejects(d6, "not a position of a listed class");

        var d7 = ValidDoc();
        Skill(d7, 0)["kind"] = "Charm";
        Rejects(d7, "kind 'Charm' must be");
    }

    [Fact]
    public void Text_fields_are_checked() {
        var d1 = ValidDoc();
        Skill(d1, 0)["key"] = "Bad Key";
        Rejects(d1, "key must match");

        var d2 = ValidDoc();
        Skill(d2, 0)["name"] = "";
        Rejects(d2, "name must be 1-64");

        var d3 = ValidDoc();
        Skill(d3, 0)["desc"] = new string('x', 201);
        Rejects(d3, "desc must be 1-200");

        var d4 = ValidDoc();
        Skill(d4, 0)["status"] = "soon";
        Rejects(d4, "status must be 'ready' or 'planned'");
    }

    [Fact]
    public void Planned_skills_carry_no_behaviour() {
        var d1 = ValidDoc();
        Skill(d1, 1)["handler"] = "bone_wall";
        Rejects(d1, "planned skill must not carry");

        var d2 = ValidDoc();
        Skill(d2, 0)["modifiers"] = JsonNode.Parse("""[{"stat":"DamageMult","kind":"PercentAdd","value":0.1}]""");
        Rejects(d2, "planned skill must not carry");

        var d3 = ValidDoc();
        Skill(d3, 1)["cooldownMs"] = 1000;
        Rejects(d3, "planned skill must not carry");
    }

    [Fact]
    public void Ready_passive_rules() {
        var ok = ValidDoc();
        Skill(ok, 0)["status"] = "ready";
        Skill(ok, 0)["modifiers"] = JsonNode.Parse("""[{"stat":"DamageMult","kind":"PercentAdd","value":0.1}]""");
        Assert.True(SkillConfig.Parse(ok.ToJsonString(), OneClass()).Skills[0].IsReady);

        var handlerOnly = ValidDoc();
        Skill(handlerOnly, 0)["status"] = "ready";
        Skill(handlerOnly, 0)["handler"] = "on_hit_bonus";
        Assert.Equal("on_hit_bonus", SkillConfig.Parse(handlerOnly.ToJsonString(), OneClass()).Skills[0].Handler);

        var empty = ValidDoc();
        Skill(empty, 0)["status"] = "ready";
        Rejects(empty, "ready passive needs modifiers and/or a handler");

        var cooldown = ValidDoc();
        Skill(cooldown, 0)["status"] = "ready";
        Skill(cooldown, 0)["handler"] = "on_hit_bonus";
        Skill(cooldown, 0)["cooldownMs"] = 500;
        Rejects(cooldown, "passive has no cooldownMs or manaCost");
    }

    [Fact]
    public void Ready_active_rules() {
        var ok = ValidDoc();
        Skill(ok, 1)["status"] = "ready";
        Skill(ok, 1)["handler"] = "bone_wall";
        Skill(ok, 1)["cooldownMs"] = 20000;
        Skill(ok, 1)["manaCost"] = 30;
        var skill = SkillConfig.Parse(ok.ToJsonString(), OneClass()).Skills[1];
        Assert.True(skill.IsReady);
        Assert.Equal((20000, 30), (skill.CooldownMs, skill.ManaCost));

        var noHandler = ValidDoc();
        Skill(noHandler, 1)["status"] = "ready";
        Skill(noHandler, 1)["cooldownMs"] = 1000;
        Rejects(noHandler, "needs a handler");

        var noCooldown = ValidDoc();
        Skill(noCooldown, 1)["status"] = "ready";
        Skill(noCooldown, 1)["handler"] = "bone_wall";
        Rejects(noCooldown, "needs cooldownMs >= 1");

        var mods = ValidDoc();
        Skill(mods, 1)["status"] = "ready";
        Skill(mods, 1)["handler"] = "bone_wall";
        Skill(mods, 1)["cooldownMs"] = 1000;
        Skill(mods, 1)["modifiers"] = JsonNode.Parse("""[{"stat":"DamageMult","kind":"PercentAdd","value":0.1}]""");
        Rejects(mods, "carries no passive modifiers");

        var badHandler = ValidDoc();
        Skill(badHandler, 1)["handler"] = "Bad Handler";
        Rejects(badHandler, "handler must match");
    }

    [Fact]
    public void Modifier_validation_is_shared_with_races() {
        var d1 = ValidDoc();
        Skill(d1, 0)["modifiers"] = JsonNode.Parse("""[{"stat":"Charisma","kind":"Flat","value":1}]""");
        Rejects(d1, "'Charisma' is not a StatId");

        var d2 = ValidDoc();
        Skill(d2, 0)["modifiers"] = JsonNode.Parse("""[{"stat":"ResistMagic","kind":"PercentAdd","value":0.1}]""");
        Rejects(d2, "takes Flat modifiers");

        var d3 = ValidDoc();
        Skill(d3, 0)["modifiers"] = JsonNode.Parse("""[{"stat":"DamageMult","kind":"PercentAdd","value":0.1},{"stat":"DamageMult","kind":"PercentAdd","value":0.2}]""");
        Rejects(d3, "declared twice");
    }

    [Fact]
    public void Document_level_errors() {
        Assert.Throws<InvalidOperationException>(() => SkillConfig.Parse("not json", OneClass()));
        Assert.Throws<InvalidOperationException>(() => SkillConfig.Parse("null", OneClass()));
        Rejects(new JsonObject { ["schemaVersion"] = 2, ["skills"] = new JsonArray() }, "schemaVersion must be 1");
        Rejects(new JsonObject { ["schemaVersion"] = 1, ["skills"] = new JsonArray() }, "skills must not be empty");

        var extra = ValidDoc();
        extra["bogus"] = 1;
        Rejects(extra, "bogus");
    }
}

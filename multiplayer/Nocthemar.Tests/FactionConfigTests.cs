using System.Text.Json.Nodes;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T73: Factions.json (4 factions, rivals, perks by tier, reputation rules) and its validation.</summary>
public class FactionConfigTests {
    private static string ShippedJson() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Factions.json"));

    private static FactionConfig Shipped() => FactionConfig.Parse(ShippedJson());

    private static JsonObject Doc() => JsonNode.Parse(ShippedJson())!.AsObject();

    private static JsonObject Faction(JsonObject doc, int index) => doc["factions"]![index]!.AsObject();

    private static void Rejects(JsonObject doc, string expected) {
        var ex = Assert.Throws<InvalidOperationException>(() => FactionConfig.Parse(doc.ToJsonString()));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void The_shipped_file_has_the_four_factions_with_contiguous_ids() {
        var cfg = Shipped();

        Assert.Equal(4, cfg.Count);
        Assert.Equal(new[] { "chant_de_fer", "choeur_abyssal", "meutes_libres", "forgeurs" }, cfg.Factions.Select(f => f.Key).ToArray());
        Assert.Equal(new[] { 0, 1, 2, 3 }, cfg.Factions.Select(f => f.Id).ToArray());
        Assert.True(cfg.Contains(3));
        Assert.False(cfg.Contains(4));
        Assert.False(cfg.Contains(-1));
        Assert.True(cfg.TryGet(1, out var choeur));
        Assert.Equal("choeur_abyssal", choeur.Key);
        Assert.False(cfg.TryGet(9, out _));
    }

    [Fact]
    public void Name_plate_colors_follow_the_design() {
        var cfg = Shipped();

        Assert.Equal("#c0c4cc", cfg.Factions[0].Colors[0]);
        Assert.Equal("#7b4fbf", cfg.Factions[1].Colors[0]);
        Assert.Equal("#3f6b3a", cfg.Factions[2].Colors[0]);
        Assert.Equal("#b8923a", cfg.Factions[3].Colors[0]);
    }

    [Fact]
    public void Rivals_are_symmetric_pairs() {
        var cfg = Shipped();

        Assert.Equal(new[] { 1 }, cfg.Factions[0].Rivals.ToArray());
        Assert.Equal(new[] { 0 }, cfg.Factions[1].Rivals.ToArray());
        Assert.Equal(new[] { 3 }, cfg.Factions[2].Rivals.ToArray());
        Assert.Equal(new[] { 2 }, cfg.Factions[3].Rivals.ToArray());
    }

    [Fact]
    public void Reputation_rules_match_the_plan() {
        var rules = Shipped().Rules;

        Assert.Equal(-10000, rules.Min);
        Assert.Equal(10000, rules.Max);
        Assert.Equal(10, rules.JoinLevel);
        Assert.Equal(5, rules.KillBase);
        Assert.Equal(0.5, rules.VictimRatio);
        Assert.Equal(1.0, rules.RivalRatio);
    }

    [Theory]
    [InlineData(-10000, ReputationTier.Hostile)]
    [InlineData(-1001, ReputationTier.Hostile)]
    [InlineData(-1000, ReputationTier.Neutral)]
    [InlineData(0, ReputationTier.Neutral)]
    [InlineData(999, ReputationTier.Neutral)]
    [InlineData(1000, ReputationTier.Friendly)]
    [InlineData(2999, ReputationTier.Friendly)]
    [InlineData(3000, ReputationTier.Honored)]
    [InlineData(5999, ReputationTier.Honored)]
    [InlineData(6000, ReputationTier.Exalted)]
    [InlineData(10000, ReputationTier.Exalted)]
    public void Tiers_start_exactly_at_their_thresholds(int value, ReputationTier expected) {
        Assert.Equal(expected, Shipped().Rules.TierOf(value));
    }

    [Fact]
    public void Floor_of_a_tier_is_its_first_value_and_clamp_keeps_the_range() {
        var rules = Shipped().Rules;

        foreach (var tier in Enum.GetValues<ReputationTier>()) {
            Assert.Equal(tier, rules.TierOf(rules.FloorOf(tier)));
        }

        Assert.Equal(-10000, rules.FloorOf(ReputationTier.Hostile));
        Assert.Equal(10000, rules.Clamp(123456));
        Assert.Equal(-10000, rules.Clamp(-123456));
        Assert.Equal(42, rules.Clamp(42));
    }

    [Fact]
    public void Perks_accumulate_with_the_tier_and_belong_to_the_faction_source() {
        foreach (var faction in Shipped().Factions) {
            Assert.Empty(faction.ModifiersAt(ReputationTier.Hostile));
            Assert.Empty(faction.ModifiersAt(ReputationTier.Neutral));
            Assert.Single(faction.ModifiersAt(ReputationTier.Friendly));
            Assert.Equal(2, faction.ModifiersAt(ReputationTier.Honored).Length);
            Assert.Equal(3, faction.ModifiersAt(ReputationTier.Exalted).Length);
            Assert.Equal(ModifierSources.Faction(faction.Id), faction.SourceId);
            Assert.All(faction.ModifiersAt(ReputationTier.Exalted), m => Assert.Equal(faction.SourceId, m.SourceId));
        }

        var first = Shipped().Factions[0].ModifiersAt(ReputationTier.Friendly)[0];
        Assert.Equal(StatId.ResistMagic, first.Stat);
        Assert.Equal(ModifierKind.Flat, first.Kind);
        Assert.Equal(0.03, first.Value);
    }

    [Fact]
    public void Faction_sources_do_not_collide_with_the_other_owner_families() {
        var cfg = Shipped();

        Assert.All(cfg.Factions, f => Assert.Equal(ModifierSourceCategory.Faction, ModifierSources.CategoryOf(f.SourceId)));
        Assert.Equal(cfg.Count, cfg.Factions.Select(f => f.SourceId).Distinct().Count());
    }

    [Fact]
    public void A_wrong_schema_version_is_rejected() {
        var doc = Doc();
        doc["schemaVersion"] = 2;

        Rejects(doc, "schemaVersion must be 1");
    }

    [Fact]
    public void An_unknown_member_is_rejected() {
        var doc = Doc();
        doc["bogus"] = 1;

        Rejects(doc, "Invalid Factions.json");
    }

    [Fact]
    public void A_missing_reputation_block_is_rejected() {
        var doc = Doc();
        doc.Remove("reputation");

        Rejects(doc, "reputation is missing");
    }

    [Fact]
    public void Ratios_outside_zero_one_are_rejected() {
        var doc = Doc();
        doc["reputation"]!["victimRatio"] = 1.5;

        Rejects(doc, "victimRatio must be in [0, 1]");
    }

    [Fact]
    public void Tiers_must_increase_around_zero() {
        var doc = Doc();
        doc["reputation"]!["tiers"]!["friendly"] = 7000;

        Rejects(doc, "reputation tiers must satisfy");
    }

    [Fact]
    public void A_range_that_does_not_straddle_zero_is_rejected() {
        var doc = Doc();
        doc["reputation"]!["min"] = 5;

        Rejects(doc, "reputation.min must be in");
    }

    [Fact]
    public void Ids_must_be_contiguous() {
        var doc = Doc();
        Faction(doc, 1)["id"] = 7;

        Rejects(doc, "factions[1].id must be 1");
    }

    [Fact]
    public void Keys_must_be_unique() {
        var doc = Doc();
        Faction(doc, 2)["key"] = "chant_de_fer";

        Rejects(doc, "is used twice");
    }

    [Fact]
    public void Colors_must_be_hex() {
        var doc = Doc();
        Faction(doc, 0)["colors"] = new JsonArray("red");

        Rejects(doc, "colors must list 1-3 colors");
    }

    [Fact]
    public void A_rival_must_exist() {
        var doc = Doc();
        Faction(doc, 0)["rivals"] = new JsonArray(9);

        Rejects(doc, "faction 9 does not exist");
    }

    [Fact]
    public void A_faction_cannot_be_its_own_rival() {
        var doc = Doc();
        Faction(doc, 0)["rivals"] = new JsonArray(0);

        Rejects(doc, "its own rival");
    }

    [Fact]
    public void Rivalry_must_go_both_ways() {
        var doc = Doc();
        Faction(doc, 0)["rivals"] = new JsonArray(1, 2);

        Rejects(doc, "but not the other way round");
    }

    [Fact]
    public void A_perk_tier_must_be_friendly_honored_or_exalted() {
        var doc = Doc();
        Faction(doc, 0)["perks"]![0]!["tier"] = "Neutral";

        Rejects(doc, "must be Friendly, Honored or Exalted");
    }

    [Fact]
    public void A_perk_tier_cannot_be_declared_twice() {
        var doc = Doc();
        Faction(doc, 0)["perks"]![1]!["tier"] = "Friendly";

        Rejects(doc, "declared twice");
    }

    [Fact]
    public void A_perk_needs_modifiers() {
        var doc = Doc();
        Faction(doc, 0)["perks"]![0]!["modifiers"] = new JsonArray();

        Rejects(doc, "must list at least one modifier");
    }

    [Fact]
    public void A_perk_modifier_must_name_a_known_stat() {
        var doc = Doc();
        Faction(doc, 0)["perks"]![0]!["modifiers"]![0]!["stat"] = "Nope";

        Rejects(doc, "is not a StatId");
    }
}

using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T89: Zones.json parsing and validation (ids, levels, factions, points of interest) and its checks against the worlds, the level ceiling and the map size.</summary>
public class ZoneConfigTests {
    private static string Json(string zones) => "{\"schemaVersion\":1,\"zones\":[" + zones + "]}";

    private static string Zone(string id = "valdorne", string name = "Valdorne", int min = 1, int max = 15, int entry = 1, string faction = "0", string pois = "")
        => "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"levelMin\":" + min + ",\"levelMax\":" + max + ",\"minLevel\":" + entry
            + (faction == "" ? "" : ",\"factionId\":" + faction) + (pois == "" ? "" : ",\"pois\":[" + pois + "]") + " }";

    private static string Poi(string key = "gate_east", string name = "East gate", string kind = "Portal", int x = 10, int y = 20)
        => "{\"key\":\"" + key + "\",\"name\":\"" + name + "\",\"kind\":\"" + kind + "\",\"x\":" + x + ",\"y\":" + y + " }";

    private static ZoneConfig Parse(string json) => ZoneConfig.Parse(json, TestSupport.ShippedFactions());

    private static void Rejects(string json, string expected) {
        var ex = Assert.Throws<InvalidOperationException>(() => Parse(json));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void The_shipped_file_loads() {
        var zones = TestSupport.ShippedZones();

        Assert.Equal(zones.Zones.Count, zones.Count);
        Assert.Equal(zones.Zones.Count, zones.Zones.Select(z => z.Id).Distinct().Count());
        Assert.All(zones.Zones, z => Assert.InRange(z.MinLevel, 1, z.LevelMax));
    }

    [Fact]
    public void A_valid_zone_exposes_its_fields() {
        var config = Parse(Json(Zone(min: 10, max: 25, entry: 8, faction: "2", pois: Poi() + "," + Poi("forge", "Soul Forge", "Npc", 5, 6))));

        var zone = Assert.Single(config.Zones);
        Assert.Equal(("valdorne", "Valdorne", 10, 25, 8, (int?)2), (zone.Id, zone.Name, zone.LevelMin, zone.LevelMax, zone.MinLevel, zone.FactionId));
        Assert.Equal(new[] { new ZonePoi("gate_east", "East gate", ZonePoiKind.Portal, 10, 20), new ZonePoi("forge", "Soul Forge", ZonePoiKind.Npc, 5, 6) }, zone.Pois.ToArray());
        Assert.True(config.TryGet("valdorne", out _));
        Assert.False(config.TryGet("aresden", out _));
    }

    [Fact]
    public void A_zone_may_have_no_faction_and_no_points() {
        var zone = Parse(Json(Zone(faction: ""))).Zones.Single();

        Assert.Null(zone.FactionId);
        Assert.Empty(zone.Pois);
    }

    [Fact]
    public void An_empty_zone_list_is_valid_and_empty_config_has_no_zone() {
        Assert.Equal(0, Parse(Json("")).Count);
        Assert.Equal(0, ZoneConfig.Empty.Count);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"zones\":[]}", "schemaVersion must be 1")]
    [InlineData("{\"schemaVersion\":1}", "zones is required")]
    [InlineData("{\"schemaVersion\":1,\"zones\":[],\"extra\":1}", "Invalid Zones.json")]
    [InlineData("not json", "Invalid Zones.json")]
    [InlineData("null", "empty document")]
    public void The_document_is_checked(string json, string expected) => Rejects(json, expected);

    [Theory]
    [InlineData("Valdorne", "must match")]
    [InlineData("v", "must match")]
    [InlineData("1abc", "must match")]
    [InlineData("val dorne", "must match")]
    public void Ids_are_ascii_keys(string id, string expected) => Rejects(Json(Zone(id: id)), expected);

    [Fact]
    public void Ids_are_unique_and_may_hold_a_hyphen() {
        Rejects(Json(Zone() + "," + Zone()), "is used twice");
        Assert.Equal("infernia-a", Parse(Json(Zone(id: "infernia-a"))).Zones.Single().Id);
    }

    [Theory]
    [InlineData("", "name must be 1-40")]
    [InlineData("0123456789012345678901234567890123456789X", "name must be 1-40")]
    public void Names_are_bounded(string name, string expected) => Rejects(Json(Zone(name: name)), expected);

    [Fact]
    public void Levels_are_ordered() {
        Rejects(Json(Zone(min: 0, max: 5, entry: 1)), "levelMin must be at least 1");
        Rejects(Json(Zone(min: 10, max: 5, entry: 1)), "levelMax 5 must not be below levelMin 10");
        Rejects(Json(Zone(min: 1, max: 15, entry: 0)), "minLevel must be in [1, levelMax 15]");
        Rejects(Json(Zone(min: 1, max: 15, entry: 16)), "minLevel must be in [1, levelMax 15]");
    }

    [Fact]
    public void The_faction_must_exist() => Rejects(Json(Zone(faction: "9")), "factionId 9 is not a faction");

    [Theory]
    [InlineData("Bad", "Portal", 1, 1, "key 'Bad' must match")]
    [InlineData("ok_key", "Door", 1, 1, "kind 'Door' must be Portal, Npc or Landmark")]
    [InlineData("ok_key", "Portal", -1, 1, "must not be negative")]
    [InlineData("ok_key", "Portal", 1, -1, "must not be negative")]
    public void Points_of_interest_are_checked(string key, string kind, int x, int y, string expected)
        => Rejects(Json(Zone(pois: Poi(key, "Name", kind, x, y))), expected);

    [Fact]
    public void Point_keys_are_unique_inside_a_zone() => Rejects(Json(Zone(pois: Poi() + "," + Poi())), "is used twice");

    [Fact]
    public void Every_problem_is_listed_at_once() {
        var ex = Assert.Throws<InvalidOperationException>(() => Parse(Json(Zone(id: "X", faction: "9"))));

        Assert.Contains("must match", ex.Message);
        Assert.Contains("factionId 9", ex.Message);
    }

    [Fact]
    public void Zones_must_be_game_worlds() {
        var config = Parse(Json(Zone() + "," + Zone(id: "sylvaneth", name: "Sylvaneth")));

        config.ValidateWorlds(["valdorne", "sylvaneth", "aresden"]);
        var ex = Assert.Throws<InvalidOperationException>(() => config.ValidateWorlds(["valdorne"]));
        Assert.Contains("sylvaneth", ex.Message);
    }

    [Fact]
    public void Levels_may_not_pass_the_progression_maximum() {
        var config = Parse(Json(Zone(max: 30)));

        config.ValidateLevels(30);
        var ex = Assert.Throws<InvalidOperationException>(() => config.ValidateLevels(29));
        Assert.Contains("levelMax 30", ex.Message);
    }

    [Fact]
    public void Points_must_lie_inside_the_map_and_other_worlds_pass() {
        var config = Parse(Json(Zone(pois: Poi(x: 99, y: 99))));

        config.ValidateMap("valdorne", 100, 100);
        config.ValidateMap("aresden", 1, 1);
        var ex = Assert.Throws<InvalidOperationException>(() => config.ValidateMap("valdorne", 99, 100));
        Assert.Contains("gate_east (99,99)", ex.Message);
    }
}

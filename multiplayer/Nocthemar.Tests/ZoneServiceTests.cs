using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T90: the entry level of a zone and its two checks in GameWorld.</summary>
public class ZoneServiceTests {
    private const string TwoZones =
        "{\"schemaVersion\":1,\"zones\":["
        + "{\"id\":\"valdorne\",\"name\":\"Valdorne\",\"levelMin\":1,\"levelMax\":15,\"minLevel\":1,\"factionId\":0,\"pois\":[{\"key\":\"gate_east\",\"name\":\"East gate\",\"kind\":\"Portal\",\"x\":10,\"y\":20}]},"
        + "{\"id\":\"forgenoire\",\"name\":\"Forge Noire\",\"levelMin\":10,\"levelMax\":25,\"minLevel\":10}]}";

    private static ZoneConfig Zones() => ZoneConfig.Parse(TwoZones, TestSupport.ShippedFactions());

    [Fact]
    public void Below_the_entry_level_the_zone_is_refused_with_its_name_and_level() {
        var service = new ZoneService(Zones());

        Assert.Equal("You must be level 10 to enter Forge Noire.", service.CheckEntry("forgenoire", 9));
        Assert.Equal("You must be level 10 to enter Forge Noire.", service.CheckEntry("forgenoire", 1));
    }

    [Fact]
    public void At_or_above_the_entry_level_the_zone_is_open() {
        var service = new ZoneService(Zones());

        Assert.Null(service.CheckEntry("forgenoire", 10));
        Assert.Null(service.CheckEntry("forgenoire", 50));
        Assert.Null(service.CheckEntry("valdorne", 1));
    }

    [Fact]
    public void A_world_without_a_zone_is_always_open() {
        var service = new ZoneService(Zones());

        Assert.Null(service.CheckEntry("aresden", 1));
        Assert.Null(new ZoneService(ZoneConfig.Empty).CheckEntry("forgenoire", 1));
    }

    [Fact]
    public void The_refusal_text_is_the_one_the_service_gives() {
        var zone = Zones().Zones[1];

        Assert.Equal(ZoneService.LevelTooLow(zone), new ZoneService(Zones()).CheckEntry("forgenoire", 3));
    }

    [Fact]
    public void Content_exposes_the_service_only_when_given_zones() {
        Assert.Null(TestSupport.ShippedContent().Zones);
        var content = new Server.Nocthemar.Skills.NocContent(
            TestSupport.ShippedClasses(), TestSupport.ShippedSkills(), TestSupport.Progression(),
            Server.Nocthemar.Skills.BuiltinSkillHandlers.Register(new Server.Nocthemar.Skills.SkillHandlerRegistry()),
            zones: Zones());

        Assert.NotNull(content.Zones);
    }

    [Fact]
    public void GameWorld_checks_the_entry_level_on_both_world_change_paths_before_moving_the_player() {
        var source = File.ReadAllText(TestSupport.SourceFile("World", "Game", "GameWorld.cs"));
        var handler = source[source.IndexOf("private void HandleWorldChangeRequest", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("private bool RefuseZoneEntry", StringComparison.Ordinal)];

        Assert.Equal(2, CountOf(handler, "RefuseZoneEntry("));
        Assert.Equal(2, CountOf(handler, "player.RequestWorldChange("));
        var requests = AllIndexes(handler, "player.RequestWorldChange(");
        var refusals = AllIndexes(handler, "RefuseZoneEntry(");
        Assert.True(refusals[0] < requests[0] && refusals[1] < requests[1]);
        Assert.Contains("nocContent?.Zones?.CheckEntry(destinationWorldId, player.Level)", source);
        Assert.Contains("CreateNocActionRefused(refusal)", source);
    }

    [Fact]
    public void The_server_validates_zones_against_worlds_levels_and_maps() {
        var source = File.ReadAllText(TestSupport.SourceFile("Server.cs"));

        Assert.Contains("ZoneConfig.LoadAsync(factionConfig)", source);
        Assert.Contains("zoneConfig.ValidateWorlds(gameWorldsById.Keys)", source);
        Assert.Contains("zoneConfig.ValidateLevels(progression.MaxLevel)", source);
        Assert.Contains("zoneConfig.ValidateMap(gw.Id, occupancyTracker.SizeX, occupancyTracker.SizeY)", source);
        Assert.Contains("factionConfig, questConfig, zoneConfig)", source);
    }

    private static int CountOf(string text, string token) => AllIndexes(text, token).Count;

    private static List<int> AllIndexes(string text, string token) {
        var result = new List<int>();
        for (var i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + 1, StringComparison.Ordinal)) {
            result.Add(i);
        }

        return result;
    }
}

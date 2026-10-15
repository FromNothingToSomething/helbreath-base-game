using Google.Protobuf;
using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Net;
using Server.Nocthemar.Skills;
using Xunit;

namespace NocthemarTests;

/// <summary>T91: the zone catalog on the wire (NocCatalog.zones = 14).</summary>
public class ZoneCatalogTests {
    private const string TwoZones =
        "{\"schemaVersion\":1,\"zones\":["
        + "{\"id\":\"valdorne\",\"name\":\"Valdorne\",\"levelMin\":1,\"levelMax\":15,\"minLevel\":1,\"factionId\":0,\"pois\":[{\"key\":\"gate_east\",\"name\":\"East gate\",\"kind\":\"Portal\",\"x\":10,\"y\":20}]},"
        + "{\"id\":\"forgenoire\",\"name\":\"Forge Noire\",\"levelMin\":10,\"levelMax\":25,\"minLevel\":10}]}";

    private static ZoneConfig Zones() => ZoneConfig.Parse(TwoZones, TestSupport.ShippedFactions());

    private static NocCatalog Build(ZoneConfig? zones) =>
        NocCatalogBuilder.Build(TestSupport.ShippedClasses(), TestSupport.ShippedSkills(), new SkillService(TestSupport.ShippedClasses(), TestSupport.ShippedSkills(), TestSupport.Progression()), zones: zones);

    [Fact]
    public void The_catalog_lists_the_zones_in_file_order_with_their_points() {
        var catalog = Build(Zones());

        Assert.Equal(new[] { "valdorne", "forgenoire" }, catalog.Zones.Select(z => z.Id).ToArray());
        var valdorne = catalog.Zones[0];
        Assert.Equal(("Valdorne", 1, 15, 1), (valdorne.Name, valdorne.LevelMin, valdorne.LevelMax, valdorne.MinLevel));
        Assert.True(valdorne.HasFactionId);
        Assert.Equal(0, valdorne.FactionId);
        var poi = Assert.Single(valdorne.Pois);
        Assert.Equal(("gate_east", "East gate", 0, 10, 20), (poi.Key, poi.Name, poi.Kind, poi.X, poi.Y));
        Assert.False(catalog.Zones[1].HasFactionId);
    }

    [Fact]
    public void The_zone_catalog_survives_the_wire() {
        var catalog = Build(Zones());

        var decoded = NocCatalog.Parser.ParseFrom(catalog.ToByteArray());

        Assert.Equal(catalog, decoded);
        Assert.Equal(2, decoded.Zones.Count);
    }

    [Fact]
    public void Without_zones_the_catalog_has_none() {
        Assert.Empty(TestSupport.ShippedContent().Catalog.Zones);
    }

    [Fact]
    public void Content_with_zones_puts_them_in_its_catalog() {
        var content = new NocContent(
            TestSupport.ShippedClasses(), TestSupport.ShippedSkills(), TestSupport.Progression(),
            BuiltinSkillHandlers.Register(new SkillHandlerRegistry()),
            zones: Zones());

        Assert.Equal(2, content.Catalog.Zones.Count);
    }

    [Fact]
    public void Without_zones_the_wire_form_is_unchanged() {
        Assert.Equal(Build(null).ToByteArray(), Build(ZoneConfig.Empty).ToByteArray());
        Assert.Empty(Build(ZoneConfig.Empty).Zones);
    }
}

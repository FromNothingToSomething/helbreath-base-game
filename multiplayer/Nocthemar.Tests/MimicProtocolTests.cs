using Google.Protobuf;
using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Net;
using Server.Utils;
using Xunit;

namespace NocthemarTests;

/// <summary>T67: wire form of the mimicry catalog, the snapshot fields and the request numbers.</summary>
public class MimicProtocolTests {
    private static NocCatalog Catalog() => TestSupport.ShippedContent().Catalog;

    [Fact]
    public void The_catalog_lists_the_22_abilities_and_the_5_fusions() {
        var catalog = Catalog();

        Assert.Equal(27, catalog.Mimics.Count);
        Assert.Equal(22, catalog.Mimics.Count(m => m.Id < MimicIds.FusionBase));
        Assert.Equal(27, catalog.Mimics.Select(m => m.Id).Distinct().Count());
        Assert.Equal(5, catalog.Fusions.Count);
        Assert.All(catalog.Fusions, f => Assert.True(f.A < f.B));
        Assert.All(catalog.Fusions, f => Assert.Contains(catalog.Mimics, m => m.Id == f.Result));
    }

    [Fact]
    public void An_ability_carries_its_rules() {
        var shipped = TestSupport.ShippedMimic();
        var swarm = Catalog().Mimics.Single(m => m.Key == "nuee");
        var source = shipped.Abilities.Single(a => a.Key == "nuee");

        Assert.Equal(MimicIds.Base, swarm.Id);
        Assert.Equal((int)source.Kind, swarm.Kind);
        Assert.Equal((int)source.Rarity, swarm.Rarity);
        Assert.Equal(source.MonsterName, swarm.MonsterName);
        Assert.Equal(source.CooldownMs, swarm.CooldownMs);
        Assert.Equal(source.ManaCost, swarm.ManaCost);
        Assert.Equal(source.IsReady, swarm.Ready);
        Assert.Equal(new[] { 3 }, swarm.ClassIds.ToArray());
        Assert.Equal(MimicIds.EssenceItemId(MimicIds.Base), swarm.EssenceItemId);
        Assert.False(string.IsNullOrEmpty(swarm.Desc));
    }

    [Fact]
    public void A_fusion_result_has_no_monster_and_no_essence() {
        var fusion = Catalog().Mimics.First(m => m.Id >= MimicIds.FusionBase);

        Assert.Equal("", fusion.MonsterName);
        Assert.Equal(0, fusion.EssenceItemId);
        Assert.False(fusion.RequiresRaid);
    }

    [Fact]
    public void Mythic_abilities_are_flagged_as_raid_only() {
        var catalog = Catalog();
        var shipped = TestSupport.ShippedMimic();

        foreach (var ability in shipped.Abilities) {
            Assert.Equal(ability.RequiresRaid, catalog.Mimics.Single(m => m.Id == ability.Id).RequiresRaid);
        }
    }

    [Fact]
    public void The_xp_table_is_in_the_catalog() {
        var catalog = Catalog();
        var xp = TestSupport.ShippedMimic().Xp;

        Assert.Equal(xp.Thresholds.Select(t => (long)t).ToArray(), catalog.MimicXpThresholds.ToArray());
        Assert.Equal(xp.MaxLevel - 1, catalog.MimicXpThresholds.Count);
        Assert.Equal(xp.PowerPerLevel, catalog.MimicPowerPerLevel);
    }

    [Fact]
    public void A_catalog_without_mimicry_has_none_of_it() {
        var content = TestSupport.ShippedContent();

        var catalog = NocCatalogBuilder.Build(content.Classes, content.Skills, content.Rules);

        Assert.Empty(catalog.Mimics);
        Assert.Empty(catalog.Fusions);
        Assert.Empty(catalog.MimicXpThresholds);
        Assert.Equal(0.0, catalog.MimicPowerPerLevel);
        Assert.Equal(content.Catalog.Classes.Count, catalog.Classes.Count);
    }

    [Fact]
    public void The_catalog_survives_a_round_trip_and_stays_small() {
        var original = Catalog();

        var back = NocCatalog.Parser.ParseFrom(original.ToByteArray());

        Assert.Equal(original, back);
        Assert.True(original.CalculateSize() < 64 * 1024);
    }

    [Fact]
    public void The_snapshot_reports_learned_abilities_with_their_level_the_bar_and_the_slots() {
        var player = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent());
        TestSupport.Restore(player, new NocState(
            NocState.CurrentV, 25, 0, 0, RaceId: 0, ClassId: 3,
            Mimics: [new MimicEntry(2005, 35), new MimicEntry(2000)], MimicEquipped: [2005]));

        var snapshot = NocSnapshot.Parser.ParseFrom(player.CreateNocSnapshot().ToByteArray());

        Assert.Equal(new[] { 2000, 2005 }, snapshot.Mimics.Select(m => m.Id).ToArray()); // ascending
        Assert.Equal(new[] { 1, 3 }, snapshot.Mimics.Select(m => m.Level).ToArray()); // 35 xp = level 3 (thresholds 10, 30)
        Assert.Equal(35L, snapshot.Mimics.Single(m => m.Id == 2005).Xp);
        Assert.Equal(new[] { 2005 }, snapshot.MimicEquipped.ToArray());
        Assert.Equal(player.MimicSlotLimit, snapshot.MimicSlots);
        Assert.True(snapshot.MimicSlots >= 1);
    }

    [Fact]
    public void A_player_without_mimicry_sends_an_unchanged_snapshot() {
        var snapshot = TestSupport.NewPlayer().CreateNocSnapshot();

        Assert.Empty(snapshot.Mimics);
        Assert.Empty(snapshot.MimicEquipped);
        Assert.Equal(0, snapshot.MimicSlots);
        Assert.Equal(0, NetworkManager.CreateNocSnapshot(5, 100, 400, 30, 90, 2).MimicSlots);
    }

    [Fact]
    public void Field_numbers_are_the_documented_ones() {
        Assert.Equal(10, NocSnapshot.MimicsFieldNumber);
        Assert.Equal(11, NocSnapshot.MimicEquippedFieldNumber);
        Assert.Equal(12, NocSnapshot.MimicSlotsFieldNumber);
        Assert.Equal(3, NocCatalog.MimicsFieldNumber);
        Assert.Equal(4, NocCatalog.FusionsFieldNumber);
        Assert.Equal(104, (int)ClientMessage.PayloadOneofCase.NocCreateItemRequest);
        Assert.Equal(105, (int)ClientMessage.PayloadOneofCase.NocForgeConsumeRequest);
        Assert.Equal(106, (int)ClientMessage.PayloadOneofCase.NocForgeFuseRequest);
        Assert.Equal(107, (int)ClientMessage.PayloadOneofCase.NocMimicEquipRequest);
    }

    [Fact]
    public void The_requests_round_trip() {
        var consume = new ClientMessage { NocForgeConsumeRequest = new NocForgeConsumeRequest { AbilityId = 2004 } };
        var fuse = new ClientMessage { NocForgeFuseRequest = new NocForgeFuseRequest { AbilityA = 2003, AbilityB = 2004 } };
        var equip = new ClientMessage { NocMimicEquipRequest = new NocMimicEquipRequest { AbilityId = 2003, Equipped = true } };

        Assert.Equal(2004, ClientMessage.Parser.ParseFrom(consume.ToByteArray()).NocForgeConsumeRequest.AbilityId);
        Assert.Equal((2003, 2004), (ClientMessage.Parser.ParseFrom(fuse.ToByteArray()).NocForgeFuseRequest.AbilityA, ClientMessage.Parser.ParseFrom(fuse.ToByteArray()).NocForgeFuseRequest.AbilityB));
        Assert.True(ClientMessage.Parser.ParseFrom(equip.ToByteArray()).NocMimicEquipRequest.Equipped);
    }
}

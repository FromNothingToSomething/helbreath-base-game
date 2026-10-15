using System.Text.Json;
using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Net;
using Xunit;

namespace NocthemarTests;

/// <summary>T79: the four faction master NPCs (ids 11-14), where they stand, and the range rule shared with the Soul Forge.</summary>
public class FactionMasterTests {
    [Theory]
    [InlineData(10, 10, 10, 10, 3, true)]
    [InlineData(10, 10, 13, 13, 3, true)] // Chebyshev: the diagonal counts as 3
    [InlineData(10, 10, 14, 10, 3, false)]
    [InlineData(10, 10, 10, 6, 3, false)]
    [InlineData(10, 10, 11, 10, 0, false)]
    [InlineData(10, 10, 10, 10, 0, true)]
    public void The_range_is_a_chebyshev_distance(int px, int py, int nx, int ny, int range, bool expected) {
        Assert.Equal(expected, NpcInteraction.IsInRange(px, py, nx, ny, range));
    }

    [Fact]
    public void The_default_range_is_the_forge_range() {
        Assert.Equal(3, NpcInteraction.DefaultRangeCells);
        Assert.Equal(NpcInteraction.DefaultRangeCells, SoulForge.RangeCells);
        Assert.True(NpcInteraction.IsInRange(0, 0, 3, 3));
        Assert.False(NpcInteraction.IsInRange(0, 0, 4, 0));
        Assert.True(FactionMaster.IsInRange(0, 0, 3, 3));
        Assert.False(FactionMaster.IsInRange(0, 0, 4, 0));
    }

    [Fact]
    public void Each_faction_has_the_master_next_to_its_id() {
        Assert.Equal(11, FactionMaster.NpcCatalogIdOf(0));
        Assert.Equal(14, FactionMaster.NpcCatalogIdOf(3));
        Assert.Equal(TestSupport.ShippedFactions().Count, FactionMaster.Count);
        Assert.All(Enumerable.Range(0, FactionMaster.Count), id => Assert.True(FactionMaster.IsValidFaction(id)));
        Assert.False(FactionMaster.IsValidFaction(-1));
        Assert.False(FactionMaster.IsValidFaction(FactionMaster.Count));
    }

    [Fact]
    public void The_masters_exist_in_the_npc_catalog_with_contiguous_ids() {
        var npcs = JsonDocument.Parse(File.ReadAllText(FindConfig("NPCs.json"))).RootElement;

        Assert.Equal(npcs.GetArrayLength() - 1, npcs.EnumerateArray().Max(n => n.GetProperty("id").GetInt32()));
        Assert.Equal(npcs.GetArrayLength(), npcs.EnumerateArray().Select(n => n.GetProperty("id").GetInt32()).Distinct().Count());
        foreach (var faction in TestSupport.ShippedFactions().Factions) {
            var master = npcs.EnumerateArray().Single(n => n.GetProperty("id").GetInt32() == FactionMaster.NpcCatalogIdOf(faction.Id));
            Assert.StartsWith("Maître", master.GetProperty("name").GetString());
        }
    }

    [Fact]
    public void The_masters_stand_in_both_blacksmiths_on_free_distinct_cells() {
        var worlds = JsonDocument.Parse(File.ReadAllText(FindConfig("GameWorlds.json"))).RootElement;
        var ids = Enumerable.Range(0, FactionMaster.Count).Select(FactionMaster.NpcCatalogIdOf).ToArray();

        foreach (var world in worlds.EnumerateArray().Where(w => w.TryGetProperty("npcs", out var l) && l.EnumerateArray().Any(n => n.GetProperty("npcId").GetInt32() == FactionMaster.FirstNpcCatalogId))) {
            var npcs = world.GetProperty("npcs").EnumerateArray().ToArray();
            Assert.Contains(world.GetProperty("id").GetString(), new[] { "arebsmith", "elvbsmith" });
            Assert.Equal(ids, npcs.Select(n => n.GetProperty("npcId").GetInt32()).Where(i => ids.Contains(i)).ToArray());
            Assert.Equal(npcs.Length, npcs.Select(n => (n.GetProperty("x").GetInt32(), n.GetProperty("y").GetInt32())).Distinct().Count());
        }

        Assert.Equal(2, worlds.EnumerateArray().Count(w => w.TryGetProperty("npcs", out var l) && l.EnumerateArray().Any(n => n.GetProperty("npcId").GetInt32() == FactionMaster.FirstNpcCatalogId)));
    }

    [Fact]
    public void The_forge_and_the_masters_share_one_range_helper() {
        var forge = File.ReadAllText(FindSource("Nocthemar", "Core", "SoulForge.cs"));

        Assert.Contains("NpcInteraction.IsInRange", forge);
        Assert.DoesNotContain("Math.Max", forge);
    }

    [Fact]
    public void GameWorld_gives_the_dispatcher_its_master_locator() {
        var source = File.ReadAllText(FindSource("World", "Game", "GameWorld.cs"));

        Assert.Contains("factionMasterInRange: IsFactionMasterInRange", source);
        Assert.Contains("FactionMaster.NpcCatalogIdOf(factionId)", source);
        Assert.Contains("FactionMaster.IsInRange(", source);
    }

    [Fact]
    public void A_join_needs_the_master_of_that_faction_not_any_master() {
        var sent = new List<ServerMessage>();
        var player = TestSupport.NewPlayer(sent, null, TestSupport.ShippedContent());
        TestSupport.Restore(player, new NocState(NocState.CurrentV, 10, 0, 0, RaceId: 0));
        var dispatcher = new NocDispatcher(false, _ => { }, null, (_, factionId) => factionId == 2);

        dispatcher.Handle(player, new ClientMessage { NocFactionJoinRequest = new NocFactionJoinRequest { FactionId = 1 } });
        Assert.Null(player.FactionId);
        Assert.Contains(sent, m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocActionRefused && m.NocActionRefused.Reason == FactionService.NoMasterInRange);

        dispatcher.Handle(player, new ClientMessage { NocFactionJoinRequest = new NocFactionJoinRequest { FactionId = 2 } });
        Assert.Equal(2, player.FactionId);
    }

    private static string FindConfig(string file) => FindSource("Config", file);

    private static string FindSource(params string[] relative) {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine([dir.FullName, "multiplayer", "server", .. relative]);
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        throw new FileNotFoundException(Path.Combine(relative));
    }
}

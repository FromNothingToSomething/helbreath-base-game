using System.Text.Json;
using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Net;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T68: forge, fusion and bar requests through the dispatcher: Soul Forge range, the D20 / D24 rules still apply, nothing is consumed on a refusal.</summary>
public class SoulForgeDispatcherTests {
    // Shipped: 2001 Morsure infectée, 2005 Frénésie, 2008 Putréfaction (open); 2000 Nuée (class 3); 2005 + 2008 -> 2102 Bête Immortelle.
    private static NocDispatcher Dispatcher(bool forge = true) => new(false, _ => { }, forge ? _ => true : null);

    private static (GameWorldPlayer Player, List<ServerMessage> Sent) NewPlayer(int? classId = null) {
        var sent = new List<ServerMessage>();
        var player = TestSupport.NewPlayer(sent, null, TestSupport.ShippedContent(), TestSupport.EssenceItems());
        if (classId is not null) {
            TestSupport.Restore(player, new NocState(NocState.CurrentV, 1, 0, 0, RaceId: 0, ClassId: classId));
        }

        return (player, sent);
    }

    private static void Give(GameWorldPlayer player, int abilityId, int quantity = 1) =>
        Assert.True(player.InventoryManager.TryCreateItems(MimicIds.EssenceItemId(abilityId), quantity, out _));

    private static int Essences(GameWorldPlayer player, int abilityId) => player.InventoryManager.CountItem(MimicIds.EssenceItemId(abilityId));

    private static bool Learned(GameWorldPlayer player, int abilityId) => player.LearnedMimics.Any(e => e.Id == abilityId);

    private static string[] Refusals(List<ServerMessage> sent) =>
        [.. sent.Where(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocActionRefused).Select(m => m.NocActionRefused.Reason)];

    private static ClientMessage Consume(int id) => new() { NocForgeConsumeRequest = new NocForgeConsumeRequest { AbilityId = id } };

    private static ClientMessage Fuse(int a, int b) => new() { NocForgeFuseRequest = new NocForgeFuseRequest { AbilityA = a, AbilityB = b } };

    private static ClientMessage Equip(int id, bool equipped) => new() { NocMimicEquipRequest = new NocMimicEquipRequest { AbilityId = id, Equipped = equipped } };

    [Fact]
    public void Consuming_near_the_forge_learns_the_ability_and_removes_the_essence() {
        var (player, sent) = NewPlayer();
        Give(player, 2001);
        sent.Clear();

        Assert.True(Dispatcher().Handle(player, Consume(2001)));

        Assert.True(Learned(player, 2001));
        Assert.Equal(0, Essences(player, 2001));
        Assert.Empty(Refusals(sent));
        Assert.Single(sent, m => m.PayloadCase == ServerMessage.PayloadOneofCase.ItemRemovedFromBag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Without_a_forge_in_range_consume_and_fuse_are_refused_and_nothing_is_spent(bool fuse) {
        var (player, sent) = NewPlayer();
        Give(player, 2005);
        Give(player, 2008);
        sent.Clear();

        Dispatcher(forge: false).Handle(player, fuse ? Fuse(2005, 2008) : Consume(2005));

        Assert.Equal(new[] { SoulForge.NoForgeInRange }, Refusals(sent));
        Assert.Empty(player.LearnedMimics);
        Assert.Equal(1, Essences(player, 2005));
        Assert.Equal(1, Essences(player, 2008));
    }

    [Fact]
    public void A_dispatcher_built_without_a_forge_locator_has_no_forge() {
        var (player, sent) = NewPlayer();
        Give(player, 2001);
        sent.Clear();

        new NocDispatcher(false, _ => { }).Handle(player, Consume(2001));

        Assert.Equal(new[] { SoulForge.NoForgeInRange }, Refusals(sent));
        Assert.False(Learned(player, 2001));
    }

    [Fact]
    public void An_essence_that_is_not_in_the_bag_is_refused() {
        var (player, sent) = NewPlayer();

        Dispatcher().Handle(player, Consume(2001));

        Assert.Single(Refusals(sent));
        Assert.False(Learned(player, 2001));
    }

    [Fact]
    public void One_ability_per_monster_a_duplicate_is_refused_and_the_essence_kept() {
        var (player, sent) = NewPlayer();
        Give(player, 2001);
        Dispatcher().Handle(player, Consume(2001));
        Give(player, 2001);
        sent.Clear();

        Dispatcher().Handle(player, Consume(2001));

        Assert.Single(Refusals(sent));
        Assert.Equal(1, Essences(player, 2001));
        Assert.Single(player.LearnedMimics);
    }

    [Fact]
    public void Class_exclusivities_still_apply_at_the_forge() {
        var (player, sent) = NewPlayer(classId: 0);
        Give(player, 2000); // Nuée: class 3 only
        sent.Clear();

        Dispatcher().Handle(player, Consume(2000));

        Assert.Single(Refusals(sent));
        Assert.False(Learned(player, 2000));
        Assert.Equal(1, Essences(player, 2000));
    }

    [Fact]
    public void Fusing_near_the_forge_gives_the_recipe_result_and_removes_both_essences() {
        var (player, sent) = NewPlayer();
        Give(player, 2005);
        Give(player, 2008);
        sent.Clear();

        Dispatcher().Handle(player, Fuse(2008, 2005)); // either order

        Assert.Empty(Refusals(sent));
        Assert.True(Learned(player, 2102));
        Assert.Equal(0, Essences(player, 2005));
        Assert.Equal(0, Essences(player, 2008));
        Assert.Equal(2, sent.Count(m => m.PayloadCase == ServerMessage.PayloadOneofCase.ItemRemovedFromBag));
    }

    [Fact]
    public void An_unknown_recipe_is_refused_and_nothing_is_consumed() {
        var (player, sent) = NewPlayer();
        Give(player, 2001);
        Give(player, 2005);
        sent.Clear();

        Dispatcher().Handle(player, Fuse(2001, 2005));

        Assert.Single(Refusals(sent));
        Assert.Empty(player.LearnedMimics);
        Assert.Equal(1, Essences(player, 2001));
        Assert.Equal(1, Essences(player, 2005));
    }

    [Fact]
    public void The_dead_cannot_use_the_forge() {
        var (player, sent) = NewPlayer();
        Give(player, 2001);
        player.ApplyDamage(player.MaxHp);
        sent.Clear();

        Dispatcher().Handle(player, Consume(2001));

        Assert.Equal(new[] { "You are dead." }, Refusals(sent));
        Assert.False(Learned(player, 2001));
    }

    [Fact]
    public void The_bar_is_changed_anywhere_without_a_forge() {
        var (player, sent) = NewPlayer();
        Give(player, 2001);
        Dispatcher().Handle(player, Consume(2001));
        var away = Dispatcher(forge: false);

        away.Handle(player, Equip(2001, true));
        Assert.True(new[] { 2001 }.SequenceEqual(player.EquippedMimicIds));

        away.Handle(player, Equip(2001, false));
        Assert.Empty(player.EquippedMimicIds);
        Assert.Empty(Refusals(sent));
    }

    [Fact]
    public void Equipping_an_unlearned_ability_is_refused() {
        var (player, sent) = NewPlayer();

        Dispatcher().Handle(player, Equip(2001, true));

        Assert.Single(Refusals(sent));
        Assert.Empty(player.EquippedMimicIds);
    }

    [Fact]
    public void The_dispatcher_owns_the_three_requests() {
        Assert.True(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.NocForgeConsumeRequest));
        Assert.True(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.NocForgeFuseRequest));
        Assert.True(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.NocMimicEquipRequest));
    }

    [Theory]
    [InlineData(10, 10, 10, 10, true)]
    [InlineData(10, 10, 13, 10, true)]
    [InlineData(10, 10, 14, 10, false)]
    [InlineData(10, 10, 13, 7, true)] // Chebyshev: the diagonal counts as 3
    [InlineData(10, 10, 10, 14, false)]
    public void The_forge_range_is_a_chebyshev_distance(int px, int py, int nx, int ny, bool expected) {
        Assert.Equal(expected, SoulForge.IsInRange(px, py, nx, ny));
        Assert.Equal(3, SoulForge.RangeCells);
    }

    [Fact]
    public void The_forge_npc_exists_in_the_catalog_and_stands_in_the_blacksmiths() {
        var npcs = JsonDocument.Parse(File.ReadAllText(FindSource("Config", "NPCs.json"))).RootElement;
        var forge = npcs.EnumerateArray().Single(n => n.GetProperty("id").GetInt32() == SoulForge.NpcCatalogId);
        Assert.Equal("Forge d'Âmes", forge.GetProperty("name").GetString());
        Assert.Equal(npcs.GetArrayLength() - 1, npcs.EnumerateArray().Max(n => n.GetProperty("id").GetInt32())); // ids stay contiguous

        var worlds = JsonDocument.Parse(File.ReadAllText(FindSource("Config", "GameWorlds.json"))).RootElement;
        var withForge = worlds.EnumerateArray()
            .Where(w => w.TryGetProperty("npcs", out var list) && list.EnumerateArray().Any(n => n.GetProperty("npcId").GetInt32() == SoulForge.NpcCatalogId))
            .Select(w => w.GetProperty("id").GetString()).ToArray();
        Assert.Equal(new[] { "arebsmith", "elvbsmith" }, withForge);
    }

    [Fact]
    public void GameWorld_gives_the_dispatcher_its_forge_locator() {
        var source = File.ReadAllText(FindSource("World", "Game", "GameWorld.cs"));

        Assert.Contains("soulForgeInRange: IsSoulForgeInRange", source);
        Assert.Contains("SoulForge.NpcCatalogId", source);
    }

    private static string FindSource(params string[] relative) {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine(new[] { dir.FullName, "server" }.Concat(relative).ToArray());
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        throw new FileNotFoundException("server/" + string.Join('/', relative));
    }
}

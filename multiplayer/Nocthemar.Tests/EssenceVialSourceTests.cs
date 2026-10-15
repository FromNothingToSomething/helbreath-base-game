using System.Text.Json.Nodes;
using Mmorpg.Network;
using Server;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Net;
using Xunit;

namespace NocthemarTests;

/// <summary>T62bis: the Essence Vial source (drop on kill, configurable) and the debug item command (gated by nocthemar.debug).</summary>
public class EssenceVialSourceTests {
    private const int StackableId = MimicIds.EssenceVialItemId;
    private const int SwordId = 9001;

    private static MimicConfig WithVialChance(string? value) {
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Mimic.json");
        var doc = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        doc.Remove("essenceVialDropChance");
        if (value is not null) {
            doc["essenceVialDropChance"] = JsonNode.Parse(value);
        }

        return MimicConfig.Parse(doc.ToJsonString(), TestSupport.ShippedClasses());
    }

    [Fact]
    public void The_shipped_catalog_drops_a_vial_with_a_five_percent_chance() {
        Assert.Equal(0.05, TestSupport.ShippedMimic().EssenceVialDropChance);
        Assert.Equal(0.05, MimicConfig.DefaultEssenceVialDropChance);
    }

    [Fact]
    public void An_absent_key_means_the_default_and_zero_switches_the_drop_off() {
        Assert.Equal(MimicConfig.DefaultEssenceVialDropChance, WithVialChance(null).EssenceVialDropChance);
        Assert.Equal(0.0, WithVialChance("0").EssenceVialDropChance);
        Assert.Equal(1.0, WithVialChance("1").EssenceVialDropChance);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.5")]
    public void A_chance_outside_zero_one_is_rejected(string value) {
        var ex = Assert.Throws<InvalidOperationException>(() => WithVialChance(value));

        Assert.Contains("essenceVialDropChance", ex.Message);
    }

    [Fact]
    public void The_vial_drops_below_the_chance_only_and_draws_one_value() {
        var draws = 0;
        var value = 0.0;
        var dropper = new EssenceDropper(WithVialChance("0.05"), () => { draws++; return value; });

        Assert.True(dropper.RollVial());
        value = 0.0499;
        Assert.True(dropper.RollVial());
        value = 0.05;
        Assert.False(dropper.RollVial());
        value = 0.99;
        Assert.False(dropper.RollVial());
        Assert.Equal(4, draws);
    }

    [Fact]
    public void A_switched_off_drop_never_drops_and_draws_nothing() {
        var draws = 0;
        var dropper = new EssenceDropper(WithVialChance("0"), () => { draws++; return 0.0; });

        Assert.False(dropper.RollVial());
        Assert.Equal(0, draws);
    }

    private static Dictionary<int, ItemConfig> Items() => new() {
        [StackableId] = new ItemConfig(StackableId, "Fiole d'Essence", "misc", Stackable: true),
        [SwordId] = new ItemConfig(SwordId, "Test sword", "misc"),
    };

    private static ClientMessage Create(int itemId, int quantity) =>
        new() { NocCreateItemRequest = new NocCreateItemRequest { ItemId = itemId, Quantity = quantity } };

    private static (GameWorldPlayer Player, List<ServerMessage> Sent) NewPlayer() {
        var sent = new List<ServerMessage>();
        return (TestSupport.NewPlayer(sent, null, null, Items()), sent);
    }

    private static string[] Refusals(List<ServerMessage> sent) =>
        [.. sent.Where(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocActionRefused).Select(m => m.NocActionRefused.Reason)];

    [Fact]
    public void The_debug_command_is_a_noc_payload_on_field_104() {
        Assert.True(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.NocCreateItemRequest));
        Assert.Equal(104, ClientMessage.NocCreateItemRequestFieldNumber);
    }

    [Fact]
    public void Debug_enabled_puts_the_quantity_in_the_bag_and_tells_the_client() {
        var (player, sent) = NewPlayer();

        Assert.True(new NocDispatcher(true, _ => { }).Handle(player, Create(StackableId, 7)));

        Assert.Equal(7, player.InventoryManager.CountItem(StackableId));
        Assert.Equal(7, sent.Single(m => m.PayloadCase == ServerMessage.PayloadOneofCase.ItemAddedToBag).ItemAddedToBag.Item.Quantity);
        Assert.Empty(Refusals(sent));
    }

    [Fact]
    public void A_second_request_merges_into_the_same_stack() {
        var (player, sent) = NewPlayer();
        var dispatcher = new NocDispatcher(true, _ => { });

        dispatcher.Handle(player, Create(StackableId, 3));
        dispatcher.Handle(player, Create(StackableId, 4));

        Assert.Equal(7, player.InventoryManager.CountItem(StackableId));
        Assert.Equal(7, sent.Last(m => m.PayloadCase == ServerMessage.PayloadOneofCase.ItemAddedToBag).ItemAddedToBag.Item.Quantity);
    }

    [Fact]
    public void A_non_stackable_item_is_created_once_per_unit() {
        var (player, sent) = NewPlayer();

        new NocDispatcher(true, _ => { }).Handle(player, Create(SwordId, 3));

        Assert.Equal(3, player.InventoryManager.CountItem(SwordId));
        Assert.Equal(3, sent.Count(m => m.PayloadCase == ServerMessage.PayloadOneofCase.ItemAddedToBag));
    }

    [Fact]
    public void Debug_disabled_refuses_logs_once_per_player_and_creates_nothing() {
        var (player, sent) = NewPlayer();
        var logs = new List<string>();
        var dispatcher = new NocDispatcher(false, logs.Add);

        for (var i = 0; i < 5; i++) {
            Assert.True(dispatcher.Handle(player, Create(StackableId, 10)));
        }

        Assert.Equal(0, player.InventoryManager.CountItem(StackableId));
        Assert.Empty(sent);
        var log = Assert.Single(logs);
        Assert.Contains("NocCreateItemRequest", log);
        Assert.Contains("nocthemar.debug is false", log);
    }

    [Fact]
    public void Refusals_of_different_debug_requests_are_logged_separately() {
        var (player, _) = NewPlayer();
        var logs = new List<string>();
        var dispatcher = new NocDispatcher(false, logs.Add);

        dispatcher.Handle(player, Create(StackableId, 1));
        dispatcher.Handle(player, new ClientMessage { NocGrantXpRequest = new NocGrantXpRequest { Amount = 5 } });

        Assert.Equal(2, logs.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(NocDispatcher.MaxDebugQuantity + 1)]
    public void A_quantity_out_of_range_is_refused_even_in_debug(int quantity) {
        var (player, sent) = NewPlayer();

        new NocDispatcher(true, _ => { }).Handle(player, Create(StackableId, quantity));

        Assert.Equal(0, player.InventoryManager.CountItem(StackableId));
        Assert.Equal(new[] { $"Quantity must be between 1 and {NocDispatcher.MaxDebugQuantity}." }, Refusals(sent));
    }

    [Fact]
    public void An_unknown_item_is_refused_even_in_debug() {
        var (player, sent) = NewPlayer();

        new NocDispatcher(true, _ => { }).Handle(player, Create(424242, 1));

        Assert.Equal(new[] { "Unknown item." }, Refusals(sent));
        Assert.DoesNotContain(sent, m => m.PayloadCase == ServerMessage.PayloadOneofCase.ItemAddedToBag);
    }
}

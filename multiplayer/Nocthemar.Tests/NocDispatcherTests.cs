using Google.Protobuf;
using Mmorpg.Network;
using Server.Nocthemar.Net;
using Xunit;

namespace NocthemarTests;

/// <summary>T19: NOCTHEMAR client payloads, the debug gate, and the contract with GameWorld's switch.</summary>
public class NocDispatcherTests {
    private static ClientMessage Grant(long amount) => new() { NocGrantXpRequest = new NocGrantXpRequest { Amount = amount } };

    [Fact]
    public void Debug_enabled_grants_xp_to_the_sender() {
        var player = TestSupport.NewPlayer();
        var dispatcher = new NocDispatcher(debugEnabled: true, _ => { });

        // 50 stays below the level-1 threshold (100 XP): no level-up, so Xp is the granted amount.
        Assert.True(dispatcher.Handle(player, Grant(50)));

        Assert.Equal(1, player.Level);
        Assert.Equal(50L, player.Xp);
    }

    [Fact]
    public void Debug_enabled_grant_above_threshold_levels_up_and_carries_the_remainder() {
        var player = TestSupport.NewPlayer();
        var dispatcher = new NocDispatcher(true, _ => { });

        dispatcher.Handle(player, Grant(250)); // level 1 costs 100, level 2 costs ~303: 250 -> level 2 with 150 XP carried

        Assert.Equal(2, player.Level);
        Assert.Equal(150L, player.Xp);
    }

    [Fact]
    public void Debug_enabled_can_level_the_player_up() {
        var player = TestSupport.NewPlayer();
        var dispatcher = new NocDispatcher(true, _ => { });

        dispatcher.Handle(player, Grant(1_000_000));

        Assert.True(player.Level > 1);
    }

    [Fact]
    public void Debug_disabled_refuses_the_request_and_changes_nothing() {
        var player = TestSupport.NewPlayer();
        var dispatcher = new NocDispatcher(debugEnabled: false, _ => { });

        Assert.True(dispatcher.Handle(player, Grant(250))); // owned by NOCTHEMAR, but refused

        Assert.Equal(0L, player.Xp);
        Assert.Equal(1, player.Level);
    }

    [Fact]
    public void Refusal_is_logged_once_per_player_not_per_request() {
        var logs = new List<string>();
        var dispatcher = new NocDispatcher(false, logs.Add);
        var a = TestSupport.NewPlayer();
        var b = TestSupport.NewPlayer();

        for (var i = 0; i < 50; i++) {
            dispatcher.Handle(a, Grant(10));
        }

        Assert.Single(logs);
        dispatcher.Handle(b, Grant(10));
        Assert.Equal(2, logs.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Non_positive_amounts_are_ignored_even_in_debug(long amount) {
        var player = TestSupport.NewPlayer();
        var dispatcher = new NocDispatcher(true, _ => { });

        dispatcher.Handle(player, Grant(amount));

        Assert.Equal(0L, player.Xp);
        Assert.Equal(1, player.Level);
    }

    [Fact]
    public void Payloads_that_are_not_ours_are_left_to_the_world() {
        var player = TestSupport.NewPlayer();
        var dispatcher = new NocDispatcher(true, _ => { });

        Assert.False(dispatcher.Handle(player, new ClientMessage { PingRequest = new PingRequest() }));
        Assert.False(dispatcher.Handle(player, new ClientMessage()));
        Assert.Equal(0L, player.Xp);
    }

    [Fact]
    public void Owns_matches_exactly_the_noc_payloads() {
        Assert.True(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.NocGrantXpRequest));
        Assert.False(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.PingRequest));
        Assert.False(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.None));
    }

    [Fact]
    public void Every_client_payload_numbered_100_or_more_is_owned_and_no_other_is() {
        var oneof = ClientMessage.Descriptor.Oneofs.Single(o => o.Name == "payload");

        foreach (var field in oneof.Fields) {
            var name = string.Concat(field.Name.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
            var payload = Enum.Parse<ClientMessage.PayloadOneofCase>(name);

            Assert.Equal(field.FieldNumber >= 100, NocDispatcher.Owns(payload));
        }
    }

    [Fact]
    public void Grant_request_is_field_100_on_the_wire() {
        var bytes = Grant(7).ToByteArray();

        Assert.Equal(0xA2, bytes[0]); // (100 << 3) | 2 = 802 -> A2 06
        Assert.Equal(0x06, bytes[1]);
        Assert.Equal(7L, ClientMessage.Parser.ParseFrom(bytes).NocGrantXpRequest.Amount);
    }

    [Fact]
    public void GameWorld_switch_keeps_its_default_for_unknown_payloads() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        FileInfo? file = null;
        while (dir is not null && file is null) {
            var candidate = Path.Combine(dir.FullName, "multiplayer", "server", "World", "Game", "GameWorld.cs");
            if (File.Exists(candidate)) {
                file = new FileInfo(candidate);
            }

            dir = dir.Parent;
        }

        Assert.NotNull(file);
        var source = File.ReadAllText(file!.FullName);

        Assert.Matches(@"case ClientMessage\.PayloadOneofCase\.NocCreateItemRequest:\s*nocDispatcher\.Handle\(", source);
        Assert.Matches(@"default:\s*throw new InvalidOperationException\(\s*\$""Unhandled client payload", source);
    }
}

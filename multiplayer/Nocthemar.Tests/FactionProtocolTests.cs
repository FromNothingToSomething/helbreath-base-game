using Google.Protobuf;
using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Net;
using Server.Utils;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T78: factions on the wire (snapshot, catalog, join and debug requests, observers) and in the dispatcher.</summary>
public class FactionProtocolTests {
    private const int Choeur = 1;

    private static NocDispatcher Dispatcher(bool debug = false, bool master = true, Action<GameWorldPlayer>? changed = null, List<string>? logs = null) =>
        new(debug, logs is null ? _ => { } : logs.Add, null, master ? (_, _) => true : null, changed);

    private static (GameWorldPlayer Player, List<ServerMessage> Sent) NewPlayer(int level = 10, int? factionId = null, FactionRep[]? reputations = null) {
        var sent = new List<ServerMessage>();
        var player = TestSupport.NewPlayer(sent, null, TestSupport.ShippedContent());
        TestSupport.Restore(player, new NocState(NocState.CurrentV, level, 0, 0, RaceId: 0, FactionId: factionId, Reputations: reputations));
        return (player, sent);
    }

    private static ClientMessage Join(int factionId) => new() { NocFactionJoinRequest = new NocFactionJoinRequest { FactionId = factionId } };

    private static ClientMessage Add(int factionId, int delta) => new() { NocReputationAddRequest = new NocReputationAddRequest { FactionId = factionId, Delta = delta } };

    private static string[] Refusals(List<ServerMessage> sent) =>
        [.. sent.Where(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocActionRefused).Select(m => m.NocActionRefused.Reason)];

    private static NocSnapshot LastState(GameWorldPlayer player, List<ServerMessage> sent) {
        sent.Clear();
        player.TryFlushNocUpdates();
        return sent.Last(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated).NocStateUpdated.State;
    }

    [Fact]
    public void Join_near_a_master_sets_the_faction_and_tells_the_world() {
        var (player, sent) = NewPlayer();
        var told = new List<GameWorldPlayer>();

        Assert.True(Dispatcher(changed: told.Add).Handle(player, Join(Choeur)));

        Assert.Equal(Choeur, player.FactionId);
        Assert.Empty(Refusals(sent));
        Assert.Equal(new[] { player }, told);
        Assert.Equal(Choeur, LastState(player, sent).FactionId);
    }

    [Fact]
    public void Join_is_refused_without_a_master_in_range_or_when_dead_and_nothing_changes() {
        var (player, sent) = NewPlayer();
        var told = new List<GameWorldPlayer>();

        Dispatcher(master: false, changed: told.Add).Handle(player, Join(Choeur));
        Assert.Equal(new[] { FactionService.NoMasterInRange }, Refusals(sent));

        sent.Clear();
        player.ApplyDamage(player.MaxHp);
        Dispatcher(changed: told.Add).Handle(player, Join(Choeur));
        Assert.Equal(new[] { "You are dead." }, Refusals(sent));

        Assert.Null(player.FactionId);
        Assert.Empty(told);
    }

    [Fact]
    public void Join_follows_the_faction_rules() {
        var (low, lowSent) = NewPlayer(level: 9);
        var (member, memberSent) = NewPlayer(factionId: 0);
        var (other, otherSent) = NewPlayer();
        var told = new List<GameWorldPlayer>();
        var dispatcher = Dispatcher(changed: told.Add);

        dispatcher.Handle(low, Join(Choeur));
        dispatcher.Handle(member, Join(Choeur));
        dispatcher.Handle(other, Join(99));

        Assert.Equal(new[] { FactionService.LevelTooLow(10) }, Refusals(lowSent));
        Assert.Equal(new[] { FactionService.AlreadyJoined }, Refusals(memberSent));
        Assert.Equal(new[] { FactionService.UnknownFaction }, Refusals(otherSent));
        Assert.Empty(told);
        Assert.Equal(0, member.FactionId);
    }

    [Fact]
    public void Reputation_add_is_debug_only_and_logged_once() {
        var (player, sent) = NewPlayer();
        var logs = new List<string>();
        var dispatcher = Dispatcher(debug: false, logs: logs);

        dispatcher.Handle(player, Add(Choeur, 500));
        dispatcher.Handle(player, Add(Choeur, 500));

        Assert.Equal(0, player.ReputationWith(Choeur));
        Assert.Single(logs);
        Assert.Contains(nameof(NocReputationAddRequest), logs[0]);
        Assert.Empty(Refusals(sent));
    }

    [Fact]
    public void Reputation_add_in_debug_moves_the_reputation_and_the_snapshot_shows_it() {
        var (player, sent) = NewPlayer();
        var dispatcher = Dispatcher(debug: true);

        dispatcher.Handle(player, Add(Choeur, 1200));
        dispatcher.Handle(player, Add(0, -300));

        Assert.Equal(1200, player.ReputationWith(Choeur));
        var state = LastState(player, sent);
        Assert.Equal(new[] { (0, -300), (Choeur, 1200) }, state.Reputations.Select(r => (r.FactionId, r.Value)).ToArray());
        Assert.False(state.HasFactionId);
    }

    [Fact]
    public void Reputation_add_to_an_unknown_faction_is_refused() {
        var (player, sent) = NewPlayer();

        Dispatcher(debug: true).Handle(player, Add(42, 10));

        Assert.Equal(new[] { FactionService.UnknownFaction }, Refusals(sent));
    }

    [Fact]
    public void The_snapshot_skips_reputations_back_at_zero() {
        var (player, sent) = NewPlayer(reputations: [new FactionRep(0, 0), new FactionRep(2, 40)]);

        var state = LastStateAfterDirty(player, sent);

        Assert.Equal(new[] { (2, 40) }, state.Reputations.Select(r => (r.FactionId, r.Value)).ToArray());
    }

    private static NocSnapshot LastStateAfterDirty(GameWorldPlayer player, List<ServerMessage> sent) {
        sent.Clear();
        player.GrantXp(1);
        player.TryFlushNocUpdates();
        return sent.Last(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated).NocStateUpdated.State;
    }

    [Fact]
    public void The_catalog_lists_the_factions_their_rivals_and_perks_by_tier() {
        var catalog = TestSupport.ShippedContent().Catalog;
        var shipped = TestSupport.ShippedFactions();

        Assert.Equal(shipped.Count, catalog.Factions.Count);
        foreach (var info in catalog.Factions) {
            var source = shipped.Factions.Single(f => f.Id == info.Id);
            Assert.Equal(source.Key, info.Key);
            Assert.Equal(source.Name, info.Name);
            Assert.Equal(source.Hq, info.Hq);
            Assert.Equal(source.Colors, info.Colors);
            Assert.Equal(source.Rivals, info.Rivals);
            Assert.Equal(source.Desc, info.Desc);
            Assert.Equal(new[] { (int)ReputationTier.Friendly, (int)ReputationTier.Honored, (int)ReputationTier.Exalted }, info.Perks.Select(p => p.Tier).ToArray());
            Assert.Equal(source.ModifiersAt(ReputationTier.Exalted).Length, info.Perks.Sum(p => p.Modifiers.Count));
        }
    }

    [Fact]
    public void The_catalog_carries_the_reputation_rules() {
        var rules = TestSupport.ShippedFactions().Rules;
        var wire = TestSupport.ShippedContent().Catalog.ReputationRules;

        Assert.Equal((rules.Min, rules.Max, rules.JoinLevel, rules.KillBase), (wire.Min, wire.Max, wire.JoinLevel, wire.KillBase));
        Assert.Equal((rules.Neutral, rules.Friendly, rules.Honored, rules.Exalted), (wire.Neutral, wire.Friendly, wire.Honored, wire.Exalted));
    }

    [Fact]
    public void Without_a_faction_catalog_nothing_faction_related_is_sent() {
        var classes = TestSupport.ShippedClasses();
        var skills = TestSupport.ShippedSkills();
        var catalog = NocCatalogBuilder.Build(classes, skills, new SkillService(classes, skills, TestSupport.Progression()));

        Assert.Empty(catalog.Factions);
        Assert.Null(catalog.ReputationRules);
    }

    [Fact]
    public void The_new_fields_have_their_frozen_numbers() {
        Assert.Equal(108, ClientMessage.Descriptor.FindFieldByName("noc_faction_join_request").FieldNumber);
        Assert.Equal(112, ClientMessage.Descriptor.FindFieldByName("noc_reputation_add_request").FieldNumber);
        Assert.Equal(102, ServerMessage.Descriptor.FindFieldByName("player_faction_changed").FieldNumber);
        Assert.Equal(20, PlayerEnteredRange.Descriptor.FindFieldByName("faction_id").FieldNumber);
        Assert.Equal(13, NocSnapshot.Descriptor.FindFieldByName("faction_id").FieldNumber);
        Assert.Equal(14, NocSnapshot.Descriptor.FindFieldByName("reputations").FieldNumber);
        Assert.Equal(10, NocCatalog.Descriptor.FindFieldByName("factions").FieldNumber);
        Assert.Equal(11, NocCatalog.Descriptor.FindFieldByName("reputation_rules").FieldNumber);
    }

    [Fact]
    public void The_join_request_is_field_108_on_the_wire() {
        var bytes = Join(Choeur).ToByteArray();

        Assert.Equal(0xE2, bytes[0]); // (108 << 3) | 2 = 866 -> E2 06
        Assert.Equal(0x06, bytes[1]);
        Assert.Equal(Choeur, ClientMessage.Parser.ParseFrom(bytes).NocFactionJoinRequest.FactionId);
    }

    [Fact]
    public void Players_in_range_see_the_faction_only_once_joined() {
        var (player, _) = NewPlayer();

        Assert.False(NetworkManager.ToPlayerEnteredRangeSnapshot(player).HasFactionId);

        Dispatcher().Handle(player, Join(Choeur));
        var snapshot = NetworkManager.ToPlayerEnteredRangeSnapshot(player);

        Assert.True(snapshot.HasFactionId);
        Assert.Equal(Choeur, snapshot.FactionId);
        Assert.Equal(Choeur, PlayerEnteredRange.Parser.ParseFrom(snapshot.ToByteArray()).FactionId);
    }

    [Fact]
    public void The_observer_message_carries_the_faction() {
        var message = NetworkManager.CreatePlayerFactionChanged(7, Choeur);

        Assert.Equal(ServerMessage.PayloadOneofCase.PlayerFactionChanged, message.PayloadCase);
        Assert.Equal(7, message.PlayerFactionChanged.PlayerId);
        Assert.Equal(Choeur, message.PlayerFactionChanged.FactionId);
        Assert.False(NetworkManager.CreatePlayerFactionChanged(7, null).PlayerFactionChanged.HasFactionId);
    }

    [Fact]
    public void GameWorld_tells_the_players_in_range_when_a_player_joins() {
        var source = GameWorldSource();

        Assert.Matches(@"factionChanged:\s*BroadcastFactionChanged", source);
        var start = source.IndexOf("private void BroadcastFactionChanged", StringComparison.Ordinal);
        Assert.True(start > 0);
        var body = source[start..Math.Min(source.Length, start + 600)];
        Assert.Contains("CreatePlayerFactionChanged", body);
        Assert.Contains("GetNearbyPlayers(player.PosX, player.PosY, player.SessionId)", body);
    }

    [Fact]
    public void GameWorld_routes_the_new_payloads_to_the_dispatcher() {
        var source = GameWorldSource();

        Assert.Matches(@"case ClientMessage\.PayloadOneofCase\.NocFactionJoinRequest:", source);
        Assert.Matches(@"case ClientMessage\.PayloadOneofCase\.NocReputationAddRequest:\s*case ClientMessage\.PayloadOneofCase\.NocMimicEquipRequest:\s*nocDispatcher\.Handle\(", source);
    }

    private static string GameWorldSource() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            var candidate = Path.Combine(dir.FullName, "multiplayer", "server", "World", "Game", "GameWorld.cs");
            if (File.Exists(candidate)) {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("GameWorld.cs");
    }
}

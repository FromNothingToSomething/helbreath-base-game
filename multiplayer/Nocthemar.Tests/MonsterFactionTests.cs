using System.Text.Json;
using Server;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T76: factionId/repOnKill in Monsters.json, their validation, and the reputation a player earns on a kill.</summary>
public class MonsterFactionTests {
    private const int ChantDeFer = 0;
    private const int Choeur = 1;
    private const int Meutes = 2;
    private const int Forgeurs = 3;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static MonsterConfig[] ShippedMonsters() =>
        JsonSerializer.Deserialize<MonsterConfig[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "Monsters.json")), Options)!;

    private static GameWorldPlayer Player(int? factionId) {
        var player = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent());
        TestSupport.Restore(player, new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: factionId));
        return player;
    }

    private static void Rejects(MonsterConfig monster, string expected) {
        var ex = Assert.Throws<InvalidOperationException>(() => Server.Config.BuildMonsterCatalog([monster]));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void The_fields_are_read_from_json_and_optional() {
        var declared = JsonSerializer.Deserialize<MonsterConfig>("""{"id":1,"name":"a","sprite":"b","movementSpeed":1,"factionId":2,"repOnKill":9}""", Options)!;
        var without = JsonSerializer.Deserialize<MonsterConfig>("""{"id":1,"name":"a","sprite":"b","movementSpeed":1}""", Options)!;

        Assert.Equal(2, declared.FactionId);
        Assert.Equal(9, declared.RepOnKill);
        Assert.Null(without.FactionId);
        Assert.Null(without.RepOnKill);
    }

    [Fact]
    public void The_shipped_monsters_pass_the_catalog_and_faction_checks() {
        var (_, byId) = Server.Config.BuildMonsterCatalog(ShippedMonsters());

        Server.Config.ValidateMonsterFactionReferences(byId, TestSupport.ShippedFactions());
    }

    [Fact]
    public void Every_faction_has_monsters_and_their_amounts_are_in_range() {
        var monsters = ShippedMonsters().Where(m => m.FactionId is not null).ToArray();

        foreach (var faction in TestSupport.ShippedFactions().Factions) {
            Assert.True(monsters.Count(m => m.FactionId == faction.Id) >= 3, $"faction {faction.Key} has fewer than 3 monsters");
        }

        Assert.All(monsters, m => Assert.InRange(m.RepOnKill ?? TestSupport.ShippedFactions().Rules.KillBase, 1, Server.Config.MaxRepOnKill));
    }

    [Fact]
    public void An_unknown_faction_is_rejected() {
        var (_, byId) = Server.Config.BuildMonsterCatalog([new MonsterConfig(0, "a", "b", 1, FactionId: 9)]);

        var ex = Assert.Throws<InvalidOperationException>(() => Server.Config.ValidateMonsterFactionReferences(byId, TestSupport.ShippedFactions()));
        Assert.Contains("unknown factionId 9", ex.Message);
    }

    [Fact]
    public void Bad_values_are_rejected_at_startup() {
        Rejects(new MonsterConfig(0, "a", "b", 1, FactionId: -1), "negative factionId");
        Rejects(new MonsterConfig(0, "a", "b", 1, FactionId: 0, RepOnKill: -1), "repOnKill");
        Rejects(new MonsterConfig(0, "a", "b", 1, FactionId: 0, RepOnKill: Server.Config.MaxRepOnKill + 1), "repOnKill");
        Rejects(new MonsterConfig(0, "a", "b", 1, RepOnKill: 5), "no factionId");
    }

    [Fact]
    public void A_valid_monster_passes() {
        Server.Config.BuildMonsterCatalog([new MonsterConfig(0, "a", "b", 1, FactionId: 1, RepOnKill: 0), new MonsterConfig(1, "c", "d", 1, FactionId: 0)]);
    }

    [Fact]
    public void A_member_who_kills_a_monster_of_a_faction_moves_reputation() {
        var player = Player(ChantDeFer);

        var changes = player.RegisterFactionKill(Choeur, null);

        Assert.Equal(2, changes.Count);
        Assert.Equal(-3, player.ReputationWith(Choeur));
        Assert.Equal(5, player.ReputationWith(ChantDeFer));
        Assert.Equal(0, player.ReputationWith(Meutes));
        Assert.Equal(new[] { new FactionRep(ChantDeFer, 5), new FactionRep(Choeur, -3) }, player.CreatePersistenceState("aresden").Noc!.Reputations);
    }

    [Fact]
    public void A_kill_uses_the_monsters_amount_and_adds_up() {
        var player = Player(Meutes);

        player.RegisterFactionKill(Forgeurs, 20);
        player.RegisterFactionKill(Forgeurs, 20);

        Assert.Equal(-20, player.ReputationWith(Forgeurs));
        Assert.Equal(40, player.ReputationWith(Meutes));
    }

    [Fact]
    public void A_character_without_a_faction_earns_nothing() {
        var player = Player(null);

        var changes = player.RegisterFactionKill(Choeur, null);

        Assert.Empty(changes);
        Assert.Equal(0, player.ReputationWith(Choeur));
        Assert.Null(player.CreatePersistenceState("aresden").Noc!.Reputations);
    }

    [Fact]
    public void Joining_later_does_not_give_back_the_earlier_kills() {
        var player = Player(null);
        player.RegisterFactionKill(Choeur, null);
        player.RegisterFactionKill(Choeur, null);

        TestSupport.Restore(player, player.CreatePersistenceState("aresden").Noc! with { FactionId = ChantDeFer });

        Assert.Equal(0, player.ReputationWith(ChantDeFer));
        Assert.Equal(0, player.ReputationWith(Choeur));
    }

    [Fact]
    public void The_gain_multiplier_stat_scales_the_rivals_gain() {
        var player = Player(ChantDeFer);
        player.Stats.Add(new StatModifier(StatId.ReputationGainMult, ModifierKind.PercentAdd, 0.5, ModifierSources.Faction(0)));

        player.RegisterFactionKill(Choeur, null);

        Assert.Equal(8, player.ReputationWith(ChantDeFer)); // 5 x 1.5 = 7.5, rounded away from zero
        Assert.Equal(-3, player.ReputationWith(Choeur));
    }

    [Fact]
    public void A_world_without_a_faction_catalog_ignores_the_kill() {
        var player = TestSupport.NewPlayer();

        Assert.Empty(player.RegisterFactionKill(Choeur, null));
        Assert.Equal(0, player.ReputationWith(Choeur));
        Assert.Null(player.FactionId);
    }

    // GameWorld cannot be built without a map and monsters, so the wiring is pinned at the source.
    [Fact]
    public void The_monster_kill_hook_credits_the_killer_only() {
        var source = File.ReadAllText(FindSource("World", "Game", "GameWorld.cs"));
        var start = source.IndexOf("public void OnMonsterKilledByPlayer", StringComparison.Ordinal);
        Assert.True(start > 0, "OnMonsterKilledByPlayer not found");
        var body = source.Substring(start, source.IndexOf("TryGetPlayerBySessionId", start, StringComparison.Ordinal) - start);

        Assert.Contains("killer.RegisterFactionKill(monsterFactionId, monsterConfig.RepOnKill);", body);
        Assert.Contains("monsterConfig?.FactionId", body);
    }

    [Fact]
    public void Startup_loads_and_checks_the_factions() {
        var source = File.ReadAllText(FindSource("Server.cs"));

        Assert.Contains("FactionConfig.LoadAsync()", source);
        Assert.Contains("Config.ValidateMonsterFactionReferences(monstersById, factionConfig);", source);
        Assert.Contains("BuiltinMimicHandlers.Register(new MimicHandlerRegistry()), factionConfig, questConfig);", source);
    }

    private static string FindSource(params string[] relative) {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine(new[] { dir.FullName, "server" }.Concat(relative).ToArray());
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        throw new FileNotFoundException("server/" + string.Join('/', relative) + " not found above " + AppContext.BaseDirectory);
    }
}

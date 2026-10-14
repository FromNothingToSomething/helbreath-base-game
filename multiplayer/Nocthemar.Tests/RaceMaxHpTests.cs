using Server.Nocthemar.Data;
using Server.Nocthemar.Net;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>
/// Max HP = level base (1000 + 50 per level above 1) resolved through the race modifiers. It grows with the level, so two players of the same race
/// but of different levels differ; nothing is applied twice on reconnect, repeated application or level-ups (the nightly E2E once mistook the level scaling for a double application).
/// </summary>
public class RaceMaxHpTests {
    private static readonly RaceConfig Races = TestSupport.ShippedRaces();

    private static RaceDefinition Race(int id) => Races.Races.Single(r => r.Id == id);

    private static GameWorldPlayer AtLevel(int level, int raceId) {
        var p = TestSupport.NewPlayer(level, 0, 0, null, Races);
        p.ApplyRace(Race(raceId));
        return p;
    }

    // Valdorniens +5% (1000 -> 1050 at level 1), Spectraux -10%, the others have no max HP modifier. Rounded half away from zero.
    [Theory]
    [InlineData(0, 1, 1050)]
    [InlineData(0, 2, 1103)]
    [InlineData(0, 3, 1155)]
    [InlineData(1, 1, 900)]
    [InlineData(1, 2, 945)]
    [InlineData(1, 3, 990)]
    [InlineData(2, 1, 1000)]
    [InlineData(2, 2, 1050)]
    [InlineData(2, 3, 1100)]
    [InlineData(5, 3, 1100)]
    public void Max_hp_is_the_level_base_through_the_race(int raceId, int level, int expected) {
        Assert.Equal(expected, AtLevel(level, raceId).MaxHp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Reaching_a_level_by_xp_gives_the_same_max_hp_as_loading_that_level(int raceId) {
        var grown = TestSupport.NewPlayer(null, Races);
        grown.ApplyNewCharacter(new NewCharacterChoice(raceId, 0, 0, 0, 0));
        for (var level = 1; level < 3; level++) {
            Assert.True(TestSupport.Progression().TryGetXpToNext(level, out var needed));
            grown.GrantXp(needed);
        }

        Assert.Equal(3, grown.CreateNocSnapshot().Level);
        Assert.Equal(AtLevel(3, raceId).MaxHp, grown.MaxHp);
    }

    [Fact]
    public void Applying_the_same_race_again_never_changes_max_hp() {
        var p = AtLevel(3, 0);
        var before = p.MaxHp;

        for (var i = 0; i < 5; i++) {
            p.ApplyRace(Race(0));
        }

        Assert.Equal(before, p.MaxHp);
        Assert.Equal(1155, p.MaxHp);
    }

    [Fact]
    public void Restoring_the_same_state_twice_is_the_same_as_once() {
        var state = new PlayerPersistenceState(
            "aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false,
            CharacterName: "T", SchemaVersion: Server.Nocthemar.Core.CharacterSchema.Current,
            Noc: new Server.Nocthemar.Core.NocState(Server.Nocthemar.Core.NocState.CurrentV, 3, 0, 50, RaceId: 1));
        var p = TestSupport.NewPlayer(null, Races);

        p.ApplyPersistedState(state);
        var once = p.MaxHp;
        p.ApplyPersistedState(state);

        Assert.Equal(990, once);
        Assert.Equal(once, p.MaxHp);
    }

    [Fact]
    public void Switching_race_and_back_restores_the_exact_max_hp_at_any_level() {
        var p = AtLevel(3, 0);

        p.ApplyRace(Race(1));
        Assert.Equal(990, p.MaxHp);
        p.ApplyRace(Race(0));

        Assert.Equal(1155, p.MaxHp);
    }

    [Fact]
    public void A_reconnect_inside_the_grace_period_reuses_the_player_and_applies_nothing() {
        var source = File.ReadAllText(FindSource("World", "Game", "GameWorld.cs"));
        var start = source.IndexOf("private void HandlePlayerReconnected", StringComparison.Ordinal);
        var end = source.IndexOf("private void HandlePlayerDisconnected", start, StringComparison.Ordinal);
        var body = source.Substring(start, end - start);

        Assert.Contains("playersBySessionId.TryGetValue(", body);
        foreach (var forbidden in new[] { "ApplyRace", "ApplyNewCharacter", "ApplyPersistedState", "CreatePlayer(", "new GameWorldPlayer(" }) {
            Assert.DoesNotContain(forbidden, body);
        }
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

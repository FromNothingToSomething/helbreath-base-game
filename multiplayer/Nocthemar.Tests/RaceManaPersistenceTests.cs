using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Persistence;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>
/// A race that raises the maximum mana (Valdorniens +5%) must not lose mana through a save and load: the load must clamp to the real maximum, after the race.
/// Regression of the nightly E2E: a Valdornien at level 2 with 116/116 mana came back with 110 (the race-less maximum of the level).
/// </summary>
public sealed class RaceManaPersistenceTests : IDisposable {
    private static readonly RaceConfig Races = TestSupport.ShippedRaces();
    private readonly string root = Path.Combine(Path.GetTempPath(), "noc-mana-" + Guid.NewGuid().ToString("N"));
    private readonly JsonCharacterStore store;

    public RaceManaPersistenceTests() {
        store = new JsonCharacterStore(Path.Combine(root, "Chars"), CharacterMigrator.CreateDefault(TestSupport.Progression()), _ => { });
    }

    public void Dispose() {
        try {
            Directory.Delete(root, recursive: true);
        } catch (IOException) {
        }
    }

    private static GameWorldPlayer Fresh() => TestSupport.NewPlayer(null, Races);

    private static GameWorldPlayer WithLevel(int level) {
        var p = Fresh();
        for (var l = 1; l < level; l++) {
            Assert.True(TestSupport.Progression().TryGetXpToNext(l, out var needed));
            p.GrantXp(needed); // level-up refills mana to the real maximum
        }

        Assert.Equal(level, p.CreateNocSnapshot().Level);
        Assert.Equal(p.MaxMana, p.Mana);
        return p;
    }

    /// <summary>Shutdown save to disk, then a fresh player loading it, as after a server restart.</summary>
    private async Task<GameWorldPlayer> SaveAndLoadAsync(GameWorldPlayer player) {
        await store.SaveAsync("hero", player.CreatePersistenceState("aresden"));
        var loaded = await store.LoadAsync("hero");
        Assert.NotNull(loaded);
        var restored = Fresh();
        restored.ApplyPersistedState(loaded!);
        return restored;
    }

    [Fact]
    public async Task A_full_level_2_valdornien_keeps_116_mana_through_shutdown_and_load() {
        var p = WithLevel(2);
        Assert.Equal(116, p.Mana);

        Assert.Equal(116, p.CreatePersistenceState("aresden").Noc!.Mana); // the save holds the current mana, not a race-less clamp
        var restored = await SaveAndLoadAsync(p);

        Assert.Equal(116, restored.Mana);
        Assert.Equal(116, restored.MaxMana);
    }

    [Fact]
    public async Task A_full_level_3_valdornien_keeps_126_mana_through_shutdown_and_load() {
        var p = WithLevel(3);
        Assert.Equal(126, p.Mana);

        var restored = await SaveAndLoadAsync(p);

        Assert.Equal(126, restored.Mana);
        Assert.Equal(126, restored.MaxMana);
    }

    [Fact]
    public async Task A_partial_mana_above_the_race_less_maximum_is_kept() {
        var p = WithLevel(2);
        var state = p.CreatePersistenceState("aresden") with { Noc = p.CreateNocState() with { Mana = 113 } }; // 113 > 110, the level's maximum without the race
        var restored = Fresh();

        restored.ApplyPersistedState(state);

        Assert.Equal(113, restored.Mana);
    }

    [Fact]
    public void Loading_the_same_state_twice_keeps_the_mana() {
        var state = WithLevel(2).CreatePersistenceState("aresden");
        var p = Fresh();

        p.ApplyPersistedState(state);
        p.ApplyPersistedState(state);

        Assert.Equal(116, p.Mana);
    }

    [Fact]
    public void A_hand_edited_mana_is_still_clamped_to_the_real_maximum_of_the_race() {
        var template = WithLevel(2).CreatePersistenceState("aresden");

        var tooMuch = Fresh();
        tooMuch.ApplyPersistedState(template with { Noc = template.Noc! with { Mana = 99_999 } });
        var negative = Fresh();
        negative.ApplyPersistedState(template with { Noc = template.Noc! with { Mana = -5 } });

        Assert.Equal(116, tooMuch.Mana); // the Valdornien maximum, not unbounded and not 110
        Assert.Equal(0, negative.Mana);
    }

    [Fact]
    public void A_race_without_a_mana_bonus_is_clamped_to_its_own_maximum() {
        var template = WithLevel(2).CreatePersistenceState("aresden");
        var noBonus = template with { Noc = template.Noc! with { RaceId = 2, Mana = 116 } }; // sang-mele: no mana modifier, maximum 110
        var p = Fresh();

        p.ApplyPersistedState(noBonus);

        Assert.Equal(2, p.RaceId);
        Assert.Equal(110, p.MaxMana);
        Assert.Equal(110, p.Mana);
    }

    [Fact]
    public async Task The_other_races_keep_their_mana_too() {
        foreach (var race in Races.Races) {
            var p = Fresh();
            p.ApplyNewCharacter(new Server.Nocthemar.Net.NewCharacterChoice(race.Id, 0, 0, 0, 0));
            Assert.True(TestSupport.Progression().TryGetXpToNext(1, out var needed));
            p.GrantXp(needed);
            var expected = p.Mana;
            Assert.Equal(p.MaxMana, expected);

            var restored = await SaveAndLoadAsync(p);

            Assert.Equal(race.Id, restored.RaceId);
            Assert.Equal(expected, restored.Mana);
        }
    }
}

using Server;
using Server.Nocthemar.Core;
using Server.Nocthemar.Persistence;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T11: GameWorldPlayer level/XP/mana, stat-derived HP and persistence of the NOCTHEMAR block.</summary>
public class PlayerProgressionTests {
    private static readonly Server.Nocthemar.Data.ProgressionConfig Config = TestSupport.Progression();

    private static GameWorldPlayer NewPlayer() {
        var player = new GameWorldPlayer(
            Guid.NewGuid(),
            _ => { },
            _ => { },
            _ => { },
            () => { },
            new Dictionary<int, ItemConfig>(),
            new MovementSpeedViolationCheckConfig(false, 5, 20, 20, 10, 50),
            20,
            0.05,
            Config,
            TestSupport.NeutralRaces());
        player.SetInitialState(5, 6);
        return player;
    }

    private static PlayerPersistenceState Saved(NocState? noc, int schemaVersion = CharacterSchema.Current) =>
        new("aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false, CharacterName: "T", SchemaVersion: schemaVersion, Noc: noc);

    private static long Needed(int level) {
        Assert.True(Config.TryGetXpToNext(level, out var xp));
        return xp;
    }

    [Fact]
    public void New_player_keeps_the_original_1000_hp_and_starts_at_level_one_with_full_mana() {
        var p = NewPlayer();

        Assert.Equal(1000, p.MaxHp);
        Assert.Equal(1000, p.Hp);
        Assert.Equal(1, p.Level);
        Assert.Equal(0L, p.Xp);
        Assert.Equal(100, p.MaxMana);
        Assert.Equal(100, p.Mana);
    }

    [Fact]
    public void Persistence_state_carries_the_current_schema_and_the_noc_block() {
        var state = NewPlayer().CreatePersistenceState("aresden");

        Assert.Equal(CharacterSchema.Current, state.SchemaVersion);
        Assert.Equal(new NocState(NocState.CurrentV, 1, 0, 100, RaceId: 0), state.Noc);
    }

    [Fact]
    public void Saved_noc_is_restored_with_level_derived_hp_and_clamped_mana() {
        var p = NewPlayer();

        p.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 5, 20, 33)));

        Assert.Equal(5, p.Level);
        Assert.Equal(20L, p.Xp);
        Assert.Equal(33, p.Mana);
        Assert.Equal(Config.MaxHpAt(5), p.MaxHp);
        Assert.Equal(p.MaxHp, p.Hp);
        Assert.Equal(Config.MaxManaAt(5), p.MaxMana);
    }

    [Fact]
    public void Save_then_restore_into_a_new_player_is_lossless() {
        var first = NewPlayer();
        first.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 7, 55, 21)));
        var snapshot = first.CreatePersistenceState("aresden");

        var second = NewPlayer();
        second.ApplyPersistedState(snapshot);

        Assert.Equal(snapshot.Noc, second.CreatePersistenceState("aresden").Noc);
        Assert.Equal(first.MaxHp, second.MaxHp);
    }

    [Fact]
    public void Missing_noc_keeps_level_one_defaults() {
        var p = NewPlayer();

        p.ApplyPersistedState(Saved(null, CharacterSchema.Legacy));

        Assert.Equal(1, p.Level);
        Assert.Equal(1000, p.MaxHp);
        Assert.Equal(100, p.Mana);
    }

    [Fact]
    public void Corrupt_noc_is_normalized_not_trusted() {
        var p = NewPlayer();

        p.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 5000, -1, 999_999)));

        Assert.Equal(Config.MaxLevel, p.Level);
        Assert.Equal(0L, p.Xp);
        Assert.Equal(p.MaxMana, p.Mana);
        Assert.Equal(Config.MaxHpAt(Config.MaxLevel), p.MaxHp);
    }

    [Fact]
    public void Legacy_file_loaded_through_the_store_becomes_a_level_one_player() {
        var dir = Path.Combine(Path.GetTempPath(), "noc-player-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy_char_minimal.json"), Path.Combine(dir, "old.json"));
            var store = new JsonCharacterStore(dir, CharacterMigrator.CreateDefault(Config));
            var p = NewPlayer();

            p.ApplyPersistedState((await_(store.LoadAsync("old")))!);

            Assert.Equal(1, p.Level);
            Assert.Equal(100, p.Mana);
            Assert.Equal(1000, p.MaxHp);
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static T await_<T>(Task<T> task) => task.GetAwaiter().GetResult();

    [Fact]
    public void Level_up_refills_hp_and_mana_and_raises_maxima() {
        var p = NewPlayer();
        p.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 1, 0, 3)));
        p.ApplyDamage(400);

        var gained = p.GrantXp(Needed(1));

        Assert.Equal(1, gained);
        Assert.Equal(2, p.Level);
        Assert.Equal(Config.MaxHpAt(2), p.MaxHp);
        Assert.Equal(p.MaxHp, p.Hp);
        Assert.Equal(p.MaxMana, p.Mana);
    }

    [Fact]
    public void Xp_below_the_threshold_changes_neither_hp_nor_mana() {
        var p = NewPlayer();
        p.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 1, 0, 3)));
        p.ApplyDamage(400);

        Assert.Equal(0, p.GrantXp(Needed(1) - 1));

        Assert.Equal(600, p.Hp);
        Assert.Equal(3, p.Mana);
        Assert.Equal(Needed(1) - 1, p.Xp);
    }

    [Fact]
    public void Level_up_does_not_resurrect_a_dead_player() {
        var p = NewPlayer();
        p.ApplyDamage(5000);
        Assert.True(p.IsDead);

        p.GrantXp(Needed(1));

        Assert.True(p.IsDead);
        Assert.Equal(Config.MaxHpAt(2), p.MaxHp);
    }

    [Fact]
    public void Non_positive_xp_is_ignored() {
        var p = NewPlayer();

        Assert.Equal(0, p.GrantXp(0));
        Assert.Equal(0, p.GrantXp(-10));
        Assert.Equal(0L, p.Xp);
    }

    [Fact]
    public void Resurrection_restores_the_stat_derived_max_hp() {
        var p = NewPlayer();
        p.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 10, 0, 0)));
        p.ApplyDamage(100_000);

        p.ApplyResurrection();

        Assert.Equal(Config.MaxHpAt(10), p.Hp);
    }
}

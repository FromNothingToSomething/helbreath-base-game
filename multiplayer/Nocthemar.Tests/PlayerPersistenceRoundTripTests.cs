using Server.Nocthemar.Core;
using Server.Nocthemar.Persistence;
using Xunit;

namespace NocthemarTests;

/// <summary>T20: a player's progression survives save -> store -> load -> new player (what a reconnect or a restart does).</summary>
public class PlayerPersistenceRoundTripTests : IDisposable {
    private readonly string dir = Path.Combine(Path.GetTempPath(), "noc-roundtrip-" + Guid.NewGuid().ToString("N"));
    private readonly JsonCharacterStore store;

    public PlayerPersistenceRoundTripTests() {
        store = new JsonCharacterStore(dir, CharacterMigrator.CreateDefault(TestSupport.Progression()), _ => { });
    }

    public void Dispose() {
        try {
            Directory.Delete(dir, recursive: true);
        } catch (IOException) {
            // Also covers DirectoryNotFoundException (the directory is created lazily by the first save): cleanup is best effort.
        }
    }

    private async Task<Server.World.Game.GameWorldPlayer> SaveAndReload(Server.World.Game.GameWorldPlayer player) {
        await store.SaveAsync("hero", player.CreatePersistenceState("aresden"));
        var loaded = await store.LoadAsync("hero");
        var fresh = TestSupport.NewPlayer();
        fresh.ApplyPersistedState(loaded!);
        return fresh;
    }

    [Fact]
    public async Task Level_xp_and_mana_are_identical_after_reload() {
        var player = TestSupport.NewPlayer(5, 123, 37);

        var back = await SaveAndReload(player);

        Assert.Equal(player.CreateNocState(), back.CreateNocState());
        Assert.Equal(player.MaxHp, back.MaxHp);
        Assert.Equal(player.MaxMana, back.MaxMana);
    }

    [Fact]
    public async Task A_level_up_and_the_xp_after_it_are_persisted() {
        var player = TestSupport.NewPlayer();
        Assert.True(TestSupport.Progression().TryGetXpToNext(1, out var needed));
        player.GrantXp(needed + 42);

        var back = await SaveAndReload(player);

        Assert.Equal(2, back.Level);
        Assert.Equal(42L, back.Xp);
        Assert.Equal(back.MaxMana, back.Mana); // refilled by the level-up
    }

    [Fact]
    public async Task Regenerated_mana_is_persisted() {
        var player = TestSupport.NewPlayer(1, 0, 0);
        player.TickMana(1_700_000_000_000);
        player.TickMana(1_700_000_003_000); // +6

        var back = await SaveAndReload(player);

        Assert.Equal(6, back.Mana);
    }

    [Fact]
    public async Task Saving_twice_without_changes_is_stable() {
        var player = TestSupport.NewPlayer(9, 77, 12);

        var once = await SaveAndReload(player);
        var twice = await SaveAndReload(once);

        Assert.Equal(once.CreateNocState(), twice.CreateNocState());
    }

    [Fact]
    public async Task Reload_does_not_mark_anything_to_send() {
        var sent = new List<Mmorpg.Network.ServerMessage>();
        var fresh = TestSupport.NewPlayer(sent);
        var saved = TestSupport.NewPlayer(4, 10, 20).CreatePersistenceState("aresden");
        await store.SaveAsync("hero", saved);
        fresh.ApplyPersistedState((await store.LoadAsync("hero"))!);

        Assert.False(fresh.TryFlushNocUpdates()); // the join InitialState carries the loaded values
        Assert.Empty(sent);
    }
}

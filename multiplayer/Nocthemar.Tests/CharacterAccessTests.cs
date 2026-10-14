using Server.Nocthemar.Core;
using Server.Nocthemar.Persistence;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T6: login/save policy, T22: shutdown log line.</summary>
public class CharacterAccessTests {
    private static PlayerPersistenceState Sample(NocState? noc = null) =>
        new("aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false, CharacterName: "T", SchemaVersion: CharacterSchema.Current, Noc: noc);

    private sealed class FakeStore : ICharacterStore {
        public Func<PlayerPersistenceState?> Load = () => null;
        public Func<bool> Exists = () => false;
        public Exception? SaveFailure;
        public int Saves;

        public Task<PlayerPersistenceState?> LoadAsync(string networkId, CancellationToken ct = default) => Task.FromResult(Load());
        public Task<bool> ExistsAsync(string networkId, CancellationToken ct = default) => Task.FromResult(Exists());

        public Task SaveAsync(string networkId, PlayerPersistenceState state, CancellationToken ct = default) {
            Saves++;
            return SaveFailure is null ? Task.CompletedTask : Task.FromException(SaveFailure);
        }
    }

    [Fact]
    public async Task Missing_save_is_a_new_character() {
        var (state, refusal) = await CharacterAccess.TryLoadAsync(new FakeStore(), "hero", log: _ => { });

        Assert.Null(state);
        Assert.Null(refusal);
    }

    [Fact]
    public async Task Existing_save_is_returned() {
        var saved = Sample();

        var (state, refusal) = await CharacterAccess.TryLoadAsync(new FakeStore { Load = () => saved }, "hero", log: _ => { });

        Assert.Same(saved, state);
        Assert.Null(refusal);
    }

    [Fact]
    public async Task Unreadable_save_refuses_the_login() {
        var logs = new List<string>();

        var (state, refusal) = await CharacterAccess.TryLoadAsync(new FakeStore { Exists = () => true }, "hero", log: logs.Add);

        Assert.Null(state);
        Assert.NotNull(refusal);
        Assert.Single(logs);
    }

    [Fact]
    public async Task Newer_schema_refuses_the_login() {
        var store = new FakeStore { Load = () => throw new CharacterSchemaTooNewException(99, CharacterSchema.Current) };

        var (state, refusal) = await CharacterAccess.TryLoadAsync(store, "hero", log: _ => { });

        Assert.Null(state);
        Assert.Contains("newer", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Store_failure_refuses_the_login() {
        var store = new FakeStore { Load = () => throw new IOException("disk") };

        var (_, refusal) = await CharacterAccess.TryLoadAsync(store, "hero", log: _ => { });

        Assert.NotNull(refusal);
    }

    [Fact]
    public async Task Cancellation_propagates() {
        var store = new FakeStore { Load = () => throw new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CharacterAccess.TryLoadAsync(store, "hero", log: _ => { }));
    }

    [Fact]
    public async Task Save_failures_are_logged_and_reported_not_thrown() {
        var logs = new List<string>();

        foreach (Exception failure in new Exception[] { new IOException("disk"), new ArgumentException("bad id"), new CharacterSchemaTooNewException(99, 1) }) {
            Assert.False(await CharacterAccess.TrySaveAsync(new FakeStore { SaveFailure = failure }, "hero", Sample(), logs.Add));
        }

        Assert.Equal(3, logs.Count);
    }

    [Fact]
    public async Task Successful_save_returns_true() {
        var store = new FakeStore();

        Assert.True(await CharacterAccess.TrySaveAsync(store, "hero", Sample(), _ => { }));
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task Against_the_real_store_a_newer_file_is_refused_and_never_overwritten() {
        var dir = Path.Combine(Path.GetTempPath(), "noc-access-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            var path = Path.Combine(dir, "hero.json");
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(Sample() with { SchemaVersion = 99 }));
            var before = await File.ReadAllBytesAsync(path);
            var store = new JsonCharacterStore(dir, CharacterMigrator.CreateDefault(TestSupport.Progression()), _ => { });

            var (_, refusal) = await CharacterAccess.TryLoadAsync(store, "hero", log: _ => { });

            Assert.NotNull(refusal);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Against_the_real_store_a_corrupt_file_is_refused_and_never_overwritten() {
        var dir = Path.Combine(Path.GetTempPath(), "noc-access-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            var path = Path.Combine(dir, "hero.json");
            await File.WriteAllTextAsync(path, "{not json");
            var store = new JsonCharacterStore(dir, CharacterMigrator.CreateDefault(TestSupport.Progression()), _ => { });

            var (state, refusal) = await CharacterAccess.TryLoadAsync(store, "hero", log: _ => { });

            Assert.Null(state);
            Assert.NotNull(refusal);
            Assert.Equal("{not json", await File.ReadAllTextAsync(path));
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Shutdown_line_reports_id_level_xp_mana_and_outcome() {
        var line = CharacterLog.FormatShutdownLine("hero", Sample(new NocState(NocState.CurrentV, 7, 1234, 56)), saved: true);

        Assert.Equal("[Server] Shutdown save: id=hero level=7 xp=1234 mana=56 saved=yes", line);
    }

    [Fact]
    public void Shutdown_line_handles_a_missing_noc_block_and_a_failed_save() {
        var line = CharacterLog.FormatShutdownLine("hero", Sample(), saved: false);

        Assert.Equal("[Server] Shutdown save: id=hero noc=none saved=no", line);
    }
}

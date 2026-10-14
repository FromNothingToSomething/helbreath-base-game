using System.Text.Json;
using Server.Nocthemar.Core;
using Server.Nocthemar.Persistence;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T5: JsonCharacterStore round-trip, migration on load, atomic writes, hostile ids.</summary>
public sealed class JsonCharacterStoreTests : IDisposable {
    private readonly string root = Path.Combine(Path.GetTempPath(), "noc-store-" + Guid.NewGuid().ToString("N"));
    private readonly string chars;
    private readonly List<string> logs = new();
    private readonly JsonCharacterStore store;

    public JsonCharacterStoreTests() {
        Directory.CreateDirectory(root);
        chars = Path.Combine(root, "Chars"); // not created: SaveAsync must create it
        store = new JsonCharacterStore(chars, CharacterMigrator.CreateDefault(TestSupport.Progression()), logs.Add);
    }

    public void Dispose() {
        try {
            Directory.Delete(root, recursive: true);
        } catch (IOException) {
        }
    }

    private static PlayerPersistenceState Sample(int x = 10, NocState? noc = null, int schemaVersion = CharacterSchema.Legacy) =>
        new("aresden", x, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false, CharacterName: "T", SchemaVersion: schemaVersion, Noc: noc);

    private string SavePath(string id) => Path.Combine(chars, id + ".json");

    [Fact]
    public async Task Save_then_load_round_trips_and_stamps_current_version() {
        var noc = new NocState(NocState.CurrentV, 5, 1200, 40);

        await store.SaveAsync("hero", Sample(noc: noc));
        var back = await store.LoadAsync("hero");

        Assert.NotNull(back);
        Assert.Equal(CharacterSchema.Current, back!.SchemaVersion);
        Assert.Equal(noc, back.Noc);
        Assert.Equal("T", back.CharacterName);
        Assert.Equal(10, back.X);
    }

    [Fact]
    public async Task Missing_save_loads_as_null_and_does_not_exist() {
        Assert.Null(await store.LoadAsync("nobody"));
        Assert.False(await store.ExistsAsync("nobody"));
    }

    [Fact]
    public async Task Exists_is_true_after_save() {
        await store.SaveAsync("hero", Sample());

        Assert.True(await store.ExistsAsync("hero"));
    }

    [Fact]
    public async Task Legacy_file_is_migrated_on_load_but_not_rewritten() {
        Directory.CreateDirectory(chars);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy_char_full.json"), SavePath("legacy"));
        var before = File.ReadAllBytes(SavePath("legacy"));

        var loaded = await store.LoadAsync("legacy");

        Assert.NotNull(loaded);
        Assert.Equal(CharacterSchema.Current, loaded!.SchemaVersion);
        Assert.Equal(1, loaded.Noc!.Level);
        Assert.Equal(100, loaded.Noc.Mana);
        Assert.Equal("Tester", loaded.CharacterName);
        Assert.Equal(before, File.ReadAllBytes(SavePath("legacy")));
    }

    [Fact]
    public async Task Second_save_replaces_the_first_and_leaves_no_temp_file() {
        await store.SaveAsync("hero", Sample(x: 1));
        await store.SaveAsync("hero", Sample(x: 2));

        var back = await store.LoadAsync("hero");

        Assert.Equal(2, back!.X);
        var files = Directory.GetFiles(chars);
        Assert.Single(files);
        Assert.Equal(SavePath("hero"), files[0]);
    }

    [Fact]
    public async Task Crash_before_rename_keeps_the_previous_save_intact() {
        await store.SaveAsync("hero", Sample(x: 10));
        var before = File.ReadAllBytes(SavePath("hero"));
        store.AfterTempWritten = _ => throw new IOException("simulated crash");

        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync("hero", Sample(x: 99)));
        store.AfterTempWritten = null;

        Assert.Equal(before, File.ReadAllBytes(SavePath("hero")));
        Assert.Empty(Directory.GetFiles(chars, "*.tmp"));
        Assert.Equal(10, (await store.LoadAsync("hero"))!.X);
    }

    [Fact]
    public async Task Stale_temp_file_from_a_real_crash_is_ignored() {
        await store.SaveAsync("hero", Sample(x: 10));
        File.WriteAllText(SavePath("hero") + "." + Guid.NewGuid().ToString("N") + ".tmp", "{ half written");

        Assert.Equal(10, (await store.LoadAsync("hero"))!.X);
        Assert.True(await store.ExistsAsync("hero"));
    }

    [Fact]
    public async Task Corrupt_save_loads_as_null_is_logged_and_left_untouched() {
        Directory.CreateDirectory(chars);
        await File.WriteAllTextAsync(SavePath("hero"), "{not json");

        Assert.Null(await store.LoadAsync("hero"));

        Assert.NotEmpty(logs);
        Assert.Equal("{not json", await File.ReadAllTextAsync(SavePath("hero")));
    }

    [Fact]
    public async Task Save_from_a_newer_server_is_refused_and_left_untouched() {
        Directory.CreateDirectory(chars);
        await File.WriteAllTextAsync(SavePath("hero"), JsonSerializer.Serialize(Sample(schemaVersion: 99)));
        var before = File.ReadAllBytes(SavePath("hero"));

        var ex = await Assert.ThrowsAsync<CharacterSchemaTooNewException>(() => store.LoadAsync("hero"));

        Assert.Equal(99, ex.Found);
        Assert.Contains(logs, l => l.Contains("untouched", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(SavePath("hero")));
    }

    [Fact]
    public async Task Saving_a_state_with_a_future_version_is_refused() {
        await Assert.ThrowsAsync<CharacterSchemaTooNewException>(() => store.SaveAsync("hero", Sample(schemaVersion: 99)));

        Assert.False(File.Exists(SavePath("hero")));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a\0b")]
    [InlineData("")]
    public async Task Hostile_ids_are_rejected_everywhere_and_write_nothing(string id) {
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(id, Sample()));

        Assert.Null(await store.LoadAsync(id));
        Assert.False(await store.ExistsAsync(id));
        Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("player1")]
    [InlineData("user.name")]
    [InlineData("a b")]
    [InlineData("üñí")]
    public async Task Plain_ids_round_trip(string id) {
        await store.SaveAsync(id, Sample(x: 7));

        Assert.Equal(7, (await store.LoadAsync(id))!.X);
    }

    [Fact]
    public async Task Concurrent_saves_always_leave_a_valid_loadable_file() {
        if (OperatingSystem.IsWindows()) {
            return; // Replacing a file that another thread is replacing can throw on Windows; the world serializes saves per character.
        }

        await Task.WhenAll(Enumerable.Range(1, 50).Select(i => store.SaveAsync("hero", Sample(x: i))));

        var back = await store.LoadAsync("hero");
        Assert.NotNull(back);
        Assert.InRange(back!.X, 1, 50);
        Assert.Empty(Directory.GetFiles(chars, "*.tmp"));
    }
}

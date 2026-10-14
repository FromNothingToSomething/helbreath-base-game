using System.Text.Json;
using Server;
using Server.Nocthemar.Persistence;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T7: provider selection and the PostgreSQL stub.</summary>
public class CharacterStoreFactoryTests {
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "noc-factory-unused");

    private static object Create(PersistenceConfig? config) =>
        CharacterStoreFactory.Create(config, Dir, TestSupport.Progression());

    [Fact]
    public void Missing_config_defaults_to_the_json_store() => Assert.IsType<JsonCharacterStore>(Create(null));

    [Theory]
    [InlineData("json")]
    [InlineData("JSON")]
    [InlineData(" Json ")]
    public void Json_provider_is_case_and_space_insensitive(string provider) =>
        Assert.IsType<JsonCharacterStore>(Create(new PersistenceConfig(provider)));

    [Theory]
    [InlineData("pg")]
    [InlineData("PG")]
    public void Pg_provider_fails_clearly_at_startup(string provider) {
        var ex = Assert.Throws<InvalidOperationException>(() => Create(new PersistenceConfig(provider, "Host=x")));

        Assert.Contains("pg", ex.Message, StringComparison.Ordinal);
        Assert.Contains("json", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mongo")]
    [InlineData("")]
    public void Unknown_provider_is_rejected(string provider) =>
        Assert.Throws<InvalidOperationException>(() => Create(new PersistenceConfig(provider)));

    [Fact]
    public async Task Pg_store_stub_throws_on_every_member() {
        var store = new PgCharacterStore("Host=x");
        var state = new PlayerPersistenceState("aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false);

        await Assert.ThrowsAsync<NotSupportedException>(() => store.LoadAsync("a"));
        await Assert.ThrowsAsync<NotSupportedException>(() => store.SaveAsync("a", state));
        await Assert.ThrowsAsync<NotSupportedException>(() => store.ExistsAsync("a"));
    }

    [Fact]
    public void Pg_store_requires_a_connection_string() =>
        Assert.ThrowsAny<ArgumentException>(() => new PgCharacterStore(" "));

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Settings_json_persistence_section_deserializes_and_is_optional() {
        var withSection = JsonSerializer.Deserialize<PersistenceConfig>("""{"provider":"pg","connectionString":"Host=x"}""", Options);
        var empty = JsonSerializer.Deserialize<PersistenceConfig>("{}", Options);

        Assert.Equal(new PersistenceConfig("pg", "Host=x"), withSection);
        Assert.Equal(PersistenceConfig.JsonProvider, empty!.Provider);
    }

    [Fact]
    public void Shipped_settings_json_selects_the_json_provider() {
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "Settings.json");
        Assert.True(File.Exists(path), "Settings.json must be copied next to the tests");

        var settings = JsonSerializer.Deserialize<SettingsConfig>(File.ReadAllText(path), Options);

        Assert.Equal(PersistenceConfig.JsonProvider, settings!.Persistence!.Provider);
    }
}

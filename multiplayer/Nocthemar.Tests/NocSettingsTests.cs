using System.Text.Json;
using Server;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T19: nocthemar.debug in Settings.json.</summary>
public class NocSettingsTests {
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Shipped_settings_keep_debug_off() {
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "Settings.json");

        var settings = JsonSerializer.Deserialize<SettingsConfig>(File.ReadAllText(path), Options);

        Assert.NotNull(settings!.Nocthemar);
        Assert.False(settings.Nocthemar!.Debug);
    }

    [Fact]
    public void Debug_can_be_switched_on() {
        var parsed = JsonSerializer.Deserialize<NocthemarSettings>("""{"debug":true}""", Options);

        Assert.True(parsed!.Debug);
    }

    [Fact]
    public void Empty_section_means_debug_off() {
        Assert.False(JsonSerializer.Deserialize<NocthemarSettings>("{}", Options)!.Debug);
    }

    [Fact]
    public void Missing_section_in_a_settings_file_means_everything_off() {
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "Settings.json");
        var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
        var withoutSection = string.Join(",", json.EnumerateObject().Where(p => p.Name != "nocthemar").Select(p => $"\"{p.Name}\":{p.Value.GetRawText()}"));

        var settings = JsonSerializer.Deserialize<SettingsConfig>("{" + withoutSection + "}", Options);

        Assert.Null(settings!.Nocthemar);
        Assert.False(settings.Nocthemar?.Debug ?? false);
    }
}

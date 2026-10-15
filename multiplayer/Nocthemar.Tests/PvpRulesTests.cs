using System.Text.Json;
using Server;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T80: nocthemar.pvpMode (legacy by default) and who may hurt whom between players.</summary>
public class PvpRulesTests {
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(null, 2)]
    [InlineData(2, null)]
    [InlineData(3, 3)]
    public void Legacy_lets_anyone_hurt_anyone(int? attacker, int? target) {
        Assert.True(PvpRules.CanHarm(PvpPolicy.Legacy, attacker, target));
    }

    [Theory]
    [InlineData(0, 0, false)] // same faction
    [InlineData(3, 3, false)]
    [InlineData(0, 1, true)] // rivals
    [InlineData(2, 3, true)]
    [InlineData(0, 2, true)] // different, not rivals
    [InlineData(null, 0, true)] // unaffiliated attacker
    [InlineData(0, null, true)] // unaffiliated target
    [InlineData(null, null, true)]
    public void Factions_only_protects_members_of_the_same_faction(int? attacker, int? target, bool expected) {
        Assert.Equal(expected, PvpRules.CanHarm(PvpPolicy.Factions, attacker, target));
    }

    [Theory]
    [InlineData(null, PvpPolicy.Legacy)]
    [InlineData("legacy", PvpPolicy.Legacy)]
    [InlineData("LEGACY", PvpPolicy.Legacy)]
    [InlineData("factions", PvpPolicy.Factions)]
    [InlineData("Factions", PvpPolicy.Factions)]
    public void The_mode_names_the_policy(string? mode, PvpPolicy expected) {
        Assert.Equal(expected, PvpRules.Parse(mode));
        Assert.Equal(expected, new NocthemarSettings(PvpMode: mode).ResolvePvpPolicy());
    }

    [Theory]
    [InlineData("")]
    [InlineData("faction")]
    [InlineData("pvp")]
    public void An_unknown_mode_is_a_configuration_error(string mode) {
        var error = Assert.Throws<InvalidOperationException>(() => new NocthemarSettings(PvpMode: mode).ResolvePvpPolicy());

        Assert.Contains("pvpMode", error.Message);
    }

    [Fact]
    public void Settings_without_the_field_are_legacy_and_keep_debug() {
        var parsed = JsonSerializer.Deserialize<NocthemarSettings>("""{"debug":true}""", Options)!;

        Assert.True(parsed.Debug);
        Assert.Null(parsed.PvpMode);
        Assert.Equal(PvpPolicy.Legacy, parsed.ResolvePvpPolicy());
        Assert.Equal(PvpPolicy.Legacy, JsonSerializer.Deserialize<NocthemarSettings>("{}", Options)!.ResolvePvpPolicy());
    }

    [Fact]
    public void The_mode_is_read_from_json() {
        var parsed = JsonSerializer.Deserialize<NocthemarSettings>("""{"pvpMode":"factions"}""", Options)!;

        Assert.Equal(PvpPolicy.Factions, parsed.ResolvePvpPolicy());
        Assert.False(parsed.Debug);
    }

    [Fact]
    public void The_shipped_settings_are_legacy() {
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "Settings.json");
        var settings = JsonSerializer.Deserialize<SettingsConfig>(File.ReadAllText(path), Options)!;

        Assert.Equal("legacy", settings.Nocthemar!.PvpMode);
        Assert.Equal(PvpPolicy.Legacy, settings.Nocthemar.ResolvePvpPolicy());
    }

    [Fact]
    public void Config_validates_the_mode_when_it_loads_the_settings() {
        var source = Source("Config.cs");

        Assert.Contains("settings.Nocthemar?.ResolvePvpPolicy()", source);
    }

    [Fact]
    public void Every_player_versus_player_damage_path_asks_the_world() {
        var combat = Source("Helpers", "Combat.cs");
        var casting = Source("Helpers", "Casting.cs");

        Assert.Equal(2, Count(combat, "wr.World.CanPlayerHarmPlayer(player, targetPlayer)") + Count(combat, "wr.World.CanPlayerHarmPlayer(attacker, targetPlayer)"));
        Assert.Equal(2, Count(casting, "wr.World.CanPlayerHarmPlayer(caster, targetPlayer)"));
        // the guard comes before the damage in each spell loop
        Assert.Matches(@"CanPlayerHarmPlayer\(caster, targetPlayer\)\)\s*\{\s*continue;\s*\}\s*Combat\.ApplyPlayerDamageToPlayer", casting);
        Assert.Matches(@"CanPlayerHarmPlayer\(player, targetPlayer\)\)\s*\{\s*return;\s*\}", combat);
    }

    [Fact]
    public void The_world_resolves_the_policy_once_and_lets_self_through() {
        var world = Source("World", "Game", "GameWorld.cs");

        Assert.Contains("pvpPolicy = settings.Nocthemar?.ResolvePvpPolicy() ?? PvpPolicy.Legacy;", world);
        Assert.Contains("attacker.PlayerId == target.PlayerId || PvpRules.CanHarm(pvpPolicy, attacker.FactionId, target.FactionId)", world);
    }

    private static int Count(string text, string needle) => text.Split(needle).Length - 1;

    private static string Source(params string[] relative) {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine([dir.FullName, "multiplayer", "server", .. relative]);
            if (File.Exists(candidate)) {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(Path.Combine(relative));
    }
}

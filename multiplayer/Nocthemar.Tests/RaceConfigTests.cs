using System.Text.Json;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T24: Races.json parsing and validation, and its parity with <see cref="StatId"/>.</summary>
public class RaceConfigTests {
    private const string Appearance = """{"genders":[0,1],"skinColors":[0,1,2],"hairStyles":[0,1,2,3,4,5,6,7],"underwearColors":[0,1,2,3,4,5,6,7]}""";
    private const string NativeArt = "{\"native\":true}";

    private static readonly string Human = Race(0, "human");

    private static string Doc(string races, int defaultRaceId = 0, int schema = 1) =>
        "{\"schemaVersion\":" + schema + ",\"defaultRaceId\":" + defaultRaceId + ",\"races\":[" + races + "]}";

    private static string Race(int id, string key, string modifiers = "", string traits = "", string? art = null, string? appearance = null) =>
        "{\"id\":" + id + ",\"key\":\"" + key + "\",\"name\":\"N" + id + "\",\"modifiers\":[" + modifiers + "],\"traits\":[" + traits +
        "],\"appearance\":" + (appearance ?? Appearance) + ",\"art\":" + (art ?? NativeArt) + "}";

    private static void Rejects(string json, string expected) {
        var ex = Assert.Throws<InvalidOperationException>(() => RaceConfig.Parse(json));
        Assert.Contains(expected, ex.Message);
    }

    private static string ShippedPath() => Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Races.json");

    // --- shipped file -----------------------------------------------------------------------------------------------

    [Fact]
    public void Shipped_file_parses_with_six_races() {
        var config = RaceConfig.Parse(File.ReadAllText(ShippedPath()));

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, config.Races.Select(r => r.Id).ToArray());
        Assert.Equal(
            new[] { "valdornien", "spectral", "sang_mele", "reanime", "chimere", "lychen" },
            config.Races.Select(r => r.Key).ToArray());
        Assert.Equal(0, config.DefaultRaceId);
        Assert.Equal("valdornien", config.Default.Key);
    }

    [Fact]
    public async Task Shipped_file_loads_from_disk() {
        var config = await RaceConfig.LoadAsync(ShippedPath());

        Assert.Equal(6, config.Races.Count);
    }

    [Fact]
    public void Shipped_modifiers_match_the_design_document() {
        var config = RaceConfig.Parse(File.ReadAllText(ShippedPath()));

        var valdornien = config.Races[0];
        Assert.Equal(5, valdornien.Modifiers.Count);
        foreach (var stat in new[] { StatId.MaxHp, StatId.MaxMana, StatId.ManaRegen, StatId.DamageMult }) {
            Assert.Contains(valdornien.Modifiers, m => m.Stat == stat && m.Kind == ModifierKind.PercentMult && m.Value == 0.05);
        }

        Assert.Contains(valdornien.Modifiers, m => m.Stat == StatId.XpGainMult && m.Kind == ModifierKind.PercentAdd && m.Value == 0.10);
        Assert.Contains(config.Races[1].Modifiers, m => m.Stat == StatId.MaxHp && m.Kind == ModifierKind.PercentMult && m.Value == -0.10);
        Assert.Contains(config.Races[2].Modifiers, m => m.Stat == StatId.DamageMult && m.Kind == ModifierKind.PercentAdd && m.Value == 0.10);
        Assert.Contains(config.Races[4].Modifiers, m => m.Stat == StatId.ArmorMult && m.Value == 0.15);
        Assert.Contains(config.Races[5].Modifiers, m => m.Stat == StatId.MoveSpeedMult && m.Value == 0.15);
    }

    [Fact]
    public void Every_shipped_modifier_carries_its_race_source_id() {
        var config = RaceConfig.Parse(File.ReadAllText(ShippedPath()));

        foreach (var race in config.Races) {
            Assert.Equal(ModifierSources.Race(race.Id), race.SourceId);
            Assert.All(race.Modifiers, m => Assert.Equal(race.SourceId, m.SourceId));
        }
    }

    [Fact]
    public void Every_stat_referenced_by_Races_json_exists_in_StatId() {
        using var doc = JsonDocument.Parse(File.ReadAllText(ShippedPath()));
        var names = Enum.GetNames<StatId>().ToHashSet(StringComparer.Ordinal);
        var referenced = doc.RootElement.GetProperty("races").EnumerateArray()
            .SelectMany(r => r.GetProperty("modifiers").EnumerateArray())
            .Select(m => m.GetProperty("stat").GetString()!)
            .ToArray();

        Assert.NotEmpty(referenced);
        // Exact casing in the shipped file, so a rename in StatId cannot hide behind the parser's case-insensitivity.
        Assert.All(referenced, name => Assert.True(names.Contains(name), $"Races.json references unknown stat '{name}'."));
    }

    [Fact]
    public void Non_native_races_carry_a_TODO_art_note_and_the_human_race_does_not() {
        var config = RaceConfig.Parse(File.ReadAllText(ShippedPath()));

        Assert.True(config.Races[0].Art.Native);
        Assert.Null(config.Races[0].Art.Todo);
        foreach (var race in config.Races.Skip(1)) {
            Assert.False(race.Art.Native);
            Assert.StartsWith("TODO(art)", race.Art.Todo);
            Assert.NotNull(race.Art.Tint);
        }
    }

    [Fact]
    public void Shipped_appearance_allows_the_full_ranges() {
        var config = RaceConfig.Parse(File.ReadAllText(ShippedPath()));

        foreach (var race in config.Races) {
            Assert.True(race.Appearance.Allows(1, 2, 7, 7));
            Assert.True(race.Appearance.Allows(0, 0, 0, 0));
            Assert.False(race.Appearance.Allows(2, 0, 0, 0));
        }
    }

    // --- lookup ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Lookup_by_id() {
        var config = RaceConfig.Parse(File.ReadAllText(ShippedPath()));

        Assert.True(config.TryGet(3, out var race));
        Assert.Equal("reanime", race.Key);
        Assert.False(config.TryGet(99, out _));
        Assert.True(config.Contains(5));
        Assert.False(config.Contains(-1));
    }

    [Fact]
    public void Appearance_restrictions_are_enforced_per_race() {
        var restricted = """{"genders":[0],"skinColors":[1],"hairStyles":[2],"underwearColors":[3]}""";
        var config = RaceConfig.Parse(Doc(Human + "," + Race(1, "ghost", appearance: restricted)));

        var appearance = config.Races[1].Appearance;
        Assert.True(appearance.Allows(0, 1, 2, 3));
        Assert.False(appearance.Allows(1, 1, 2, 3));
        Assert.False(appearance.Allows(0, 0, 2, 3));
        Assert.False(appearance.Allows(0, 1, 0, 3));
        Assert.False(appearance.Allows(0, 1, 2, 0));
    }

    // --- rejections ------------------------------------------------------------------------------------------------

    [Fact]
    public void Rejects_bad_documents() {
        Rejects("null", "empty document");
        Rejects("{not json", "Invalid Races.json");
        Rejects(Doc(Human, schema: 2), "schemaVersion must be 1");
        Rejects("""{"schemaVersion":1,"defaultRaceId":0,"races":[]}""", "at least one race");
        Rejects("""{"schemaVersion":1,"defaultRaceId":0}""", "at least one race");
        Rejects(Doc(Human, defaultRaceId: 7), "defaultRaceId 7 is not the id of a listed race");
        Rejects("""{"schemaVersion":1,"races":[]}""", "defaultRaceId");
    }

    [Fact]
    public void Rejects_unmapped_members_so_typos_fail_loudly() {
        Rejects(Doc(Human.Replace("\"traits\"", "\"trait\"")), "trait");
        Rejects(Doc(Human).Replace("\"races\"", "\"racez\""), "racez");
    }

    [Fact]
    public void Rejects_duplicate_and_invalid_ids_and_keys() {
        Rejects(Doc(Human + "," + Race(0, "other")), "id 0 is duplicated");
        Rejects(Doc(Human + "," + Race(1, "human")), "'human' is duplicated");
        Rejects(Doc(Race(-1, "human")), "id must be in [0, 65535]");
        Rejects(Doc(Race(65536, "human")), "id must be in [0, 65535]");
        Rejects(Doc(Race(0, "Human")), "key must match");
        Rejects(Doc(Race(0, "1human")), "key must match");
    }

    [Fact]
    public void Rejects_unknown_stat_kind_and_numeric_names() {
        Rejects(Doc(Race(0, "human", """{"stat":"Charisma","kind":"Flat","value":1}""")), "'Charisma' is not a StatId");
        Rejects(Doc(Race(0, "human", """{"stat":"3","kind":"Flat","value":1}""")), "'3' is not a StatId");
        Rejects(Doc(Race(0, "human", """{"stat":" 3","kind":"Flat","value":1}""")), "is not a StatId");
        Rejects(Doc(Race(0, "human", """{"stat":"MaxHp, MaxMana","kind":"Flat","value":1}""")), "is not a StatId");
        Rejects(Doc(Race(0, "human", """{"stat":"MaxHp ","kind":"Flat","value":1}""")), "is not a StatId");
        Rejects(Doc(Race(0, "human", """{"stat":"MaxHp","kind":"Percent","value":0.1}""")), "'Percent' is not a ModifierKind");
        Rejects(Doc(Race(0, "human", """{"stat":"MaxHp","kind":"1","value":0.1}""")), "'1' is not a ModifierKind");
        Rejects(Doc(Race(0, "human", """{"kind":"Flat","value":1}""")), "is not a StatId");
    }

    [Fact]
    public void Accepts_stat_names_case_insensitively() {
        var config = RaceConfig.Parse(Doc(Race(0, "human", """{"stat":"maxhp","kind":"percentmult","value":0.1}""")));

        Assert.Equal(StatId.MaxHp, config.Races[0].Modifiers[0].Stat);
        Assert.Equal(ModifierKind.PercentMult, config.Races[0].Modifiers[0].Kind);
    }

    [Fact]
    public void Rejects_bad_modifier_values() {
        Rejects(Doc(Race(0, "human", """{"stat":"MaxHp","kind":"Flat"}""")), "value must be a finite number");
        Rejects(Doc(Race(0, "human", """{"stat":"MaxHp","kind":"PercentAdd","value":-1}""")), "percent modifier must be in (-1, 10]");
        Rejects(Doc(Race(0, "human", """{"stat":"MaxHp","kind":"PercentMult","value":11}""")), "percent modifier must be in (-1, 10]");
        Rejects(Doc(Race(0, "human", """{"stat":"MaxHp","kind":"Flat","value":2000000}""")), "flat modifier must be within");
        Rejects(Doc(Race(0, "human", """{"stat":"ResistMagic","kind":"PercentAdd","value":0.1}""")), "ResistMagic takes Flat modifiers");
    }

    [Fact]
    public void Rejects_a_modifier_declared_twice() {
        var twice = """{"stat":"MaxHp","kind":"PercentMult","value":0.1},{"stat":"MaxHp","kind":"PercentMult","value":0.2}""";

        Rejects(Doc(Race(0, "human", twice)), "declared twice");
    }

    [Fact]
    public void Same_stat_with_different_kinds_is_allowed() {
        var both = """{"stat":"MaxHp","kind":"Flat","value":10},{"stat":"MaxHp","kind":"PercentMult","value":0.2}""";

        Assert.Equal(2, RaceConfig.Parse(Doc(Race(0, "human", both))).Races[0].Modifiers.Count);
    }

    [Fact]
    public void Rejects_missing_modifiers_and_traits_sections_and_bad_traits() {
        Rejects(Doc(Human.Replace("\"modifiers\":[],", "")), "modifiers missing");
        Rejects(Doc(Human.Replace("\"traits\":[],", "")), "traits missing");
        Rejects(Doc(Race(0, "human", traits: "\"Bad Trait\"")), "traits entry 'Bad Trait'");
        Rejects(Doc(Race(0, "human", traits: "\"a_trait\",\"a_trait\"")), "duplicated");
    }

    [Fact]
    public void Rejects_bad_appearance() {
        Rejects(Doc(Race(0, "human", appearance: """{"genders":[0,2],"skinColors":[0],"hairStyles":[0],"underwearColors":[0]}""")), "genders values must be in [0, 1]");
        Rejects(Doc(Race(0, "human", appearance: """{"genders":[0],"skinColors":[3],"hairStyles":[0],"underwearColors":[0]}""")), "skinColors values must be in [0, 2]");
        Rejects(Doc(Race(0, "human", appearance: """{"genders":[0],"skinColors":[0],"hairStyles":[8],"underwearColors":[0]}""")), "hairStyles values must be in [0, 7]");
        Rejects(Doc(Race(0, "human", appearance: """{"genders":[0],"skinColors":[0],"hairStyles":[0],"underwearColors":[]}""")), "underwearColors must list at least one value");
        Rejects(Doc(Race(0, "human", appearance: """{"genders":[0,0],"skinColors":[0],"hairStyles":[0],"underwearColors":[0]}""")), "genders has duplicate values");
        Rejects(Doc(Human.Replace($"\"appearance\":{Appearance},", "")), "appearance missing");
    }

    [Fact]
    public void Rejects_bad_art() {
        Rejects(Doc(Race(0, "human", art: """{"native":false,"tint":"#112233"}""")), "needs a todo starting with 'TODO(art)'");
        Rejects(Doc(Race(0, "human", art: """{"native":false,"todo":"later"}""")), "needs a todo starting with 'TODO(art)'");
        Rejects(Doc(Race(0, "human", art: """{"native":true,"todo":"TODO(art): x"}""")), "native race must not carry a todo");
        Rejects(Doc(Race(0, "human", art: """{"native":true,"tint":"red"}""")), "tint must be #RRGGBB");
        Rejects(Doc(Human.Replace("\"art\":{\"native\":true}", "\"art\":null")), "art missing");
    }

    [Fact]
    public void Error_message_lists_every_problem_not_only_the_first() {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RaceConfig.Parse(Doc(Race(0, "human", """{"stat":"Nope","kind":"Flat","value":1}""") + "," + Race(0, "human2"), schema: 5)));

        Assert.Contains("schemaVersion", ex.Message);
        Assert.Contains("'Nope' is not a StatId", ex.Message);
        Assert.Contains("id 0 is duplicated", ex.Message);
    }
}

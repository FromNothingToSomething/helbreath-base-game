using System.Text.Json;
using Server.Nocthemar.Core;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T2: pre-versioning saves must still load, and the new fields must stay optional.</summary>
public class PersistenceSchemaTests {
    // Same call shape as Server.cs LoadPlayerPersistenceState: default JsonSerializerOptions, stream input.
    private static PlayerPersistenceState LoadFixture(string name) {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        return JsonSerializer.Deserialize<PlayerPersistenceState>(stream)!;
    }

    private static PlayerPersistenceState Minimal(int schemaVersion = CharacterSchema.Legacy, NocState? noc = null) =>
        new("aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false, SchemaVersion: schemaVersion, Noc: noc);

    [Fact]
    public void Legacy_full_file_deserializes_and_new_fields_take_defaults() {
        var state = LoadFixture("legacy_char_full.json");

        Assert.Equal("aresden", state.GameWorldId);
        Assert.Equal(120, state.X);
        Assert.Equal(5, state.FacingDirection);
        Assert.Equal("Tester", state.CharacterName);
        Assert.Single(state.BagItems!);
        Assert.Equal("weapon", state.EquippedItems![0].Slot);
        Assert.Equal(CharacterSchema.Legacy, state.SchemaVersion);
        Assert.Null(state.Noc);
    }

    [Fact]
    public void Legacy_minimal_file_deserializes() {
        var state = LoadFixture("legacy_char_minimal.json");

        Assert.Equal("elvine", state.GameWorldId);
        Assert.True(state.AttackMode);
        Assert.Null(state.BagItems);
        Assert.Equal("", state.CharacterName);
        Assert.Equal(CharacterSchema.Legacy, state.SchemaVersion);
        Assert.Null(state.Noc);
    }

    [Fact]
    public void Noc_block_round_trips() {
        var noc = new NocState(NocState.CurrentV, 5, 1200, 40, 2, null, null);
        var original = Minimal(CharacterSchema.Current, noc);

        var json = JsonSerializer.Serialize(original);
        var back = JsonSerializer.Deserialize<PlayerPersistenceState>(json)!;

        Assert.Equal(CharacterSchema.Current, back.SchemaVersion);
        Assert.Equal(noc, back.Noc);
        Assert.Equal(json, JsonSerializer.Serialize(back));
    }

    [Fact]
    public void Unknown_members_are_ignored_at_both_levels() {
        const string json = """
            {"GameWorldId":"a","X":1,"Y":2,"MovementSpeedMs":300,"CastSpeedMs":1000,"AttackSpeedMs":600,
             "AttackRange":1,"Damage":20,"StunDuration":500,"AttackType":0,"AttackMode":false,"RunMode":false,
             "AllowDashAttack":false,"SchemaVersion":1,"FutureField":123,
             "Noc":{"V":1,"Level":7,"Xp":5,"Mana":9,"FutureNocField":{"a":1}}}
            """;

        var state = JsonSerializer.Deserialize<PlayerPersistenceState>(json)!;

        Assert.Equal(7, state.Noc!.Level);
        Assert.Equal(9, state.Noc.Mana);
    }
}

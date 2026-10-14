using System.Text.Json;
using Server.Nocthemar.Core;
using Xunit;

namespace NocthemarTests;

/// <summary>T3: NocState round-trip, forward compatibility, defaults.</summary>
public class NocStateTests {
    [Fact]
    public void CreateDefault_is_level_one_with_given_mana() {
        var state = NocState.CreateDefault(100);

        Assert.Equal(1, state.Level);
        Assert.Equal(0, state.Xp);
        Assert.Equal(100, state.Mana);
        Assert.Equal(NocState.CurrentV, state.V);
        Assert.Null(state.RaceId);
        Assert.Null(state.ClassId);
        Assert.Null(state.FactionId);
    }

    [Fact]
    public void CreateDefault_never_negative_mana() => Assert.Equal(0, NocState.CreateDefault(-5).Mana);

    [Fact]
    public void Round_trip_is_identical() {
        var original = new NocState(1, 42, 9_000_000_000L, 77, 3, 1, 2);

        var json = JsonSerializer.Serialize(original);
        var back = JsonSerializer.Deserialize<NocState>(json)!;

        Assert.Equal(original, back);
        Assert.Equal(json, JsonSerializer.Serialize(back));
    }

    [Fact]
    public void Unknown_json_member_is_ignored() {
        var state = JsonSerializer.Deserialize<NocState>("""{"V":1,"Level":3,"Xp":10,"Mana":5,"SomethingNew":[1,2]}""")!;

        Assert.Equal(3, state.Level);
    }

    [Fact]
    public void Missing_members_take_defaults() {
        var state = JsonSerializer.Deserialize<NocState>("""{"Level":9}""")!;

        Assert.Equal(NocState.CurrentV, state.V);
        Assert.Equal(9, state.Level);
        Assert.Equal(0, state.Xp);
        Assert.Null(state.ClassId);
    }
}

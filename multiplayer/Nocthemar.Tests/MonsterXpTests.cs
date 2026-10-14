using Server;
using Xunit;

namespace NocthemarTests;

/// <summary>T13: XP value of a kill, and the Monsters.json xp field.</summary>
public class MonsterXpTests {
    private static readonly Server.Nocthemar.Data.ProgressionConfig Config = TestSupport.Progression();

    [Fact]
    public void Configured_xp_wins() => Assert.Equal(250, Config.ResolveMonsterXp(250));

    [Fact]
    public void Zero_xp_is_a_valid_explicit_value() => Assert.Equal(0, Config.ResolveMonsterXp(0));

    [Fact]
    public void Missing_xp_falls_back_to_the_progression_default() =>
        Assert.Equal(Config.DefaultMonsterXp, Config.ResolveMonsterXp(null));

    [Fact]
    public void Negative_xp_falls_back_to_the_progression_default() =>
        Assert.Equal(Config.DefaultMonsterXp, Config.ResolveMonsterXp(-5));

    [Fact]
    public void Kill_xp_levels_the_killer_up_through_the_player() {
        var p = TestSupport.NewPlayer();
        Assert.True(Config.TryGetXpToNext(1, out var needed));

        p.GrantXp(Config.ResolveMonsterXp(null));
        Assert.Equal(Config.DefaultMonsterXp, p.Xp);

        p.GrantXp(Config.ResolveMonsterXp((int)needed));
        Assert.Equal(2, p.Level);
    }

    [Fact]
    public void Monsters_json_xp_field_is_read_and_optional() {
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var withXp = System.Text.Json.JsonSerializer.Deserialize<MonsterConfig>("""{"id":1,"name":"a","sprite":"b","movementSpeed":1,"xp":40}""", options);
        var without = System.Text.Json.JsonSerializer.Deserialize<MonsterConfig>("""{"id":1,"name":"a","sprite":"b","movementSpeed":1}""", options);

        Assert.Equal(40, withXp!.Xp);
        Assert.Null(without!.Xp);
    }

    [Fact]
    public void Negative_monster_xp_is_rejected_at_startup() {
        var bad = new MonsterConfig(0, "a", "b", 1, Xp: -1);

        var ex = Assert.Throws<InvalidOperationException>(() => Config_BuildCatalog(bad));
        Assert.Contains("xp", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_monster_xp_passes_catalog_validation() =>
        Config_BuildCatalog(new MonsterConfig(0, "a", "b", 1, Xp: 0), new MonsterConfig(1, "c", "d", 1));

    private static void Config_BuildCatalog(params MonsterConfig[] monsters) => Server.Config.BuildMonsterCatalog(monsters);
}

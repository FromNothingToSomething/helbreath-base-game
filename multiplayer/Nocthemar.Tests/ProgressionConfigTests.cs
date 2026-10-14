using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T8: shipped Progression.json parses; every invalid curve is refused at startup.</summary>
public class ProgressionConfigTests {
    private const string Valid =
        """{"schemaVersion":1,"maxLevel":100,"xp":{"base":100,"exponent":1.6},"hp":{"base":1000,"perLevel":50},"mana":{"base":100,"perLevel":10,"regenPerSecond":2.0},"defaultMonsterXp":10}""";

    private static string ShippedPath => Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Progression.json");

    [Fact]
    public async Task Shipped_file_loads_with_expected_values() {
        var cfg = await ProgressionConfig.LoadAsync(ShippedPath);

        Assert.Equal(100, cfg.MaxLevel);
        Assert.Equal(1000, cfg.MaxHpAt(1)); // keeps the pre-NOCTHEMAR fixed 1000 HP at level 1
        Assert.Equal(5950, cfg.MaxHpAt(100));
        Assert.Equal(100, cfg.MaxManaAt(1));
        Assert.Equal(1090, cfg.MaxManaAt(100));
        Assert.Equal(2.0, cfg.ManaRegenPerSecond);
        Assert.Equal(10, cfg.DefaultMonsterXp);
    }

    [Fact]
    public async Task Xp_table_is_strictly_increasing_and_ends_at_max_level() {
        var cfg = await ProgressionConfig.LoadAsync(ShippedPath);

        Assert.True(cfg.TryGetXpToNext(1, out var first));
        Assert.Equal(100, first);
        var previous = 0L;
        for (var level = 1; level < cfg.MaxLevel; level++) {
            Assert.True(cfg.TryGetXpToNext(level, out var xp));
            Assert.True(xp > previous, $"level {level}");
            previous = xp;
        }

        Assert.False(cfg.TryGetXpToNext(cfg.MaxLevel, out _));
        Assert.False(cfg.TryGetXpToNext(0, out _));
    }

    [Fact]
    public void Levels_are_clamped() {
        var cfg = ProgressionConfig.Parse(Valid);

        Assert.Equal(1, cfg.ClampLevel(-3));
        Assert.Equal(100, cfg.ClampLevel(5000));
        Assert.Equal(cfg.MaxHpAt(100), cfg.MaxHpAt(5000));
    }

    [Theory]
    [InlineData("\"maxLevel\":100", "\"maxLevel\":0", "maxLevel")]
    [InlineData("\"maxLevel\":100", "\"maxLevel\":1001", "maxLevel")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":2", "schemaVersion")]
    [InlineData("\"exponent\":1.6", "\"exponent\":-1", "xp.exponent")]
    [InlineData("\"exponent\":1.6", "\"exponent\":0", "xp.exponent")]
    [InlineData("\"exponent\":1.6", "\"exponent\":9", "exceeds")]
    [InlineData("\"xp\":{\"base\":100", "\"xp\":{\"base\":0", "xp.base")]
    [InlineData("\"xp\":{\"base\":100,\"exponent\":1.6}", "\"xp\":{\"base\":1,\"exponent\":0.01}", "not strictly increasing")]
    [InlineData("\"perLevel\":50", "\"perLevel\":2000000000", "hp overflows")]
    [InlineData("\"defaultMonsterXp\":10", "\"defaultMonsterXp\":-5", "defaultMonsterXp")]
    [InlineData("\"hp\":{\"base\":1000,\"perLevel\":50},", "", "hp section missing")]
    [InlineData("\"defaultMonsterXp\":10", "\"defaultMonsterXp\":10,\"maxLevl\":3", "maxLevl")]
    public void Invalid_config_is_refused(string find, string replace, string expectedFragment) {
        var json = Valid.Replace(find, replace);
        Assert.NotEqual(Valid, json);

        var ex = Assert.Throws<InvalidOperationException>(() => ProgressionConfig.Parse(json));

        Assert.Contains(expectedFragment, ex.Message);
    }

    [Fact]
    public void Valid_template_parses() => Assert.Equal(100, ProgressionConfig.Parse(Valid).MaxLevel);
}

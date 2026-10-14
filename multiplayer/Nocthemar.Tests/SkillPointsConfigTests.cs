using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T40: skill points earned by level (1 per level from level 2); an older file without the section still loads.</summary>
public class SkillPointsConfigTests {
    private const string Base =
        """{"schemaVersion":1,"maxLevel":100,"xp":{"base":100,"exponent":1.6},"hp":{"base":1000,"perLevel":50},"mana":{"base":100,"perLevel":10,"regenPerSecond":2.0},"defaultMonsterXp":10""";

    private static string With(string section) => Base + (section.Length == 0 ? "" : "," + section) + "}";

    private static string ShippedPath => Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Progression.json");

    [Fact]
    public async Task Shipped_file_gives_one_point_per_level_from_level_2() {
        var cfg = await ProgressionConfig.LoadAsync(ShippedPath);

        Assert.Equal(2, cfg.SkillPointsFirstLevel);
        Assert.Equal(1, cfg.SkillPointsPerLevel);
        Assert.Equal(0, cfg.SkillPointsAt(1));
        Assert.Equal(1, cfg.SkillPointsAt(2));
        Assert.Equal(10, cfg.SkillPointsAt(11));
        Assert.Equal(29, cfg.SkillPointsAt(30));
        Assert.Equal(99, cfg.SkillPointsAt(100));
    }

    [Fact]
    public async Task Points_never_decrease_with_the_level() {
        var cfg = await ProgressionConfig.LoadAsync(ShippedPath);

        for (var level = 2; level <= cfg.MaxLevel; level++) {
            Assert.Equal(cfg.SkillPointsAt(level - 1) + cfg.SkillPointsPerLevel, cfg.SkillPointsAt(level));
        }
    }

    [Fact]
    public async Task Two_complete_branches_fit_in_the_points_of_level_30() {
        var cfg = await ProgressionConfig.LoadAsync(ShippedPath);

        // 2 branches x (7 skills at 1 point + 1 ultimate at 2 points) = 18.
        Assert.True(cfg.SkillPointsAt(30) >= 18);
    }

    [Fact]
    public void An_older_file_without_the_section_loads_with_the_defaults() {
        var cfg = ProgressionConfig.Parse(With(""));

        Assert.Equal(ProgressionConfig.DefaultSkillPointsFirstLevel, cfg.SkillPointsFirstLevel);
        Assert.Equal(ProgressionConfig.DefaultSkillPointsPerLevel, cfg.SkillPointsPerLevel);
        Assert.Equal(29, cfg.SkillPointsAt(30));
    }

    [Fact]
    public void A_custom_section_is_used() {
        var cfg = ProgressionConfig.Parse(With("\"skillPoints\":{\"firstLevel\":5,\"perLevel\":3}"));

        Assert.Equal(0, cfg.SkillPointsAt(4));
        Assert.Equal(3, cfg.SkillPointsAt(5));
        Assert.Equal(3 * 96, cfg.SkillPointsAt(100));
    }

    [Fact]
    public void Zero_points_per_level_is_valid_and_gives_none() {
        var cfg = ProgressionConfig.Parse(With("\"skillPoints\":{\"firstLevel\":2,\"perLevel\":0}"));

        Assert.Equal(0, cfg.SkillPointsAt(100));
    }

    [Theory]
    [InlineData("\"skillPoints\":{\"firstLevel\":0,\"perLevel\":1}", "skillPoints.firstLevel")]
    [InlineData("\"skillPoints\":{\"firstLevel\":101,\"perLevel\":1}", "skillPoints.firstLevel")]
    [InlineData("\"skillPoints\":{\"firstLevel\":2,\"perLevel\":-1}", "skillPoints.perLevel")]
    [InlineData("\"skillPoints\":{\"firstLevel\":2,\"perLevel\":11}", "skillPoints.perLevel")]
    [InlineData("\"skillPoints\":{\"firstLevel\":2,\"perLevel\":1,\"extra\":1}", "extra")]
    public void Invalid_sections_are_refused(string section, string expectedMessage) {
        var ex = Assert.Throws<InvalidOperationException>(() => ProgressionConfig.Parse(With(section)));

        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public void Levels_are_clamped_like_the_other_tables() {
        var cfg = ProgressionConfig.Parse(With(""));

        Assert.Equal(0, cfg.SkillPointsAt(-5));
        Assert.Equal(cfg.SkillPointsAt(100), cfg.SkillPointsAt(10_000));
    }
}

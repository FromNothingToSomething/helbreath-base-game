using Server.Nocthemar.Data;

namespace NocthemarTests;

internal static class TestSupport {
    // Mirrors the shipped Config/Nocthemar/Progression.json values asserted in ProgressionConfigTests.
    public const string ProgressionJson =
        """{"schemaVersion":1,"maxLevel":100,"xp":{"base":100,"exponent":1.6},"hp":{"base":1000,"perLevel":50},"mana":{"base":100,"perLevel":10,"regenPerSecond":2.0},"defaultMonsterXp":10}""";

    public static ProgressionConfig Progression() => ProgressionConfig.Parse(ProgressionJson);
}

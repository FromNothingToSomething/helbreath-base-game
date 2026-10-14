using Mmorpg.Network;
using Server;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.World.Game;

namespace NocthemarTests;

internal static class TestSupport {
    // Mirrors the shipped Config/Nocthemar/Progression.json values asserted in ProgressionConfigTests.
    public const string ProgressionJson =
        """{"schemaVersion":1,"maxLevel":100,"xp":{"base":100,"exponent":1.6},"hp":{"base":1000,"perLevel":50},"mana":{"base":100,"perLevel":10,"regenPerSecond":2.0},"defaultMonsterXp":10}""";

    public static ProgressionConfig Progression() => ProgressionConfig.Parse(ProgressionJson);

    /// <summary>A player outside any world, spawned with the level-1 defaults; messages sent to it are appended to <paramref name="sent"/> when given.</summary>
    public static GameWorldPlayer NewPlayer(List<ServerMessage>? sent = null) {
        Action<ServerMessage> send = sent is null ? Discard : sent.Add;
        var player = new GameWorldPlayer(
            Guid.NewGuid(),
            send,
            _ => { },
            _ => { },
            () => { },
            new Dictionary<int, ItemConfig>(),
            new MovementSpeedViolationCheckConfig(false, 5, 20, 20, 10, 50),
            20,
            0.05,
            Progression());
        player.SetInitialState(5, 6);
        return player;
    }

    private static void Discard(ServerMessage message) {
    }

    /// <summary>A player restored from a save carrying the given level, XP and mana.</summary>
    public static GameWorldPlayer NewPlayer(int level, long xp, int mana, List<ServerMessage>? sent = null) {
        var player = NewPlayer(sent);
        player.ApplyPersistedState(new PlayerPersistenceState(
            "aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false,
            CharacterName: "T", SchemaVersion: CharacterSchema.Current, Noc: new NocState(NocState.CurrentV, level, xp, mana)));
        return player;
    }
}

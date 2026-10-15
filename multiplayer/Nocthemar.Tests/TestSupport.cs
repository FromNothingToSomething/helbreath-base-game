using Mmorpg.Network;
using Server;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Skills;
using Server.World.Game;

namespace NocthemarTests;

internal static class TestSupport {
    // Mirrors the shipped Config/Nocthemar/Progression.json values asserted in ProgressionConfigTests.
    public const string ProgressionJson =
        """{"schemaVersion":1,"maxLevel":100,"xp":{"base":100,"exponent":1.6},"hp":{"base":1000,"perLevel":50},"mana":{"base":100,"perLevel":10,"regenPerSecond":2.0},"defaultMonsterXp":10}""";

    /// <summary>The shipped Config/Nocthemar/Classes.json (linked into the test output).</summary>
    public static ClassConfig ShippedClasses() => ClassConfig.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Classes.json")).GetAwaiter().GetResult();

    /// <summary>The shipped Config/Nocthemar/Skills.json validated against <see cref="ShippedClasses"/>.</summary>
    public static SkillConfig ShippedSkills() => SkillConfig.LoadAsync(ShippedClasses(), Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Skills.json")).GetAwaiter().GetResult();

    /// <summary>The shipped Config/Nocthemar/Mimic.json validated against <see cref="ShippedClasses"/>.</summary>
    public static MimicConfig ShippedMimic() => MimicConfig.LoadAsync(ShippedClasses(), Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Mimic.json")).GetAwaiter().GetResult();

    /// <summary>The shipped Config/Nocthemar/Factions.json (linked into the test output).</summary>
    public static FactionConfig ShippedFactions() => FactionConfig.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Factions.json")).GetAwaiter().GetResult();

    /// <summary>The shipped classes and skills with the given handlers (none by default: every shipped skill is a catalog entry or a stat passive).</summary>
    public static NocContent ShippedContent(SkillHandlerRegistry? handlers = null) =>
        new(ShippedClasses(), ShippedSkills(), Progression(), handlers ?? BuiltinSkillHandlers.Register(new SkillHandlerRegistry()), ShippedMimic(), BuiltinMimicHandlers.Register(new MimicHandlerRegistry()));

    /// <summary>Loads a NOCTHEMAR block into a player the way a login does.</summary>
    public static void Restore(GameWorldPlayer player, NocState noc) {
        player.ApplyPersistedState(new PlayerPersistenceState(
            "aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false,
            CharacterName: "T", SchemaVersion: CharacterSchema.Current, Noc: noc));
    }

    public static ProgressionConfig Progression() => ProgressionConfig.Parse(ProgressionJson);

    /// <summary>One race without any modifier: players built with it behave exactly as before races existed (the default of <see cref="NewPlayer(List{ServerMessage}?, RaceConfig?)"/>).</summary>
    public static RaceConfig NeutralRaces() => RaceConfig.Parse(
        """{"schemaVersion":1,"defaultRaceId":0,"races":[{"id":0,"key":"neutral","name":"Neutral","modifiers":[],"traits":[],"appearance":{"genders":[0,1],"skinColors":[0,1,2],"hairStyles":[0,1,2,3,4,5,6,7],"underwearColors":[0,1,2,3,4,5,6,7]},"art":{"native":true}}]}""");

    /// <summary>The shipped Config/Nocthemar/Races.json (linked into the test output).</summary>
    public static RaceConfig ShippedRaces() => RaceConfig.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Races.json")));

    /// <summary>A player outside any world, spawned with the level-1 defaults; messages sent to it are appended to <paramref name="sent"/> when given.</summary>
    /// <summary>The essence items of the shipped mimicry catalog, the Essence Vial and nothing else: a bag catalog for players that carry essences.</summary>
    public static IReadOnlyDictionary<int, ItemConfig> EssenceItems() {
        var items = new Dictionary<int, ItemConfig>();
        foreach (var ability in ShippedMimic().Abilities.Where(a => a.EssenceItemId is not null)) {
            items[ability.EssenceItemId!.Value] = new ItemConfig(ability.EssenceItemId.Value, "Essence: " + ability.Name, "misc", Stackable: true);
        }

        items[MimicIds.EssenceVialItemId] = new ItemConfig(MimicIds.EssenceVialItemId, "Fiole d'Essence", "misc", Stackable: true);
        return items;
    }

    public static GameWorldPlayer NewPlayer(List<ServerMessage>? sent = null, RaceConfig? races = null, NocContent? content = null, IReadOnlyDictionary<int, ItemConfig>? items = null) {
        Action<ServerMessage> send = sent is null ? Discard : sent.Add;
        var player = new GameWorldPlayer(
            Guid.NewGuid(),
            send,
            _ => { },
            _ => { },
            () => { },
            items ?? new Dictionary<int, ItemConfig>(),
            new MovementSpeedViolationCheckConfig(false, 5, 20, 20, 10, 50),
            20,
            0.05,
            Progression(),
            races ?? NeutralRaces(),
            content);
        player.SetInitialState(5, 6);
        return player;
    }

    private static void Discard(ServerMessage message) {
    }

    /// <summary>A player restored from a save carrying the given level, XP and mana.</summary>
    public static GameWorldPlayer NewPlayer(int level, long xp, int mana, List<ServerMessage>? sent = null, RaceConfig? races = null, NocContent? content = null) {
        var player = NewPlayer(sent, races, content);
        player.ApplyPersistedState(new PlayerPersistenceState(
            "aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false,
            CharacterName: "T", SchemaVersion: CharacterSchema.Current, Noc: new NocState(NocState.CurrentV, level, xp, mana)));
        return player;
    }
}

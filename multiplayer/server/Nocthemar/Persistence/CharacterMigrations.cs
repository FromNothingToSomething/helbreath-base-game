using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.World.Game;

namespace Server.Nocthemar.Persistence;

/// <summary>One step of the save upgrade chain: <see cref="From"/> -> <see cref="To"/> (always <c>From + 1</c>).</summary>
public interface ICharacterMigration {
    int From { get; }
    int To { get; }

    /// <summary>Returns the upgraded state; the migrator stamps <see cref="PlayerPersistenceState.SchemaVersion"/> afterwards.</summary>
    PlayerPersistenceState Apply(PlayerPersistenceState state);
}

/// <summary>Raised when a save was written by a newer server than this one; the file must be left untouched.</summary>
public sealed class CharacterSchemaTooNewException : Exception {
    public CharacterSchemaTooNewException(int found, int supported)
        : base($"Character save schema version {found} is newer than the supported version {supported}.") {
        Found = found;
        Supported = supported;
    }

    public int Found { get; }
    public int Supported { get; }
}

/// <summary>0 -> 1: adds the NOCTHEMAR block (level 1, XP 0, mana at maximum) when absent.</summary>
public sealed class Migration0To1 : ICharacterMigration {
    private readonly ProgressionConfig progression;

    public Migration0To1(ProgressionConfig progression) {
        this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
    }

    public int From => CharacterSchema.Legacy;
    public int To => 1;

    public PlayerPersistenceState Apply(PlayerPersistenceState state) =>
        state with { Noc = state.Noc ?? NocState.CreateDefault(progression.MaxManaAt(1)) };
}

/// <summary>
/// 1 -> 2: a character with a NOCTHEMAR block but no race becomes <see cref="NocState.LegacyRaceId"/>.
/// A null block stays null (defaults, including the default race, are created at login). An existing RaceId is never touched, known or not:
/// whether it is listed in Races.json is decided at load time (fallback), not by the schema.
/// </summary>
public sealed class Migration1To2 : ICharacterMigration {
    public int From => 1;
    public int To => 2;

    public PlayerPersistenceState Apply(PlayerPersistenceState state) =>
        state.Noc is null
            ? state
            : state with { Noc = state.Noc with { RaceId = state.Noc.RaceId ?? NocState.LegacyRaceId } };
}

/// <summary>
/// 2 -> 3: the NOCTHEMAR block gets layout <see cref="NocState.CurrentV"/> (class, skills and kills are new nullable/zero fields, so no value changes: nobody has a class yet).
/// A null block stays null. Characters keep their race, level, XP and mana untouched.
/// </summary>
public sealed class Migration2To3 : ICharacterMigration {
    public int From => 2;
    public int To => 3;

    public PlayerPersistenceState Apply(PlayerPersistenceState state) =>
        state.Noc is null
            ? state
            : state with { Noc = state.Noc with { V = Math.Max(state.Noc.V, NocState.CurrentV) } };
}

/// <summary>Applies the ordered migrations needed to bring a loaded save to <see cref="CurrentVersion"/>.</summary>
public sealed class CharacterMigrator {
    private readonly ICharacterMigration[] chain;

    /// <summary>Migrations must form one contiguous chain starting at version 0 and ending at <paramref name="currentVersion"/>.</summary>
    public CharacterMigrator(IEnumerable<ICharacterMigration> migrations, int currentVersion = CharacterSchema.Current) {
        ArgumentNullException.ThrowIfNull(migrations);
        if (currentVersion < 0) {
            throw new ArgumentOutOfRangeException(nameof(currentVersion));
        }

        var ordered = migrations.OrderBy(m => m.From).ToArray();
        for (var i = 0; i < ordered.Length; i++) {
            if (ordered[i].From != i || ordered[i].To != i + 1) {
                throw new ArgumentException(
                    $"Migration chain must be contiguous from 0 with To == From + 1; offending step index {i} is {ordered[i].From}->{ordered[i].To}.",
                    nameof(migrations));
            }
        }

        if (ordered.Length != currentVersion) {
            throw new ArgumentException(
                $"Migration chain has {ordered.Length} step(s) but current version is {currentVersion}.", nameof(migrations));
        }

        chain = ordered;
        CurrentVersion = currentVersion;
    }

    public int CurrentVersion { get; }

    /// <summary>Chain for the shipped schema (<see cref="CharacterSchema.Current"/>).</summary>
    public static CharacterMigrator CreateDefault(ProgressionConfig progression) =>
        new([new Migration0To1(progression), new Migration1To2(), new Migration2To3()], CharacterSchema.Current);

    /// <summary>
    /// Upgrades <paramref name="state"/> to <see cref="CurrentVersion"/>.
    /// Throws <see cref="CharacterSchemaTooNewException"/> for a newer version and <see cref="InvalidDataException"/> for a negative one.
    /// </summary>
    public PlayerPersistenceState Migrate(PlayerPersistenceState state) {
        ArgumentNullException.ThrowIfNull(state);
        var version = state.SchemaVersion;
        if (version < 0) {
            throw new InvalidDataException($"Character save schema version {version} is invalid.");
        }

        if (version > CurrentVersion) {
            throw new CharacterSchemaTooNewException(version, CurrentVersion);
        }

        while (version < CurrentVersion) {
            var step = chain[version];
            state = step.Apply(state) with { SchemaVersion = step.To };
            version = step.To;
        }

        return state;
    }
}

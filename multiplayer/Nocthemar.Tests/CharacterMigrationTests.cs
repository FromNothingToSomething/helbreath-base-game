using Server.Nocthemar.Core;
using Server.Nocthemar.Persistence;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T4: versioned migration chain.</summary>
public class CharacterMigrationTests {
    private static PlayerPersistenceState Sample(int schemaVersion, NocState? noc = null) =>
        new("aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false, CharacterName: "T", SchemaVersion: schemaVersion, Noc: noc);

    private sealed class StepMigration(int from, Func<PlayerPersistenceState, PlayerPersistenceState> apply) : ICharacterMigration {
        public int From => from;
        public int To => from + 1;
        public PlayerPersistenceState Apply(PlayerPersistenceState state) => apply(state);
    }

    private static CharacterMigrator Default() => CharacterMigrator.CreateDefault(TestSupport.Progression());

    [Fact]
    public void Legacy_save_gets_default_noc_with_full_mana() {
        var migrated = Default().Migrate(Sample(CharacterSchema.Legacy));

        Assert.Equal(CharacterSchema.Current, migrated.SchemaVersion);
        Assert.NotNull(migrated.Noc);
        Assert.Equal(1, migrated.Noc!.Level);
        Assert.Equal(0L, migrated.Noc.Xp);
        Assert.Equal(100, migrated.Noc.Mana); // MaxManaAt(1) in the test progression
    }

    [Fact]
    public void Migration_keeps_every_other_field() {
        var original = Sample(CharacterSchema.Legacy);

        var migrated = Default().Migrate(original);

        Assert.Equal(original, migrated with { SchemaVersion = CharacterSchema.Legacy, Noc = null });
    }

    [Fact]
    public void Existing_noc_block_is_preserved() {
        var noc = new NocState(NocState.CurrentV, 12, 345, 7);

        var migrated = Default().Migrate(Sample(CharacterSchema.Legacy, noc));

        Assert.Equal(noc, migrated.Noc);
        Assert.Equal(CharacterSchema.Current, migrated.SchemaVersion);
    }

    [Fact]
    public void Current_version_without_noc_is_left_alone() {
        var state = Sample(CharacterSchema.Current);

        var migrated = Default().Migrate(state);

        Assert.Equal(state, migrated);
        Assert.Null(migrated.Noc);
    }

    [Theory]
    [InlineData(CharacterSchema.Current + 1)]
    [InlineData(99)]
    [InlineData(int.MaxValue)]
    public void Newer_version_is_refused(int version) {
        var ex = Assert.Throws<CharacterSchemaTooNewException>(() => Default().Migrate(Sample(version)));

        Assert.Equal(version, ex.Found);
        Assert.Equal(CharacterSchema.Current, ex.Supported);
    }

    [Fact]
    public void Negative_version_is_invalid_data() =>
        Assert.Throws<InvalidDataException>(() => Default().Migrate(Sample(-1)));

    [Fact]
    public void Steps_run_in_version_order_regardless_of_registration_order() {
        var step01 = new StepMigration(0, s => s with { Noc = new NocState(Level: 10) });
        var step12 = new StepMigration(1, s => s with { Noc = s.Noc! with { Level = s.Noc.Level + 1 } });
        var migrator = new CharacterMigrator([step12, step01], currentVersion: 2);

        var migrated = migrator.Migrate(Sample(0));

        Assert.Equal(2, migrated.SchemaVersion);
        Assert.Equal(11, migrated.Noc!.Level);
    }

    [Fact]
    public void Partial_chain_starts_at_the_saved_version() {
        var step01 = new StepMigration(0, s => throw new InvalidOperationException("must not run"));
        var step12 = new StepMigration(1, s => s with { Noc = new NocState(Level: 5) });
        var migrator = new CharacterMigrator([step01, step12], currentVersion: 2);

        var migrated = migrator.Migrate(Sample(1));

        Assert.Equal(2, migrated.SchemaVersion);
        Assert.Equal(5, migrated.Noc!.Level);
    }

    [Fact]
    public void Chain_with_a_gap_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new CharacterMigrator(
            [new StepMigration(0, s => s), new StepMigration(2, s => s)], currentVersion: 3));

    [Fact]
    public void Chain_with_duplicate_step_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new CharacterMigrator(
            [new StepMigration(0, s => s), new StepMigration(0, s => s)], currentVersion: 2));

    [Fact]
    public void Chain_not_reaching_current_version_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new CharacterMigrator([new StepMigration(0, s => s)], currentVersion: 2));
}

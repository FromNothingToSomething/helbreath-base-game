using System.Text.Json;
using Server;
using Server.Nocthemar.Core;
using Server.Nocthemar.Persistence;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T41: NocState layout 2 (class, skills, kills) and the 2 -> 3 migration; the layout and schema numbers follow the later modules (T58).</summary>
public class SkillStateSchemaTests {
    private static PlayerPersistenceState Sample(int schemaVersion, NocState? noc) =>
        new("aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false, CharacterName: "T", SchemaVersion: schemaVersion, Noc: noc);

    private static CharacterMigrator Default() => CharacterMigrator.CreateDefault(TestSupport.Progression());

    [Fact]
    public void Layout_and_schema_numbers() {
        Assert.Equal(4, NocState.CurrentV);
        Assert.Equal(5, CharacterSchema.Current);
        Assert.Equal(2, CharacterSchema.V2);
        Assert.Equal(3, CharacterSchema.V3);
        Assert.Equal(5, Default().CurrentVersion);
    }

    [Fact]
    public void A_fresh_state_has_no_class_no_skills_no_kills() {
        var state = NocState.CreateDefault(100);

        Assert.Null(state.ClassId);
        Assert.Null(state.Skills);
        Assert.Equal(0, state.Kills);
    }

    [Fact]
    public void A_layout_1_json_reads_as_no_skills_and_no_kills() {
        var state = JsonSerializer.Deserialize<NocState>("""{"V":1,"Level":9,"Xp":3,"Mana":4,"RaceId":2}""")!;

        Assert.Equal(1, state.V);
        Assert.Null(state.ClassId);
        Assert.Null(state.Skills);
        Assert.Equal(0, state.Kills);
    }

    [Fact]
    public void Class_skills_and_kills_round_trip_through_json() {
        var original = new NocState(NocState.CurrentV, 12, 5, 40, RaceId: 3, ClassId: 2, Skills: [1020, 1021], Kills: 77);

        var back = JsonSerializer.Deserialize<NocState>(JsonSerializer.Serialize(original))!;

        Assert.Equal(2, back.ClassId);
        Assert.True(new[] { 1020, 1021 }.SequenceEqual(back.Skills!));
        Assert.Equal(77, back.Kills);
        Assert.Equal(3, back.RaceId);
    }

    [Fact]
    public void Migration_2_to_3_raises_the_block_layout_and_changes_nothing_else() {
        var old = new NocState(1, 12, 345, 7, RaceId: 4);

        var migrated = new Migration2To3().Apply(Sample(2, old));

        Assert.Equal(old with { V = 3 }, migrated.Noc);
        Assert.Null(migrated.Noc!.Skills);
        Assert.Null(migrated.Noc.ClassId);
    }

    [Fact]
    public void Migration_2_to_3_never_lowers_the_layout_and_keeps_a_null_block() {
        Assert.Equal(7, new Migration2To3().Apply(Sample(2, new NocState(7, 1, 0, 0))).Noc!.V);
        Assert.Null(new Migration2To3().Apply(Sample(2, null)).Noc);
    }

    [Fact]
    public void A_schema_2_save_reaches_the_current_schema_with_its_progress() {
        var saved = new NocState(1, 20, 99, 55, RaceId: 5);

        var migrated = Default().Migrate(Sample(2, saved));

        Assert.Equal(CharacterSchema.Current, migrated.SchemaVersion);
        Assert.Equal(saved with { V = NocState.CurrentV }, migrated.Noc);
    }

    [Fact]
    public void A_legacy_save_goes_through_every_step() {
        var migrated = Default().Migrate(Sample(CharacterSchema.Legacy, null));

        Assert.Equal(CharacterSchema.Current, migrated.SchemaVersion);
        Assert.Equal(NocState.CurrentV, migrated.Noc!.V);
        Assert.Equal(NocState.LegacyRaceId, migrated.Noc.RaceId);
    }

    [Fact]
    public void A_current_schema_save_is_left_alone_and_a_newer_one_is_refused() {
        var state = Sample(CharacterSchema.Current, new NocState(NocState.CurrentV, 3, 0, 5, RaceId: 1, ClassId: 0, Skills: [1000]));

        Assert.Equal(state.Noc, Default().Migrate(state).Noc);
        Assert.Throws<CharacterSchemaTooNewException>(() => Default().Migrate(Sample(6, null)));
    }

    [Fact]
    public void The_persisted_state_of_a_new_player_is_the_current_schema_without_class_or_skills() {
        var player = TestSupport.NewPlayer();

        var state = player.CreatePersistenceState("aresden");

        Assert.Equal(CharacterSchema.Current, state.SchemaVersion);
        Assert.Equal(NocState.CurrentV, state.Noc!.V);
        Assert.Null(state.Noc.ClassId);
        Assert.Null(state.Noc.Skills);
        Assert.Equal(0, state.Noc.Kills);
    }
}

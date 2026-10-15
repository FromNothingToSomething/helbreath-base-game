using System.Text.Json;
using Server;
using Server.Nocthemar.Core;
using Server.Nocthemar.Persistence;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T58: NocState layout 3 (mimicry abilities and equipment), schema 4 and the 3 -> 4 migration.</summary>
public class MimicStateSchemaTests {
    private static PlayerPersistenceState Sample(int schemaVersion, NocState? noc) =>
        new("aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false, CharacterName: "T", SchemaVersion: schemaVersion, Noc: noc);

    private static CharacterMigrator Default() => CharacterMigrator.CreateDefault(TestSupport.Progression());

    [Fact]
    public void A_fresh_state_has_no_mimicry() {
        var state = NocState.CreateDefault(100);

        Assert.Equal(NocState.CurrentV, state.V);
        Assert.Null(state.Mimics);
        Assert.Null(state.MimicEquipped);
    }

    [Fact]
    public void A_layout_2_json_reads_as_no_mimicry() {
        var state = JsonSerializer.Deserialize<NocState>("""{"V":2,"Level":9,"Xp":3,"Mana":4,"RaceId":2,"ClassId":1,"Skills":[1020],"Kills":5}""")!;

        Assert.Equal(2, state.V);
        Assert.Null(state.Mimics);
        Assert.Null(state.MimicEquipped);
        Assert.Equal(5, state.Kills);
    }

    [Fact]
    public void Mimics_and_equipment_round_trip_through_json() {
        var original = new NocState(NocState.CurrentV, 12, 5, 40, RaceId: 3, ClassId: 2, Skills: [1020], Kills: 7,
            Mimics: [new MimicEntry(2000, 12), new MimicEntry(2003)], MimicEquipped: [2003, 2000]);

        var back = JsonSerializer.Deserialize<NocState>(JsonSerializer.Serialize(original))!;

        Assert.True(original.Mimics!.SequenceEqual(back.Mimics!));
        Assert.Equal(12, back.Mimics![0].Xp);
        Assert.Equal(0, back.Mimics[1].Xp);
        Assert.True(new[] { 2003, 2000 }.SequenceEqual(back.MimicEquipped!));
        Assert.Equal(1020, back.Skills![0]);
    }

    [Fact]
    public void An_entry_without_xp_reads_as_zero() {
        var back = JsonSerializer.Deserialize<NocState>("""{"V":3,"Mimics":[{"Id":2001}]}""")!;

        Assert.Equal(new MimicEntry(2001, 0), back.Mimics![0]);
    }

    [Fact]
    public void Migration_3_to_4_raises_the_block_layout_and_changes_nothing_else() {
        var old = new NocState(2, 12, 345, 7, RaceId: 4, ClassId: 1, Skills: [1020], Kills: 3);

        var migrated = new Migration3To4().Apply(Sample(3, old));

        Assert.Equal(old with { V = 3 }, migrated.Noc);
        Assert.Null(migrated.Noc!.Mimics);
        Assert.Null(migrated.Noc.MimicEquipped);
    }

    [Fact]
    public void Migration_3_to_4_is_idempotent_never_lowers_the_layout_and_keeps_a_null_block() {
        var once = new Migration3To4().Apply(Sample(3, new NocState(2, 1, 0, 0)));
        var twice = new Migration3To4().Apply(once);

        Assert.Equal(once.Noc, twice.Noc);
        Assert.Equal(9, new Migration3To4().Apply(Sample(3, new NocState(9, 1, 0, 0))).Noc!.V);
        Assert.Null(new Migration3To4().Apply(Sample(3, null)).Noc);
    }

    [Fact]
    public void Migration_3_to_4_keeps_mimicry_already_present() {
        var noc = new NocState(3, 1, 0, 0, Mimics: [new MimicEntry(2000, 4)], MimicEquipped: [2000]);

        var migrated = new Migration3To4().Apply(Sample(3, noc));

        Assert.Equal(noc, migrated.Noc);
    }

    [Fact]
    public void A_schema_3_save_reaches_the_current_schema_with_its_progress() {
        var saved = new NocState(3, 20, 99, 55, RaceId: 5, ClassId: 2, Skills: [1020, 1021], Kills: 40);

        var migrated = Default().Migrate(Sample(3, saved));

        Assert.Equal(CharacterSchema.Current, migrated.SchemaVersion);
        Assert.Equal(saved with { V = 4 }, migrated.Noc);
    }

    [Fact]
    public void A_schema_2_save_goes_through_both_new_steps() {
        var migrated = Default().Migrate(Sample(2, new NocState(1, 20, 99, 55, RaceId: 5)));

        Assert.Equal(CharacterSchema.Current, migrated.SchemaVersion);
        Assert.Equal(NocState.CurrentV, migrated.Noc!.V);
        Assert.Null(migrated.Noc.Mimics);
    }

    [Fact]
    public void The_chain_has_five_steps_and_refuses_schema_6() {
        Assert.Equal(5, CharacterSchema.Current);
        Assert.Equal(5, Default().CurrentVersion);
        Assert.Throws<CharacterSchemaTooNewException>(() => Default().Migrate(Sample(6, null)));
    }

    [Fact]
    public void The_persisted_state_of_a_new_player_has_no_mimicry() {
        var state = TestSupport.NewPlayer().CreatePersistenceState("aresden");

        Assert.Equal(CharacterSchema.Current, state.SchemaVersion);
        Assert.Null(state.Noc!.Mimics);
        Assert.Null(state.Noc.MimicEquipped);
    }
}

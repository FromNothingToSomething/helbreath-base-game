using System.Text.Json;
using Server;
using Server.Nocthemar.Core;
using Server.Nocthemar.Persistence;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T75: NocState layout 4 (faction and reputations), schema 5, the 4 -> 5 migration and the clean-up of a loaded block.</summary>
public class FactionStateSchemaTests {
    private static PlayerPersistenceState Sample(int schemaVersion, NocState? noc) =>
        new("aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false, CharacterName: "T", SchemaVersion: schemaVersion, Noc: noc);

    private static CharacterMigrator Default() => CharacterMigrator.CreateDefault(TestSupport.Progression());

    private static GameWorldPlayer Player() => TestSupport.NewPlayer(null, null, TestSupport.ShippedContent());

    [Fact]
    public void A_fresh_state_has_no_faction_and_no_reputation() {
        var state = NocState.CreateDefault(100);

        Assert.Equal(4, NocState.CurrentV);
        Assert.Equal(NocState.CurrentV, state.V);
        Assert.Null(state.FactionId);
        Assert.Null(state.Reputations);
    }

    [Fact]
    public void A_layout_3_json_reads_as_no_faction() {
        var state = JsonSerializer.Deserialize<NocState>("""{"V":3,"Level":9,"Xp":3,"Mana":4,"RaceId":2,"Mimics":[{"Id":2001,"Xp":2}],"Kills":5}""")!;

        Assert.Equal(3, state.V);
        Assert.Null(state.FactionId);
        Assert.Null(state.Reputations);
        Assert.Equal(5, state.Kills);
        Assert.Equal(2001, state.Mimics![0].Id);
    }

    [Fact]
    public void Faction_and_reputations_round_trip_through_json() {
        var original = new NocState(NocState.CurrentV, 12, 5, 40, RaceId: 3, ClassId: 2, FactionId: 1, Kills: 7,
            Reputations: [new FactionRep(0, 250), new FactionRep(1, -30)]);

        var back = JsonSerializer.Deserialize<NocState>(JsonSerializer.Serialize(original))!;

        Assert.Equal(1, back.FactionId);
        Assert.Equal(new[] { new FactionRep(0, 250), new FactionRep(1, -30) }, back.Reputations);
        Assert.Equal(7, back.Kills);
    }

    [Fact]
    public void Migration_4_to_5_raises_the_block_layout_and_changes_nothing_else() {
        var old = new NocState(3, 12, 345, 7, RaceId: 4, ClassId: 1, Skills: [1020], Kills: 3, Mimics: [new MimicEntry(2000, 4)], MimicEquipped: [2000]);

        var migrated = new Migration4To5().Apply(Sample(4, old));

        Assert.Equal(old with { V = 4 }, migrated.Noc);
        Assert.Null(migrated.Noc!.FactionId);
        Assert.Null(migrated.Noc.Reputations);
    }

    [Fact]
    public void Migration_4_to_5_is_idempotent_never_lowers_the_layout_and_keeps_a_null_block() {
        var once = new Migration4To5().Apply(Sample(4, new NocState(3, 1, 0, 0)));
        var twice = new Migration4To5().Apply(once);

        Assert.Equal(once.Noc, twice.Noc);
        Assert.Equal(9, new Migration4To5().Apply(Sample(4, new NocState(9, 1, 0, 0))).Noc!.V);
        Assert.Null(new Migration4To5().Apply(Sample(4, null)).Noc);
    }

    [Fact]
    public void Migration_4_to_5_keeps_a_faction_already_present() {
        var noc = new NocState(4, 1, 0, 0, FactionId: 2, Reputations: [new FactionRep(2, 40)]);

        var migrated = new Migration4To5().Apply(Sample(4, noc));

        Assert.Equal(noc, migrated.Noc);
    }

    [Fact]
    public void A_schema_4_save_reaches_schema_5_with_its_progress() {
        var saved = new NocState(4, 20, 99, 55, RaceId: 5, ClassId: 2, Skills: [1020], Kills: 40, Mimics: [new MimicEntry(2003, 9)], MimicEquipped: [2003]);

        var migrated = Default().Migrate(Sample(4, saved));

        Assert.Equal(5, migrated.SchemaVersion);
        Assert.Equal(saved, migrated.Noc);
    }

    [Fact]
    public void The_chain_ends_at_schema_5() {
        Assert.Equal(5, CharacterSchema.Current);
        Assert.Equal(CharacterSchema.Current, Default().CurrentVersion);
        Assert.Equal(CharacterSchema.Current, Default().Migrate(Sample(0, null)).SchemaVersion);
    }

    [Fact]
    public void The_persisted_state_of_a_new_player_has_no_faction() {
        var state = Player().CreatePersistenceState("aresden");

        Assert.Equal(CharacterSchema.Current, state.SchemaVersion);
        Assert.Null(state.Noc!.FactionId);
        Assert.Null(state.Noc.Reputations);
    }

    [Fact]
    public void A_loaded_block_is_cleaned_without_refusing_the_login() {
        var player = Player();

        TestSupport.Restore(player, new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: 9,
            Reputations: [new FactionRep(7, 5), new FactionRep(1, 99999), new FactionRep(0, -50)]));
        var noc = player.CreatePersistenceState("aresden").Noc!;

        Assert.Null(noc.FactionId);
        Assert.Equal(new[] { new FactionRep(0, -50), new FactionRep(1, 10000) }, noc.Reputations);
    }

    [Fact]
    public void A_valid_faction_and_reputations_survive_a_save_and_a_load() {
        var first = Player();
        TestSupport.Restore(first, new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: 3,
            Reputations: [new FactionRep(2, 120), new FactionRep(3, 4000)]));

        var second = Player();
        second.ApplyPersistedState(first.CreatePersistenceState("aresden"));
        var noc = second.CreatePersistenceState("aresden").Noc!;

        Assert.Equal(3, noc.FactionId);
        Assert.Equal(new[] { new FactionRep(2, 120), new FactionRep(3, 4000) }, noc.Reputations);
    }

    [Fact]
    public void Without_a_faction_catalog_the_block_is_left_as_it_is() {
        var player = TestSupport.NewPlayer();

        TestSupport.Restore(player, new NocState(NocState.CurrentV, 20, 0, 0, RaceId: 0, FactionId: 9, Reputations: [new FactionRep(7, 5)]));
        var noc = player.CreatePersistenceState("aresden").Noc!;

        Assert.Equal(9, noc.FactionId);
        Assert.Equal(new[] { new FactionRep(7, 5) }, noc.Reputations);
    }
}

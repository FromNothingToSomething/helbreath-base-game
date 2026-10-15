using System.Text.Json;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Persistence;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>
/// T28: schema 2 and races. Two different situations, kept apart on purpose:
/// a save written by a NEWER server is refused (CharacterSchemaTooNewException); a RaceId missing from Races.json is NOT refused (fallback + warning).
/// </summary>
public sealed class RaceMigrationTests : IDisposable {
    private static readonly RaceConfig Races = TestSupport.ShippedRaces();
    private readonly string root = Path.Combine(Path.GetTempPath(), "noc-race-" + Guid.NewGuid().ToString("N"));
    private readonly string chars;
    private readonly List<string> logs = new();
    private readonly JsonCharacterStore store;

    public RaceMigrationTests() {
        Directory.CreateDirectory(root);
        chars = Path.Combine(root, "Chars");
        store = new JsonCharacterStore(chars, CharacterMigrator.CreateDefault(TestSupport.Progression()), logs.Add);
    }

    public void Dispose() {
        try {
            Directory.Delete(root, recursive: true);
        } catch (IOException) {
        }
    }

    private static PlayerPersistenceState Sample(int schemaVersion, NocState? noc) =>
        new("aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false, CharacterName: "T", SchemaVersion: schemaVersion, Noc: noc);

    private static NocState WithRace(int? raceId) => new(NocState.CurrentV, 4, 30, 12, RaceId: raceId);

    private string SavePath(string id) => Path.Combine(chars, id + ".json");

    // --- Migration1To2 -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_default_chain_ends_at_the_current_schema() {
        Assert.Equal(5, CharacterSchema.Current);
        Assert.Equal(CharacterSchema.Current, CharacterMigrator.CreateDefault(TestSupport.Progression()).CurrentVersion);
    }

    [Fact]
    public void A_block_without_a_race_gets_the_legacy_race() {
        var migrated = new Migration1To2().Apply(Sample(1, WithRace(null)));

        Assert.Equal(NocState.LegacyRaceId, migrated.Noc!.RaceId);
        Assert.Equal(0, NocState.LegacyRaceId);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0)]
    [InlineData(99)] // not judged by the schema step: whether it is listed is decided at load time
    [InlineData(-1)]
    public void An_existing_race_id_is_never_touched_by_the_migration(int raceId) {
        var migrated = new Migration1To2().Apply(Sample(1, WithRace(raceId)));

        Assert.Equal(raceId, migrated.Noc!.RaceId);
    }

    [Fact]
    public void A_null_block_stays_null_and_every_other_field_is_kept() {
        var original = Sample(1, null);

        var migrated = new Migration1To2().Apply(original);

        Assert.Equal(original, migrated);
        Assert.Null(migrated.Noc);
    }

    [Fact]
    public void The_migration_keeps_the_rest_of_the_noc_block() {
        var migrated = new Migration1To2().Apply(Sample(1, WithRace(null)));

        Assert.Equal(WithRace(NocState.LegacyRaceId), migrated.Noc);
    }

    [Fact]
    public void A_schema_zero_save_goes_through_both_steps() {
        var migrated = CharacterMigrator.CreateDefault(TestSupport.Progression()).Migrate(Sample(CharacterSchema.Legacy, null));

        Assert.Equal(CharacterSchema.Current, migrated.SchemaVersion);
        Assert.Equal(1, migrated.Noc!.Level);
        Assert.Equal(NocState.LegacyRaceId, migrated.Noc.RaceId);
    }

    [Fact]
    public void A_schema_one_save_gets_the_legacy_race_and_keeps_its_progress() {
        var migrated = CharacterMigrator.CreateDefault(TestSupport.Progression()).Migrate(Sample(1, WithRace(null)));

        Assert.Equal(CharacterSchema.Current, migrated.SchemaVersion);
        Assert.Equal(WithRace(0), migrated.Noc);
    }

    [Fact]
    public async Task A_legacy_file_is_given_race_zero_on_load_and_not_rewritten() {
        Directory.CreateDirectory(chars);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy_char_full.json"), SavePath("legacy"));
        var before = File.ReadAllBytes(SavePath("legacy"));

        var (state, refusal) = await CharacterAccess.TryLoadAsync(store, "legacy", log: logs.Add, races: Races);

        Assert.Null(refusal);
        Assert.Equal(NocState.LegacyRaceId, state!.Noc!.RaceId);
        Assert.Empty(logs);
        Assert.Equal(before, File.ReadAllBytes(SavePath("legacy")));
    }

    // --- unknown race: fallback, not refusal ---------------------------------------------------------------------------

    [Theory]
    [InlineData(99)]
    [InlineData(6)] // the first id after the shipped ones
    [InlineData(-1)]
    public async Task An_unknown_race_id_falls_back_to_the_default_race_without_refusing_the_login(int raceId) {
        await store.SaveAsync("hero", Sample(CharacterSchema.Current, WithRace(raceId)));
        logs.Clear();

        var (state, refusal) = await CharacterAccess.TryLoadAsync(store, "hero", log: logs.Add, races: Races);

        Assert.Null(refusal);
        Assert.NotNull(state);
        Assert.Equal(Races.DefaultRaceId, state!.Noc!.RaceId);
        Assert.Equal(4, state.Noc.Level); // the rest of the character is intact
        Assert.Equal(30L, state.Noc.Xp);
        var warning = Assert.Single(logs);
        Assert.Contains("WARN", warning);
        Assert.Contains("'hero'", warning); // network id
        Assert.Contains($"RaceId {raceId} ", warning); // original id
    }

    [Fact]
    public async Task The_file_is_not_touched_by_the_fallback_and_keeps_the_original_race_id() {
        await store.SaveAsync("hero", Sample(CharacterSchema.Current, WithRace(99)));
        var before = File.ReadAllBytes(SavePath("hero"));

        await CharacterAccess.TryLoadAsync(store, "hero", log: logs.Add, races: Races);

        Assert.Equal(before, File.ReadAllBytes(SavePath("hero")));
        Assert.Contains("\"RaceId\": 99", File.ReadAllText(SavePath("hero")));
    }

    [Fact]
    public async Task Only_the_next_regular_save_stores_the_fallback_race() {
        await store.SaveAsync("hero", Sample(CharacterSchema.Current, WithRace(99)));
        var (state, _) = await CharacterAccess.TryLoadAsync(store, "hero", log: logs.Add, races: Races);

        Assert.True(await CharacterAccess.TrySaveAsync(store, "hero", state!, logs.Add));

        var back = await store.LoadAsync("hero");
        Assert.Equal(Races.DefaultRaceId, back!.Noc!.RaceId);
    }

    [Fact]
    public async Task A_listed_race_is_returned_unchanged_and_silently() {
        await store.SaveAsync("hero", Sample(CharacterSchema.Current, WithRace(4)));
        logs.Clear();

        var (state, refusal) = await CharacterAccess.TryLoadAsync(store, "hero", log: logs.Add, races: Races);

        Assert.Null(refusal);
        Assert.Equal(4, state!.Noc!.RaceId);
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Without_a_race_catalog_nothing_is_replaced() {
        await store.SaveAsync("hero", Sample(CharacterSchema.Current, WithRace(99)));

        var (state, refusal) = await CharacterAccess.TryLoadAsync(store, "hero", log: logs.Add);

        Assert.Null(refusal);
        Assert.Equal(99, state!.Noc!.RaceId);
    }

    [Fact]
    public async Task A_missing_save_and_a_save_without_a_block_are_not_touched_by_the_race_check() {
        var (missing, missingRefusal) = await CharacterAccess.TryLoadAsync(store, "nobody", log: logs.Add, races: Races);
        await store.SaveAsync("blockless", Sample(CharacterSchema.Current, null));
        var (blockless, blocklessRefusal) = await CharacterAccess.TryLoadAsync(store, "blockless", log: logs.Add, races: Races);

        Assert.Null(missing);
        Assert.Null(missingRefusal);
        Assert.NotNull(blockless);
        Assert.Null(blockless!.Noc);
        Assert.Null(blocklessRefusal);
        Assert.Empty(logs);
    }

    // --- too new schema: refusal, even when the race would also be unknown ----------------------------------------------

    [Fact]
    public async Task A_newer_schema_is_refused_and_the_file_is_left_alone() {
        Directory.CreateDirectory(chars);
        var newer = Sample(CharacterSchema.Current + 1, WithRace(99));
        File.WriteAllText(SavePath("future"), JsonSerializer.Serialize(newer));
        var before = File.ReadAllBytes(SavePath("future"));

        var (state, refusal) = await CharacterAccess.TryLoadAsync(store, "future", log: logs.Add, races: Races);

        Assert.Null(state);
        Assert.NotNull(refusal);
        Assert.Contains("newer server version", refusal);
        Assert.Contains(logs, l => l.Contains("Refusing login"));
        Assert.DoesNotContain(logs, l => l.Contains("WARN")); // refused before any race fallback could apply
        Assert.Equal(before, File.ReadAllBytes(SavePath("future")));
    }

    [Fact]
    public async Task The_same_unknown_race_on_a_current_schema_is_not_refused() {
        await store.SaveAsync("hero", Sample(CharacterSchema.Current, WithRace(99)));

        var (state, refusal) = await CharacterAccess.TryLoadAsync(store, "hero", log: logs.Add, races: Races);

        Assert.Null(refusal);
        Assert.NotNull(state);
    }

    [Fact]
    public void Only_the_schema_exception_means_refusal_and_it_is_not_a_race_problem() {
        var migrator = CharacterMigrator.CreateDefault(TestSupport.Progression());

        Assert.Throws<CharacterSchemaTooNewException>(() => migrator.Migrate(Sample(CharacterSchema.Current + 1, WithRace(0))));
        Assert.Equal(99, migrator.Migrate(Sample(CharacterSchema.Current, WithRace(99))).Noc!.RaceId); // no exception for an unknown race
    }

    // --- log text and catalog invariant --------------------------------------------------------------------------------

    [Fact]
    public void The_warning_names_the_character_the_original_id_and_the_fallback() {
        var line = CharacterLog.FormatUnknownRaceWarning("hero-1", 42, 0);

        Assert.Contains("WARN", line);
        Assert.Contains("'hero-1'", line);
        Assert.Contains("RaceId 42", line);
        Assert.Contains("race 0", line);
    }

    [Fact]
    public void The_race_catalog_must_list_the_legacy_race() {
        var onlyOne = """{"schemaVersion":1,"defaultRaceId":1,"races":[{"id":1,"key":"other","name":"Other","modifiers":[],"traits":[],"appearance":{"genders":[0],"skinColors":[0],"hairStyles":[0],"underwearColors":[0]},"art":{"native":true}}]}""";

        var ex = Assert.Throws<InvalidOperationException>(() => RaceConfig.Parse(onlyOne));

        Assert.Contains("race id 0", ex.Message);
        Assert.Contains("legacy", ex.Message);
    }
}

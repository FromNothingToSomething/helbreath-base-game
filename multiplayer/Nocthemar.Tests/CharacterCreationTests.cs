using Mmorpg.Network;
using Server.Nocthemar.Data;
using Server.Nocthemar.Net;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T30: AuthenticateRequest.creation is applied to a new character only; an existing save ignores it; invalid choices are refused with a message.</summary>
public class CharacterCreationTests {
    private static readonly RaceConfig Shipped = TestSupport.ShippedRaces();

    // Race 1 only allows female, dark skin, hair 0-1, underwear 3.
    private static readonly RaceConfig Restricted = RaceConfig.Parse(
        """{"schemaVersion":1,"defaultRaceId":0,"races":["""
        + """{"id":0,"key":"open","name":"Open","modifiers":[],"traits":[],"appearance":{"genders":[0,1],"skinColors":[0,1,2],"hairStyles":[0,1,2,3,4,5,6,7],"underwearColors":[0,1,2,3,4,5,6,7]},"art":{"native":true}},"""
        + """{"id":1,"key":"narrow","name":"Narrow","modifiers":[],"traits":[],"appearance":{"genders":[1],"skinColors":[2],"hairStyles":[0,1],"underwearColors":[3]},"art":{"native":true}}"""
        + """]}""");

    private static NocCharacterCreation Creation(int race, int gender = 0, int skin = 0, int hair = 0, int underwear = 0) =>
        new() { RaceId = race, Gender = gender, SkinColor = skin, HairStyleIndex = hair, UnderwearColorIndex = underwear };

    [Fact]
    public void Without_a_creation_block_nothing_is_applied_and_nothing_is_refused() {
        var result = CharacterCreationService.Resolve(null, saveExists: false, Shipped);

        Assert.Null(result.Choice);
        Assert.Null(result.Refusal);
    }

    [Fact]
    public void An_existing_save_ignores_the_creation_block_even_when_it_is_invalid() {
        foreach (var creation in new[] { Creation(3, 1, 2, 5, 7), Creation(99), Creation(-1) }) {
            var result = CharacterCreationService.Resolve(creation, saveExists: true, Shipped);

            Assert.Null(result.Choice);
            Assert.Null(result.Refusal);
        }
    }

    [Fact]
    public void A_valid_creation_becomes_a_choice() {
        var result = CharacterCreationService.Resolve(Creation(3, 1, 2, 5, 7), saveExists: false, Shipped);

        Assert.Null(result.Refusal);
        Assert.Equal(new NewCharacterChoice(3, 1, 2, 5, 7), result.Choice);
    }

    [Fact]
    public void An_empty_creation_block_means_race_zero_with_the_default_appearance() {
        var result = CharacterCreationService.Resolve(new NocCharacterCreation(), saveExists: false, Shipped);

        Assert.Equal(new NewCharacterChoice(0, 0, 0, 0, 0), result.Choice);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(6)]
    [InlineData(-1)]
    public void An_unknown_race_is_refused_with_a_message_naming_it(int raceId) {
        var result = CharacterCreationService.Resolve(Creation(raceId), saveExists: false, Shipped);

        Assert.Null(result.Choice);
        Assert.Contains("unknown race " + raceId, result.Refusal);
    }

    [Theory]
    [InlineData(0, 2, 0, 3, "gender")]
    [InlineData(1, 1, 0, 3, "skin_color")]
    [InlineData(1, 2, 5, 3, "hair_style_index")]
    [InlineData(1, 2, 0, 4, "underwear_color_index")]
    public void The_refusal_names_the_first_offending_axis(int gender, int skin, int hair, int underwear, string axis) {
        var result = CharacterCreationService.Resolve(Creation(1, gender, skin, hair, underwear), saveExists: false, Restricted);

        Assert.Contains(axis + " is not allowed", result.Refusal);
    }

    [Fact]
    public void An_allowed_restricted_appearance_is_accepted() {
        var result = CharacterCreationService.Resolve(Creation(1, 1, 2, 1, 3), saveExists: false, Restricted);

        Assert.Equal(new NewCharacterChoice(1, 1, 2, 1, 3), result.Choice);
    }

    [Fact]
    public void Out_of_range_values_are_refused_not_clamped() {
        Assert.NotNull(CharacterCreationService.Resolve(Creation(0, 0, 0, 8, 0), saveExists: false, Shipped).Refusal);
        Assert.NotNull(CharacterCreationService.Resolve(Creation(0, 2, 0, 0, 0), saveExists: false, Shipped).Refusal);
        Assert.NotNull(CharacterCreationService.Resolve(Creation(0, 0, 3, 0, 0), saveExists: false, Shipped).Refusal);
        Assert.NotNull(CharacterCreationService.Resolve(Creation(0, 0, 0, 0, -1), saveExists: false, Shipped).Refusal);
    }

    [Fact]
    public void Applying_a_choice_sets_race_appearance_and_full_hp_and_mana_without_a_message() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent, Shipped);

        p.ApplyNewCharacter(new NewCharacterChoice(2, 1, 2, 5, 7));

        Assert.Equal(2, p.RaceId);
        Assert.Equal(1, p.GenderValue);
        Assert.Equal(2, p.SkinColorValue);
        Assert.Equal(5, p.HairStyleIndex);
        Assert.Equal(7, p.UnderwearColorIndex);
        Assert.Equal(p.MaxHp, p.Hp);
        var snapshot = p.CreateNocSnapshot();
        Assert.Equal(snapshot.MaxMana, snapshot.Mana);
        Assert.False(p.TryFlushNocUpdates()); // the join snapshot carries it
        Assert.Empty(sent);
    }

    [Fact]
    public void Every_shipped_race_starts_at_full_hp_and_mana() {
        foreach (var race in Shipped.Races) {
            var p = TestSupport.NewPlayer(null, Shipped);

            p.ApplyNewCharacter(new NewCharacterChoice(race.Id, 0, 0, 0, 0));

            Assert.Equal(race.Id, p.RaceId);
            Assert.Equal(p.MaxHp, p.Hp);
            Assert.Equal(p.CreateNocSnapshot().MaxMana, p.CreateNocSnapshot().Mana);
        }
    }

    [Fact]
    public void An_unlisted_race_in_a_choice_falls_back_to_the_default_race() {
        var p = TestSupport.NewPlayer(null, Shipped);

        p.ApplyNewCharacter(new NewCharacterChoice(99, 0, 0, 0, 0));

        Assert.Equal(Shipped.DefaultRaceId, p.RaceId);
    }

    [Fact]
    public void Server_cs_resolves_the_creation_after_the_load_and_refuses_before_joining() {
        var source = File.ReadAllText(FindSource("Server.cs"));
        var load = source.IndexOf("CharacterAccess.TryLoadAsync(", StringComparison.Ordinal);
        var resolve = source.IndexOf("CharacterCreationService.Resolve(", StringComparison.Ordinal);
        var join = source.IndexOf("new PlayerConnectedMessage(", StringComparison.Ordinal);

        Assert.True(load > 0 && resolve > load && join > resolve, "load, then creation, then join");
        Assert.Contains("loadedPlayerState is not null, races)", source);
        Assert.Contains("creationResult.Refusal", source);
    }

    [Fact]
    public void Apply_new_character_is_only_reached_for_a_player_without_a_save() {
        var source = File.ReadAllText(FindSource("World", "Game", "GameWorld.cs"));
        var persisted = source.IndexOf("player.ApplyPersistedState(connectedMessage.PersistedState);", StringComparison.Ordinal);
        var created = source.IndexOf("player.ApplyNewCharacter(connectedMessage.NewCharacter);", StringComparison.Ordinal);

        Assert.True(persisted > 0 && created > persisted);
        Assert.Contains("} else if (connectedMessage.NewCharacter is not null)", source);
    }

    private static string FindSource(params string[] relative) {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine(new[] { dir.FullName, "server" }.Concat(relative).ToArray());
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        throw new FileNotFoundException("server/" + string.Join('/', relative) + " not found above " + AppContext.BaseDirectory);
    }
}

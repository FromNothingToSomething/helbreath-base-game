using Server.Nocthemar.Data;
using Server.Nocthemar.Net;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T31: appearance changes follow the race; the race itself never changes after creation; a legacy character is not locked out.</summary>
public class RaceAppearanceTests {
    // Race 1 only allows female, dark skin, hair 0-1, underwear 3.
    private static readonly RaceConfig Restricted = RaceConfig.Parse(
        """{"schemaVersion":1,"defaultRaceId":0,"races":["""
        + """{"id":0,"key":"open","name":"Open","modifiers":[],"traits":[],"appearance":{"genders":[0,1],"skinColors":[0,1,2],"hairStyles":[0,1,2,3,4,5,6,7],"underwearColors":[0,1,2,3,4,5,6,7]},"art":{"native":true}},"""
        + """{"id":1,"key":"narrow","name":"Narrow","modifiers":[],"traits":[],"appearance":{"genders":[1],"skinColors":[2],"hairStyles":[0,1],"underwearColors":[3]},"art":{"native":true}}"""
        + """]}""");

    private static GameWorldPlayer Narrow() {
        var p = TestSupport.NewPlayer(null, Restricted);
        p.ApplyNewCharacter(new NewCharacterChoice(1, 1, 2, 0, 3));
        return p;
    }

    [Fact]
    public void An_allowed_change_is_applied() {
        var p = Narrow();

        Assert.True(RaceAppearanceRules.TryApply(p, Restricted, 1, 2, 1, 3));

        Assert.Equal(1, p.HairStyleIndex);
    }

    [Theory]
    [InlineData(0, 2, 0, 3)]
    [InlineData(1, 0, 0, 3)]
    [InlineData(1, 2, 5, 3)]
    [InlineData(1, 2, 0, 0)]
    public void A_change_the_race_does_not_allow_is_refused_and_changes_nothing(int gender, int skin, int hair, int underwear) {
        var p = Narrow();

        Assert.False(RaceAppearanceRules.TryApply(p, Restricted, gender, skin, hair, underwear));

        Assert.Equal(1, p.GenderValue);
        Assert.Equal(2, p.SkinColorValue);
        Assert.Equal(0, p.HairStyleIndex);
        Assert.Equal(3, p.UnderwearColorIndex);
    }

    [Fact]
    public void An_appearance_change_never_changes_the_race() {
        var p = Narrow();

        RaceAppearanceRules.TryApply(p, Restricted, 1, 2, 1, 3);
        RaceAppearanceRules.TryApply(p, Restricted, 0, 0, 0, 0);

        Assert.Equal(1, p.RaceId);
    }

    [Fact]
    public void A_legacy_character_in_race_zero_may_use_the_whole_appearance_domain() {
        var races = TestSupport.ShippedRaces();
        var p = TestSupport.NewPlayer(null, races); // legacy characters are migrated to race 0

        for (var gender = 0; gender <= 1; gender++) {
            for (var skin = 0; skin <= 2; skin++) {
                for (var hair = 0; hair <= 7; hair++) {
                    for (var underwear = 0; underwear <= 7; underwear++) {
                        Assert.True(RaceAppearanceRules.TryApply(p, races, gender, skin, hair, underwear));
                    }
                }
            }
        }
    }

    [Fact]
    public void The_shipped_default_race_allows_the_whole_domain() {
        var race = TestSupport.ShippedRaces().Default;

        Assert.Equal(new[] { 0, 1 }, race.Appearance.Genders.Order().ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, race.Appearance.SkinColors.Order().ToArray());
        Assert.Equal(Enumerable.Range(0, 8).ToArray(), race.Appearance.HairStyles.Order().ToArray());
        Assert.Equal(Enumerable.Range(0, 8).ToArray(), race.Appearance.UnderwearColors.Order().ToArray());
    }

    [Fact]
    public void Without_a_race_catalog_or_with_an_unlisted_race_everything_is_allowed() {
        Assert.True(RaceAppearanceRules.IsAllowed(null, 1, 0, 0, 7, 7));
        Assert.True(RaceAppearanceRules.IsAllowed(Restricted, 42, 0, 0, 7, 7));
    }

    [Fact]
    public void The_world_handler_goes_through_the_race_rules_and_nothing_else_changes_the_race() {
        var source = File.ReadAllText(FindSource("World", "Game", "GameWorld.cs"));
        var start = source.IndexOf("private void HandleChangePlayerAppearance", StringComparison.Ordinal);
        var end = source.IndexOf("private void HandleChangePlayerAttackRange", start, StringComparison.Ordinal);
        var body = source.Substring(start, end - start);

        Assert.Contains("RaceAppearanceRules.TryApply(", body);
        Assert.DoesNotContain("SetAppearance(", body);
        Assert.DoesNotContain("ApplyRace", body);

        // The race is set in exactly one place of the world: the first join of a new character.
        Assert.Equal(1, Count(source, "ApplyNewCharacter("));
        Assert.Equal(0, Count(source, ".ApplyRace("));
    }

    [Fact]
    public void No_client_payload_handler_applies_a_race() {
        foreach (var file in new[] { new[] { "Nocthemar", "Net", "NocDispatcher.cs" }, new[] { "World", "Game", "GameWorld.cs" } }) {
            Assert.DoesNotContain(".ApplyRace(", File.ReadAllText(FindSource(file)));
        }
    }

    private static int Count(string text, string needle) {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal)) {
            n++;
        }

        return n;
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

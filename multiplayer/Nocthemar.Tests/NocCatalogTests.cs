using Google.Protobuf;
using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Net;
using Server.Utils;
using Xunit;

namespace NocthemarTests;

/// <summary>T44: wire form of the classes, skills, class state and refusals.</summary>
public class NocCatalogTests {
    private static NocCatalog Catalog() => TestSupport.ShippedContent().Catalog;

    [Fact]
    public void The_catalog_lists_every_class_branch_and_skill() {
        var catalog = Catalog();

        Assert.Equal(5, catalog.Classes.Count);
        Assert.All(catalog.Classes, c => Assert.Equal(3, c.Branches.Count));
        Assert.Equal(120, catalog.Skills.Count);
        Assert.Equal(120, catalog.Skills.Select(s => s.Id).Distinct().Count());
        Assert.Equal("Chevalier d'Osseux", catalog.Classes[0].Name);
        Assert.Equal("chantre_fer", catalog.Classes[1].Key);
        Assert.Equal("Rempart", catalog.Classes[0].Branches[0].Name);
    }

    [Fact]
    public void Skills_carry_their_rules() {
        var skills = Catalog().Skills.ToDictionary(s => s.Id);

        var posture = skills[1000];
        Assert.Equal((0, 0, 0, 1, 0, 1, true, 0), (posture.ClassId, posture.Branch, posture.Slot, posture.Level, posture.Kind, posture.Cost, posture.Ready, posture.PrerequisiteId));
        Assert.Equal("Posture de Fer", posture.Name);

        Assert.Equal(1020, skills[1022].PrerequisiteId); // the catalog-only slot 1 is skipped
        Assert.Equal(1214, skills[1216].PrerequisiteId);
        Assert.False(skills[1003].Ready);
        Assert.Equal(1, skills[1003].Kind);
        Assert.Equal(2, skills[1007].Kind);
        Assert.Equal(2, skills[1007].Cost);
        Assert.Equal(30, skills[1007].Level);
        Assert.Equal(1001, skills[1003].PrerequisiteId); // 1001 is a ready active
        Assert.Equal(1000, skills[1001].PrerequisiteId);
        Assert.Equal(0, skills[1000].PrerequisiteId);
        Assert.All(skills.Values, s => Assert.False(string.IsNullOrEmpty(s.Desc)));
    }

    [Fact]
    public void Recommended_races_are_a_hint_from_the_catalog() {
        var classes = Catalog().Classes;

        Assert.True(new[] { 0, 2, 3, 4, 5 }.SequenceEqual(classes[0].RecommendedRaceIds));
        Assert.True(new[] { 0, 1 }.SequenceEqual(classes[1].RecommendedRaceIds));
        Assert.All(classes, c => Assert.Contains(0, c.RecommendedRaceIds)); // Valdorniens suit every class
    }

    [Fact]
    public void The_catalog_survives_a_serialization_round_trip() {
        var original = Catalog();

        var back = NocCatalog.Parser.ParseFrom(original.ToByteArray());

        Assert.Equal(original, back);
        Assert.True(original.CalculateSize() < 64 * 1024, "the catalog is sent at every first join: keep it small");
    }

    [Fact]
    public void A_snapshot_without_class_encodes_like_before_classes_existed() {
        var snapshot = NetworkManager.CreateNocSnapshot(5, 100, 400, 30, 90, 2);
        var legacy = new NocSnapshot { Level = 5, Xp = 100, XpToNext = 400, Mana = 30, MaxMana = 90, RaceId = 2 };

        Assert.False(snapshot.HasClassId);
        Assert.Empty(snapshot.Skills);
        Assert.Equal(0, snapshot.SkillPoints);
        Assert.Equal(legacy.ToByteArray(), snapshot.ToByteArray());
    }

    [Fact]
    public void A_snapshot_carries_class_points_and_skills() {
        var snapshot = NetworkManager.CreateNocSnapshot(10, 0, 500, 10, 100, 1, classId: 0, skillPoints: 7, skills: new[] { 1000, 1010 });

        var back = NocSnapshot.Parser.ParseFrom(snapshot.ToByteArray());

        Assert.True(back.HasClassId);
        Assert.Equal(0, back.ClassId); // class 0 is a real class, distinct from "absent"
        Assert.Equal(7, back.SkillPoints);
        Assert.True(new[] { 1000, 1010 }.SequenceEqual(back.Skills));
    }

    [Fact]
    public void The_player_snapshot_reports_class_skills_and_points() {
        var p = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent());
        TestSupport.Restore(p, new NocState(NocState.CurrentV, 10, 0, 0, RaceId: 0, ClassId: 0, Skills: [1000]));

        var snapshot = p.CreateNocSnapshot();

        Assert.Equal(0, snapshot.ClassId);
        Assert.Equal(8, snapshot.SkillPoints);
        Assert.True(new[] { 1000 }.SequenceEqual(snapshot.Skills));
    }

    [Fact]
    public void A_player_without_class_has_an_empty_class_snapshot() {
        var snapshot = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent()).CreateNocSnapshot();

        Assert.False(snapshot.HasClassId);
        Assert.Equal(0, snapshot.SkillPoints);
    }

    [Fact]
    public void A_player_without_content_has_no_catalog() {
        Assert.Null(TestSupport.NewPlayer().NocCatalog);
        Assert.NotNull(TestSupport.NewPlayer(null, null, TestSupport.ShippedContent()).NocCatalog);
    }

    [Fact]
    public void The_refusal_message_carries_the_reason() {
        var message = NetworkManager.CreateNocActionRefused("Not enough skill points.");

        Assert.Equal(ServerMessage.PayloadOneofCase.NocActionRefused, message.PayloadCase);
        Assert.Equal("Not enough skill points.", ServerMessage.Parser.ParseFrom(message.ToByteArray()).NocActionRefused.Reason);
    }

    [Fact]
    public void The_new_requests_use_the_numbers_101_to_103() {
        Assert.Equal(101, (int)ClientMessage.PayloadOneofCase.NocChooseClassRequest);
        Assert.Equal(102, (int)ClientMessage.PayloadOneofCase.NocLearnSkillRequest);
        Assert.Equal(103, (int)ClientMessage.PayloadOneofCase.NocRespecRequest);
        Assert.Equal(101, (int)ServerMessage.PayloadOneofCase.NocActionRefused);
        Assert.Equal(29, InitialState.NocCatalogFieldNumber);
    }

    [Fact]
    public void The_catalog_is_only_sent_with_the_spells_at_the_first_join() {
        var source = File.ReadAllText(FindSource("Helpers", "Spawn.cs"));

        Assert.Contains("includeSpells ? player.NocCatalog : null", source);
    }

    [Fact]
    public void Recommended_races_must_exist_in_races_json() {
        var classes = TestSupport.ShippedClasses();

        classes.ValidateRaces(TestSupport.ShippedRaces());
        var ex = Assert.Throws<InvalidOperationException>(() => classes.ValidateRaces(TestSupport.NeutralRaces()));
        Assert.Contains("recommends unknown race", ex.Message);
    }

    [Theory]
    [InlineData("[-1]")]
    [InlineData("[70000]")]
    [InlineData("[1,1]")]
    public void Invalid_recommended_races_are_refused(string races) {
        const string branches = """[{"id":0,"key":"aa","name":"A","role":"r"},{"id":1,"key":"bb","name":"B","role":"r"},{"id":2,"key":"cc","name":"C","role":"r"}]""";
        var json = "{\"schemaVersion\":1,\"classes\":[{\"id\":0,\"key\":\"knight\",\"name\":\"K\",\"role\":\"r\",\"recommendedRaces\":" + races + ",\"branches\":" + branches + "}]}";

        var ex = Assert.Throws<InvalidOperationException>(() => ClassConfig.Parse(json));

        Assert.Contains("recommendedRaces", ex.Message);
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

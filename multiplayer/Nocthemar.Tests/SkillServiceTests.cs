using Server.Nocthemar.Core;
using Xunit;

namespace NocthemarTests;

/// <summary>T42: class choice, skill learning rules, points and load-time sanitizing, on the shipped catalog.</summary>
public class SkillServiceTests {
    // Shipped ready skills used below: knight 1000 / 1010 / 1020 (slot 0, level 1) and 1022 (slot 2, level 6, prerequisite 1020 because slot 1 is catalog-only);
    // singer 1100; shapeshifter 1200, 1210, 1214 (level 14, prerequisite 1210), 1216 (level 24, prerequisite 1214).
    private static SkillService Service() => new(TestSupport.ShippedClasses(), TestSupport.ShippedSkills(), TestSupport.Progression());

    private static NocState Knight(int level, params int[] skills) =>
        new(NocState.CurrentV, level, 0, 0, RaceId: 0, ClassId: 0, Skills: skills.Length == 0 ? null : skills);

    private static void Refused(SkillActionResult result, string expected) {
        Assert.False(result.Ok);
        Assert.Contains(expected, result.Refusal);
    }

    [Fact]
    public void The_class_is_chosen_once() {
        var service = Service();
        var fresh = NocState.CreateDefault(100);

        var chosen = service.ChooseClass(fresh, 3);

        Assert.True(chosen.Ok);
        Assert.Equal(3, chosen.State.ClassId);
        Refused(service.ChooseClass(chosen.State, 1), "already been chosen");
        Assert.Equal(3, service.ChooseClass(chosen.State, 1).State.ClassId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(99)]
    public void An_unknown_class_is_refused(int classId) {
        Refused(Service().ChooseClass(NocState.CreateDefault(100), classId), "Unknown class");
    }

    [Fact]
    public void Every_shipped_class_can_be_chosen() {
        var service = Service();

        for (var id = 0; id < 5; id++) {
            Assert.Equal(id, service.ChooseClass(NocState.CreateDefault(100), id).State.ClassId);
        }
    }

    [Fact]
    public void Points_are_one_per_level_from_level_two() {
        var service = Service();

        Assert.Equal(0, service.PointsAvailable(Knight(1)));
        Assert.Equal(1, service.PointsAvailable(Knight(2)));
        Assert.Equal(10, service.PointsAvailable(Knight(11)));
        Assert.Equal(9, service.PointsAvailable(Knight(11, 1000)));
        Assert.Equal(1, service.PointsSpent(Knight(11, 1000)));
        Assert.Equal(0, service.PointsAvailable(Knight(2, 1000)));
        Assert.Equal(0, service.PointsSpent(Knight(11, 5)));
    }

    [Fact]
    public void Learning_spends_a_point_and_keeps_the_ids_sorted() {
        var service = Service();
        var state = Knight(5);

        var first = service.Learn(state, 1010);
        var second = service.Learn(first.State, 1000);

        Assert.True(first.Ok && second.Ok);
        Assert.True(new[] { 1000, 1010 }.SequenceEqual(second.State.Skills!));
        Assert.Equal(2, service.PointsAvailable(second.State));
        Assert.True(service.IsLearned(second.State, 1000));
        Assert.False(service.IsLearned(second.State, 1020));
        Assert.Null(state.Skills);
    }

    [Fact]
    public void Learning_refusals() {
        var service = Service();

        Refused(service.Learn(NocState.CreateDefault(100) with { Level = 10 }, 1000), "Choose a class first");
        Refused(service.Learn(Knight(10), 5), "Unknown skill");
        Refused(service.Learn(Knight(10), 1100), "another class");
        Refused(service.Learn(Knight(10), 1001), "not available yet");
        Refused(service.Learn(Knight(10, 1000), 1000), "already learned");
        Refused(service.Learn(Knight(1), 1000), "Not enough skill points");
        Refused(service.Learn(Knight(5), 1022), "Requires level 6");
        Refused(service.Learn(Knight(7), 1022), "Learn Souvenir Ancestral first");
        Refused(service.Learn(Knight(10, 1000, 1010), 1020), "Only two branches");
    }

    [Fact]
    public void A_refusal_returns_the_unchanged_state() {
        var state = Knight(10, 1000);

        var result = Service().Learn(state, 1001);

        Assert.Same(state, result.State);
    }

    [Fact]
    public void Catalog_only_skills_do_not_block_the_chain() {
        var service = Service();
        var state = service.Learn(Knight(7), 1020).State;

        var result = service.Learn(state, 1022);

        Assert.True(result.Ok);
        Assert.Equal(1020, service.PrerequisiteOf(TestSupport.ShippedSkills().Skills.Single(s => s.Id == 1022))!.Id);
        Assert.Null(service.PrerequisiteOf(TestSupport.ShippedSkills().Skills.Single(s => s.Id == 1020)));
    }

    [Fact]
    public void Two_branches_can_be_combined_and_a_third_cannot() {
        var service = Service();
        var state = service.Learn(service.Learn(Knight(10), 1000).State, 1010).State;

        Refused(service.Learn(state, 1020), "Only two branches");
        Assert.True(service.Learn(state, 1000).Refusal is not null);
    }

    [Fact]
    public void Points_run_out() {
        var service = Service();
        var state = service.Learn(Knight(2), 1000).State; // level 2 = 1 point

        Assert.Equal(0, service.PointsAvailable(state));

        Refused(service.Learn(state, 1010), "Not enough skill points");
    }

    [Fact]
    public void The_shapeshifter_chain_follows_its_prerequisites() {
        var service = Service();
        var state = new NocState(NocState.CurrentV, 30, 0, 0, ClassId: 2);

        Refused(service.Learn(state, 1214), "Learn Peau Écailleuse first");
        state = service.Learn(state, 1210).State;
        state = service.Learn(state, 1214).State;
        state = service.Learn(state, 1216).State;

        Assert.True(new[] { 1210, 1214, 1216 }.SequenceEqual(state.Skills!));
    }

    [Fact]
    public void Respec_gives_the_points_back_and_keeps_the_class() {
        var service = Service();
        var state = Knight(10, 1000, 1010);

        var reset = service.Respec(state);

        Assert.Null(reset.Skills);
        Assert.Equal(0, reset.ClassId);
        Assert.Equal(9, service.PointsAvailable(reset));
    }

    [Fact]
    public void Learned_resolves_catalog_entries_in_id_order() {
        var learned = Service().Learned(Knight(10, 1000, 1010, 77));

        Assert.True(new[] { 1000, 1010 }.SequenceEqual(learned.Select(s => s.Id)));
    }

    // --- Sanitize ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_valid_state_is_untouched() {
        var state = Knight(10, 1000, 1010);

        var clean = Service().Sanitize(state, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(0, clean.ClassId);
        Assert.True(new[] { 1000, 1010 }.SequenceEqual(clean.Skills!));
    }

    [Fact]
    public void A_state_without_class_or_skills_stays_empty() {
        var clean = Service().Sanitize(NocState.CreateDefault(100), out var warnings);

        Assert.Empty(warnings);
        Assert.Null(clean.ClassId);
        Assert.Null(clean.Skills);
    }

    [Fact]
    public void An_unknown_class_is_dropped_with_its_skills() {
        var clean = Service().Sanitize(new NocState(NocState.CurrentV, 10, 0, 0, ClassId: 9, Skills: [1000]), out var warnings);

        Assert.Null(clean.ClassId);
        Assert.Null(clean.Skills);
        Assert.Contains(warnings, w => w.Contains("unknown class 9"));
    }

    [Fact]
    public void Skills_without_a_class_are_dropped() {
        var clean = Service().Sanitize(new NocState(NocState.CurrentV, 10, 0, 0, Skills: [1000]), out var warnings);

        Assert.Null(clean.Skills);
        Assert.Contains(warnings, w => w.Contains("no class"));
    }

    [Fact]
    public void Unknown_planned_foreign_and_duplicate_skills_are_dropped() {
        var clean = Service().Sanitize(Knight(10, 1000, 1000, 5, 1001, 1100, 1010), out var warnings);

        Assert.True(new[] { 1000, 1010 }.SequenceEqual(clean.Skills!));
        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("skill 5") && w.Contains("catalog"));
        Assert.Contains(warnings, w => w.Contains("skill 1001") && w.Contains("not available"));
        Assert.Contains(warnings, w => w.Contains("skill 1100") && w.Contains("another class"));
    }

    [Fact]
    public void A_skill_above_the_level_is_dropped() {
        var clean = Service().Sanitize(Knight(3, 1000, 1020, 1022), out var warnings);

        Assert.True(new[] { 1000, 1020 }.SequenceEqual(clean.Skills!));
        Assert.Contains(warnings, w => w.Contains("1022") && w.Contains("level 6"));
    }

    [Fact]
    public void A_skill_missing_its_prerequisite_is_dropped() {
        var clean = Service().Sanitize(Knight(10, 1022), out var warnings);

        Assert.Null(clean.Skills);
        Assert.Contains(warnings, w => w.Contains("1022") && w.Contains("prerequisite"));
    }

    [Fact]
    public void More_points_than_earned_keep_the_first_skills_only() {
        var clean = Service().Sanitize(Knight(2, 1000, 1010), out var warnings); // level 2 = 1 point

        Assert.True(new[] { 1000 }.SequenceEqual(clean.Skills!));
        Assert.Contains(warnings, w => w.Contains("1010") && w.Contains("points"));
    }

    [Fact]
    public void A_third_branch_is_dropped() {
        var clean = Service().Sanitize(Knight(10, 1000, 1010, 1020), out var warnings);

        Assert.True(new[] { 1000, 1010 }.SequenceEqual(clean.Skills!));
        Assert.Contains(warnings, w => w.Contains("1020") && w.Contains("two branches"));
    }

    [Fact]
    public void Sanitize_does_not_change_the_other_fields() {
        var state = new NocState(NocState.CurrentV, 10, 55, 7, RaceId: 4, ClassId: 0, FactionId: 2, Skills: [1000], Kills: 12);

        var clean = Service().Sanitize(state, out _);

        Assert.Equal((10, 55L, 7, 4, 2, 12), (clean.Level, clean.Xp, clean.Mana, clean.RaceId ?? -1, clean.FactionId ?? -1, clean.Kills));
    }
}

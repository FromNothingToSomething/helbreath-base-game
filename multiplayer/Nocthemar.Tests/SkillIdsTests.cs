using Server.Nocthemar.Core;
using Xunit;

namespace NocthemarTests;

/// <summary>T37: skill id scheme and the Skill modifier source category.</summary>
public class SkillIdsTests {
    [Fact]
    public void Ids_follow_the_documented_formula() {
        Assert.Equal(1000, SkillIds.Make(0, 0, 0));
        Assert.Equal(1007, SkillIds.Make(0, 0, 7));
        Assert.Equal(1010, SkillIds.Make(0, 1, 0));
        Assert.Equal(1100, SkillIds.Make(1, 0, 0));
        Assert.Equal(1427, SkillIds.Make(4, 2, 7));
    }

    [Fact]
    public void Every_position_round_trips_and_ids_are_unique() {
        var seen = new HashSet<int>();
        for (var c = 0; c < SkillIds.MaxClasses; c++) {
            for (var b = 0; b < SkillIds.BranchesPerClass; b++) {
                for (var s = 0; s < SkillIds.SlotsPerBranch; s++) {
                    var id = SkillIds.Make(c, b, s);
                    Assert.True(seen.Add(id), $"duplicate id {id}");
                    Assert.True(SkillIds.TryDecompose(id, out var c2, out var b2, out var s2));
                    Assert.Equal((c, b, s), (c2, b2, s2));
                    Assert.True(id <= ModifierSources.MaxId);
                }
            }
        }

        Assert.Equal(SkillIds.MaxClasses * SkillIds.SkillsPerClass, seen.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(1008)]
    [InlineData(1009)]
    [InlineData(1030)]
    [InlineData(1099)]
    [InlineData(2000)]
    [InlineData(-5)]
    public void Legacy_spells_and_holes_are_not_class_skills(int id) {
        Assert.False(SkillIds.IsClassSkill(id));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(10, 0, 0)]
    [InlineData(0, 3, 0)]
    [InlineData(0, 0, 8)]
    public void Make_rejects_out_of_range_positions(int c, int b, int s) {
        Assert.Throws<ArgumentOutOfRangeException>(() => SkillIds.Make(c, b, s));
    }

    [Fact]
    public void Slot_levels_and_kinds_match_the_design() {
        Assert.True(new[] { 1, 3, 6, 10, 14, 18, 24, 30 }.SequenceEqual(SkillIds.SlotLevels));
        Assert.Equal(8, SkillIds.SlotLevels.Count);
        Assert.True(new[] { true, false, true, false, true, false, true, false }.SequenceEqual(Enumerable.Range(0, 8).Select(SkillIds.IsPassiveSlot)));
        Assert.True(SkillIds.IsUltimateSlot(7));
        Assert.False(SkillIds.IsUltimateSlot(6));
        Assert.Equal(30, SkillIds.RequiredLevel(7));
    }

    [Fact]
    public void Skill_source_category_is_6_and_does_not_collide_with_other_owners() {
        var skill = ModifierSources.Skill(1000);
        Assert.Equal(ModifierSourceCategory.Skill, ModifierSources.CategoryOf(skill));
        Assert.Equal(6, (int)ModifierSourceCategory.Skill);
        Assert.Equal(1000, ModifierSources.IdOf(skill));
        Assert.NotEqual(ModifierSources.Class(1000), skill);
        Assert.NotEqual(ModifierSources.Buff(1000), skill);
        Assert.NotEqual(ModifierSources.Race(1000), skill);
    }

    [Fact]
    public void Removing_a_skill_source_leaves_the_other_sources() {
        var cfg = TestSupport.Progression();
        var pipe = new StatPipeline(cfg);
        pipe.Add(new StatModifier(StatId.DamageMult, ModifierKind.PercentAdd, 0.10, ModifierSources.Skill(1020)));
        pipe.Add(new StatModifier(StatId.DamageMult, ModifierKind.PercentAdd, 0.20, ModifierSources.Class(0)));
        pipe.Add(new StatModifier(StatId.DamageMult, ModifierKind.PercentAdd, 0.30, ModifierSources.Race(0)));

        Assert.Equal(1.6, pipe.Get(StatId.DamageMult), 6);
        Assert.Equal(1, pipe.RemoveSource(ModifierSources.Skill(1020)));
        Assert.Equal(1.5, pipe.Get(StatId.DamageMult), 6);
    }
}

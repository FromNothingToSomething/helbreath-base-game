using Server.Nocthemar.Core;
using Xunit;

namespace NocthemarTests;

/// <summary>T56: id ranges of spells, class skills and mimicry abilities, the essence item mapping and the three new stats.</summary>
public class MimicIdsTests {
    [Fact]
    public void Ranges_do_not_overlap() {
        var highestSkill = SkillIds.Make(SkillIds.MaxClasses - 1, SkillIds.BranchesPerClass - 1, SkillIds.SlotsPerBranch - 1);

        Assert.True(highestSkill < SkillIds.End);
        Assert.Equal(MimicIds.Base, SkillIds.End);
        Assert.Equal(2000, MimicIds.Base);
        Assert.Equal(2100, MimicIds.FusionBase);
        Assert.Equal(2200, MimicIds.End);
        Assert.False(MimicIds.IsMimic(SkillIds.Base));
        Assert.False(MimicIds.IsMimic(1999));
        Assert.True(MimicIds.IsMimic(2000));
        Assert.True(MimicIds.IsMimic(2199));
        Assert.False(MimicIds.IsMimic(2200));
    }

    [Fact]
    public void Monster_abilities_and_fusions_are_disjoint() {
        for (var id = 1990; id < 2210; id++) {
            Assert.False(MimicIds.IsMonsterAbility(id) && MimicIds.IsFusion(id));
            Assert.Equal(MimicIds.IsMimic(id), MimicIds.IsMonsterAbility(id) || MimicIds.IsFusion(id));
        }

        Assert.True(MimicIds.IsMonsterAbility(2099));
        Assert.True(MimicIds.IsFusion(2100));
    }

    [Fact]
    public void The_essence_item_maps_back_to_its_ability() {
        Assert.Equal(5000, MimicIds.EssenceItemId(2000));
        Assert.Equal(5021, MimicIds.EssenceItemId(2021));

        for (var id = MimicIds.Base; id < MimicIds.FusionBase; id++) {
            Assert.True(MimicIds.TryAbilityOfEssence(MimicIds.EssenceItemId(id), out var back));
            Assert.Equal(id, back);
        }

        Assert.False(MimicIds.TryAbilityOfEssence(4999, out _));
        Assert.False(MimicIds.TryAbilityOfEssence(5100, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => MimicIds.EssenceItemId(2100));
        Assert.Throws<ArgumentOutOfRangeException>(() => MimicIds.EssenceItemId(1000));
    }

    [Fact]
    public void The_new_stats_have_their_frozen_ids_and_neutral_defaults() {
        var p = new StatPipeline(TestSupport.Progression());

        Assert.Equal(18, (int)StatId.MimicSlots);
        Assert.Equal(19, (int)StatId.MimicPowerMult);
        Assert.Equal(20, (int)StatId.MimicCostMult);
        Assert.Equal(21, (int)StatId.EssenceCapture);
        Assert.Equal(Enum.GetValues<StatId>().Length, StatIds.Count); // follows the enum: StatIdTests freezes the ids and the total
        Assert.Equal(0.0, p.Get(StatId.EssenceCapture));
        Assert.Equal(0.0, p.Get(StatId.MimicSlots));
        Assert.Equal(1.0, p.Get(StatId.MimicPowerMult));
        Assert.Equal(1.0, p.Get(StatId.MimicCostMult));
    }

    [Fact]
    public void Extra_slots_are_a_flat_bonus_and_costs_follow_the_multiplier_rules() {
        var p = new StatPipeline(TestSupport.Progression());
        p.Add(new StatModifier(StatId.MimicSlots, ModifierKind.Flat, 2, 1));
        p.Add(new StatModifier(StatId.MimicCostMult, ModifierKind.PercentAdd, -0.20, 1));
        p.Add(new StatModifier(StatId.MimicPowerMult, ModifierKind.PercentAdd, 0.10, 1));

        Assert.Equal(2.0, p.Get(StatId.MimicSlots));
        Assert.Equal(0.80, p.Get(StatId.MimicCostMult), 12);
        Assert.Equal(1.10, p.Get(StatId.MimicPowerMult), 12);
        p.SetLevel(40);
        Assert.Equal(2.0, p.Get(StatId.MimicSlots));
    }
}

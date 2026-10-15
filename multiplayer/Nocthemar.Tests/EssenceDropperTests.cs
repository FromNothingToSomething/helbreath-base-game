using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T61: essence drop rates with an injected random source, raid-only essences, and the orb registry.</summary>
public class EssenceDropperTests {
    private static MimicConfig Shipped() => TestSupport.ShippedMimic();

    private static EssenceDropper Dropper(Func<double> roll) => new(Shipped(), roll);

    private static MimicAbility Of(string key) => Shipped().Abilities.Single(a => a.Key == key);

    [Fact]
    public void A_monster_without_an_ability_never_drops_and_draws_nothing() {
        var draws = 0;
        var dropper = Dropper(() => { draws++; return 0.0; });

        Assert.Null(dropper.Roll(0, raidWorld: true));
        Assert.Null(dropper.Roll(2, raidWorld: false));
        Assert.Null(dropper.Roll(-1, raidWorld: true));
        Assert.Equal(0, draws);
    }

    [Fact]
    public void A_roll_below_the_drop_chance_drops_the_ability_and_a_roll_at_or_above_does_not() {
        var chance = Of("nuee").DropChance;
        var monster = Of("nuee").MonsterId!.Value;

        Assert.Equal("nuee", Dropper(() => 0.0).Roll(monster, false)!.Key);
        Assert.Equal("nuee", Dropper(() => chance - 0.0001).Roll(monster, false)!.Key);
        Assert.Null(Dropper(() => chance).Roll(monster, false));
        Assert.Null(Dropper(() => 0.999).Roll(monster, false));
    }

    [Fact]
    public void One_random_value_is_drawn_per_possible_drop() {
        var draws = 0;
        var dropper = Dropper(() => { draws++; return 0.5; });

        dropper.Roll(Of("nuee").MonsterId!.Value, false);
        dropper.Roll(Of("peau_de_fer").MonsterId!.Value, false);

        Assert.Equal(2, draws);
    }

    [Theory]
    [InlineData("nuee", 0.20)]
    [InlineData("peau_de_fer", 0.10)]
    [InlineData("trois_tetes", 0.04)]
    [InlineData("folie", 0.01)]
    public void The_observed_rate_over_a_uniform_grid_matches_the_configured_chance(string key, double expected) {
        var ability = Of(key);
        Assert.Equal(expected, ability.DropChance, 9);
        var step = 0;
        var dropper = Dropper(() => step++ / 10_000.0);

        var drops = Enumerable.Range(0, 10_000).Count(_ => dropper.Roll(ability.MonsterId!.Value, raidWorld: true) is not null);

        Assert.Equal((int)Math.Round(expected * 10_000), drops);
    }

    [Fact]
    public void Mythic_essences_only_drop_in_a_raid_world() {
        var dropper = Dropper(() => 0.0);
        foreach (var key in new[] { "jugement", "chant_de_la_fin" }) {
            var ability = Of(key);
            Assert.True(ability.RequiresRaid);
            Assert.Null(dropper.Roll(ability.MonsterId!.Value, raidWorld: false));
            Assert.Equal(key, dropper.Roll(ability.MonsterId!.Value, raidWorld: true)!.Key);
        }
    }

    [Fact]
    public void A_blocked_mythic_drop_draws_no_random_value() {
        var draws = 0;
        var dropper = Dropper(() => { draws++; return 0.0; });

        Assert.Null(dropper.Roll(Of("jugement").MonsterId!.Value, raidWorld: false));
        Assert.Equal(0, draws);
    }

    [Fact]
    public void Every_non_mythic_monster_ability_can_drop_outside_a_raid_and_has_an_essence_item() {
        var dropper = Dropper(() => 0.0);

        foreach (var ability in Shipped().Abilities.Where(a => !a.IsFusion)) {
            var dropped = dropper.Roll(ability.MonsterId!.Value, raidWorld: false);
            if (ability.RequiresRaid) {
                Assert.Null(dropped);
            } else {
                Assert.Same(ability, dropped);
                Assert.Equal(MimicIds.EssenceItemBase + (ability.Id - MimicIds.Base), dropped!.EssenceItemId);
            }
        }
    }

    [Fact]
    public void The_registry_tells_orbs_from_other_ground_items_and_forgets_a_captured_one() {
        var orbs = new EssenceOrbRegistry();

        orbs.Mark(7);
        orbs.Mark(7);

        Assert.True(orbs.IsOrb(7));
        Assert.False(orbs.IsOrb(8));
        Assert.Equal(1, orbs.Count);
        Assert.True(orbs.Release(7));
        Assert.False(orbs.IsOrb(7));
        Assert.False(orbs.Release(7));
    }

    [Fact]
    public void The_registry_is_bounded_and_forgets_the_oldest_first() {
        var orbs = new EssenceOrbRegistry(capacity: 3);

        for (var uid = 1; uid <= 5; uid++) {
            orbs.Mark(uid);
        }

        Assert.Equal(3, orbs.Count);
        Assert.False(orbs.IsOrb(1));
        Assert.False(orbs.IsOrb(2));
        Assert.True(orbs.IsOrb(3) && orbs.IsOrb(4) && orbs.IsOrb(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EssenceOrbRegistry(0));
    }
}

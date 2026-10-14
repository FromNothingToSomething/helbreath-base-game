using Server.Nocthemar.Core;
using Xunit;

namespace NocthemarTests;

/// <summary>T46: critical hits. Chance = StatId.CritChance (base 5 %), damage = normal x 1.5 x CritDamageMult.</summary>
public class CritTests {
    private static Server.World.Game.GameWorldPlayer Player(int damage = 20) {
        var p = TestSupport.NewPlayer();
        p.SetAttackDamage(damage);
        return p;
    }

    [Fact]
    public void The_base_chance_is_five_percent_and_the_base_multiplier_one_and_a_half() {
        var p = Player();

        Assert.Equal(0.05, p.Stats.CritChance, 12);
        Assert.Equal(1.0, p.Stats.CritDamageMult, 12);
        Assert.Equal(1.5, Server.World.Game.GameWorldPlayer.CritBaseMultiplier);
        Assert.Equal(16, (int)StatId.CritChance);
    }

    [Fact]
    public void A_roll_below_the_chance_is_critical_and_one_at_the_chance_is_not() {
        var p = Player(20);

        Assert.Equal(new AttackRoll(30, true), p.RollAttackDamage(0.0));
        Assert.Equal(new AttackRoll(30, true), p.RollAttackDamage(0.049999));
        Assert.Equal(new AttackRoll(20, false), p.RollAttackDamage(0.05));
        Assert.Equal(new AttackRoll(20, false), p.RollAttackDamage(0.99));
    }

    [Fact]
    public void Exactly_five_percent_of_evenly_spread_rolls_are_critical() {
        var p = Player();
        var crits = 0;

        for (var i = 0; i < 10_000; i++) {
            if (p.RollAttackDamage(i / 10_000.0).Crit) {
                crits++;
            }
        }

        Assert.Equal(500, crits);
    }

    [Fact]
    public void The_damage_multiplier_applies_before_the_critical_bonus() {
        var p = Player(20);
        p.Stats.Add(new StatModifier(StatId.DamageMult, ModifierKind.PercentAdd, 0.2, 1));

        Assert.Equal(24, p.RollAttackDamage(0.9).Damage);
        Assert.Equal(36, p.RollAttackDamage(0.0).Damage); // 24 x 1.5
    }

    [Fact]
    public void Crit_damage_modifiers_scale_the_bonus() {
        var p = Player(20);
        p.Stats.Add(new StatModifier(StatId.CritDamageMult, ModifierKind.PercentAdd, 0.10, 1));

        Assert.Equal(33, p.RollAttackDamage(0.0).Damage); // 20 x 1.5 x 1.1
        Assert.Equal(20, p.RollAttackDamage(0.5).Damage);
    }

    [Fact]
    public void A_critical_hit_is_never_weaker_than_a_normal_one() {
        var p = Player(20);
        p.Stats.Add(new StatModifier(StatId.CritDamageMult, ModifierKind.PercentAdd, -0.95, 1));

        var roll = p.RollAttackDamage(0.0);

        Assert.True(roll.Crit);
        Assert.Equal(20, roll.Damage);
    }

    [Fact]
    public void Chance_modifiers_are_added_and_clamped() {
        var p = Player();
        p.Stats.Add(new StatModifier(StatId.CritChance, ModifierKind.Flat, 0.10, 1));
        Assert.Equal(0.15, p.Stats.CritChance, 12);
        Assert.True(p.RollAttackDamage(0.149).Crit);
        Assert.False(p.RollAttackDamage(0.16).Crit);

        p.Stats.Add(new StatModifier(StatId.CritChance, ModifierKind.Flat, 5.0, 2));
        Assert.True(p.RollAttackDamage(0.999999).Crit); // a chance above 1 is a sure hit

        p.Stats.RemoveSource(1);
        p.Stats.RemoveSource(2);
        p.Stats.Add(new StatModifier(StatId.CritChance, ModifierKind.Flat, -0.05, 3));
        Assert.Equal(0.0, p.Stats.CritChance, 12);
        Assert.False(p.RollAttackDamage(0.0).Crit);
    }

    [Fact]
    public void The_plain_damage_computation_has_no_critical_bonus() {
        var p = Player(20);

        Assert.Equal(20, p.ComputeAttackDamage());
    }

    [Fact]
    public void A_shapeshifter_skill_raises_the_critical_damage() {
        var p = TestSupport.NewPlayer(null, null, TestSupport.ShippedContent());
        p.SetAttackDamage(100);
        TestSupport.Restore(p, new NocState(NocState.CurrentV, 5, 0, 0, RaceId: 0, ClassId: 2, Skills: [1200]));
        p.SetAttackDamage(100);

        Assert.Equal(165, p.RollAttackDamage(0.0).Damage); // 100 x 1.5 x 1.1
    }

    [Fact]
    public void Combat_rolls_through_the_player() {
        var source = File.ReadAllText(FindSource("Helpers", "Combat.cs"));

        Assert.DoesNotContain("attacker.ComputeAttackDamage()", source);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source, @"attacker\.RollAttackDamage\(\)\.Damage").Count);
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

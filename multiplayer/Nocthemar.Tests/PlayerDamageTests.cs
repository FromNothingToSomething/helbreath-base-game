using Server.Nocthemar.Core;
using Xunit;

namespace NocthemarTests;

/// <summary>T14: attack damage goes through the stat pipeline; no modifier means no change.</summary>
public class PlayerDamageTests {
    [Fact]
    public void Without_modifiers_every_valid_damage_value_is_unchanged() {
        var p = TestSupport.NewPlayer();

        for (var d = 1; d <= 1000; d++) {
            p.SetAttackDamage(d);
            Assert.Equal(p.Damage, p.ComputeAttackDamage());
        }
    }

    [Fact]
    public void Default_player_damage_is_unchanged() {
        var p = TestSupport.NewPlayer();

        Assert.Equal(p.Damage, p.ComputeAttackDamage());
    }

    [Fact]
    public void Plus_twenty_percent_modifier_raises_damage_and_removal_restores_it() {
        var p = TestSupport.NewPlayer();
        p.SetAttackDamage(20);
        p.Stats.Add(new StatModifier(StatId.DamageMult, ModifierKind.PercentAdd, 0.2, 42));

        Assert.Equal(24, p.ComputeAttackDamage());
        p.SetAttackDamage(100);
        Assert.Equal(120, p.ComputeAttackDamage());

        p.Stats.RemoveSource(42);
        Assert.Equal(100, p.ComputeAttackDamage());
    }

    [Fact]
    public void Raw_damage_stays_available_for_the_client_snapshot() {
        var p = TestSupport.NewPlayer();
        p.SetAttackDamage(20);
        p.Stats.Add(new StatModifier(StatId.DamageMult, ModifierKind.PercentAdd, 0.2, 1));

        Assert.Equal(20, p.Damage);
    }

    [Fact]
    public void Persistence_keeps_the_raw_damage_not_the_modified_one() {
        var p = TestSupport.NewPlayer();
        p.SetAttackDamage(20);
        p.Stats.Add(new StatModifier(StatId.DamageMult, ModifierKind.PercentAdd, 0.2, 1));

        Assert.Equal(20, p.CreatePersistenceState("aresden").Damage);
    }
}

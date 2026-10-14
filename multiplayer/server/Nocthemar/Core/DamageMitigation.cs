namespace Server.Nocthemar.Core;

/// <summary>What kind of damage a hit is, which decides the defences that apply to it.</summary>
public enum DamageSchool : byte {
    /// <summary>Weapon hits and monster melee: <see cref="StatId.ArmorMult"/> and <see cref="StatId.ResistPhysical"/>.</summary>
    Physical = 0,

    /// <summary>Spell damage: <see cref="StatId.ResistMagic"/> only.</summary>
    Magic = 1,
}

/// <summary>
/// Damage a defender takes: <c>damage x (1 - resist) / armor</c>, rounded half away from zero, never below 1 for a positive hit.
/// Resistance is a fraction capped at <see cref="MaxResist"/>; armor (<see cref="StatId.ArmorMult"/>, 1.0 = none) applies to physical damage and cannot drop under <see cref="MinArmor"/>.
/// A defender without modifiers takes exactly the damage dealt.
/// </summary>
public static class DamageMitigation {
    public const double MaxResist = 0.8;
    public const double MinArmor = 0.1;

    public static int Apply(int damage, DamageSchool school, StatPipeline stats) {
        ArgumentNullException.ThrowIfNull(stats);
        if (damage <= 0) {
            return damage;
        }

        var resist = Math.Clamp(stats.Get(school == DamageSchool.Physical ? StatId.ResistPhysical : StatId.ResistMagic), 0.0, MaxResist);
        var armor = school == DamageSchool.Physical ? Math.Max(stats.Get(StatId.ArmorMult), MinArmor) : 1.0;
        var reduced = Math.Round(damage * (1.0 - resist) / armor, MidpointRounding.AwayFromZero);
        return reduced >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)reduced);
    }
}

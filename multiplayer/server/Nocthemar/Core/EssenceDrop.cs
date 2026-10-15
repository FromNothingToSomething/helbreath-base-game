using Server.Nocthemar.Data;

namespace Server.Nocthemar.Core;

/// <summary>
/// Decides whether a killed monster leaves the essence of its mimicry ability. The random source is injected (a uniform value in [0, 1)) so the rates are testable.
/// A monster without an ability never drops (and consumes no random value); a raid-only ability (mythic) never drops outside a raid world.
/// </summary>
public sealed class EssenceDropper {
    private readonly MimicConfig mimic;
    private readonly Func<double> roll;

    public EssenceDropper(MimicConfig mimic, Func<double> roll) {
        this.mimic = mimic ?? throw new ArgumentNullException(nameof(mimic));
        this.roll = roll ?? throw new ArgumentNullException(nameof(roll));
    }

    /// <summary>The ability whose essence drops for a kill of the monster with <c>Monsters.json</c> id <paramref name="monsterId"/>, or null for no drop. Draws exactly one random value when a drop is possible.</summary>
    public MimicAbility? Roll(int monsterId, bool raidWorld) {
        if (!mimic.TryGetForMonster(monsterId, out var ability)) {
            return null;
        }

        if (ability.RequiresRaid && !raidWorld) {
            return null;
        }

        return roll() < ability.DropChance ? ability : null;
    }

    /// <summary>True when a monster killed by a player also leaves an Essence Vial. Draws one random value, none when the drop is switched off (<c>essenceVialDropChance</c> 0).</summary>
    public bool RollVial() {
        var chance = mimic.EssenceVialDropChance;
        return chance > 0 && roll() < chance;
    }
}

/// <summary>Remembers which ground items are essence orbs (dropped by a monster), as opposed to essences a player put on the ground: only orbs must be captured. Bounded; the oldest are forgotten first.</summary>
public sealed class EssenceOrbRegistry {
    public const int DefaultCapacity = 4096;

    private readonly int capacity;
    private readonly Queue<long> order = new();
    private readonly HashSet<long> orbs = [];

    public EssenceOrbRegistry(int capacity = DefaultCapacity) {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        this.capacity = capacity;
    }

    public int Count => orbs.Count;

    public void Mark(long itemUid) {
        if (!orbs.Add(itemUid)) {
            return;
        }

        order.Enqueue(itemUid);
        while (order.Count > capacity) {
            orbs.Remove(order.Dequeue());
        }
    }

    public bool IsOrb(long itemUid) => orbs.Contains(itemUid);

    /// <summary>Forgets an orb that was captured; false when it was not one.</summary>
    public bool Release(long itemUid) => orbs.Remove(itemUid);
}

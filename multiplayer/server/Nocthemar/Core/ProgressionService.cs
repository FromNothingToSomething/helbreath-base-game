using Server.Nocthemar.Data;

namespace Server.Nocthemar.Core;

/// <summary>Outcome of <see cref="ProgressionService.GrantXp"/>.</summary>
/// <param name="State">New state (mana refilled to the base maximum when at least one level was gained).</param>
/// <param name="LevelsGained">Number of levels gained by this grant.</param>
public readonly record struct XpGrantResult(NocState State, int LevelsGained);

/// <summary>
/// Pure XP and level logic over <see cref="NocState"/>; holds no per-player state, so one instance is shared by every world.
/// Mana values here use the base maximum of the level: the owning player re-clamps with its <see cref="StatPipeline"/> (modifiers).
/// </summary>
public sealed class ProgressionService {
    private readonly ProgressionConfig config;

    public ProgressionService(ProgressionConfig config) {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Makes a loaded (possibly hand-edited or corrupt) state valid: level clamped to [1, max], negative XP dropped,
    /// XP at or above the level threshold converted into levels, no XP kept at max level, mana clamped to [0, the level's base maximum].
    /// That maximum ignores the race and other modifiers: a caller that has them must clamp to its own maximum instead (see <c>GameWorldPlayer.ApplyPersistedState</c>).
    /// A null state yields a fresh level-1 character with full mana. Reserved ids (race, class, faction) are preserved.
    /// </summary>
    public NocState Normalize(NocState? loaded) {
        if (loaded is null) {
            return NocState.CreateDefault(config.MaxManaAt(1));
        }

        var level = config.ClampLevel(loaded.Level);
        var xp = Math.Max(0L, loaded.Xp);
        (level, xp, _) = Advance(level, xp);
        var mana = Math.Clamp(loaded.Mana, 0, config.MaxManaAt(level));
        return loaded with { V = NocState.CurrentV, Level = level, Xp = xp, Mana = mana };
    }

    /// <summary>
    /// Adds <paramref name="amount"/> XP and chains as many level-ups as it pays for. Non-positive amounts change nothing.
    /// At max level XP is discarded. Overflow saturates instead of wrapping. Mana is refilled on level-up.
    /// </summary>
    public XpGrantResult GrantXp(NocState state, long amount) {
        ArgumentNullException.ThrowIfNull(state);
        if (amount <= 0) {
            return new XpGrantResult(state, 0);
        }

        var level = config.ClampLevel(state.Level);
        var xp = Math.Max(0L, state.Xp);
        xp = amount > long.MaxValue - xp ? long.MaxValue : xp + amount;
        var startLevel = level;
        (level, xp, var gained) = Advance(level, xp);
        var mana = gained > 0 ? config.MaxManaAt(level) : Math.Clamp(state.Mana, 0, config.MaxManaAt(level));
        return new XpGrantResult(state with { Level = level, Xp = xp, Mana = mana }, level - startLevel);
    }

    private (int Level, long Xp, int Gained) Advance(int level, long xp) {
        var gained = 0;
        while (config.TryGetXpToNext(level, out var needed) && xp >= needed) {
            xp -= needed;
            level++;
            gained++;
        }

        if (level >= config.MaxLevel) {
            xp = 0;
        }

        return (level, xp, gained);
    }
}

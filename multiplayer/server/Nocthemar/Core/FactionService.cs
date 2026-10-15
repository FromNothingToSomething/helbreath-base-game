using Server.Nocthemar.Data;

namespace Server.Nocthemar.Core;

/// <summary>Outcome of a faction request: the new state on success, otherwise the reason shown to the player (state unchanged).</summary>
public sealed record FactionResult(bool Ok, NocState State, string? Refusal) {
    public static FactionResult Accepted(NocState state) => new(true, state, null);

    public static FactionResult Refused(NocState state, string reason) => new(false, state, reason);
}

/// <summary>
/// Faction rules (module 5): joining one (once, from a minimum level) and the perks that follow from the character's standing with it.
/// Pure like <see cref="ReputationService"/>: it takes a <see cref="NocState"/> and returns a new one.
/// </summary>
public sealed class FactionService {
    public const string UnknownFaction = "Unknown faction.";
    public const string AlreadyJoined = "You already belong to a faction.";
    public const string NoMasterInRange = "No faction master in range.";

    private readonly ReputationService reputation;

    public FactionService(ReputationService reputation) {
        this.reputation = reputation ?? throw new ArgumentNullException(nameof(reputation));
    }

    public FactionConfig Config => reputation.Config;

    /// <summary>Owners of every faction's modifiers: removing them all clears the perks of whichever faction was joined.</summary>
    public IReadOnlyList<int> SourceIds => [.. Config.Factions.Select(f => f.SourceId)];

    public static string LevelTooLow(int level) => $"You must reach level {level} to join a faction.";

    /// <summary>
    /// Joins a faction. Refusals, in order: unknown faction, already in a faction (the choice is final), level below <c>reputation.joinLevel</c>.
    /// Joining changes nothing else: the reputations earned so far (none before the first affiliation) are kept.
    /// </summary>
    public FactionResult Join(NocState state, int factionId) {
        ArgumentNullException.ThrowIfNull(state);
        if (!Config.Contains(factionId)) {
            return FactionResult.Refused(state, UnknownFaction);
        }

        if (state.FactionId is not null) {
            return FactionResult.Refused(state, AlreadyJoined);
        }

        if (state.Level < Config.Rules.JoinLevel) {
            return FactionResult.Refused(state, LevelTooLow(Config.Rules.JoinLevel));
        }

        return FactionResult.Accepted(state with { FactionId = factionId });
    }

    /// <summary>
    /// The modifiers a character has from its faction: the perks of its own faction up to its tier there (Friendly, then Honored, then Exalted).
    /// Reputation with the other factions gives nothing; no faction, no perk.
    /// </summary>
    public StatModifier[] PerksOf(NocState state) {
        ArgumentNullException.ThrowIfNull(state);
        if (state.FactionId is not int factionId || !Config.TryGet(factionId, out var faction)) {
            return [];
        }

        return faction.ModifiersAt(reputation.TierOf(state, factionId));
    }
}

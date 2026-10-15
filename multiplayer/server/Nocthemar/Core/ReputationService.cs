using Server.Nocthemar.Data;

namespace Server.Nocthemar.Core;

/// <summary>One reputation value that moved: the faction and the value before and after (already clamped).</summary>
public readonly record struct ReputationChange(int FactionId, int Before, int After) {
    public int Delta => After - Before;
}

/// <summary>The new state and what moved; <see cref="State"/> is the very same instance as the input when nothing changed.</summary>
public sealed record ReputationResult(NocState State, IReadOnlyList<ReputationChange> Changes);

/// <summary>
/// Reputation rules (module 5): reading a value, moving it within the range, what a kill does and cleaning a loaded save.
/// Pure: every method takes a <see cref="NocState"/> and returns a new one; the player owns the state and decides when to persist and send it.
/// </summary>
public sealed class ReputationService {
    private readonly FactionConfig factions;

    public ReputationService(FactionConfig factions) {
        this.factions = factions ?? throw new ArgumentNullException(nameof(factions));
    }

    public FactionConfig Config => factions;

    public ReputationRules Rules => factions.Rules;

    /// <summary>Reputation with <paramref name="factionId"/>; 0 when none was earned yet or the faction is unknown.</summary>
    public int ValueOf(NocState state, int factionId) {
        ArgumentNullException.ThrowIfNull(state);
        foreach (var entry in state.Reputations ?? []) {
            if (entry.Id == factionId) {
                return entry.Value;
            }
        }

        return 0;
    }

    public ReputationTier TierOf(NocState state, int factionId) => Rules.TierOf(ValueOf(state, factionId));

    /// <summary>
    /// Adds <paramref name="delta"/> to one faction, clamped to the range. A positive delta is first multiplied by <paramref name="gainMult"/>
    /// (the <see cref="StatId.ReputationGainMult"/> stat); a loss is never reduced. An unknown faction or a null move changes nothing.
    /// </summary>
    public ReputationResult Adjust(NocState state, int factionId, int delta, double gainMult = 1.0) =>
        Move(state, new[] { (factionId, Scaled(delta, gainMult)) });

    /// <summary>
    /// What killing a monster of faction <paramref name="monsterFactionId"/> does: that faction loses <c>repOnKill x victimRatio</c>
    /// and each of its rivals gains <c>repOnKill x rivalRatio</c> (times <paramref name="gainMult"/>). <paramref name="repOnKill"/> null means the config's <c>killBase</c>.
    /// A character without a faction earns and loses nothing: reputation only starts counting once it has joined one.
    /// </summary>
    public ReputationResult ApplyKill(NocState state, int monsterFactionId, int? repOnKill = null, double gainMult = 1.0) {
        ArgumentNullException.ThrowIfNull(state);
        if (state.FactionId is null || !factions.TryGet(monsterFactionId, out var victim)) {
            return Unchanged(state);
        }

        var basis = repOnKill ?? Rules.KillBase;
        if (basis <= 0) {
            return Unchanged(state);
        }

        var deltas = new List<(int, int)>();
        var loss = Share(basis, Rules.VictimRatio);
        if (loss > 0) {
            deltas.Add((victim.Id, -loss));
        }

        var gain = Scaled(Share(basis, Rules.RivalRatio), gainMult);
        if (gain > 0) {
            foreach (var rival in victim.Rivals) {
                deltas.Add((rival, gain));
            }
        }

        return Move(state, deltas);
    }

    /// <summary>
    /// Makes a loaded block valid: unknown factions (reputation or affiliation) are dropped, values are clamped to the range, a faction listed twice keeps its last value,
    /// zero values are left out and the list is sorted. Never refuses; every change is a warning.
    /// </summary>
    public NocState Sanitize(NocState state, out IReadOnlyList<string> warnings) {
        ArgumentNullException.ThrowIfNull(state);
        var notes = new List<string>();
        warnings = notes;
        var values = new SortedDictionary<int, int>();
        foreach (var entry in state.Reputations ?? []) {
            if (!factions.Contains(entry.Id)) {
                notes.Add($"reputation of faction {entry.Id} dropped: not in the catalog");
                continue;
            }

            var value = Rules.Clamp(entry.Value);
            if (value != entry.Value) {
                notes.Add($"reputation of faction {entry.Id} set to {value}: out of range");
            }

            if (values.ContainsKey(entry.Id)) {
                notes.Add($"reputation of faction {entry.Id} listed twice: kept the last value");
            }

            values[entry.Id] = value;
        }

        var factionId = state.FactionId;
        if (factionId is int joined && !factions.Contains(joined)) {
            notes.Add($"faction {joined} dropped: not in the catalog");
            factionId = null;
        }

        return WithValues(state, values) with { FactionId = factionId };
    }

    private ReputationResult Move(NocState state, IEnumerable<(int FactionId, int Delta)> deltas) {
        ArgumentNullException.ThrowIfNull(state);
        var values = ValuesOf(state);
        var changes = new List<ReputationChange>();
        foreach (var (factionId, delta) in deltas) {
            if (delta == 0 || !factions.Contains(factionId)) {
                continue;
            }

            values.TryGetValue(factionId, out var before);
            var after = (int)Math.Clamp((long)before + delta, Rules.Min, Rules.Max);
            if (after == before) {
                continue;
            }

            values[factionId] = after;
            changes.Add(new ReputationChange(factionId, before, after));
        }

        return changes.Count == 0 ? Unchanged(state) : new ReputationResult(WithValues(state, values), changes);
    }

    private static ReputationResult Unchanged(NocState state) => new(state, []);

    private static int Scaled(int delta, double gainMult) =>
        delta > 0 ? (int)Math.Round(delta * Math.Clamp(gainMult, 0.0, 100.0), MidpointRounding.AwayFromZero) : delta;

    /// <summary>Part of a kill's reputation: rounded to the nearest whole, at least 1 when the ratio is above 0.</summary>
    private static int Share(int basis, double ratio) =>
        ratio <= 0 ? 0 : Math.Max(1, (int)Math.Round(basis * ratio, MidpointRounding.AwayFromZero));

    private static SortedDictionary<int, int> ValuesOf(NocState state) {
        var values = new SortedDictionary<int, int>();
        foreach (var entry in state.Reputations ?? []) {
            values[entry.Id] = entry.Value;
        }

        return values;
    }

    private static NocState WithValues(NocState state, SortedDictionary<int, int> values) {
        var entries = values.Where(pair => pair.Value != 0).Select(pair => new FactionRep(pair.Key, pair.Value)).ToArray();
        return state with { Reputations = entries.Length == 0 ? null : entries };
    }
}

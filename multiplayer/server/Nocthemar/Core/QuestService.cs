using Server.Nocthemar.Data;

namespace Server.Nocthemar.Core;

/// <summary>
/// Outcome of a quest request: the new state on success, otherwise the reason shown to the player (state unchanged).
/// <see cref="Changes"/> lists the reputation a turn-in moved (empty otherwise).
/// </summary>
public sealed record QuestResult(bool Ok, NocState State, string? Refusal, QuestDefinition? Quest, IReadOnlyList<ReputationChange> Changes) {
    public static QuestResult Accepted(NocState state, QuestDefinition quest, IReadOnlyList<ReputationChange>? changes = null) => new(true, state, null, quest, changes ?? []);

    public static QuestResult Refused(NocState state, string reason, QuestDefinition? quest = null) => new(false, state, reason, quest, []);
}

/// <summary>
/// Quest rules (module 5): accepting, abandoning, progress and turning in. Pure like <see cref="FactionService"/>: it takes a <see cref="NocState"/> and returns a new one.
/// What touches the bag (taking delivered items, giving reward items, the room for them) and the experience reward belong to the player, which calls this service around them.
/// </summary>
public sealed class QuestService {
    public const string UnknownQuest = "Unknown quest.";
    public const string NoFaction = "You must join a faction first.";
    public const string OtherFaction = "This quest belongs to another faction.";
    public const string AlreadyDone = "You already completed this quest.";
    public const string AlreadyActive = "You already have this quest.";
    public const string NotActive = "You do not have this quest.";
    public const string NotComplete = "The quest is not complete.";
    public const string BagFull = "Your bag is too full to carry the reward.";
    public const string NoMasterInRange = "No faction master in range.";

    private readonly QuestConfig quests;
    private readonly ReputationService reputation;

    public QuestService(QuestConfig quests, ReputationService reputation) {
        this.quests = quests ?? throw new ArgumentNullException(nameof(quests));
        this.reputation = reputation ?? throw new ArgumentNullException(nameof(reputation));
    }

    public QuestConfig Config => quests;

    public static string TierTooLow(ReputationTier tier) => $"You need {tier} standing with your faction for this quest.";

    public static string TooManyQuests(int max) => $"You cannot carry more than {max} quests.";

    public bool IsDone(NocState state, int questId) => state.QuestsDone is { } done && Array.IndexOf(done, questId) >= 0;

    /// <summary>The progress entry of an active quest, or null.</summary>
    public QuestEntry? ActiveEntry(NocState state, int questId) => state.Quests?.FirstOrDefault(e => e.Id == questId);

    /// <summary>True when the active quest has reached its count.</summary>
    public bool IsComplete(NocState state, int questId) =>
        quests.TryGet(questId, out var quest) && ActiveEntry(state, questId) is { } entry && entry.Progress >= quest.Count;

    /// <summary>
    /// Accepts a quest. Refusals, in order: unknown quest, no faction, quest of another faction, already completed, already active,
    /// standing with the faction below the quest's tier, too many active quests. The progress starts at 0.
    /// </summary>
    public QuestResult Accept(NocState state, int questId) {
        ArgumentNullException.ThrowIfNull(state);
        if (!quests.TryGet(questId, out var quest)) {
            return QuestResult.Refused(state, UnknownQuest);
        }

        if (state.FactionId is not int factionId) {
            return QuestResult.Refused(state, NoFaction, quest);
        }

        if (factionId != quest.FactionId) {
            return QuestResult.Refused(state, OtherFaction, quest);
        }

        if (IsDone(state, questId)) {
            return QuestResult.Refused(state, AlreadyDone, quest);
        }

        if (ActiveEntry(state, questId) is not null) {
            return QuestResult.Refused(state, AlreadyActive, quest);
        }

        if (reputation.TierOf(state, factionId) < quest.MinTier) {
            return QuestResult.Refused(state, TierTooLow(quest.MinTier), quest);
        }

        if ((state.Quests?.Length ?? 0) >= quests.MaxActive) {
            return QuestResult.Refused(state, TooManyQuests(quests.MaxActive), quest);
        }

        var active = (state.Quests ?? []).Append(new QuestEntry(questId, 0)).OrderBy(e => e.Id).ToArray();
        return QuestResult.Accepted(state with { Quests = active }, quest);
    }

    /// <summary>Gives a quest up: it can be accepted again later. Refused when it is not active.</summary>
    public QuestResult Abandon(NocState state, int questId) {
        ArgumentNullException.ThrowIfNull(state);
        if (!quests.TryGet(questId, out var quest)) {
            return QuestResult.Refused(state, UnknownQuest);
        }

        if (ActiveEntry(state, questId) is null) {
            return QuestResult.Refused(state, NotActive, quest);
        }

        return QuestResult.Accepted(state with { Quests = WithoutQuest(state.Quests, questId) }, quest);
    }

    /// <summary>
    /// Counts one kill of monster <paramref name="monsterId"/> for every active kill quest that asks for it (progress stops at the quest's count).
    /// Returns the very same instance when nothing moved.
    /// </summary>
    public NocState RecordKill(NocState state, int monsterId) {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Quests is not { Length: > 0 } active) {
            return state;
        }

        QuestEntry[]? changed = null;
        for (var i = 0; i < active.Length; i++) {
            if (!quests.TryGet(active[i].Id, out var quest) || quest.Kind != QuestKind.Kill || quest.TargetId != monsterId || active[i].Progress >= quest.Count) {
                continue;
            }

            changed ??= (QuestEntry[])active.Clone();
            changed[i] = active[i] with { Progress = active[i].Progress + 1 };
        }

        return changed is null ? state : state with { Quests = changed };
    }

    /// <summary>
    /// Sets the progress of every active deliver quest to what the bag holds (capped at the quest's count); <paramref name="countInBag"/> gives the quantity of an item id.
    /// Returns the very same instance when nothing moved.
    /// </summary>
    public NocState SyncDeliver(NocState state, Func<int, int> countInBag) {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(countInBag);
        if (state.Quests is not { Length: > 0 } active) {
            return state;
        }

        QuestEntry[]? changed = null;
        for (var i = 0; i < active.Length; i++) {
            if (!quests.TryGet(active[i].Id, out var quest) || quest.Kind != QuestKind.Deliver) {
                continue;
            }

            var progress = Math.Clamp(countInBag(quest.TargetId), 0, quest.Count);
            if (progress == active[i].Progress) {
                continue;
            }

            changed ??= (QuestEntry[])active.Clone();
            changed[i] = active[i] with { Progress = progress };
        }

        return changed is null ? state : state with { Quests = changed };
    }

    /// <summary>
    /// What stands between an active quest and its reward, ignoring the bag: unknown quest, quest not active, objective not reached. Null when the quest can be turned in.
    /// </summary>
    public string? CheckTurnIn(NocState state, int questId) {
        ArgumentNullException.ThrowIfNull(state);
        if (!quests.TryGet(questId, out _)) {
            return UnknownQuest;
        }

        if (ActiveEntry(state, questId) is null) {
            return NotActive;
        }

        return IsComplete(state, questId) ? null : NotComplete;
    }

    /// <summary>
    /// Turns a completed quest in: it leaves the active list and is recorded as done, and the reputation reward is added (scaled by <paramref name="gainMult"/> like any gain).
    /// The caller has already checked the bag, taken the delivered items and will give the reward items and experience. Refusals are those of <see cref="CheckTurnIn"/>.
    /// </summary>
    public QuestResult TurnIn(NocState state, int questId, double gainMult = 1.0) {
        ArgumentNullException.ThrowIfNull(state);
        var refusal = CheckTurnIn(state, questId);
        quests.TryGet(questId, out var quest);
        if (refusal is not null) {
            return QuestResult.Refused(state, refusal, quest);
        }

        var done = (state.QuestsDone ?? []).Append(questId).OrderBy(id => id).ToArray();
        var next = state with { Quests = WithoutQuest(state.Quests, questId), QuestsDone = done };
        var moved = reputation.Adjust(next, quest!.FactionId, quest.RewardRep, gainMult);
        return QuestResult.Accepted(moved.State, quest, moved.Changes);
    }

    /// <summary>
    /// Makes a loaded block valid: unknown quest ids are dropped, a quest listed twice keeps its first entry, an active quest that is also done is dropped,
    /// progress is clamped to [0, count], quests beyond the active limit are dropped, lists are sorted and an empty list becomes null. Never refuses; every change is a warning.
    /// </summary>
    public NocState Sanitize(NocState state, out IReadOnlyList<string> warnings) {
        ArgumentNullException.ThrowIfNull(state);
        var notes = new List<string>();
        warnings = notes;
        var done = new SortedSet<int>();
        foreach (var id in state.QuestsDone ?? []) {
            if (!quests.Contains(id)) {
                notes.Add($"completed quest {id} dropped: not in the catalog");
            } else if (!done.Add(id)) {
                notes.Add($"completed quest {id} listed twice");
            }
        }

        var active = new SortedDictionary<int, int>();
        foreach (var entry in state.Quests ?? []) {
            if (!quests.TryGet(entry.Id, out var quest)) {
                notes.Add($"quest {entry.Id} dropped: not in the catalog");
                continue;
            }

            if (done.Contains(entry.Id)) {
                notes.Add($"quest {entry.Id} dropped: already completed");
                continue;
            }

            if (active.ContainsKey(entry.Id)) {
                notes.Add($"quest {entry.Id} listed twice: kept the first entry");
                continue;
            }

            var progress = Math.Clamp(entry.Progress, 0, quest.Count);
            if (progress != entry.Progress) {
                notes.Add($"quest {entry.Id} progress set to {progress}: out of range");
            }

            active[entry.Id] = progress;
        }

        while (active.Count > quests.MaxActive) {
            var last = active.Keys.Last();
            active.Remove(last);
            notes.Add($"quest {last} dropped: more than {quests.MaxActive} active quests");
        }

        var cleanQuests = active.Count == 0 ? null : active.Select(p => new QuestEntry(p.Key, p.Value)).ToArray();
        var cleanDone = done.Count == 0 ? null : done.ToArray();
        // Records compare arrays by reference: hand back the very same state when the block was already clean.
        if (notes.Count == 0 && SameItems(state.Quests, cleanQuests) && SameItems(state.QuestsDone, cleanDone)) {
            return state;
        }

        return state with { Quests = cleanQuests, QuestsDone = cleanDone };
    }

    private static bool SameItems<T>(T[]? a, T[]? b) =>
        a is null || b is null ? a is null && b is null : a.SequenceEqual(b);

    private static QuestEntry[]? WithoutQuest(QuestEntry[]? active, int questId) {
        var rest = (active ?? []).Where(e => e.Id != questId).ToArray();
        return rest.Length == 0 ? null : rest;
    }
}

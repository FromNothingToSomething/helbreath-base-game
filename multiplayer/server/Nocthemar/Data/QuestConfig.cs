using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Server.Nocthemar.Data;

/// <summary>What a quest asks for.</summary>
public enum QuestKind : byte {
    /// <summary>Kill <see cref="QuestDefinition.Count"/> monsters of one catalog id (the killer only: the fatal blow).</summary>
    Kill = 0,

    /// <summary>Hand over <see cref="QuestDefinition.Count"/> units of an item, taken from the bag when the quest is turned in.</summary>
    Deliver = 1,
}

public sealed record QuestItemFile(int ItemId = -1, int Qty = 0);

public sealed record QuestRewardsFile(long? Xp = null, int? Rep = null, List<QuestItemFile>? Items = null);

public sealed record QuestRulesFile(int? MaxActive = null, int? BagSlots = null);

public sealed record QuestFile(
    int Id = -1,
    string? Key = null,
    string? Name = null,
    int FactionId = -1,
    string? MinTier = null,
    string? Kind = null,
    int? MonsterId = null,
    int? ItemId = null,
    int? Count = null,
    QuestRewardsFile? Rewards = null,
    string? Desc = null);

public sealed record QuestsFile(int SchemaVersion = 0, QuestRulesFile? Rules = null, List<QuestFile>? Quests = null);

/// <summary>An item given as a reward.</summary>
public readonly record struct QuestItem(int ItemId, int Quantity);

/// <summary>One validated quest of <c>Quests.json</c>.</summary>
public sealed class QuestDefinition {
    internal QuestDefinition(
        int id, string key, string name, int factionId, ReputationTier minTier, QuestKind kind, int targetId, int count,
        long rewardXp, int rewardRep, QuestItem[] rewardItems, string desc) {
        Id = id;
        Key = key;
        Name = name;
        FactionId = factionId;
        MinTier = minTier;
        Kind = kind;
        TargetId = targetId;
        Count = count;
        RewardXp = rewardXp;
        RewardRep = rewardRep;
        RewardItems = rewardItems;
        Desc = desc;
    }

    public int Id { get; }
    public string Key { get; }
    public string Name { get; }

    /// <summary>The faction whose master gives and takes back the quest; only its members can do it.</summary>
    public int FactionId { get; }

    /// <summary>Lowest standing with the faction that opens the quest (Neutral or better).</summary>
    public ReputationTier MinTier { get; }

    public QuestKind Kind { get; }

    /// <summary>Monster catalog id for a kill quest, item id for a deliver quest.</summary>
    public int TargetId { get; }

    /// <summary>Kills to make or units to hand over.</summary>
    public int Count { get; }

    public long RewardXp { get; }

    /// <summary>Reputation with <see cref="FactionId"/> (multiplied by the character's reputation gain like any gain).</summary>
    public int RewardRep { get; }

    public IReadOnlyList<QuestItem> RewardItems { get; }

    public string Desc { get; }
}

/// <summary>
/// <c>Config/Nocthemar/Quests.json</c>: the quests of the faction masters and the quest rules.
/// Fail-fast: <see cref="Parse"/> throws one <see cref="InvalidOperationException"/> listing every problem. Monsters and items are checked by
/// <see cref="ValidateMonsters"/> and <see cref="ValidateItems"/> once their catalogs are loaded (as <see cref="MimicConfig"/> does).
/// </summary>
public sealed class QuestConfig {
    public const int SupportedSchemaVersion = 1;
    public const int MaxQuests = 64;
    public const int MaxRewardItems = 4;
    public const int MaxCount = 1000;
    public const long MaxRewardXp = 1_000_000;
    public const int MaxRewardItemQuantity = 100;

    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{1,31}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly QuestDefinition[] quests;

    private QuestConfig(QuestDefinition[] quests, int maxActive, int bagSlots) {
        this.quests = quests;
        MaxActive = maxActive;
        BagSlots = bagSlots;
    }

    /// <summary>Quests a character can carry at the same time.</summary>
    public int MaxActive { get; }

    /// <summary>Bag rows a quest turn-in may leave: a reward that would not fit is refused ("bag full"). Applies to quest rewards only.</summary>
    public int BagSlots { get; }

    /// <summary>Ordered by id (ids are contiguous from 0).</summary>
    public IReadOnlyList<QuestDefinition> Quests => quests;

    public int Count => quests.Length;

    public bool Contains(int id) => id >= 0 && id < quests.Length;

    public bool TryGet(int id, out QuestDefinition quest) {
        if (Contains(id)) {
            quest = quests[id];
            return true;
        }

        quest = null!;
        return false;
    }

    /// <summary>Loads <c>Config/Nocthemar/Quests.json</c> relative to the process working directory.</summary>
    public static async Task<QuestConfig> LoadAsync(FactionConfig factions, string? path = null) {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), "Config", "Nocthemar", "Quests.json");
        var json = await File.ReadAllTextAsync(path);
        try {
            return Parse(json, factions);
        } catch (InvalidOperationException ex) {
            throw new InvalidOperationException($"{ex.Message} (file: {path})", ex);
        }
    }

    /// <summary>Parses and validates against the factions; throws <see cref="InvalidOperationException"/> listing every problem found.</summary>
    public static QuestConfig Parse(string json, FactionConfig factions) {
        ArgumentNullException.ThrowIfNull(factions);
        QuestsFile? file;
        try {
            file = JsonSerializer.Deserialize<QuestsFile>(json, JsonOptions);
        } catch (JsonException ex) {
            throw new InvalidOperationException($"Invalid Quests.json: {ex.Message}", ex);
        }

        if (file is null) {
            throw new InvalidOperationException("Invalid Quests.json: empty document.");
        }

        var errors = new List<string>();
        if (file.SchemaVersion != SupportedSchemaVersion) {
            errors.Add($"schemaVersion must be {SupportedSchemaVersion} (got {file.SchemaVersion})");
        }

        var maxActive = file.Rules?.MaxActive ?? 0;
        var bagSlots = file.Rules?.BagSlots ?? 0;
        if (maxActive < 1 || maxActive > 10) {
            errors.Add($"rules.maxActive must be in [1, 10] (got {file.Rules?.MaxActive?.ToString() ?? "nothing"})");
        }

        if (bagSlots < 1 || bagSlots > 200) {
            errors.Add($"rules.bagSlots must be in [1, 200] (got {file.Rules?.BagSlots?.ToString() ?? "nothing"})");
        }

        var parsed = new List<QuestDefinition>();
        if (file.Quests is null || file.Quests.Count == 0) {
            errors.Add("quests must contain at least one quest");
        } else if (file.Quests.Count > MaxQuests) {
            errors.Add($"quests has {file.Quests.Count} entries (max {MaxQuests})");
        } else {
            var keys = new HashSet<string>();
            for (var i = 0; i < file.Quests.Count; i++) {
                var quest = ParseQuest(file.Quests[i], i, keys, factions, errors);
                if (quest is not null) {
                    parsed.Add(quest);
                }
            }

            CheckRewardsRiseWithTier(parsed, errors);
        }

        if (errors.Count > 0) {
            throw new InvalidOperationException($"Invalid Quests.json: {string.Join("; ", errors)}.");
        }

        return new QuestConfig([.. parsed], maxActive, bagSlots);
    }

    /// <summary>
    /// Every kill quest must name an existing monster that is not of the quest's own faction (killing it would cost the character reputation with the giver).
    /// <paramref name="monsterFactions"/> maps each monster catalog id to its faction (null = none).
    /// </summary>
    public void ValidateMonsters(IReadOnlyDictionary<int, int?> monsterFactions) {
        ArgumentNullException.ThrowIfNull(monsterFactions);
        foreach (var quest in quests.Where(q => q.Kind == QuestKind.Kill)) {
            if (!monsterFactions.TryGetValue(quest.TargetId, out var monsterFaction)) {
                throw new InvalidOperationException($"Quests.json quest {quest.Id} ({quest.Key}) references unknown monsterId {quest.TargetId}.");
            }

            if (monsterFaction == quest.FactionId) {
                throw new InvalidOperationException($"Quests.json quest {quest.Id} ({quest.Key}) asks to kill monster {quest.TargetId} of its own faction {quest.FactionId}.");
            }
        }
    }

    /// <summary>Every delivered and rewarded item must exist in <c>Items.json</c>.</summary>
    public void ValidateItems(IReadOnlyCollection<int> itemIds) {
        ArgumentNullException.ThrowIfNull(itemIds);
        var known = itemIds as ISet<int> ?? new HashSet<int>(itemIds);
        foreach (var quest in quests) {
            if (quest.Kind == QuestKind.Deliver && !known.Contains(quest.TargetId)) {
                throw new InvalidOperationException($"Quests.json quest {quest.Id} ({quest.Key}) references unknown itemId {quest.TargetId}.");
            }

            foreach (var item in quest.RewardItems) {
                if (!known.Contains(item.ItemId)) {
                    throw new InvalidOperationException($"Quests.json quest {quest.Id} ({quest.Key}) rewards unknown itemId {item.ItemId}.");
                }
            }
        }
    }

    private static QuestDefinition? ParseQuest(QuestFile? file, int index, HashSet<string> keys, FactionConfig factions, List<string> errors) {
        var before = errors.Count;
        var where = $"quests[{index}]";
        if (file is null) {
            errors.Add($"{where} is null");
            return null;
        }

        if (file.Id != index) {
            errors.Add($"{where}.id must be {index} (ids are contiguous from 0, got {file.Id})");
        }

        if (file.Key is null || !KeyPattern.IsMatch(file.Key)) {
            errors.Add($"{where}.key '{file.Key}' must match {KeyPattern}");
        } else if (!keys.Add(file.Key)) {
            errors.Add($"{where}.key '{file.Key}' is used twice");
        }

        if (string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 60) {
            errors.Add($"{where}.name must be 1-60 characters");
        }

        if (!factions.Contains(file.FactionId)) {
            errors.Add($"{where}.factionId {file.FactionId} is not a faction of Factions.json");
        }

        if (!ModifierParser.TryParseEnum<ReputationTier>(file.MinTier, out var minTier) || minTier < ReputationTier.Neutral) {
            errors.Add($"{where}.minTier '{file.MinTier}' must be Neutral, Friendly, Honored or Exalted");
        }

        if (!ModifierParser.TryParseEnum<QuestKind>(file.Kind, out var kind)) {
            errors.Add($"{where}.kind '{file.Kind}' must be {string.Join(" or ", Enum.GetNames<QuestKind>())}");
        }

        var targetId = 0;
        if (kind == QuestKind.Kill) {
            if (file.MonsterId is not int monsterId || monsterId < 0) {
                errors.Add($"{where}.monsterId is required for a kill quest");
            } else {
                targetId = monsterId;
            }

            if (file.ItemId is not null) {
                errors.Add($"{where}.itemId is only for a deliver quest");
            }
        } else {
            if (file.ItemId is not int itemId || itemId < 0) {
                errors.Add($"{where}.itemId is required for a deliver quest");
            } else {
                targetId = itemId;
            }

            if (file.MonsterId is not null) {
                errors.Add($"{where}.monsterId is only for a kill quest");
            }
        }

        var count = file.Count ?? 0;
        if (count < 1 || count > MaxCount) {
            errors.Add($"{where}.count must be in [1, {MaxCount}] (got {file.Count?.ToString() ?? "nothing"})");
        }

        var (rewardXp, rewardRep, rewardItems) = ParseRewards(file.Rewards, where, factions, errors);
        if (errors.Count > before) {
            return null;
        }

        return new QuestDefinition(file.Id, file.Key!, file.Name!, file.FactionId, minTier, kind, targetId, count, rewardXp, rewardRep, rewardItems, file.Desc ?? string.Empty);
    }

    private static (long Xp, int Rep, QuestItem[] Items) ParseRewards(QuestRewardsFile? rewards, string where, FactionConfig factions, List<string> errors) {
        if (rewards is null) {
            errors.Add($"{where}.rewards is missing");
            return (0, 0, []);
        }

        var xp = rewards.Xp ?? 0;
        var rep = rewards.Rep ?? 0;
        if (xp < 0 || xp > MaxRewardXp) {
            errors.Add($"{where}.rewards.xp must be in [0, {MaxRewardXp}] (got {xp})");
        }

        if (rep < 0 || rep > factions.Rules.Max) {
            errors.Add($"{where}.rewards.rep must be in [0, {factions.Rules.Max}] (got {rep})");
        }

        var items = new List<QuestItem>();
        var seen = new HashSet<int>();
        foreach (var item in rewards.Items ?? []) {
            if (item is null || item.ItemId < 0 || item.Qty < 1 || item.Qty > MaxRewardItemQuantity) {
                errors.Add($"{where}.rewards.items needs an itemId and a qty in [1, {MaxRewardItemQuantity}]");
            } else if (!seen.Add(item.ItemId)) {
                errors.Add($"{where}.rewards.items lists item {item.ItemId} twice");
            } else {
                items.Add(new QuestItem(item.ItemId, item.Qty));
            }
        }

        if (items.Count > MaxRewardItems) {
            errors.Add($"{where}.rewards.items has {items.Count} entries (max {MaxRewardItems})");
        }

        if (xp == 0 && rep == 0 && items.Count == 0) {
            errors.Add($"{where}.rewards must give something (xp, rep or items)");
        }

        return (xp, rep, [.. items]);
    }

    /// <summary>Within one faction a quest that needs a better standing never pays less experience than an easier one listed before it.</summary>
    private static void CheckRewardsRiseWithTier(List<QuestDefinition> parsed, List<string> errors) {
        foreach (var group in parsed.GroupBy(q => q.FactionId)) {
            QuestDefinition? previous = null;
            foreach (var quest in group.OrderBy(q => q.MinTier).ThenBy(q => q.Id)) {
                if (previous is not null && quest.MinTier > previous.MinTier && quest.RewardXp < previous.RewardXp) {
                    errors.Add($"quest {quest.Key} ({quest.MinTier}) pays less xp than {previous.Key} ({previous.MinTier}) of the same faction");
                }

                previous = quest;
            }
        }
    }
}

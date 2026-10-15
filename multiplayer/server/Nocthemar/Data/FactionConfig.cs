using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Server.Nocthemar.Core;

namespace Server.Nocthemar.Data;

/// <summary>Standing of a character with one faction, from its reputation value (thresholds in <c>Factions.json</c>).</summary>
public enum ReputationTier : byte {
    Hostile = 0,
    Neutral = 1,
    Friendly = 2,
    Honored = 3,
    Exalted = 4,
}

public sealed record ReputationTiersFile(int? Neutral = null, int? Friendly = null, int? Honored = null, int? Exalted = null);

public sealed record ReputationFile(
    int? Min = null,
    int? Max = null,
    int? JoinLevel = null,
    int? KillBase = null,
    double? VictimRatio = null,
    double? RivalRatio = null,
    ReputationTiersFile? Tiers = null);

public sealed record FactionPerkFile(string? Tier = null, List<RaceModifierFile>? Modifiers = null);

public sealed record FactionFile(
    int Id = -1,
    string? Key = null,
    string? Name = null,
    string? Hq = null,
    string? Symbol = null,
    List<string>? Colors = null,
    List<int>? Rivals = null,
    List<FactionPerkFile>? Perks = null,
    string? Desc = null);

public sealed record FactionsFile(int SchemaVersion = 0, ReputationFile? Reputation = null, List<FactionFile>? Factions = null);

/// <summary>Validated reputation rules: value range, tier thresholds, the level needed to join and how a kill moves reputation.</summary>
public sealed class ReputationRules {
    internal ReputationRules(int min, int max, int joinLevel, int killBase, double victimRatio, double rivalRatio, int neutral, int friendly, int honored, int exalted) {
        Min = min;
        Max = max;
        JoinLevel = joinLevel;
        KillBase = killBase;
        VictimRatio = victimRatio;
        RivalRatio = rivalRatio;
        Neutral = neutral;
        Friendly = friendly;
        Honored = honored;
        Exalted = exalted;
    }

    /// <summary>Lowest reputation value (negative).</summary>
    public int Min { get; }

    /// <summary>Highest reputation value (positive).</summary>
    public int Max { get; }

    /// <summary>Character level needed to join a faction.</summary>
    public int JoinLevel { get; }

    /// <summary>Reputation of a kill when a monster does not say (<c>repOnKill</c> in Monsters.json).</summary>
    public int KillBase { get; }

    /// <summary>Share of a kill's reputation that the monster's own faction loses.</summary>
    public double VictimRatio { get; }

    /// <summary>Share of a kill's reputation that each rival of the monster's faction gains.</summary>
    public double RivalRatio { get; }

    /// <summary>First value of each tier; Hostile runs from <see cref="Min"/> up to just below <see cref="Neutral"/>.</summary>
    public int Neutral { get; }

    public int Friendly { get; }
    public int Honored { get; }
    public int Exalted { get; }

    public int Clamp(int value) => Math.Clamp(value, Min, Max);

    public ReputationTier TierOf(int value) =>
        value >= Exalted ? ReputationTier.Exalted
        : value >= Honored ? ReputationTier.Honored
        : value >= Friendly ? ReputationTier.Friendly
        : value >= Neutral ? ReputationTier.Neutral
        : ReputationTier.Hostile;

    /// <summary>Lowest value of <paramref name="tier"/>.</summary>
    public int FloorOf(ReputationTier tier) => tier switch {
        ReputationTier.Exalted => Exalted,
        ReputationTier.Honored => Honored,
        ReputationTier.Friendly => Friendly,
        ReputationTier.Neutral => Neutral,
        _ => Min,
    };
}

/// <summary>One validated faction.</summary>
public sealed class FactionDefinition {
    private readonly StatModifier[][] perksByTier;

    internal FactionDefinition(int id, string key, string name, string hq, string symbol, string[] colors, int[] rivals, StatModifier[][] perksByTier, string desc) {
        Id = id;
        Key = key;
        Name = name;
        Hq = hq;
        Symbol = symbol;
        Colors = colors;
        Rivals = rivals;
        this.perksByTier = perksByTier;
        Desc = desc;
    }

    public int Id { get; }
    public string Key { get; }
    public string Name { get; }

    /// <summary>Headquarters (a zone name, documentation).</summary>
    public string Hq { get; }

    /// <summary>Description of the emblem (documentation; the icon itself is art, module 7).</summary>
    public string Symbol { get; }

    /// <summary>Hex colors <c>#rrggbb</c>; the first one is the name plate color.</summary>
    public IReadOnlyList<string> Colors { get; }

    /// <summary>Ids of the opposed factions (symmetric).</summary>
    public IReadOnlyList<int> Rivals { get; }

    public string Desc { get; }

    /// <summary>Owner of the faction's modifiers: one call to <c>StatPipeline.RemoveSource</c> clears them all.</summary>
    public int SourceId => ModifierSources.Faction(Id);

    /// <summary>The perks a member has at <paramref name="tier"/>: those of every tier up to and including it (Friendly, then Honored, then Exalted).</summary>
    public StatModifier[] ModifiersAt(ReputationTier tier) {
        var all = new List<StatModifier>();
        for (var t = (int)ReputationTier.Friendly; t <= (int)tier && t < perksByTier.Length; t++) {
            all.AddRange(perksByTier[t]);
        }

        return [.. all];
    }
}

/// <summary>
/// <c>Config/Nocthemar/Factions.json</c>: the factions, their rivals, their perks by reputation tier and the reputation rules.
/// Fail-fast like <see cref="ClassConfig"/>: <see cref="Parse"/> throws one <see cref="InvalidOperationException"/> listing every problem.
/// </summary>
public sealed class FactionConfig {
    public const int SupportedSchemaVersion = 1;
    public const int MaxFactions = 16;
    public const int MaxPerkModifiers = 4;

    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{1,31}$", RegexOptions.Compiled);
    private static readonly Regex ColorPattern = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly FactionDefinition[] factions;

    private FactionConfig(FactionDefinition[] factions, ReputationRules rules) {
        this.factions = factions;
        Rules = rules;
    }

    public ReputationRules Rules { get; }

    /// <summary>Ordered by id (ids are contiguous from 0).</summary>
    public IReadOnlyList<FactionDefinition> Factions => factions;

    public int Count => factions.Length;

    public bool Contains(int id) => id >= 0 && id < factions.Length;

    public bool TryGet(int id, out FactionDefinition faction) {
        if (Contains(id)) {
            faction = factions[id];
            return true;
        }

        faction = null!;
        return false;
    }

    /// <summary>Loads <c>Config/Nocthemar/Factions.json</c> relative to the process working directory.</summary>
    public static async Task<FactionConfig> LoadAsync(string? path = null) {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), "Config", "Nocthemar", "Factions.json");
        var json = await File.ReadAllTextAsync(path);
        try {
            return Parse(json);
        } catch (InvalidOperationException ex) {
            throw new InvalidOperationException($"{ex.Message} (file: {path})", ex);
        }
    }

    /// <summary>Parses and validates; throws <see cref="InvalidOperationException"/> listing every problem found.</summary>
    public static FactionConfig Parse(string json) {
        FactionsFile? file;
        try {
            file = JsonSerializer.Deserialize<FactionsFile>(json, JsonOptions);
        } catch (JsonException ex) {
            throw new InvalidOperationException($"Invalid Factions.json: {ex.Message}", ex);
        }

        if (file is null) {
            throw new InvalidOperationException("Invalid Factions.json: empty document.");
        }

        var errors = new List<string>();
        if (file.SchemaVersion != SupportedSchemaVersion) {
            errors.Add($"schemaVersion must be {SupportedSchemaVersion} (got {file.SchemaVersion})");
        }

        var rules = ParseRules(file.Reputation, errors);
        var parsed = new List<FactionDefinition>();
        if (file.Factions is null || file.Factions.Count == 0) {
            errors.Add("factions must contain at least one faction");
        } else if (file.Factions.Count > MaxFactions) {
            errors.Add($"factions has {file.Factions.Count} entries (max {MaxFactions})");
        } else {
            var keys = new HashSet<string>();
            for (var i = 0; i < file.Factions.Count; i++) {
                var faction = ParseFaction(file.Factions[i], i, file.Factions.Count, keys, errors);
                if (faction is not null) {
                    parsed.Add(faction);
                }
            }

            CheckRivalsAreSymmetric(parsed, errors);
        }

        if (errors.Count > 0 || rules is null) {
            throw new InvalidOperationException($"Invalid Factions.json: {string.Join("; ", errors)}.");
        }

        return new FactionConfig([.. parsed], rules);
    }

    private static ReputationRules? ParseRules(ReputationFile? file, List<string> errors) {
        if (file is null) {
            errors.Add("reputation is missing");
            return null;
        }

        var before = errors.Count;
        var min = Require(file.Min, "reputation.min", errors);
        var max = Require(file.Max, "reputation.max", errors);
        var joinLevel = Require(file.JoinLevel, "reputation.joinLevel", errors);
        var killBase = Require(file.KillBase, "reputation.killBase", errors);
        var victimRatio = file.VictimRatio;
        var rivalRatio = file.RivalRatio;
        if (victimRatio is null || !(victimRatio >= 0 && victimRatio <= 1)) {
            errors.Add($"reputation.victimRatio must be in [0, 1] (got {victimRatio?.ToString() ?? "nothing"})");
        }

        if (rivalRatio is null || !(rivalRatio >= 0 && rivalRatio <= 1)) {
            errors.Add($"reputation.rivalRatio must be in [0, 1] (got {rivalRatio?.ToString() ?? "nothing"})");
        }

        var tiers = file.Tiers;
        if (tiers is null) {
            errors.Add("reputation.tiers is missing");
        }

        var neutral = Require(tiers?.Neutral, "reputation.tiers.neutral", errors);
        var friendly = Require(tiers?.Friendly, "reputation.tiers.friendly", errors);
        var honored = Require(tiers?.Honored, "reputation.tiers.honored", errors);
        var exalted = Require(tiers?.Exalted, "reputation.tiers.exalted", errors);
        if (errors.Count > before) {
            return null;
        }

        if (min >= 0 || min < -1_000_000) {
            errors.Add($"reputation.min must be in [-1000000, -1] (got {min})");
        }

        if (max <= 0 || max > 1_000_000) {
            errors.Add($"reputation.max must be in [1, 1000000] (got {max})");
        }

        if (joinLevel < 1 || joinLevel > 1000) {
            errors.Add($"reputation.joinLevel must be in [1, 1000] (got {joinLevel})");
        }

        if (killBase < 1 || killBase > 1000) {
            errors.Add($"reputation.killBase must be in [1, 1000] (got {killBase})");
        }

        // Zero is Neutral: the Neutral tier starts below zero and Friendly above it.
        if (!(min < neutral && neutral < 0 && 0 < friendly && friendly < honored && honored < exalted && exalted <= max)) {
            errors.Add($"reputation tiers must satisfy min < neutral < 0 < friendly < honored < exalted <= max (got {min}, {neutral}, {friendly}, {honored}, {exalted}, {max})");
        }

        return errors.Count > before
            ? null
            : new ReputationRules(min, max, joinLevel, killBase, victimRatio!.Value, rivalRatio!.Value, neutral, friendly, honored, exalted);
    }

    private static int Require(int? value, string name, List<string> errors) {
        if (value is null) {
            errors.Add($"{name} is missing");
            return 0;
        }

        return value.Value;
    }

    private static FactionDefinition? ParseFaction(FactionFile? file, int index, int count, HashSet<string> keys, List<string> errors) {
        var before = errors.Count;
        var where = $"factions[{index}]";
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

        if (string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 40) {
            errors.Add($"{where}.name must be 1-40 characters");
        }

        if (string.IsNullOrWhiteSpace(file.Hq)) {
            errors.Add($"{where}.hq is missing");
        }

        if (file.Colors is null || file.Colors.Count < 1 || file.Colors.Count > 3 || file.Colors.Any(c => c is null || !ColorPattern.IsMatch(c))) {
            errors.Add($"{where}.colors must list 1-3 colors like #aabbcc");
        }

        var rivals = new List<int>();
        foreach (var rival in file.Rivals ?? []) {
            if (rival < 0 || rival >= count) {
                errors.Add($"{where}.rivals: faction {rival} does not exist");
            } else if (rival == file.Id) {
                errors.Add($"{where}.rivals: a faction cannot be its own rival");
            } else if (rivals.Contains(rival)) {
                errors.Add($"{where}.rivals: faction {rival} is listed twice");
            } else {
                rivals.Add(rival);
            }
        }

        var perksByTier = ParsePerks(file, where, errors);
        if (errors.Count > before) {
            return null;
        }

        return new FactionDefinition(
            file.Id, file.Key!, file.Name!, file.Hq!, file.Symbol ?? string.Empty, [.. file.Colors!], [.. rivals], perksByTier, file.Desc ?? string.Empty);
    }

    private static StatModifier[][] ParsePerks(FactionFile file, string where, List<string> errors) {
        var byTier = new StatModifier[Enum.GetValues<ReputationTier>().Length][];
        for (var t = 0; t < byTier.Length; t++) {
            byTier[t] = [];
        }

        // An invalid id is reported by the caller; the source id only has to be valid for the modifiers parsed meanwhile.
        var sourceId = file.Id >= 0 && file.Id <= ModifierSources.MaxId ? ModifierSources.Faction(file.Id) : 0;
        var seen = new HashSet<ReputationTier>();
        var perks = file.Perks ?? [];
        for (var i = 0; i < perks.Count; i++) {
            var perk = perks[i];
            var at = $"{where}.perks[{i}]";
            if (perk is null) {
                errors.Add($"{at} is null");
                continue;
            }

            if (!ModifierParser.TryParseEnum<ReputationTier>(perk.Tier, out var tier) || tier < ReputationTier.Friendly) {
                errors.Add($"{at}.tier '{perk.Tier}' must be Friendly, Honored or Exalted");
                continue;
            }

            if (!seen.Add(tier)) {
                errors.Add($"{at}.tier {tier} is declared twice");
                continue;
            }

            if (perk.Modifiers is null || perk.Modifiers.Count == 0) {
                errors.Add($"{at}.modifiers must list at least one modifier");
                continue;
            }

            byTier[(int)tier] = ModifierParser.Parse(perk.Modifiers, at, sourceId, MaxPerkModifiers, required: true, errors);
        }

        return byTier;
    }

    private static void CheckRivalsAreSymmetric(List<FactionDefinition> parsed, List<string> errors) {
        foreach (var faction in parsed) {
            foreach (var rival in faction.Rivals) {
                var other = parsed.FirstOrDefault(f => f.Id == rival);
                if (other is not null && !other.Rivals.Contains(faction.Id)) {
                    errors.Add($"faction {faction.Key} lists {other.Key} as a rival, but not the other way round");
                }
            }
        }
    }
}

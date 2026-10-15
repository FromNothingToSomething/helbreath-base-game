using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Server.Nocthemar.Core;

namespace Server.Nocthemar.Data;

public enum MimicKind : byte {
    Offensive = 0,
    Defensive = 1,
    Utility = 2,
    Control = 3,
    Ultimate = 4,
}

/// <summary>Essence rarity; the doc's split of essences is 60 / 25 / 10 / 4 / 1 %.</summary>
public enum MimicRarity : byte {
    Common = 0,
    Rare = 1,
    Epic = 2,
    Legendary = 3,
    Mythic = 4,
}

public sealed record MimicAbilityFile(
    int Id = -1,
    string? Key = null,
    string? Name = null,
    string? Kind = null,
    string? Rarity = null,
    int? MonsterId = null,
    string? MonsterName = null,
    List<int>? ClassIds = null,
    bool RequiresRaid = false,
    double DropChance = 0,
    int CooldownMs = 0,
    int ManaCost = -1,
    int? SpellId = null,
    string? Handler = null,
    string? Status = null,
    string? Desc = null);

public sealed record MimicFusionFile(int A = -1, int B = -1, int Result = -1);

public sealed record MimicXpFile(int MaxLevel = 0, int PerCast = 0, List<int>? Thresholds = null, double PowerPerLevel = -1);

public sealed record MimicSlotsFile(int First = 0, int LevelsPerSlot = 0, int Max = 0);

public sealed record MimicFile(
    int SchemaVersion = 0,
    MimicSlotsFile? Slots = null,
    MimicXpFile? Xp = null,
    List<MimicAbilityFile>? Abilities = null,
    List<MimicFusionFile>? Fusions = null);

/// <summary>One validated mimicry ability (a monster ability learned from an essence, or the result of a fusion).</summary>
public sealed class MimicAbility {
    internal MimicAbility(
        int id, string key, string name, MimicKind kind, MimicRarity rarity, int? monsterId, string? monsterName, int[] classIds,
        bool requiresRaid, double dropChance, int cooldownMs, int manaCost, int? spellId, string? handler, bool isReady, string desc) {
        Id = id;
        Key = key;
        Name = name;
        Kind = kind;
        Rarity = rarity;
        MonsterId = monsterId;
        MonsterName = monsterName;
        ClassIds = classIds;
        RequiresRaid = requiresRaid;
        DropChance = dropChance;
        CooldownMs = cooldownMs;
        ManaCost = manaCost;
        SpellId = spellId;
        Handler = handler;
        IsReady = isReady;
        Desc = desc;
    }

    public int Id { get; }
    public string Key { get; }
    public string Name { get; }
    public MimicKind Kind { get; }
    public MimicRarity Rarity { get; }

    /// <summary>Id in <c>Monsters.json</c> of the monster the ability comes from; null for a fusion result.</summary>
    public int? MonsterId { get; }

    /// <summary>Name of that monster in <c>Monsters.json</c> (documentation, checked by tests).</summary>
    public string? MonsterName { get; }

    /// <summary>Classes allowed to learn and equip it; empty = every class.</summary>
    public IReadOnlyList<int> ClassIds { get; }

    /// <summary>The essence only drops in a world flagged as a raid.</summary>
    public bool RequiresRaid { get; }

    /// <summary>Probability that the monster leaves its essence when killed by a player.</summary>
    public double DropChance { get; }

    public int CooldownMs { get; }
    public int ManaCost { get; }

    /// <summary>Legacy <c>Spells.json</c> id (&lt; 1000) reused for projectile, area and damage; null when the handler does everything.</summary>
    public int? SpellId { get; }

    public string? Handler { get; }

    /// <summary>False: listed but not castable yet (no handler).</summary>
    public bool IsReady { get; }

    public string Desc { get; }

    public bool IsFusion => MimicIds.IsFusion(Id);

    /// <summary>Items.json id of the essence; null for a fusion result (it has no essence).</summary>
    public int? EssenceItemId => IsFusion ? null : MimicIds.EssenceItemId(Id);

    /// <summary>True when a character of <paramref name="classId"/> (null = no class yet) may use the ability.</summary>
    public bool AllowsClass(int? classId) => ClassIds.Count == 0 || (classId is int c && ClassIds.Contains(c));
}

/// <summary>Two monster abilities that fuse into a third; <see cref="A"/> is always the lower id.</summary>
public sealed record MimicFusion(int A, int B, int Result);

/// <summary>Ability experience: each cast gives <see cref="PerCast"/>, <see cref="Thresholds"/>[i] is the total needed for level i + 2.</summary>
public sealed class MimicXpTable {
    internal MimicXpTable(int maxLevel, int perCast, int[] thresholds, double powerPerLevel) {
        MaxLevel = maxLevel;
        PerCast = perCast;
        Thresholds = thresholds;
        PowerPerLevel = powerPerLevel;
    }

    public int MaxLevel { get; }
    public int PerCast { get; }
    public IReadOnlyList<int> Thresholds { get; }
    public double PowerPerLevel { get; }

    /// <summary>Ability level (1..<see cref="MaxLevel"/>) reached with <paramref name="xp"/> total experience.</summary>
    public int LevelOf(long xp) {
        var level = 1;
        foreach (var threshold in Thresholds) {
            if (xp < threshold) {
                break;
            }

            level++;
        }

        return level;
    }

    /// <summary>Total experience at which the ability is at its maximum level; experience is capped there.</summary>
    public long MaxXp => Thresholds.Count == 0 ? 0 : Thresholds[^1];

    /// <summary><c>1 + PowerPerLevel x (level - 1)</c>.</summary>
    public double PowerMultiplier(int level) => 1.0 + PowerPerLevel * (Math.Clamp(level, 1, MaxLevel) - 1);
}

/// <summary>Number of mimicry slots by character level: <c>min(Max, First + (level - 1) / LevelsPerSlot)</c>.</summary>
public sealed class MimicSlotTable {
    internal MimicSlotTable(int first, int levelsPerSlot, int max) {
        First = first;
        LevelsPerSlot = levelsPerSlot;
        Max = max;
    }

    public int First { get; }
    public int LevelsPerSlot { get; }
    public int Max { get; }

    public int SlotsAt(int level) => Math.Min(Max, First + (Math.Max(level, 1) - 1) / LevelsPerSlot);
}

/// <summary>
/// <c>Config/Nocthemar/Mimic.json</c>: mimicry abilities (22 monster abilities and the fusion results), fusion recipes, slot and experience tables.
/// Fail-fast like <see cref="SkillConfig"/>: <see cref="Parse"/> throws one <see cref="InvalidOperationException"/> listing every problem.
/// Cross-file checks that need other catalogs (monsters, items, spells) are the <c>Validate*</c> methods, called at startup.
/// </summary>
public sealed class MimicConfig {
    public const int SupportedSchemaVersion = 1;

    /// <summary>Mimicry cooldowns are longer than the class skills' (12-20 s).</summary>
    public const int MinCooldownMs = 30_000;
    public const int MaxCooldownMs = 600_000;
    public const int MaxManaCost = 10_000;
    public const int MaxLevelCap = 10;

    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{1,47}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly Dictionary<int, MimicAbility> byId;
    private readonly Dictionary<int, MimicAbility> byMonster;
    private readonly Dictionary<(int, int), MimicFusion> byPair;

    private MimicConfig(MimicAbility[] abilities, MimicFusion[] fusions, MimicXpTable xp, MimicSlotTable slots) {
        Abilities = abilities;
        Fusions = fusions;
        Xp = xp;
        Slots = slots;
        byId = abilities.ToDictionary(a => a.Id);
        byMonster = abilities.Where(a => a.MonsterId is not null).ToDictionary(a => a.MonsterId!.Value);
        byPair = fusions.ToDictionary(f => (f.A, f.B));
    }

    /// <summary>Ordered by id.</summary>
    public IReadOnlyList<MimicAbility> Abilities { get; }

    public IReadOnlyList<MimicFusion> Fusions { get; }

    public MimicXpTable Xp { get; }

    public MimicSlotTable Slots { get; }

    public bool TryGet(int id, out MimicAbility ability) => byId.TryGetValue(id, out ability!);

    public bool Contains(int id) => byId.ContainsKey(id);

    /// <summary>The ability a monster of <c>Monsters.json</c> id <paramref name="monsterId"/> can leave an essence of; false when it has none.</summary>
    public bool TryGetForMonster(int monsterId, out MimicAbility ability) => byMonster.TryGetValue(monsterId, out ability!);

    /// <summary>The fusion of two abilities, in either order.</summary>
    public bool TryFindFusion(int a, int b, out MimicFusion fusion) => byPair.TryGetValue(a < b ? (a, b) : (b, a), out fusion!);

    /// <summary>Throws when a monster ability names a monster that is not in <c>Monsters.json</c> or whose name differs.</summary>
    public void ValidateMonsters(IReadOnlyDictionary<int, string> monsterNamesById) {
        ArgumentNullException.ThrowIfNull(monsterNamesById);
        var problems = new List<string>();
        foreach (var ability in Abilities.Where(a => a.MonsterId is not null)) {
            if (!monsterNamesById.TryGetValue(ability.MonsterId!.Value, out var name)) {
                problems.Add($"{ability.Key}: monster {ability.MonsterId} is not in Monsters.json");
            } else if (!string.Equals(name, ability.MonsterName, StringComparison.Ordinal)) {
                problems.Add($"{ability.Key}: monster {ability.MonsterId} is '{name}' in Monsters.json, not '{ability.MonsterName}'");
            }
        }

        Throw("Mimic.json does not match Monsters.json", problems);
    }

    /// <summary>Throws unless <c>Items.json</c> has the essence item of every monster ability.</summary>
    public void ValidateEssenceItems(IReadOnlyCollection<int> itemIds) {
        ArgumentNullException.ThrowIfNull(itemIds);
        var known = itemIds as ISet<int> ?? itemIds.ToHashSet();
        Throw(
            "Mimic.json does not match Items.json",
            Abilities.Where(a => a.EssenceItemId is int item && !known.Contains(item)).Select(a => $"{a.Key}: essence item {a.EssenceItemId} is missing").ToList());
    }

    /// <summary>Throws when an ability reuses a legacy spell id that <c>Spells.json</c> does not have.</summary>
    public void ValidateSpells(IReadOnlyCollection<int> spellIds) {
        ArgumentNullException.ThrowIfNull(spellIds);
        var known = spellIds as ISet<int> ?? spellIds.ToHashSet();
        Throw(
            "Mimic.json does not match Spells.json",
            Abilities.Where(a => a.SpellId is int spell && !known.Contains(spell)).Select(a => $"{a.Key}: spell {a.SpellId} is not in Spells.json").ToList());
    }

    private static void Throw(string title, List<string> problems) {
        if (problems.Count > 0) {
            throw new InvalidOperationException($"{title}: {string.Join("; ", problems)}.");
        }
    }

    /// <summary>Loads <c>Config/Nocthemar/Mimic.json</c> relative to the process working directory.</summary>
    public static async Task<MimicConfig> LoadAsync(ClassConfig classes, string? path = null) {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), "Config", "Nocthemar", "Mimic.json");
        var json = await File.ReadAllTextAsync(path);
        try {
            return Parse(json, classes);
        } catch (InvalidOperationException ex) {
            throw new InvalidOperationException($"{ex.Message} (file: {path})", ex);
        }
    }

    /// <summary>Parses and validates; throws <see cref="InvalidOperationException"/> listing every problem found.</summary>
    public static MimicConfig Parse(string json, ClassConfig classes) {
        ArgumentNullException.ThrowIfNull(classes);
        MimicFile? file;
        try {
            file = JsonSerializer.Deserialize<MimicFile>(json, JsonOptions);
        } catch (JsonException ex) {
            throw new InvalidOperationException($"Invalid Mimic.json: {ex.Message}", ex);
        }

        if (file is null) {
            throw new InvalidOperationException("Invalid Mimic.json: empty document.");
        }

        var errors = new List<string>();
        if (file.SchemaVersion != SupportedSchemaVersion) {
            errors.Add($"schemaVersion must be {SupportedSchemaVersion} (got {file.SchemaVersion})");
        }

        var slots = ParseSlots(file.Slots, errors);
        var xp = ParseXp(file.Xp, errors);
        var abilities = new List<MimicAbility>();
        if (file.Abilities is null || file.Abilities.Count == 0) {
            errors.Add("abilities must not be empty");
        } else if (file.Abilities.Count > MimicIds.MonsterCount + MimicIds.FusionCount) {
            errors.Add($"abilities has {file.Abilities.Count} entries (max {MimicIds.MonsterCount + MimicIds.FusionCount})");
        } else {
            var ids = new HashSet<int>();
            var keys = new HashSet<string>();
            var monsters = new HashSet<int>();
            for (var i = 0; i < file.Abilities.Count; i++) {
                var ability = ParseAbility(file.Abilities[i], i, classes, ids, keys, monsters, errors);
                if (ability is not null) {
                    abilities.Add(ability);
                }
            }
        }

        var fusions = ParseFusions(file.Fusions, abilities, errors);
        if (errors.Count > 0 || slots is null || xp is null) {
            throw new InvalidOperationException($"Invalid Mimic.json: {string.Join("; ", errors)}.");
        }

        return new MimicConfig([.. abilities.OrderBy(a => a.Id)], [.. fusions.OrderBy(f => f.Result)], xp, slots);
    }

    private static MimicSlotTable? ParseSlots(MimicSlotsFile? file, List<string> errors) {
        if (file is null) {
            errors.Add("slots section missing");
            return null;
        }

        var before = errors.Count;
        if (file.First < 1 || file.First > 9) {
            errors.Add($"slots.first must be in [1, 9] (got {file.First}; missing?)");
        }

        if (file.LevelsPerSlot < 1 || file.LevelsPerSlot > 100) {
            errors.Add($"slots.levelsPerSlot must be in [1, 100] (got {file.LevelsPerSlot}; missing?)");
        }

        if (file.Max < file.First || file.Max > 9) {
            errors.Add($"slots.max must be in [slots.first, 9] (got {file.Max})");
        }

        return errors.Count == before ? new MimicSlotTable(file.First, file.LevelsPerSlot, file.Max) : null;
    }

    private static MimicXpTable? ParseXp(MimicXpFile? file, List<string> errors) {
        if (file is null) {
            errors.Add("xp section missing");
            return null;
        }

        var before = errors.Count;
        if (file.MaxLevel < 1 || file.MaxLevel > MaxLevelCap) {
            errors.Add($"xp.maxLevel must be in [1, {MaxLevelCap}] (got {file.MaxLevel}; missing?)");
        }

        if (file.PerCast < 1 || file.PerCast > 1000) {
            errors.Add($"xp.perCast must be in [1, 1000] (got {file.PerCast}; missing?)");
        }

        if (!(file.PowerPerLevel >= 0 && file.PowerPerLevel <= 1)) {
            errors.Add($"xp.powerPerLevel must be in [0, 1] (got {file.PowerPerLevel}; missing?)");
        }

        var thresholds = file.Thresholds ?? [];
        if (file.MaxLevel >= 1 && thresholds.Count != file.MaxLevel - 1) {
            errors.Add($"xp.thresholds must have maxLevel - 1 = {file.MaxLevel - 1} entries (got {thresholds.Count})");
        } else {
            for (var i = 0; i < thresholds.Count; i++) {
                if (thresholds[i] < 1 || (i > 0 && thresholds[i] <= thresholds[i - 1])) {
                    errors.Add($"xp.thresholds must be positive and strictly increasing (index {i} is {thresholds[i]})");
                    break;
                }
            }
        }

        return errors.Count == before ? new MimicXpTable(file.MaxLevel, file.PerCast, [.. thresholds], file.PowerPerLevel) : null;
    }

    private static MimicAbility? ParseAbility(
        MimicAbilityFile? file, int index, ClassConfig classes, HashSet<int> ids, HashSet<string> keys, HashSet<int> monsters, List<string> errors) {
        var before = errors.Count;
        var where = $"abilities[{index}]";
        if (file is null) {
            errors.Add($"{where} is null");
            return null;
        }

        where = $"abilities[{index}] (id {file.Id})";
        var inRange = MimicIds.IsMimic(file.Id);
        if (!inRange) {
            errors.Add($"{where}.id must be in [{MimicIds.Base}, {MimicIds.End - 1}]");
        } else if (!ids.Add(file.Id)) {
            errors.Add($"{where}.id is duplicated");
        }

        if (file.Key is null || !KeyPattern.IsMatch(file.Key)) {
            errors.Add($"{where}.key must match {KeyPattern} (got '{file.Key}')");
        } else if (!keys.Add(file.Key)) {
            errors.Add($"{where}.key '{file.Key}' is duplicated");
        }

        if (string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 48) {
            errors.Add($"{where}.name must be 1-48 characters");
        }

        if (string.IsNullOrWhiteSpace(file.Desc) || file.Desc.Length > 200) {
            errors.Add($"{where}.desc must be 1-200 characters");
        }

        MimicKind kind;
        if (!TryParseName(file.Kind, out kind)) {
            errors.Add($"{where}.kind '{file.Kind}' must be offensive, defensive, utility, control or ultimate");
        }

        MimicRarity rarity;
        if (!TryParseName(file.Rarity, out rarity)) {
            errors.Add($"{where}.rarity '{file.Rarity}' must be common, rare, epic, legendary or mythic");
        } else if (rarity == MimicRarity.Mythic && !file.RequiresRaid) {
            errors.Add($"{where}: a mythic ability requires a raid (requiresRaid must be true)");
        }

        if (inRange && MimicIds.IsMonsterAbility(file.Id)) {
            if (file.MonsterId is not int monsterId || monsterId < 0) {
                errors.Add($"{where}.monsterId is required for a monster ability");
            } else if (!monsters.Add(monsterId)) {
                errors.Add($"{where}.monsterId {monsterId} already has an ability (one ability per monster)");
            }

            if (string.IsNullOrWhiteSpace(file.MonsterName)) {
                errors.Add($"{where}.monsterName is required for a monster ability");
            }

            if (!(file.DropChance >= 0 && file.DropChance <= 1)) {
                errors.Add($"{where}.dropChance must be in [0, 1] (got {file.DropChance})");
            }
        } else if (inRange) {
            if (file.MonsterId is not null || file.MonsterName is not null) {
                errors.Add($"{where}: a fusion result has no monster");
            }

            if (file.DropChance != 0) {
                errors.Add($"{where}: a fusion result does not drop (dropChance must be 0)");
            }
        }

        var classIds = file.ClassIds ?? [];
        if (classIds.Count > classes.Classes.Count || classIds.Distinct().Count() != classIds.Count || classIds.Any(c => !classes.Contains(c))) {
            errors.Add($"{where}.classIds must be distinct ids of Classes.json");
        }

        if (file.CooldownMs < MinCooldownMs || file.CooldownMs > MaxCooldownMs) {
            errors.Add($"{where}.cooldownMs must be in [{MinCooldownMs}, {MaxCooldownMs}] (mimicry is slower than class skills; got {file.CooldownMs})");
        }

        if (file.ManaCost < 0 || file.ManaCost > MaxManaCost) {
            errors.Add($"{where}.manaCost must be in [0, {MaxManaCost}] (got {file.ManaCost}; missing?)");
        }

        if (file.SpellId is int spell && (spell < 0 || spell >= SkillIds.Base)) {
            errors.Add($"{where}.spellId must be a legacy Spells.json id in [0, {SkillIds.Base - 1}] (got {spell})");
        }

        if (file.Handler is not null && !KeyPattern.IsMatch(file.Handler)) {
            errors.Add($"{where}.handler must match {KeyPattern} (got '{file.Handler}')");
        }

        var ready = false;
        if (file.Status is "ready") {
            ready = true;
            if (file.Handler is null) {
                errors.Add($"{where}: a ready ability needs a handler");
            }
        } else if (file.Status is "planned") {
            if (file.Handler is not null) {
                errors.Add($"{where}: a planned ability has no handler");
            }
        } else {
            errors.Add($"{where}.status must be 'ready' or 'planned' (got '{file.Status}')");
        }

        if (errors.Count > before) {
            return null;
        }

        return new MimicAbility(
            file.Id, file.Key!, file.Name!, kind, rarity, file.MonsterId, file.MonsterName, [.. classIds.OrderBy(c => c)],
            file.RequiresRaid, file.DropChance, file.CooldownMs, file.ManaCost, file.SpellId, file.Handler, ready, file.Desc!);
    }

    private static List<MimicFusion> ParseFusions(List<MimicFusionFile>? files, List<MimicAbility> abilities, List<string> errors) {
        var fusions = new List<MimicFusion>();
        var byId = abilities.ToDictionary(a => a.Id);
        var pairs = new HashSet<(int, int)>();
        var results = new HashSet<int>();
        for (var i = 0; i < (files?.Count ?? 0); i++) {
            var file = files![i];
            var where = $"fusions[{i}]";
            if (file is null) {
                errors.Add($"{where} is null");
                continue;
            }

            var before = errors.Count;
            if (file.A == file.B) {
                errors.Add($"{where}: the two essences must differ");
            }

            foreach (var (label, id) in new[] { ("a", file.A), ("b", file.B) }) {
                if (!byId.TryGetValue(id, out var input) || input.IsFusion) {
                    errors.Add($"{where}.{label} {id} must be a monster ability of the file");
                }
            }

            if (!byId.TryGetValue(file.Result, out var result) || !result.IsFusion) {
                errors.Add($"{where}.result {file.Result} must be a fusion ability of the file");
            }

            var pair = file.A < file.B ? (file.A, file.B) : (file.B, file.A);
            if (errors.Count == before && !pairs.Add(pair)) {
                errors.Add($"{where}: the pair {pair.Item1}+{pair.Item2} is already a recipe");
            }

            if (errors.Count == before && !results.Add(file.Result)) {
                errors.Add($"{where}: the result {file.Result} already has a recipe");
            }

            if (errors.Count == before) {
                fusions.Add(new MimicFusion(pair.Item1, pair.Item2, file.Result));
            }
        }

        foreach (var fusionAbility in abilities.Where(a => a.IsFusion && !results.Contains(a.Id))) {
            errors.Add($"fusion ability {fusionAbility.Key} (id {fusionAbility.Id}) has no recipe");
        }

        return fusions;
    }

    private static bool TryParseName<T>(string? value, out T result) where T : struct, Enum {
        result = default;
        return !string.IsNullOrEmpty(value) && !char.IsDigit(value[0]) && Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);
    }
}

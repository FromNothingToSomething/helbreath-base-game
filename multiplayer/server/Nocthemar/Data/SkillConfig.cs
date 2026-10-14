using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Server.Nocthemar.Core;

namespace Server.Nocthemar.Data;

/// <summary>Raw <c>Config/Nocthemar/Skills.json</c> shape; validated by <see cref="SkillConfig.Parse"/>.</summary>
public sealed record SkillsFile(int SchemaVersion = 0, List<SkillFile>? Skills = null);

/// <summary>
/// <c>Kind</c>: passive | active | ultimate. <c>Status</c>: ready | planned. A <c>ready</c> passive carries modifiers and/or a handler; a <c>ready</c> active carries a handler, a cooldown and a mana cost.
/// A <c>planned</c> skill only exists as a catalog entry (name, level, description) and cannot be learned.
/// </summary>
public sealed record SkillFile(
    int Id = -1,
    string? Key = null,
    string? Name = null,
    int Class = -1,
    int Branch = -1,
    int Slot = -1,
    int Level = -1,
    string? Kind = null,
    int Cost = -1,
    string? Desc = null,
    string? Status = null,
    string? Handler = null,
    int CooldownMs = 0,
    int ManaCost = 0,
    List<RaceModifierFile>? Modifiers = null);

public enum SkillKind : byte {
    Passive = 0,
    Active = 1,
    Ultimate = 2,
}

/// <summary>One validated class skill. Passive <see cref="Modifiers"/> carry <see cref="SourceId"/> so one <c>RemoveSource</c> undoes them.</summary>
public sealed class SkillDefinition {
    internal SkillDefinition(
        int id, string key, string name, int classId, int branch, int slot, int level, SkillKind kind, int cost, string description,
        bool isReady, string? handler, int cooldownMs, int manaCost, StatModifier[] modifiers) {
        Id = id;
        Key = key;
        Name = name;
        ClassId = classId;
        Branch = branch;
        Slot = slot;
        Level = level;
        Kind = kind;
        Cost = cost;
        Description = description;
        IsReady = isReady;
        Handler = handler;
        CooldownMs = cooldownMs;
        ManaCost = manaCost;
        Modifiers = modifiers;
    }

    public int Id { get; }
    public string Key { get; }
    public string Name { get; }
    public int ClassId { get; }
    public int Branch { get; }
    public int Slot { get; }

    /// <summary>Minimum character level to learn it.</summary>
    public int Level { get; }

    public SkillKind Kind { get; }

    /// <summary>Skill points to learn it.</summary>
    public int Cost { get; }

    public string Description { get; }

    /// <summary>False for catalog-only (planned) skills: visible, not learnable.</summary>
    public bool IsReady { get; }

    /// <summary>Key of the <c>ISkillHandler</c> that runs the skill (null when it is only modifiers).</summary>
    public string? Handler { get; }

    public int CooldownMs { get; }
    public int ManaCost { get; }
    public int SourceId => ModifierSources.Skill(Id);
    public IReadOnlyList<StatModifier> Modifiers { get; }
}

/// <summary>
/// Validated skill catalog (<c>Config/Nocthemar/Skills.json</c>): the complete grid class x branch x slot of every class of <see cref="ClassConfig"/>, each position exactly once.
/// Immutable after <see cref="Parse"/>; startup must fail on any invalid value.
/// </summary>
public sealed class SkillConfig {
    public const int SupportedSchemaVersion = 1;
    private const int MaxModifiersPerSkill = 8;
    private const int MaxCost = 5;
    private const int MaxCooldownMs = 3_600_000;
    private const int MaxManaCost = 100_000;

    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{1,47}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly Dictionary<int, SkillDefinition> byId;
    private readonly SkillDefinition[][] byClass;

    private SkillConfig(SkillDefinition[] skills, int classSlots) {
        Skills = skills;
        byId = skills.ToDictionary(s => s.Id);
        byClass = new SkillDefinition[classSlots][];
        for (var c = 0; c < classSlots; c++) {
            byClass[c] = [.. skills.Where(s => s.ClassId == c).OrderBy(s => s.Id)];
        }
    }

    /// <summary>Ordered by id.</summary>
    public IReadOnlyList<SkillDefinition> Skills { get; }

    public bool TryGet(int id, out SkillDefinition skill) => byId.TryGetValue(id, out skill!);

    public bool Contains(int id) => byId.ContainsKey(id);

    /// <summary>The 24 skills of a class ordered by id (branch, then slot); empty for an unknown class.</summary>
    public IReadOnlyList<SkillDefinition> ForClass(int classId) => classId >= 0 && classId < byClass.Length ? byClass[classId] : [];

    /// <summary>Loads <c>Config/Nocthemar/Skills.json</c> relative to the process working directory.</summary>
    public static async Task<SkillConfig> LoadAsync(ClassConfig classes, string? path = null) {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), "Config", "Nocthemar", "Skills.json");
        var json = await File.ReadAllTextAsync(path);
        try {
            return Parse(json, classes);
        } catch (InvalidOperationException ex) {
            throw new InvalidOperationException($"{ex.Message} (file: {path})", ex);
        }
    }

    /// <summary>Parses and validates; throws <see cref="InvalidOperationException"/> listing every problem found.</summary>
    public static SkillConfig Parse(string json, ClassConfig classes) {
        ArgumentNullException.ThrowIfNull(classes);
        SkillsFile? file;
        try {
            file = JsonSerializer.Deserialize<SkillsFile>(json, JsonOptions);
        } catch (JsonException ex) {
            throw new InvalidOperationException($"Invalid Skills.json: {ex.Message}", ex);
        }

        if (file is null) {
            throw new InvalidOperationException("Invalid Skills.json: empty document.");
        }

        var errors = new List<string>();
        if (file.SchemaVersion != SupportedSchemaVersion) {
            errors.Add($"schemaVersion must be {SupportedSchemaVersion} (got {file.SchemaVersion})");
        }

        var skills = new List<SkillDefinition>();
        var expected = classes.Classes.Count * SkillIds.SkillsPerClass;
        if (file.Skills is null || file.Skills.Count == 0) {
            errors.Add("skills must not be empty");
        } else if (file.Skills.Count > SkillIds.MaxClasses * SkillIds.SkillsPerClass) {
            errors.Add($"skills has {file.Skills.Count} entries (max {SkillIds.MaxClasses * SkillIds.SkillsPerClass})");
        } else {
            var ids = new HashSet<int>();
            var keys = new HashSet<string>();
            for (var i = 0; i < file.Skills.Count; i++) {
                var skill = ParseSkill(file.Skills[i], i, classes, ids, keys, errors);
                if (skill is not null) {
                    skills.Add(skill);
                }
            }

            if (errors.Count == 0 && skills.Count != expected) {
                var missing = classes.Classes
                    .SelectMany(c => Enumerable.Range(0, SkillIds.BranchesPerClass).SelectMany(b => Enumerable.Range(0, SkillIds.SlotsPerBranch).Select(s => SkillIds.Make(c.Id, b, s))))
                    .Where(id => !ids.Contains(id))
                    .Take(5);
                errors.Add($"expected {expected} skills (every class x branch x slot), got {skills.Count}; first missing ids: {string.Join(", ", missing)}");
            }
        }

        if (errors.Count > 0) {
            throw new InvalidOperationException($"Invalid Skills.json: {string.Join("; ", errors)}.");
        }

        var ordered = skills.OrderBy(s => s.Id).ToArray();
        return new SkillConfig(ordered, (classes.Classes.Max(c => c.Id)) + 1);
    }

    private static SkillDefinition? ParseSkill(SkillFile? file, int index, ClassConfig classes, HashSet<int> ids, HashSet<string> keys, List<string> errors) {
        var before = errors.Count;
        var where = $"skills[{index}]";
        if (file is null) {
            errors.Add($"{where} is null");
            return null;
        }

        where = $"skills[{index}] (id {file.Id})";
        var positionOk = SkillIds.IsValidPosition(file.Class, file.Branch, file.Slot) && classes.Contains(file.Class);
        if (!positionOk) {
            errors.Add($"{where}: class {file.Class} / branch {file.Branch} / slot {file.Slot} is not a position of a listed class");
        } else if (file.Id != SkillIds.Make(file.Class, file.Branch, file.Slot)) {
            errors.Add($"{where}.id must be {SkillIds.Make(file.Class, file.Branch, file.Slot)} for class {file.Class} branch {file.Branch} slot {file.Slot}");
        } else if (!ids.Add(file.Id)) {
            errors.Add($"{where}.id {file.Id} is duplicated");
        }

        if (file.Key is null || !KeyPattern.IsMatch(file.Key)) {
            errors.Add($"{where}.key must match {KeyPattern} (got '{file.Key}')");
        } else if (!keys.Add(file.Key)) {
            errors.Add($"{where}.key '{file.Key}' is duplicated");
        }

        if (string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 64) {
            errors.Add($"{where}.name must be 1-64 characters");
        }

        if (string.IsNullOrWhiteSpace(file.Desc) || file.Desc.Length > 200) {
            errors.Add($"{where}.desc must be 1-200 characters");
        }

        var kind = SkillKind.Passive;
        if (!ModifierParser.TryParseEnum(file.Kind, out kind)) {
            errors.Add($"{where}.kind '{file.Kind}' must be passive, active or ultimate");
        } else if (positionOk) {
            var expectedKind = SkillIds.IsPassiveSlot(file.Slot) ? SkillKind.Passive : SkillIds.IsUltimateSlot(file.Slot) ? SkillKind.Ultimate : SkillKind.Active;
            if (kind != expectedKind) {
                errors.Add($"{where}.kind must be {expectedKind.ToString().ToLowerInvariant()} for slot {file.Slot}");
            }
        }

        if (positionOk && file.Level != SkillIds.RequiredLevel(file.Slot)) {
            errors.Add($"{where}.level must be {SkillIds.RequiredLevel(file.Slot)} for slot {file.Slot} (got {file.Level})");
        }

        if (file.Cost < 1 || file.Cost > MaxCost) {
            errors.Add($"{where}.cost must be in [1, {MaxCost}] (got {file.Cost}; missing?)");
        }

        var ready = false;
        if (file.Status is "ready") {
            ready = true;
        } else if (file.Status is not "planned") {
            errors.Add($"{where}.status must be 'ready' or 'planned' (got '{file.Status}')");
        }

        if (file.Handler is not null && !KeyPattern.IsMatch(file.Handler)) {
            errors.Add($"{where}.handler must match {KeyPattern} (got '{file.Handler}')");
        }

        if (file.CooldownMs < 0 || file.CooldownMs > MaxCooldownMs) {
            errors.Add($"{where}.cooldownMs must be in [0, {MaxCooldownMs}]");
        }

        if (file.ManaCost < 0 || file.ManaCost > MaxManaCost) {
            errors.Add($"{where}.manaCost must be in [0, {MaxManaCost}]");
        }

        var sourceId = file.Id is >= 0 and <= ModifierSources.MaxId ? ModifierSources.Skill(file.Id) : 0;
        var modifiers = ModifierParser.Parse(file.Modifiers, where, sourceId, MaxModifiersPerSkill, required: false, errors);

        CheckStatus(file, where, kind, ready, modifiers.Length, errors);

        if (errors.Count > before) {
            return null;
        }

        return new SkillDefinition(
            file.Id, file.Key!, file.Name!, file.Class, file.Branch, file.Slot, file.Level, kind, file.Cost, file.Desc!,
            ready, file.Handler, file.CooldownMs, file.ManaCost, modifiers);
    }

    private static void CheckStatus(SkillFile file, string where, SkillKind kind, bool ready, int modifierCount, List<string> errors) {
        if (!ready) {
            if (modifierCount > 0 || file.Handler is not null || file.CooldownMs != 0 || file.ManaCost != 0) {
                errors.Add($"{where}: a planned skill must not carry modifiers, handler, cooldownMs or manaCost");
            }

            return;
        }

        if (kind == SkillKind.Passive) {
            if (modifierCount == 0 && file.Handler is null) {
                errors.Add($"{where}: a ready passive needs modifiers and/or a handler");
            }

            if (file.CooldownMs != 0 || file.ManaCost != 0) {
                errors.Add($"{where}: a passive has no cooldownMs or manaCost");
            }

            return;
        }

        if (file.Handler is null) {
            errors.Add($"{where}: a ready {kind.ToString().ToLowerInvariant()} needs a handler");
        }

        if (file.CooldownMs < 1) {
            errors.Add($"{where}: a ready {kind.ToString().ToLowerInvariant()} needs cooldownMs >= 1");
        }

        if (modifierCount > 0) {
            errors.Add($"{where}: an active skill carries no passive modifiers (a handler applies buffs)");
        }
    }
}

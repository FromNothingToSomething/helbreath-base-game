using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Server.Nocthemar.Core;

namespace Server.Nocthemar.Data;

/// <summary>Raw <c>Config/Nocthemar/Classes.json</c> shape; validated by <see cref="ClassConfig.Parse"/>.</summary>
public sealed record ClassesFile(int SchemaVersion = 0, List<ClassFile>? Classes = null);

/// <summary><c>RecommendedRaces</c>: race ids shown as a hint when picking a class (a soft recommendation, never a restriction).</summary>
public sealed record ClassFile(int Id = -1, string? Key = null, string? Name = null, string? Role = null, List<BranchFile>? Branches = null, List<int>? RecommendedRaces = null);

public sealed record BranchFile(int Id = -1, string? Key = null, string? Name = null, string? Role = null);

public sealed record BranchDefinition(int Id, string Key, string Name, string Role);

/// <summary>One validated class: 3 branches of 8 skills (the skills themselves live in <see cref="SkillConfig"/>).</summary>
public sealed class ClassDefinition {
    internal ClassDefinition(int id, string key, string name, string role, BranchDefinition[] branches, int[] recommendedRaceIds) {
        Id = id;
        Key = key;
        Name = name;
        Role = role;
        Branches = branches;
        RecommendedRaceIds = recommendedRaceIds;
    }

    public int Id { get; }
    public string Key { get; }
    public string Name { get; }
    public string Role { get; }

    /// <summary>Owner of class-wide modifiers (none yet); skills use <see cref="ModifierSources.Skill"/>.</summary>
    public int SourceId => ModifierSources.Class(Id);

    /// <summary>Indexed by branch id (0, 1, 2).</summary>
    public IReadOnlyList<BranchDefinition> Branches { get; }

    /// <summary>Races the class is recommended for (UI hint only; the server accepts any race x class pair).</summary>
    public IReadOnlyList<int> RecommendedRaceIds { get; }
}

/// <summary>Validated class catalog (<c>Config/Nocthemar/Classes.json</c>). Immutable after <see cref="Parse"/>; startup must fail on any invalid value.</summary>
public sealed class ClassConfig {
    public const int SupportedSchemaVersion = 1;
    private const int MaxRecommendedRaces = 64;

    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{1,31}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly Dictionary<int, ClassDefinition> byId;

    private ClassConfig(ClassDefinition[] classes) {
        Classes = classes;
        byId = classes.ToDictionary(c => c.Id);
    }

    public IReadOnlyList<ClassDefinition> Classes { get; }

    public bool TryGet(int id, out ClassDefinition definition) => byId.TryGetValue(id, out definition!);

    public bool Contains(int id) => byId.ContainsKey(id);

    /// <summary>Checks that every recommended race id is a listed race; throws listing each unknown one (startup check: Classes.json and Races.json are separate files).</summary>
    public void ValidateRaces(RaceConfig races) {
        ArgumentNullException.ThrowIfNull(races);
        var problems = Classes
            .SelectMany(c => c.RecommendedRaceIds.Where(r => !races.Contains(r)).Select(r => $"class '{c.Key}' recommends unknown race {r}"))
            .ToArray();
        if (problems.Length > 0) {
            throw new InvalidOperationException($"Invalid Classes.json: {string.Join("; ", problems)}.");
        }
    }

    /// <summary>Loads <c>Config/Nocthemar/Classes.json</c> relative to the process working directory.</summary>
    public static async Task<ClassConfig> LoadAsync(string? path = null) {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), "Config", "Nocthemar", "Classes.json");
        var json = await File.ReadAllTextAsync(path);
        try {
            return Parse(json);
        } catch (InvalidOperationException ex) {
            throw new InvalidOperationException($"{ex.Message} (file: {path})", ex);
        }
    }

    /// <summary>Parses and validates; throws <see cref="InvalidOperationException"/> listing every problem found.</summary>
    public static ClassConfig Parse(string json) {
        ClassesFile? file;
        try {
            file = JsonSerializer.Deserialize<ClassesFile>(json, JsonOptions);
        } catch (JsonException ex) {
            throw new InvalidOperationException($"Invalid Classes.json: {ex.Message}", ex);
        }

        if (file is null) {
            throw new InvalidOperationException("Invalid Classes.json: empty document.");
        }

        var errors = new List<string>();
        if (file.SchemaVersion != SupportedSchemaVersion) {
            errors.Add($"schemaVersion must be {SupportedSchemaVersion} (got {file.SchemaVersion})");
        }

        var classes = new List<ClassDefinition>();
        if (file.Classes is null || file.Classes.Count == 0) {
            errors.Add("classes must contain at least one class");
        } else if (file.Classes.Count > SkillIds.MaxClasses) {
            errors.Add($"classes has {file.Classes.Count} entries (max {SkillIds.MaxClasses})");
        } else {
            var ids = new HashSet<int>();
            var keys = new HashSet<string>();
            for (var i = 0; i < file.Classes.Count; i++) {
                var definition = ParseClass(file.Classes[i], i, ids, keys, errors);
                if (definition is not null) {
                    classes.Add(definition);
                }
            }
        }

        if (errors.Count > 0) {
            throw new InvalidOperationException($"Invalid Classes.json: {string.Join("; ", errors)}.");
        }

        return new ClassConfig([.. classes]);
    }

    private static ClassDefinition? ParseClass(ClassFile? file, int index, HashSet<int> ids, HashSet<string> keys, List<string> errors) {
        var before = errors.Count;
        var where = $"classes[{index}]";
        if (file is null) {
            errors.Add($"{where} is null");
            return null;
        }

        if (file.Id < 0 || file.Id >= SkillIds.MaxClasses) {
            errors.Add($"{where}.id must be in [0, {SkillIds.MaxClasses - 1}] (got {file.Id}; missing?)");
        } else if (!ids.Add(file.Id)) {
            errors.Add($"{where}.id {file.Id} is duplicated");
        }

        where = $"classes[{index}] (id {file.Id})";
        CheckKey(file.Key, where, keys, errors);
        CheckText(file.Name, $"{where}.name", 64, errors);
        CheckText(file.Role, $"{where}.role", 64, errors);

        var branches = new List<BranchDefinition>();
        if (file.Branches is null || file.Branches.Count != SkillIds.BranchesPerClass) {
            errors.Add($"{where}.branches must list exactly {SkillIds.BranchesPerClass} branches");
        } else {
            var branchKeys = new HashSet<string>();
            for (var b = 0; b < file.Branches.Count; b++) {
                var branch = file.Branches[b];
                var at = $"{where}.branches[{b}]";
                if (branch is null) {
                    errors.Add($"{at} is null");
                    continue;
                }

                if (branch.Id != b) {
                    errors.Add($"{at}.id must be {b} (branches are listed in id order; got {branch.Id})");
                }

                CheckKey(branch.Key, at, branchKeys, errors);
                CheckText(branch.Name, $"{at}.name", 64, errors);
                CheckText(branch.Role, $"{at}.role", 64, errors);
                branches.Add(new BranchDefinition(branch.Id, branch.Key ?? "", branch.Name ?? "", branch.Role ?? ""));
            }
        }

        var recommended = file.RecommendedRaces ?? [];
        if (recommended.Count > MaxRecommendedRaces || recommended.Any(r => r < 0 || r > ModifierSources.MaxId) || recommended.Distinct().Count() != recommended.Count) {
            errors.Add($"{where}.recommendedRaces must hold at most {MaxRecommendedRaces} distinct race ids in [0, {ModifierSources.MaxId}]");
        }

        return errors.Count > before ? null : new ClassDefinition(file.Id, file.Key!, file.Name!, file.Role!, [.. branches], [.. recommended]);
    }

    private static void CheckKey(string? key, string where, HashSet<string> keys, List<string> errors) {
        if (key is null || !KeyPattern.IsMatch(key)) {
            errors.Add($"{where}.key must match {KeyPattern} (got '{key}')");
        } else if (!keys.Add(key)) {
            errors.Add($"{where}.key '{key}' is duplicated");
        }
    }

    private static void CheckText(string? value, string where, int max, List<string> errors) {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max) {
            errors.Add($"{where} must be 1-{max} characters");
        }
    }
}

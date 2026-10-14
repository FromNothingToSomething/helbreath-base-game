using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Server.Nocthemar.Core;

namespace Server.Nocthemar.Data;

/// <summary>Raw <c>Config/Nocthemar/Races.json</c> shape; validated by <see cref="RaceConfig.Parse"/>.</summary>
public sealed record RacesFile(int SchemaVersion = 0, int DefaultRaceId = -1, List<RaceFile>? Races = null);

public sealed record RaceFile(
    int Id = -1,
    string? Key = null,
    string? Name = null,
    List<RaceModifierFile>? Modifiers = null,
    List<string>? Traits = null,
    RaceAppearanceFile? Appearance = null,
    RaceArtFile? Art = null);

/// <summary><c>Stat</c> is a <see cref="StatId"/> name, <c>Kind</c> a <see cref="ModifierKind"/> name; a missing value stays NaN and is rejected.</summary>
public sealed record RaceModifierFile(string? Stat = null, string? Kind = null, double Value = double.NaN);

/// <summary>Allowed values per appearance axis (genders 0-1, skin colors 0-2, hair styles 0-7, underwear colors 0-7).</summary>
public sealed record RaceAppearanceFile(List<int>? Genders = null, List<int>? SkinColors = null, List<int>? HairStyles = null, List<int>? UnderwearColors = null);

/// <summary><c>Native</c> = the race has its own sprites. Otherwise human sprites are tinted and <c>Todo</c> must carry a <c>TODO(art)</c> note.</summary>
public sealed record RaceArtFile(bool Native = false, string? Tint = null, string? Todo = null);

/// <summary>Allowed appearance values of one race; used by character creation and by appearance changes.</summary>
public sealed class RaceAppearance {
    private readonly HashSet<int> genders;
    private readonly HashSet<int> skinColors;
    private readonly HashSet<int> hairStyles;
    private readonly HashSet<int> underwearColors;

    internal RaceAppearance(IEnumerable<int> genders, IEnumerable<int> skinColors, IEnumerable<int> hairStyles, IEnumerable<int> underwearColors) {
        this.genders = [.. genders];
        this.skinColors = [.. skinColors];
        this.hairStyles = [.. hairStyles];
        this.underwearColors = [.. underwearColors];
    }

    public IReadOnlyCollection<int> Genders => genders;
    public IReadOnlyCollection<int> SkinColors => skinColors;
    public IReadOnlyCollection<int> HairStyles => hairStyles;
    public IReadOnlyCollection<int> UnderwearColors => underwearColors;

    public bool Allows(int gender, int skinColor, int hairStyle, int underwearColor) =>
        genders.Contains(gender) && skinColors.Contains(skinColor) && hairStyles.Contains(hairStyle) && underwearColors.Contains(underwearColor);
}

/// <param name="Native">True when the race has its own sprites; false means human sprites tinted with <paramref name="Tint"/> (see <paramref name="Todo"/>).</param>
public sealed record RaceArt(bool Native, string? Tint, string? Todo);

/// <summary>One validated race. <see cref="Modifiers"/> carry <see cref="SourceId"/> so that one <c>RemoveSource</c> call undoes them all.</summary>
public sealed class RaceDefinition {
    internal RaceDefinition(int id, string key, string name, StatModifier[] modifiers, string[] traits, RaceAppearance appearance, RaceArt art) {
        Id = id;
        Key = key;
        Name = name;
        Modifiers = modifiers;
        Traits = traits;
        Appearance = appearance;
        Art = art;
    }

    public int Id { get; }
    public string Key { get; }
    public string Name { get; }
    public int SourceId => ModifierSources.Race(Id);
    public IReadOnlyList<StatModifier> Modifiers { get; }
    public IReadOnlyList<string> Traits { get; }
    public RaceAppearance Appearance { get; }
    public RaceArt Art { get; }
}

/// <summary>
/// Validated race catalog (<c>Config/Nocthemar/Races.json</c>). Immutable after <see cref="Parse"/>; startup must fail on any invalid value.
/// Modifiers may target stats that no gameplay code reads yet (see the <c>// consumer:</c> comments on <see cref="StatId"/>): they are stored and applied, and simply inert until a consumer exists.
/// </summary>
public sealed class RaceConfig {
    public const int SupportedSchemaVersion = 1;
    private const int MaxRaces = 64;
    private const int MaxModifiersPerRace = 32;
    private const int MaxTraitsPerRace = 16;

    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{1,31}$", RegexOptions.Compiled);
    private static readonly Regex TraitPattern = new("^[a-z][a-z0-9_]{1,47}$", RegexOptions.Compiled);
    private static readonly Regex TintPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly Dictionary<int, RaceDefinition> byId;

    private RaceConfig(RaceDefinition[] races, int defaultRaceId) {
        Races = races;
        byId = races.ToDictionary(r => r.Id);
        DefaultRaceId = defaultRaceId;
    }

    public IReadOnlyList<RaceDefinition> Races { get; }

    /// <summary>Race given to legacy characters and used as the fallback for unknown ids.</summary>
    public int DefaultRaceId { get; }

    public RaceDefinition Default => byId[DefaultRaceId];

    public bool TryGet(int id, out RaceDefinition race) => byId.TryGetValue(id, out race!);

    public bool Contains(int id) => byId.ContainsKey(id);

    /// <summary>Loads <c>Config/Nocthemar/Races.json</c> relative to the process working directory.</summary>
    public static async Task<RaceConfig> LoadAsync(string? path = null) {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), "Config", "Nocthemar", "Races.json");
        var json = await File.ReadAllTextAsync(path);
        try {
            return Parse(json);
        } catch (InvalidOperationException ex) {
            throw new InvalidOperationException($"{ex.Message} (file: {path})", ex);
        }
    }

    /// <summary>Parses and validates; throws <see cref="InvalidOperationException"/> listing every problem found.</summary>
    public static RaceConfig Parse(string json) {
        RacesFile? file;
        try {
            file = JsonSerializer.Deserialize<RacesFile>(json, JsonOptions);
        } catch (JsonException ex) {
            throw new InvalidOperationException($"Invalid Races.json: {ex.Message}", ex);
        }

        if (file is null) {
            throw new InvalidOperationException("Invalid Races.json: empty document.");
        }

        var errors = new List<string>();
        if (file.SchemaVersion != SupportedSchemaVersion) {
            errors.Add($"schemaVersion must be {SupportedSchemaVersion} (got {file.SchemaVersion})");
        }

        var races = new List<RaceDefinition>();
        if (file.Races is null || file.Races.Count == 0) {
            errors.Add("races must contain at least one race");
        } else if (file.Races.Count > MaxRaces) {
            errors.Add($"races has {file.Races.Count} entries (max {MaxRaces})");
        } else {
            var ids = new HashSet<int>();
            var keys = new HashSet<string>();
            for (var i = 0; i < file.Races.Count; i++) {
                var race = ParseRace(file.Races[i], i, ids, keys, errors);
                if (race is not null) {
                    races.Add(race);
                }
            }
        }

        if (file.DefaultRaceId < 0 || (errors.Count == 0 && races.All(r => r.Id != file.DefaultRaceId))) {
            errors.Add($"defaultRaceId {file.DefaultRaceId} is not the id of a listed race");
        }

        if (errors.Count > 0) {
            throw new InvalidOperationException($"Invalid Races.json: {string.Join("; ", errors)}.");
        }

        return new RaceConfig([.. races], file.DefaultRaceId);
    }

    private static RaceDefinition? ParseRace(RaceFile? file, int index, HashSet<int> ids, HashSet<string> keys, List<string> errors) {
        var before = errors.Count;
        var where = $"races[{index}]";
        if (file is null) {
            errors.Add($"{where} is null");
            return null;
        }

        if (file.Id < 0 || file.Id > ModifierSources.MaxId) {
            errors.Add($"{where}.id must be in [0, {ModifierSources.MaxId}] (got {file.Id}; missing?)");
        } else if (!ids.Add(file.Id)) {
            errors.Add($"{where}.id {file.Id} is duplicated");
        }

        where = $"races[{index}] (id {file.Id})";
        if (file.Key is null || !KeyPattern.IsMatch(file.Key)) {
            errors.Add($"{where}.key must match {KeyPattern} (got '{file.Key}')");
        } else if (!keys.Add(file.Key)) {
            errors.Add($"{where}.key '{file.Key}' is duplicated");
        }

        if (string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 64) {
            errors.Add($"{where}.name must be 1-64 characters");
        }

        var modifiers = ParseModifiers(file, where, errors);
        var traits = ParseTraits(file, where, errors);
        var appearance = ParseAppearance(file.Appearance, where, errors);
        var art = ParseArt(file.Art, where, errors);

        if (errors.Count > before || appearance is null || art is null) {
            return null;
        }

        return new RaceDefinition(file.Id, file.Key!, file.Name!, modifiers, traits, appearance, art);
    }

    private static StatModifier[] ParseModifiers(RaceFile file, string where, List<string> errors) {
        var result = new List<StatModifier>();
        if (file.Modifiers is null) {
            errors.Add($"{where}.modifiers missing (use [] for none)");
            return [];
        }

        if (file.Modifiers.Count > MaxModifiersPerRace) {
            errors.Add($"{where}.modifiers has {file.Modifiers.Count} entries (max {MaxModifiersPerRace})");
            return [];
        }

        var seen = new HashSet<(StatId, ModifierKind)>();
        for (var i = 0; i < file.Modifiers.Count; i++) {
            var m = file.Modifiers[i];
            var at = $"{where}.modifiers[{i}]";
            if (m is null || !TryParseEnum<StatId>(m.Stat, out var stat)) {
                errors.Add($"{at}.stat '{m?.Stat}' is not a StatId ({string.Join(", ", Enum.GetNames<StatId>())})");
                continue;
            }

            if (!TryParseEnum<ModifierKind>(m.Kind, out var kind)) {
                errors.Add($"{at}.kind '{m.Kind}' is not a ModifierKind ({string.Join(", ", Enum.GetNames<ModifierKind>())})");
                continue;
            }

            if (!double.IsFinite(m.Value)) {
                errors.Add($"{at}.value must be a finite number (missing?)");
                continue;
            }

            if (kind != ModifierKind.Flat && (m.Value <= -1.0 || m.Value > 10.0)) {
                errors.Add($"{at}.value for a percent modifier must be in (-1, 10] (got {m.Value})");
                continue;
            }

            if (kind == ModifierKind.Flat && Math.Abs(m.Value) > 1_000_000) {
                errors.Add($"{at}.value for a flat modifier must be within ±1000000 (got {m.Value})");
                continue;
            }

            if (stat is StatId.ResistPhysical or StatId.ResistMagic or StatId.ResistCurse or StatId.ResistDeath && kind != ModifierKind.Flat) {
                errors.Add($"{at}: {stat} takes Flat modifiers (fractions, 0.10 = 10%)");
                continue;
            }

            if (!seen.Add((stat, kind))) {
                errors.Add($"{at}: {stat}/{kind} is declared twice for this race");
                continue;
            }

            result.Add(new StatModifier(stat, kind, m.Value, ModifierSources.Race(Math.Clamp(file.Id, 0, ModifierSources.MaxId))));
        }

        return [.. result];
    }

    private static string[] ParseTraits(RaceFile file, string where, List<string> errors) {
        if (file.Traits is null) {
            errors.Add($"{where}.traits missing (use [] for none)");
            return [];
        }

        if (file.Traits.Count > MaxTraitsPerRace) {
            errors.Add($"{where}.traits has {file.Traits.Count} entries (max {MaxTraitsPerRace})");
            return [];
        }

        var seen = new HashSet<string>();
        foreach (var trait in file.Traits) {
            if (trait is null || !TraitPattern.IsMatch(trait)) {
                errors.Add($"{where}.traits entry '{trait}' must match {TraitPattern}");
            } else if (!seen.Add(trait)) {
                errors.Add($"{where}.traits entry '{trait}' is duplicated");
            }
        }

        return [.. file.Traits.Where(t => t is not null)];
    }

    private static RaceAppearance? ParseAppearance(RaceAppearanceFile? file, string where, List<string> errors) {
        if (file is null) {
            errors.Add($"{where}.appearance missing");
            return null;
        }

        var before = errors.Count;
        CheckAxis(file.Genders, "genders", 1, where, errors);
        CheckAxis(file.SkinColors, "skinColors", 2, where, errors);
        CheckAxis(file.HairStyles, "hairStyles", 7, where, errors);
        CheckAxis(file.UnderwearColors, "underwearColors", 7, where, errors);
        return errors.Count > before ? null : new RaceAppearance(file.Genders!, file.SkinColors!, file.HairStyles!, file.UnderwearColors!);
    }

    private static void CheckAxis(List<int>? values, string name, int max, string where, List<string> errors) {
        if (values is null || values.Count == 0) {
            errors.Add($"{where}.appearance.{name} must list at least one value");
        } else if (values.Any(v => v < 0 || v > max)) {
            errors.Add($"{where}.appearance.{name} values must be in [0, {max}]");
        } else if (values.Distinct().Count() != values.Count) {
            errors.Add($"{where}.appearance.{name} has duplicate values");
        }
    }

    private static RaceArt? ParseArt(RaceArtFile? file, string where, List<string> errors) {
        if (file is null) {
            errors.Add($"{where}.art missing");
            return null;
        }

        var before = errors.Count;
        if (file.Tint is not null && !TintPattern.IsMatch(file.Tint)) {
            errors.Add($"{where}.art.tint must be #RRGGBB (got '{file.Tint}')");
        }

        if (!file.Native && (file.Todo is null || !file.Todo.StartsWith("TODO(art)", StringComparison.Ordinal))) {
            errors.Add($"{where}.art: a race without its own sprites needs a todo starting with 'TODO(art)'");
        }

        if (file.Native && file.Todo is not null) {
            errors.Add($"{where}.art: a native race must not carry a todo");
        }

        return errors.Count > before ? null : new RaceArt(file.Native, file.Tint, file.Todo);
    }

    /// <summary>Exact (case-insensitive) member-name match; numbers, padded names and flag lists are rejected.</summary>
    private static bool TryParseEnum<T>(string? name, out T value) where T : struct, Enum {
        value = default;
        if (string.IsNullOrEmpty(name) || !Enum.TryParse(name, ignoreCase: true, out value) || !Enum.IsDefined(value)) {
            return false;
        }

        // Enum.TryParse also accepts " 3", "3" and "MaxHp, MaxMana"; only a plain member name is valid here.
        return string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase);
    }
}

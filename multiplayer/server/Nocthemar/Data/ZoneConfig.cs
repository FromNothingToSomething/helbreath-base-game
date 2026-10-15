using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Server.Nocthemar.Data;

/// <summary>What a point of interest is (documentation and map display; no server behavior).</summary>
public enum ZonePoiKind : byte {
    Portal = 0,
    Npc = 1,
    Landmark = 2,
}

public sealed record ZonePoiFile(string? Key = null, string? Name = null, string? Kind = null, int X = -1, int Y = -1);

public sealed record ZoneFile(
    string? Id = null,
    string? Name = null,
    int LevelMin = 0,
    int LevelMax = 0,
    int MinLevel = 0,
    int? FactionId = null,
    List<ZonePoiFile>? Pois = null);

public sealed record ZonesFile(int SchemaVersion = 0, List<ZoneFile>? Zones = null);

/// <summary>A point of interest of a zone, in cells of the zone's map.</summary>
public sealed record ZonePoi(string Key, string Name, ZonePoiKind Kind, int X, int Y);

/// <summary>One validated zone: the metadata of one game world of <c>GameWorlds.json</c>.</summary>
public sealed class ZoneDefinition {
    internal ZoneDefinition(string id, string name, int levelMin, int levelMax, int minLevel, int? factionId, ZonePoi[] pois) {
        Id = id;
        Name = name;
        LevelMin = levelMin;
        LevelMax = levelMax;
        MinLevel = minLevel;
        FactionId = factionId;
        Pois = pois;
    }

    /// <summary>Id of the game world (GameWorlds.json) this zone describes.</summary>
    public string Id { get; }

    public string Name { get; }

    /// <summary>Recommended level range (display only).</summary>
    public int LevelMin { get; }

    public int LevelMax { get; }

    /// <summary>Level needed to enter by a portal; the server refuses the transfer below it.</summary>
    public int MinLevel { get; }

    /// <summary>Faction of the zone's headquarters or control (display and placement of the faction masters); none when null. It has no effect on reputation.</summary>
    public int? FactionId { get; }

    public IReadOnlyList<ZonePoi> Pois { get; }
}

/// <summary>
/// <c>Config/Nocthemar/Zones.json</c>: metadata of the Nocthemar zones (level range, entry level, faction, points of interest). A zone is a game world of
/// <c>GameWorlds.json</c> with the same id; worlds without a zone (the Helbreath ones) are not gated. Fail-fast: <see cref="Parse"/> throws one
/// <see cref="InvalidOperationException"/> listing every problem; the worlds, the map bounds and the level ceiling are checked by the Validate methods once
/// those are known (as <see cref="MimicConfig"/> does for its catalogs).
/// </summary>
public sealed class ZoneConfig {
    public const int SupportedSchemaVersion = 1;
    public const int MaxZones = 32;
    public const int MaxPois = 64;

    private static readonly Regex IdPattern = new("^[a-z][a-z0-9_-]{1,31}$", RegexOptions.Compiled);
    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{1,31}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly ZoneDefinition[] zones;
    private readonly Dictionary<string, ZoneDefinition> byId;

    private ZoneConfig(ZoneDefinition[] zones) {
        this.zones = zones;
        byId = zones.ToDictionary(z => z.Id, StringComparer.Ordinal);
    }

    /// <summary>No zone at all: every world is open (a server without Zones.json content).</summary>
    public static ZoneConfig Empty { get; } = new([]);

    /// <summary>In file order.</summary>
    public IReadOnlyList<ZoneDefinition> Zones => zones;

    public int Count => zones.Length;

    public bool TryGet(string worldId, out ZoneDefinition zone) => byId.TryGetValue(worldId, out zone!);

    /// <summary>Loads <c>Config/Nocthemar/Zones.json</c> relative to the process working directory.</summary>
    public static async Task<ZoneConfig> LoadAsync(FactionConfig factions, string? path = null) {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), "Config", "Nocthemar", "Zones.json");
        var json = await File.ReadAllTextAsync(path);
        try {
            return Parse(json, factions);
        } catch (InvalidOperationException ex) {
            throw new InvalidOperationException($"{ex.Message} (file: {path})", ex);
        }
    }

    /// <summary>Parses and validates against the factions; throws <see cref="InvalidOperationException"/> listing every problem found.</summary>
    public static ZoneConfig Parse(string json, FactionConfig factions) {
        ArgumentNullException.ThrowIfNull(factions);
        ZonesFile? file;
        try {
            file = JsonSerializer.Deserialize<ZonesFile>(json, JsonOptions);
        } catch (JsonException ex) {
            throw new InvalidOperationException($"Invalid Zones.json: {ex.Message}", ex);
        }

        if (file is null) {
            throw new InvalidOperationException("Invalid Zones.json: empty document.");
        }

        var errors = new List<string>();
        if (file.SchemaVersion != SupportedSchemaVersion) {
            errors.Add($"schemaVersion must be {SupportedSchemaVersion} (got {file.SchemaVersion})");
        }

        var parsed = new List<ZoneDefinition>();
        if (file.Zones is null) {
            errors.Add("zones is required (it may be empty)");
        } else if (file.Zones.Count > MaxZones) {
            errors.Add($"zones has {file.Zones.Count} entries (max {MaxZones})");
        } else {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < file.Zones.Count; i++) {
                var zone = ParseZone(file.Zones[i], i, ids, factions, errors);
                if (zone is not null) {
                    parsed.Add(zone);
                }
            }
        }

        if (errors.Count > 0) {
            throw new InvalidOperationException($"Invalid Zones.json: {string.Join("; ", errors)}.");
        }

        return new ZoneConfig([.. parsed]);
    }

    /// <summary>Every zone must be a game world of <c>GameWorlds.json</c>.</summary>
    public void ValidateWorlds(IEnumerable<string> worldIds) {
        ArgumentNullException.ThrowIfNull(worldIds);
        var known = new HashSet<string>(worldIds, StringComparer.Ordinal);
        var missing = zones.Where(z => !known.Contains(z.Id)).Select(z => z.Id).ToArray();
        if (missing.Length > 0) {
            throw new InvalidOperationException($"Zones.json zone(s) {string.Join(", ", missing)} are not game worlds of GameWorlds.json.");
        }
    }

    /// <summary>No level may exceed the progression's maximum level.</summary>
    public void ValidateLevels(int maxLevel) {
        var errors = zones
            .Where(z => z.LevelMax > maxLevel)
            .Select(z => $"zone {z.Id} levelMax {z.LevelMax} is above the maximum level {maxLevel}")
            .ToArray();
        if (errors.Length > 0) {
            throw new InvalidOperationException($"Zones.json: {string.Join("; ", errors)}.");
        }
    }

    /// <summary>The points of interest of the zone <paramref name="worldId"/> (if any) must lie inside its map. Worlds without a zone pass.</summary>
    public void ValidateMap(string worldId, int sizeX, int sizeY) {
        if (!byId.TryGetValue(worldId, out var zone)) {
            return;
        }

        var outside = zone.Pois.Where(p => p.X >= sizeX || p.Y >= sizeY).Select(p => $"{p.Key} ({p.X},{p.Y})").ToArray();
        if (outside.Length > 0) {
            throw new InvalidOperationException($"Zones.json zone {zone.Id}: point(s) of interest outside the {sizeX}x{sizeY} map: {string.Join(", ", outside)}.");
        }
    }

    private static ZoneDefinition? ParseZone(ZoneFile? file, int index, HashSet<string> ids, FactionConfig factions, List<string> errors) {
        var before = errors.Count;
        var where = $"zones[{index}]";
        if (file is null) {
            errors.Add($"{where} is null");
            return null;
        }

        if (file.Id is null || !IdPattern.IsMatch(file.Id)) {
            errors.Add($"{where}.id '{file.Id}' must match {IdPattern}");
        } else if (!ids.Add(file.Id)) {
            errors.Add($"{where}.id '{file.Id}' is used twice");
        }

        if (string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 40) {
            errors.Add($"{where}.name must be 1-40 characters");
        }

        if (file.LevelMin < 1) {
            errors.Add($"{where}.levelMin must be at least 1 (got {file.LevelMin})");
        }

        if (file.LevelMax < file.LevelMin) {
            errors.Add($"{where}.levelMax {file.LevelMax} must not be below levelMin {file.LevelMin}");
        }

        if (file.MinLevel < 1 || file.MinLevel > file.LevelMax) {
            errors.Add($"{where}.minLevel must be in [1, levelMax {file.LevelMax}] (got {file.MinLevel})");
        }

        if (file.FactionId is int factionId && !factions.Contains(factionId)) {
            errors.Add($"{where}.factionId {factionId} is not a faction of Factions.json");
        }

        var pois = ParsePois(file.Pois, where, errors);
        if (errors.Count > before) {
            return null;
        }

        return new ZoneDefinition(file.Id!, file.Name!, file.LevelMin, file.LevelMax, file.MinLevel, file.FactionId, pois);
    }

    private static ZonePoi[] ParsePois(List<ZonePoiFile>? files, string zoneWhere, List<string> errors) {
        if (files is null || files.Count == 0) {
            return [];
        }

        if (files.Count > MaxPois) {
            errors.Add($"{zoneWhere}.pois has {files.Count} entries (max {MaxPois})");
            return [];
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ZonePoi>();
        for (var i = 0; i < files.Count; i++) {
            var poi = files[i];
            var where = $"{zoneWhere}.pois[{i}]";
            if (poi is null) {
                errors.Add($"{where} is null");
                continue;
            }

            var ok = true;
            if (poi.Key is null || !KeyPattern.IsMatch(poi.Key)) {
                errors.Add($"{where}.key '{poi.Key}' must match {KeyPattern}");
                ok = false;
            } else if (!keys.Add(poi.Key)) {
                errors.Add($"{where}.key '{poi.Key}' is used twice");
                ok = false;
            }

            if (string.IsNullOrWhiteSpace(poi.Name) || poi.Name.Length > 40) {
                errors.Add($"{where}.name must be 1-40 characters");
                ok = false;
            }

            if (!ModifierParser.TryParseEnum<ZonePoiKind>(poi.Kind, out var kind)) {
                errors.Add($"{where}.kind '{poi.Kind}' must be Portal, Npc or Landmark");
                ok = false;
            }

            if (poi.X < 0 || poi.Y < 0) {
                errors.Add($"{where} position ({poi.X},{poi.Y}) must not be negative");
                ok = false;
            }

            if (ok) {
                result.Add(new ZonePoi(poi.Key!, poi.Name!, kind, poi.X, poi.Y));
            }
        }

        return [.. result];
    }
}

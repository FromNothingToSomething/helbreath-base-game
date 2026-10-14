using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Nocthemar.Data;

/// <summary>Raw <c>Config/Nocthemar/Progression.json</c> shape; every section is required and checked by <see cref="ProgressionConfig.Parse"/>.</summary>
public sealed record ProgressionFile(
    int SchemaVersion = 0,
    int MaxLevel = 0,
    XpCurveFile? Xp = null,
    HpCurveFile? Hp = null,
    ManaCurveFile? Mana = null,
    int DefaultMonsterXp = -1,
    SkillPointsFile? SkillPoints = null);

/// <summary>Skill points earned: <c>PerLevel</c> for every level from <c>FirstLevel</c> on (the optional section defaults to 1 point per level from level 2).</summary>
public sealed record SkillPointsFile(int FirstLevel = ProgressionConfig.DefaultSkillPointsFirstLevel, int PerLevel = ProgressionConfig.DefaultSkillPointsPerLevel);

/// <summary>XP needed to go from level L to L+1 is <c>round(Base * L^Exponent)</c>.</summary>
public sealed record XpCurveFile(double Base = 0, double Exponent = 0);

/// <summary>Max HP at level L is <c>Base + PerLevel * (L - 1)</c>; <c>RegenPerSecond</c> (optional, default 0 = none) is the HP regained per second out of combat.</summary>
public sealed record HpCurveFile(int Base = 0, int PerLevel = -1, double RegenPerSecond = 0);

/// <summary>Max mana at level L is <c>Base + PerLevel * (L - 1)</c>; regeneration is per second.</summary>
public sealed record ManaCurveFile(int Base = -1, int PerLevel = -1, double RegenPerSecond = -1);

/// <summary>
/// Validated progression tables: XP curve, HP/mana by level, mana regeneration and default monster XP.
/// Immutable after <see cref="Parse"/>; startup must fail on any invalid value rather than run with a broken curve.
/// </summary>
public sealed class ProgressionConfig {
    public const int SupportedSchemaVersion = 1;
    public const int DefaultSkillPointsFirstLevel = 2;
    public const int DefaultSkillPointsPerLevel = 1;
    private const int MaxSkillPointsPerLevel = 10;
    private const int MaxLevelCeiling = 1000;
    private const double XpPerLevelCeiling = 1e12;

    // Mirrors Config.JsonOptions (case-insensitive names); unmapped members are rejected so typos in this new file fail loudly.
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly long[] xpToNext;
    private readonly int hpBase;
    private readonly int hpPerLevel;
    private readonly int manaBase;
    private readonly int manaPerLevel;

    private ProgressionConfig(
        int maxLevel, long[] xpToNext, int hpBase, int hpPerLevel, int manaBase, int manaPerLevel,
        double manaRegenPerSecond, int defaultMonsterXp, int skillPointsFirstLevel, int skillPointsPerLevel, double hpRegenPerSecond) {
        MaxLevel = maxLevel;
        this.xpToNext = xpToNext;
        this.hpBase = hpBase;
        this.hpPerLevel = hpPerLevel;
        this.manaBase = manaBase;
        this.manaPerLevel = manaPerLevel;
        ManaRegenPerSecond = manaRegenPerSecond;
        DefaultMonsterXp = defaultMonsterXp;
        SkillPointsFirstLevel = skillPointsFirstLevel;
        SkillPointsPerLevel = skillPointsPerLevel;
        HpRegenPerSecond = hpRegenPerSecond;
    }

    public int MaxLevel { get; }
    public double ManaRegenPerSecond { get; }
    public int DefaultMonsterXp { get; }
    public int SkillPointsFirstLevel { get; }
    public int SkillPointsPerLevel { get; }

    /// <summary>HP regained per second out of combat (base of <see cref="Server.Nocthemar.Core.StatId.HpRegen"/>); 0 = no regeneration.</summary>
    public double HpRegenPerSecond { get; }

    /// <summary>Total skill points a character of <paramref name="level"/> has earned (spent ones included): 0 below <see cref="SkillPointsFirstLevel"/>, then <see cref="SkillPointsPerLevel"/> per level up to <see cref="MaxLevel"/>.</summary>
    public int SkillPointsAt(int level) => SkillPointsPerLevel * Math.Max(0, ClampLevel(level) - SkillPointsFirstLevel + 1);

    /// <summary>XP for a kill: the monster's own <c>xp</c> from <c>Monsters.json</c> when set and non-negative, otherwise <see cref="DefaultMonsterXp"/>.</summary>
    public int ResolveMonsterXp(int? configuredXp) => configuredXp is int xp && xp >= 0 ? xp : DefaultMonsterXp;

    /// <summary>Clamps to [1, <see cref="MaxLevel"/>].</summary>
    public int ClampLevel(int level) => Math.Clamp(level, 1, MaxLevel);

    /// <summary>XP required to go from <paramref name="level"/> to the next one; false at <see cref="MaxLevel"/> or for out-of-range levels.</summary>
    public bool TryGetXpToNext(int level, out long xp) {
        if (level < 1 || level >= MaxLevel) {
            xp = 0;
            return false;
        }

        xp = xpToNext[level - 1];
        return true;
    }

    public int MaxHpAt(int level) => hpBase + hpPerLevel * (ClampLevel(level) - 1);

    public int MaxManaAt(int level) => manaBase + manaPerLevel * (ClampLevel(level) - 1);

    /// <summary>Loads <c>Config/Nocthemar/Progression.json</c> relative to the process working directory (same convention as <see cref="Server.Config"/>).</summary>
    public static async Task<ProgressionConfig> LoadAsync(string? path = null) {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), "Config", "Nocthemar", "Progression.json");
        var json = await File.ReadAllTextAsync(path);
        try {
            return Parse(json);
        } catch (InvalidOperationException ex) {
            throw new InvalidOperationException($"{ex.Message} (file: {path})", ex);
        }
    }

    /// <summary>Parses and validates; throws <see cref="InvalidOperationException"/> listing every problem found.</summary>
    public static ProgressionConfig Parse(string json) {
        ProgressionFile? file;
        try {
            file = JsonSerializer.Deserialize<ProgressionFile>(json, JsonOptions);
        } catch (JsonException ex) {
            throw new InvalidOperationException($"Invalid Progression.json: {ex.Message}", ex);
        }

        if (file is null) {
            throw new InvalidOperationException("Invalid Progression.json: empty document.");
        }

        var errors = new List<string>();
        if (file.SchemaVersion != SupportedSchemaVersion) {
            errors.Add($"schemaVersion must be {SupportedSchemaVersion} (got {file.SchemaVersion})");
        }

        if (file.MaxLevel < 1 || file.MaxLevel > MaxLevelCeiling) {
            errors.Add($"maxLevel must be in [1, {MaxLevelCeiling}] (got {file.MaxLevel})");
        }

        if (file.DefaultMonsterXp < 0) {
            errors.Add($"defaultMonsterXp must be >= 0 (got {file.DefaultMonsterXp}; missing?)");
        }

        if (file.Xp is null) {
            errors.Add("xp section missing");
        } else if (!(file.Xp.Base > 0) || !double.IsFinite(file.Xp.Base)) {
            errors.Add($"xp.base must be a finite number > 0 (got {file.Xp.Base})");
        } else if (!(file.Xp.Exponent > 0) || file.Xp.Exponent > 10 || !double.IsFinite(file.Xp.Exponent)) {
            errors.Add($"xp.exponent must be in (0, 10] (got {file.Xp.Exponent})");
        }

        if (file.Hp is null) {
            errors.Add("hp section missing");
        } else {
            if (file.Hp.Base < 1) {
                errors.Add($"hp.base must be >= 1 (got {file.Hp.Base})");
            }

            if (file.Hp.PerLevel < 0) {
                errors.Add($"hp.perLevel must be >= 0 (got {file.Hp.PerLevel}; missing?)");
            }

            if (!(file.Hp.RegenPerSecond >= 0) || !double.IsFinite(file.Hp.RegenPerSecond) || file.Hp.RegenPerSecond > 100_000) {
                errors.Add($"hp.regenPerSecond must be a finite number in [0, 100000] (got {file.Hp.RegenPerSecond})");
            }
        }

        if (file.Mana is null) {
            errors.Add("mana section missing");
        } else {
            if (file.Mana.Base < 0) {
                errors.Add($"mana.base must be >= 0 (got {file.Mana.Base}; missing?)");
            }

            if (file.Mana.PerLevel < 0) {
                errors.Add($"mana.perLevel must be >= 0 (got {file.Mana.PerLevel}; missing?)");
            }

            if (!(file.Mana.RegenPerSecond >= 0) || !double.IsFinite(file.Mana.RegenPerSecond)) {
                errors.Add($"mana.regenPerSecond must be a finite number >= 0 (got {file.Mana.RegenPerSecond}; missing?)");
            }
        }

        var skillPoints = file.SkillPoints ?? new SkillPointsFile();
        if (skillPoints.FirstLevel < 1 || (file.MaxLevel >= 1 && skillPoints.FirstLevel > file.MaxLevel)) {
            errors.Add($"skillPoints.firstLevel must be in [1, maxLevel] (got {skillPoints.FirstLevel})");
        }

        if (skillPoints.PerLevel < 0 || skillPoints.PerLevel > MaxSkillPointsPerLevel) {
            errors.Add($"skillPoints.perLevel must be in [0, {MaxSkillPointsPerLevel}] (got {skillPoints.PerLevel})");
        }

        // Stat ceilings use long math so a huge perLevel cannot wrap into a valid-looking int.
        if (file.MaxLevel >= 1 && file.MaxLevel <= MaxLevelCeiling) {
            if (file.Hp is { Base: >= 1, PerLevel: >= 0 } hp &&
                (long)hp.Base + (long)hp.PerLevel * (file.MaxLevel - 1) > int.MaxValue) {
                errors.Add("hp overflows int at maxLevel");
            }

            if (file.Mana is { Base: >= 0, PerLevel: >= 0 } mana &&
                (long)mana.Base + (long)mana.PerLevel * (file.MaxLevel - 1) > int.MaxValue) {
                errors.Add("mana overflows int at maxLevel");
            }
        }

        long[] table = [];
        if (errors.Count == 0) {
            table = BuildXpTable(file.MaxLevel, file.Xp!, errors);
        }

        if (errors.Count > 0) {
            throw new InvalidOperationException($"Invalid Progression.json: {string.Join("; ", errors)}.");
        }

        return new ProgressionConfig(
            file.MaxLevel, table, file.Hp!.Base, file.Hp.PerLevel, file.Mana!.Base, file.Mana.PerLevel,
            file.Mana.RegenPerSecond, file.DefaultMonsterXp, skillPoints.FirstLevel, skillPoints.PerLevel, file.Hp.RegenPerSecond);
    }

    /// <summary>Builds the per-level XP table and verifies it is strictly increasing, positive, and bounded (rounding can flatten a curve with a tiny base or exponent).</summary>
    private static long[] BuildXpTable(int maxLevel, XpCurveFile curve, List<string> errors) {
        var table = new long[Math.Max(0, maxLevel - 1)];
        for (var level = 1; level < maxLevel; level++) {
            var raw = curve.Base * Math.Pow(level, curve.Exponent);
            if (!double.IsFinite(raw) || raw >= XpPerLevelCeiling) {
                errors.Add($"xp curve exceeds {XpPerLevelCeiling:0.##E+0} at level {level}");
                return table;
            }

            var value = (long)Math.Round(raw);
            if (value < 1) {
                errors.Add($"xp curve rounds to < 1 at level {level}");
                return table;
            }

            if (level > 1 && value <= table[level - 2]) {
                errors.Add($"xp curve is not strictly increasing at level {level} ({table[level - 2]} -> {value})");
                return table;
            }

            table[level - 1] = value;
        }

        return table;
    }
}

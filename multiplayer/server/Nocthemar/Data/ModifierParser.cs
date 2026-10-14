using Server.Nocthemar.Core;

namespace Server.Nocthemar.Data;

/// <summary>Validation shared by every JSON catalog that declares <see cref="RaceModifierFile"/> entries (races, class skills).</summary>
internal static class ModifierParser {
    /// <param name="required">True: a missing list is an error ("use [] for none"); false: it means no modifier.</param>
    public static StatModifier[] Parse(List<RaceModifierFile>? modifiers, string where, int sourceId, int max, bool required, List<string> errors) {
        var result = new List<StatModifier>();
        if (modifiers is null) {
            if (required) {
                errors.Add($"{where}.modifiers missing (use [] for none)");
            }

            return [];
        }

        if (modifiers.Count > max) {
            errors.Add($"{where}.modifiers has {modifiers.Count} entries (max {max})");
            return [];
        }

        var seen = new HashSet<(StatId, ModifierKind)>();
        for (var i = 0; i < modifiers.Count; i++) {
            var m = modifiers[i];
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
                errors.Add($"{at}: {stat}/{kind} is declared twice for this owner");
                continue;
            }

            result.Add(new StatModifier(stat, kind, m.Value, sourceId));
        }

        return [.. result];
    }

    /// <summary>Exact (case-insensitive) member-name match; numbers, padded names and flag lists are rejected.</summary>
    internal static bool TryParseEnum<T>(string? name, out T value) where T : struct, Enum {
        value = default;
        if (string.IsNullOrEmpty(name) || !Enum.TryParse(name, ignoreCase: true, out value) || !Enum.IsDefined(value)) {
            return false;
        }

        // Enum.TryParse also accepts " 3", "3" and "MaxHp, MaxMana"; only a plain member name is valid here.
        return string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase);
    }
}

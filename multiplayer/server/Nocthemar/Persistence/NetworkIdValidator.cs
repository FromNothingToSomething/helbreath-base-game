using System.Diagnostics.CodeAnalysis;

namespace Server.Nocthemar.Persistence;

/// <summary>
/// Validates client-supplied network ids before they become file names. Rejects path traversal (<c>..</c>, <c>/</c>, <c>\</c>, NUL),
/// characters invalid on common file systems, Windows reserved device names and over-long ids.
/// </summary>
public static class NetworkIdValidator {
    /// <summary>Leaves room under the 255-char file name limit for ".json" and the ".{guid}.tmp" suffix.</summary>
    public const int MaxLength = 200;

    private static readonly char[] ForbiddenChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase) {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool IsValid([NotNullWhen(true)] string? networkId) {
        if (string.IsNullOrWhiteSpace(networkId) || networkId.Length > MaxLength) {
            return false;
        }

        if (networkId.Contains("..", StringComparison.Ordinal)) {
            return false;
        }

        var first = networkId[0];
        var last = networkId[^1];
        if (first == '.' || first == ' ' || last == '.' || last == ' ') {
            return false;
        }

        foreach (var c in networkId) {
            if (c < 32 || c == 127 || Array.IndexOf(ForbiddenChars, c) >= 0) {
                return false;
            }
        }

        var dot = networkId.IndexOf('.');
        var stem = dot >= 0 ? networkId[..dot] : networkId;
        return !ReservedNames.Contains(stem);
    }

    /// <summary>
    /// Resolves <c>{id}.json</c> inside <paramref name="directoryFullPath"/> (an absolute path without trailing separator).
    /// Besides <see cref="IsValid"/>, the resolved file must sit directly in that directory.
    /// </summary>
    public static bool TryResolveSavePath(string directoryFullPath, string? networkId, [NotNullWhen(true)] out string? path) {
        path = null;
        if (!IsValid(networkId)) {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(directoryFullPath, networkId + ".json"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(candidate), directoryFullPath, comparison)) {
            return false;
        }

        path = candidate;
        return true;
    }
}

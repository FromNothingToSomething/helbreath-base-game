using System.Text.RegularExpressions;
using Server.Utils;

namespace Server.World.Game;

/// <summary>
/// Loads .amd map files and extracts occupancy data for game world collision/movement.
/// </summary>
public static class Map {
    private const int HeaderSize = 256;
    private const byte BlockedFlag = 0x80; // Bit 7: 1 = blocked, 0 = move allowed

    /// <summary>
    /// Loads an .amd map file and returns a GameWorldOccupancyTracker with blocked cells marked occupied.
    /// Only occupancy data is retained; other map data is discarded after parsing.
    /// </summary>
    /// <param name="mapsDirectory">Directory containing .amd files (e.g. Config/maps)</param>
    /// <param name="mapName">Map file name without extension (e.g. aresden)</param>
    public static GameWorldOccupancyTracker LoadOccupancy(
        string mapsDirectory,
        string mapName,
        IEnumerable<WorldLocationConfig>? teleportCells = null) {
        var path = Path.Combine(mapsDirectory, $"{mapName}.amd");
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length < HeaderSize) {
            throw new InvalidOperationException($"Map file '{path}' is too small to contain a valid header.");
        }

        var headerText = System.Text.Encoding.ASCII.GetString(bytes.AsSpan(0, HeaderSize));
        ParseHeader(headerText, out var sizeX, out var sizeY, out var tileSize);

        if (sizeX <= 0 || sizeY <= 0 || tileSize <= 0) {
            throw new InvalidOperationException($"Invalid map dimensions in '{path}': {sizeX}x{sizeY}, tileSize={tileSize}");
        }

        var expectedDataSize = sizeX * sizeY * tileSize;
        if (bytes.Length < HeaderSize + expectedDataSize) {
            throw new InvalidOperationException($"Map file '{path}' is too small for dimensions {sizeX}x{sizeY} with tileSize {tileSize}.");
        }

        var blockedCells = new List<(int X, int Y)>();
        var offset = HeaderSize;

        for (var y = 0; y < sizeY; y++) {
            for (var x = 0; x < sizeX; x++) {
                var flags = bytes[offset + 8];
                var isBlocked = (flags & BlockedFlag) != 0;
                if (isBlocked) {
                    blockedCells.Add((x, y));
                }
                offset += tileSize;
            }
        }

        return new GameWorldOccupancyTracker(
            sizeX,
            sizeY,
            blockedCells,
            teleportCells?.Select(cell => (cell.X, cell.Y)));
    }

    /// <summary>Token-scans the 256-byte ASCII header for MAPSIZEX, MAPSIZEY, and TILESIZE key=value triples.</summary>
    internal static void ParseHeader(string headerText, out int sizeX, out int sizeY, out int tileSize) {
        sizeX = 0;
        sizeY = 0;
        tileSize = 0;

        var normalized = headerText.Replace('\0', ' ');
        var tokens = Regex.Split(normalized, @"\s+").Where(t => t.Length > 0).ToArray();

        for (var i = 0; i < tokens.Length; i++) {
            var token = tokens[i];
            if (i + 2 >= tokens.Length) {
                continue;
            }

            if (tokens[i + 1] != "=") {
                continue;
            }

            if (!int.TryParse(tokens[i + 2], out var value)) {
                continue;
            }

            switch (token) {
                case "MAPSIZEX":
                    sizeX = value;
                    break;
                case "MAPSIZEY":
                    sizeY = value;
                    break;
                case "TILESIZE":
                    tileSize = value;
                    break;
            }
        }
    }
}

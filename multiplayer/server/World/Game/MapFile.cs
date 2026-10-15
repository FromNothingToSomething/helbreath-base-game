namespace Server.World.Game;

/// <summary>Dimensions of a validated <c>.amd</c> file.</summary>
public sealed record MapInfo(int SizeX, int SizeY, int TileSize, int BlockedCount);

/// <summary>A cell a map must offer: <see cref="MustBeWalkable"/> cells (spawn, arrival, NPC placement) must not carry the blocked flag; others only have to lie inside the map.</summary>
public readonly record struct MapCell(string What, int X, int Y, bool MustBeWalkable);

/// <summary>
/// Structure checks of <c>.amd</c> files (256-byte ASCII header, then <c>MAPSIZEX x MAPSIZEY</c> tiles of <c>TILESIZE</c> bytes, flags in byte 8, bit 7 = blocked),
/// for the zone maps of module 6 whatever their origin (a copied Helbreath map, a generated one or one drawn in an editor). <see cref="Map.LoadOccupancy"/> stays the loader.
/// </summary>
public static class MapFile {
    public const int HeaderSize = 256;

    /// <summary>The flags live in byte 8 of a tile.</summary>
    public const int MinTileSize = 9;

    public const int MaxSide = 1000;
    private const int FlagsOffset = 8;
    private const byte BlockedFlag = 0x80;

    /// <summary>Checks the header and the file length; throws <see cref="InvalidOperationException"/> listing every problem.</summary>
    public static MapInfo Inspect(byte[] bytes, string name) {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < HeaderSize) {
            throw new InvalidOperationException($"Map '{name}': {bytes.Length} bytes is smaller than the {HeaderSize}-byte header.");
        }

        Map.ParseHeader(System.Text.Encoding.ASCII.GetString(bytes.AsSpan(0, HeaderSize)), out var sizeX, out var sizeY, out var tileSize);
        var errors = new List<string>();
        if (sizeX < 1 || sizeX > MaxSide) {
            errors.Add($"MAPSIZEX must be in [1, {MaxSide}] (got {sizeX})");
        }

        if (sizeY < 1 || sizeY > MaxSide) {
            errors.Add($"MAPSIZEY must be in [1, {MaxSide}] (got {sizeY})");
        }

        if (tileSize < MinTileSize || tileSize > 64) {
            errors.Add($"TILESIZE must be in [{MinTileSize}, 64] (got {tileSize})");
        }

        if (errors.Count > 0) {
            throw new InvalidOperationException($"Map '{name}': {string.Join("; ", errors)}.");
        }

        var expected = (long)HeaderSize + (long)sizeX * sizeY * tileSize;
        if (bytes.Length != expected) {
            throw new InvalidOperationException($"Map '{name}': {bytes.Length} bytes, expected exactly {expected} for {sizeX}x{sizeY} tiles of {tileSize} bytes.");
        }

        var blocked = 0;
        for (var offset = HeaderSize + FlagsOffset; offset < bytes.Length; offset += tileSize) {
            if ((bytes[offset] & BlockedFlag) != 0) {
                blocked++;
            }
        }

        return new MapInfo(sizeX, sizeY, tileSize, blocked);
    }

    public static bool IsBlocked(byte[] bytes, MapInfo info, int x, int y) {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(info);
        return (bytes[HeaderSize + (y * info.SizeX + x) * info.TileSize + FlagsOffset] & BlockedFlag) != 0;
    }

    /// <summary>One message per cell that lies outside the map or, when it must be walkable, is blocked. Empty when every cell is fine.</summary>
    public static IReadOnlyList<string> CheckCells(byte[] bytes, MapInfo info, string name, IEnumerable<MapCell> cells) {
        ArgumentNullException.ThrowIfNull(cells);
        var problems = new List<string>();
        foreach (var cell in cells) {
            if (cell.X < 0 || cell.Y < 0 || cell.X >= info.SizeX || cell.Y >= info.SizeY) {
                problems.Add($"Map '{name}': {cell.What} ({cell.X},{cell.Y}) is outside the {info.SizeX}x{info.SizeY} map.");
            } else if (cell.MustBeWalkable && IsBlocked(bytes, info, cell.X, cell.Y)) {
                problems.Add($"Map '{name}': {cell.What} ({cell.X},{cell.Y}) is on a blocked cell.");
            }
        }

        return problems;
    }
}

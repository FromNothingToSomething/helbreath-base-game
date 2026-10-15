using System.Text;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T92: structure checks of .amd files, run on every shipped map and on synthetic ones.</summary>
public class MapFileTests {
    private static byte[] Build(int sizeX, int sizeY, int tileSize = 10, Func<int, int, bool>? blocked = null, int? lengthDelta = null, string? header = null) {
        var text = header ?? $"MAPSIZEX = {sizeX,4} MAPSIZEY = {sizeY,4} TILESIZE = {tileSize}";
        var bytes = new byte[256 + sizeX * sizeY * tileSize + (lengthDelta ?? 0)];
        Encoding.ASCII.GetBytes(text).CopyTo(bytes, 0);
        for (var y = 0; y < sizeY; y++) {
            for (var x = 0; x < sizeX; x++) {
                if (blocked?.Invoke(x, y) == true) {
                    bytes[256 + (y * sizeX + x) * tileSize + 8] = 0x80;
                }
            }
        }

        return bytes;
    }

    private static string MapsDirectory() => Path.GetDirectoryName(TestSupport.SourceFile("Config", "maps", "aresden.amd"))!;

    private static void Rejects(byte[] bytes, string expected) {
        var ex = Assert.Throws<InvalidOperationException>(() => MapFile.Inspect(bytes, "t"));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Every_shipped_map_is_well_formed_and_matches_the_loader() {
        var files = Directory.GetFiles(MapsDirectory(), "*.amd");

        Assert.True(files.Length >= 80);
        foreach (var file in files) {
            var bytes = File.ReadAllBytes(file);
            var info = MapFile.Inspect(bytes, Path.GetFileName(file));
            var occupancy = Map.LoadOccupancy(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file));
            Assert.Equal((occupancy.SizeX, occupancy.SizeY, occupancy.OccupiedCount), (info.SizeX, info.SizeY, info.BlockedCount));
        }
    }

    [Theory]
    [InlineData("aresden", 300, 300)]
    [InlineData("inferniaA", 150, 150)]
    [InlineData("2ndmiddle", 250, 250)]
    public void The_source_maps_of_the_zones_are_there(string name, int sizeX, int sizeY) {
        var info = MapFile.Inspect(File.ReadAllBytes(Path.Combine(MapsDirectory(), name + ".amd")), name);

        Assert.Equal((sizeX, sizeY, 10), (info.SizeX, info.SizeY, info.TileSize));
        Assert.True(info.BlockedCount > 0 && info.BlockedCount < sizeX * sizeY);
    }

    [Fact]
    public void A_synthetic_map_reports_its_size_and_blocked_cells() {
        var info = MapFile.Inspect(Build(20, 12, blocked: (x, y) => x == 0 || y == 0 || x == 19 || y == 11), "t");

        Assert.Equal((20, 12, 10, 60), (info.SizeX, info.SizeY, info.TileSize, info.BlockedCount));
    }

    [Fact]
    public void A_tile_of_nine_bytes_is_accepted() => Assert.Equal(9, MapFile.Inspect(Build(5, 5, tileSize: 9), "t").TileSize);

    [Fact]
    public void Malformed_files_are_rejected_with_the_reason() {
        Rejects(new byte[100], "smaller than the 256-byte header");
        Rejects(Build(10, 10, header: "garbage"), "MAPSIZEX must be in [1, 1000] (got 0)");
        Rejects(Build(10, 10, header: "MAPSIZEX = 10 MAPSIZEY = 10 TILESIZE = 4"), "TILESIZE must be in [9, 64] (got 4)");
        Rejects(Build(10, 10, header: "MAPSIZEX = 1001 MAPSIZEY = 10 TILESIZE = 10"), "MAPSIZEX must be in [1, 1000] (got 1001)");
        Rejects(Build(10, 10, lengthDelta: -1), "expected exactly 1256");
        Rejects(Build(10, 10, lengthDelta: 1), "expected exactly 1256");
    }

    [Fact]
    public void Every_header_problem_is_listed_at_once() {
        var ex = Assert.Throws<InvalidOperationException>(() => MapFile.Inspect(Build(1, 1, header: "MAPSIZEX = 0 MAPSIZEY = 0 TILESIZE = 0"), "t"));

        Assert.Contains("MAPSIZEX", ex.Message);
        Assert.Contains("MAPSIZEY", ex.Message);
        Assert.Contains("TILESIZE", ex.Message);
    }

    [Fact]
    public void Cells_outside_the_map_or_on_blocked_tiles_are_reported() {
        var bytes = Build(20, 12, blocked: (x, y) => x == 0 || y == 0 || x == 19 || y == 11);
        var info = MapFile.Inspect(bytes, "t");

        var problems = MapFile.CheckCells(bytes, info, "t", [
            new MapCell("spawn", 5, 5, true),
            new MapCell("arrival", 0, 3, true),
            new MapCell("npc", 7, 99, true),
            new MapCell("teleport", 19, 5, false),
            new MapCell("teleport", 20, 5, false),
        ]);

        Assert.Equal(3, problems.Count);
        Assert.Contains("arrival (0,3) is on a blocked cell", problems[0]);
        Assert.Contains("npc (7,99) is outside the 20x12 map", problems[1]);
        Assert.Contains("teleport (20,5) is outside", problems[2]);
    }

    [Fact]
    public void Good_cells_give_no_problem() {
        var bytes = Build(20, 12, blocked: (x, y) => x == 0);
        var info = MapFile.Inspect(bytes, "t");

        Assert.Empty(MapFile.CheckCells(bytes, info, "t", [new MapCell("spawn", 5, 5, true), new MapCell("teleport", 0, 5, false)]));
    }

    [Fact]
    public void The_generator_script_documents_the_same_format() {
        var script = File.ReadAllText(TestSupport.SourceFile("..", "..", "tools", "noc-gen", "gen_maps.py"));

        Assert.Contains("HEADER = 256", script);
        Assert.Contains("MIN_TILE = 9", script);
        Assert.Contains("MAX_SIDE = 1000", script);
        Assert.Contains("0x80", script);
    }
}

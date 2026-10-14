using System.Text.Json;
using Google.Protobuf;
using Mmorpg.Network;
using Xunit;

namespace NocthemarTests;

/// <summary>
/// T23: the canonical NocStateUpdated message is serialized by C# and compared byte for byte with the committed fixture;
/// TS (tools/proto-roundtrip/decode.ts) and Python (decode.py) decode the very same file in the proto-roundtrip workflow.
/// </summary>
public class ProtoFixtureTests {
    private const string BinName = "noc_state_updated.bin";
    private const string ExpectedName = "expected.json";

    // int64 values beyond 32 bits so that a language mapping int64 to a 32-bit or double value would be caught.
    private static ServerMessage Canonical() => new() {
        NocStateUpdated = new NocStateUpdated {
            State = new NocSnapshot { Level = 7, Xp = 4_294_967_297L, XpToNext = 8_589_934_592L, Mana = 45, MaxMana = 170 },
        },
    };

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "proto", name);

    [Fact]
    public void Csharp_encoding_is_identical_to_the_committed_fixture() {
        var encoded = Canonical().ToByteArray();

        // The workflow uploads this copy so the TS job decodes bytes produced by C#, not only the committed file.
        var outDir = Environment.GetEnvironmentVariable("NOC_FIXTURE_OUT");
        if (!string.IsNullOrEmpty(outDir)) {
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, BinName), encoded);
        }

        Assert.Equal(File.ReadAllBytes(FixturePath(BinName)), encoded);
    }

    [Fact]
    public void Csharp_decoding_of_the_fixture_matches_expected_json() {
        var message = ServerMessage.Parser.ParseFrom(File.ReadAllBytes(FixturePath(BinName)));
        using var expected = JsonDocument.Parse(File.ReadAllText(FixturePath(ExpectedName)));
        var root = expected.RootElement;
        var state = root.GetProperty("state");

        Assert.Equal(ServerMessage.PayloadOneofCase.NocStateUpdated, message.PayloadCase);
        Assert.Equal(root.GetProperty("payload").GetString(), "noc_state_updated");
        Assert.Equal(state.GetProperty("level").GetInt32(), message.NocStateUpdated.State.Level);
        Assert.Equal(state.GetProperty("xp").GetString(), message.NocStateUpdated.State.Xp.ToString());
        Assert.Equal(state.GetProperty("xp_to_next").GetString(), message.NocStateUpdated.State.XpToNext.ToString());
        Assert.Equal(state.GetProperty("mana").GetInt32(), message.NocStateUpdated.State.Mana);
        Assert.Equal(state.GetProperty("max_mana").GetInt32(), message.NocStateUpdated.State.MaxMana);
    }

    [Fact]
    public void Fixture_reencodes_to_itself() {
        var bytes = File.ReadAllBytes(FixturePath(BinName));

        Assert.Equal(bytes, ServerMessage.Parser.ParseFrom(bytes).ToByteArray());
    }
}

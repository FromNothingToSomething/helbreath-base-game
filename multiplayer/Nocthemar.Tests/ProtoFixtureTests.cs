using System.Text.Json;
using Google.Protobuf;
using Mmorpg.Network;
using Xunit;

namespace NocthemarTests;

/// <summary>
/// T23/T29: canonical messages are serialized by C# and compared byte for byte with the committed fixtures;
/// TS (tools/proto-roundtrip/decode.ts) and Python (decode.py) decode the very same files in the proto-roundtrip workflow.
/// </summary>
public class ProtoFixtureTests {
    public const string NocStateUpdatedName = "noc_state_updated";
    public const string AuthenticateRequestName = "authenticate_request";

    // int64 values beyond 32 bits so that a language mapping int64 to a 32-bit or double value would be caught.
    private static ServerMessage CanonicalNocStateUpdated() => new() {
        NocStateUpdated = new NocStateUpdated {
            State = new NocSnapshot { Level = 7, Xp = 4_294_967_297L, XpToNext = 8_589_934_592L, Mana = 45, MaxMana = 170, RaceId = 3 },
        },
    };

    // Non-ASCII name: the UTF-8 encoding must agree across the three implementations.
    private static ClientMessage CanonicalAuthenticateRequest() => new() {
        AuthenticateRequest = new AuthenticateRequest {
            Id = "0f8fad5b-d9cb-469f-a165-70867728950e",
            CharacterName = "Zoë-Ægir",
            Creation = new NocCharacterCreation { RaceId = 5, Gender = 1, SkinColor = 2, HairStyleIndex = 5, UnderwearColorIndex = 7 },
        },
    };

    private static IMessage Canonical(string name) => name switch {
        NocStateUpdatedName => CanonicalNocStateUpdated(),
        AuthenticateRequestName => CanonicalAuthenticateRequest(),
        _ => throw new ArgumentException(name),
    };

    public static TheoryData<string> Names => new() { NocStateUpdatedName, AuthenticateRequestName };

    private static string FixturePath(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "proto", file);

    [Theory]
    [MemberData(nameof(Names))]
    public void Csharp_encoding_is_identical_to_the_committed_fixture(string name) {
        var encoded = Canonical(name).ToByteArray();

        // The workflow uploads this copy so the TS and Python jobs decode bytes produced by C#, not only the committed files.
        var outDir = Environment.GetEnvironmentVariable("NOC_FIXTURE_OUT");
        if (!string.IsNullOrEmpty(outDir)) {
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, name + ".bin"), encoded);
        }

        Assert.Equal(File.ReadAllBytes(FixturePath(name + ".bin")), encoded);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Fixture_reencodes_to_itself(string name) {
        var bytes = File.ReadAllBytes(FixturePath(name + ".bin"));

        Assert.Equal(bytes, Parse(name, bytes).ToByteArray());
    }

    [Fact]
    public void Csharp_decoding_of_the_noc_state_updated_fixture_matches_its_json() {
        var message = ServerMessage.Parser.ParseFrom(File.ReadAllBytes(FixturePath(NocStateUpdatedName + ".bin")));
        using var expected = JsonDocument.Parse(File.ReadAllText(FixturePath(NocStateUpdatedName + ".json")));
        var root = expected.RootElement;
        var state = root.GetProperty("state");

        Assert.Equal(ServerMessage.PayloadOneofCase.NocStateUpdated, message.PayloadCase);
        Assert.Equal("noc_state_updated", root.GetProperty("payload").GetString());
        Assert.Equal(state.GetProperty("level").GetInt32(), message.NocStateUpdated.State.Level);
        Assert.Equal(state.GetProperty("xp").GetString(), message.NocStateUpdated.State.Xp.ToString());
        Assert.Equal(state.GetProperty("xp_to_next").GetString(), message.NocStateUpdated.State.XpToNext.ToString());
        Assert.Equal(state.GetProperty("mana").GetInt32(), message.NocStateUpdated.State.Mana);
        Assert.Equal(state.GetProperty("max_mana").GetInt32(), message.NocStateUpdated.State.MaxMana);
        Assert.Equal(state.GetProperty("race_id").GetInt32(), message.NocStateUpdated.State.RaceId);
    }

    [Fact]
    public void Csharp_decoding_of_the_authenticate_request_fixture_matches_its_json() {
        var message = ClientMessage.Parser.ParseFrom(File.ReadAllBytes(FixturePath(AuthenticateRequestName + ".bin")));
        using var expected = JsonDocument.Parse(File.ReadAllText(FixturePath(AuthenticateRequestName + ".json")));
        var root = expected.RootElement;
        var creation = root.GetProperty("creation");

        Assert.Equal(ClientMessage.PayloadOneofCase.AuthenticateRequest, message.PayloadCase);
        Assert.Equal("authenticate_request", root.GetProperty("payload").GetString());
        Assert.Equal(root.GetProperty("id").GetString(), message.AuthenticateRequest.Id);
        Assert.Equal(root.GetProperty("character_name").GetString(), message.AuthenticateRequest.CharacterName);
        Assert.Equal(creation.GetProperty("race_id").GetInt32(), message.AuthenticateRequest.Creation.RaceId);
        Assert.Equal(creation.GetProperty("gender").GetInt32(), message.AuthenticateRequest.Creation.Gender);
        Assert.Equal(creation.GetProperty("skin_color").GetInt32(), message.AuthenticateRequest.Creation.SkinColor);
        Assert.Equal(creation.GetProperty("hair_style_index").GetInt32(), message.AuthenticateRequest.Creation.HairStyleIndex);
        Assert.Equal(creation.GetProperty("underwear_color_index").GetInt32(), message.AuthenticateRequest.Creation.UnderwearColorIndex);
    }

    private static IMessage Parse(string name, byte[] bytes) => name switch {
        NocStateUpdatedName => ServerMessage.Parser.ParseFrom(bytes),
        AuthenticateRequestName => ClientMessage.Parser.ParseFrom(bytes),
        _ => throw new ArgumentException(name),
    };
}

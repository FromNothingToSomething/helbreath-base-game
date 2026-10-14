using Google.Protobuf;
using Mmorpg.Network;
using Server.Utils;
using Xunit;

namespace NocthemarTests;

/// <summary>T15: wire contract of nocthemar.proto and its two hooks in network.proto.</summary>
public class NocProtocolTests {
    [Fact]
    public void Server_message_carries_noc_state_updated_as_field_100() {
        var field = ServerMessage.Descriptor.FindFieldByName("noc_state_updated");

        Assert.NotNull(field);
        Assert.Equal(100, field.FieldNumber);
        Assert.Equal("payload", field.ContainingOneof.Name);
    }

    [Fact]
    public void Initial_state_carries_noc_as_field_28() {
        var field = InitialState.Descriptor.FindFieldByName("noc");

        Assert.NotNull(field);
        Assert.Equal(28, field.FieldNumber);
    }

    [Fact]
    public void Neighbouring_existing_numbers_are_untouched() {
        Assert.Equal(63, ServerMessage.Descriptor.FindFieldByName("logout_cancelled").FieldNumber);
        Assert.Equal(28, ServerMessage.Descriptor.FindFieldByName("hp_updated").FieldNumber);
        Assert.Equal(27, InitialState.Descriptor.FindFieldByName("npc_directory").FieldNumber);
        Assert.Equal(26, InitialState.Descriptor.FindFieldByName("underwear_color_index").FieldNumber);
    }

    [Fact]
    public void Snapshot_field_numbers_are_stable() {
        var d = NocSnapshot.Descriptor;

        Assert.Equal(1, d.FindFieldByName("level").FieldNumber);
        Assert.Equal(2, d.FindFieldByName("xp").FieldNumber);
        Assert.Equal(3, d.FindFieldByName("xp_to_next").FieldNumber);
        Assert.Equal(4, d.FindFieldByName("mana").FieldNumber);
        Assert.Equal(5, d.FindFieldByName("max_mana").FieldNumber);
    }

    [Fact]
    public void Snapshot_carries_race_id_as_field_6() {
        Assert.Equal(6, NocSnapshot.Descriptor.FindFieldByName("race_id").FieldNumber);
    }

    [Fact]
    public void Character_creation_field_numbers_are_stable() {
        var d = NocCharacterCreation.Descriptor;

        Assert.Equal(1, d.FindFieldByName("race_id").FieldNumber);
        Assert.Equal(2, d.FindFieldByName("gender").FieldNumber);
        Assert.Equal(3, d.FindFieldByName("skin_color").FieldNumber);
        Assert.Equal(4, d.FindFieldByName("hair_style_index").FieldNumber);
        Assert.Equal(5, d.FindFieldByName("underwear_color_index").FieldNumber);
    }

    [Fact]
    public void Authenticate_request_carries_creation_as_field_3_and_keeps_fields_1_and_2() {
        var d = AuthenticateRequest.Descriptor;

        Assert.Equal(1, d.FindFieldByName("id").FieldNumber);
        Assert.Equal(2, d.FindFieldByName("character_name").FieldNumber);
        Assert.Equal(3, d.FindFieldByName("creation").FieldNumber);
    }

    [Fact]
    public void Player_entered_range_carries_race_id_as_field_19_after_the_existing_ones() {
        var d = PlayerEnteredRange.Descriptor;

        Assert.Equal(18, d.FindFieldByName("cast_speed_ms").FieldNumber);
        Assert.Equal(19, d.FindFieldByName("race_id").FieldNumber);
    }

    [Fact]
    public void Authenticate_request_with_creation_round_trips() {
        var request = new AuthenticateRequest {
            Id = "0f8fad5b-d9cb-469f-a165-70867728950e",
            CharacterName = "Zoë-Ægir",
            Creation = new NocCharacterCreation { RaceId = 5, Gender = 1, SkinColor = 2, HairStyleIndex = 5, UnderwearColorIndex = 7 },
        };

        var back = AuthenticateRequest.Parser.ParseFrom(request.ToByteArray());

        Assert.Equal(request, back);
        Assert.Equal(5, back.Creation.RaceId);
    }

    [Fact]
    public void Authenticate_request_from_an_older_client_has_no_creation() {
        var old = new AuthenticateRequest { Id = "abc", CharacterName = "Old" };

        var back = AuthenticateRequest.Parser.ParseFrom(old.ToByteArray());

        Assert.Null(back.Creation);
        Assert.Equal("Old", back.CharacterName);
    }

    [Fact]
    public void Race_id_zero_is_not_serialized_so_the_old_wire_layout_is_unchanged() {
        var withoutRace = NetworkManager.CreateNocSnapshot(7, 1234, 5000, 56, 160);
        var withRace = withoutRace.Clone();
        withRace.RaceId = 3;

        Assert.Equal(0, withoutRace.RaceId);
        Assert.Equal(withoutRace.CalculateSize() + 2, withRace.CalculateSize()); // tag 0x30 + value
        Assert.Equal(3, NocSnapshot.Parser.ParseFrom(withRace.ToByteArray()).RaceId);
    }

    [Fact]
    public void Noc_state_updated_round_trips_and_uses_tag_100_on_the_wire() {
        var message = NetworkManager.CreateNocStateUpdated(NetworkManager.CreateNocSnapshot(7, 1234, 5000, 56, 160));

        var bytes = message.ToByteArray();
        var back = ServerMessage.Parser.ParseFrom(bytes);

        // Tag = (100 << 3) | 2 = 802 -> varint A2 06.
        Assert.Equal(0xA2, bytes[0]);
        Assert.Equal(0x06, bytes[1]);
        Assert.Equal(ServerMessage.PayloadOneofCase.NocStateUpdated, back.PayloadCase);
        Assert.Equal(message, back);
        Assert.Equal(7, back.NocStateUpdated.State.Level);
        Assert.Equal(1234L, back.NocStateUpdated.State.Xp);
        Assert.Equal(5000L, back.NocStateUpdated.State.XpToNext);
        Assert.Equal(56, back.NocStateUpdated.State.Mana);
        Assert.Equal(160, back.NocStateUpdated.State.MaxMana);
    }

    [Fact]
    public void Initial_state_round_trips_with_the_noc_block() {
        var state = new InitialState { PlayerId = 9, Noc = NetworkManager.CreateNocSnapshot(3, 10, 400, 5, 120) };

        var back = InitialState.Parser.ParseFrom(state.ToByteArray());

        Assert.Equal(state, back);
        Assert.Equal(3, back.Noc.Level);
    }

    [Fact]
    public void Initial_state_without_noc_has_no_noc_block() {
        var back = InitialState.Parser.ParseFrom(new InitialState { PlayerId = 9 }.ToByteArray());

        Assert.Null(back.Noc);
    }

    [Fact]
    public void Large_xp_values_survive_the_wire() {
        var back = NocSnapshot.Parser.ParseFrom(NetworkManager.CreateNocSnapshot(100, long.MaxValue, 0, 0, 0).ToByteArray());

        Assert.Equal(long.MaxValue, back.Xp);
    }
}

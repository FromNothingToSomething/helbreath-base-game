using Google.Protobuf;
using Mmorpg.Network;
using Server.Nocthemar.Net;
using Server.Utils;
using Xunit;

namespace NocthemarTests;

/// <summary>T32: the race travels in NocSnapshot (self) and PlayerEnteredRange (observers); the one-NocStateUpdated-per-tick rule still holds.</summary>
public class RaceSnapshotTests {
    private static readonly Server.Nocthemar.Data.RaceConfig Races = TestSupport.ShippedRaces();

    private static int NocUpdates(List<ServerMessage> sent) => sent.Count(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated);

    [Fact]
    public void The_local_snapshot_carries_the_race() {
        var p = TestSupport.NewPlayer(null, Races);
        p.ApplyNewCharacter(new NewCharacterChoice(4, 0, 0, 0, 0));

        Assert.Equal(4, p.CreateNocSnapshot().RaceId);
    }

    [Fact]
    public void Observers_receive_the_race_in_player_entered_range() {
        var p = TestSupport.NewPlayer(null, Races);
        p.ApplyNewCharacter(new NewCharacterChoice(5, 0, 0, 0, 0));

        Assert.Equal(5, NetworkManager.ToPlayerEnteredRangeSnapshot(p).RaceId);
    }

    [Fact]
    public void The_race_survives_the_wire() {
        var p = TestSupport.NewPlayer(null, Races);
        p.ApplyNewCharacter(new NewCharacterChoice(3, 0, 0, 0, 0));

        var back = PlayerEnteredRange.Parser.ParseFrom(NetworkManager.ToPlayerEnteredRangeSnapshot(p).ToByteArray());
        var noc = NocSnapshot.Parser.ParseFrom(p.CreateNocSnapshot().ToByteArray());

        Assert.Equal(3, back.RaceId);
        Assert.Equal(3, noc.RaceId);
    }

    [Fact]
    public void A_race_change_grant_and_mana_change_in_one_tick_send_one_state_carrying_the_race() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent, Races);

        p.ApplyRace(Races.Races.Single(r => r.Id == 2));
        p.GrantXp(1);
        p.ApplyRace(Races.Races.Single(r => r.Id == 3));
        p.TryFlushNocUpdates();

        Assert.Equal(1, NocUpdates(sent));
        var state = sent.Single(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated).NocStateUpdated.State;
        Assert.Equal(3, state.RaceId);
        Assert.False(p.TryFlushNocUpdates());
    }

    [Fact]
    public void Over_many_ticks_with_race_changes_there_is_never_more_than_one_update_per_tick() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent, Races);

        for (var tick = 0; tick < 60; tick++) {
            var before = NocUpdates(sent);
            p.ApplyRace(Races.Races.Single(r => r.Id == tick % 6));
            p.GrantXp(tick % 3);
            p.TryFlushNocUpdates();

            Assert.InRange(NocUpdates(sent) - before, 0, 1);
        }
    }

    [Fact]
    public void An_unchanged_race_reapplied_sends_nothing() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent, Races);

        p.ApplyRace(Races.Races.Single(r => r.Id == p.RaceId));

        Assert.False(p.TryFlushNocUpdates());
        Assert.Equal(0, NocUpdates(sent));
    }

    [Fact]
    public void The_wire_stays_unchanged_for_the_default_race() {
        var plain = NetworkManager.CreateNocSnapshot(7, 1234, 5000, 56, 160);
        var raceZero = NetworkManager.CreateNocSnapshot(7, 1234, 5000, 56, 160, 0);

        Assert.Equal(plain.ToByteArray(), raceZero.ToByteArray());
    }
}

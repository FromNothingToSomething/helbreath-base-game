using Mmorpg.Network;
using Xunit;

namespace NocthemarTests;

/// <summary>T17: progression updates reach the client at most once per tick and player, last state winning.</summary>
public class NocUpdateTests {
    private const long T0 = 1_700_000_000_000;
    private static readonly Server.Nocthemar.Data.ProgressionConfig Config = TestSupport.Progression();

    private static long Needed(int level) {
        Assert.True(Config.TryGetXpToNext(level, out var xp));
        return xp;
    }

    private static List<NocStateUpdated> NocUpdates(List<ServerMessage> sent) =>
        sent.Where(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated).Select(m => m.NocStateUpdated).ToList();

    [Fact]
    public void Nothing_pending_sends_nothing() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent);

        Assert.False(p.TryFlushNocUpdates());
        Assert.Empty(sent);
    }

    [Fact]
    public void An_xp_gain_sends_exactly_one_update_then_nothing() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent);

        p.GrantXp(10);
        Assert.True(p.TryFlushNocUpdates());
        Assert.False(p.TryFlushNocUpdates());

        var updates = NocUpdates(sent);
        Assert.Single(updates);
        Assert.Equal(10L, updates[0].State.Xp);
        Assert.Equal(1, updates[0].State.Level);
        Assert.Equal(Needed(1), updates[0].State.XpToNext);
        Assert.Equal(100, updates[0].State.Mana);
        Assert.Equal(100, updates[0].State.MaxMana);
        Assert.Single(sent);
    }

    [Fact]
    public void Several_changes_inside_one_tick_collapse_into_the_last_state() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent);

        p.GrantXp(10);
        p.GrantXp(20);
        p.GrantXp(Needed(1)); // level-up in the same tick
        p.GrantXp(5);
        p.TryFlushNocUpdates();

        var updates = NocUpdates(sent);
        Assert.Single(updates);
        Assert.Equal(2, updates[0].State.Level);
        Assert.Equal(p.Xp, updates[0].State.Xp);
        Assert.Equal(35L, updates[0].State.Xp);
        Assert.Equal(Needed(2), updates[0].State.XpToNext);
    }

    [Fact]
    public void Over_many_ticks_a_player_never_gets_more_than_one_update_per_tick() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent);
        var ticksWithChange = 0;

        for (var tick = 0; tick < 50; tick++) {
            var grants = tick % 5; // 0..4 grants in this tick
            for (var g = 0; g < grants; g++) {
                p.GrantXp(1);
            }

            var before = NocUpdates(sent).Count;
            p.TryFlushNocUpdates();
            var delta = NocUpdates(sent).Count - before;

            Assert.InRange(delta, 0, 1);
            ticksWithChange += grants > 0 ? 1 : 0;
        }

        Assert.Equal(ticksWithChange, NocUpdates(sent).Count);
    }

    [Fact]
    public void A_level_up_also_sends_one_hp_update_with_the_new_maximum() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent);

        p.GrantXp(Needed(1));
        p.TryFlushNocUpdates();

        var hp = sent.Where(m => m.PayloadCase == ServerMessage.PayloadOneofCase.HpUpdated).ToList();
        Assert.Single(hp);
        Assert.Equal(Config.MaxHpAt(2), hp[0].HpUpdated.MaxHp);
        Assert.Equal(Config.MaxHpAt(2), hp[0].HpUpdated.Hp);
        Assert.Single(NocUpdates(sent));
        Assert.Equal(2, sent.Count); // exactly 1 NocStateUpdated + 1 HpUpdated, nothing else
    }

    [Fact]
    public void Several_level_ups_in_one_tick_still_send_exactly_one_of_each() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent);

        p.GrantXp(Needed(1));
        p.GrantXp(Needed(2));
        p.GrantXp(Needed(3));
        p.TryFlushNocUpdates();

        Assert.Equal(2, sent.Count);
        Assert.Equal(1, sent.Count(m => m.PayloadCase == ServerMessage.PayloadOneofCase.HpUpdated));
        Assert.Equal(1, sent.Count(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated));
        Assert.Equal(4, NocUpdates(sent)[0].State.Level);
        Assert.Equal(Config.MaxHpAt(4), sent.Single(m => m.PayloadCase == ServerMessage.PayloadOneofCase.HpUpdated).HpUpdated.MaxHp);
    }

    [Fact]
    public void Snapshot_and_xp_threshold_are_built_at_flush_not_per_tick() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(1, 0, 0, sent);
        p.TickMana(T0);
        var baseline = p.NocSnapshotsBuilt;

        for (var i = 1; i <= 200; i++) {
            p.TickMana(T0 + i * 50); // mana changes every 10 ticks, no flush yet
        }

        p.GrantXp(5);
        p.GrantXp(Needed(1)); // level-up
        Assert.Equal(baseline, p.NocSnapshotsBuilt);

        p.TryFlushNocUpdates();
        Assert.Equal(baseline + 1, p.NocSnapshotsBuilt);

        for (var i = 0; i < 1000; i++) {
            p.TryFlushNocUpdates();
        }

        Assert.Equal(baseline + 1, p.NocSnapshotsBuilt);
    }

    [Fact]
    public void An_xp_gain_without_level_up_sends_no_hp_update() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent);

        p.GrantXp(5);
        p.TryFlushNocUpdates();

        Assert.DoesNotContain(sent, m => m.PayloadCase == ServerMessage.PayloadOneofCase.HpUpdated);
    }

    [Fact]
    public void Non_positive_grants_send_nothing() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent);

        p.GrantXp(0);
        p.GrantXp(-5);

        Assert.False(p.TryFlushNocUpdates());
        Assert.Empty(sent);
    }

    [Fact]
    public void Xp_discarded_at_max_level_sends_nothing() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(Config.MaxLevel, 0, 0, sent);

        p.GrantXp(1_000_000);

        Assert.False(p.TryFlushNocUpdates());
        Assert.Empty(sent);
    }

    [Fact]
    public void Max_level_snapshot_reports_zero_xp_to_next() {
        var p = TestSupport.NewPlayer(Config.MaxLevel, 0, 0);

        Assert.Equal(0L, p.CreateNocSnapshot().XpToNext);
        Assert.Equal(Config.MaxLevel, p.CreateNocSnapshot().Level);
    }

    [Fact]
    public void Mana_regeneration_sends_an_update_only_when_mana_changed() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(1, 0, 0, sent);
        p.TickMana(T0);
        p.TryFlushNocUpdates();
        Assert.Empty(sent); // baseline tick changes nothing

        p.TickMana(T0 + 1000); // +2 mana
        p.TryFlushNocUpdates();
        var updates = NocUpdates(sent);
        Assert.Single(updates);
        Assert.Equal(2, updates[0].State.Mana);

        p.TickMana(T0 + 1100); // 0.2 -> no whole point yet
        p.TryFlushNocUpdates();
        Assert.Single(NocUpdates(sent));
    }

    [Fact]
    public void Full_mana_never_triggers_updates() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent);
        p.TickMana(T0);

        for (var i = 1; i <= 100; i++) {
            p.TickMana(T0 + i * 50);
            p.TryFlushNocUpdates();
        }

        Assert.Empty(sent);
    }

    [Fact]
    public void Changes_while_disconnected_are_dropped_because_the_reconnect_initial_state_carries_them() {
        var sent = new List<ServerMessage>();
        var p = TestSupport.NewPlayer(sent);
        p.DetachConnection();

        p.GrantXp(Needed(1));
        Assert.False(p.TryFlushNocUpdates());

        p.AttachConnection(sent.Add, _ => { });
        Assert.False(p.TryFlushNocUpdates());
        Assert.Empty(sent);
        Assert.Equal(2, p.CreateNocSnapshot().Level);
    }

    [Fact]
    public void Idle_flush_does_not_allocate() {
        var p = TestSupport.NewPlayer();
        long sink = 0;
        for (var i = 0; i < 20_000; i++) {
            sink += p.TryFlushNocUpdates() ? 1 : 0;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000_000; i++) {
            sink += p.TryFlushNocUpdates() ? 1 : 0;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, sink);
        Assert.True(allocated <= 16 * 1024, $"idle flush allocated {allocated} bytes over 1,000,000 calls");
    }
}

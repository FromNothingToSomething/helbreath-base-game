using Server.Nocthemar.Core;
using Server.Nocthemar.Net;
using Mmorpg.Network;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T27: XpGainMult scales the XP of kills only; debug and admin grants stay raw.</summary>
public class KillXpTests {
    private static readonly Server.Nocthemar.Data.ProgressionConfig Config = TestSupport.Progression();

    private static GameWorldPlayer Valdornien() => TestSupport.NewPlayer(races: TestSupport.ShippedRaces());

    [Fact]
    public void A_valdornien_kill_is_worth_ten_percent_more() {
        var p = Valdornien();

        Assert.Equal(11, p.ScaleKillXp(10));
        p.GrantKillXp(10);

        Assert.Equal(11L, p.Xp);
    }

    [Fact]
    public void Without_a_race_bonus_a_kill_is_worth_exactly_its_base_xp() {
        var p = TestSupport.NewPlayer();

        p.GrantKillXp(10);

        Assert.Equal(10L, p.Xp);
        Assert.Equal(1.0, p.Stats.XpGainMult);
    }

    [Theory]
    [InlineData(1, 1)] // 1.1 -> 1
    [InlineData(3, 3)] // 3.3 -> 3
    [InlineData(7, 8)] // 7.7 -> 8
    [InlineData(100, 110)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void Kill_xp_is_rounded_to_the_nearest_whole_number(int baseXp, long expected) {
        Assert.Equal(expected, Valdornien().ScaleKillXp(baseXp));
    }

    [Fact]
    public void A_debug_grant_stays_raw_for_a_player_with_a_gain_bonus() {
        var p = Valdornien();
        var dispatcher = new NocDispatcher(debugEnabled: true, _ => { });

        Assert.True(dispatcher.Handle(p, new ClientMessage { NocGrantXpRequest = new NocGrantXpRequest { Amount = 50 } }));

        Assert.Equal(50L, p.Xp);
    }

    [Fact]
    public void GrantXp_itself_stays_raw() {
        var p = Valdornien();

        p.GrantXp(50);

        Assert.Equal(50L, p.Xp);
    }

    [Fact]
    public void Any_xp_gain_modifier_applies_to_kills() {
        var p = TestSupport.NewPlayer();
        p.Stats.Add(new StatModifier(StatId.XpGainMult, ModifierKind.PercentAdd, 1.0, ModifierSources.Buff(1)));

        Assert.Equal(20, p.ScaleKillXp(10));
    }

    [Fact]
    public void A_gain_of_minus_one_hundred_percent_gives_no_xp_and_no_update() {
        var p = TestSupport.NewPlayer();
        p.Stats.Add(new StatModifier(StatId.XpGainMult, ModifierKind.PercentAdd, -1.0, ModifierSources.Buff(1)));

        Assert.Equal(0, p.ScaleKillXp(10));
        Assert.Equal(0, p.GrantKillXp(10));
        Assert.Equal(0L, p.Xp);
        Assert.False(p.TryFlushNocUpdates());
    }

    [Fact]
    public void The_largest_base_xp_does_not_overflow() {
        var p = Valdornien();

        Assert.Equal((long)Math.Round(int.MaxValue * 1.1, MidpointRounding.AwayFromZero), p.ScaleKillXp(int.MaxValue));
    }

    [Fact]
    public void A_scaled_kill_can_level_the_killer_up() {
        var p = Valdornien();
        Assert.True(Config.TryGetXpToNext(1, out var needed));

        var gained = p.GrantKillXp((int)needed);

        Assert.Equal(1, gained);
        Assert.Equal(2, p.Level);
        Assert.Equal((long)Math.Round(needed * 1.1, MidpointRounding.AwayFromZero) - needed, p.Xp);
    }

    // GameWorld cannot be built without a map and monsters, so the wiring is pinned at the source: kills go through GrantKillXp.
    [Fact]
    public void The_monster_kill_hook_uses_the_scaled_grant() {
        var source = File.ReadAllText(FindSource("World", "Game", "GameWorld.cs"));
        var start = source.IndexOf("public void OnMonsterKilledByPlayer", StringComparison.Ordinal);
        Assert.True(start > 0, "OnMonsterKilledByPlayer not found");
        var body = source.Substring(start, source.IndexOf("TryGetPlayerBySessionId", start, StringComparison.Ordinal) - start);

        Assert.Contains("killer.GrantKillXp(", body);
        Assert.DoesNotContain("killer.GrantXp(", body);
    }

    private static string FindSource(params string[] relative) {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine(new[] { dir.FullName, "server" }.Concat(relative).ToArray());
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        throw new FileNotFoundException("server/" + string.Join('/', relative) + " not found above " + AppContext.BaseDirectory);
    }
}

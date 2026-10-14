using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T26: applying a race to a player: modifiers, idempotence, bounded HP and mana, persistence.</summary>
public class RaceApplyTests {
    private static readonly RaceConfig Races = TestSupport.ShippedRaces();
    private static readonly ProgressionConfig Progression = TestSupport.Progression();

    private static RaceDefinition Race(string key) => Races.Races.Single(r => r.Key == key);

    private static GameWorldPlayer Player(List<ServerMessage>? sent = null) => TestSupport.NewPlayer(sent, Races);

    private static PlayerPersistenceState Saved(NocState? noc) =>
        new("aresden", 1, 2, 300, 1000, 600, 1, 20, 500, 0, false, false, false, CharacterName: "T", SchemaVersion: CharacterSchema.Current, Noc: noc);

    private static int Count(List<ServerMessage> sent, ServerMessage.PayloadOneofCase kind) => sent.Count(m => m.PayloadCase == kind);

    [Fact]
    public void A_new_player_starts_as_the_default_race_with_full_hp_and_mana() {
        var p = Player();

        Assert.Equal(0, p.RaceId);
        Assert.Equal(1050, p.MaxHp); // Valdorniens: +5% on 1000
        Assert.Equal(1050, p.Hp);
        Assert.Equal(105, p.MaxMana);
        Assert.Equal(105, p.Mana);
        Assert.Equal(1.05, p.Stats.DamageMult, 12);
        Assert.Equal(1.10, p.Stats.XpGainMult, 12);
    }

    [Fact]
    public void Applying_another_race_replaces_the_modifiers_instead_of_stacking_them() {
        var p = Player();

        p.ApplyRace(Race("spectral"));

        Assert.Equal(1, p.RaceId);
        Assert.Equal(900, p.MaxHp); // -10%
        Assert.Equal(100, p.MaxMana); // no mana modifier: Valdorniens' +5% is gone
        Assert.Equal(1.0, p.Stats.DamageMult, 12);
        Assert.Equal(1.0, p.Stats.XpGainMult, 12);
        Assert.Equal(1.10, p.Stats.Get(StatId.MagicDamageMult), 12);
        Assert.Equal(0.15, p.Stats.Get(StatId.ResistCurse), 12);
        Assert.Equal(Race("spectral").Modifiers.Count, p.Stats.ModifierCount);
    }

    [Fact]
    public void Hp_and_mana_are_clamped_down_to_the_new_maxima() {
        var p = Player();
        Assert.Equal(1050, p.Hp);
        Assert.Equal(105, p.Mana);

        p.ApplyRace(Race("spectral"));

        Assert.Equal(900, p.Hp);
        Assert.Equal(100, p.Mana);
    }

    [Fact]
    public void Hp_and_mana_are_never_raised_by_a_race_change() {
        var p = Player();
        p.ApplyDamage(600); // 450 / 1050

        p.ApplyRace(Race("reanime")); // max HP 1000
        Assert.Equal(450, p.Hp);

        p.ApplyRace(Race("valdornien")); // max HP back to 1050
        Assert.Equal(450, p.Hp);
        Assert.Equal(1050, p.MaxHp);
    }

    [Fact]
    public void A_dead_player_stays_dead() {
        var p = Player();
        p.ApplyDamage(5000);
        Assert.True(p.IsDead);

        p.ApplyRace(Race("chimere"));

        Assert.True(p.IsDead);
        Assert.Equal(0, p.Hp);
    }

    [Fact]
    public void Applying_the_same_race_again_changes_nothing() {
        var sent = new List<ServerMessage>();
        var p = Player(sent);
        p.ApplyRace(Race("spectral"));
        Assert.True(p.TryFlushNocUpdates());
        sent.Clear();

        p.ApplyRace(Race("spectral"));
        p.ApplyRace(Race("spectral"));

        Assert.Equal(900, p.MaxHp);
        Assert.Equal(900, p.Hp);
        Assert.Equal(Race("spectral").Modifiers.Count, p.Stats.ModifierCount);
        Assert.False(p.TryFlushNocUpdates()); // nothing observable changed, nothing to tell the client
        Assert.Empty(sent);
    }

    [Fact]
    public void Going_back_to_a_previous_race_restores_its_exact_stats() {
        var p = Player();

        p.ApplyRace(Race("chimere"));
        p.ApplyRace(Race("lychen"));
        p.ApplyRace(Race("valdornien"));

        Assert.Equal(1050, p.MaxHp);
        Assert.Equal(105, p.MaxMana);
        Assert.Equal(Race("valdornien").Modifiers.Count, p.Stats.ModifierCount);
        Assert.Equal(1.0, p.Stats.Get(StatId.ArmorMult), 12);
    }

    [Fact]
    public void Other_sources_are_untouched_by_a_race_change() {
        var p = Player();
        p.Stats.Add(new StatModifier(StatId.DamageMult, ModifierKind.PercentAdd, 0.2, ModifierSources.Buff(7)));

        p.ApplyRace(Race("sang_mele"));

        Assert.Equal(1.0 + 0.2 + 0.1, p.Stats.DamageMult, 12);
        Assert.Equal(1, p.Stats.RemoveSource(ModifierSources.Buff(7)));
    }

    [Fact]
    public void A_race_change_is_reported_once_with_the_nocthemar_state_and_hp() {
        var sent = new List<ServerMessage>();
        var p = Player(sent);
        Assert.False(p.TryFlushNocUpdates()); // joining is not a change

        p.ApplyRace(Race("spectral"));

        Assert.True(p.TryFlushNocUpdates());
        Assert.Equal(1, Count(sent, ServerMessage.PayloadOneofCase.NocStateUpdated));
        Assert.Equal(1, Count(sent, ServerMessage.PayloadOneofCase.HpUpdated)); // max HP changed
        Assert.False(p.TryFlushNocUpdates());
    }

    [Fact]
    public void A_null_race_is_rejected() {
        Assert.Throws<ArgumentNullException>(() => Player().ApplyRace(null!));
    }

    [Fact]
    public void The_race_survives_level_ups() {
        var p = Player();

        p.GrantXp(1_000_000);

        Assert.True(p.Level > 1);
        Assert.Equal(0, p.RaceId);
        Assert.Equal((int)Math.Round(Progression.MaxHpAt(p.Level) * 1.05, MidpointRounding.AwayFromZero), p.MaxHp);
        Assert.Equal(p.MaxHp, p.Hp);
    }

    // --- persistence --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_saved_race_is_restored_with_its_modifiers_and_full_join_hp() {
        var p = Player();

        p.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 5, 20, 33, RaceId: 3)));

        Assert.Equal(3, p.RaceId);
        Assert.Equal(5, p.Level);
        Assert.Equal(Progression.MaxHpAt(5), p.MaxHp); // Réanimés have no HP modifier
        Assert.Equal(p.MaxHp, p.Hp);
        Assert.Equal(33, p.Mana);
        Assert.Equal(0.9, p.Stats.Get(StatId.MoveSpeedMult), 12);
        Assert.Equal(0.2, p.Stats.Get(StatId.ResistDeath), 12);
        Assert.Equal(Race("reanime").Modifiers.Count, p.Stats.ModifierCount);
    }

    [Fact]
    public void Restoring_a_valdornien_applies_the_bonus_to_the_level_derived_maxima() {
        var p = Player();

        p.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 5, 20, 33, RaceId: 0)));

        Assert.Equal(1260, p.MaxHp); // (1000 + 50 * 4) * 1.05
        Assert.Equal(1260, p.Hp);
        Assert.Equal(147, p.MaxMana); // (100 + 10 * 4) * 1.05
    }

    [Fact]
    public void Joining_does_not_queue_a_state_update() {
        var sent = new List<ServerMessage>();
        var p = Player(sent);

        p.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 5, 20, 33, RaceId: 4)));

        Assert.False(p.TryFlushNocUpdates());
        Assert.Empty(sent);
    }

    [Fact]
    public void A_missing_or_unknown_race_id_falls_back_to_the_default_race() {
        var missing = Player();
        missing.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 3, 0, 10, RaceId: null)));
        var unknown = Player();
        unknown.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 3, 0, 10, RaceId: 99)));
        var noBlock = Player();
        noBlock.ApplyPersistedState(Saved(null));

        foreach (var p in new[] { missing, unknown, noBlock }) {
            Assert.Equal(Races.DefaultRaceId, p.RaceId);
            Assert.Equal(Race("valdornien").Modifiers.Count, p.Stats.ModifierCount);
        }

        Assert.Equal(Races.DefaultRaceId, unknown.CreatePersistenceState("aresden").Noc!.RaceId);
    }

    [Fact]
    public void The_race_is_part_of_the_persisted_block_and_round_trips() {
        var first = Player();
        first.ApplyPersistedState(Saved(new NocState(NocState.CurrentV, 7, 55, 21, RaceId: 4)));
        var snapshot = first.CreatePersistenceState("aresden");

        var second = Player();
        second.ApplyPersistedState(snapshot);

        Assert.Equal(4, snapshot.Noc!.RaceId);
        Assert.Equal(snapshot.Noc, second.CreatePersistenceState("aresden").Noc);
        Assert.Equal(first.MaxHp, second.MaxHp);
        Assert.Equal(1.15, second.Stats.Get(StatId.ArmorMult), 12);
    }

    [Fact]
    public void Neutral_races_leave_every_stat_as_before_races_existed() {
        var p = TestSupport.NewPlayer();

        Assert.Equal(1000, p.MaxHp);
        Assert.Equal(100, p.MaxMana);
        Assert.Equal(1.0, p.Stats.DamageMult);
        Assert.Equal(0, p.Stats.ModifierCount);
    }
}

using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T47: armor and resistances, Heal, out-of-combat HP regeneration.</summary>
public class MitigationTests {
    private static StatPipeline Stats() => new(TestSupport.Progression(), 1);

    [Fact]
    public void Without_modifiers_the_damage_is_unchanged() {
        var s = Stats();

        Assert.Equal(37, DamageMitigation.Apply(37, DamageSchool.Physical, s));
        Assert.Equal(37, DamageMitigation.Apply(37, DamageSchool.Magic, s));
        Assert.Equal(0, DamageMitigation.Apply(0, DamageSchool.Physical, s));
    }

    [Fact]
    public void Armor_divides_physical_damage_only() {
        var s = Stats();
        s.Add(new StatModifier(StatId.ArmorMult, ModifierKind.PercentAdd, 0.15, 1));

        Assert.Equal(100, DamageMitigation.Apply(115, DamageSchool.Physical, s));
        Assert.Equal(115, DamageMitigation.Apply(115, DamageSchool.Magic, s));
    }

    [Fact]
    public void Resistance_applies_per_school_and_is_capped() {
        var s = Stats();
        s.Add(new StatModifier(StatId.ResistPhysical, ModifierKind.Flat, 0.25, 1));
        s.Add(new StatModifier(StatId.ResistMagic, ModifierKind.Flat, 0.95, 1));

        Assert.Equal(75, DamageMitigation.Apply(100, DamageSchool.Physical, s));
        Assert.Equal(20, DamageMitigation.Apply(100, DamageSchool.Magic, s));
    }

    [Fact]
    public void A_positive_hit_always_deals_at_least_one() {
        var s = Stats();
        s.Add(new StatModifier(StatId.ResistMagic, ModifierKind.Flat, 0.9, 1));

        Assert.Equal(1, DamageMitigation.Apply(1, DamageSchool.Magic, s));
    }

    [Fact]
    public void Player_mitigation_uses_its_own_stats() {
        var p = TestSupport.NewPlayer();
        p.Stats.Add(new StatModifier(StatId.ResistMagic, ModifierKind.Flat, 0.5, 1));

        Assert.Equal(50, p.MitigateDamage(100, DamageSchool.Magic));
        Assert.Equal(100, p.MitigateDamage(100, DamageSchool.Physical));
    }

    [Fact]
    public void Heal_is_scaled_clamped_and_ignores_the_dead() {
        var p = TestSupport.NewPlayer();
        p.ApplyDamage(500);
        p.Stats.Add(new StatModifier(StatId.HealReceivedMult, ModifierKind.PercentAdd, 0.5, 1));

        Assert.Equal(150, p.Heal(100));
        Assert.Equal(p.MaxHp - 350, p.Hp);
        Assert.Equal(350, p.Heal(10_000));
        Assert.Equal(p.MaxHp, p.Hp);
        Assert.Equal(0, p.Heal(10));
        Assert.Equal(0, p.Heal(-5));

        p.ApplyDamage(p.MaxHp);
        Assert.True(p.IsDead);
        Assert.Equal(0, p.Heal(100));
        Assert.Equal(0, p.Hp);
    }

    private static GameWorldPlayer Regen(double perSecond) {
        var p = TestSupport.NewPlayer();
        p.Stats.Add(new StatModifier(StatId.HpRegen, ModifierKind.Flat, perSecond, 1));
        return p;
    }

    private const long T0 = 1_760_000_000_000;

    [Fact]
    public void Without_regen_nothing_is_regained() {
        var p = TestSupport.NewPlayer();
        p.ApplyDamage(100);
        p.TickHp(T0);

        Assert.False(p.TickHp(T0 + 4000));
        Assert.False(p.TickHp(T0 + 9000));
        Assert.Equal(p.MaxHp - 100, p.Hp);
    }

    [Fact]
    public void Regen_waits_for_the_out_of_combat_delay() {
        var p = Regen(10);
        p.TickHp(T0);
        p.ApplyDamage(100);

        Assert.False(p.TickHp(T0 + 1000)); // damage seen now
        Assert.False(p.TickHp(T0 + 2000));
        Assert.False(p.TickHp(T0 + 5900));
        Assert.True(p.TickHp(T0 + 6000));
        Assert.True(p.Hp > p.MaxHp - 100);
    }

    [Fact]
    public void Regen_carries_fractions_and_stops_at_max() {
        var p = Regen(0.5);
        p.ApplyDamage(10);
        p.TickHp(T0);
        var now = T0;
        var start = p.Hp;
        for (var i = 0; i < 4; i++) {
            now += 1000;
            p.TickHp(now); // within the combat delay
        }

        Assert.Equal(start, p.Hp);
        for (var i = 0; i < 4; i++) {
            now += 1000;
            p.TickHp(now);
        }

        Assert.Equal(start + 2, p.Hp);
        for (var i = 0; i < 100; i++) {
            now += 1000;
            p.TickHp(now);
        }

        Assert.Equal(p.MaxHp, p.Hp);
    }

    [Fact]
    public void A_long_gap_credits_at_most_five_seconds() {
        var p = Regen(10);
        p.ApplyDamage(500);
        p.TickHp(T0);
        p.TickHp(T0 + 6000);
        var before = p.Hp;

        p.TickHp(T0 + 600_000);

        Assert.Equal(before + 50, p.Hp);
    }

    [Fact]
    public void The_dead_do_not_regenerate() {
        var p = Regen(10);
        p.TickHp(T0);
        p.ApplyDamage(p.MaxHp);

        Assert.False(p.TickHp(T0 + 20_000));
        Assert.Equal(0, p.Hp);
    }

    [Fact]
    public void Progression_validates_hp_regen() {
        const string tpl = """{"schemaVersion":1,"maxLevel":100,"xp":{"base":100,"exponent":1.6},"hp":{"base":1000,"perLevel":50,"regenPerSecond":REGEN},"mana":{"base":100,"perLevel":10,"regenPerSecond":2.0},"defaultMonsterXp":10}""";

        Assert.Equal(1.5, ProgressionConfig.Parse(tpl.Replace("REGEN", "1.5")).HpRegenPerSecond);
        Assert.Equal(0, ProgressionConfig.Parse(TestSupport.ProgressionJson).HpRegenPerSecond);
        Assert.ThrowsAny<Exception>(() => ProgressionConfig.Parse(tpl.Replace("REGEN", "-1")));
    }

    [Fact]
    public void Combat_paths_pass_the_damage_school() {
        var helpers = FindDir("Helpers");
        var casting = File.ReadAllText(Path.Combine(helpers, "Casting.cs"));
        var combat = File.ReadAllText(Path.Combine(helpers, "Combat.cs"));
        var visibility = File.ReadAllText(Path.Combine(helpers, "MonsterVisibility.cs"));

        Assert.Contains("DamageSchool.Magic", casting);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(casting, @"ApplyPlayerDamageToPlayer\([^;]*DamageSchool\.Magic\)").Count);
        Assert.Contains("school: DamageSchool.Magic", combat);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(visibility, @"MitigateForTarget\(wr").Count >= 2);
    }

    private static string FindDir(string name) {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine(dir.FullName, "server", name);
            if (Directory.Exists(candidate)) {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(name);
    }
}

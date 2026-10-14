using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Skills;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T49: the first shipped active skills (bone shield buff, black blessing heal) and timed buffs.</summary>
public class BuiltinHandlerTests {
    private const long Now = 1_760_000_000_000;

    private static NocContent Content() => TestSupport.ShippedContent(BuiltinSkillHandlers.Register(new SkillHandlerRegistry()));

    private static GameWorldPlayer Player(int classId, int level, params int[] skills) {
        var p = TestSupport.NewPlayer(null, null, Content());
        TestSupport.Restore(p, new NocState(NocState.CurrentV, level, 0, 150, RaceId: 0, ClassId: classId, Skills: skills));
        return p;
    }

    [Fact]
    public void The_shipped_catalog_binds_both_actives_to_registered_handlers() {
        var skills = TestSupport.ShippedSkills();

        Assert.True(skills.TryGet(1001, out var shield));
        Assert.True(skills.TryGet(1301, out var blessing));
        Assert.Equal(("buff_bone_shield", true, SkillKind.Active), (shield.Handler, shield.IsReady, shield.Kind));
        Assert.Equal(("heal_black_blessing", true, SkillKind.Active), (blessing.Handler, blessing.IsReady, blessing.Kind));
        Assert.True(shield.CooldownMs > 0 && shield.ManaCost > 0 && blessing.CooldownMs > 0 && blessing.ManaCost > 0);
        BuiltinSkillHandlers.Register(new SkillHandlerRegistry()).ValidateCatalog(skills);
    }

    [Fact]
    public void Bone_shield_adds_resistances_until_the_duration_ends() {
        var p = Player(0, 10, 1000, 1001);
        Assert.Equal(0.0, p.Stats.Get(StatId.ResistPhysical));

        Assert.Null(p.TryBeginSkillCast(1001, Now));

        Assert.Equal(0.30, p.Stats.Get(StatId.ResistPhysical), 12);
        Assert.Equal(0.30, p.Stats.Get(StatId.ResistMagic), 12);
        Assert.Equal(1, p.ActiveBuffCount);
        Assert.Equal(25, p.MitigateDamage(35, Server.Nocthemar.Core.DamageSchool.Physical));
        Assert.False(p.TickBuffs(Now + 7999));
        Assert.True(p.TickBuffs(Now + 8000));
        Assert.Equal(0.0, p.Stats.Get(StatId.ResistPhysical));
        Assert.Equal(0, p.ActiveBuffCount);
    }

    [Fact]
    public void Casting_again_refreshes_the_buff_without_stacking() {
        var p = Player(0, 10, 1000, 1001);
        Assert.Null(p.TryBeginSkillCast(1001, Now));

        Assert.Null(p.TryBeginSkillCast(1001, Now + 20_000));

        Assert.Equal(0.30, p.Stats.Get(StatId.ResistPhysical), 12);
        Assert.Equal(1, p.ActiveBuffCount);
        Assert.False(p.TickBuffs(Now + 27_000));
        Assert.True(p.TickBuffs(Now + 28_000));
    }

    [Fact]
    public void Black_blessing_heals_and_is_refused_at_full_hp() {
        var p = Player(3, 10, 1301);
        Assert.Equal("Nothing to heal.", p.TryBeginSkillCast(1301, Now));
        Assert.Equal(150, p.Mana);

        p.ApplyDamage(400);
        var hp = p.Hp;
        Assert.Null(p.TryBeginSkillCast(1301, Now));

        Assert.Equal(hp + 250, p.Hp);
        Assert.Equal(120, p.Mana);
    }

    [Fact]
    public void Abyssal_heritage_grows_with_kills_up_to_twenty_percent() {
        var p = Player(0, 24, 1020, 1022, 1026);
        Assert.Equal(1.0, p.Stats.Get(StatId.ArmorMult), 12);

        for (var i = 0; i < 5; i++) {
            p.RegisterKill();
        }

        Assert.Equal(1.05, p.Stats.Get(StatId.ArmorMult), 12);
        for (var i = 0; i < 40; i++) {
            p.RegisterKill();
        }

        Assert.Equal(1.20, p.Stats.Get(StatId.ArmorMult), 12);
        Assert.Equal(45, p.Kills);
    }

    [Fact]
    public void Abyssal_heritage_is_rebuilt_from_the_persisted_kills_and_dropped_by_a_respec() {
        var p = TestSupport.NewPlayer(null, null, Content());
        TestSupport.Restore(p, new NocState(NocState.CurrentV, 24, 0, 100, RaceId: 0, ClassId: 0, Skills: [1020, 1022, 1026], Kills: 12));

        Assert.Equal(1.12, p.Stats.Get(StatId.ArmorMult), 12);
        Assert.True(p.Respec());
        Assert.Equal(1.0, p.Stats.Get(StatId.ArmorMult), 12);
    }

    [Fact]
    public void Kills_without_the_skill_change_nothing() {
        var p = Player(0, 10, 1000);

        p.RegisterKill();

        Assert.Equal(1, p.Kills);
        Assert.Equal(1.15, p.Stats.Get(StatId.ArmorMult), 12);
    }

    [Fact]
    public void Handlers_without_an_actor_refuse() {
        var skill = TestSupport.ShippedSkills().Skills.Single(s => s.Id == 1001);
        var table = new SkillHookTable(Content().Handlers);
        table.Add(skill);

        Assert.False(table.RunSkill(1001, SkillHook.OnCast, 1, null, Now).Success);
    }

    [Fact]
    public void Buffs_are_ticked_by_the_world_and_the_server_registers_the_handlers() {
        var world = File.ReadAllText(FindSource("World", "Game", "GameWorld.cs"));
        var server = File.ReadAllText(FindSource("Server.cs"));

        Assert.Contains("player.TickBuffs(nowMs)", world);
        Assert.Contains("BuiltinSkillHandlers.Register(new SkillHandlerRegistry())", server);
    }

    private static string FindSource(params string[] relative) {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine(new[] { dir.FullName, "server" }.Concat(relative).ToArray());
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        throw new FileNotFoundException(string.Join('/', relative));
    }
}

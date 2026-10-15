using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Skills;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T48: casting learned active class skills (ids >= 1000): learned/ready, cooldown, mana cost, handler refusal.</summary>
public class SkillCastTests {
    private sealed class Caster(string id, bool ok = true) : ISkillHandler {
        public string Id { get; } = id;
        public SkillHook Hooks => SkillHook.OnCast;
        public int Calls;

        public SkillResult Execute(in SkillContext context) {
            Calls++;
            return ok ? SkillResult.Ok : SkillResult.Refuse("Nothing to heal.");
        }
    }

    private const long Now = 1_760_000_000_000;

    private static (GameWorldPlayer Player, Caster Handler) Setup(bool ok = true, int mana = 100, params int[] learned) {
        var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Skills.json")))!;
        foreach (var node in doc["skills"]!.AsArray()) {
            if (node!["id"]!.GetValue<int>() is 1001 or 1003) {
                node["status"] = "ready";
                node["handler"] = "test_cast";
                node["cooldownMs"] = 5000;
                node["manaCost"] = 20;
            }
        }

        var handler = new Caster("test_cast", ok);
        var skills = SkillConfig.Parse(doc.ToJsonString(), TestSupport.ShippedClasses());
        var content = new NocContent(TestSupport.ShippedClasses(), skills, TestSupport.Progression(), BuiltinSkillHandlers.Register(new SkillHandlerRegistry()).Register(handler));
        var p = TestSupport.NewPlayer(null, null, content);
        TestSupport.Restore(p, new NocState(NocState.CurrentV, 10, 0, mana, RaceId: 0, ClassId: 0, Skills: learned.Length == 0 ? null : learned));
        return (p, handler);
    }

    [Fact]
    public void A_learned_active_skill_spends_mana_and_starts_the_cooldown() {
        var (p, handler) = Setup(learned: [1000, 1001]);

        Assert.Null(p.SkillCastRefusal(1001, Now));
        Assert.Null(p.TryBeginSkillCast(1001, Now));

        Assert.Equal(80, p.Mana);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("Skill is on cooldown.", p.TryBeginSkillCast(1001, Now + 4999));
        Assert.Equal(80, p.Mana);
        Assert.Null(p.TryBeginSkillCast(1001, Now + 5000));
        Assert.Equal(60, p.Mana);
    }

    [Fact]
    public void Refusals_spend_nothing() {
        var (p, handler) = Setup(learned: [1000, 1001]);

        Assert.Equal("Skill not learned.", p.TryBeginSkillCast(1003, Now));
        Assert.Equal("Skill not learned.", p.TryBeginSkillCast(7, Now));
        Assert.Equal("This skill cannot be cast.", p.TryBeginSkillCast(1000, Now));
        Assert.Equal(0, handler.Calls);
        Assert.Equal(100, p.Mana);
    }

    [Fact]
    public void Not_enough_mana_is_refused_and_the_cost_follows_mana_cost_mult() {
        var (p, _) = Setup(mana: 19, learned: [1000, 1001]);
        Assert.Equal("Not enough mana.", p.TryBeginSkillCast(1001, Now));

        var (q, _) = Setup(mana: 19, learned: [1000, 1001]);
        q.Stats.Add(new StatModifier(StatId.ManaCostMult, ModifierKind.PercentAdd, -0.25, 1));
        Assert.Equal(15, q.ManaCostOf(q.FindLearnedSkill(1001)!));
        Assert.Null(q.TryBeginSkillCast(1001, Now));
        Assert.Equal(4, q.Mana);
    }

    [Fact]
    public void A_handler_refusal_spends_nothing_and_starts_no_cooldown() {
        var (p, handler) = Setup(ok: false, learned: [1000, 1001]);

        Assert.Equal("Nothing to heal.", p.TryBeginSkillCast(1001, Now));
        Assert.Equal("Nothing to heal.", p.TryBeginSkillCast(1001, Now + 1));

        Assert.Equal(2, handler.Calls);
        Assert.Equal(100, p.Mana);
    }

    [Fact]
    public void The_dead_cannot_cast_and_a_world_without_classes_refuses() {
        var (p, _) = Setup(learned: [1000, 1001]);
        p.ApplyDamage(p.MaxHp);
        Assert.Equal("You are dead.", p.SkillCastRefusal(1001, Now));

        var none = TestSupport.NewPlayer();
        Assert.Equal("Classes are not available.", none.TryBeginSkillCast(1001, Now));
    }

    [Fact]
    public void The_cast_messages_route_class_skills_and_leave_legacy_spells_alone() {
        var casting = File.ReadAllText(Path.Combine(FindServer(), "Helpers", "Casting.cs"));

        Assert.Contains("CastRouting.Of(request.SpellId)", casting);
        Assert.Contains("CastRouting.Of(player.RequestedSpellId.Value)", casting);
        Assert.Contains("TryBeginSkillCast(skillId, nowMs)", casting);
        Assert.Equal(CastRoute.Spell, CastRouting.Of(SkillIds.Base - 1));
        Assert.Equal(CastRoute.Skill, CastRouting.Of(SkillIds.Base));
        Assert.Equal(CastRoute.Mimic, CastRouting.Of(MimicIds.Base));
    }

    private static string FindServer() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent) {
            var candidate = Path.Combine(dir.FullName, "server", "Helpers");
            if (Directory.Exists(candidate)) {
                return Path.Combine(dir.FullName, "server");
            }
        }

        throw new DirectoryNotFoundException("server");
    }
}

using Server.Nocthemar.Data;
using Server.Nocthemar.Skills;
using Xunit;

namespace NocthemarTests;

/// <summary>T39: skill handler contract, registry and per-player hook table.</summary>
public class SkillHookTests {
    private sealed class Recorder(string id, SkillHook hooks, bool ok = true) : ISkillHandler {
        public string Id { get; } = id;
        public SkillHook Hooks { get; } = hooks;
        public List<SkillContext> Calls { get; } = [];

        public SkillResult Execute(in SkillContext context) {
            Calls.Add(context);
            return ok ? SkillResult.Ok : SkillResult.Refuse("no");
        }
    }

    private sealed class Thrower() : ISkillHandler {
        public string Id => "thrower";
        public SkillHook Hooks => SkillHook.OnHitDealt;
        public SkillResult Execute(in SkillContext context) => throw new InvalidOperationException("boom");
    }

    private static readonly SkillConfig Skills = TestSupport.ShippedSkills();

    /// <summary>Same catalog with some skills turned ready and bound to handlers, to exercise the real validation path.</summary>
    private static SkillConfig WithHandlers(params (int id, string handler)[] bindings) {
        var classes = TestSupport.ShippedClasses();
        var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Skills.json")))!;
        foreach (var node in doc["skills"]!.AsArray()) {
            var id = node!["id"]!.GetValue<int>();
            foreach (var (bid, handler) in bindings) {
                if (id != bid) {
                    continue;
                }

                node["status"] = "ready";
                node["handler"] = handler;
                if (node["kind"]!.GetValue<string>() != "passive") {
                    node["cooldownMs"] = 5000;
                    node["manaCost"] = 20;
                }
            }
        }

        return SkillConfig.Parse(doc.ToJsonString(), classes);
    }

    [Fact]
    public void Hook_index_is_the_bit_position() {
        Assert.Equal(0, SkillHooks.IndexOf(SkillHook.OnCast));
        Assert.Equal(1, SkillHooks.IndexOf(SkillHook.OnHitDealt));
        Assert.Equal(2, SkillHooks.IndexOf(SkillHook.OnDamageTaken));
        Assert.Equal(3, SkillHooks.IndexOf(SkillHook.OnKill));
        Assert.Equal(4, SkillHooks.IndexOf(SkillHook.OnTick));
        Assert.Equal(SkillHooks.Count, Enum.GetValues<SkillHook>().Count(h => h != SkillHook.None));
    }

    [Theory]
    [InlineData(SkillHook.None)]
    [InlineData(SkillHook.OnCast | SkillHook.OnKill)]
    [InlineData((SkillHook)64)]
    public void Index_of_rejects_zero_combined_and_unknown_hooks(SkillHook hook) {
        Assert.Throws<ArgumentException>(() => SkillHooks.IndexOf(hook));
    }

    [Fact]
    public void Registry_rejects_bad_ids_hooks_and_duplicates() {
        var registry = new SkillHandlerRegistry();
        registry.Register(new Recorder("bone_wall", SkillHook.OnCast));

        Assert.Throws<ArgumentException>(() => registry.Register(new Recorder("bone_wall", SkillHook.OnCast)));
        Assert.Throws<ArgumentException>(() => registry.Register(new Recorder("Bad Id", SkillHook.OnCast)));
        Assert.Throws<ArgumentException>(() => registry.Register(new Recorder("no_hooks", SkillHook.None)));
        Assert.Throws<ArgumentException>(() => registry.Register(new Recorder("odd_hooks", (SkillHook)128)));
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryGet("bone_wall", out _));
        Assert.False(registry.TryGet("missing", out _));
    }

    [Fact]
    public void Validation_passes_for_the_shipped_catalog_without_any_handler() {
        new SkillHandlerRegistry().ValidateCatalog(Skills);
    }

    [Fact]
    public void Validation_lists_ready_skills_with_an_unregistered_handler() {
        var catalog = WithHandlers((1001, "bone_wall"), (1003, "taunt"));
        var registry = new SkillHandlerRegistry().Register(new Recorder("bone_wall", SkillHook.OnCast));

        var ex = Assert.Throws<InvalidOperationException>(() => registry.ValidateCatalog(catalog));

        Assert.Contains("'taunt'", ex.Message);
        Assert.DoesNotContain("'bone_wall'", ex.Message);

        registry.Register(new Recorder("taunt", SkillHook.OnCast));
        registry.ValidateCatalog(catalog);
    }

    [Fact]
    public void A_skill_without_handler_is_not_subscribed() {
        var table = new SkillHookTable(new SkillHandlerRegistry());

        table.Add(Skills.Skills.First(s => s.IsReady));

        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void Adding_an_unregistered_handler_throws() {
        var catalog = WithHandlers((1001, "bone_wall"));
        var table = new SkillHookTable(new SkillHandlerRegistry());

        Assert.Throws<InvalidOperationException>(() => table.Add(catalog.Skills.Single(s => s.Id == 1001)));
    }

    [Fact]
    public void Handlers_run_only_on_the_hooks_they_declared() {
        var catalog = WithHandlers((1001, "bone_wall"), (1020, "on_hit"));
        var wall = new Recorder("bone_wall", SkillHook.OnCast);
        var onHit = new Recorder("on_hit", SkillHook.OnHitDealt | SkillHook.OnKill);
        var table = new SkillHookTable(new SkillHandlerRegistry().Register(wall).Register(onHit));
        table.Add(catalog.Skills.Single(s => s.Id == 1001));
        table.Add(catalog.Skills.Single(s => s.Id == 1020));

        Assert.Equal(2, table.Count);
        Assert.Equal(1, table.SubscribersOf(SkillHook.OnCast));
        Assert.Equal(1, table.SubscribersOf(SkillHook.OnHitDealt));
        Assert.Equal(1, table.SubscribersOf(SkillHook.OnKill));
        Assert.Equal(0, table.SubscribersOf(SkillHook.OnTick));

        Assert.Equal(1, table.Run(SkillHook.OnHitDealt, casterId: 7, targetId: 9, amount: 120, nowMs: 5000));
        Assert.Empty(wall.Calls);
        var call = Assert.Single(onHit.Calls);
        Assert.Equal(1020, call.Skill.Id);
        Assert.Equal(SkillHook.OnHitDealt, call.Hook);
        Assert.Equal(7, call.CasterId);
        Assert.Equal(9, call.TargetId);
        Assert.Equal(120, call.Amount);
        Assert.Equal(5000L, call.NowMs);
        Assert.Equal(0, table.Run(SkillHook.OnTick, 7, null, 0, 6000));
    }

    [Fact]
    public void Adding_the_same_skill_twice_subscribes_once() {
        var catalog = WithHandlers((1001, "bone_wall"));
        var wall = new Recorder("bone_wall", SkillHook.OnCast);
        var table = new SkillHookTable(new SkillHandlerRegistry().Register(wall));
        var skill = catalog.Skills.Single(s => s.Id == 1001);

        table.Add(skill);
        table.Add(skill);

        Assert.Equal(1, table.Count);
        Assert.Equal(1, table.Run(SkillHook.OnCast, 1, null, 0, 0));
        Assert.Single(wall.Calls);
    }

    [Fact]
    public void Remove_unsubscribes_from_every_hook() {
        var catalog = WithHandlers((1020, "on_hit"));
        var onHit = new Recorder("on_hit", SkillHook.OnHitDealt | SkillHook.OnKill);
        var table = new SkillHookTable(new SkillHandlerRegistry().Register(onHit));
        table.Add(catalog.Skills.Single(s => s.Id == 1020));

        Assert.True(table.Contains(1020));
        Assert.True(table.Remove(1020));
        Assert.False(table.Remove(1020));
        Assert.False(table.Contains(1020));
        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.Run(SkillHook.OnHitDealt, 1, 2, 3, 4));
        Assert.Empty(onHit.Calls);
    }

    [Fact]
    public void Run_counts_successes_and_keeps_subscription_order() {
        var catalog = WithHandlers((1001, "first"), (1003, "second"), (1005, "third"));
        var registry = new SkillHandlerRegistry()
            .Register(new Recorder("first", SkillHook.OnCast))
            .Register(new Recorder("second", SkillHook.OnCast, ok: false))
            .Register(new Recorder("third", SkillHook.OnCast));
        var table = new SkillHookTable(registry);
        foreach (var id in new[] { 1001, 1003, 1005 }) {
            table.Add(catalog.Skills.Single(s => s.Id == id));
        }

        Assert.Equal(2, table.Run(SkillHook.OnCast, 1, null, 0, 0));
        Assert.Equal(3, table.SubscribersOf(SkillHook.OnCast));
    }

    [Fact]
    public void A_throwing_handler_is_isolated_and_reported() {
        var catalog = WithHandlers((1020, "thrower"), (1021, "good"));
        var good = new Recorder("good", SkillHook.OnHitDealt);
        var table = new SkillHookTable(new SkillHandlerRegistry().Register(new Thrower()).Register(good));
        table.Add(catalog.Skills.Single(s => s.Id == 1020));
        table.Add(catalog.Skills.Single(s => s.Id == 1021));
        var errors = new List<(int, string)>();

        var ok = table.Run(SkillHook.OnHitDealt, 1, 2, 10, 0, (skill, ex) => errors.Add((skill.Id, ex.Message)));

        Assert.Equal(1, ok);
        Assert.Single(good.Calls);
        var error = Assert.Single(errors);
        Assert.Equal(1020, error.Item1);
        Assert.Equal("boom", error.Item2);
        Assert.Equal(1, table.Run(SkillHook.OnHitDealt, 1, 2, 10, 0));
    }

    [Fact]
    public void Run_skill_targets_one_skill_and_refuses_an_unknown_one() {
        var catalog = WithHandlers((1001, "bone_wall"), (1003, "taunt"));
        var wall = new Recorder("bone_wall", SkillHook.OnCast);
        var taunt = new Recorder("taunt", SkillHook.OnCast, ok: false);
        var table = new SkillHookTable(new SkillHandlerRegistry().Register(wall).Register(taunt));
        table.Add(catalog.Skills.Single(s => s.Id == 1001));
        table.Add(catalog.Skills.Single(s => s.Id == 1003));

        Assert.True(table.RunSkill(1001, SkillHook.OnCast, 5, 6, 100).Success);
        Assert.Single(wall.Calls);
        Assert.Empty(taunt.Calls);

        var refused = table.RunSkill(1003, SkillHook.OnCast, 5, null, 100);
        Assert.False(refused.Success);
        Assert.Equal("no", refused.Refusal);

        var unknown = table.RunSkill(1005, SkillHook.OnCast, 5, null, 100);
        Assert.False(unknown.Success);
        Assert.Equal("skill not available", unknown.Refusal);
        Assert.False(table.RunSkill(1001, SkillHook.OnKill, 5, null, 100).Success);
    }

    [Fact]
    public void Clear_empties_the_table() {
        var catalog = WithHandlers((1001, "bone_wall"));
        var table = new SkillHookTable(new SkillHandlerRegistry().Register(new Recorder("bone_wall", SkillHook.OnCast)));
        table.Add(catalog.Skills.Single(s => s.Id == 1001));

        table.Clear();

        Assert.Equal(0, table.Count);
        Assert.Equal(0, table.SubscribersOf(SkillHook.OnCast));
    }

    [Fact]
    public void Dispatch_has_no_switch_over_skill_ids() {
        var source = File.ReadAllText(FindSource("Nocthemar/Skills/SkillHandlers.cs"));

        Assert.DoesNotContain("switch", source);
        Assert.DoesNotContain("case ", source);
    }

    private static string FindSource(string relative) {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            var candidate = Path.Combine(dir.FullName, "server", relative);
            if (File.Exists(candidate)) {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}

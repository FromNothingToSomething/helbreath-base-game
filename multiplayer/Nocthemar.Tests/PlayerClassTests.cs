using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Server.Nocthemar.Skills;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T43: class, skills and kills on the player: effects on the stat pipeline, persistence round trip, load order and the no-content mode.</summary>
public class PlayerClassTests {
    private static NocContent Content() => TestSupport.ShippedContent();

    private static GameWorldPlayer Player(NocContent? content, List<ServerMessage>? sent = null) => TestSupport.NewPlayer(sent, null, content);

    private static int NocUpdates(List<ServerMessage> sent) => sent.Count(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated);

    private static NocState Saved(int level, int? classId, params int[] skills) =>
        new(NocState.CurrentV, level, 0, 0, RaceId: 0, ClassId: classId, Skills: skills.Length == 0 ? null : skills);

    [Fact]
    public void A_new_player_has_no_class_and_no_points() {
        var p = Player(Content());

        Assert.Null(p.ClassId);
        Assert.Empty(p.LearnedSkillIds);
        Assert.Equal(0, p.SkillPoints);
        Assert.Equal(0, p.Kills);
    }

    [Fact]
    public void Points_follow_the_level() {
        var p = Player(Content());
        Assert.True(TestSupport.Progression().TryGetXpToNext(1, out var needed));

        p.GrantXp(needed);

        Assert.Equal(2, p.Level);
        Assert.Equal(1, p.SkillPoints);
    }

    [Fact]
    public void Choosing_a_class_marks_the_state_and_is_final() {
        var sent = new List<ServerMessage>();
        var p = Player(Content(), sent);
        p.TryFlushNocUpdates();
        sent.Clear();

        Assert.Null(p.TryChooseClass(2));
        Assert.True(p.TryFlushNocUpdates());
        Assert.Equal(1, NocUpdates(sent));
        Assert.Equal(2, p.ClassId);
        Assert.Contains("already", p.TryChooseClass(1));
        Assert.Equal(2, p.ClassId);
        Assert.Contains("Unknown class", Player(Content()).TryChooseClass(42));
    }

    [Fact]
    public void Learning_applies_the_modifiers_and_spends_the_point() {
        var p = Player(Content());
        TestSupport.Restore(p, Saved(2, 2));

        Assert.Equal(1, p.SkillPoints);
        Assert.Equal(1.0, p.Stats.Get(StatId.ArmorMult), 6);
        Assert.Null(p.TryLearnSkill(1210));

        Assert.Equal(1.10, p.Stats.Get(StatId.ArmorMult), 6);
        Assert.Equal(0, p.SkillPoints);
        Assert.True(new[] { 1210 }.SequenceEqual(p.LearnedSkillIds));
        Assert.Contains("Not enough", p.TryLearnSkill(1200));
    }

    [Fact]
    public void Learning_refusals_are_the_service_refusals() {
        var p = Player(Content());
        TestSupport.Restore(p, Saved(10, null));

        Assert.Contains("Choose a class", p.TryLearnSkill(1000));
        p.TryChooseClass(0);
        Assert.Contains("not available yet", p.TryLearnSkill(1001));
        Assert.Contains("Unknown skill", p.TryLearnSkill(7));
    }

    [Fact]
    public void A_skill_that_raises_max_hp_raises_it_at_once_and_is_undone_by_the_respec() {
        var sent = new List<ServerMessage>();
        var p = Player(Content(), sent);
        TestSupport.Restore(p, Saved(24, 2, 1210, 1214));
        var before = p.MaxHp;
        Assert.Equal(1000 + 50 * 23, before);
        p.TryFlushNocUpdates();
        sent.Clear();

        Assert.Null(p.TryLearnSkill(1216)); // Coeur de Monstre: +15% max HP

        Assert.Equal((int)Math.Round(before * 1.15, MidpointRounding.AwayFromZero), p.MaxHp);
        Assert.Equal(before, p.Hp); // never raised by a learn, only clamped
        Assert.True(p.TryFlushNocUpdates());
        Assert.Contains(sent, m => m.PayloadCase == ServerMessage.PayloadOneofCase.HpUpdated);

        Assert.True(p.Respec());
        Assert.Equal(1000 + 50 * 23, p.MaxHp);
        Assert.Empty(p.LearnedSkillIds);
        Assert.Equal(1.0, p.Stats.Get(StatId.ArmorMult), 6);
        Assert.Equal(2, p.ClassId);
        Assert.False(p.Respec());
    }

    [Fact]
    public void Hp_is_clamped_when_a_respec_lowers_the_maximum() {
        var p = Player(Content());
        TestSupport.Restore(p, Saved(30, 2, 1210, 1214, 1216));
        Assert.Equal(p.MaxHp, p.Hp);
        var boosted = p.MaxHp;

        Assert.True(p.Respec());

        Assert.True(p.MaxHp < boosted);
        Assert.Equal(p.MaxHp, p.Hp);
    }

    [Fact]
    public void Restoring_a_save_applies_class_skills_before_hp_and_mana_are_derived() {
        var p = Player(Content());

        TestSupport.Restore(p, Saved(30, 2, 1210, 1214, 1216) with { Mana = 300 });

        Assert.Equal((int)Math.Round((1000 + 50 * 29) * 1.15, MidpointRounding.AwayFromZero), p.MaxHp);
        Assert.Equal(p.MaxHp, p.Hp);
        Assert.Equal(1.10, p.Stats.Get(StatId.ArmorMult), 6);
        Assert.Equal(0.20, p.Stats.Get(StatId.ResistPhysical), 6);
        Assert.Equal(300, p.Mana);
    }

    [Fact]
    public void The_persisted_state_carries_class_skills_and_kills() {
        var p = Player(Content());
        TestSupport.Restore(p, Saved(10, 0, 1000, 1010) with { Kills = 5 });
        p.RegisterKill();
        p.RegisterKill();

        var noc = p.CreatePersistenceState("aresden").Noc!;

        Assert.Equal(0, noc.ClassId);
        Assert.True(new[] { 1000, 1010 }.SequenceEqual(noc.Skills!));
        Assert.Equal(7, noc.Kills);
        Assert.Equal(NocState.CurrentV, noc.V);
    }

    [Fact]
    public void Save_and_load_again_gives_the_same_character() {
        var first = Player(Content());
        TestSupport.Restore(first, Saved(24, 2, 1210, 1214, 1216));
        var state = first.CreatePersistenceState("aresden");

        var second = Player(Content());
        second.ApplyPersistedState(state);

        Assert.Equal(first.MaxHp, second.MaxHp);
        Assert.Equal(first.ClassId, second.ClassId);
        Assert.True(first.LearnedSkillIds.SequenceEqual(second.LearnedSkillIds));
        Assert.Equal(first.Stats.Get(StatId.ResistPhysical), second.Stats.Get(StatId.ResistPhysical), 6);
    }

    [Fact]
    public void A_load_drops_what_the_catalog_does_not_accept() {
        var p = Player(Content());

        TestSupport.Restore(p, Saved(10, 9, 1000)); // unknown class
        Assert.Null(p.ClassId);
        Assert.Empty(p.LearnedSkillIds);

        TestSupport.Restore(p, Saved(3, 0, 1000, 1010, 1020, 1001, 5)); // 2 points, third branch, planned, unknown
        Assert.Equal(0, p.ClassId);
        Assert.True(new[] { 1000, 1010 }.SequenceEqual(p.LearnedSkillIds));
        Assert.Equal(0, p.SkillPoints);
    }

    [Fact]
    public void Killing_counts_and_saturates() {
        var p = Player(Content());
        TestSupport.Restore(p, Saved(1, null) with { Kills = int.MaxValue - 1 });

        p.RegisterKill();
        p.RegisterKill();
        p.RegisterKill();

        Assert.Equal(int.MaxValue, p.Kills);
    }

    [Fact]
    public void A_level_up_keeps_class_and_skills_and_their_modifiers() {
        var p = Player(Content());
        TestSupport.Restore(p, Saved(2, 2, 1210));
        Assert.True(TestSupport.Progression().TryGetXpToNext(2, out var needed));

        p.GrantXp(needed);

        Assert.Equal(3, p.Level);
        Assert.Equal(1.10, p.Stats.Get(StatId.ArmorMult), 6);
        Assert.True(new[] { 1210 }.SequenceEqual(p.LearnedSkillIds));
        Assert.Equal(1, p.SkillPoints);
    }

    [Fact]
    public void Re_applying_the_same_state_never_stacks_the_modifiers() {
        var p = Player(Content());
        var state = Saved(24, 2, 1210, 1214, 1216);

        for (var i = 0; i < 4; i++) {
            TestSupport.Restore(p, state);
        }

        Assert.Equal(1.10, p.Stats.Get(StatId.ArmorMult), 6);
        Assert.Equal((int)Math.Round((1000 + 50 * 23) * 1.15, MidpointRounding.AwayFromZero), p.MaxHp);
    }

    [Fact]
    public void Without_content_nothing_is_available_and_the_save_is_kept_as_it_is() {
        var p = Player(null);

        Assert.Contains("not available", p.TryChooseClass(0));
        Assert.Contains("not available", p.TryLearnSkill(1000));
        Assert.False(p.Respec());
        Assert.Equal(0, p.SkillPoints);

        TestSupport.Restore(p, Saved(10, 3, 1000));
        var noc = p.CreatePersistenceState("aresden").Noc!;

        Assert.Equal(3, noc.ClassId);
        Assert.True(new[] { 1000 }.SequenceEqual(noc.Skills!));
        Assert.Equal(1.0, p.Stats.Get(StatId.ArmorMult), 6);
    }

    [Fact]
    public void Skill_handlers_are_subscribed_for_the_learned_skills() {
        var handler = new RecordingHandler("test_buff");
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "Nocthemar", "Skills.json"));
        var doc = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var node = doc["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<int>() == 1000)!;
        node["handler"] = "test_buff";
        var skills = SkillConfig.Parse(doc.ToJsonString(), TestSupport.ShippedClasses());
        var content = new NocContent(TestSupport.ShippedClasses(), skills, TestSupport.Progression(), new SkillHandlerRegistry().Register(handler));
        var p = Player(content);

        TestSupport.Restore(p, Saved(5, 0, 1000));

        Assert.Equal(1, p.SkillHookSet!.SubscribersOf(SkillHook.OnHitDealt));
        Assert.True(p.Respec());
        Assert.Equal(0, p.SkillHookSet.SubscribersOf(SkillHook.OnHitDealt));
    }

    private sealed class RecordingHandler(string id) : ISkillHandler {
        public string Id { get; } = id;
        public SkillHook Hooks => SkillHook.OnHitDealt;
        public SkillResult Execute(in SkillContext context) => SkillResult.Ok;
    }

    [Fact]
    public void The_monster_kill_hook_counts_the_kill() {
        var source = File.ReadAllText(FindSource("World", "Game", "GameWorld.cs"));
        var start = source.IndexOf("public void OnMonsterKilledByPlayer", StringComparison.Ordinal);
        var body = source.Substring(start, source.IndexOf("TryGetPlayerBySessionId", start, StringComparison.Ordinal) - start);

        Assert.Contains("killer.RegisterKill();", body);
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

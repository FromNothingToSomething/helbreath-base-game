using Mmorpg.Network;
using Server.Nocthemar.Core;
using Server.Nocthemar.Net;
using Server.World.Game;
using Xunit;

namespace NocthemarTests;

/// <summary>T45: class and skill requests through the dispatcher.</summary>
public class NocDispatcherClassTests {
    private static ClientMessage Choose(int classId) => new() { NocChooseClassRequest = new NocChooseClassRequest { ClassId = classId } };

    private static ClientMessage Learn(int skillId) => new() { NocLearnSkillRequest = new NocLearnSkillRequest { SkillId = skillId } };

    private static ClientMessage Respec() => new() { NocRespecRequest = new NocRespecRequest() };

    private static NocDispatcher Dispatcher(bool debug = false) => new(debug, _ => { });

    private static GameWorldPlayer Player(List<ServerMessage> sent, bool withContent = true) =>
        TestSupport.NewPlayer(sent, null, withContent ? TestSupport.ShippedContent() : null);

    private static string[] Refusals(List<ServerMessage> sent) =>
        [.. sent.Where(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocActionRefused).Select(m => m.NocActionRefused.Reason)];

    [Fact]
    public void Choosing_a_class_is_accepted_without_an_answer_message() {
        var sent = new List<ServerMessage>();
        var player = Player(sent);

        Assert.True(Dispatcher().Handle(player, Choose(3)));

        Assert.Equal(3, player.ClassId);
        Assert.Empty(Refusals(sent));
    }

    [Fact]
    public void Choosing_twice_or_an_unknown_class_is_refused_with_a_reason() {
        var sent = new List<ServerMessage>();
        var player = Player(sent);
        var dispatcher = Dispatcher();

        dispatcher.Handle(player, Choose(3));
        dispatcher.Handle(player, Choose(1));
        dispatcher.Handle(Player(sent), Choose(77));

        var refusals = Refusals(sent);
        Assert.Equal(2, refusals.Length);
        Assert.Contains("already", refusals[0]);
        Assert.Contains("Unknown class", refusals[1]);
        Assert.Equal(3, player.ClassId);
    }

    [Fact]
    public void Learning_goes_through_the_rules_and_refusals_reach_the_sender() {
        var sent = new List<ServerMessage>();
        var player = Player(sent);
        var dispatcher = Dispatcher(debug: true);
        dispatcher.Handle(player, Learn(1000)); // no class
        dispatcher.Handle(player, Choose(0));
        dispatcher.Handle(player, Learn(1000)); // level 1: no point yet
        dispatcher.Handle(player, new ClientMessage { NocGrantXpRequest = new NocGrantXpRequest { Amount = 100 } });
        dispatcher.Handle(player, Learn(1000));
        dispatcher.Handle(player, Learn(1000)); // twice

        var refusals = Refusals(sent);
        Assert.Contains("Choose a class first.", refusals[0]);
        Assert.Contains("Not enough skill points", refusals[1]);
        Assert.Contains("already learned", refusals[2]);
        Assert.Equal(3, refusals.Length);
        Assert.True(new[] { 1000 }.SequenceEqual(player.LearnedSkillIds));
    }

    [Fact]
    public void The_next_flush_tells_the_client_its_class_skills_and_points() {
        var sent = new List<ServerMessage>();
        var player = Player(sent);
        var dispatcher = Dispatcher(debug: true);
        dispatcher.Handle(player, Choose(0));
        dispatcher.Handle(player, new ClientMessage { NocGrantXpRequest = new NocGrantXpRequest { Amount = 100 } });
        dispatcher.Handle(player, Learn(1000));
        sent.Clear();

        Assert.True(player.TryFlushNocUpdates());

        var updates = sent.Where(m => m.PayloadCase == ServerMessage.PayloadOneofCase.NocStateUpdated).ToArray();
        var state = Assert.Single(updates).NocStateUpdated.State;
        Assert.Equal(0, state.ClassId);
        Assert.Equal(0, state.SkillPoints);
        Assert.True(new[] { 1000 }.SequenceEqual(state.Skills));
    }

    [Fact]
    public void Without_class_content_every_request_is_refused() {
        var sent = new List<ServerMessage>();
        var player = Player(sent, withContent: false);
        var dispatcher = Dispatcher();

        dispatcher.Handle(player, Choose(0));
        dispatcher.Handle(player, Learn(1000));

        Assert.Equal(new[] { "Classes are not available.", "Classes are not available." }, Refusals(sent));
        Assert.Null(player.ClassId);
    }

    [Fact]
    public void Respec_needs_the_debug_flag() {
        var sent = new List<ServerMessage>();
        var logs = new List<string>();
        var player = Player(sent);
        TestSupport.Restore(player, new NocState(NocState.CurrentV, 10, 0, 0, RaceId: 0, ClassId: 0, Skills: [1000]));

        var refusing = new NocDispatcher(false, logs.Add);
        refusing.Handle(player, Respec());
        refusing.Handle(player, Respec());

        Assert.True(new[] { 1000 }.SequenceEqual(player.LearnedSkillIds));
        Assert.Single(logs.Where(l => l.Contains("NocRespecRequest")));

        new NocDispatcher(true, _ => { }).Handle(player, Respec());

        Assert.Empty(player.LearnedSkillIds);
        Assert.Equal(0, player.ClassId);
    }

    [Fact]
    public void Owns_the_three_new_payloads() {
        Assert.True(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.NocChooseClassRequest));
        Assert.True(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.NocLearnSkillRequest));
        Assert.True(NocDispatcher.Owns(ClientMessage.PayloadOneofCase.NocRespecRequest));
    }

    [Fact]
    public void GameWorld_routes_the_new_payloads_to_the_dispatcher() {
        var source = File.ReadAllText(FindSource("World", "Game", "GameWorld.cs"));

        // The labels stacked right before `nocDispatcher.Handle(` are exactly the payloads the dispatcher owns (order does not matter).
        var handle = source.IndexOf("nocDispatcher.Handle(", StringComparison.Ordinal);
        Assert.True(handle > 0);
        var routed = new HashSet<string>();
        for (var end = handle; ;) {
            var m = System.Text.RegularExpressions.Regex.Match(source[..end], @"case ClientMessage\.PayloadOneofCase\.(\w+):\s*$");
            if (!m.Success) {
                break;
            }

            routed.Add(m.Groups[1].Value);
            end = m.Index;
        }

        var owned = Enum.GetValues<ClientMessage.PayloadOneofCase>().Where(NocDispatcher.Owns).Select(c => c.ToString()).ToHashSet();
        Assert.Equal(owned.OrderBy(x => x), routed.OrderBy(x => x));
        Assert.Contains("NocFactionJoinRequest", routed);
        Assert.Contains("NocReputationAddRequest", routed);
    }

    [Fact]
    public void Server_cs_loads_the_catalogs_and_builds_the_content() {
        var source = File.ReadAllText(FindSource("Server.cs"));

        Assert.Contains("ClassConfig.LoadAsync()", source);
        Assert.Contains("SkillConfig.LoadAsync(classes)", source);
        Assert.Contains("classes.ValidateRaces(races)", source);
        Assert.Contains("nocContent: nocContent", source);
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

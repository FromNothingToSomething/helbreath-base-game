using Server.Nocthemar.Core;
using Server.Nocthemar.Data;
using Xunit;

namespace NocthemarTests;

/// <summary>T38: Classes.json catalog.</summary>
public class ClassConfigTests {
    private static string Branches(string a = "a", string b = "b", string c = "c") =>
        "[" + string.Join(",", new[] { a, b, c }.Select((k, i) => "{\"id\":" + i + ",\"key\":\"" + k + "_x\",\"name\":\"B\",\"role\":\"r\"}")) + "]";

    private static string Class(int id, string key, string? branches = null) =>
        "{\"id\":" + id + ",\"key\":\"" + key + "\",\"name\":\"N" + id + "\",\"role\":\"role\",\"branches\":" + (branches ?? Branches()) + "}";

    private static string Doc(params string[] classes) => "{\"schemaVersion\":1,\"classes\":[" + string.Join(",", classes) + "]}";

    private static void Rejects(string json, string expected) {
        var ex = Assert.Throws<InvalidOperationException>(() => ClassConfig.Parse(json));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Shipped_file_has_the_five_classes_with_three_branches() {
        var cfg = TestSupport.ShippedClasses();

        Assert.True(new[] { "chevalier_osseux", "chantre_fer", "metamorphe", "necrophante", "forgeur_abysses" }.SequenceEqual(cfg.Classes.Select(c => c.Key)));
        Assert.True(new[] { 0, 1, 2, 3, 4 }.SequenceEqual(cfg.Classes.Select(c => c.Id)));
        Assert.All(cfg.Classes, c => Assert.Equal(3, c.Branches.Count));
        Assert.True(new[] { "rempart", "briseur", "memoire" }.SequenceEqual(cfg.Classes[0].Branches.Select(b => b.Key)));
        Assert.Equal("Chevalier d'Osseux", cfg.Classes[0].Name);
    }

    [Fact]
    public void Lookup_and_source_id() {
        var cfg = TestSupport.ShippedClasses();

        Assert.True(cfg.TryGet(3, out var necro));
        Assert.Equal("necrophante", necro.Key);
        Assert.Equal(ModifierSources.Class(3), necro.SourceId);
        Assert.False(cfg.TryGet(5, out _));
        Assert.True(cfg.Contains(4));
        Assert.False(cfg.Contains(-1));
    }

    [Fact]
    public void A_minimal_valid_document_parses() {
        var cfg = ClassConfig.Parse(Doc(Class(0, "knight")));

        Assert.Single(cfg.Classes);
    }

    [Fact]
    public void Invalid_documents_are_refused() {
        Rejects("""{"schemaVersion":2,"classes":[]}""", "schemaVersion must be 1");
        Rejects("""{"schemaVersion":1,"classes":[]}""", "at least one class");
        Rejects("not json", "Invalid Classes.json");
        Rejects("null", "empty document");
        Rejects("""{"schemaVersion":1,"classes":[],"extra":1}""", "extra");
        Rejects(Doc(Class(10, "knight")), "id must be in [0, 9]");
        Rejects(Doc(Class(-1, "knight")), "id must be in [0, 9]");
        Rejects(Doc(Class(0, "knight"), Class(0, "mage")), "id 0 is duplicated");
        Rejects(Doc(Class(0, "knight"), Class(1, "knight")), "key 'knight' is duplicated");
        Rejects(Doc(Class(0, "Knight")), "key must match");
        Rejects(Doc(Class(0, "k")), "key must match");
        Rejects(Doc(Class(0, "knight", """[{"id":0,"key":"aa","name":"A","role":"r"}]""")), "exactly 3 branches");
        Rejects(Doc(Class(0, "knight", Branches("a", "a", "c"))), "duplicated");
        Rejects(Doc(Class(0, "knight", """[{"id":1,"key":"aa","name":"A","role":"r"},{"id":0,"key":"bb","name":"B","role":"r"},{"id":2,"key":"cc","name":"C","role":"r"}]""")), "branches[0].id must be 0");
        Rejects(Doc("{\"id\":0,\"key\":\"knight\",\"name\":\"\",\"role\":\"r\",\"branches\":" + Branches() + "}"), "name must be 1-64");
    }

    [Fact]
    public void More_than_ten_classes_are_refused() {
        var many = Enumerable.Range(0, 11).Select(i => Class(i % 10, "c" + i));

        Rejects(Doc([.. many]), "max 10");
    }

    [Fact]
    public void Every_problem_is_reported_at_once() {
        var ex = Assert.Throws<InvalidOperationException>(() => ClassConfig.Parse(Doc(Class(0, "Bad"), Class(0, "worse"))));

        Assert.Contains("key must match", ex.Message);
        Assert.Contains("duplicated", ex.Message);
    }
}

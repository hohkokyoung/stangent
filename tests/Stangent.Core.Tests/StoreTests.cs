using System.Text.Json.Nodes;
using Stangent.Core.Store;

namespace Stangent.Core.Tests;

public class CanonicalJsonTests
{
    [Fact]
    public void Same_content_in_any_key_order_gives_the_same_bytes_and_hash()
    {
        var a = JsonNode.Parse("""{"b":1,"a":{"y":[2,{"q":1,"p":2}],"x":"é"}}""")!;
        var b = JsonNode.Parse("""{"a":{"x":"é","y":[2,{"p":2,"q":1}]},"b":1}""")!;

        var text = CanonicalJson.Serialize(a);
        Assert.Equal(text, CanonicalJson.Serialize(b));
        Assert.Equal(CanonicalJson.Hash(text), CanonicalJson.Hash(CanonicalJson.Serialize(b)));
        Assert.EndsWith("}\n", text);
        Assert.DoesNotContain('\r', text);
        Assert.Contains("é", text);
    }
}

public class StoreTests
{
    [Fact]
    public void Put_then_get_returns_the_artifact()
    {
        using var temp = new TempRepo();
        var store = temp.Store();

        var result = store.Put(Artifacts.Note("hello", "Hello"));

        Assert.True(result.Changed);
        Assert.Equal("note/hello", result.Ref);
        Assert.Equal("Hello", (string?)store.Get(new ArtifactRef("note", "hello"))["title"]);
        Assert.True(File.Exists(Path.Combine(temp.Repo.StoreRoot, "note", "hello.json")));
    }

    [Fact]
    public void Putting_the_same_content_twice_changes_nothing()
    {
        using var temp = new TempRepo();
        var store = temp.Store();
        var first = store.Put(Artifacts.Note("hello", "Hello"));

        var second = store.Put(Artifacts.Note("hello", "Hello"));

        Assert.False(second.Changed);
        Assert.Equal(first.Hash, second.Hash);
    }

    [Theory]
    [InlineData("""{"id":"x","type":"note","schemaVersion":1,"extra":true}""", "#/extra")]
    [InlineData("""{"id":"../escape","type":"note","schemaVersion":1}""", "#/id")]
    [InlineData("""{"id":"x","type":"Note","schemaVersion":1}""", "#/type")]
    [InlineData("""{"id":"x","schemaVersion":1}""", "type")]
    [InlineData("""{"id":"x","type":"note","schemaVersion":0}""", "#/schemaVersion")]
    [InlineData("""{"id":"x","type":"note","schemaVersion":1,"provenance":{"kind":"guess"}}""", "#/provenance.kind")]
    public void Put_rejects_an_artifact_that_breaks_the_base_contract(string json, string expectedPath)
    {
        using var temp = new TempRepo();
        var store = temp.Store();

        var error = Assert.Throws<StangentException>(() => store.Put(JsonNode.Parse(json)!.AsObject()));

        Assert.Equal("invalid_artifact", error.Code);
        Assert.Contains(error.Details, d => d.Contains(expectedPath));
        Assert.Empty(Directory.EnumerateFiles(temp.Repo.StoreRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Put_rejects_a_link_to_an_artifact_that_does_not_exist()
    {
        using var temp = new TempRepo();
        var store = temp.Store();

        var error = Assert.Throws<StangentException>(() => store.Put(Artifacts.Note("a", links: ("refines", "note/missing"))));

        Assert.Contains("links: target note/missing does not exist", error.Details);
    }

    [Fact]
    public void Link_adds_the_link_once_and_query_finds_it()
    {
        using var temp = new TempRepo();
        var store = temp.Store();
        store.Put(Artifacts.Note("a"));
        store.Put(Artifacts.Note("b"));

        store.Link(new("note", "a"), "refines", new("note", "b"));
        var again = store.Link(new("note", "a"), "refines", new("note", "b"));

        Assert.False(again.Changed);
        Assert.Single(store.Get(new("note", "a"))["links"]!.AsArray());
        Assert.Equal(["note/a"], store.Query(new ArtifactQuery(LinksTo: "note/b")).Select(e => e.Ref));
        Assert.Empty(store.Query(new ArtifactQuery(LinksTo: "note/b", Rel: "blocks")));
    }

    [Fact]
    public void A_type_schema_validates_the_data_field()
    {
        using var temp = new TempRepo();
        var schemas = new SchemaRegistry(name => name == "types/note.v1.json"
            ? """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}"""
            : SchemaRegistry.Embedded.Has(name) ? EmbeddedText(name) : null);
        var store = temp.Store(schemas);
        var bad = Artifacts.Note("x");
        bad["data"] = new JsonObject { ["text"] = 3 };

        var error = Assert.Throws<StangentException>(() => store.Put(bad));

        Assert.Contains(error.Details, d => d.StartsWith("data/text"));
        bad["data"] = new JsonObject { ["text"] = "ok" };
        Assert.True(store.Put(bad).Changed);
    }

    [Fact]
    public void Rebuilding_the_index_from_files_reproduces_it_exactly()
    {
        using var temp = new TempRepo();
        var store = temp.Store();
        store.Put(Artifacts.Note("b", "Bee"));
        store.Put(Artifacts.Note("a", "Ay", ("refines", "note/b")));
        store.Put(Artifacts.Note("c", links: [("refines", "note/a"), ("cites", "note/b")]));
        var before = CanonicalJson.Serialize(store.DumpIndex());

        File.Delete(temp.Repo.IndexPath);
        temp.Store().RebuildIndex();

        Assert.Equal(before, CanonicalJson.Serialize(temp.Store().DumpIndex()));
        Assert.Contains("\"ref\": \"note/c\"", before);
    }

    [Fact]
    public void Query_sees_files_changed_outside_the_store_such_as_a_git_pull()
    {
        using var temp = new TempRepo();
        var store = temp.Store();
        store.Put(Artifacts.Note("a"));
        Assert.Single(store.Query(new ArtifactQuery()));

        File.WriteAllText(Path.Combine(temp.Repo.StoreRoot, "note", "pulled.json"), CanonicalJson.Serialize(Artifacts.Note("pulled")));

        Assert.Equal(["note/a", "note/pulled"], store.Query(new ArtifactQuery()).Select(e => e.Ref));
    }

    [Fact]
    public void Rebuild_refuses_a_broken_store_and_keeps_the_old_index()
    {
        using var temp = new TempRepo();
        var store = temp.Store();
        store.Put(Artifacts.Note("a"));
        store.RebuildIndex();
        var before = File.ReadAllBytes(temp.Repo.IndexPath);
        File.WriteAllText(Path.Combine(temp.Repo.StoreRoot, "note", "misplaced.json"), CanonicalJson.Serialize(Artifacts.Note("other")));
        File.WriteAllText(Path.Combine(temp.Repo.StoreRoot, "note", "broken.json"), "{not json");

        var error = Assert.Throws<StangentException>(() => store.RebuildIndex());

        Assert.Equal("invalid_store", error.Code);
        Assert.Contains(error.Details, d => d.Contains("misplaced.json") && d.Contains("note/other"));
        Assert.Contains(error.Details, d => d.Contains("broken.json"));
        Assert.Equal(before, File.ReadAllBytes(temp.Repo.IndexPath));
    }

    static string EmbeddedText(string name)
    {
        using var stream = typeof(SchemaRegistry).Assembly.GetManifestResourceStream("schemas/" + name)!;
        return new StreamReader(stream).ReadToEnd();
    }
}

public class BatchTests
{
    [Fact]
    public void A_batch_with_one_bad_op_writes_nothing()
    {
        using var temp = new TempRepo();
        var store = temp.Store();
        var ops = new JsonArray(
            new JsonObject { ["op"] = "put", ["artifact"] = Artifacts.Note("a") },
            new JsonObject { ["op"] = "put", ["artifact"] = new JsonObject { ["id"] = "b" } });

        var error = Assert.Throws<StangentException>(() => StoreBatch.Run(store, ops));

        Assert.Equal("invalid_batch", error.Code);
        Assert.All(error.Details, d => Assert.StartsWith("ops[1]", d));
        Assert.False(store.Exists(new("note", "a")));
    }

    [Fact]
    public void Ops_may_use_artifacts_created_earlier_in_the_batch_but_not_later()
    {
        using var temp = new TempRepo();
        var store = temp.Store();
        var good = new JsonArray(
            new JsonObject { ["op"] = "put", ["artifact"] = Artifacts.Note("b") },
            new JsonObject { ["op"] = "put", ["artifact"] = Artifacts.Note("a", links: ("refines", "note/b")) },
            new JsonObject { ["op"] = "link", ["source"] = "note/b", ["rel"] = "cites", ["target"] = "note/a" },
            new JsonObject { ["op"] = "query", ["linksTo"] = "note/a" });

        var results = StoreBatch.Run(store, good);

        Assert.Equal(4, results.Count);
        Assert.Equal("note/b", (string?)results[3]![0]!["ref"]);

        var bad = new JsonArray(
            new JsonObject { ["op"] = "link", ["source"] = "note/c", ["rel"] = "cites", ["target"] = "note/a" },
            new JsonObject { ["op"] = "put", ["artifact"] = Artifacts.Note("c") });
        var error = Assert.Throws<StangentException>(() => StoreBatch.Run(store, bad));
        Assert.Contains("ops[0]: source note/c does not exist", error.Details);
        Assert.False(store.Exists(new("note", "c")));
    }
}

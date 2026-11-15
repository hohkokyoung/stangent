using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>
/// The hard bar of the overhaul: saved paid AI work stays valid. The values below came from the build before 2.3 (commit 7064baa),
/// by running this same test there. A change to any of them makes every saved page or link on a real project run again, for money,
/// so a failure here is never fixed by updating the numbers without the owner's say.
/// </summary>
public class SavedWorkTests
{
    internal static readonly string[] Saved =
    [
        "writer 20d521af7016b7598063f3018f69e725d5f00e0a86d9ba7d57da112249c1b382",
        "recorded 20d521af7016b7598063f3018f69e725d5f00e0a86d9ba7d57da112249c1b382",
        "verifier 77dbf424d6dd8732bdf9e086ca65cd22343a1bd3d35f5d53c12a15a9b829b99e",
        "entry L940b1e792c",
        "prompt page-verifier 692cab254cf0e4562aafa9e387d0237bdd2d1d860550cd0abed921feafb2a88f",
        "prompt page-writer 67802692161a82d7aa194f496f0a0acd21f424e7449f26ba0701d7355a0558aa",
        "schema page-writer-output 59c33291e9b53ba70e670d249d9f6d9c5e226ae5ef0e95aa75fdbcf219f390d2",
        "schema page-verifier-output f9ccbfbafe3f0a23af3b75e1629db24332032b809d5c90fdaacc7152f750b3f8",
    ];

    internal const string Fsd = """
        #! Orders
        The orders screen has a Place order button.
        Orders over 50 need approval.
        Every order sends a receipt.
        """;

    static JsonObject S(string text, params string[] cites) => new() { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) };

    internal static string Sha(string text) => CanonicalJson.Hash(text);

    /// <summary>Records a page through the real loop; returns what the verifier was given.</summary>
    static string Record(SpecApp app, string page, JsonObject output)
    {
        var def = app.Pages().Find(page)!;
        var step = PageRun.Prepare(app.Pages(), def, app.Repo);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), output.ToJsonString());
        step = PageRun.Check(app.Pages(), def, app.Repo, null);
        var verify = SharedHead.Assemble(File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "verify.md")));
        File.WriteAllText(Path.Combine(app.Root, step.Output!), "{\"findings\":[]}");
        Assert.Equal("recorded", PageRun.Check(app.Pages(), def, app.Repo, null).Status);
        return verify;
    }

    internal static SpecApp App(out string verifierInput)
    {
        var archive = Directory.CreateTempSubdirectory("stangent-archive-").FullName;
        var app = new SpecApp($"version: 1\nfsd: {{ archive: {{ name: docs, path: '{archive}' }}, findings: true }}\n");
        verifierInput = Record(app, SpecApp.Post, new JsonObject
        {
            ["title"] = "Place an order",
            ["shows"] = new JsonArray(), ["actions"] = new JsonArray(),
            ["rules"] = new JsonArray(S("Orders over 100 are refused with \"Orders over 100 need approval\".", SpecApp.Place)),
            ["sideEffects"] = new JsonArray(S("Placing an order publishes OrderPlaced, which sends a receipt.", SpecApp.Place, SpecApp.Receipt)),
        });
        var file = Path.Combine(app.Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Fsd));
        FsdImport.Import(app.Repo, file);
        return app;
    }

    internal static string[] Hashes(SpecApp app, string verifierInput)
    {
        var index = SpecIndex.Load(app.Root);
        List<string> lines =
        [
            $"writer {Bundles.Build(app.Pages(), app.Pages().Find(SpecApp.Post)!, null).InputHash}",
            $"recorded {(string)index.Pages[SpecApp.Post]["generated"]!["bundleHash"]!}",
            $"verifier {Sha(verifierInput)}",
            $"entry {TraceFile.EntryId("conflict", "shop", "1", SpecApp.Post, "Orders over 50 need approval.", "Orders over 100 are refused", null)}",
        ];
        foreach (var name in new[] { "page-verifier", "page-writer" })
            lines.Add($"prompt {name} {Sha(Bundles.Instructions(name + ".md"))}");
        foreach (var schema in new[] { "page-writer-output", "page-verifier-output" })
            lines.Add($"schema {schema} {Sha(SchemaRegistry.EmbeddedText(schema + ".v1.json"))}");
        return [.. lines];
    }

    [Fact]
    public void Every_input_hash_and_prompt_is_the_one_the_build_before_stable_ids_gave()
    {
        using var app = App(out var verifierInput);
        Assert.Equal(string.Join("\n", Saved), string.Join("\n", Hashes(app, verifierInput)));
    }

    [Fact]
    public void Adopting_ids_and_filling_citations_changes_nothing_a_hash_reads()
    {
        using var app = App(out var verifierInput);
        var pageFile = new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!);
        var before = File.ReadAllText(pageFile);

        CleanGraph(app);
        var report = PageLines.Refresh(app.Root, Stangent.Graph.GraphStore.Load(Stangent.Graph.GraphStore.PathFor(app.Repo))!, null);
        Assert.True((int)report["citations"]!["withLines"]! > 0);

        Assert.Equal(before, File.ReadAllText(pageFile));
        Assert.Equal(string.Join("\n", Saved), string.Join("\n", Hashes(app, verifierInput)));
        Assert.Equal("current", PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo).Status);
    }

    /// <summary>The graph as a clean build at a real commit leaves it.</summary>
    internal static void CleanGraph(SpecApp app, IReadOnlyList<string>? dirty = null, IReadOnlyList<string>? notInCommit = null, char commit = 'a')
    {
        var graph = Stangent.Graph.GraphStore.Load(Stangent.Graph.GraphStore.PathFor(app.Repo))!;
        graph.Commit = new string(commit, 40);
        graph.DirtyFiles = [.. dirty ?? []];
        graph.NotInCommit = [.. notInCommit ?? []];
        Stangent.Graph.GraphStore.Save(graph, Stangent.Graph.GraphStore.PathFor(app.Repo));
    }
}

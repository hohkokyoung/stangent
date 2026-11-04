using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;
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
        "judge 3069b7a96fbbdb17f8826bfee0d9663bcccfef0a8314831d5a8b4744357b1569",
        "judge-off 8f4c93f7065f6da886f10626285f1deba8c9225106223ba05da8f93feb259275",
        "judge-req 00e79627e5a5637048b6f114b75ed4b2a92b0ed87598c706333ecf09bf9b86b6",
        "requirement d2a47a6c1684833f62c7d59335f7edb352848f3a93d93eb8a54024899278d870",
        "entry L940b1e792c",
        "prompt agree-checker 8af7e3c77e8c32fbe54252717f9e9077093e04df08b30ffaed0b535e0547c8fc",
        "prompt candidate-finder db2544477f2d2c72528cdca7b962ff83acedf0824ea13dcdf183e44fac593f87",
        "prompt link-judge 28db5dc1b6834dd19d895f9b219d06e6565dcf8fecdaa0bfeb26b88e972b78d3",
        "prompt page-verifier 692cab254cf0e4562aafa9e387d0237bdd2d1d860550cd0abed921feafb2a88f",
        "prompt page-writer 67802692161a82d7aa194f496f0a0acd21f424e7449f26ba0701d7355a0558aa",
        "prompt requirement-agree-checker 672f8b0e0e2278dc81db94d75cd78ed7f60ae3a2060b21baca79e4c170cb6f93",
        "prompt requirement-checker fa49fbccd6cbabc9d578ad6491466e68bb84214db5bba3fac00fdbe428937f0a",
        "prompt requirement-judge 0f56670a4531531001f8ab38456f000f1e6cff6a081d809b24788167db187045",
        "prompt requirement-writer dcf03fccafd3d87894868413e6d1dab97761c23871c1065731eebb9e61723648",
        "schema page-writer-output 59c33291e9b53ba70e670d249d9f6d9c5e226ae5ef0e95aa75fdbcf219f390d2",
        "schema page-verifier-output f9ccbfbafe3f0a23af3b75e1629db24332032b809d5c90fdaacc7152f750b3f8",
        "schema link-judge-output 3e5b09fc786717bdb2513123c810ff8b21dbab5db2ef2fe936e942683643781a",
        "schema agree-checker-output a91c9f0bc9e4c9770a7eefe342552f231988ae92d02e43204ae8fb77fc436f24",
        "schema candidate-finder-output ba8e4198567d865558b1f89c1dcf0cdc0decd61f7546a73928e0fc1f844b14a7",
        "schema requirement-judge-output 209d9d258d3dbbbf0a5b3ca1a7e83be7809a37687a4bb1ec404f37c8929e552d",
        "schema requirement-writer-output 67cdccda476e2ed6457b8532d36e305d9af893f095e97044bce334f44d228f05",
        "schema requirement-checker-output 8f787c9045590edf0a85cb1f176a3a834471231fa427a201745e376eb7edcf9e",
    ];

    const string Fsd = """
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
        var verify = File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "verify.md"));
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
        var section = index.Sections[("shop", "1")];
        var requirement = new JsonObject
        {
            ["id"] = "R-0001", ["title"] = "Approval", ["kind"] = "rule", ["statement"] = "Orders over 50 need approval.",
            ["quotes"] = new JsonArray(new JsonObject { ["section"] = "1", ["text"] = "Orders over 50 need approval." }),
        };
        string[] pages = [SpecApp.Post];
        List<string> lines =
        [
            $"writer {Bundles.Build(app.Pages(), app.Pages().Find(SpecApp.Post)!, null).InputHash}",
            $"recorded {(string)index.Pages[SpecApp.Post]["generated"]!["bundleHash"]!}",
            $"verifier {Sha(verifierInput)}",
            $"judge {LinkRun.JudgeInputs(section, "shop", pages, index, null, true)}",
            $"judge-off {LinkRun.JudgeInputs(section, "shop", pages, index, null, false)}",
            $"judge-req {LinkRun.JudgeInputs(section, "shop", pages, index, [requirement], true)}",
            $"requirement {RequirementRun.InputsOf(section, "shop")}",
            $"entry {TraceFile.EntryId("conflict", "shop", "1", SpecApp.Post, "Orders over 50 need approval.", "Orders over 100 are refused", null)}",
        ];
        foreach (var name in new[] { "agree-checker", "candidate-finder", "link-judge", "page-verifier", "page-writer", "requirement-agree-checker", "requirement-checker", "requirement-judge", "requirement-writer" })
            lines.Add($"prompt {name} {Sha(Bundles.Instructions(name + ".md"))}");
        foreach (var schema in new[] { "page-writer-output", "page-verifier-output", "link-judge-output", "agree-checker-output", "candidate-finder-output", "requirement-judge-output", "requirement-writer-output", "requirement-checker-output" })
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
    internal static void CleanGraph(SpecApp app, IReadOnlyList<string>? dirty = null, IReadOnlyList<string>? notInCommit = null)
    {
        var graph = Stangent.Graph.GraphStore.Load(Stangent.Graph.GraphStore.PathFor(app.Repo))!;
        graph.Commit = new string('a', 40);
        graph.DirtyFiles = [.. dirty ?? []];
        graph.NotInCommit = [.. notInCommit ?? []];
        Stangent.Graph.GraphStore.Save(graph, Stangent.Graph.GraphStore.PathFor(app.Repo));
    }
}

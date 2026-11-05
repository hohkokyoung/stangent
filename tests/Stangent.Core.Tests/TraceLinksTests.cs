using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary><c>trace links</c> (D133): a link's page statement and the lines that statement cites, looked up when read, with nothing written.</summary>
public class TraceLinksTests
{
    const string Sentence = "Orders over 50 need approval.";
    const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static readonly JsonObject First = new() { ["id"] = "1", ["title"] = "Orders", ["permanentId"] = "S-0001" };

    static SpecApp App()
    {
        var app = SavedWorkTests.App(out _);
        SavedWorkTests.CleanGraph(app);
        PageLines.Refresh(app.Root, GraphStore.Load(GraphStore.PathFor(app.Repo))!, null);
        return app;
    }

    static JsonObject Agree(string statement = "R1", JsonObject? section = null) =>
        LinkRun.Entry("agree", "shop", section ?? First, Sentence, SpecApp.Post, statement, "Orders over 100 are refused", "judge", "confirmed", null);

    static void Save(SpecApp app, params JsonObject[] entries)
    {
        var trace = TraceFile.Empty();
        foreach (var e in entries) trace["entries"]!.AsArray().Add((JsonNode)e);
        TraceFile.Save(app.Root, trace);
    }

    static JsonObject Only(SpecApp app) => TraceLinks.Run(app.Root, "shop", null)["links"]!.AsArray().Single()!.AsObject();

    static string PageFile(SpecApp app) => SpecIndex.Load(app.Root).PageFiles[SpecApp.Post];

    /// <summary>Gives the page's first rule these citations, and the line map a place for each.</summary>
    static void CiteOnRule(SpecApp app, params string[] cites)
    {
        var file = PageFile(app);
        var page = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        PageChecks.Statements(page).First(s => s.Id == "R1").Statement["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]);
        File.WriteAllText(file, page.ToJsonString());
        var map = PageLines.Load(file)!;
        foreach (var c in cites) map["symbols"]![c] ??= new JsonObject { ["file"] = "web/src/Orders.tsx", ["start"] = 1, ["end"] = 4 };
        PageLines.Write(file, map);
    }

    [Fact]
    public void An_agree_link_shows_its_statement_and_the_lines_it_cites_and_says_where_they_came_from()
    {
        using var app = App();
        Save(app, Agree());
        var link = Only(app);
        Assert.Equal(("agree", "1", Sentence), ((string)link["state"]!, (string)link["section"]!, (string)link["fsdQuote"]!));
        Assert.Null(link["finding"]);
        Assert.Equal((SpecApp.Post, "R1", "Orders over 100 are refused"), ((string)link["page"]!["id"]!, (string)link["page"]!["statement"]!, (string)link["page"]!["quote"]!));
        var code = link["code"]!.AsArray().Single()!;
        Assert.Equal(("Core/OrderService.cs", 3, 8, SpecApp.Place), ((string)code["path"]!, (int)code["lines"]!["start"]!, (int)code["lines"]!["end"]!, (string)code["symbol"]!));
        Assert.Equal("page-citations", (string)link["codeFrom"]!);
        Assert.Null(link["codeNote"]);
        Assert.Equal(Commit, (string)link["commit"]!);
    }

    [Fact]
    public void A_finding_shows_its_id_and_the_same_lines_its_evidence_cites()
    {
        using var app = App();
        Save(app, LinkRun.Entry("conflict", "shop", First, Sentence, SpecApp.Post, "R1", "Orders over 100 are refused", "judge", null, "FSD 50, code 100"));
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        Assert.True((bool)TraceCheck.Run(app.Root, graph, null, false)["ok"]!);
        var link = Only(app);
        Assert.Equal(("conflict", "X-0001"), ((string)link["state"]!, (string)link["finding"]!));
        var evidence = TraceFile.Load(app.Root)["entries"]![0]!["evidence"]!.AsArray().Single(r => (string)r!["citation"]!["kind"]! == "code")!["citation"]!;
        var code = link["code"]!.AsArray().Single()!;
        Assert.EndsWith("/" + (string)code["path"]!, (string)evidence["ref"]!);
        Assert.Equal((int)evidence["lines"]!["start"]!, (int)code["lines"]!["start"]!);
    }

    [Fact]
    public void A_link_with_no_page_statement_has_no_code_and_says_so()
    {
        using var app = App();
        Save(app, LinkRun.Entry("gap", "shop", First, Sentence, null, null, null, "judge", null, "not on any page"));
        var link = Only(app);
        Assert.Null(link["page"]);
        Assert.Empty(link["code"]!.AsArray());
        Assert.Null(link["codeFrom"]);
        Assert.Null(link["commit"]);
        Assert.Equal("no page statement", (string)link["codeNote"]!);
    }

    [Fact]
    public void A_statement_gone_from_the_page_has_no_code_and_says_so()
    {
        using var app = App();
        Save(app, Agree("R9"));
        var link = Only(app);
        Assert.Equal("R9", (string)link["page"]!["statement"]!);
        Assert.Empty(link["code"]!.AsArray());
        Assert.Equal("statement not on the page", (string)link["codeNote"]!);
        Assert.Equal(Commit, (string)link["commit"]!);
    }

    [Fact]
    public void A_page_with_no_line_map_has_no_code_and_says_so()
    {
        using var app = App();
        File.Delete(PageLines.FileFor(PageFile(app)));
        Save(app, Agree());
        var link = Only(app);
        Assert.Empty(link["code"]!.AsArray());
        Assert.Equal("page has no line map", (string)link["codeNote"]!);
        Assert.Null(link["commit"]);
    }

    [Fact]
    public void A_citation_of_a_page_is_left_out_of_the_code()
    {
        using var app = App();
        CiteOnRule(app, SpecApp.Place, SpecApp.Route);
        Save(app, Agree());
        Assert.Equal(SpecApp.Place, (string)Only(app)["code"]!.AsArray().Single()!["symbol"]!);

        CiteOnRule(app, SpecApp.Route);
        var alone = Only(app);
        Assert.Empty(alone["code"]!.AsArray());
        Assert.Equal("no code cited", (string)alone["codeNote"]!);
    }

    [Fact]
    public void Section_narrows_to_that_section_in_file_order_and_the_counts_follow()
    {
        using var app = App();
        var second = new JsonObject { ["id"] = "2", ["title"] = "Other", ["permanentId"] = "S-0002" };
        var gap = LinkRun.Entry("gap", "shop", First, "Another sentence.", null, null, null, "judge", null, "none");
        Save(app, Agree(section: second), gap, Agree());

        var all = TraceLinks.Run(app.Root, "shop", null);
        Assert.Equal(TraceFile.Load(app.Root)["entries"]!.AsArray().Select(e => (string)e!["id"]!), all["links"]!.AsArray().Select(l => (string)l!["id"]!));
        Assert.Equal((3, 2, 1), ((int)all["counts"]!["links"]!, (int)all["counts"]!["withCode"]!, (int)all["counts"]!["withoutCode"]!));
        Assert.Equal((2, 1), ((int)all["counts"]!["byState"]!["agree"]!, (int)all["counts"]!["byState"]!["gap"]!));

        var one = TraceLinks.Run(app.Root, "shop", "1");
        Assert.Equal("gap", (string)one["links"]![0]!["state"]!);
        Assert.Equal((2, 1, 1), ((int)one["counts"]!["links"]!, (int)one["counts"]!["withCode"]!, (int)one["counts"]!["withoutCode"]!));
    }

    [Fact]
    public void No_links_give_empty_lists_and_an_unknown_document_or_section_is_not_found()
    {
        using var app = App();
        var none = TraceLinks.Run(app.Root, "shop", null);
        Assert.Empty(none["links"]!.AsArray());
        Assert.Empty(none["counts"]!["byState"]!.AsObject());
        Assert.Equal(0, (int)none["counts"]!["links"]!);

        Assert.Equal(("not_found", ExitCodes.NotFound), Fails(() => TraceLinks.Run(app.Root, "nope", null)));
        Assert.Equal(("not_found", ExitCodes.NotFound), Fails(() => TraceLinks.Run(app.Root, "shop", "99")));
    }

    static (string, int) Fails(Action run)
    {
        var e = Assert.Throws<StangentException>(run);
        return (e.Code, e.ExitCode);
    }

    [Fact]
    public void The_command_leaves_every_project_file_byte_identical()
    {
        using var app = App();
        Save(app, Agree());
        var before = Snapshot(app.Root);

        var run = WholeFlow.RunIn(app.Root, "trace", "links", "shop");
        Assert.Equal(0, run.Exit);
        Assert.Equal(1, (int)run.Envelope["result"]!["counts"]!["links"]!);
        var refused = WholeFlow.RunIn(app.Root, "trace", "links", "nope");
        Assert.Equal(ExitCodes.NotFound, refused.Exit);

        Assert.Equal(before, Snapshot(app.Root));
    }

    /// <summary>Every file and folder under the repo, hidden ones too, with a hash of each file's bytes; only telemetry, which every command writes, is left out.</summary>
    static List<string> Snapshot(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(root, p).StartsWith(Path.Combine(".stangent", "telemetry"), StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .Select(p => File.Exists(p) ? $"{p} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))}" : p)];
}

using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>Phase 2b on the small app: candidates by code, the judge and the agree checker with canned outputs, the
/// decisions file and its CI checks. No LLM.</summary>
public class FsdLinkTests
{
    const string Fsd = """
        #! Orders
        The orders screen has a Place order button.
        Orders over 50 need approval.
        Every order sends a receipt.
        #! Loyalty points
        Customers earn one point per dollar.
        """;

    static SpecApp App(string extra = "")
    {
        var archive = Directory.CreateTempSubdirectory("stangent-archive-").FullName;
        var app = new SpecApp($"version: 1\nfsd: {{ archive: {{ name: docs, path: '{archive}' }} }}\n{extra}");
        Record(app, SpecApp.Post, new JsonObject
        {
            ["title"] = "Place an order",
            ["shows"] = new JsonArray(), ["actions"] = new JsonArray(),
            ["rules"] = new JsonArray(S("Orders over 100 are refused with \"Orders over 100 need approval\".", SpecApp.Place)),
            ["sideEffects"] = new JsonArray(S("Placing an order publishes OrderPlaced, which sends a receipt.", SpecApp.Place, SpecApp.Receipt)),
        });
        Record(app, "page:/orders", new JsonObject
        {
            ["title"] = "Orders",
            ["shows"] = new JsonArray(S("The screen shows the heading Orders.", SpecApp.OrdersPage)),
            ["actions"] = new JsonArray(S("The Place order button posts the order.", SpecApp.OrdersPage, SpecApp.Save)),
            ["rules"] = new JsonArray(), ["sideEffects"] = new JsonArray(),
        });
        Record(app, "page:/cart", new JsonObject
        {
            ["title"] = "Cart", ["shows"] = new JsonArray(S("The cart shows a Cart button.", "ts|web/src/Other.tsx#CartPage")),
            ["actions"] = new JsonArray(), ["rules"] = new JsonArray(), ["sideEffects"] = new JsonArray(),
        });
        var file = Path.Combine(app.Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Fsd));
        FsdImport.Import(app.Repo, file);
        return app;
    }

    static JsonObject S(string text, params string[] cites) => new() { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) };

    static void Record(SpecApp app, string page, JsonObject output)
    {
        var def = app.Pages().Find(page)!;
        var step = PageRun.Prepare(app.Pages(), def, app.Repo);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), output.ToJsonString());
        step = PageRun.Check(app.Pages(), def, app.Repo, null);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), "{\"findings\":[]}");
        Assert.Equal("recorded", PageRun.Check(app.Pages(), def, app.Repo, null).Status);
    }

    static void Write(SpecApp app, RunStep step, JsonNode output) => File.WriteAllText(Path.Combine(app.Root, step.Output!), output.ToJsonString());

    static JsonObject Link(string state, string? fsd, string? page, string? statement, string? pageQuote, string note = "n") =>
        new() { ["state"] = state, ["fsdQuote"] = fsd, ["page"] = page, ["statement"] = statement, ["pageQuote"] = pageQuote, ["note"] = note };

    static JsonObject Links(params JsonObject[] links)
    {
        foreach (var l in links) foreach (var k in l.Where(p => p.Value is null).Select(p => p.Key).ToList()) l.Remove(k);
        return new JsonObject { ["links"] = new JsonArray(links) };
    }

    [Fact]
    public void Code_finds_candidates_by_name_and_adds_the_endpoints_a_screen_calls()
    {
        using var app = App();
        var index = SpecIndex.Load(app.Root);
        var orders = Candidates.ForSection(index.Sections[("shop", "1")], app.Pages(), index.Pages);
        // The screen by its route, the endpoint by its route, the handler by the event it handles; recorded or not.
        Assert.Equal([("page:/orders", "name"), (SpecApp.Post, "name"), (SpecApp.Receipt, "name")], orders.Select(c => (c.Page, c.Why)));
        Assert.Empty(Candidates.ForSection(index.Sections[("shop", "2")], app.Pages(), index.Pages));
        Assert.True(Candidates.Words("Series Details").SetEquals(Candidates.Words("seriesDetails")));
        Assert.True(Candidates.Words("Adding series").SetEquals(Candidates.Words("/add/series")));
    }

    [Fact]
    public void A_section_is_judged_checked_confirmed_and_recorded_and_a_rerun_makes_no_AI_call()
    {
        using var app = App();
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        Assert.Equal(("judge", LinkRun.Judge), (step.Action, step.Agent));
        var bundle = File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "judge.md"));
        Assert.Contains("Orders over 50 need approval.", bundle);
        Assert.Contains("**R1** (rules) Orders over 100 are refused", bundle);

        // First try: a made-up FSD quote. Rejected with one specific error, for one retry.
        Write(app, step, Links(Link("conflict", "Orders over 75 need approval.", SpecApp.Post, "R1", "Orders over 100 are refused")));
        step = LinkRun.Check(app.Pages(), app.Repo, graph, "shop", "1", null);
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains("fsdQuote \"Orders over 75 need approval.\" isn't a sentence from the section's text, word for word"));

        Write(app, step, Links(
            Link("conflict", "Orders over 50 need approval.", SpecApp.Post, "R1", "Orders over 100 are refused", "FSD 50, code 100"),
            Link("agree", "Every order sends a receipt.", SpecApp.Post, "E1", "which sends a receipt"),
            Link("agree", "The orders screen has a Place order button.", "page:/orders", "A1", "The Place order button posts the order."),
            Link("undocumented", null, "page:/cart", "S1", "The cart shows a Cart button.")));   // not a candidate: rejected
        step = LinkRun.Check(app.Pages(), app.Repo, graph, "shop", "1", null);
        Assert.Equal(("confirm", LinkRun.Checker), (step.Action, step.Agent));

        // The second, fresh check confirms one agree link and disputes the other with quotes.
        Write(app, step, new JsonObject
        {
            ["checks"] = new JsonArray(
                new JsonObject { ["link"] = "K1", ["verdict"] = "confirm", ["note"] = "same" },
                new JsonObject
                {
                    ["link"] = "K2", ["verdict"] = "dispute", ["state"] = "gap", ["fsdQuote"] = "The orders screen has a Place order button.",
                    ["note"] = "the page's button posts, it isn't named",
                }),
        });
        step = LinkRun.Check(app.Pages(), app.Repo, graph, "shop", "1", null);
        Assert.Equal("recorded", step.Status);

        var entries = TraceFile.Load(app.Root)["entries"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => (string)e["state"]! == "conflict" && (string)e["page"]!["statement"]! == "R1");
        Assert.Contains(entries, e => (string)e["state"]! == "agree" && (string?)e["check"] == "confirmed");
        Assert.Contains(entries, e => (string)e["state"]! == "gap" && (string?)e["check"] == "disputed" && e["page"]!["quote"] is null);
        var runs = LinkRun.LoadRuns(app.Root)["shop#1"]!;
        Assert.Equal((1, 1, "code"), ((int)runs["rejected"]!, (int)runs["retries"]!, (string)runs["candidatesBy"]!));

        var report = TraceCheck.Run(app.Root, graph, null, fix: false);
        Assert.True((bool)report["ok"]!, report.ToJsonString());

        var bytes = File.ReadAllBytes(TraceFile.PathIn(app.Root));
        Assert.Equal(("current", "none"), (LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1").Status, LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1").Action));
        Assert.Equal(bytes, File.ReadAllBytes(TraceFile.PathIn(app.Root)));
    }

    [Fact]
    public void A_section_no_page_matches_goes_to_the_finder_and_with_none_found_is_a_gap_by_code()
    {
        using var app = App();
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "2");
        Assert.Equal(("find", LinkRun.Finder), (step.Action, step.Agent));
        var bundle = File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "finder.md"));
        Assert.Contains("| `page:/orders` | screen | Orders |", bundle);
        Assert.DoesNotContain("Orders over 100", bundle);                 // titles only, not the pages

        Write(app, step, new JsonObject { ["candidates"] = new JsonArray() });
        Assert.Equal("recorded", LinkRun.Check(app.Pages(), app.Repo, graph, "shop", "2", null).Status);
        var gap = Assert.Single(TraceFile.Load(app.Root)["entries"]!.AsArray());
        Assert.Equal(("gap", "code", "Customers earn one point per dollar."), ((string)gap!["state"]!, (string)gap["by"]!, (string)gap["fsd"]!["quote"]!));
        Assert.Equal(("current", "none"), (LinkRun.Prepare(app.Pages(), app.Repo, "shop", "2").Status, "none"));
        Assert.Equal("ai", (string)LinkRun.LoadRuns(app.Root)["shop#2"]!["candidatesBy"]!);
    }

    [Fact]
    public void Screens_no_section_links_to_are_undocumented_within_the_FSD_scope()
    {
        using var app = App("");
        Assert.Equal(2, Undocumented.Apply(app.Root, app.Pages(), null));           // /orders and /cart: both unlinked yet
        using var scoped = App("");
        Assert.Equal(1, Undocumented.Apply(scoped.Root, scoped.Pages(), ["/cart"]));
        Assert.True(Undocumented.InScope("/settings", "/settings/tags"));
        Assert.False(Undocumented.InScope("/settings", "/settingsx"));
        Assert.False(Undocumented.InScope("/", "/series"));
    }

    [Fact]
    public void Trace_check_catches_hand_edits_stale_quotes_and_typed_decided_by()
    {
        using var app = App();
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        LinkRun.ApplyEntries(app.Root, "shop", "1", [LinkRun.Entry("agree", "shop", SpecIndex.Load(app.Root).Sections[("shop", "1")],
            "Every order sends a receipt.", SpecApp.Post, "E1", "which sends a receipt", "judge", "confirmed", null)]);
        var baseTrace = TraceFile.Load(app.Root);

        Assert.Equal(1, TraceCheck.ApproveChapter(app.Root, "shop", "1"));
        Assert.True((bool)TraceCheck.Run(app.Root, graph, baseTrace, fix: false)["ok"]!);
        Assert.Equal(1, TraceCheck.Attribute(app.Root, baseTrace, "pm-anna"));
        Assert.Equal("pm-anna", (string)TraceFile.Load(app.Root)["entries"]![0]!["decided_by"]!);

        // Typed in a PR: rejected.
        var typed = File.ReadAllText(TraceFile.PathIn(app.Root)).Replace("\"pm-anna\"", "\"someone\"");
        File.WriteAllText(TraceFile.PathIn(app.Root), typed);
        var before = TraceFile.Parse(typed.Replace("\"someone\"", "\"pm-anna\""));
        Assert.Contains(TraceCheck.Run(app.Root, graph, before, false)["problems"]!.AsArray(), p => ((string)p!).Contains("decided_by was set in this change"));

        // A quote edited by hand changes what the id stands for.
        File.WriteAllText(TraceFile.PathIn(app.Root), typed.Replace("which sends a receipt", "which sends an email"));
        Assert.Contains(TraceCheck.Run(app.Root, graph, null, false)["problems"]!.AsArray(), p => ((string)p!).Contains("edited by hand"));

        // The FSD changes under a decided link: its decision drops back to undecided.
        File.WriteAllText(TraceFile.PathIn(app.Root), typed);
        var archive = Path.Combine(app.Root, "shop.docx");
        File.WriteAllBytes(archive, DocxWriter.Write(Fsd.Replace("Orders over 50 need approval.", "Orders over 60 need approval.")));
        FsdImport.Import(app.Repo, archive);
        Assert.True((bool)TraceCheck.Run(app.Root, graph, null, false)["ok"]!);  // the quote is still in the text
        File.WriteAllBytes(archive, DocxWriter.Write(Fsd.Replace("Every order sends a receipt.", "Receipts are optional.")));
        FsdImport.Import(app.Repo, archive);
        var stale = TraceCheck.Run(app.Root, graph, null, false);
        Assert.Contains(stale["problems"]!.AsArray(), p => ((string)p!).Contains("must drop back to undecided"));
        Assert.True((bool)TraceCheck.Run(app.Root, graph, null, fix: true)["ok"]!);
        Assert.Null(TraceFile.Load(app.Root)["entries"]![0]!["decision"]);
    }

    [Fact]
    public void The_decisions_file_is_written_in_one_fixed_form_and_reads_back_the_same()
    {
        var trace = TraceFile.Empty();
        trace["entries"]!.AsArray().Add(LinkRun.Entry("conflict", "d", new JsonObject { ["id"] = "4.10", ["title"] = "Tags: \"max\"" },
            "At most 3 tags: per series.", "page:/x", "R1", "No limit # really", "judge", null, "50 vs 100"));
        trace["entries"]!.AsArray().Add(LinkRun.Entry("gap", "d", new JsonObject { ["id"] = "4.9", ["title"] = "T" }, "x y z.", null, null, null, "code", null, null));
        var text = TraceFile.Write(trace);
        Assert.Equal(text, TraceFile.Write(TraceFile.Parse(text)));
        Assert.True(text.IndexOf("\"4.9\"", StringComparison.Ordinal) < text.IndexOf("\"4.10\"", StringComparison.Ordinal));
        Assert.Empty(SchemaRegistry.Embedded.Validate(TraceFile.Schema, TraceFile.Parse(text).ToJsonString()));
    }

    [Fact]
    public void Impact_answers_reach_FSD_sections_through_the_pages_and_their_links()
    {
        using var app = App();
        var section = SpecIndex.Load(app.Root).Sections[("shop", "1")];
        LinkRun.ApplyEntries(app.Root, "shop", "1", [LinkRun.Entry("conflict", "shop", section, "Orders over 50 need approval.", SpecApp.Post, "R1",
            "Orders over 100 are refused", "judge", null, "50 vs 100")]);
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        var report = new Graph.Impact.ImpactAnalyzer(graph).Analyze(new Graph.Impact.ChangeSpec("symbol", SpecApp.Place), app.Root, null, null,
            (reached, expand) => FsdImpact.Sections(app.Root, reached, expand));
        Assert.Empty(SchemaRegistry.Embedded.Validate("impact-report.v5.json", report.ToJsonString()));
        var item = Assert.Single(report["fsdSections"]!["items"]!.AsArray())!;
        Assert.Equal(("1", "Orders", false), ((string)item["section"]!, (string)item["title"]!, (bool)item["verified"]!));
        Assert.Equal([SpecApp.Post], item["pages"]!.AsArray().Select(p => (string)p!));
        Assert.Contains("**📄 1 FSD section(s) reached through the reference pages**", Graph.Impact.ImpactAnalyzer.Markdown(report));
        Assert.Contains("§1 Orders (shop): via `endpoint:Shop.Api|POST /api/orders` · conflict · unverified", Graph.Impact.ImpactAnalyzer.Markdown(report));
    }

    [Fact]
    public void Fragments_are_not_quotes_and_a_missing_output_still_gets_its_retry()
    {
        using var app = App();
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        // Nothing written at all (a timeout): a retry, not a crash.
        step = LinkRun.Check(app.Pages(), app.Repo, graph, "shop", "1", null);
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains("No output"));
        // Four-character fragments found anywhere are not quotes (fresh review, finding 2).
        Write(app, step, Links(Link("conflict", "rder", SpecApp.Post, "R1", "over")));
        step = LinkRun.Check(app.Pages(), app.Repo, graph, "shop", "1", null);
        Assert.Equal("recorded", step.Status);
        Assert.Empty(TraceFile.Load(app.Root)["entries"]!.AsArray());
        Assert.Equal(1, (int)LinkRun.LoadRuns(app.Root)["shop#1"]!["rejected"]!);
        Assert.False(Quotes.InText("over", "Orders over 50 need approval."));
        Assert.True(Quotes.InText("Orders over 50 need", "Orders over 50 need approval."));
        Assert.True(Quotes.InText("at most 100", "| Total | at most 100 |"));
    }

    [Fact]
    public void A_section_whose_candidate_pages_were_never_recorded_is_not_turned_into_a_gap()
    {
        using var app = App();
        File.Delete(new SpecPaths(app.Root).PageJson(app.Pages().Find("page:/orders")!));
        File.Delete(new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!));
        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        Assert.Equal("failed", step.Status);
        Assert.Contains("no candidate page has been recorded yet", step.Errors![0]);
        Assert.Empty(TraceFile.Load(app.Root)["entries"]!.AsArray());
    }
}

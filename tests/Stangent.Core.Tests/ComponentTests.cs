using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Graph.Detectors;
using Stangent.Graph.History;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;
using Stangent.Spec.Trace;
using Stangent.Tickets;
using Stangent.Tickets.Proof;

namespace Stangent.Core.Tests;

/// <summary>Every component has a switch (D121): each one here really changes the outcome, says it's off, and moves no input hash.</summary>
public class ComponentTests
{
    const string Fsd = """
        # 1 Orders

        Orders over 50 need approval.

        # 2 Returns

        A return is refused after thirty days have passed.
        """;

    static SpecApp App(bool findings = true)
    {
        var archive = Directory.CreateTempSubdirectory("stangent-archive-").FullName;
        var app = new SpecApp($"version: 1\nfsd: {{ archive: {{ name: docs, path: '{archive}' }}, findings: {(findings ? "true" : "false")} }}\n");
        var file = Path.Combine(app.Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Fsd));
        FsdImport.Import(app.Repo, file);
        return app;
    }

    static JsonObject S(string text, params string[] cites) => new() { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) };

    static readonly JsonObject PostPage = new()
    {
        ["title"] = "Place an order",
        ["shows"] = new JsonArray(), ["actions"] = new JsonArray(),
        ["rules"] = new JsonArray(S("Orders over 100 are refused with \"Orders over 100 need approval\".", SpecApp.Place)),
        ["sideEffects"] = new JsonArray(S("Placing an order publishes OrderPlaced, which sends a receipt.", SpecApp.Place, SpecApp.Receipt)),
    };

    static RunStep Write(SpecApp app, string page, JsonObject output)
    {
        var def = app.Pages().Find(page)!;
        var step = PageRun.Prepare(app.Pages(), def, app.Repo);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), output.ToJsonString());
        return PageRun.Check(app.Pages(), def, app.Repo, null);
    }

    static void Record(SpecApp app, string page, JsonObject output)
    {
        var step = Write(app, page, output);
        if (step.Status == "recorded") return;
        File.WriteAllText(Path.Combine(app.Root, step.Output!), "{\"findings\":[]}");
        Assert.Equal("recorded", PageRun.Check(app.Pages(), app.Pages().Find(page)!, app.Repo, null).Status);
    }

    static JsonNode Page(SpecApp app, string page) => PageRun.LoadJson(new SpecPaths(app.Root).PageJson(app.Pages().Find(page)!))!;

    // ---- the settings ----

    [Fact]
    public void Components_settle_onto_the_old_keys_and_win_over_them()
    {
        using var repo = new TempRepo("version: 1\nhistory: { enabled: true }\nfsd: { findings: false, disprove: false }\ntickets: { aiPoints: true }\ncomponents: { git-history: false, findings: true, disprove: true, file-finder: false, ai: false }\n");
        var c = repo.Repo.Config;
        Assert.Equal((false, true, true, false, false), (c.History.Enabled, c.Fsd.Findings, c.Fsd.Disprove, c.Tickets.AiPoints, c.Ai.Enabled));
    }

    [Fact]
    public void An_old_config_keeps_working_and_reads_as_today()
    {
        using var repo = new TempRepo("version: 1\nhistory: { enabled: false }\nfsd: { findings: true }\ntickets: { aiPoints: false }\ndetectors: [config-keys]\n");
        var c = repo.Repo.Config;
        Assert.False(StangentComponents.IsOn(c, "git-history"));
        Assert.True(StangentComponents.IsOn(c, "findings"));
        Assert.False(StangentComponents.IsOn(c, "file-finder"));
        Assert.True(StangentComponents.IsOn(c, "detector-config-keys"));
        Assert.False(StangentComponents.IsOn(c, "detector-microsoft-di"));
    }

    [Fact]
    public void With_nothing_set_each_component_is_at_its_default()
    {
        var c = new StangentConfig();
        Assert.All(StangentComponents.All, x => Assert.Equal(x.OnByDefault, StangentComponents.IsOn(c, x.Name)));
        Assert.Equal(["disprove", "findings"], StangentComponents.All.Where(x => !x.OnByDefault).Select(x => x.Name).Order());
    }

    [Fact]
    public void An_unknown_component_is_refused()
    {
        var config = new StangentConfig { Components = { ["page-verifer"] = false } };
        var e = Assert.Throws<StangentException>(() => StangentComponents.Apply(config));
        Assert.Equal(("unknown_component", ExitCodes.Usage), (e.Code, e.ExitCode));
    }

    [Fact]
    public void Every_detector_is_a_component_and_what_a_component_needs_is_a_component()
    {
        Assert.Equal(DetectorRegistry.BuiltIn.Select(d => $"detector-{d.Name}").Order(), StangentComponents.All.Where(c => c.Name.StartsWith("detector-", StringComparison.Ordinal)).Select(c => c.Name).Order());
        Assert.All(StangentComponents.All.SelectMany(c => c.Needs), n => Assert.Contains(StangentComponents.All, c => c.Name == n));
    }

    [Fact]
    public void A_run_says_which_components_it_used()
    {
        var c = new StangentConfig { Components = { ["agree-checker"] = false } };
        var used = StangentComponents.ForCommand(c, "fsd check")!.Value;
        Assert.Contains("agree-checker", used.Off);
        Assert.Contains("candidate-finder", used.On);
        Assert.Null(StangentComponents.ForCommand(c, "store get"));
    }

    // ---- each switch ----

    [Fact]
    public void Git_history_off_reads_nothing_and_says_off()
    {
        using var app = new SpecApp("version: 1\ncomponents: { git-history: false }\n");
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        HistoryStep.Apply(app.Repo, graph, null, () => null, []);
        Assert.Equal("off", graph.History!.Status);
    }

    [Theory]
    [InlineData("config-keys")]
    [InlineData("playwright-specs")]
    public void A_detector_off_doesnt_run_whatever_the_list_says(string detector)
    {
        var all = new StangentConfig { Components = { [$"detector-{detector}"] = false } };
        Assert.DoesNotContain(detector, DetectorRegistry.Enabled(all).Select(d => d.Name));
        Assert.Equal(DetectorRegistry.BuiltIn.Count - 1, DetectorRegistry.Enabled(all).Count);
        var listed = new StangentConfig { Detectors = ["config-keys", "playwright-specs"], Components = { [$"detector-{detector}"] = false } };
        Assert.Single(DetectorRegistry.Enabled(listed));
    }

    [Fact]
    public void The_old_detector_list_means_what_it_did()
    {
        Assert.Equal(DetectorRegistry.BuiltIn, DetectorRegistry.Enabled(new StangentConfig()));
        Assert.Empty(DetectorRegistry.Enabled(new StangentConfig { Detectors = ["none"] }));
        Assert.Equal("unknown_detector", Assert.Throws<StangentException>(() => DetectorRegistry.Enabled(new StangentConfig { Detectors = ["nope"] })).Code);
    }

    [Fact]
    public void Page_verifier_on_asks_for_the_check()
    {
        using var app = App();
        Assert.Equal("verify", Write(app, SpecApp.Post, PostPage).Action);
    }

    [Fact]
    public void Page_verifier_off_goes_straight_to_recorded_and_turning_it_on_asks_for_no_new_call()
    {
        using var app = App();
        app.Repo.Config.Components["page-verifier"] = false;
        var step = Write(app, SpecApp.Post, PostPage);
        Assert.Equal(("recorded", "none"), (step.Status, step.Action));
        Assert.Contains("page-verifier is off", step.Warning);
        Assert.True((bool)Page(app, SpecApp.Post)["verification"]!["off"]!);
        Assert.Empty(SchemaRegistry.Embedded.Validate("reference-page.v1.json", Page(app, SpecApp.Post).ToJsonString()));

        // The switch isn't in the page's input hash.
        app.Repo.Config.Components.Clear();
        Assert.Equal("current", PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo).Status);
    }

    [Fact]
    public void A_checked_page_has_no_off_mark()
    {
        using var app = App();
        Record(app, SpecApp.Post, PostPage);
        Assert.Null(Page(app, SpecApp.Post)["verification"]!["off"]);
    }

    [Fact]
    public void Line_citations_off_leaves_no_lines_file_and_says_so_with_no_new_call()
    {
        using var app = App();
        Record(app, SpecApp.Post, PostPage);
        var pageJson = new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!);
        Assert.True(File.Exists(PageLines.FileFor(pageJson)));

        using var off = App();
        off.Repo.Config.Components["line-citations"] = false;
        off.Repo.Config.Components["page-verifier"] = false;
        var step = Write(off, SpecApp.Post, PostPage);
        Assert.Contains("line-citations is off", step.Warning);
        Assert.False(File.Exists(PageLines.FileFor(new SpecPaths(off.Root).PageJson(off.Pages().Find(SpecApp.Post)!))));
        Assert.Equal("off", (string)PageLines.Refresh(off.Root, GraphStore.Load(GraphStore.PathFor(off.Repo))!, null, on: false)["lineCitations"]!);
        off.Repo.Config.Components.Clear();
        Assert.Equal("current", PageRun.Prepare(off.Pages(), off.Pages().Find(SpecApp.Post)!, off.Repo).Status);
    }

    [Fact]
    public void Candidate_finder_off_leaves_the_section_unjudged_and_never_calls_it_a_gap()
    {
        using var app = App();
        Record(app, SpecApp.Post, PostPage);
        Assert.Equal("find", LinkRun.Prepare(app.Pages(), app.Repo, "shop", "2").Action);    // on: code found nothing, so the finder runs

        using var off = App();
        Record(off, SpecApp.Post, PostPage);
        off.Repo.Config.Components["candidate-finder"] = false;
        var step = LinkRun.Prepare(off.Pages(), off.Repo, "shop", "2");
        Assert.Equal(("off", "none"), (step.Status, step.Action));
        Assert.Contains("isn't a gap", step.Errors![0]);
        Assert.Empty(TraceFile.Load(off.Root)["entries"]!.AsArray());
        Assert.Equal("off", (string)LinkRun.LoadRuns(off.Root)["shop#2"]!["candidatesBy"]!);
    }

    [Fact]
    public void Agree_checker_off_records_the_agree_with_no_check_and_says_so()
    {
        using var app = App();
        Record(app, SpecApp.Post, PostPage);
        app.Repo.Config.Components["agree-checker"] = false;
        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        Assert.Equal(LinkRun.Judge, step.Agent);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), new JsonObject
        {
            ["links"] = new JsonArray(new JsonObject { ["state"] = "agree", ["fsdQuote"] = "Orders over 50 need approval.", ["page"] = SpecApp.Post, ["statement"] = "R1", ["pageQuote"] = "Orders over 100 are refused", ["note"] = "n" }),
        }.ToJsonString());
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        var done = LinkRun.Check(app.Pages(), app.Repo, graph, "shop", "1", null);
        Assert.Equal(("recorded", "none"), (done.Status, done.Action));
        var entry = TraceFile.Load(app.Root)["entries"]!.AsArray().Single()!;
        Assert.Null(entry["check"]);
        Assert.Contains("agree-checker is off", (string)entry["note"]!);
        Assert.Equal("off", (string)LinkRun.LoadRuns(app.Root)["shop#1"]!["agreeChecker"]!);
    }

    [Fact]
    public void Requirements_links_off_judges_the_section_not_its_requirements()
    {
        using var app = App();
        Record(app, SpecApp.Post, PostPage);
        var index = SpecIndex.Load(app.Root);
        var section = index.Sections[("shop", "1")];
        var feature = RequirementFile.Ensure(app.Root, "shop", section, index);
        feature["textHash"] = CanonicalJson.Hash((string)section["text"]!);
        RequirementFile.Save(app.Root, feature);

        Assert.Equal("skipped", LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1").Status);     // on: a section whose requirements are all background has nothing to judge
        app.Repo.Config.Components["requirements-links"] = false;
        Assert.Equal(LinkRun.Judge, LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1").Agent);
    }

    [Fact]
    public void Findings_off_records_only_links()
    {
        using var app = App();
        Record(app, SpecApp.Post, PostPage);
        app.Repo.Config.Fsd.Findings = false;
        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        File.WriteAllText(Path.Combine(app.Root, step.Output!), new JsonObject
        {
            ["links"] = new JsonArray(new JsonObject { ["state"] = "conflict", ["fsdQuote"] = "Orders over 50 need approval.", ["page"] = SpecApp.Post, ["statement"] = "R1", ["pageQuote"] = "Orders over 100 are refused", ["note"] = "n" }),
        }.ToJsonString());
        LinkRun.Check(app.Pages(), app.Repo, GraphStore.Load(GraphStore.PathFor(app.Repo))!, "shop", "1", null);
        Assert.Empty(TraceFile.Load(app.Root)["entries"]!.AsArray());
    }

    [Fact]
    public void Disprove_off_is_off_and_on_with_findings_off_says_what_it_needs()
    {
        using var app = App(findings: false);
        Assert.Equal("off", DisproveRun.Prepare(app.Pages(), app.Repo, "shop", "1").Status);
        app.Repo.Config.Fsd.Disprove = true;
        var step = DisproveRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        Assert.Equal("off", step.Status);
        Assert.Contains("disprove needs findings", step.Errors![0]);
    }

    [Fact]
    public void File_finder_off_makes_codes_signals_the_starting_points()
    {
        using var repo = TicketFixtures.Repo("components: { file-finder: false }\n");
        Assert.False(repo.Repo.Config.Tickets.AiPoints);
        Assert.False(StangentComponents.IsOn(repo.Repo.Config, "file-finder"));
    }

    [Fact]
    public void Bug_proof_off_tries_nothing_and_says_that_proves_nothing()
    {
        using var app = App();
        app.Repo.Config.Components["bug-proof"] = false;
        var step = BugProof.Prepare(app.Repo, TicketContext.Load(app.Repo), "SHOP-1", null);
        Assert.Equal(("off", "none"), (step.Status, step.Action));
        Assert.Contains("neither proven nor disproven", step.Errors![0]);
    }

    [Fact]
    public void Drafts_off_makes_no_draft()
    {
        using var app = App();
        app.Repo.Config.Components["drafts"] = false;
        var step = TicketDrafts.Prepare(app.Repo, TicketContext.Load(app.Repo), "SHOP-1", "questions", null, null, null);
        Assert.Equal(("off", "none"), (step.Status, step.Action));
    }

    [Fact]
    public void Ai_off_stops_a_step_that_needs_it_and_the_one_that_needs_it_says_so()
    {
        using var app = App();
        Record(app, SpecApp.Post, PostPage);
        app.Repo.Config.Ai.Enabled = false;
        Assert.Equal("ai_disabled", Assert.Throws<StangentException>(() => LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1")).Code);
        Assert.Contains("page-verifier needs ai", StangentComponents.MissingNeed(app.Repo.Config, "page-verifier"));
    }
}

/// <summary>The whole flow with a few components off (D121): it still runs, and what's off says so. Built as the main flow is, with the steps that need what's off left out.</summary>
public sealed class WholeFlowOff : IDisposable
{
    public WholeFlow Flow { get; } = WholeFlow.With("components: { git-history: false, findings: false, disprove: false }");

    public void Dispose() => Flow.Dispose();
}

public class WholeFlowOffTests(WholeFlowOff off) : IClassFixture<WholeFlowOff>
{
    readonly WholeFlow flow = off.Flow;

    [Fact]
    public void The_flow_gets_through_with_history_findings_and_disprove_off()
    {
        Assert.Equal(int.MaxValue, flow.BrokeAt);
        Assert.Empty(flow.SchemaErrors);
    }

    [Fact]
    public void History_says_off()
    {
        flow.Reached(1);
        Assert.Equal("off", (string?)flow.Seen("graph")["historyStatus"]);
    }

    [Fact]
    public void Only_links_are_on_file_and_the_finding_lists_are_empty_for_a_stated_reason()
    {
        flow.Reached(4);
        Assert.All(flow.Seen("trace.links")["entries"]!.AsArray(), e => Assert.Equal("agree", (string?)e!["state"]));
        Assert.NotEmpty(flow.Seen("trace.links")["entries"]!.AsArray());
        var envelope = flow.Cli("trace", "check");
        Assert.Contains(envelope["components"]!["off"]!.AsArray(), n => (string?)n == "findings");
    }

    [Fact]
    public void Disprove_says_off()
    {
        flow.Reached(4);
        Assert.Equal("off", (string?)flow.Cli("fsd", "disprove", "prepare", "shop", "1")["result"]!["status"]);
    }

    [Fact]
    public void A_ticket_bundle_is_still_built()
    {
        flow.Reached(5);
        Assert.Equal("SHOP-1", (string?)flow.Seen("ticket.bundle")["key"]);
    }

    [Fact]
    public void The_list_and_the_telemetry_say_what_was_off()
    {
        flow.Reached(1);
        var list = flow.Cli("components")["result"]!;
        var history = list["components"]!.AsArray().Single(c => (string?)c!["name"] == "git-history")!;
        Assert.Equal((false, true, "history.enabled"), ((bool)history["on"]!, (bool)history["default"]!, (string)history["setting"]!));
        var disprove = list["components"]!.AsArray().Single(c => (string?)c!["name"] == "disprove")!;
        Assert.Contains("findings", disprove["needs"]!.AsArray().Select(n => (string?)n));
        Assert.Contains("disprove needs findings", (string?)disprove["blocked"]);
        Assert.Contains(list["components"]!.AsArray().Single(c => (string?)c!["name"] == "findings")!["neededBy"]!.AsArray(), n => (string?)n == "disprove");
        Assert.NotEmpty(list["noSwitch"]!.AsArray());

        var events = Directory.EnumerateFiles(Path.Combine(flow.Root, ".stangent", "telemetry"), "*.jsonl").SelectMany(File.ReadAllLines).Select(l => JsonNode.Parse(l)!);
        var build = events.First(e => (string?)e["attributes"]!["stangent.command"] == "graph build");
        Assert.Contains("git-history", (string?)build["attributes"]!["stangent.components.off"]);
        Assert.Contains("detector-config-keys", (string?)build["attributes"]!["stangent.components.on"]);
    }
}

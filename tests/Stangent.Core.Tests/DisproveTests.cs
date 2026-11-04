using System.Text.Json.Nodes;
using Stangent.Core.Evidence;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>The disprove step (2.5, D119), with canned answers. No test here reaches an AI.</summary>
public class DisproveTests
{
    const string Fsd50 = "Orders over 50 need approval.";
    const string Page100 = "Orders over 100 are refused";
    const string RealLine = "if (total > 100) throw new ArgumentException(\"Orders over 100 need approval\");";
    const string Secret = "THE FINDERS OWN REASONING";

    static SpecApp App(bool on = true)
    {
        var app = SavedWorkTests.App(out _);
        SavedWorkTests.CleanGraph(app);
        app.Repo.Config.Fsd.Disprove = on;
        return app;
    }

    static JsonObject Section() => new() { ["id"] = "1", ["title"] = "Orders" };

    static JsonObject Conflict() => LinkRun.Entry("conflict", "shop", Section(), Fsd50, SpecApp.Post, "R1", Page100, "judge", null, Secret);

    static void Save(SpecApp app, params JsonObject[] entries)
    {
        var trace = TraceFile.Empty();
        foreach (var e in entries) trace["entries"]!.AsArray().Add((JsonNode)e);
        TraceFile.Save(app.Root, trace);
    }

    static JsonObject Entry(SpecApp app, int i = 0) => TraceFile.Load(app.Root)["entries"]![i]!.AsObject();

    static RunStep Prepare(SpecApp app) => DisproveRun.Prepare(app.Pages(), app.Repo, "shop", "1");

    static RunStep Answer(SpecApp app, RunStep step, object answer)
    {
        File.WriteAllText(Path.Combine(app.Root, step.Output!), answer as string ?? ((JsonObject)answer).ToJsonString());
        return DisproveRun.Check(app.Pages(), app.Repo, "shop", "1", new Usage("sonnet-test", 1, 100, 0, 0, 10, 0.01, 5));
    }

    /// <summary>The answer quoting line 5 of Core/OrderService.cs, as the app writes it.</summary>
    static JsonObject Quoted(string verdict, string quote = RealLine) =>
        new() { ["verdict"] = verdict, ["why"] = "because", ["path"] = "Core/OrderService.cs", ["start"] = 5, ["end"] = 5, ["quote"] = quote };

    static string Bundle(SpecApp app, RunStep step) => File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "bundle.md"));

    static JsonArray Problems(JsonObject report) => report["problems"]!.AsArray();

    [Fact]
    public void It_is_off_unless_asked_and_off_writes_nothing()
    {
        using var app = App(on: false);
        Save(app, Conflict());
        Assert.False(new StangentConfig.FsdConfig().Disprove);
        var before = File.ReadAllText(TraceFile.PathIn(app.Root));
        var step = Prepare(app);
        Assert.Equal(("off", "none"), (step.Status, step.Action));
        Assert.Equal(before, File.ReadAllText(TraceFile.PathIn(app.Root)));
        Assert.False(Directory.Exists(Path.Combine(app.Root, ".stangent", "fsd-disprove")));
    }

    [Fact]
    public void The_bundle_holds_the_claim_and_the_code_and_never_who_found_it_or_why()
    {
        using var app = App();
        Save(app, Conflict());
        var step = Prepare(app);
        Assert.Equal(DisproveRun.Disprover, step.Agent);
        var bundle = Bundle(app, step);
        Assert.DoesNotContain(Secret, bundle);
        Assert.DoesNotContain("judge", bundle.Split("# The FSD section")[1]);
        Assert.Contains(Fsd50, bundle);
        Assert.Contains(Page100, bundle);
        Assert.Contains(SpecApp.Place, bundle);
        Assert.Contains("`Core/OrderService.cs` lines", bundle);
        Assert.Contains(RealLine, bundle);
        Assert.Contains($"`{SpecApp.Post}`", bundle.Split("# The entry points that reach it")[1]);
    }

    [Fact]
    public void A_symbol_that_cant_be_shown_is_named_in_the_bundle()
    {
        using var app = App();
        SavedWorkTests.CleanGraph(app, dirty: ["Core/OrderService.cs"]);
        Save(app, Conflict());
        var step = Prepare(app);
        var bundle = Bundle(app, step);
        var missing = bundle.Split("# Couldn't be shown")[1].Split("# The entry points")[0];
        Assert.Contains(SpecApp.Place, missing);
        Assert.Contains("differed from the commit", missing);
        Assert.DoesNotContain(RealLine, bundle);
    }

    [Fact]
    public void A_graph_that_cant_say_it_was_clean_stops_the_run_rather_than_blame_the_finding()
    {
        using var app = App();
        Save(app, Conflict());
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        graph.DirtyFiles = null;
        GraphStore.Save(graph, GraphStore.PathFor(app.Repo));
        var step = Prepare(app);
        Assert.Equal("failed", step.Status);
        Assert.Contains("rebuild", step.Errors![0]);
        Assert.Null(Entry(app)["verdict"]);
    }

    [Theory]
    [InlineData("refuted", "closed", "refuted")]
    [InlineData("unreachable", "closed", "unreachable")]
    [InlineData("stands", "ba", "code-confirmed")]
    [InlineData("page-gap", "developer", "page-gap")]
    public void A_verdict_goes_where_it_should_with_its_quote_in_the_evidence_as_read(string verdict, string goesTo, string label)
    {
        using var app = App();
        Save(app, Conflict());
        var done = Answer(app, Prepare(app), Quoted(verdict));
        Assert.Equal(("recorded", "none"), (done.Status, done.Action));

        var e = Entry(app);
        Assert.Equal(verdict, (string)e["verdict"]!);
        var record = e["evidence"]!.AsArray().OfType<JsonObject>().Single(r => (string)r["id"]! == "X-0001.disprove");
        Assert.Equal("read", (string)record["strength"]!);
        Assert.Equal(RealLine, (string)record["citation"]!["quote"]!);
        Assert.Equal(SpecApp.Place, (string)record["citation"]!["symbol"]!);
        var by = record["checkedBy"]!.AsArray().Select(c => (string)c!["kind"]!).ToList();
        Assert.Equal(["agent", "code"], by);
        Assert.Equal("sonnet-test", (string)record["checkedBy"]![0]!["name"]!);

        var report = TraceCheck.Run(app.Root, GraphStore.Load(GraphStore.PathFor(app.Repo)), null, false);
        Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report).Select(p => (string)p!)));
        var listed = report["findings"]![0]!;
        Assert.Equal((goesTo, verdict, label), ((string)listed["goesTo"]!, (string)listed["verdict"]!, (string)listed["label"]!));
        Assert.Equal(RealLine, (string)listed["quote"]!);
        Assert.Equal("X-0001", (string)report["queues"]![goesTo]![0]!);
        Assert.Equal(verdict, StateChanges.History(app.Root, StateKinds.FindingVerdict, (string)e["id"]!).Last().To);
        // Kept, never deleted, whatever the verdict.
        Assert.Single(TraceFile.Load(app.Root)["entries"]!.AsArray());
    }

    [Fact]
    public void A_quote_that_fails_gets_one_retry_then_is_cant_tell_in_the_bas_queue()
    {
        using var app = App();
        Save(app, Conflict());
        var retry = Answer(app, Prepare(app), Quoted("refuted", "this line is nowhere in the code at all"));
        Assert.Equal("retry", retry.Status);
        Assert.Contains("isn't word for word", retry.Errors![0]);
        Assert.Contains(".stangent/", retry.Prompt);

        var done = Answer(app, retry, Quoted("refuted", "still nowhere in the code at all, sorry"));
        Assert.Equal("recorded", done.Status);
        var e = Entry(app);
        Assert.Equal("cant-tell", (string)e["verdict"]!);
        Assert.Equal("refuted", (string)e["disproof"]!["claimed"]!);
        Assert.Null(e["disproof"]!["quote"]);
        Assert.DoesNotContain(e["evidence"]!.AsArray().OfType<JsonObject>(), r => (string)r["id"]! == "X-0001.disprove");
        Assert.Equal("ba", TraceFindings.GoesTo(e));
    }

    [Theory]
    [InlineData(1, 1)]        // real file, wrong line
    [InlineData(5000, 5001)]  // lines not shown
    public void A_real_quote_at_the_wrong_lines_fails(int start, int end)
    {
        using var app = App();
        Save(app, Conflict());
        var answer = Quoted("refuted");
        answer["start"] = start;
        answer["end"] = end;
        Assert.Equal("retry", Answer(app, Prepare(app), answer).Status);
    }

    [Fact]
    public void Cant_tell_needs_no_quote_and_a_missing_answer_ends_there_too()
    {
        using var app = App();
        Save(app, Conflict());
        var done = Answer(app, Prepare(app), new JsonObject { ["verdict"] = "cant-tell", ["why"] = "not enough" });
        Assert.Equal("recorded", done.Status);
        Assert.Equal("cant-tell", (string)Entry(app)["verdict"]!);
        Assert.Equal("ba", TraceFindings.GoesTo(Entry(app)));

        using var other = App();
        Save(other, Conflict());
        var retry = Answer(other, Prepare(other), "not json");
        Assert.Equal("retry", retry.Status);
        Assert.Equal("recorded", Answer(other, retry, "still not json").Status);
        Assert.Equal("cant-tell", (string)Entry(other)["verdict"]!);
    }

    [Fact]
    public void A_severity_suggestion_is_shown_and_never_set()
    {
        using var app = App();
        Save(app, Conflict());
        var answer = Quoted("stands");
        answer["suggestedSeverity"] = "blocker";
        Answer(app, Prepare(app), answer);
        var report = TraceCheck.Run(app.Root, null, null, false);
        var listed = report["findings"]![0]!;
        Assert.Equal(("blocker", Severity.Ungraded), ((string)listed["suggestedSeverity"]!, (string)listed["severity"]!));
    }

    [Fact]
    public void An_unchanged_bundle_means_no_call_and_changed_code_reopens_a_closed_finding_first()
    {
        using var app = App();
        Save(app, Conflict());
        Answer(app, Prepare(app), Quoted("refuted"));
        Assert.Equal(("current", "none"), (Prepare(app).Status, Prepare(app).Action));

        File.AppendAllText(Path.Combine(app.Root, "Core/OrderService.cs"), "\n");
        var src = File.ReadAllText(Path.Combine(app.Root, "Core/OrderService.cs")).Replace("total > 100", "total > 50");
        File.WriteAllText(Path.Combine(app.Root, "Core/OrderService.cs"), src);
        var step = Prepare(app);
        Assert.Equal(DisproveRun.Disprover, step.Agent);
        var e = Entry(app);
        Assert.Null(e["verdict"]);
        Assert.Null(e["disproof"]);
        Assert.Equal("ba", TraceFindings.GoesTo(e));
        Assert.Equal("unverified", StateChanges.History(app.Root, StateKinds.FindingVerdict, (string)e["id"]!).Last().To);
    }

    [Fact]
    public void A_persons_decision_isnt_tested_again()
    {
        using var app = App();
        var decided = Conflict();
        decided["decision"] = "code-wrong";
        Save(app, decided);
        Assert.Equal(("current", "none"), (Prepare(app).Status, Prepare(app).Action));
    }

    [Fact]
    public void A_finding_with_no_code_is_cant_tell_by_code_with_no_call()
    {
        using var app = App();
        Save(app, LinkRun.Entry("gap", "shop", Section(), Fsd50, null, null, null, "code", null, "no page"));
        var step = Prepare(app);
        Assert.Equal(("recorded", "none"), (step.Status, step.Action));
        var e = Entry(app);
        Assert.Equal("cant-tell", (string)e["verdict"]!);
        Assert.Null(e["disproof"]!["agent"]);
        Assert.Equal(("current", "none"), (Prepare(app).Status, Prepare(app).Action));
    }

    [Fact]
    public void A_gap_with_no_statement_still_gets_the_pages_code()
    {
        using var app = App();
        Save(app, LinkRun.Entry("gap", "shop", Section(), Fsd50, SpecApp.Post, null, null, "judge", null, Secret));
        var bundle = Bundle(app, Prepare(app));
        Assert.Contains(RealLine, bundle);
        Assert.Contains("doesn't do what this FSD sentence says", bundle);
        Assert.DoesNotContain(Secret, bundle);
    }

    [Fact]
    public void A_screens_gap_also_gets_the_code_behind_the_endpoints_it_reaches()
    {
        using var app = App();
        // The FSD's words are in the backend rule; the screen's own slice stops at the API.
        Save(app, LinkRun.Entry("gap", "shop", Section(), Fsd50, "page:/orders", null, null, "judge", null, Secret));
        var bundle = Bundle(app, Prepare(app));
        Assert.Contains(RealLine, bundle);
    }

    [Fact]
    public void A_verdict_typed_by_hand_is_a_problem()
    {
        using var app = App();
        Save(app, Conflict());
        var trace = TraceFile.Load(app.Root);
        trace["entries"]![0]!["verdict"] = "refuted";
        TraceFile.Save(app.Root, trace);
        var report = TraceCheck.Run(app.Root, null, null, false);
        Assert.Contains(Problems(report), p => ((string)p!).Contains("isn't what code wrote", StringComparison.Ordinal));
    }

    [Fact]
    public void An_agent_can_never_move_a_verdict()
    {
        using var app = App();
        Assert.Throws<StangentException>(() => StateChanges.Move(app.Root, StateKinds.FindingVerdict, "L1", null, "refuted", StateActor.Agent("x"), "no"));
        Assert.Throws<StangentException>(() => StateChanges.Move(app.Root, StateKinds.FindingVerdict, "L1", null, "refuted", StateActor.Person("x"), "no"));
    }

    [Fact]
    public void The_verdict_survives_a_read_a_write_and_a_re_run_of_the_links()
    {
        using var app = App();
        Save(app, Conflict());
        Answer(app, Prepare(app), Quoted("refuted"));
        var bytes = File.ReadAllText(TraceFile.PathIn(app.Root));
        TraceFile.Save(app.Root, TraceFile.Load(app.Root));
        Assert.Equal(bytes, File.ReadAllText(TraceFile.PathIn(app.Root)));
        Assert.Empty(SchemaRegistry.Embedded.Validate(TraceFile.Schema, TraceFile.Load(app.Root).ToJsonString()));

        LinkRun.ApplyEntries(app.Root, "shop", "1", [Conflict()]);
        Assert.Equal("refuted", (string)Entry(app)["verdict"]!);
        Assert.True((bool)TraceCheck.Run(app.Root, null, null, false)["ok"]!);
    }

    [Fact]
    public void Closed_findings_leave_the_impact_answer_and_page_wrong_isnt_verified()
    {
        using var app = App();
        Save(app, Conflict());
        Assert.Equal(1, (int)FsdImpact.Sections(app.Root, [SpecApp.Place], false)["total"]!);
        Answer(app, Prepare(app), Quoted("refuted"));
        Assert.Equal(0, (int)FsdImpact.Sections(app.Root, [SpecApp.Place], false)["total"]!);

        var wrong = Conflict();
        wrong["decision"] = "page-wrong";
        Save(app, wrong);
        Assert.False((bool)FsdImpact.Sections(app.Root, [SpecApp.Place], false)["items"]![0]!["verified"]!);
    }

    // ---- page-wrong ----

    [Fact]
    public void Page_wrong_is_a_decision_with_decided_by_from_git_and_makes_the_page_stale_until_it_is_written_again()
    {
        using var app = App();
        Save(app, Conflict());
        TraceCheck.Run(app.Root, null, null, false);
        var before = TraceFile.Load(app.Root);
        var set = app.Pages();
        Assert.DoesNotContain(Staleness.Report(set, new SpecPaths(app.Root), null)["stale"]!.AsArray(), p => (string)p!["id"]! == SpecApp.Post);

        var edited = TraceFile.Load(app.Root);
        edited["entries"]![0]!["decision"] = "page-wrong";
        TraceFile.Save(app.Root, edited);
        var check = TraceCheck.Run(app.Root, null, before, false);
        Assert.True((bool)check["ok"]!, string.Join("\n", Problems(check).Select(p => (string)p!)));
        Assert.Equal(1, TraceCheck.Attribute(app.Root, before, "pat"));
        var e = Entry(app);
        Assert.Equal(("page-wrong", "pat"), ((string)e["decision"]!, (string)e["decided_by"]!));
        Assert.Equal("page-owner", TraceFindings.GoesTo(e));
        Assert.Equal("page-wrong", StateChanges.History(app.Root, StateKinds.TraceDecision, (string)e["id"]!).Last().To);

        var stale = Staleness.Report(set, new SpecPaths(app.Root), null);
        var item = stale["stale"]!.AsArray().Single(p => (string)p!["id"]! == SpecApp.Post)!;
        Assert.Equal("pat", (string)item["reportedWrong"]![0]!["by"]!);
        Assert.Contains("a person said this page is wrong", Staleness.Markdown(stale));

        var page = set.Find(SpecApp.Post)!;
        var step = PageRun.Prepare(set, page, app.Repo);
        Assert.Equal(PageRun.Writer, step.Agent);
        var bundle = File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "bundle.md"));
        Assert.Contains("A person said this page is wrong", bundle);
        // The note is after the hashed part: the hash the page was recorded with is still what its inputs give.
        Assert.Equal((string)SpecIndex.Load(app.Root).Pages[SpecApp.Post]["generated"]!["bundleHash"]!, Bundles.Build(set, page, null).InputHash);

        File.WriteAllText(Path.Combine(app.Root, step.Output!), new JsonObject
        {
            ["title"] = "Place an order",
            ["shows"] = new JsonArray(), ["actions"] = new JsonArray(),
            ["rules"] = new JsonArray(new JsonObject { ["text"] = "Orders over 100 are refused with \"Orders over 100 need approval\".", ["cites"] = new JsonArray(SpecApp.Place) }),
            ["sideEffects"] = new JsonArray(new JsonObject { ["text"] = "Placing an order publishes OrderPlaced, which sends a receipt.", ["cites"] = new JsonArray(SpecApp.Place, SpecApp.Receipt) }),
        }.ToJsonString());
        step = PageRun.Check(app.Pages(), page, app.Repo, null);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), "{\"findings\":[]}");
        Assert.Equal("recorded", PageRun.Check(app.Pages(), page, app.Repo, null).Status);

        e = Entry(app);
        Assert.Null(e["decision"]);
        Assert.Null(e["decided_by"]);
        Assert.Equal("current", PageRun.Prepare(app.Pages(), page, app.Repo).Status);
    }

    [Fact]
    public void Page_wrong_needs_a_page()
    {
        using var app = App();
        var gap = LinkRun.Entry("gap", "shop", Section(), Fsd50, null, null, null, "code", null, "no page");
        gap["decision"] = "page-wrong";
        Save(app, gap);
        Assert.Contains(Problems(TraceCheck.Run(app.Root, null, null, false)), p => ((string)p!).Contains("page-wrong needs a page", StringComparison.Ordinal));
    }

    // ---- the hard bar ----

    [Fact]
    public void Adding_the_step_changes_no_saved_hash_or_prompt_even_after_it_has_run()
    {
        using var app = SavedWorkTests.App(out var verifierInput);
        Assert.Equal(string.Join("\n", SavedWorkTests.Saved), string.Join("\n", SavedWorkTests.Hashes(app, verifierInput)));

        SavedWorkTests.CleanGraph(app);
        app.Repo.Config.Fsd.Disprove = true;
        Save(app, Conflict());
        Answer(app, Prepare(app), Quoted("refuted"));
        Assert.Equal(string.Join("\n", SavedWorkTests.Saved), string.Join("\n", SavedWorkTests.Hashes(app, verifierInput)));
        Assert.Equal("current", PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo).Status);
        // The switch isn't in the links' hash: turning it on re-judges nothing (with findings on, as here, or off).
        var index = SpecIndex.Load(app.Root);
        Assert.Equal(LinkRun.JudgeInputs(index.Sections[("shop", "1")], "shop", [SpecApp.Post], index, null, true), SavedWorkTests.Saved[3].Split(' ')[1]);
    }

    [Fact]
    public void A_file_from_before_the_step_still_reads_and_checks()
    {
        using var app = SavedWorkTests.App(out _);
        Save(app, Conflict());
        var old = File.ReadAllText(TraceFile.PathIn(app.Root));
        Assert.DoesNotContain("verdict", old);
        Assert.True((bool)TraceCheck.Run(app.Root, null, null, false)["ok"]!);
        var listed = TraceCheck.Run(app.Root, null, null, false)["findings"]![0]!;
        Assert.Equal(("unverified", "ba"), ((string)listed["verdict"]!, (string)listed["goesTo"]!));
    }
}

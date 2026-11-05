using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;
using static Stangent.Core.Tests.FsdLinkTests;

namespace Stangent.Core.Tests;

/// <summary>The link judge can say <c>partial</c> and <c>unclear</c> (C.4, D124), with a fake AI's answers. Nothing here has run on a real AI.</summary>
public class FinerVerdictTests
{
    const string On = "components: { judge-finer-verdicts: true }\n";
    const string Sentence = "Orders over 50 need approval.";

    static SpecApp Finer(bool findings = true) => App(On, findings);

    static string Bundle(SpecApp app, RunStep step) => SharedHead.Assemble(File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "judge.md")));

    static JsonObject Partial(string covered = "the limit's existence", string missing = "who approves") =>
        new()
        {
            ["state"] = "partial", ["fsdQuote"] = Sentence, ["page"] = SpecApp.Post, ["statement"] = "R1", ["pageQuote"] = "Orders over 100 are refused",
            ["covered"] = covered, ["missing"] = missing, ["note"] = "n",
        };

    static JsonObject Unclear(string note = "over 50 of what?") => new() { ["state"] = "unclear", ["fsdQuote"] = Sentence, ["note"] = note };

    static RunStep Answer(SpecApp app, RunStep step, params JsonObject[] links)
    {
        Write(app, step, new JsonObject { ["links"] = new JsonArray(links) });
        return LinkRun.Check(app.Pages(), app.Repo, GraphStore.Load(GraphStore.PathFor(app.Repo))!, "shop", "1", null);
    }

    static RunStep Prepare(SpecApp app) => LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");

    static List<JsonObject> Entries(SpecApp app) => TraceFile.Load(app.Root)["entries"]!.AsArray().OfType<JsonObject>().ToList();

    [Fact]
    public void Off_the_bundle_and_schema_are_the_old_ones_and_the_new_states_are_refused()
    {
        using var app = App();
        var step = Prepare(app);
        var bundle = Bundle(app, step);
        Assert.Contains(Bundles.Instructions("link-judge.md"), bundle);
        Assert.DoesNotContain("partial", bundle);
        Assert.Equal("link-judge-output.v1.json", (string)step.ToJson("x")["next"]!["schema"]!);
        Assert.Equal("retry", Answer(app, step, Partial()).Status);
    }

    [Fact]
    public void On_it_has_its_own_instructions_schema_and_hash()
    {
        using var off = App();
        using var on = Finer();
        var offBundle = Bundle(off, Prepare(off));
        var step = Prepare(on);
        Assert.Contains(Bundles.Instructions("link-judge-finer.md"), Bundle(on, step));
        Assert.Equal("link-judge-finer-output.v1.json", (string)step.ToJson("x")["next"]!["schema"]!);
        Assert.NotEqual(offBundle, Bundle(on, step));
        Assert.StartsWith("You compare", Bundles.Instructions("link-judge-finer.md"));
    }

    [Fact]
    public void A_partial_is_a_finding_with_both_quotes_and_what_is_covered_and_what_is_not()
    {
        using var app = Finer();
        Assert.Equal("recorded", Answer(app, Prepare(app), Partial()).Status);
        var entry = Assert.Single(Entries(app));
        Assert.Equal("partial", (string)entry["state"]!);
        Assert.Equal(("the limit's existence", "who approves"), ((string)entry["partial"]!["covered"]!, (string)entry["partial"]!["missing"]!));

        var report = TraceCheck.Run(app.Root, GraphStore.Load(GraphStore.PathFor(app.Repo)), null, fix: false);
        Assert.True((bool)report["ok"]!, report.ToJsonString());
        Assert.Equal(1, (int)report["counts"]!["findings"]!);
        Assert.Equal(1, (int)report["counts"]!["byState"]!["partial"]!);
        var listed = report["findings"]![0]!;
        Assert.StartsWith("X-", (string)listed["id"]!);
        Assert.Equal("who approves", (string)listed["partial"]!["missing"]!);
    }

    [Fact]
    public void A_partial_needs_both_quotes_and_both_sides()
    {
        using var app = Finer();
        var noMissing = Partial();
        noMissing.Remove("missing");
        var noPageQuote = Partial();
        noPageQuote.Remove("pageQuote");
        var step = Answer(app, Prepare(app), noMissing, noPageQuote);
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains("covered and missing", StringComparison.Ordinal));
        Assert.Contains(step.Errors!, e => e.Contains("a partial link needs page, statement and pageQuote", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unclear_one_goes_to_the_BA_labelled_and_is_not_counted_as_a_difference()
    {
        using var app = Finer();
        Assert.Equal("recorded", Answer(app, Prepare(app), Unclear()).Status);
        var report = TraceCheck.Run(app.Root, GraphStore.Load(GraphStore.PathFor(app.Repo)), null, fix: false);
        Assert.True((bool)report["ok"]!, report.ToJsonString());
        Assert.Equal((0, 1, 1), ((int)report["counts"]!["findings"]!, (int)report["counts"]!["unclear"]!, (int)report["counts"]!["undecided"]!));
        var listed = report["findings"]![0]!;
        Assert.Equal("ba", (string)listed["goesTo"]!);
        Assert.False((bool)listed["difference"]!);
        Assert.Contains("not counted as a difference: over 50 of what?", (string)listed["label"]!);
        Assert.False(TraceFindings.Testable(Entries(app)[0]));
        Assert.Equal(1, (int)FsdStatus.Of(app.Root, "shop")["queues"]!["unclear"]!);
    }

    [Fact]
    public void An_unclear_one_needs_the_FSD_quote_and_a_reason_and_names_no_page()
    {
        using var app = Finer();
        var withPage = Unclear();
        withPage["page"] = SpecApp.Post;
        var noQuote = Unclear();
        noQuote.Remove("fsdQuote");
        var step = Answer(app, Prepare(app), withPage, noQuote, Unclear("  "));
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains("names no page", StringComparison.Ordinal));
        Assert.Contains(step.Errors!, e => e.Contains("an unclear link needs fsdQuote", StringComparison.Ordinal));
        Assert.Contains(step.Errors!, e => e.Contains("its reason in note", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unclear_one_does_not_count_a_section_as_linked_and_a_partial_does()
    {
        using var unclear = Finer();
        Answer(unclear, Prepare(unclear), Unclear());
        Assert.DoesNotContain("1", TraceCoverage.All(unclear.Root, TraceFile.Load(unclear.Root))["shop"]!["linked"]!.AsArray().Select(s => (string?)s!["section"]));
        using var partial = Finer();
        Answer(partial, Prepare(partial), Partial());
        Assert.Contains("1", TraceCoverage.All(partial.Root, TraceFile.Load(partial.Root))["shop"]!["linked"]!.AsArray().Select(s => (string?)s!["section"]));
    }

    [Fact]
    public void With_findings_off_only_links_are_recorded_and_the_run_says_how_many_it_left_out()
    {
        using var app = Finer(findings: false);
        Assert.Equal("recorded", Answer(app, Prepare(app), Partial(), Unclear()).Status);
        Assert.Empty(Entries(app));
        Assert.Equal(1, (int)LinkRun.LoadRuns(app.Root)["shop#1"]!["unclearLeftOut"]!);
    }

    [Fact]
    public void Turning_it_on_runs_a_section_again_and_an_unchanged_one_makes_no_call()
    {
        using var app = Finer();
        Answer(app, Prepare(app), Partial());
        Assert.Equal("current", Prepare(app).Status);
        File.WriteAllText(Path.Combine(app.Root, "stangent.yaml"), File.ReadAllText(Path.Combine(app.Root, "stangent.yaml")).Replace(On, "", StringComparison.Ordinal));
        var off = new SpecAppView(app);
        Assert.Equal("judge", off.Prepare().Action);
    }

    [Fact]
    public void Impact_reads_a_partial_as_a_difference_in_its_contract()
    {
        using var app = Finer();
        Answer(app, Prepare(app), Partial());
        var cited = SpecIndex.Load(app.Root).Statement(SpecApp.Post, "R1")!["cites"]!.AsArray().Select(c => (string)c!).ToList();
        var state = FsdImpact.Sections(app.Root, cited, expand: true)["items"]![0]!["states"]![0]!;
        Assert.Equal("conflict", (string)state!);
    }

    /// <summary>The same repo read again, as a new command would, with the config as it is now.</summary>
    sealed class SpecAppView(SpecApp app)
    {
        public RunStep Prepare() => LinkRun.Prepare(app.Pages(), StangentRepo.Init(app.Root).Repo, "shop", "1");
    }
}

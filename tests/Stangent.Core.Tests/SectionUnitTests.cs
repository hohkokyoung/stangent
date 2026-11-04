using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;
using static Stangent.Core.Tests.FsdLinkTests;

namespace Stangent.Core.Tests;

/// <summary>Small neighbouring sections judged as one unit (C.6, D124), with a fake AI's answers. Whether it helps is unknown.</summary>
public class SectionUnitTests
{
    const string Button = "The orders screen has a Place order button.";
    const string Over = "Orders over 50 need approval.";
    const string Points = "Customers earn one point per dollar.";

    static SpecApp Grouped(bool findings = true)
    {
        var app = App(findings: findings);
        app.Repo.Config.Fsd.GroupSections = true;
        return app;
    }

    static RunStep Prepare(SpecApp app, string section = "1") => LinkRun.Prepare(app.Pages(), app.Repo, "shop", section);

    static RunStep Check(SpecApp app, string section = "1") => LinkRun.Check(app.Pages(), app.Repo, GraphStore.Load(GraphStore.PathFor(app.Repo))!, "shop", section, null);

    static JsonObject On(string section, JsonObject link)
    {
        link["section"] = section;
        return link;
    }

    static JsonObject Conflict() => On("1", Link("conflict", Over, SpecApp.Post, "R1", "Orders over 100 are refused", "50 and 100"));

    static JsonObject Gap(string section = "2") => On(section, Link("gap", Points, null, null, null, "no points"));

    static List<JsonObject> Entries(SpecApp app) => TraceFile.Load(app.Root)["entries"]!.AsArray().OfType<JsonObject>().ToList();

    static string Input(SpecApp app, RunStep step) => File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "judge.md"));

    static JsonObject Plain(JsonObject e) => new() { ["id"] = (string)e["id"]!, ["note"] = (string?)e["note"], ["quote"] = (string?)e["fsd"]!["quote"] };

    static void Judge(SpecApp app, params JsonObject[] links)
    {
        Write(app, Prepare(app), Links(links));
        Assert.Equal("recorded", Check(app).Status);
    }

    [Fact]
    public void Off_a_section_is_judged_alone_with_the_old_schema()
    {
        using var app = App();
        var step = Prepare(app);
        Assert.Equal("link-judge-output.v1.json", (string)step.ToJson("x")["next"]!["schema"]!);
        Assert.DoesNotContain(Points, Input(app, step));
    }

    [Fact]
    public void On_the_first_section_runs_the_unit_and_the_others_say_grouped()
    {
        using var app = Grouped();
        var step = Prepare(app);
        Assert.Equal(("judge", LinkRun.Judge), (step.Action, step.Agent));
        Assert.Equal("link-judge-group-output.v1.json", (string)step.ToJson("x")["next"]!["schema"]!);
        Assert.Contains(Over, Input(app, step));
        Assert.Contains(Points, Input(app, step));
        Assert.Contains("neighbouring sections", Input(app, step));

        var other = Prepare(app, "2");
        Assert.Equal(("grouped", "none"), (other.Status, other.Action));
        Assert.Contains("fsd prepare shop 1", other.Warning!);
    }

    [Fact]
    public void A_link_must_cite_its_own_section()
    {
        using var app = Grouped();
        var step = Prepare(app);
        // The quote is section 2's, and the link says section 1.
        Write(app, step, Links(Gap("1")));
        step = Check(app);
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.Contains("isn't a sentence from the section's text", StringComparison.Ordinal));

        Write(app, step, Links(Gap("9")));
        step = Check(app);
        Assert.Equal("recorded", step.Status);
        Assert.Empty(Entries(app));
        Assert.Equal(1, (int)LinkRun.LoadRuns(app.Root)["shop#1"]!["unit"]!["rejected"]!);
    }

    [Fact]
    public void A_link_with_no_section_is_refused_by_the_schema()
    {
        using var app = Grouped();
        var step = Prepare(app);
        Write(app, step, Links(Link("gap", Points, null, null, null, "no points")));
        Assert.Equal("retry", Check(app).Status);
    }

    [Fact]
    public void Each_link_goes_on_its_own_section_and_the_runs_record_the_unit()
    {
        using var app = Grouped();
        Judge(app, Conflict(), Gap());
        var entries = Entries(app);
        Assert.Equal(["1", "2"], entries.Select(e => (string)e["fsd"]!["section"]!).Order());
        var runs = LinkRun.LoadRuns(app.Root);
        foreach (var key in new[] { "shop#1", "shop#2" })
        {
            var unit = runs[key]!["unit"]!;
            Assert.Equal(("group", "1"), ((string)unit["kind"]!, (string)unit["leader"]!));
            Assert.Equal(["1", "2"], unit["sections"]!.AsArray().Select(s => (string)s!));
        }
        var report = TraceCheck.Run(app.Root, GraphStore.Load(GraphStore.PathFor(app.Repo)), null, fix: false);
        Assert.True((bool)report["ok"]!, report.ToJsonString());
        Assert.Equal(("current", "none"), (Prepare(app).Status, Prepare(app).Action));
    }

    [Fact]
    public void The_agree_checker_reads_every_section_of_the_unit()
    {
        using var app = Grouped();
        Write(app, Prepare(app), Links(On("1", Link("agree", Button, "page:/orders", "A1", "The Place order button posts the order."))));
        var step = Check(app);
        Assert.Equal(LinkRun.Checker, step.Agent);
        var checker = File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "checker.md"));
        Assert.Contains(Over, checker);
        Assert.Contains(Points, checker);
        Write(app, step, new JsonObject { ["checks"] = new JsonArray(new JsonObject { ["link"] = "K1", ["verdict"] = "confirm", ["note"] = "same" }) });
        Assert.Equal("recorded", Check(app).Status);
        Assert.Equal("confirmed", (string)Entries(app).Single()["check"]!);
    }

    [Fact]
    public void Turning_it_on_runs_turning_it_off_reuses_and_turning_it_on_again_reuses()
    {
        using var app = App();
        // The sections' own paid work first.
        Write(app, Prepare(app), Links(Link("conflict", Over, SpecApp.Post, "R1", "Orders over 100 are refused", "alone")));
        Assert.Equal("recorded", Check(app).Status);
        var alone = Entries(app).Single();

        app.Repo.Config.Fsd.GroupSections = true;
        var grouped = Prepare(app);
        Assert.Equal("judge", grouped.Action);
        Write(app, grouped, Links(Conflict(), Gap()));
        Assert.Equal("recorded", Check(app).Status);
        var together = Entries(app);
        Assert.Equal(2, together.Count);

        app.Repo.Config.Fsd.GroupSections = false;
        var back = Prepare(app);
        Assert.Equal(("current", "none"), (back.Status, back.Action));
        var own = Entries(app).Where(e => (string)e["fsd"]!["section"]! == "1").ToList();
        Assert.True(JsonNode.DeepEquals(Plain(alone), Plain(Assert.Single(own))));
        Assert.Null(LinkRun.LoadRuns(app.Root)["shop#1"]!["unit"]);

        app.Repo.Config.Fsd.GroupSections = true;
        var again = Prepare(app);
        Assert.Equal(("current", "none"), (again.Status, again.Action));
        Assert.Equal(together.Select(e => (string)e["id"]!).Order(), Entries(app).Select(e => (string)e["id"]!).Order());
        Assert.NotNull(LinkRun.LoadRuns(app.Root)["shop#1"]!["unit"]);
    }

    [Fact]
    public void A_decision_on_a_groups_link_doesnt_ride_back_on_a_saved_copy()
    {
        using var app = Grouped();
        Judge(app, Conflict(), Gap());
        var trace = TraceFile.Load(app.Root);
        trace["entries"]![0]!["decision"] = "code-wrong";
        trace["entries"]![0]!["decided_by"] = "Pat";
        TraceFile.Save(app.Root, trace);
        var runs = LinkRun.LoadRuns(app.Root);
        Assert.Null(runs["shop#1"]!["saved"]!["group"]!["entries"]![0]!["decision"]);
    }

    [Fact]
    public void A_section_over_the_limit_stays_alone_and_the_limit_is_a_setting()
    {
        using var app = Grouped();
        app.Repo.Config.Fsd.GroupMaxChars = 10;
        var step = Prepare(app);
        Assert.Equal("link-judge-output.v1.json", (string)step.ToJson("x")["next"]!["schema"]!);
        Assert.DoesNotContain(Points, Input(app, step));
        Assert.Equal(4000, new StangentConfig().Fsd.GroupMaxChars);
    }

    [Fact]
    public void Units_stop_at_another_parent_and_at_a_section_with_no_text()
    {
        using var temp = new TempRepo();
        static JsonObject S(string id, string? parent, string text) => new() { ["id"] = id, ["parent"] = parent, ["text"] = text, ["title"] = id };
        var units = SectionUnits.Of(temp.Repo, "none", [S("1", null, ""), S("1.1", "1", "a"), S("1.2", "1", "b"), S("2", null, "c"), S("2.1", "2", "d"), S("2.2", "2", ""), S("2.3", "2", "e")]);
        Assert.Equal([["1.1", "1.2"], ["2"], ["2.1"], ["2.3"]], units.Select(u => u.Select(s => (string)s["id"]!).ToArray()));
    }

    [Fact]
    public void A_unit_that_runs_over_the_limit_starts_again()
    {
        using var temp = new TempRepo();
        temp.Repo.Config.Fsd.GroupMaxChars = 5;
        static JsonObject S(string id, string text) => new() { ["id"] = id, ["parent"] = null, ["text"] = text, ["title"] = id };
        var units = SectionUnits.Of(temp.Repo, "none", [S("a", "abc"), S("b", "de"), S("c", "f"), S("big", "abcdefgh"), S("d", "g")]);
        Assert.Equal([["a", "b"], ["c"], ["d"]], units.Select(u => u.Select(s => (string)s["id"]!).ToArray()));
    }

    [Fact]
    public void Findings_off_records_only_links_for_each_section()
    {
        using var app = Grouped(findings: false);
        Judge(app, Conflict(), Gap());
        Assert.Empty(Entries(app));
        Assert.NotNull(LinkRun.LoadRuns(app.Root)["shop#2"]!["unit"]);
    }

    [Fact]
    public void Requirements_keep_a_section_out_of_a_unit()
    {
        using var app = WithRequirements();
        app.Repo.Config.Fsd.GroupSections = true;
        Assert.Equal(LinkRun.RequirementJudge, Prepare(app).Agent);
    }

    [Fact]
    public void Requirement_judge_can_say_partial_and_unclear_with_the_finer_switch()
    {
        using var app = WithRequirements();
        app.Repo.Config.Fsd.JudgeFinerVerdicts = true;
        var step = Prepare(app);
        Assert.Equal("requirement-judge-finer-output.v1.json", (string)step.ToJson("x")["next"]!["schema"]!);
        var partial = new JsonObject { ["state"] = "partial", ["requirement"] = "R-0002", ["page"] = SpecApp.Post, ["statement"] = "R1", ["pageQuote"] = "Orders over 100 are refused", ["covered"] = "a limit", ["missing"] = "the number", ["note"] = "n" };
        var unclear = new JsonObject { ["state"] = "unclear", ["requirement"] = "R-0003", ["note"] = "which receipt?" };
        var gap = new JsonObject { ["state"] = "gap", ["requirement"] = "R-0001", ["note"] = "n" };
        Write(app, step, new JsonObject { ["links"] = new JsonArray(partial, unclear, gap) });
        Assert.Equal("recorded", Check(app).Status);
        var entries = Entries(app);
        Assert.Equal(Over, (string)entries.Single(e => (string)e["state"]! == "partial")["fsd"]!["quote"]!);
        Assert.Equal("R-0003", (string)entries.Single(e => (string)e["state"]! == "unclear")["fsd"]!["requirement"]!);
    }
}

/// <summary>D124's switches off leave what the old build gave: prompts, bundles and every input hash. <see cref="SavedWorkTests"/> pins those values; this holds them with each switch named off, and with each turned on and off.</summary>
public class SwitchedOffStaysTheSameTests
{

    static (string Bundle, string Trace, string Runs, string Output) Run(SpecApp app)
    {
        var step = Stangent.Spec.Fsd.LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        var bundle = File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "judge.md"));
        return (bundle, File.Exists(TraceFile.PathIn(app.Root)) ? File.ReadAllText(TraceFile.PathIn(app.Root)) : "",
            File.Exists(Stangent.Spec.Fsd.LinkRun.RunsFile(app.Root)) ? File.ReadAllText(Stangent.Spec.Fsd.LinkRun.RunsFile(app.Root)) : "", step.Output!);
    }

    [Fact]
    public void Each_switch_named_off_gives_the_same_bundle_as_none_named()
    {
        using var none = App();
        using var named = App("components: { table-facts: false, judge-finer-verdicts: false, group-sections: false, spec-ask: false }\n");
        var (a, b) = (Run(none), Run(named));
        Assert.Equal(a.Bundle.Replace(none.Root, "", StringComparison.Ordinal), b.Bundle.Replace(named.Root, "", StringComparison.Ordinal));
        Assert.Equal(a.Runs, b.Runs);
    }

    [Fact]
    public void Off_a_runs_record_has_no_unit_and_a_saved_trace_has_no_new_field()
    {
        using var app = App();
        Write(app, Stangent.Spec.Fsd.LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1"), Links(Link("conflict", "Orders over 50 need approval.", SpecApp.Post, "R1", "Orders over 100 are refused")));
        Assert.Equal("recorded", Stangent.Spec.Fsd.LinkRun.Check(app.Pages(), app.Repo, GraphStore.Load(GraphStore.PathFor(app.Repo))!, "shop", "1", null).Status);
        var runs = File.ReadAllText(Stangent.Spec.Fsd.LinkRun.RunsFile(app.Root));
        Assert.DoesNotContain("\"unit\"", runs);
        Assert.DoesNotContain("\"saved\"", runs);
        var trace = File.ReadAllText(TraceFile.PathIn(app.Root));
        Assert.DoesNotContain("table_fact", trace);
        Assert.DoesNotContain("partial", trace);
    }

    [Fact]
    public void Finer_verdicts_on_then_off_gives_back_the_old_hash()
    {
        using var app = App();
        var off = Run(app).Bundle;
        app.Repo.Config.Fsd.JudgeFinerVerdicts = true;
        Assert.NotEqual(off, Run(app).Bundle);
        app.Repo.Config.Fsd.JudgeFinerVerdicts = false;
        Assert.Equal(off, Run(app).Bundle);
    }
}

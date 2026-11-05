using System.Text.Json.Nodes;
using Stangent.Spec.Pages;

namespace Stangent.Core.Tests;

/// <summary>T.1: a person's correction is recorded, approved and survives the page being written again.</summary>
public class PageCorrectionTests
{
    const string Wrong = "Orders over 100 are refused with \"Orders over 100 need approval\".";
    static SpecApp App() => new("version: 1\ncomponents: { page-corrections: true }\n");

    static JsonObject S(string text, params string[] cites) => new() { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) };

    static JsonObject Output() => new()
    {
        ["title"] = "Place an order", ["shows"] = new JsonArray(), ["actions"] = new JsonArray(),
        ["rules"] = new JsonArray(S(Wrong, SpecApp.Place)),
        ["sideEffects"] = new JsonArray(S("Placing an order publishes OrderPlaced, which sends a receipt.", SpecApp.Place, SpecApp.Receipt)),
    };

    static void Write(SpecApp app, JsonObject output, bool retry = false)
    {
        var def = app.Pages().Find(SpecApp.Post)!;
        var step = PageRun.Prepare(app.Pages(), def, app.Repo, retry);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), output.ToJsonString());
        step = PageRun.Check(app.Pages(), def, app.Repo, null);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), "{\"findings\":[]}");
        Assert.Equal("recorded", PageRun.Check(app.Pages(), def, app.Repo, null).Status);
    }

    static JsonObject Page(SpecApp app) => (JsonObject)PageRun.LoadJson(new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!))!;

    static IEnumerable<string> Texts(SpecApp app) => PageChecks.Statements(Page(app)).Select(s => (string)s.Statement["text"]!);

    static string Rule(SpecApp app) => PageChecks.Statements(Page(app)).First(s => s.Section == "rules").Id;

    [Fact]
    public void Off_it_refuses_and_a_page_with_none_is_as_it_was()
    {
        using var app = new SpecApp();
        Write(app, Output());
        var e = Assert.Throws<StangentException>(() => PageCorrections.Propose(app.Pages(), app.Repo, SpecApp.Post, Rule(app), "x", "why", "me"));
        Assert.Equal("component_off", e.Code);
        Assert.False(File.Exists(PageCorrections.FileIn(app.Root)));
    }

    [Fact]
    public void A_proposal_changes_nothing_until_a_person_approves()
    {
        using var app = App();
        Write(app, Output());
        var c = PageCorrections.Propose(app.Pages(), app.Repo, SpecApp.Post, Rule(app), "Orders over 50 need approval.", "the code's limit is 50 since March", "ana");
        Assert.Equal("proposed", (string)c["state"]!);
        Assert.Contains(Wrong, Texts(app));

        var hash = (string)Page(app)["generated"]!["bundleHash"]!;
        PageCorrections.Approve(app.Pages(), app.Repo, "C-0001", "ben");
        Assert.Contains("Orders over 50 need approval.", Texts(app));
        Assert.DoesNotContain(Wrong, Texts(app));
        Assert.Equal(hash, (string)Page(app)["generated"]!["bundleHash"]!);
        // No AI run follows: its inputs didn't move.
        Assert.Equal("current", PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo).Status);
        var md = File.ReadAllText(new SpecPaths(app.Root).PageMarkdown(app.Pages().Find(SpecApp.Post)!));
        Assert.Contains("(corrected by a person, C-0001)", md);
        Assert.Equal("approved", (string)StateChangesLast(app, "C-0001")!);
    }

    static string? StateChangesLast(SpecApp app, string id) => Stangent.Core.State.StateChanges.History(app.Root, Stangent.Core.State.StateKinds.PageCorrection, id).LastOrDefault()?.To;

    [Fact]
    public void It_survives_the_page_being_written_again_and_keeps_its_id()
    {
        using var app = App();
        Write(app, Output());
        var rule = Rule(app);
        PageCorrections.Propose(app.Pages(), app.Repo, SpecApp.Post, rule, "Orders over 50 need approval.", "why", "ana");
        PageCorrections.Approve(app.Pages(), app.Repo, "C-0001", "ben");

        // The writer says the same wrong thing again.
        File.Delete(new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!));
        Write(app, Output());
        Assert.Contains("Orders over 50 need approval.", Texts(app));
        Assert.DoesNotContain(Wrong, Texts(app));
    }

    [Fact]
    public void Regenerating_over_a_corrected_page_finds_it_by_id_and_a_removal_sticks_too()
    {
        using var app = App();
        Write(app, Output());
        var receipt = PageChecks.Statements(Page(app)).First(s => s.Section == "sideEffects").Id;
        var rule = Rule(app);
        PageCorrections.Propose(app.Pages(), app.Repo, SpecApp.Post, rule, "Orders over 50 need approval.", "why", "ana");
        PageCorrections.Approve(app.Pages(), app.Repo, "C-0001", "ben");
        PageCorrections.Propose(app.Pages(), app.Repo, SpecApp.Post, receipt, null, "no receipt is sent any more", "ana");
        PageCorrections.Approve(app.Pages(), app.Repo, "C-0002", "ben");
        Assert.DoesNotContain(Texts(app), t => t.Contains("receipt", StringComparison.Ordinal));

        ForceRun(app);
        Write(app, Output());
        Assert.Equal(rule, Rule(app));
        Assert.Equal(["Orders over 50 need approval."], Texts(app));
        Assert.Contains("Removed by a person", File.ReadAllText(new SpecPaths(app.Root).PageMarkdown(app.Pages().Find(SpecApp.Post)!)));
    }

    /// <summary>The way a page with the same inputs runs again: its recorded hash no longer matches.</summary>
    static void ForceRun(SpecApp app)
    {
        var page = Page(app);
        page["generated"]!["bundleHash"] = "moved";
        File.WriteAllText(new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!), CanonicalJson.Serialize(page));
    }

    [Fact]
    public void A_statement_gone_from_the_page_is_said_not_lost()
    {
        using var app = App();
        Write(app, Output());
        PageCorrections.Propose(app.Pages(), app.Repo, SpecApp.Post, Rule(app), "Orders over 50 need approval.", "why", "ana");
        PageCorrections.Approve(app.Pages(), app.Repo, "C-0001", "ben");
        File.Delete(new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!));
        var other = Output();
        other["rules"] = new JsonArray();
        Write(app, other);
        var row = PageCorrections.Report(app.Pages(), app.Root, null)["corrections"]![0]!;
        Assert.True((bool)row["notApplied"]!);
    }

    [Fact]
    public void Withdrawn_is_left_off_next_time_and_a_second_correction_waits_for_that()
    {
        using var app = App();
        Write(app, Output());
        var rule = Rule(app);
        PageCorrections.Propose(app.Pages(), app.Repo, SpecApp.Post, rule, "Orders over 50 need approval.", "why", "ana");
        PageCorrections.Approve(app.Pages(), app.Repo, "C-0001", "ben");
        Assert.Equal("already_corrected", Assert.Throws<StangentException>(() => PageCorrections.Propose(app.Pages(), app.Repo, SpecApp.Post, rule, "other", "why", "ana")).Code);
        Assert.Equal("withdrawn", (string)PageCorrections.Reject(app.Repo, "C-0001", "ben", "wrong call")["state"]!);
        ForceRun(app);
        Write(app, Output());
        Assert.Contains(Wrong, Texts(app));
    }

    [Fact]
    public void An_agent_or_code_can_never_approve()
    {
        var kind = Stangent.Core.State.StateKinds.Get(Stangent.Core.State.StateKinds.PageCorrection);
        Assert.All(kind.Moves, m => Assert.Equal([Stangent.Core.State.StateActor.PersonKind], m.Who));
    }
}

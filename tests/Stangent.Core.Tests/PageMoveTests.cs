using System.Text.Json.Nodes;
using Stangent.Spec.Pages;

namespace Stangent.Core.Tests;

/// <summary>I.4: code that only moved takes the page's citations with it, with no AI; anything else still asks for a run.</summary>
public class PageMoveTests
{
    static SpecApp App(bool on = true) => new($"version: 1\ncomponents: {{ follow-moves: {(on ? "true" : "false")} }}\n");

    static JsonObject S(string text, params string[] cites) => new() { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) };

    static void Write(SpecApp app)
    {
        var def = app.Pages().Find(SpecApp.Post)!;
        var step = PageRun.Prepare(app.Pages(), def, app.Repo);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), new JsonObject
        {
            ["title"] = "Place an order", ["shows"] = new JsonArray(), ["actions"] = new JsonArray(),
            ["rules"] = new JsonArray(S("Orders over 100 are refused with \"Orders over 100 need approval\".", SpecApp.Place)), ["sideEffects"] = new JsonArray(),
        }.ToJsonString());
        step = PageRun.Check(app.Pages(), def, app.Repo, null);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), "{\"findings\":[]}");
        Assert.Equal("recorded", PageRun.Check(app.Pages(), def, app.Repo, null).Status);
    }

    /// <summary>A line added above every point in the file: each one's code is the same, one line further down.</summary>
    static void ShiftFile(SpecApp app, string file)
    {
        var path = Path.Combine(app.Root, file);
        File.WriteAllText(path, "// a line added at the top\n" + File.ReadAllText(path));
        var graph = Stangent.Graph.GraphStore.Load(Stangent.Graph.GraphStore.PathFor(app.Repo))!;
        foreach (var fragment in graph.Fragments.Values)
            for (var i = 0; i < fragment.Nodes.Count; i++)
                if (fragment.Nodes[i].File == file)
                    fragment.Nodes[i] = fragment.Nodes[i] with { Line = fragment.Nodes[i].Line + 1, EndLine = fragment.Nodes[i].EndLine + 1 };
        Stangent.Graph.GraphStore.Save(graph, Stangent.Graph.GraphStore.PathFor(app.Repo));
    }

    const string File1 = "Core/OrderService.cs";

    static string RecordedHash(SpecApp app) => (string)((JsonObject)PageRun.LoadJson(new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!))!)["generated"]!["bundleHash"]!;

    [Fact]
    public void Off_a_shifted_line_asks_for_a_run_as_it_always_did()
    {
        using var app = App(on: false);
        Write(app);
        Assert.False(File.Exists(PageMoves.FileFor(new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!))));
        ShiftFile(app, File1);
        Assert.Equal("write", PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo).Status);
    }

    [Fact]
    public void A_shifted_line_moves_the_page_on_with_no_ai_and_its_statements_stay()
    {
        using var app = App();
        Write(app);
        var def = app.Pages().Find(SpecApp.Post)!;
        var paths = new SpecPaths(app.Root);
        var before = PageRun.LoadJson(paths.PageJson(def))!["sections"]!.ToJsonString();
        var hash = RecordedHash(app);

        ShiftFile(app, File1);
        var stale = Staleness.Report(app.Pages(), paths, null);
        Assert.Contains(stale["moved"]!.AsArray(), m => (string)m!["id"]! == SpecApp.Post);
        Assert.DoesNotContain(stale["stale"]!.AsArray(), m => (string)m!["id"]! == SpecApp.Post);
        Assert.False(Staleness.Behind(app.Pages(), paths, app.Pages().Find(SpecApp.Post)!));

        var step = PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo);
        Assert.Equal("current", step.Status);
        Assert.Contains("only moved", step.Warning);
        Assert.NotEqual(hash, RecordedHash(app));
        Assert.Equal(before, PageRun.LoadJson(paths.PageJson(def))!["sections"]!.ToJsonString());
        Assert.Equal("current", PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo).Status);
        Assert.Null(PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo).Warning);
    }

    [Fact]
    public void Changed_code_still_asks_for_a_run()
    {
        using var app = App();
        Write(app);
        var path = Path.Combine(app.Root, File1);
        var lines = File.ReadAllLines(path).ToList();
        var graph = Stangent.Graph.GraphStore.Load(Stangent.Graph.GraphStore.PathFor(app.Repo))!;
        var place = graph.AllNodes.First(n => n.Id == SpecApp.Place);
        lines[place.Line - 1] += " // changed";
        File.WriteAllLines(path, lines);
        Assert.Equal("write", PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo).Status);
    }

    [Fact]
    public void Follow_does_them_all_and_starts_a_shape_for_a_current_page_with_none()
    {
        using var app = App();
        Write(app);
        var file = PageMoves.FileFor(new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!));
        File.Delete(file);
        Assert.Equal(1, (int)PageMoves.Run(app.Pages(), app.Repo.Config, new SpecPaths(app.Root))["shapesStarted"]!);
        Assert.True(File.Exists(file));
        ShiftFile(app, File1);
        var report = PageMoves.Run(app.Pages(), app.Repo.Config, new SpecPaths(app.Root));
        Assert.Equal(SpecApp.Post, (string)report["followed"]![0]!);
    }

    [Fact]
    public void Follow_refuses_off()
    {
        using var app = App(on: false);
        Assert.Equal("component_off", Assert.Throws<StangentException>(() => PageMoves.Run(app.Pages(), app.Repo.Config, new SpecPaths(app.Root))).Code);
    }
}

/// <summary>I.2: only the statements citing changed code go back to the AI; code keeps the rest, word for word.</summary>
public class StatementUpdateTests
{
    const string RuleText = "Orders over 100 are refused with \"Orders over 100 need approval\".";
    const string Receipt = "Placing an order publishes OrderPlaced, which sends a receipt.";

    static SpecApp App(bool on = true) => new($"version: 1\ncomponents: {{ statement-update: {(on ? "true" : "false")} }}\n");

    static JsonObject S(string text, params string[] cites) => new() { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) };

    static JsonObject Output(JsonArray rules, JsonArray effects) => new() { ["title"] = "Place an order", ["shows"] = new JsonArray(), ["actions"] = new JsonArray(), ["rules"] = rules, ["sideEffects"] = effects };

    static void First(SpecApp app)
    {
        var def = app.Pages().Find(SpecApp.Post)!;
        var step = PageRun.Prepare(app.Pages(), def, app.Repo);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), Output(new JsonArray(S(RuleText, SpecApp.Place)), new JsonArray(S(Receipt, SpecApp.Receipt))).ToJsonString());
        step = PageRun.Check(app.Pages(), def, app.Repo, null);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), "{\"findings\":[]}");
        Assert.Equal("recorded", PageRun.Check(app.Pages(), def, app.Repo, null).Status);
    }

    /// <summary>Changes the receipt handler's own line.</summary>
    static void ChangeReceipt(SpecApp app)
    {
        var path = Path.Combine(app.Root, "Core/OrderService.cs");
        var lines = File.ReadAllLines(path).ToList();
        var at = Stangent.Graph.GraphStore.Load(Stangent.Graph.GraphStore.PathFor(app.Repo))!.AllNodes.First(n => n.Id == SpecApp.Receipt).Line;
        lines[at - 1] += " // changed";
        File.WriteAllLines(path, lines);
    }

    static string Work(SpecApp app, RunStep step, string name) => Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, name);

    [Fact]
    public void Off_the_writer_gets_the_whole_page_and_what_it_returns_is_the_page()
    {
        using var app = App(on: false);
        First(app);
        ChangeReceipt(app);
        var step = PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo);
        Assert.DoesNotContain("Update only what changed", File.ReadAllText(Work(app, step, "bundle.md")));
    }

    [Fact]
    public void Only_the_changed_statement_goes_back_and_the_rest_stays_word_for_word_with_its_id()
    {
        using var app = App();
        First(app);
        var def = app.Pages().Find(SpecApp.Post)!;
        var paths = new SpecPaths(app.Root);
        var before = PageChecks.Statements((JsonObject)PageRun.LoadJson(paths.PageJson(def))!).First(s => s.Section == "rules");

        // The whole bundle's hash is what it would be with the switch off.
        ChangeReceipt(app);
        var hash = Bundles.Build(app.Pages(), def, null).InputHash;
        var step = PageRun.Prepare(app.Pages(), def, app.Repo);
        var bundle = SharedHead.Assemble(File.ReadAllText(Work(app, step, "bundle.md")));
        Assert.Contains("Update only what changed", bundle);
        Assert.Contains($"- (rules) {RuleText}", bundle);

        // The writer returns only the touched statement, reworded.
        const string reworded = "An order sends a receipt through OrderPlaced.";
        File.WriteAllText(Path.Combine(app.Root, step.Output!), Output(new JsonArray(), new JsonArray(S(reworded, SpecApp.Receipt))).ToJsonString());
        step = PageRun.Check(app.Pages(), def, app.Repo, null);
        Assert.Equal("verify", step.Status);

        var verify = SharedHead.Assemble(File.ReadAllText(Work(app, step, "verify.md")));
        Assert.Contains(reworded, verify);
        Assert.DoesNotContain(RuleText, verify[verify.IndexOf("# The page", StringComparison.Ordinal)..]);

        File.WriteAllText(Path.Combine(app.Root, step.Output!), "{\"findings\":[]}");
        Assert.Equal("recorded", PageRun.Check(app.Pages(), def, app.Repo, null).Status);
        var page = (JsonObject)PageRun.LoadJson(paths.PageJson(def))!;
        var rule = PageChecks.Statements(page).First(s => s.Section == "rules");
        Assert.Equal(before.Id, rule.Id);
        Assert.Equal(RuleText, (string)rule.Statement["text"]!);
        Assert.Contains(reworded, PageChecks.Statements(page).Select(s => (string)s.Statement["text"]!));
        Assert.Equal(hash, (string)page["generated"]!["bundleHash"]!);
    }

    [Fact]
    public void A_person_marking_it_wrong_or_no_shape_runs_the_whole_page()
    {
        using var app = App();
        First(app);
        var def = app.Pages().Find(SpecApp.Post)!;
        var paths = new SpecPaths(app.Root);
        var recorded = (JsonObject)PageRun.LoadJson(paths.PageJson(def))!;
        Assert.NotNull(StatementUpdate.Plan(app.Pages(), app.Repo.Config, paths, def, recorded, reportedWrong: false));
        Assert.Null(StatementUpdate.Plan(app.Pages(), app.Repo.Config, paths, def, recorded, reportedWrong: true));
        File.Delete(PageMoves.FileFor(paths.PageJson(def)));
        Assert.Null(StatementUpdate.Plan(app.Pages(), app.Repo.Config, paths, def, recorded, reportedWrong: false));
    }

    [Fact]
    public void If_nothing_is_new_no_verifier_runs()
    {
        using var app = App();
        First(app);
        // Unrelated code in the slice changed: every statement is kept, and the writer adds nothing.
        var def = app.Pages().Find(SpecApp.Post)!;
        var path = Path.Combine(app.Root, "Core/OrderService.cs");
        var lines = File.ReadAllLines(path).ToList();
        var create = Stangent.Graph.GraphStore.Load(Stangent.Graph.GraphStore.PathFor(app.Repo))!.AllNodes.First(n => n.Id == SpecApp.Charge);
        lines[create.Line - 1] += " // changed";
        File.WriteAllLines(path, lines);
        var step = PageRun.Prepare(app.Pages(), def, app.Repo);
        if (step.Status == "current") return; // that code isn't in this page's slice, so nothing runs at all
        File.WriteAllText(Path.Combine(app.Root, step.Output!), Output(new JsonArray(), new JsonArray()).ToJsonString());
        Assert.Equal("recorded", PageRun.Check(app.Pages(), def, app.Repo, null).Status);
    }
}

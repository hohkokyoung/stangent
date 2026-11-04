using System.Text.Json.Nodes;
using Stangent.Core.Evidence;
using Stangent.Graph;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>Statement ids stay with their statements, and line citations come from the graph (2.3, D118).</summary>
public class StatementIdTests
{
    const string Rule = "Orders over 100 are refused with \"Orders over 100 need approval\".";
    const string Effect = "Placing an order publishes OrderPlaced, which sends a receipt.";

    static JsonObject S(string text, params string[] cites) => new() { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) };

    static JsonObject Writer(JsonObject[]? shows = null, JsonObject[]? rules = null, JsonObject[]? effects = null) => new()
    {
        ["title"] = "Place an order",
        ["shows"] = new JsonArray([.. (shows ?? []).Select(s => (JsonNode)s)]), ["actions"] = new JsonArray(),
        ["rules"] = new JsonArray([.. (rules ?? []).Select(s => (JsonNode)s)]),
        ["sideEffects"] = new JsonArray([.. (effects ?? []).Select(s => (JsonNode)s)]),
    };

    /// <summary>The whole loop, for a page regenerated because its code changed: the writer's output in, the recorded page out.</summary>
    static JsonObject Regenerate(SpecApp app, JsonObject output, JsonArray? findings = null, JsonObject? fix = null)
    {
        var def = app.Pages().Find(SpecApp.Post)!;
        var file = new SpecPaths(app.Root).PageJson(def);
        // A changed input, so prepare runs. Any change to the slice's source will do.
        if (File.Exists(file)) app.Write("Core/OrderService.cs", File.ReadAllText(Path.Combine(app.Root, "Core/OrderService.cs")).TrimEnd('\n').Replace("public Order Place(decimal total)", "public Order Place(decimal total) /*changed*/"));
        var step = PageRun.Prepare(app.Pages(), def, app.Repo);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), output.ToJsonString());
        step = PageRun.Check(app.Pages(), def, app.Repo, null);
        File.WriteAllText(Path.Combine(app.Root, step.Output!), new JsonObject { ["findings"] = findings?.DeepClone() ?? new JsonArray() }.ToJsonString());
        step = PageRun.Check(app.Pages(), def, app.Repo, null);
        if (fix is not null)
        {
            File.WriteAllText(Path.Combine(app.Root, step.Output!), fix.ToJsonString());
            step = PageRun.Check(app.Pages(), def, app.Repo, null);
        }
        Assert.Equal("recorded", step.Status);
        return (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
    }

    static Dictionary<string, string> Ids(JsonObject page) => PageChecks.Statements(page).ToDictionary(s => (string)s.Statement["text"]!, s => s.Id);

    static PermanentIds LedgerOf(SpecApp app) => StatementIds.Ledger(app.Root, new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!));

    [Fact]
    public void A_first_recording_numbers_statements_as_before()
    {
        using var app = new SpecApp();
        var page = Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place), S("Totals are in dollars only.", SpecApp.Place)], effects: [S(Effect, SpecApp.Place, SpecApp.Receipt)]));
        Assert.Equal(["R1", "R2", "E1"], PageChecks.Statements(page).Select(s => s.Id).OrderByDescending(x => x[0]).ThenBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Ids_stay_across_a_rewrite_with_the_same_text_even_reordered_and_rewrapped()
    {
        using var app = new SpecApp();
        var first = Ids(Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place), S("Totals are in dollars only.", SpecApp.Create)])));
        var again = Regenerate(app, Writer(rules: [S("Totals   are in\ndollars only.", SpecApp.Create), S(Rule, SpecApp.Place)]));
        // The second statement in the file is now R1 in place, but it keeps the id it had.
        Assert.Equal(["R2", "R1"], PageChecks.Statements(again).Select(s => s.Id));
        Assert.Equal(first[Rule], Ids(again)[Rule]);
    }

    [Fact]
    public void Text_moved_to_another_section_keeps_its_id()
    {
        using var app = new SpecApp();
        Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place)]));
        var again = Regenerate(app, Writer(shows: [S(Rule, SpecApp.Place)]));
        Assert.Equal(("shows", "R1"), PageChecks.Statements(again).Select(s => (s.Section, s.Id)).Single());
    }

    [Fact]
    public void A_rephrase_with_the_same_symbols_keeps_its_id_and_a_different_symbol_or_section_does_not()
    {
        var old = new[] { new StatementRef("rules", "R1", "One.", ["a", "b"]) };
        Assert.Equal("R1", StatementIds.Match(old, [new StatementRef("rules", "?", "Two.", ["b", "a", "a"])]).Kept[0]);
        Assert.Empty(StatementIds.Match(old, [new StatementRef("rules", "?", "Two.", ["a"])]).Kept);
        Assert.Empty(StatementIds.Match(old, [new StatementRef("shows", "?", "Two.", ["a", "b"])]).Kept);
    }

    [Fact]
    public void A_rephrase_needs_to_be_alone_on_both_sides()
    {
        var twoOld = new[] { new StatementRef("rules", "R1", "One.", ["a"]), new StatementRef("rules", "R2", "Two.", ["a"]) };
        var match = StatementIds.Match(twoOld, [new StatementRef("rules", "?", "Three.", ["a"])]);
        Assert.Empty(match.Kept);
        Assert.Equal(["R1", "R2"], match.Retired);

        var twoFresh = StatementIds.Match([twoOld[0]], [new StatementRef("rules", "?", "Three.", ["a"]), new StatementRef("rules", "?", "Four.", ["a"])]);
        Assert.Empty(twoFresh.Kept);
        Assert.Equal(["R1"], twoFresh.Retired);

        // What's already matched by its words isn't a rival any more.
        var one = StatementIds.Match(twoOld, [new StatementRef("rules", "?", "One.", ["a"]), new StatementRef("rules", "?", "Four.", ["a"])]);
        Assert.Equal(("R1", "R2"), (one.Kept[0], one.Kept[1]));
        Assert.Equal([1], one.Rephrased);
    }

    [Fact]
    public void A_retired_number_is_never_given_again()
    {
        using var app = new SpecApp();
        Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place), S("Totals are in dollars only.", SpecApp.Create)]));
        var dropped = Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place)]));
        Assert.Equal(["R1"], PageChecks.Statements(dropped).Select(s => s.Id));
        Assert.Equal(["R2"], LedgerOf(app).Removed("R"));

        var grown = Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place), S("Nothing is refused on a Sunday.", SpecApp.Create)]));
        Assert.Equal(["R1", "R3"], PageChecks.Statements(grown).Select(s => s.Id));
    }

    [Fact]
    public void A_page_from_before_keeps_the_ids_it_has_at_adoption()
    {
        using var app = new SpecApp();
        Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place), S("Totals are in dollars only.", SpecApp.Create)]));
        // As if an old build wrote it: ids on the page, no ledger.
        File.Delete(StatementIds.LedgerFile(new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!)));
        var again = Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place), S("Totals are in dollars only.", SpecApp.Create), S("Orders need a customer.", SpecApp.Place, SpecApp.Create)]));
        Assert.Equal(["R1", "R2", "R3"], PageChecks.Statements(again).Select(s => s.Id));
    }

    [Fact]
    public void Code_numbers_a_writers_output_by_place_whatever_it_claims()
    {
        var output = Writer(rules: [S(Rule, SpecApp.Place)]);
        output["rules"]![0]!["id"] = "R77";
        Assert.Equal("R1", PageChecks.Statements(output).Single().Id);
    }

    [Fact]
    public void A_verifiers_finding_is_recorded_on_the_id_its_statement_ended_up_with()
    {
        using var app = new SpecApp();
        Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place)], effects: [S(Effect, SpecApp.Place)]));
        var findings = new JsonArray(
            new JsonObject { ["statement"] = "E1", ["kind"] = "unsupported", ["explanation"] = "No receipt is visible." });
        var page = Regenerate(app, Writer(rules: [S("Totals are in dollars only.", SpecApp.Create), S(Rule, SpecApp.Place)], effects: [S(Effect, SpecApp.Place)]), findings,
            Writer(rules: [S("Totals are in dollars only.", SpecApp.Create), S(Rule, SpecApp.Place)], effects: [S("Placing an order publishes OrderPlaced.", SpecApp.Place)])
                .Also(o => o["resolutions"] = new JsonArray(new JsonObject { ["finding"] = "F1", ["outcome"] = "fixed", ["reason"] = "Reworded." })));
        Assert.Equal("E1", (string)page["verification"]!["findings"]![0]!["statement"]!);

        var removed = Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place)], effects: [S("Placing an order publishes OrderPlaced.", SpecApp.Place)]), findings,
            Writer(rules: [S(Rule, SpecApp.Place)])
                .Also(o => o["resolutions"] = new JsonArray(new JsonObject { ["finding"] = "F1", ["outcome"] = "fixed", ["reason"] = "Gone." })));
        Assert.Equal("(removed in the fix)", (string)removed["verification"]!["findings"]![0]!["statement"]!);
    }

    [Fact]
    public void A_link_to_a_retired_id_drops_back_and_is_reported_never_repointed()
    {
        using var app = new SpecApp();
        Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place)], effects: [S(Effect, SpecApp.Place, SpecApp.Receipt)]));
        var link = Stangent.Spec.Fsd.LinkRun.Entry("agree", "shop", null, null, SpecApp.Post, "E1", Effect, "judge", "confirmed", null);
        link["decision"] = "agree";
        var trace = TraceFile.Empty();
        trace["entries"]!.AsArray().Add((JsonNode)link);
        TraceFile.Save(app.Root, trace);
        var clean = TraceCheck.Run(app.Root, null, null, false);
        Assert.Empty(clean["staleQuotes"]!.AsArray());

        // E1's only statement goes, with a different symbol set taking its place: a new id, E2. The link doesn't follow.
        var page = Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place)], effects: [S("A different thing happens after placing.", SpecApp.Create)]));
        Assert.Equal("E2", PageChecks.Statements(page).Single(s => s.Section == "sideEffects").Id);
        var report = TraceCheck.Run(app.Root, null, null, true);
        var entry = TraceFile.Load(app.Root)["entries"]![0]!;
        Assert.Null(entry["decision"]);
        Assert.Equal("E1", (string)entry["page"]!["statement"]!);
        Assert.Equal((string)link["id"]!, (string)report["staleQuotes"]![0]!);
        Assert.Equal("E1", (string)report["retiredStatements"]![0]!["statement"]!);
    }

    [Fact]
    public void A_moved_statement_keeps_its_links_decision()
    {
        using var app = new SpecApp();
        Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place), S("Totals are in dollars only.", SpecApp.Create)]));
        var link = Stangent.Spec.Fsd.LinkRun.Entry("agree", "shop", null, null, SpecApp.Post, "R2", "Totals are in dollars only.", "judge", "confirmed", null);
        link["decision"] = "agree";
        var trace = TraceFile.Empty();
        trace["entries"]!.AsArray().Add((JsonNode)link);
        TraceFile.Save(app.Root, trace);
        Regenerate(app, Writer(rules: [S("Totals are in dollars only.", SpecApp.Create), S(Rule, SpecApp.Place)]));
        Assert.Empty(TraceCheck.Run(app.Root, null, null, true)["staleQuotes"]!.AsArray());
        Assert.Equal("agree", (string?)TraceFile.Load(app.Root)["entries"]![0]!["decision"]);
    }

    // ---- Line citations ----

    [Fact]
    public void A_clean_graph_gives_each_cited_symbol_its_lines_beside_the_page_and_the_page_shows_them()
    {
        using var app = new SpecApp();
        SavedWorkTests.CleanGraph(app);
        var page = Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place)]));
        Assert.DoesNotContain("Core/OrderService.cs:3", page.ToJsonString());

        var file = new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!);
        var lines = PageLines.Load(file)!;
        Assert.Equal(new string('a', 40), (string?)lines["commit"]);
        Assert.Equal("Core/OrderService.cs:3-8", PageLines.Cite(lines["symbols"]![SpecApp.Place]!.AsObject()));
        var md = File.ReadAllText(file[..^".json".Length] + ".md");
        Assert.Contains("`Core/OrderService.cs:3-8`", md);
        Assert.Contains("Lines are at commit aaaaaaa", md);
    }

    [Fact]
    public void A_dirty_tree_writes_no_citation_at_all_and_the_page_says_why()
    {
        using var app = new SpecApp();
        // Not even the file cited: the tree as a whole wasn't the commit.
        SavedWorkTests.CleanGraph(app, dirty: ["web/src/Other.tsx"]);
        Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place)]));
        var file = new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!);
        var lines = PageLines.Load(file)!;
        Assert.Null(lines["commit"]);
        Assert.Null(PageLines.Cite(lines["symbols"]![SpecApp.Place]!.AsObject()));
        var md = File.ReadAllText(file[..^".json".Length] + ".md");
        Assert.Contains("No lines are cited: the graph's tree was dirty", md);
        Assert.DoesNotContain("OrderService.cs:3", md);
    }

    [Fact]
    public void A_file_the_commit_does_not_hold_gets_no_citation_and_says_why()
    {
        using var app = new SpecApp();
        SavedWorkTests.CleanGraph(app, notInCommit: ["Core/OrderService.cs"]);
        Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place)]));
        var file = new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!);
        var symbol = PageLines.Load(file)!["symbols"]![SpecApp.Place]!.AsObject();
        Assert.Null(PageLines.Cite(symbol));
        Assert.Contains("the commit doesn't contain this file", (string)symbol["why"]!);
        Assert.Contains("no lines: ", File.ReadAllText(file[..^".json".Length] + ".md"));
    }

    [Fact]
    public void A_graph_with_no_commit_gets_no_citation()
    {
        using var app = new SpecApp();
        Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place)]));
        var lines = PageLines.Load(new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!))!;
        Assert.Equal("the graph wasn't built at a git commit", (string?)lines["note"]);
    }

    [Fact]
    public void The_refresh_fills_a_page_from_before_without_touching_it_and_the_lists_pass_over_what_it_writes()
    {
        using var app = new SpecApp();
        Regenerate(app, Writer(rules: [S(Rule, SpecApp.Place)]));
        var file = new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!);
        File.Delete(PageLines.FileFor(file));
        File.Delete(StatementIds.LedgerFile(file));
        var before = File.ReadAllText(file);

        SavedWorkTests.CleanGraph(app);
        var report = PageLines.Refresh(app.Root, GraphStore.Load(GraphStore.PathFor(app.Repo))!, null);
        Assert.Equal((1, 1, 0), ((int)report["pages"]!, (int)report["citations"]!["withLines"]!, (int)report["citations"]!["withoutLines"]!));
        Assert.Equal(before, File.ReadAllText(file));
        Assert.True(File.Exists(PageLines.FileFor(file)));
        Assert.Equal(["R1"], StatementIds.Ledger(app.Root, file).Active("R"));

        Assert.Single(SpecIndex.Load(app.Root).Pages);
        Assert.Empty(Staleness.Report(app.Pages(), new SpecPaths(app.Root), null)["removed"]!.AsArray());
        Assert.Equal("not_found", Assert.Throws<StangentException>(() => PageLines.Refresh(app.Root, GraphStore.Load(GraphStore.PathFor(app.Repo))!, "page:/nope")).Code);
    }

    [Fact]
    public void The_lookup_answers_as_graph_where_does()
    {
        using var app = new SpecApp();
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        Assert.Equal(SymbolLines.Where(graph, SpecApp.Place).Single(), SymbolLines.Lookup(graph)(SpecApp.Place)! with { AlsoIn = SymbolLines.Where(graph, SpecApp.Place).Single().AlsoIn });
        Assert.Null(SymbolLines.Lookup(graph)("nope"));
    }
}

static class JsonObjectExtensions
{
    public static JsonObject Also(this JsonObject o, Action<JsonObject> change) { change(o); return o; }
}

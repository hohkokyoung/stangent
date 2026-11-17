using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Core.Tests;

/// <summary><c>fsd review</c>, <c>review-check</c> and <c>review-merge</c> (D341): the reviewer's input, code's checks with one
/// retry, and the union of the runs. No AI: the tests write what a reviewer would have returned.</summary>
public sealed class FsdReviewTests : IDisposable
{
    const string Outline = """
        % Shop
        # 1 Orders
        Orders ship within two days of payment.
        Each order shows a **Cancel** button until it ships.
        ## 1.1 Order list
        The order list shows 20 orders per page.
        ## 1.2 Help
        Press the **Save**, button on top.
        It’s “quoted” here – with a dash.
        - Open the menu
        - Choose a theme
        # 2 Settings
        | Option | Default |
        |---|---|
        | Currency | US dollars |
        """;

    readonly TempRepo archive = new();
    readonly TempRepo repo;

    public FsdReviewTests()
    {
        repo = new TempRepo($"version: 1\nfsd: {{ archive: {{ name: a, path: '{archive.Root}', git: false }}, scope: [/orders, /settings], reviewBudget: {{ turns: 40 }} }}\n");
        Track("src/orders.ts", string.Join("\n", Enumerable.Range(1, 30).Select(i => $"// order line {i}")) + "\n");
        Track("src/settings/currency.ts", "export const currency = 'USD';\n");
        Git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "code");
        var file = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Outline));
        FsdImport.Import(repo.Repo, file);
        Committed.All(repo.Repo);
    }

    public void Dispose()
    {
        repo.Dispose();
        archive.Dispose();
    }

    string Root => repo.Root;
    StangentRepo Repo => StangentRepo.Discover(Root);

    void Git(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    void Track(string path, string text)
    {
        if (!Directory.Exists(Path.Combine(Root, ".git"))) Git("init", "-q");
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        Git("add", "--", path);
    }

    static JsonObject F(string state, string? section, string? quote, string? screen, string note, params string[] evidence) => new()
    {
        ["state"] = state, ["section"] = section, ["fsdQuote"] = quote, ["screen"] = screen, ["note"] = note,
        ["evidence"] = new JsonArray([.. evidence.Select(e => (JsonNode)e)]),
    };

    void Answer(int run, params JsonObject[] findings) =>
        File.WriteAllText(Path.Combine(FsdReview.RunDir(Root, "shop", run), "output.json"), new JsonObject { ["findings"] = new JsonArray([.. findings]) }.ToJsonString());

    JsonObject Checked(int run) => JsonNode.Parse(File.ReadAllText(Path.Combine(FsdReview.RunDir(Root, "shop", run), "checked.json")))!.AsObject();

    RunStep Run(int run, params JsonObject[] findings)
    {
        FsdReview.Prepare(Repo, "shop", run, again: true);
        Answer(run, findings);
        return FsdReview.Check(Repo, "shop", run, null);
    }

    static string Code(Action a) => Assert.Throws<StangentException>(a).Code;

    static JsonObject Good => F("conflict", "1.1", "The order list shows 20 orders per page.", null, "It shows 50.", "src/orders.ts:12");

    [Fact]
    public void Prepare_gives_the_sections_with_their_ids_the_scope_and_the_schema_and_names_the_reviewer()
    {
        var step = FsdReview.Prepare(Repo, "shop", 1, again: false);
        Assert.Equal(("prepared", FsdReview.Reviewer, FsdReview.OutputSchema), (step.Status, step.Agent, step.Schema));
        var input = File.ReadAllText(Path.Combine(FsdReview.RunDir(Root, "shop", 1), "input.md"));
        Assert.Contains("## 1 Orders  [section id: 1]", input);
        Assert.Contains("### 1.1 Order list  [section id: 1.1]", input);
        Assert.Contains("Orders ship within two days of payment.", input);
        Assert.Contains("`/orders`, `/settings`", input);
        Assert.Contains("urn:stangent:schema:fsd-review-output:v2", input);
        Assert.DoesNotContain("Kavita", input);

        var limits = FsdReview.DriverLimits(Repo, "shop", 1);
        Assert.Equal(40, (int)limits["maxTurns"]!);
        Assert.Equal(3.0, (double)limits["maxBudgetUsd"]!);    // turns set alone keeps the default dollars
        var denied = limits["disallowedTools"]!.AsArray().Select(d => (string)d!).ToList();
        Assert.Contains(denied, d => d.EndsWith("/specs/**)", StringComparison.Ordinal));
        Assert.Contains(denied, d => d.EndsWith("/stangent.yaml)", StringComparison.Ordinal));
        Assert.Contains(denied, d => d.EndsWith("/.stangent/**)", StringComparison.Ordinal));
        Assert.Equal("usage", Code(() => FsdReview.Prepare(Repo, "shop", 3, again: false)));
    }

    [Fact]
    public void A_checked_run_with_the_same_input_and_commit_is_current_unless_asked_again()
    {
        Assert.Equal("recorded", Run(1, Good).Status);
        Assert.Equal("current", FsdReview.Prepare(Repo, "shop", 1, again: false).Status);
        Assert.Equal("prepared", FsdReview.Prepare(Repo, "shop", 1, again: true).Status);
    }

    [Fact]
    public void Good_findings_pass_first_time()
    {
        var step = Run(1,
            Good,
            // Emphasis in the FSD isn't part of the words; a parent section holds its subsections' text.
            F("gap", "1", "Each order shows a Cancel button until it ships.", null, "No Cancel button.", "src/orders.ts:3-5"),
            F("conflict", "1", "The order list shows 20 orders per page.", null, "Same sentence, quoted from the chapter.", "src/orders.ts:30"),
            // A table row's cells, quoted across the bars.
            F("conflict", "2", "Currency US dollars", null, "The default is euros.", "src/settings/currency.ts:1"),
            // A whole screen no section fits; a folder where a missing thing would be.
            F("undocumented", null, null, "/orders/:id/refund", "A refund screen.", "src/settings"));
        Assert.Equal("recorded", step.Status);
        var c = Checked(1);
        Assert.Equal(5, c["findings"]!.AsArray().Count);
        Assert.Empty(c["dropped"]!.AsArray());
        Assert.False((bool)c["retried"]!);
    }

    [Fact]
    public void A_failing_finding_gets_one_retry_with_every_error_then_is_dropped_and_listed()
    {
        Track("src/later.ts", "export const later = 1;\n");    // staged, not in the reviewed commit
        var bad = new[]
        {
            Good,
            F("gap", "9.9", "Orders ship within two days of payment.", null, "No such section.", "src/orders.ts:1"),
            F("gap", "2", "Orders ship within two days of payment.", null, "Quote from another section.", "src/orders.ts:1"),
            F("conflict", "1", "ship within two", null, "A fragment.", "src/orders.ts:1"),
            F("gap", "1", null, null, "No quote.", "src/orders.ts:1"),
            F("undocumented", null, null, null, "Neither section nor screen.", "src/orders.ts:1"),
            F("conflict", "1.1", "The order list shows 20 orders per page.", null, "Line past the end.", "src/orders.ts:31"),
            F("conflict", "1.1", "The order list shows 20 orders per page.", null, "No such file.", "src/missing.ts:1"),
            F("conflict", "1.1", "The order list shows 20 orders per page.", null, "Not committed.", "src/later.ts:1"),
        };
        var first = Run(1, bad);
        Assert.Equal("retry", first.Status);
        var errors = first.Errors!;
        Assert.Contains(errors, e => e.StartsWith("findings[1]: there's no section \"9.9\"", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("findings[2]: the fsdQuote", StringComparison.Ordinal) && e.Contains("section 2"));
        Assert.Contains(errors, e => e.StartsWith("findings[3]: the fsdQuote", StringComparison.Ordinal));
        Assert.Contains(errors, e => e == "findings[4]: a gap quotes the FSD sentence(s) concerned");
        Assert.Contains(errors, e => e.StartsWith("findings[5]: an undocumented finding names a screen or a section", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("findings[6]:", StringComparison.Ordinal) && e.Contains("line 31 is past the end of src/orders.ts (30 lines)"));
        Assert.Contains(errors, e => e.StartsWith("findings[7]:", StringComparison.Ordinal) && e.Contains("no such file src/missing.ts"));
        Assert.Contains(errors, e => e.StartsWith("findings[8]:", StringComparison.Ordinal) && e.Contains("no such file src/later.ts"));
        Assert.DoesNotContain(errors, e => e.StartsWith("findings[0]", StringComparison.Ordinal));
        var dir = FsdReview.RunDir(Root, "shop", 1);
        Assert.True(File.Exists(Path.Combine(dir, "feedback.md")));
        Assert.True(File.Exists(Path.Combine(dir, "previous-output.json")));
        Assert.Contains("previous-output.json", first.Prompt);

        // The retry comes back unchanged: what passed is kept, what failed is dropped with its reasons.
        Answer(1, [.. bad.Select(b => (JsonObject)b.DeepClone())]);
        var second = FsdReview.Check(Repo, "shop", 1, null);
        Assert.Equal("recorded", second.Status);
        var c = Checked(1);
        Assert.Single(c["findings"]!.AsArray());
        Assert.Equal(8, c["dropped"]!.AsArray().Count);
        Assert.True((bool)c["retried"]!);
        Assert.All(c["dropped"]!.AsArray(), d => Assert.NotEmpty(d!["reasons"]!.AsArray()));
    }

    [Fact]
    public void No_answer_asks_once_then_the_run_fails()
    {
        FsdReview.Prepare(Repo, "shop", 1, again: true);
        var first = FsdReview.Check(Repo, "shop", 1, null);
        Assert.Equal("retry", first.Status);
        Assert.StartsWith("You returned no answer", first.Errors![0], StringComparison.Ordinal);
        Assert.Equal("failed", FsdReview.Check(Repo, "shop", 1, Usage.None with { Calls = 1 }).Status);
        Assert.False(File.Exists(Path.Combine(FsdReview.RunDir(Root, "shop", 1), "checked.json")));
        Assert.Equal("prepared", FsdReview.Prepare(Repo, "shop", 1, again: false).Status);    // a failed run starts again
    }

    [Fact]
    public void Merge_joins_what_code_can_show_is_one_difference_and_marks_what_may_be()
    {
        Run(1,
            Good,
            F("undocumented", null, null, "/orders/:id/refund", "A refund screen.", "src/orders.ts:2"),
            F("undocumented", "1.1", null, null, "A sort dropdown.", "src/orders.ts:7"),
            F("undocumented", "1", null, null, "An export button.", "src/orders.ts:8"),
            F("undocumented", "1", null, null, "A print button.", "src/orders.ts:9"));
        Run(2,
            F("conflict", "1.1", "The  order list shows **20** orders per page.", null, "It pages by 50.", "src/orders.ts:13"),
            F("undocumented", null, null, "/orders/:id/refund/", "Refunds have their own screen.", "src/orders.ts:3"),
            // One finding each side in 1.1, a file in common, one route left blank: joined.
            F("undocumented", "1.1", null, "/orders", "The list can be sorted.", "src/orders.ts:20"),
            // Two in section 1 against run 2's one, all in one file: ambiguous, kept apart.
            F("undocumented", "1", null, "/orders", "Export and print buttons.", "src/orders.ts:10"),
            F("gap", "1", "Orders ship within two days of payment.", null, "Only in run 2.", "src/orders.ts:1"));
        var result = FsdReview.Merge(Repo, "shop");
        Assert.Equal(7, (int)result["findings"]!);
        Assert.Equal(3, (int)result["foundByEveryRun"]!);
        Assert.Equal(3, (int)result["maybeSame"]!);

        var review = JsonNode.Parse(File.ReadAllText(FsdReview.ReviewJson(Root, "shop")))!.AsObject();
        Assert.Empty(SchemaRegistry.Embedded.Validate("fsd-review.v2.json", review.ToJsonString()));
        Assert.Equal(("fsd-review", 2), ((string)review["contract"]!, (int)review["version"]!));
        var findings = review["findings"]!.AsArray().OfType<JsonObject>().ToList();
        // Section order, then gap, conflict, undocumented; a screen with no section last.
        Assert.Equal(["gap", "undocumented", "undocumented", "undocumented", "conflict", "undocumented", "undocumented"],
            new[] { findings[0], findings[1], findings[2], findings[3], findings[4], findings[5], findings[6] }.Select(f => (string)f["state"]!));
        Assert.Equal(["1", "1", "1", "1", "1.1", "1.1", null], findings.Select(f => (string?)f["section"]));
        var conflict = findings[4];
        Assert.Equal([1, 2], conflict["runs"]!.AsArray().Select(r => (int)r!));
        Assert.Equal(["src/orders.ts:12", "src/orders.ts:13"], conflict["evidence"]!.AsArray().Select(e => (string)e!));
        Assert.Equal("It shows 50.", (string)conflict["note"]!);    // the first run's words
        Assert.Equal([1, 2], findings[5]["runs"]!.AsArray().Select(r => (int)r!));    // 1.1's unique pair
        Assert.Equal([1, 2], findings[6]["runs"]!.AsArray().Select(r => (int)r!));    // the refund screen
        var ambiguous = findings.Where(f => (string?)f["section"] == "1" && (string)f["state"]! == "undocumented").ToList();
        Assert.Equal(3, ambiguous.Count);
        Assert.All(ambiguous, f => Assert.Single(f["runs"]!.AsArray()));
        Assert.Equal(["F-004"], ambiguous[0]["maybeSameAs"]!.AsArray().Select(m => (string)m!));
        Assert.Equal(["F-002", "F-003"], ambiguous[2]["maybeSameAs"]!.AsArray().Select(m => (string)m!));
        Assert.Equal("S-0001", (string)findings[0]["sectionId"]!);

        var md = File.ReadAllText(FsdReview.ReviewMarkdown(Root, "shop"));
        Assert.StartsWith("<!-- Generated by `stangent fsd review-merge`", md, StringComparison.Ordinal);
        Assert.Contains("## 1 Orders", md);
        Assert.Contains("## Screens no section covers", md);
        Assert.Contains("Maybe the same as **F-002**, **F-003**.", md);
        Assert.Contains("_(found by every run)_", md);
        Assert.Contains("_(found by run 2)_", md);
        Assert.Contains("it did not check that the difference is real", md);
    }

    [Fact]
    public void A_match_quotes_its_statement_and_gives_the_line_that_does_it()
    {
        var bad = new[]
        {
            F("match", "1.1", "The order list shows 20 orders per page.", null, "Pages by 20.", "src/orders.ts:12"),
            F("match", "1", "Orders ship within two days of payment.", null, "A path alone.", "src/orders.ts"),
            F("match", "1", "Orders are shipped the same day.", null, "Not the FSD's words.", "src/orders.ts:2"),
            F("match", "1", null, null, "No quote.", "src/orders.ts:2"),
            // One run calling the same statement a match and a conflict: both go back (D366).
            F("match", "2", "Currency US dollars", null, "Dollars.", "src/settings/currency.ts:1"),
            F("conflict", "2", "Currency  US dollars", null, "Euros.", "src/settings/currency.ts:1"),
        };
        var first = Run(1, bad);
        Assert.Equal("retry", first.Status);
        var errors = first.Errors!;
        Assert.DoesNotContain(errors, e => e.StartsWith("findings[0]", StringComparison.Ordinal));
        Assert.Contains(errors, e => e == "findings[1]: a match gives the line(s) that do it (path:line), not a path alone");
        Assert.Contains(errors, e => e.StartsWith("findings[2]: the fsdQuote", StringComparison.Ordinal));
        Assert.Contains(errors, e => e == "findings[3]: a match quotes the FSD sentence(s) concerned");
        Assert.Contains(errors, e => e.StartsWith("findings[4]: the same statement is also reported as a conflict (findings[5])", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.StartsWith("findings[5]", StringComparison.Ordinal));    // the difference stands (D367)
        Answer(1, [.. bad.Select(b => (JsonObject)b.DeepClone())]);
        FsdReview.Check(Repo, "shop", 1, null);
        Assert.Equal(["match", "conflict"], Checked(1)["findings"]!.AsArray().Select(f => (string)f!["state"]!));
    }

    [Fact]
    public void Merge_joins_matches_by_section_and_quote_and_marks_runs_that_disagree()
    {
        // Run 1 calls 1.1's sentence a match, run 2 a conflict: the runs disagree, and both stay, marked.
        Run(1,
            F("match", "1.1", "The order list shows 20 orders per page.", null, "Pages by 20.", "src/orders.ts:12"),
            F("match", "1", "Orders ship within two days of payment.", null, "Ships in two days.", "src/orders.ts:1"));
        Run(2,
            F("match", "1", "Orders  ship within two days of payment.", null, "Two days.", "src/orders.ts:2"),
            F("conflict", "1.1", "The order list shows 20 orders per page.", null, "It shows 50.", "src/orders.ts:13"));
        var result = FsdReview.Merge(Repo, "shop");
        Assert.Equal(3, (int)result["findings"]!);
        var review = JsonNode.Parse(File.ReadAllText(FsdReview.ReviewJson(Root, "shop")))!.AsObject();
        Assert.Empty(SchemaRegistry.Embedded.Validate("fsd-review.v2.json", review.ToJsonString()));
        Assert.Equal(2, (int)review["counts"]!["match"]!);
        var findings = review["findings"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Equal([("1", "match"), ("1.1", "conflict"), ("1.1", "match")], findings.Select(f => ((string)f["section"]!, (string)f["state"]!)));
        Assert.Equal([1, 2], findings[0]["runs"]!.AsArray().Select(r => (int)r!));
        Assert.Equal(["src/orders.ts:1", "src/orders.ts:2"], findings[0]["evidence"]!.AsArray().Select(e => (string)e!));
        Assert.Equal(["F-003"], findings[1]["disagreesWith"]!.AsArray().Select(m => (string)m!));
        Assert.Equal(["F-002"], findings[2]["disagreesWith"]!.AsArray().Select(m => (string)m!));
        Assert.Null(findings[0]["disagreesWith"]);
        var md = File.ReadAllText(FsdReview.ReviewMarkdown(Root, "shop"));
        Assert.Contains("### Matches (the code does what the FSD says)", md);
        Assert.Contains("2 matches", md);
        Assert.Contains("The runs disagree: see **F-003**.", md);
    }

    [Fact]
    public void Merge_refuses_runs_made_on_different_commits()
    {
        Run(1, Good);
        Track("src/more.ts", "export const more = 1;\n");
        Git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "more");
        Run(2, Good);
        Assert.Equal("runs_differ", Code(() => FsdReview.Merge(Repo, "shop")));
    }

    [Fact]
    public void Quotes_ignore_emphasis_curly_quotes_dashes_and_list_markers_but_not_a_cut_word()
    {
        var step = Run(1,
            F("gap", "1.2", "Press the Save, button on top.", null, "Emphasis dropped before a comma.", "src/orders.ts:1"),
            F("gap", "1.2", "It's \"quoted\" here - with a dash.", null, "Straight quotes and a hyphen.", "src/orders.ts:1"),
            F("gap", "1.2", "Open the menu Choose a theme", null, "Across two list items.", "src/orders.ts:1"),
            F("gap", "1.2", "ress the Save, button on top.", null, "Starts inside a word.", "src/orders.ts:1"));
        Assert.Equal("retry", step.Status);
        Assert.Equal(["findings[3]"], step.Errors!.Select(e => e[..e.IndexOf(':')]).Distinct());
    }

    [Fact]
    public void A_schema_error_names_the_field()
    {
        FsdReview.Prepare(Repo, "shop", 1, again: true);
        Answer(1, F("gap", "1", "Orders ship within two days of payment.", null, "x", "src/orders.ts:1"));
        var step = FsdReview.Check(Repo, "shop", 1, null);
        Assert.Contains(step.Errors!, e => e.Contains("note") && e.Contains("StringTooShort"));
    }

    [Fact]
    public void A_failed_run_reads_as_failed_and_a_check_with_the_retry_still_waiting_is_refused()
    {
        FsdReview.Prepare(Repo, "shop", 1, again: true);
        Assert.Equal("retry", FsdReview.Check(Repo, "shop", 1, null).Status);
        Assert.Equal("no_output", Code(() => FsdReview.Check(Repo, "shop", 1, null)));    // nothing new: not the reviewer's failure
        Assert.Equal("failed", FsdReview.Check(Repo, "shop", 1, Usage.None with { Calls = 1 }).Status);
        Assert.Equal("failed", FsdReview.Check(Repo, "shop", 1, null).Status);
        var status = FsdReview.Status(Repo, "shop");
        Assert.Equal(2, (int)status["runs"]!);
        Assert.Equal(["failed", "not prepared"], status["run"]!.AsArray().Select(r => (string)r!["stage"]!));
        Assert.False((bool)status["merged"]!);
    }
}

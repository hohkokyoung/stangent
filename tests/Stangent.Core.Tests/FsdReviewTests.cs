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
        Help lives at /orders/:id/help.
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
        // D401: the prompt names the FSD file and the answer file for the driver to fill in; the FSD is a file beside it.
        var input = File.ReadAllText(Path.Combine(FsdReview.RunDir(Root, "shop", 1), "input.md"));
        Assert.Contains("- The FSD: `{{FSD_FILE}}`.", input);
        Assert.Contains("Write a JSON file to `{{OUTPUT_FILE}}`", input);
        Assert.Contains("- Scope: the FSD covers the screens whose routes start with these prefixes: `/orders`, `/settings`.", input);
        Assert.Contains("## What to report, three kinds", input);
        Assert.DoesNotContain("{{SCOPE}}", input);
        Assert.DoesNotContain("match", input);
        Assert.DoesNotContain("Kavita", input);
        var fsd = File.ReadAllText(Path.Combine(FsdReview.RunDir(Root, "shop", 1), FsdReview.FsdFile));
        Assert.Contains("## 1 Orders  [section id: 1]", fsd);
        Assert.Contains("### 1.1 Order list  [section id: 1.1]", fsd);
        Assert.Contains("Orders ship within two days of payment.", fsd);
        Assert.Contains($"FSD: .stangent/fsd/shop/review/run-1/{FsdReview.FsdFile}", step.Prompt);

        var limits = FsdReview.DriverLimits(Repo, "shop", 1);
        Assert.Equal("Read,Grep,Glob,Write", (string)limits["tools"]!);
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
        Assert.Empty(SchemaRegistry.Embedded.Validate("fsd-review.v3.json", review.ToJsonString()));
        Assert.Equal(("fsd-review", 3), ((string)review["contract"]!, (int)review["version"]!));
        // No inventory: no checklist, and review.md says so (D403).
        Assert.Null(review["checklist"]);
        Assert.Contains("- No checklist: there was no `specs/inventory.json`", File.ReadAllText(FsdReview.ReviewMarkdown(Root, "shop")));
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
        Assert.Empty(SchemaRegistry.Embedded.Validate("fsd-review.v3.json", review.ToJsonString()));
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

    [Fact]
    public void A_run_prepared_before_the_fsd_became_a_file_is_refused_until_prepared_again()
    {
        FsdReview.Prepare(Repo, "shop", 1, again: true);
        File.Delete(Path.Combine(FsdReview.RunDir(Root, "shop", 1), FsdReview.FsdFile));
        Answer(1);
        Assert.Equal("prepared_before", Code(() => FsdReview.Check(Repo, "shop", 1, null)));
    }
    // ---- the checklist (D403) ------------------------------------------------------------------------------------------

    static JsonObject Line(string kind, string? name, string code, string at) => new()
    {
        ["kind"] = kind, ["name"] = name ?? $"❓ {code}: no visible text found", ["named"] = name is not null, ["code"] = code, ["at"] = at,
    };

    static JsonObject Screen(string route, string title, params JsonObject[] lines) => new()
    {
        ["id"] = $"page:web|{route}", ["title"] = title, ["lines"] = new JsonArray([.. lines]),
    };

    /// <summary>Screens: /orders (four controls), /orders/:id (a placeholder title), /orders/:id/help (its route is in the
    /// FSD), /settings (one field), and /admin out of scope.</summary>
    void Inventory()
    {
        var inventory = new JsonObject
        {
            ["contract"] = "inventory", ["commit"] = "c0ffee",
            ["screens"] = new JsonArray(
                Screen("/settings", "Settings (/settings)", Line("field", "Currency", "currency", "src/settings/currency.ts:1")),
                Screen("/orders", "Orders (/orders)",
                    Line("button", "Cancel", "cancel", "src/orders.ts:3"),                  // one word: the reviewer's
                    Line("menu-entry", "choose a THEME", "theme", "src/orders.ts:4"),       // two words, in 1.2: code's
                    Line("button", "Export orders", "exportOrders", "src/orders.ts:8"),
                    Line("button", null, "toggle", "src/orders.ts:9"),
                    Line("button", "Cancel", "cancel", "src/orders.ts:3")),                 // the same control twice: once
                Screen("/orders/:id", "Loading... (/orders/:id)"),
                Screen("/orders/:id/help", "❓ no visible text found (/orders/:id/help)"),
                Screen("/admin", "Admin Area (/admin)", Line("button", "Delete everything", "nuke", "src/orders.ts:1"))),
        };
        File.WriteAllText(Path.Combine(Root, "specs", "inventory.json"), inventory.ToJsonString());
    }

    static JsonObject A(string id, string answer, string? section = null, string? quote = null, string? note = null, params string[] evidence)
    {
        var a = new JsonObject { ["id"] = id, ["answer"] = answer, ["section"] = section, ["fsdQuote"] = quote, ["note"] = note };
        if (evidence.Length > 0) a["evidence"] = new JsonArray([.. evidence.Select(e => (JsonNode)e)]);
        return a;
    }

    /// <summary>The lister's answer (D404), in the run's list part.</summary>
    void AnswerList(int run, params JsonObject[] checklist) =>
        File.WriteAllText(Path.Combine(FsdReview.ListDir(Root, "shop", run), "output.json"),
            new JsonObject { ["checklist"] = new JsonArray([.. checklist.Select(a => a.DeepClone())]) }.ToJsonString());

    /// <summary>A run with a list part: the review recorded with <see cref="Good"/>, and the lister named next.</summary>
    void Reviewed(int run)
    {
        FsdReview.Prepare(Repo, "shop", run, again: true);
        Answer(run, Good);
        var step = FsdReview.Check(Repo, "shop", run, null);
        Assert.Equal(("recorded", FsdReview.Lister), (step.Status, step.Agent));
    }

    [Fact]
    public void Prepare_lists_the_screens_in_scope_and_their_controls_and_ticks_off_what_the_fsd_names()
    {
        Inventory();
        var step = FsdReview.Prepare(Repo, "shop", 1, again: true);
        // D404: the review first, its prompt D401's unchanged; the list part beside it, for the lister afterwards.
        Assert.Equal(FsdReview.Reviewer, step.Agent);
        Assert.DoesNotContain("Checklist:", step.Prompt);
        Assert.DoesNotContain("checklist", File.ReadAllText(Path.Combine(FsdReview.RunDir(Root, "shop", 1), "input.md")), StringComparison.OrdinalIgnoreCase);
        var dir = FsdReview.ListDir(Root, "shop", 1);
        var input = File.ReadAllText(Path.Combine(dir, "input.md"));
        Assert.Contains("- The checklist: `{{CHECKLIST_FILE}}`.", input);
        Assert.Contains("- The FSD: `{{FSD_FILE}}`.", input);
        Assert.Contains("Write a JSON file to `{{OUTPUT_FILE}}`", input);
        Assert.Contains("- Scope: the FSD covers the screens whose routes start with these prefixes: `/orders`, `/settings`.", input);
        Assert.True(File.Exists(Path.Combine(dir, FsdReview.FsdFile)));
        Answer(1, Good);
        var next = FsdReview.Check(Repo, "shop", 1, null);
        Assert.Equal(("recorded", "review", FsdReview.Lister, FsdReview.ListOutputSchema), (next.Status, next.Action, next.Agent, next.Schema));
        Assert.Contains($"Checklist: .stangent/fsd/shop/review/run-1/list/{FsdReview.ChecklistFile}", next.Prompt);
        Assert.Contains("Write your JSON to: .stangent/fsd/shop/review/run-1/list/output.json", next.Prompt);
        Assert.Equal("checked", (string)FsdReview.Status(Repo, "shop")["run"]![0]!["stage"]!);
        Assert.Equal("prepared", (string)FsdReview.Status(Repo, "shop")["run"]![0]!["list"]!);
        Assert.Equal("not_checked", Code(() => FsdReview.Merge(Repo, "shop")));

        var items = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, FsdReview.ChecklistJson)))!["items"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Equal(["S01", "S01.001", "S01.002", "S01.003", "S01.004", "S02", "S03", "S04", "S04.001"], items.Select(i => (string)i["id"]!));
        Assert.Equal(["/orders", "/orders/:id", "/orders/:id/help", "/settings"], items.Where(i => (string)i["kind"]! == "screen").Select(i => (string)i["screen"]!));
        // Code's ticks: a two-word label in any case and the screen whose route the FSD names; not "Cancel" (one word),
        // not "Currency" (one word, though the table has it), not /orders (only named inside /orders/:id/help).
        Assert.Equal([("S01.002", "1.2"), ("S03", "1.2")], items.Where(i => i["coveredBy"] is not null).Select(i => ((string)i["id"]!, (string)i["coveredBy"]!)));
        Assert.Null((string?)items.Single(i => (string)i["id"]! == "S02")["label"]);    // "Loading..." is no title

        var list = File.ReadAllText(Path.Combine(dir, FsdReview.ChecklistFile));
        Assert.Contains("9 items. 2 of them are not listed here", list);
        Assert.Contains("- S01.001 · button · “Cancel” · `src/orders.ts:3`", list);
        Assert.Contains("- S01.004 · button · (no label; in the code: `toggle`) · `src/orders.ts:9`", list);
        Assert.Contains("- S01 · screen · the whole screen `/orders` (“Orders”)", list);
        Assert.DoesNotContain("S01.002", list);
        Assert.DoesNotContain("help", list);
        Assert.DoesNotContain("/admin", list);
    }

    JsonObject[] Answers => [
        A("S01", "covered", "1", "Orders ship within two days of payment."),
        A("S01.001", "covered", "1", "Each order shows a Cancel button until it ships."),
        A("S01.002", "left-out", note: "Ticked by code: ignored."),
        A("S01.003", "left-out", null, null, "Exports the list as CSV.", "src/orders.ts:8", "src/nowhere.ts:1"),
        A("S01.004", "covered", "1.1", "The order list shows 50 orders per page."),
        A("S02", "left-out", note: "An order's own page."),
        A("S04.001", "covered", "2", "Currency"),
        A("S09", "covered", "1", "Orders ship within two days of payment."),
    ];

    [Fact]
    public void Each_checklist_answer_is_checked_a_failed_quote_counts_as_left_out_and_a_skipped_item_as_not_checked()
    {
        Inventory();
        Reviewed(1);
        AnswerList(1, Answers);
        var step = FsdReview.Check(Repo, "shop", 1, null);
        Assert.Equal(("retry", FsdReview.Lister), (step.Status, step.Agent));
        Assert.Contains(step.Errors!, e => e.StartsWith("checklist: S01.004: the fsdQuote", StringComparison.Ordinal));
        Assert.Contains(step.Errors!, e => e.StartsWith("checklist: S01.003: evidence \"src/nowhere.ts:1\"", StringComparison.Ordinal));
        Assert.Contains(step.Errors!, e => e.StartsWith("checklist: S02: a left-out screen gives the line", StringComparison.Ordinal));
        Assert.Contains(step.Errors!, e => e.StartsWith("checklist: there's no item \"S09\"", StringComparison.Ordinal));
        Assert.Contains("checklist: 1 items not answered: S04", step.Errors!);
        Assert.DoesNotContain(step.Errors!, e => e.Contains("S01.002", StringComparison.Ordinal) || e.StartsWith("checklist: S01:", StringComparison.Ordinal));
        Assert.Contains("every answer included", File.ReadAllText(Path.Combine(FsdReview.ListDir(Root, "shop", 1), "feedback.md")));
        Assert.Contains("Your previous attempt: .stangent/fsd/shop/review/run-1/list/previous-output.json", step.Prompt);

        // The same answer again: settled, never sent back.
        AnswerList(1, Answers);
        Assert.Equal("recorded", FsdReview.Check(Repo, "shop", 1, null).Status);
        var result = Checked(1);
        var outcomes = result["checklist"]!.AsArray().OfType<JsonObject>().ToDictionary(i => (string)i["id"]!, i => (string)i["result"]!);
        Assert.Equal(new Dictionary<string, string>
        {
            ["S01"] = "covered", ["S01.001"] = "covered", ["S01.002"] = "code", ["S01.003"] = "left-out", ["S01.004"] = "left-out",
            ["S02"] = "left-out", ["S03"] = "code", ["S04"] = "not-checked", ["S04.001"] = "covered",
        }, outcomes);
        var leftOut = result["findings"]!.AsArray().OfType<JsonObject>().Where(f => f["item"] is not null).ToList();
        Assert.Equal(["S01.003", "S01.004", "S02"], leftOut.Select(f => (string)f["item"]!));
        Assert.All(leftOut, f => Assert.Equal(("undocumented", (string?)null), ((string)f["state"]!, (string?)f["fsdQuote"])));
        // The item's own line, and the agent's evidence that passes; a screen with none has none.
        Assert.Equal(["src/orders.ts:8"], leftOut[0]["evidence"]!.AsArray().Select(e => (string)e!));
        Assert.Equal("The FSD doesn't mention the button “Export orders” on /orders. Exports the list as CSV.", (string)leftOut[0]["note"]!);
        Assert.Equal("The FSD doesn't mention the button with no label (`toggle` in the code) on /orders.", (string)leftOut[1]["note"]!);
        Assert.Equal(("/orders/:id", 0), ((string)leftOut[2]["screen"]!, leftOut[2]["evidence"]!.AsArray().Count));
        Assert.NotNull(result["checklist"]!.AsArray().OfType<JsonObject>().Single(i => (string)i["id"]! == "S01.004")["why"]);
        Assert.Contains(result["findings"]!.AsArray().OfType<JsonObject>(), f => (string)f["state"]! == "conflict");    // the review's, kept
        Assert.Equal("current", FsdReview.Check(Repo, "shop", 1, null).Status);
        Assert.Equal("current", FsdReview.Prepare(Repo, "shop", 1, again: false).Status);
    }

    [Fact]
    public void A_lister_with_no_answer_after_its_retry_leaves_every_item_not_checked_and_the_run_recorded()
    {
        Inventory();
        Reviewed(1);
        Assert.Equal("retry", FsdReview.Check(Repo, "shop", 1, Usage.None with { Calls = 1 }).Status);
        Assert.Equal("no_output", Code(() => FsdReview.Check(Repo, "shop", 1, null)));
        var step = FsdReview.Check(Repo, "shop", 1, Usage.None with { Calls = 1 });
        Assert.Equal("recorded", step.Status);
        var results = Checked(1)["checklist"]!.AsArray().OfType<JsonObject>().Select(i => (string)i["result"]!).ToList();
        Assert.Equal(7, results.Count(r => r == "not-checked"));
        Assert.Equal(2, results.Count(r => r == "code"));
        Assert.Single(Checked(1)["findings"]!.AsArray());
    }

    void Settle(int run, params JsonObject[] checklist)
    {
        Reviewed(run);
        AnswerList(run, checklist);
        var step = FsdReview.Check(Repo, "shop", run, null);
        if (step.Status == "retry")
        {
            AnswerList(run, checklist);
            step = FsdReview.Check(Repo, "shop", run, null);
        }
        Assert.Equal("recorded", step.Status);
    }

    [Fact]
    public void Merge_unions_the_checklists_lists_left_out_by_screen_and_counts_coverage()
    {
        Inventory();
        // S04 is skipped by both runs: one retry each, then not checked.
        Settle(1, A("S01", "covered", "1", "Orders ship within two days of payment."), A("S01.001", "covered", "1", "Each order shows a Cancel button until it ships."),
            A("S01.003", "left-out", null, null, "Exports the list as CSV.", "src/orders.ts:8"), A("S01.004", "left-out", note: "Shows or hides the filters."),
            A("S02", "left-out", "1", null, null, "src/orders.ts:20"), A("S04.001", "covered", "2", "Currency"));
        Settle(2, A("S01", "covered", "1", "Orders ship within two days of payment."), A("S01.001", "left-out"),
            A("S01.003", "covered", "1", "Orders ship within two days of payment."), A("S01.004", "left-out"),
            A("S02", "covered", "1", "Orders ship within two days of payment."), A("S04.001", "covered", "2", "Currency"));

        var result = FsdReview.Merge(Repo, "shop");
        Assert.Equal(4, (int)result["counts"]!["undocumented"]!);     // S01.001, S01.003, S01.004 (both runs), S02
        Assert.Equal((9, 2, 2, 4, 1), ((int)result["checklist"]!["listed"]!, (int)result["checklist"]!["coveredByCode"]!,
            (int)result["checklist"]!["coveredByAgent"]!, (int)result["checklist"]!["leftOut"]!, (int)result["checklist"]!["notChecked"]!));
        var review = JsonNode.Parse(File.ReadAllText(FsdReview.ReviewJson(Root, "shop")))!.AsObject();
        Assert.Empty(SchemaRegistry.Embedded.Validate("fsd-review.v3.json", review.ToJsonString()));
        var items = review["checklist"]!["items"]!.AsArray().OfType<JsonObject>().ToDictionary(i => (string)i["id"]!);
        Assert.Equal(["left-out", "covered"], items["S01.003"]["byRun"]!.AsArray().Select(x => (string)x!));
        Assert.Equal("left-out", (string)items["S01.003"]["result"]!);
        Assert.Equal(["covered", "left-out"], items["S01.001"]["byRun"]!.AsArray().Select(x => (string)x!));
        Assert.Equal(["not-checked", "not-checked"], items["S04"]["byRun"]!.AsArray().Select(x => (string)x!));
        var findings = review["findings"]!.AsArray().OfType<JsonObject>().ToDictionary(f => (string)f["id"]!);
        var toggle = findings[(string)items["S01.004"]["finding"]!];
        Assert.Equal([1, 2], toggle["runs"]!.AsArray().Select(r => (int)r!));
        Assert.Equal("1", (string?)findings[(string)items["S02"]["finding"]!]["section"]);
        // D405: an item every run covered with a checked quote is a match for the PM to confirm, joined across runs; one a
        // run left out (S01.003) is not.
        var currency = findings[(string)items["S04.001"]["finding"]!];
        Assert.Equal(("match", "2", "Currency", "S04.001"), ((string)currency["state"]!, (string?)currency["section"], (string?)currency["fsdQuote"], (string?)currency["item"]));
        Assert.Equal([1, 2], currency["runs"]!.AsArray().Select(r => (int)r!));
        Assert.Equal([(string)items["S04.001"]["at"]!], currency["evidence"]!.AsArray().Select(e => (string)e!));
        Assert.Equal("undocumented", (string)findings[(string)items["S01.003"]["finding"]!]["state"]!);
        Assert.All(findings.Values.Where(f => (string)f["state"]! == "match"), f => Assert.Contains((string)f["item"]!, new[] { "S01", "S04.001" }));

        var md = File.ReadAllText(FsdReview.ReviewMarkdown(Root, "shop"));
        Assert.Contains("- Checklist (D403): 9 screens and controls in scope listed by code; 2 covered by code (their name is in the FSD word for word), 2 covered by the AI (its FSD quote checked), 4 left out, 1 not checked; the inventory was built at `c0ffee`, not the reviewed commit", md);
        Assert.Contains("## Left out, by screen (the checklist)", md);
        Assert.Contains("### `/orders`", md);
        Assert.Contains($"- **{(string)toggle["id"]!}** button with no label (`toggle`) `src/orders.ts:9` _(every run)_ — Shows or hides the filters.", md);
        Assert.Contains("_(run 1; run 2: covered)_", md);
        Assert.Contains("## Not checked (the checklist)", md);
        Assert.Contains("- `/settings`: S04 the whole screen “Settings”", md);
        // A checklist finding is listed once, under its screen, not again by section; its matches are listed by section.
        Assert.DoesNotContain("Left out (the code has it, the FSD doesn't say)", md);
        Assert.Contains("Matches (the code does what the FSD says)", md);
    }

    [Fact]
    public void A_run_prepared_without_a_checklist_is_checked_without_one()
    {
        Assert.Equal("recorded", Run(1, Good).Status);
        Assert.Null(Checked(1)["checklist"]);
    }
    [Fact]
    public void Merge_refuses_runs_where_only_some_had_a_checklist()
    {
        Assert.Equal("recorded", Run(1, Good).Status);                  // no inventory yet: no checklist
        Inventory();
        Settle(2, A("S01", "covered", "1", "Orders ship within two days of payment."));
        Assert.Equal("runs_differ", Code(() => FsdReview.Merge(Repo, "shop")));
    }

    [Fact]
    public void Two_apps_with_one_route_keep_both_screens_controls_and_the_root_route_is_never_ticked_by_a_slash()
    {
        var inventory = new JsonObject
        {
            ["screens"] = new JsonArray(
                new JsonObject { ["id"] = "page:web|/orders", ["title"] = "Orders", ["lines"] = new JsonArray(Line("button", "Cancel", "cancel", "src/orders.ts:3")) },
                new JsonObject { ["id"] = "page:admin|/orders/", ["title"] = "Orders", ["lines"] = new JsonArray(Line("button", "Refund", "refund", "src/orders.ts:5")) },
                new JsonObject { ["id"] = "page:web|/", ["title"] = "Home", ["lines"] = new JsonArray() }),
        };
        File.WriteAllText(Path.Combine(Root, "specs", "inventory.json"), inventory.ToJsonString());
        var checklist = FsdReview.BuildChecklist(Root, ["/"], FsdOutline.Load(Root, "shop"))!;
        var items = checklist["items"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Equal(["S01", "S02", "S02.001", "S02.002"], items.Select(i => (string)i["id"]!));
        Assert.Equal(["Cancel", "Refund"], items.Where(i => (string)i["screen"]! == "/orders").Skip(1).Select(i => (string)i["label"]!));
        // Section 1.2 holds "/orders/:id/help." and a list; "/" stays the reviewer's.
        Assert.Null(items[0]["coveredBy"]);
    }

    [Fact]
    public void A_checklist_that_breaks_the_schema_goes_to_the_retry_and_never_costs_the_findings()
    {
        Inventory();
        Reviewed(1);
        var odd = new JsonObject { ["id"] = 7, ["answer"] = "covered" };
        var many = A("S01.003", "left-out", null, null, null, [.. Enumerable.Range(1, 13).Select(i => $"src/orders.ts:{i}")]);
        AnswerList(1, odd, many, A("S01", "covered", "1", "Orders ship within two days of payment."));
        var step = FsdReview.Check(Repo, "shop", 1, null);
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.StartsWith("Schema:", StringComparison.Ordinal));
        AnswerList(1, odd, many, A("S01", "covered", "1", "Orders ship within two days of payment."));
        Assert.Equal("recorded", FsdReview.Check(Repo, "shop", 1, null).Status);
        var result = Checked(1);
        Assert.Contains(result["findings"]!.AsArray().OfType<JsonObject>(), f => (string)f["state"]! == "conflict");
        var results = result["checklist"]!.AsArray().OfType<JsonObject>().ToDictionary(i => (string)i["id"]!, i => (string)i["result"]!);
        Assert.Equal(("covered", "left-out", "not-checked"), (results["S01"], results["S01.003"], results["S01.001"]));
    }
    [Fact]
    public void Preparing_again_after_the_review_was_recorded_carries_on_with_the_lister_and_keeps_the_review()
    {
        Assert.Equal(1, (int)FsdReview.Status(Repo, "shop")["callsPerRun"]!);
        Inventory();
        Assert.Equal(2, (int)FsdReview.Status(Repo, "shop")["callsPerRun"]!);
        Reviewed(1);
        var step = FsdReview.Prepare(Repo, "shop", 1, again: false);
        Assert.Equal(("recorded", FsdReview.Lister), (step.Status, step.Agent));
        Assert.True(File.Exists(Path.Combine(FsdReview.RunDir(Root, "shop", 1), "checked.json")));
        AnswerList(1, A("S09", "covered"));
        Assert.Equal("retry", FsdReview.Check(Repo, "shop", 1, null).Status);
        step = FsdReview.Prepare(Repo, "shop", 1, again: false);
        Assert.Equal(FsdReview.Lister, step.Agent);
        Assert.Contains("Your previous attempt: .stangent/fsd/shop/review/run-1/list/previous-output.json", step.Prompt);
        // --again starts both parts over.
        Assert.Equal(FsdReview.Reviewer, FsdReview.Prepare(Repo, "shop", 1, again: true).Agent);
    }
}

using System.Text.Json.Nodes;
using Stangent.Core.Evidence;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>Findings carry a permanent id, evidence and a severity, all by code or a person, never the AI (2.4, D118).</summary>
public class TraceFindingTests
{
    const string Fsd50 = "Orders over 50 need approval.";
    const string Page100 = "Orders over 100 are refused";

    static (SpecApp App, string Verifier) App() { var app = SavedWorkTests.App(out var v); return (app, v); }

    static JsonObject Conflict() => LinkRun.Entry("conflict", "shop", SpecIndexSection(), Fsd50, SpecApp.Post, "R1", Page100, "judge", null, "FSD 50, code 100");

    static JsonObject SpecIndexSection() => new() { ["id"] = "1", ["title"] = "Orders", ["permanentId"] = "S-0001" };

    static void Save(SpecApp app, params JsonObject[] entries)
    {
        var trace = TraceFile.Empty();
        foreach (var e in entries) trace["entries"]!.AsArray().Add((JsonNode)e);
        TraceFile.Save(app.Root, trace);
    }

    static JsonObject Entry(SpecApp app, int i = 0) => TraceFile.Load(app.Root)["entries"]![i]!.AsObject();

    static void Change(SpecApp app, Action<JsonObject> change)
    {
        var trace = TraceFile.Load(app.Root);
        change(trace["entries"]![0]!.AsObject());
        TraceFile.Save(app.Root, trace);
    }

    static JsonObject Check(SpecApp app, CodeGraph? graph = null, JsonObject? baseTrace = null, bool fix = false) => TraceCheck.Run(app.Root, graph, baseTrace, fix);

    static IEnumerable<string> Problems(JsonObject report) => report["problems"]!.AsArray().Select(p => (string)p!);

    [Fact]
    public void Trace_check_issues_a_permanent_id_a_starting_severity_and_valid_evidence()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict(), LinkRun.Entry("agree", "shop", SpecIndexSection(), Fsd50, SpecApp.Post, "E1", "which sends a receipt", "judge", "confirmed", null));
        var report = Check(app);
        Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report)));

        var entries = TraceFile.Load(app.Root)["entries"]!.AsArray().OfType<JsonObject>().ToList();
        var finding = entries.Single(e => (string)e["state"]! == "conflict");
        var agree = entries.Single(e => (string)e["state"]! == "agree");
        Assert.Equal("X-0001", (string)finding["finding"]!);
        Assert.Equal(Severity.Ungraded, (string)finding["severity"]!);
        Assert.All(TraceFile.FindingFields, f => Assert.Null(agree[f]));

        var evidence = finding["evidence"]!.AsArray().OfType<JsonObject>().ToDictionary(r => (string)r["id"]!);
        Assert.Equal(("document", "stated", Fsd50), ((string)evidence["X-0001.fsd"]["citation"]!["kind"]!, (string)evidence["X-0001.fsd"]["strength"]!, (string)evidence["X-0001.fsd"]["citation"]!["quote"]!));
        Assert.Equal(("page", "read", "R1"), ((string)evidence["X-0001.page"]["citation"]!["kind"]!, (string)evidence["X-0001.page"]["strength"]!, (string)evidence["X-0001.page"]["citation"]!["symbol"]!));
        Assert.Equal(64, ((string)evidence["X-0001.fsd"]["contentHash"]!).Length);
        Assert.All(evidence.Values, r => Assert.Empty(EvidenceValidator.Validate(r.ToJsonString())));
        Assert.Contains(evidence["X-0001.fsd"]["checkedBy"]!.AsArray(), c => (string)c!["kind"]! == "code" && (string)c["name"]! == "stangent trace check");
        Assert.Contains(evidence["X-0001.fsd"]["checkedBy"]!.AsArray(), c => (string)c!["kind"]! == "agent");

        Assert.Equal("X-0001", (string)report["findings"]![0]!["id"]!);
        Assert.Equal("ungraded", (string)report["findings"]![0]!["severity"]!);
        Assert.Equal(1, (int)report["counts"]!["bySeverity"]!["ungraded"]!);
    }

    [Fact]
    public void Evidence_cites_code_at_its_commit_when_the_page_has_line_citations_and_ran_never_appears()
    {
        var (app, _) = App();
        using var _d = app;
        SavedWorkTests.CleanGraph(app);
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        PageLines.Refresh(app.Root, graph, null);
        Save(app, Conflict());
        Assert.True((bool)Check(app, graph)["ok"]!);
        var code = Entry(app)["evidence"]!.AsArray().OfType<JsonObject>().Single(r => (string)r["citation"]!["kind"]! == "code");
        Assert.EndsWith("/Core/OrderService.cs", (string)code["citation"]!["ref"]!);
        Assert.Equal((new string('a', 40), 3, 8, "read"), ((string)code["commit"]!, (int)code["citation"]!["lines"]!["start"]!, (int)code["citation"]!["lines"]!["end"]!, (string)code["strength"]!));
        Assert.DoesNotContain(Entry(app)["evidence"]!.AsArray(), r => (string)r!["strength"]! == "ran");
    }

    [Fact]
    public void A_second_check_changes_no_bytes_and_ids_survive_a_rerun()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict());
        Check(app);
        var once = File.ReadAllText(TraceFile.PathIn(app.Root));
        Check(app);
        Assert.Equal(once, File.ReadAllText(TraceFile.PathIn(app.Root)));
        // A section re-judged with the same link: the same entry id, so the same finding.
        LinkRun.ApplyEntries(app.Root, "shop", "1", [Conflict()]);
        Assert.Equal("X-0001", (string)Entry(app)["finding"]!);
        Assert.NotEmpty(Entry(app)["evidence"]!.AsArray());
    }

    [Fact]
    public void A_gone_finding_is_marked_removed_and_its_number_never_comes_back()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict());
        Check(app);
        LinkRun.ApplyEntries(app.Root, "shop", "1", [LinkRun.Entry("gap", "shop", SpecIndexSection(), Fsd50, null, null, null, "judge", null, "n")]);
        Check(app);
        Assert.Equal("X-0002", (string)Entry(app)["finding"]!);
        Assert.Equal(["X-0001"], new PermanentIds(TraceFindings.LedgerFile(app.Root), Path.Combine(app.Root, ".stangent", "fsd", "trace-ids.lock")).Removed("X"));
    }

    [Fact]
    public void A_deleted_ledger_catches_up_with_the_ids_in_the_file()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict());
        Check(app);
        File.Delete(TraceFindings.LedgerFile(app.Root));
        LinkRun.ApplyEntries(app.Root, "shop", "1", [Conflict(), LinkRun.Entry("gap", "shop", SpecIndexSection(), "Every order sends a receipt.", null, null, null, "judge", null, "n")]);
        Check(app);
        var ids = TraceFile.Load(app.Root)["entries"]!.AsArray().Select(e => (string)e!["finding"]!).Order().ToList();
        Assert.Equal(["X-0001", "X-0002"], ids);
    }

    [Theory]
    [InlineData("code-unknown-symbol")]
    [InlineData("wrong-quote")]
    [InlineData("bad-lines")]
    [InlineData("no-commit")]
    [InlineData("ran-by-agent")]
    public void A_finding_with_a_bad_citation_is_listed(string how)
    {
        var (app, _) = App();
        using var _d = app;
        SavedWorkTests.CleanGraph(app);
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        Save(app, Conflict());
        Check(app, graph);
        Change(app, e =>
        {
            var evidence = e["evidence"]!.AsArray();
            var code = new JsonObject
            {
                ["id"] = "X-0001.code.1", ["citation"] = new JsonObject { ["kind"] = "code", ["ref"] = "shop/Core/OrderService.cs", ["symbol"] = SpecApp.Place, ["lines"] = new JsonObject { ["start"] = 3, ["end"] = 8 } },
                ["commit"] = new string('a', 40), ["strength"] = "read", ["checkedBy"] = new JsonArray(new JsonObject { ["kind"] = "code", ["name"] = "stangent trace check" }),
            };
            switch (how)
            {
                case "code-unknown-symbol": code["citation"]!["symbol"] = "Nope"; break;
                case "wrong-quote": evidence[0]!["citation"]!["quote"] = "Orders over 75 need approval."; break;
                case "bad-lines": code["citation"]!["lines"]!["start"] = 9; break;
                case "no-commit": code.Remove("commit"); break;
                case "ran-by-agent": code["strength"] = "ran"; code["checkedBy"] = new JsonArray(new JsonObject { ["kind"] = "agent", ["name"] = "m" }); break;
            }
            evidence.Add((JsonNode)code);
        });
        var report = Check(app, graph);
        Assert.False((bool)report["ok"]!);
        Assert.Contains(Problems(report), p => p.StartsWith("X-0001: evidence", StringComparison.Ordinal));
    }

    [Fact]
    public void Claiming_a_real_run_without_a_test_citation_is_listed()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict());
        Check(app);
        Change(app, e => e["evidence"]![1]!["strength"] = "ran");
        Assert.Contains(Problems(Check(app)), p => p.Contains("a page citation's strength is read, not ran"));
    }

    [Theory]
    [InlineData("X-0007", null)]
    [InlineData("specs/trace.yaml", null)]
    [InlineData(SpecApp.Post, "X-0001")]
    public void Evidence_citing_a_finding_is_rejected(string cited, string? symbol)
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict());
        Check(app);
        Change(app, e =>
        {
            var record = e["evidence"]![1]!;
            record["citation"]!["ref"] = cited;
            if (symbol is not null) record["citation"]!["symbol"] = symbol;
        });
        Assert.Contains(Problems(Check(app)), p => p.Contains("it cites a finding"));
    }

    [Fact]
    public void A_findings_text_is_never_read_as_a_page_statement()
    {
        var (app, verifier) = App();
        using var _d = app;
        var entry = Conflict();
        entry["note"] = "The opinion nobody should read as behaviour.";
        Save(app, entry);
        Check(app);
        var index = SpecIndex.Load(app.Root);
        Assert.Null(index.Statement(SpecApp.Post, "X-0001"));
        Assert.DoesNotContain(PageChecks.Statements(index.Pages[SpecApp.Post]), s => ((string)s.Statement["text"]!).Contains("opinion", StringComparison.Ordinal));
        // A writer can't cite one either: the code graph has no such symbol.
        Assert.Contains("X-0001", string.Join(' ', PageChecks.CheckWriter(
            new JsonObject
            {
                ["title"] = "t", ["shows"] = new JsonArray(new JsonObject { ["text"] = "A statement about it.", ["cites"] = new JsonArray("X-0001") }),
                ["actions"] = new JsonArray(), ["rules"] = new JsonArray(), ["sideEffects"] = new JsonArray(),
            },
            app.Pages().Slices[SpecApp.Post], app.Pages().Builder, Bundles.Build(app.Pages(), app.Pages().Find(SpecApp.Post)!, null))));
    }

    [Fact]
    public void No_finding_id_severity_or_note_reaches_a_prompt_or_a_hash()
    {
        var (app, verifier) = App();
        using var _d = app;
        var entry = Conflict();
        Save(app, entry);
        Check(app);
        Change(app, e => e["severity"] = Severity.Blocker);

        Assert.Equal(string.Join("\n", SavedWorkTests.Saved), string.Join("\n", SavedWorkTests.Hashes(app, verifier)));
        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        var folder = Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!);
        foreach (var file in Directory.EnumerateFiles(folder, "*.md"))
        {
            var bundle = File.ReadAllText(file);
            Assert.DoesNotContain("X-0001", bundle);
            Assert.DoesNotContain(Severity.Blocker, bundle);
            Assert.DoesNotContain("FSD 50, code 100", bundle);
        }
        Assert.Equal((string)entry["id"]!, (string)Entry(app)["id"]!);
    }

    // ---- Evidence is code's own ----

    static List<JsonObject> Records(SpecApp app) => [.. Entry(app)["evidence"]!.AsArray().OfType<JsonObject>()];

    const string Rule = "Orders over 100 are refused with \"Orders over 100 need approval\".";

    static void Record(SpecApp app, params string[] cites) => StatementIdTests.Regenerate(app, StatementIdTests.Writer(rules: [StatementIdTests.S(Rule, cites)]));

    [Fact]
    public void Evidence_follows_a_page_recorded_again_and_keeps_its_person_and_a_second_check_changes_nothing()
    {
        var (app, _) = App();
        using var _d = app;
        SavedWorkTests.CleanGraph(app);
        PageLines.Refresh(app.Root, GraphStore.Load(GraphStore.PathFor(app.Repo))!, null);
        Save(app, Conflict());
        Check(app);
        Change(app, e => { e["decision"] = "code-wrong"; e["decided_by"] = "bob"; });
        var baseTrace = TraceFile.Load(app.Root);
        Assert.True((bool)Check(app, null, baseTrace)["ok"]!);
        Assert.Equal(SpecApp.Place, (string)Records(app).Single(r => (string)r["id"]! == "X-0001.code.1")["citation"]!["symbol"]!);

        // Page R1 is the same words, cited from another symbol, at another commit.
        SavedWorkTests.CleanGraph(app, commit: 'b');
        Record(app, SpecApp.Create);
        foreach (var against in new[] { null, baseTrace })
        {
            var report = Check(app, GraphStore.Load(GraphStore.PathFor(app.Repo))!, against);
            Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report)));
        }

        var code = Records(app).Single(r => (string)r["citation"]!["kind"]! == "code");
        Assert.Equal(("X-0001.code.1", SpecApp.Create, new string('b', 40)), ((string)code["id"]!, (string)code["citation"]!["symbol"]!, (string)code["commit"]!));
        Assert.DoesNotContain(SpecApp.Place, Entry(app)["evidence"]!.ToJsonString());
        Assert.All(Records(app), r => Assert.Contains(r["checkedBy"]!.AsArray(), c => (string)c!["kind"]! == "person" && (string)c["name"]! == "bob"));
        var once = File.ReadAllText(TraceFile.PathIn(app.Root));
        Check(app, GraphStore.Load(GraphStore.PathFor(app.Repo))!);
        Assert.Equal(once, File.ReadAllText(TraceFile.PathIn(app.Root)));
    }

    static JsonObject TypedTest() => new()
    {
        ["id"] = "X-0001.test.1", ["citation"] = new JsonObject { ["kind"] = "test", ["ref"] = "shop/Tests/Nope.cs", ["symbol"] = "Nope" },
        ["commit"] = new string('a', 40), ["strength"] = "ran", ["checkedBy"] = new JsonArray(new JsonObject { ["kind"] = "code", ["name"] = "typed by hand" }),
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Typed_evidence_is_refused_by_name_and_fix_does_not_launder_it(bool fix)
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict());
        Check(app);
        Change(app, e => e["evidence"]!.AsArray().Add((JsonNode)TypedTest()));
        var report = Check(app, fix: fix);
        Assert.False((bool)report["ok"]!);
        Assert.Contains(Problems(report), p => p.StartsWith("X-0001: evidence X-0001.test.1: it isn't what code derives", StringComparison.Ordinal));
        Assert.Contains(Records(app), r => (string)r["id"]! == "X-0001.test.1");

        // The way out: delete it. Code writes what's true.
        Change(app, e => { var ev = e["evidence"]!.AsArray(); ev.Remove(ev.Single(r => (string)r!["id"]! == "X-0001.test.1")); });
        Assert.True((bool)Check(app, fix: fix)["ok"]!);
        Assert.DoesNotContain(Records(app), r => (string)r["id"]! == "X-0001.test.1");
    }

    [Fact]
    public void A_value_typed_over_a_real_record_is_refused_and_a_person_typed_on_it_too()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict());
        Check(app);
        Change(app, e => e["evidence"]![0]!["checkedBy"]!.AsArray().Add((JsonNode)new JsonObject { ["kind"] = "person", ["name"] = "nobody-asked" }));
        // A person only comes from decided_by, so this one drops on the record, and that record doesn't equal what code derives.
        Assert.Contains(Problems(Check(app)), p => p.StartsWith("X-0001: evidence X-0001.fsd:", StringComparison.Ordinal));
    }

    [Fact]
    public void Typed_evidence_with_its_hash_worked_out_is_still_refused_against_the_base()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict());
        Check(app);
        var baseTrace = TraceFile.Load(app.Root);
        Change(app, e =>
        {
            e["evidence"]!.AsArray().Add((JsonNode)TypedTest());
            e["evidence_hash"] = TraceFile.EvidenceHash(e["evidence"]!.AsArray());
        });
        Assert.Contains(Problems(Check(app, null, baseTrace)), p => p.Contains("evidence X-0001.test.1: it isn't what code derives"));
    }

    [Fact]
    public void A_partial_types_note_goes_on_the_evidence_and_it_stays_valid()
    {
        var (app, _) = App();
        using var _d = app;
        SavedWorkTests.CleanGraph(app);
        const string Partial = "a partial type: these lines are its first part, and it has members outside them";
        var file = new SpecPaths(app.Root).PageJson(app.Pages().Find(SpecApp.Post)!);
        var graph = GraphStore.Load(GraphStore.PathFor(app.Repo))!;
        PageLines.Write(file, PageLines.Build(SpecIndex.Load(app.Root).Pages[SpecApp.Post], graph,
            id => new SymbolLines(id, "type", "OrderService", "Core/OrderService.cs", 1, 5, new string('a', 40), true, false, ["Core/OrderService.B.cs"], Partial)));
        Save(app, Conflict());
        var report = Check(app);
        Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report)));
        var code = Records(app).Single(r => (string)r["citation"]!["kind"]! == "code");
        Assert.Equal((Partial, 1, 5), ((string)code["citation"]!["note"]!, (int)code["citation"]!["lines"]!["start"]!, (int)code["citation"]!["lines"]!["end"]!));
        Assert.Empty(EvidenceValidator.Validate(code.ToJsonString()));
    }

    [Fact]
    public void A_dirty_file_gets_no_code_evidence_and_a_clean_file_keeps_its_own()
    {
        var (app, _) = App();
        using var _d = app;
        SavedWorkTests.CleanGraph(app, dirty: ["Core/OrderService.cs"]);
        Record(app, SpecApp.Place, SpecApp.Create);
        Save(app, Conflict());
        var report = Check(app, GraphStore.Load(GraphStore.PathFor(app.Repo))!);
        Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report)));
        var code = Records(app).Where(r => (string)r["citation"]!["kind"]! == "code").ToList();
        Assert.Equal(SpecApp.Create, (string)Assert.Single(code)["citation"]!["symbol"]!);
    }

    // ---- A decision rests on the words it was made on ----

    const string Flipped = "Orders over 100 are refused is never done; any order is accepted.";

    static JsonObject Decided()
    {
        var link = LinkRun.Entry("agree", "shop", SpecIndexSection(), Fsd50, SpecApp.Post, "R1", Page100, "judge", "confirmed", null);
        link["decision"] = "agree";
        link["decided_by"] = "bob";
        return link;
    }

    static void Rephrase(SpecApp app, string text) =>
        StatementIdTests.Regenerate(app, StatementIdTests.Writer(rules: [StatementIdTests.S(text, SpecApp.Place)]));

    [Fact]
    public void A_rephrase_that_flips_the_meaning_keeps_its_id_and_the_decision_drops_back_and_is_reported()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Decided());
        Assert.True((bool)Check(app)["ok"]!);
        var id = (string)Entry(app)["id"]!;
        Assert.Equal(TraceFile.QuoteHash(Rule), (string)Entry(app)["decided_on"]!);

        Rephrase(app, Flipped);
        Assert.Equal(Flipped, (string)SpecIndex.Load(app.Root).Statement(SpecApp.Post, "R1")!["text"]!);

        var plain = Check(app);
        Assert.False((bool)plain["ok"]!);
        Assert.Contains(Problems(plain), p => p.StartsWith(id, StringComparison.Ordinal) && p.Contains("changed after this decision was made"));
        Assert.Equal("agree", (string)Entry(app)["decision"]!);

        var report = Check(app, fix: true);
        Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report)));
        Assert.Empty(report["staleQuotes"]!.AsArray());
        Assert.Equal((id, "R1", "agree"), ((string)report["changedStatements"]![0]!["entry"]!, (string)report["changedStatements"]![0]!["statement"]!, (string)report["changedStatements"]![0]!["was"]!));
        Assert.Equal(1, (int)report["counts"]!["changedStatements"]!);
        Assert.Equal((null, null, null), ((string?)Entry(app)["decision"], (string?)Entry(app)["decided_by"], (string?)Entry(app)["decided_on"]));
        var move = StateChanges.History(app.Root, StateKinds.TraceDecision, id)[^1];
        Assert.Equal(("agree", "undecided", "code"), (move.From, move.To, move.Who.Kind));
        Assert.Contains("the statement it was decided on changed", move.Reason);
    }

    [Fact]
    public void A_decision_on_a_finding_drops_back_and_its_evidence_stops_naming_the_person_in_one_go()
    {
        var (app, _) = App();
        using var _d = app;
        var entry = Conflict();
        entry["decision"] = "code-wrong";
        entry["decided_by"] = "bob";
        Save(app, entry);
        Check(app);
        Rephrase(app, Flipped);
        Check(app, fix: true);
        Assert.Null(Entry(app)["decision"]);
        Assert.DoesNotContain(Records(app).SelectMany(r => r["checkedBy"]!.AsArray()), c => (string)c!["kind"]! == "person");
        var once = File.ReadAllText(TraceFile.PathIn(app.Root));
        Check(app);
        Assert.Equal(once, File.ReadAllText(TraceFile.PathIn(app.Root)));
    }

    [Fact]
    public void Only_whitespace_changing_is_not_a_changed_statement()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Decided());
        Check(app);
        Change(app, e => e["decided_on"] = TraceFile.QuoteHash("  Orders  over 100 are refused with\n\"Orders over 100 need approval\". "));
        var report = Check(app, fix: true);
        Assert.True((bool)report["ok"]!);
        Assert.Equal("agree", (string)Entry(app)["decision"]!);
    }

    [Fact]
    public void A_file_with_no_stored_hash_gets_one_at_first_check_and_drops_nothing()
    {
        var (app, _) = App();
        using var _d = app;
        Rephrase(app, Rule + " Always.");
        Save(app, Decided());
        Assert.Null(Entry(app)["decided_on"]);
        var report = Check(app);
        Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report)));
        Assert.Empty(report["changedStatements"]!.AsArray());
        Assert.Equal(("agree", "bob", TraceFile.QuoteHash(Rule + " Always.")), ((string)Entry(app)["decision"]!, (string)Entry(app)["decided_by"]!, (string)Entry(app)["decided_on"]!));
        Assert.Empty(StateChanges.History(app.Root, StateKinds.TraceDecision, (string)Entry(app)["id"]!));
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("attribute")]
    [InlineData("rejudge")]
    public void A_decision_is_tied_before_a_regeneration_whichever_way_it_was_made(string how)
    {
        var (app, _) = App();
        using var _d = app;
        var link = Decided();
        link["decision"] = null;
        link["decided_by"] = null;
        Save(app, link);
        var baseTrace = TraceFile.Load(app.Root);
        switch (how)
        {
            case "approve":
                Assert.Equal(1, TraceCheck.ApproveChapter(app.Root, "shop", "1", "bob"));
                break;
            case "attribute":
                Change(app, e => e["decision"] = "agree");
                Assert.Equal(1, TraceCheck.Attribute(app.Root, baseTrace, "bob"));
                break;
            case "rejudge":
                // The same link judged again, after it was decided and tied: what it was decided on stays with it.
                Change(app, e => { e["decision"] = "agree"; e["decided_by"] = "bob"; });
                Check(app);
                link["decision"] = null;
                link["decided_by"] = null;
                LinkRun.ApplyEntries(app.Root, "shop", "1", [link]);
                break;
        }
        Assert.Equal(TraceFile.QuoteHash(Rule), (string)Entry(app)["decided_on"]!);
        Rephrase(app, Flipped);
        Assert.Single(Check(app, fix: true)["changedStatements"]!.AsArray());
        Assert.Null(Entry(app)["decision"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Changing_what_a_decision_rests_on_by_hand_is_listed_against_the_base(bool delete)
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Decided());
        Check(app);
        var baseTrace = TraceFile.Load(app.Root);
        Rephrase(app, Flipped);
        Change(app, e => { if (delete) e.Remove("decided_on"); else e["decided_on"] = TraceFile.QuoteHash(Flipped); });
        Assert.Contains(Problems(Check(app, null, baseTrace)), p => p.Contains("decided_on was changed in this change"));
    }

    [Fact]
    public void What_a_decision_rests_on_and_the_evidence_hash_reach_no_hash_and_no_prompt()
    {
        var (app, verifier) = App();
        using var _d = app;
        Save(app, Decided(), Conflict());
        Check(app);
        var entries = TraceFile.Load(app.Root)["entries"]!.AsArray().OfType<JsonObject>().ToList();
        var decidedOn = (string)entries.Single(e => e["decided_on"] is not null)["decided_on"]!;
        var evidenceHash = (string)entries.Single(e => e["evidence_hash"] is not null)["evidence_hash"]!;
        Assert.Equal(string.Join("\n", SavedWorkTests.Saved), string.Join("\n", SavedWorkTests.Hashes(app, verifier)));
        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!), "*.md"))
        {
            var bundle = File.ReadAllText(file);
            Assert.DoesNotContain(decidedOn, bundle);
            Assert.DoesNotContain(evidenceHash, bundle);
        }
    }

    // ---- Severity ----

    [Fact]
    public void Severity_has_one_scale_and_the_schema_agrees()
    {
        var schema = JsonNode.Parse(Stangent.Core.SchemaRegistry.EmbeddedText(TraceFile.Schema))!;
        var values = schema["properties"]!["entries"]!["items"]!["properties"]!["severity"]!["enum"]!.AsArray().Select(v => (string)v!);
        Assert.Equal(Severity.All, values);
        Assert.Equal(Severity.All.OrderBy(s => s), StateKinds.Get(StateKinds.FindingSeverity).States.OrderBy(s => s));
    }

    [Fact]
    public void A_person_sets_severity_through_the_state_module_and_the_name_comes_from_the_merge()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict());
        Check(app);
        var baseTrace = TraceFile.Load(app.Root);
        Change(app, e => e["severity"] = Severity.Major);

        Assert.Equal(1, TraceCheck.Attribute(app.Root, baseTrace, "pm-anna"));
        var entry = Entry(app);
        Assert.Equal(("major", "pm-anna"), ((string)entry["severity"]!, (string)entry["severity_by"]!));
        Assert.Null(entry["decision"]);
        var moves = StateChanges.History(app.Root, StateKinds.FindingSeverity, "X-0001");
        Assert.Equal(("ungraded", "major", "person", "pm-anna"), (moves[^1].From, moves[^1].To, moves[^1].Who.Kind, moves[^1].Who.Name));

        // A decision and a severity are told apart: dropping the decision leaves the grade and who gave it.
        Change(app, e => { e["decision"] = "defer"; e["decided_by"] = "pm-anna"; });
        StateChanges.Apply(app.Root, TraceFile.Load(app.Root)["entries"]![0]!.AsObject(), StateKinds.TraceDecision, "L", "undecided", StateActor.Code("t"), "stale");
        Assert.Equal("pm-anna", (string)Entry(app)["severity_by"]!);
    }

    [Fact]
    public void Code_and_an_agent_cannot_grade_a_finding()
    {
        var (app, _) = App();
        using var _d = app;
        Assert.Equal("illegal_transition", Assert.Throws<StangentException>(() =>
            StateChanges.Move(app.Root, StateKinds.FindingSeverity, "X-0001", "ungraded", "major", StateActor.Code("t"), "r")).Code);
        Assert.Equal("illegal_transition", Assert.Throws<StangentException>(() =>
            StateChanges.Move(app.Root, StateKinds.FindingSeverity, "X-0001", "ungraded", "major", StateActor.Agent("claude"), "r")).Code);
    }

    [Fact]
    public void A_typed_severity_by_a_made_up_value_and_a_changed_id_are_all_listed()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict());
        Check(app);
        var baseTrace = TraceFile.Load(app.Root);

        Change(app, e => e["severity_by"] = "Pat");
        Assert.Contains(Problems(Check(app, null, baseTrace)), p => p.Contains("severity_by was set in this change"));
        Change(app, e => { e.Remove("severity_by"); e["finding"] = "X-0009"; });
        Assert.Contains(Problems(Check(app, null, baseTrace)), p => p.Contains("its finding id was X-0001"));
        Change(app, e => { e["finding"] = "X-0001"; e["severity"] = "catastrophic"; });
        Assert.False((bool)Check(app)["ok"]!);
    }

    [Fact]
    public void An_agree_link_can_not_carry_a_severity()
    {
        var (app, _) = App();
        using var _d = app;
        var agree = LinkRun.Entry("agree", "shop", SpecIndexSection(), Fsd50, SpecApp.Post, "E1", "which sends a receipt", "judge", "confirmed", null);
        agree["severity"] = "major";
        Save(app, agree);
        Assert.Contains(Problems(Check(app)), p => p.Contains("an agree link isn't a finding"));
    }

    // ---- Old files ----

    [Fact]
    public void An_old_trace_file_still_reads_and_keeps_its_decisions()
    {
        var (app, _) = App();
        using var _d = app;
        var conflict = Conflict();
        File.WriteAllText(TraceFile.PathIn(app.Root), $$"""
            version: 2
            entries:
              - id: "{{(string)conflict["id"]!}}"
                state: "conflict"
                fsd:
                  doc: "shop"
                  section: "1"
                  title: "Orders"
                  quote: "{{Fsd50}}"
                  quoteHash: "{{TraceFile.QuoteHash(Fsd50)}}"
                page:
                  id: "{{SpecApp.Post}}"
                  statement: "R1"
                  quote: "{{Page100}}"
                  quoteHash: "{{TraceFile.QuoteHash(Page100)}}"
                by: "judge"
                note: "FSD 50, code 100"
                decision: "code-wrong"
                decided_by: "pm-anna"

            """);
        var baseTrace = TraceFile.Load(app.Root);
        var report = TraceCheck.Run(app.Root, null, baseTrace, false);
        Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report)));
        var entry = Entry(app);
        Assert.Equal(("code-wrong", "pm-anna", (string)conflict["id"]!, "X-0001"), ((string)entry["decision"]!, (string)entry["decided_by"]!, (string)entry["id"]!, (string)entry["finding"]!));
        // The person who decided is on the evidence, from the file, not typed.
        Assert.Contains(entry["evidence"]![0]!["checkedBy"]!.AsArray(), c => (string)c!["kind"]! == "person" && (string)c["name"]! == "pm-anna");
        Assert.StartsWith("# FSD", File.ReadAllText(TraceFile.PathIn(app.Root)));
        Assert.Contains("version: 4", File.ReadAllText(TraceFile.PathIn(app.Root)));
    }

    [Fact]
    public void With_findings_off_the_check_issues_nothing_and_writes_nothing()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, LinkRun.Entry("agree", "shop", SpecIndexSection(), Fsd50, SpecApp.Post, "E1", "which sends a receipt", "judge", "confirmed", null));
        var before = File.ReadAllText(TraceFile.PathIn(app.Root));
        Check(app);
        Assert.Equal(before, File.ReadAllText(TraceFile.PathIn(app.Root)));
        Assert.False(File.Exists(TraceFindings.LedgerFile(app.Root)));
    }

    [Fact]
    public void A_moved_section_keeps_the_finding_and_its_grade()
    {
        var (app, _) = App();
        using var _d = app;
        Save(app, Conflict());
        Check(app);
        var baseTrace = TraceFile.Load(app.Root);
        Change(app, e => { e["id"] = TraceFile.EntryId("conflict", "shop", "2", SpecApp.Post, Fsd50, Page100); e["fsd"]!["section"] = "2"; });
        // Nobody graded anything in that move, so there's nothing to attribute.
        Assert.Equal(0, TraceCheck.Attribute(app.Root, baseTrace, "pm-anna"));
        Assert.Equal("X-0001", (string)Entry(app)["finding"]!);
    }
}

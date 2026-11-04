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

    static JsonObject SpecIndexSection() => new() { ["id"] = "1", ["title"] = "Orders" };

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
        Assert.Contains("version: 3", File.ReadAllText(TraceFile.PathIn(app.Root)));
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

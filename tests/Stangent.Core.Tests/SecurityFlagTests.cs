using System.Text.Json.Nodes;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>The security flag (D141, D148), with canned answers. No test here reaches an AI.</summary>
public class SecurityFlagTests
{
    const string Fsd50 = "Orders over 50 need approval.";
    const string Page100 = "Orders over 100 are refused";
    const string RealLine = "if (total > 100) throw new ArgumentException(\"Orders over 100 need approval\");";
    const string Why = "The server trusts the total the browser sent.";

    static SpecApp App(bool flag = true)
    {
        var app = SavedWorkTests.App(out _);
        SavedWorkTests.CleanGraph(app);
        app.Repo.Config.Fsd.Disprove = true;
        if (flag) app.Repo.Config.Components["security-flag"] = true;
        return app;
    }

    static JsonObject Section() => new() { ["id"] = "1", ["title"] = "Orders", ["permanentId"] = "S-0001" };

    static JsonObject Conflict() => LinkRun.Entry("conflict", "shop", Section(), Fsd50, SpecApp.Post, "R1", Page100, "judge", null, "n");

    static void Save(SpecApp app, params JsonObject[] entries)
    {
        var trace = TraceFile.Empty();
        foreach (var e in entries) trace["entries"]!.AsArray().Add((JsonNode)e);
        TraceFile.Save(app.Root, trace);
    }

    static JsonObject Entry(SpecApp app) => TraceFile.Load(app.Root)["entries"]![0]!.AsObject();

    static void Edit(SpecApp app, Action<JsonObject> change)
    {
        var trace = TraceFile.Load(app.Root);
        change(trace["entries"]![0]!.AsObject());
        TraceFile.Save(app.Root, trace);
    }

    static RunStep Prepare(SpecApp app) => DisproveRun.Prepare(app.Pages(), app.Repo, "shop", "1");

    static RunStep Answer(SpecApp app, RunStep step, JsonObject answer)
    {
        File.WriteAllText(Path.Combine(app.Root, step.Output!), answer.ToJsonString());
        return DisproveRun.Check(app.Pages(), app.Repo, "shop", "1", new Usage("sonnet-test", 1, 100, 0, 0, 10, 0.01, 5));
    }

    static JsonObject Flag(string quote = RealLine, int line = 5) => new() { ["touches"] = true, ["why"] = Why, ["path"] = "Core/OrderService.cs", ["start"] = line, ["end"] = line, ["quote"] = quote };

    static JsonObject NoFlag() => new() { ["touches"] = false, ["why"] = "Nothing here is about who may do what." };

    static JsonObject Said(string verdict, JsonObject? security, string? severity = "minor")
    {
        var o = verdict == "cant-tell" ? new JsonObject { ["verdict"] = verdict, ["why"] = "because" }
            : new JsonObject { ["verdict"] = verdict, ["why"] = "because", ["path"] = "Core/OrderService.cs", ["start"] = 5, ["end"] = 5, ["quote"] = RealLine };
        if (severity is not null) o["suggestedSeverity"] = severity;
        if (security is not null) o["security"] = security;
        return o;
    }

    static JsonObject Flagged(SpecApp app, string verdict = "stands")
    {
        Save(app, Conflict());
        Assert.Equal("recorded", Answer(app, Prepare(app), Said(verdict, Flag())).Status);
        return Entry(app);
    }

    static JsonObject CheckIt(SpecApp app, bool fix = false, JsonObject? baseTrace = null) => TraceCheck.Run(app.Root, GraphStore.Load(GraphStore.PathFor(app.Repo)), baseTrace, fix);

    static List<string> Problems(JsonObject report) => [.. report["problems"]!.AsArray().Select(p => (string)p!)];

    static string Bundle(SpecApp app, RunStep step) => File.ReadAllText(Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, "bundle.md"));

    // ---- the step ----

    [Fact]
    public void Its_on_the_step_only_with_the_switch_and_asks_with_its_own_instructions_and_schema()
    {
        using var on = App();
        Save(on, Conflict());
        var step = Prepare(on);
        Assert.Equal(DisproveRun.SecurityOutputSchema, (string)step.ToJson("x")["next"]!["schema"]!);
        Assert.Contains(Bundles.Instructions("finding-disprover-security.md"), Bundle(on, step));
        Assert.Contains("\"touches\"", Bundle(on, step));

        using var off = App(flag: false);
        Save(off, Conflict());
        var plain = Prepare(off);
        Assert.Equal(DisproveRun.OutputSchema, (string)plain.ToJson("x")["next"]!["schema"]!);
        Assert.DoesNotContain("touches", Bundle(off, plain));
        Assert.DoesNotContain(Bundles.Instructions("finding-disprover-security.md"), Bundle(off, plain));
    }

    [Fact]
    public void It_needs_the_disprove_step()
    {
        using var app = App();
        app.Repo.Config.Fsd.Disprove = false;
        Save(app, Conflict());
        Assert.Equal("off", Prepare(app).Status);
        Assert.Contains("needs disprove", StangentComponents.MissingNeed(app.Repo.Config, "security-flag"));
    }

    [Fact]
    public void A_flagged_finding_holds_its_flag_with_the_line_it_cites_and_a_person_still_grades_it()
    {
        using var app = App();
        var e = Flagged(app);
        Assert.Equal(Why, (string)e["security"]!["why"]!);
        Assert.Equal(RealLine, (string)e["security"]!["quote"]!);
        Assert.Equal(5, (int)e["security"]!["start"]!);
        Assert.Equal(SpecApp.Place, (string)e["security"]!["symbol"]!);
        Assert.Equal("unanswered", (string)e["security_state"]!);
        Assert.Equal("unanswered", StateChanges.History(app.Root, StateKinds.FindingSecurity, (string)e["id"]!).Last().To);
        Assert.Equal(StateActor.CodeKind, StateChanges.History(app.Root, StateKinds.FindingSecurity, (string)e["id"]!).Last().Who.Kind);
        // D118: the flag grades nothing, and a suggestion stays one.
        Assert.Equal("ungraded", (string)e["severity"]!);
        Assert.Equal("minor", (string)e["disproof"]!["suggestedSeverity"]!);
        Assert.Null(e["severity_by"]);
        Assert.True((bool)CheckIt(app)["ok"]!, string.Join("\n", Problems(CheckIt(app))));
    }

    [Fact]
    public void A_finding_that_doesnt_touch_security_has_no_flag_and_no_queue_for_it()
    {
        using var app = App();
        Save(app, Conflict());
        Assert.Equal("recorded", Answer(app, Prepare(app), Said("stands", NoFlag())).Status);
        var e = Entry(app);
        Assert.Null(e["security"]);
        Assert.Null(e["security_state"]);
        Assert.False(TraceFindings.Flagged(e));
        var report = CheckIt(app);
        Assert.Null(report["queues"]!["security"]);
        Assert.Null(report["findings"]![0]!["security"]);
        Assert.Null(report["findings"]![0]!["alsoGoesTo"]);
    }

    [Theory]
    [InlineData("refuted", "developer-confirm")]
    [InlineData("unreachable", "developer-confirm")]
    [InlineData("page-gap", "developer")]
    [InlineData("stands", "ba")]
    [InlineData("cant-tell", "ba")]
    public void Any_verdict_can_be_flagged_and_its_listed_as_well_as_wherever_the_verdict_sends_it(string verdict, string goesTo)
    {
        using var app = App();
        Flagged(app, verdict);
        var report = CheckIt(app);
        Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report)));
        var listed = report["findings"]![0]!;
        Assert.Equal(goesTo, (string)listed["goesTo"]!);
        Assert.Equal("security", (string)listed["alsoGoesTo"]![0]!);
        Assert.Equal("unanswered", (string)listed["security"]!["answer"]!);
        Assert.Equal("Core/OrderService.cs:5-5", (string)listed["security"]!["at"]!);
        Assert.Equal("X-0001", (string)report["queues"]!["security"]![0]!);
        Assert.Equal("X-0001", (string)report["queues"]![goesTo]![0]!);
    }

    [Fact]
    public void A_refuted_flagged_finding_a_person_closed_stays_on_the_security_queue()
    {
        using var app = App();
        Flagged(app, "refuted");
        TraceFindings.Review(app.Root, "X-0001", "confirmed", "pat", "the quote holds");
        var report = CheckIt(app);
        Assert.Equal("closed", (string)report["findings"]![0]!["goesTo"]!);
        Assert.Equal("X-0001", (string)report["queues"]!["closed"]![0]!);
        Assert.Equal("X-0001", (string)report["queues"]!["security"]![0]!);
        Assert.True(TraceFindings.SecurityOpen(Entry(app)));
    }

    [Fact]
    public void A_person_deciding_a_flagged_finding_answers_nothing()
    {
        using var app = App();
        Flagged(app);
        Edit(app, e => StateChanges.Apply(app.Root, e, StateKinds.TraceDecision, (string)e["id"]!, "code-wrong", StateActor.Person("pat"), "decided"));
        var report = CheckIt(app);
        Assert.Equal("decided", (string)report["findings"]![0]!["goesTo"]!);
        Assert.Equal("X-0001", (string)report["queues"]!["security"]![0]!);
    }

    // ---- code checks it ----

    [Theory]
    [InlineData("return nothing like this line;", 5)]
    [InlineData(RealLine, 500)]
    public void A_flag_whose_citation_fails_gets_one_retry_then_is_no_flag_and_what_it_said_stays_on_file(string quote, int line)
    {
        using var app = App();
        Save(app, Conflict());
        var retry = Answer(app, Prepare(app), Said("stands", Flag(quote, line)));
        Assert.Equal("retry", retry.Status);
        Assert.Contains("the security flag", retry.Errors![0]);
        Assert.Equal(DisproveRun.SecurityOutputSchema, (string)retry.ToJson("x")["next"]!["schema"]!);
        Assert.Null(Entry(app)["security"]);

        Assert.Equal("recorded", Answer(app, retry, Said("stands", Flag(quote, line))).Status);
        var e = Entry(app);
        Assert.Equal("cant-tell", (string)e["verdict"]!);
        Assert.Contains("the security flag", (string)e["disproof"]!["why"]!);
        Assert.Equal(Why, (string)e["disproof"]!["claimedSecurity"]!);
        Assert.False(TraceFindings.Flagged(e));
        var report = CheckIt(app);
        Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report)));
        Assert.Equal(Why, (string)report["findings"]![0]!["securityClaimed"]!);
        Assert.Null(report["queues"]!["security"]);
    }

    [Fact]
    public void A_retry_can_put_the_citation_right()
    {
        using var app = App();
        Save(app, Conflict());
        var retry = Answer(app, Prepare(app), Said("stands", Flag("return nothing like this line;")));
        Assert.Equal("recorded", Answer(app, retry, Said("stands", Flag())).Status);
        Assert.True(TraceFindings.SecurityOpen(Entry(app)));
    }

    [Theory]
    [InlineData("path")]
    [InlineData("quote")]
    public void A_flag_with_nothing_to_back_it_is_sent_back(string missing)
    {
        using var app = App();
        Save(app, Conflict());
        var flag = Flag();
        flag.Remove(missing);
        var retry = Answer(app, Prepare(app), Said("stands", flag));
        Assert.Equal("retry", retry.Status);
        Assert.Contains("a security flag needs path, start, end and quote", retry.Errors![0]);
    }

    [Fact]
    public void An_answer_with_no_security_at_all_is_sent_back()
    {
        using var app = App();
        Save(app, Conflict());
        var retry = Answer(app, Prepare(app), Said("stands", null));
        Assert.Equal("retry", retry.Status);
        Assert.Contains("security", retry.Errors![0]);
    }

    [Fact]
    public void A_stands_verdict_has_to_suggest_a_severity_and_other_verdicts_dont()
    {
        using var app = App();
        Save(app, Conflict());
        var retry = Answer(app, Prepare(app), Said("stands", NoFlag(), severity: null));
        Assert.Equal("retry", retry.Status);
        Assert.Contains("suggestedSeverity", retry.Errors![0]);

        using var other = App();
        Save(other, Conflict());
        Assert.Equal("recorded", Answer(other, Prepare(other), Said("refuted", NoFlag(), severity: null)).Status);
    }

    // ---- the switch ----

    [Fact]
    public void Off_a_stands_verdict_with_no_severity_is_fine_and_an_answer_with_security_is_refused_by_the_schema()
    {
        using var app = App(flag: false);
        Save(app, Conflict());
        var step = Prepare(app);
        var retry = Answer(app, step, Said("stands", Flag(), severity: null));
        Assert.Equal("retry", retry.Status);
        Assert.Equal("recorded", Answer(app, retry, Said("stands", null, severity: null)).Status);
        var e = Entry(app);
        Assert.Null(e["security"]);
        Assert.Null(e["disproof"]!["suggestedSeverity"]);
    }

    [Fact]
    public void Off_the_file_and_the_report_have_nothing_of_it()
    {
        using var app = App(flag: false);
        Save(app, Conflict());
        Answer(app, Prepare(app), Said("stands", null));
        var report = CheckIt(app);
        Assert.DoesNotContain("security", File.ReadAllText(TraceFile.PathIn(app.Root)));
        Assert.DoesNotContain("security", report.ToJsonString());
    }

    [Fact]
    public void A_verdict_from_before_the_switch_stays_valid_until_the_step_runs_again_and_then_its_asked_afresh()
    {
        using var app = App(flag: false);
        Save(app, Conflict());
        Answer(app, Prepare(app), Said("refuted", null));
        TraceFindings.Review(app.Root, "X-0001", "confirmed", "pat", "read it");
        var before = File.ReadAllText(TraceFile.PathIn(app.Root));

        // Turning it on changes nothing until a run: the verdict and a person's word stand, and the check is happy.
        app.Repo.Config.Components["security-flag"] = true;
        var report = CheckIt(app);
        Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report)));
        Assert.Equal("closed", (string)report["findings"]![0]!["goesTo"]!);
        Assert.Equal(before, File.ReadAllText(TraceFile.PathIn(app.Root)));

        // The switch's input moved the bundle's hash, so a run asks again, and the verdict and that word drop first, as for any changed bundle (D119).
        var step = Prepare(app);
        Assert.Equal(DisproveRun.SecurityOutputSchema, (string)step.ToJson("x")["next"]!["schema"]!);
        var dropped = Entry(app);
        Assert.Null(dropped["verdict"]);
        Assert.Null(dropped["review"]);
        Assert.Equal("recorded", Answer(app, step, Said("refuted", Flag())).Status);
        Assert.True(TraceFindings.SecurityOpen(Entry(app)));
        Assert.Equal("current", Prepare(app).Status);
    }

    [Fact]
    public void Turning_the_switch_off_closes_nothing()
    {
        using var app = App();
        Flagged(app);
        app.Repo.Config.Components["security-flag"] = false;
        // The bundle's hash moved back, so a run asks again in the plain form; the flag isn't part of the verdict, so it stays through it.
        var step = Prepare(app);
        Assert.True(TraceFindings.SecurityOpen(Entry(app)));
        Assert.Equal("recorded", Answer(app, step, Said("stands", null)).Status);
        Assert.True(TraceFindings.SecurityOpen(Entry(app)));
        Assert.Equal("X-0001", (string)CheckIt(app)["queues"]!["security"]![0]!);
    }

    // ---- it never closes by itself ----

    [Fact]
    public void A_later_run_that_doesnt_flag_it_closes_nothing()
    {
        using var app = App();
        var was = Flagged(app);
        Edit(app, e => StateChanges.Apply(app.Root, e, StateKinds.FindingVerdict, (string)e["id"]!, "unverified", StateActor.Code("t"), "a changed bundle"));
        Assert.True(TraceFindings.SecurityOpen(Entry(app)));
        Assert.Equal("recorded", Answer(app, Prepare(app), Said("refuted", NoFlag())).Status);
        var e = Entry(app);
        Assert.True(TraceFindings.SecurityOpen(e));
        Assert.Equal((string)was["security_hash"]!, (string)e["security_hash"]!);
    }

    [Fact]
    public void A_link_nothing_finds_any_more_keeps_its_flag_as_gone()
    {
        using var app = App();
        Flagged(app);
        LinkRun.ApplyEntries(app.Root, "shop", "1", []);
        var e = Entry(app);
        Assert.True(TraceFindings.IsGone(e));
        Assert.True(TraceFindings.SecurityOpen(e));
        var report = CheckIt(app);
        Assert.Equal("X-0001", (string)report["queues"]!["security"]![0]!);
        Assert.Equal("X-0001", (string)report["queues"]!["gone"]![0]!);
        Assert.Equal("unanswered", (string)report["gone"]![0]!["security"]!["answer"]!);
    }

    [Fact]
    public void A_link_nothing_finds_with_no_flag_still_goes_as_it_always_has()
    {
        using var app = App();
        Save(app, Conflict());
        Answer(app, Prepare(app), Said("stands", NoFlag()));
        LinkRun.ApplyEntries(app.Root, "shop", "1", []);
        Assert.Empty(TraceFile.Load(app.Root)["entries"]!.AsArray());
    }

    [Fact]
    public void A_link_judged_again_keeps_its_flag()
    {
        using var app = App();
        var was = Flagged(app);
        LinkRun.ApplyEntries(app.Root, "shop", "1", [Conflict()]);
        var e = Entry(app);
        Assert.Equal((string)was["id"]!, (string)e["id"]!);
        Assert.True(TraceFindings.SecurityOpen(e));
    }

    // ---- a person answers it ----

    [Theory]
    [InlineData("confirmed")]
    [InlineData("not-security")]
    public void A_person_answers_it_from_their_git_name_and_it_stays_on_file_and_grades_nothing(string answer)
    {
        using var app = App();
        var was = Flagged(app, "refuted");
        var result = TraceFindings.AnswerSecurity(app.Root, "X-0001", answer, "pat", "read the line");
        Assert.Equal((answer, "pat", "developer-confirm"), ((string)result["answer"]!, (string)result["by"]!, (string)result["goesTo"]!));

        var e = Entry(app);
        Assert.Equal(TraceFindings.SecurityAnswer(e), answer);
        Assert.False(TraceFindings.SecurityOpen(e));
        Assert.Equal((string)was["security_hash"]!, (string)e["security_hash"]!);
        var move = StateChanges.History(app.Root, StateKinds.FindingSecurity, (string)e["id"]!).Last();
        Assert.Equal((answer, StateActor.PersonKind, "pat"), (move.To, move.Who.Kind, move.Who.Name));
        // D118: a confirmed flag grades nothing.
        Assert.Equal("ungraded", (string)e["severity"]!);
        Assert.Null(e["severity_by"]);
        Assert.Equal((string)was["verdict"]!, (string)e["verdict"]!);

        var report = CheckIt(app);
        Assert.True((bool)report["ok"]!, string.Join("\n", Problems(report)));
        Assert.Empty(report["queues"]!["security"]!.AsArray());
        var listed = report["findings"]![0]!;
        Assert.Equal((answer, "pat", "read the line"), ((string)listed["security"]!["answer"]!, (string)listed["security"]!["answerBy"]!, (string)listed["security"]!["answerWhy"]!));
        Assert.Null(listed["alsoGoesTo"]);
        Assert.Equal("developer-confirm", (string)listed["goesTo"]!);
        Assert.Equal(0, (int)report["counts"]!["security"]!["unanswered"]!);
        Assert.Equal(1, (int)report["counts"]!["security"]!["flagged"]!);
    }

    [Fact]
    public void A_person_can_change_their_mind_and_saying_the_same_again_is_refused()
    {
        using var app = App();
        Flagged(app);
        TraceFindings.AnswerSecurity(app.Root, "X-0001", "confirmed", "pat", null);
        Assert.Equal("already_answered", Assert.Throws<StangentException>(() => TraceFindings.AnswerSecurity(app.Root, "X-0001", "confirmed", "sam", null)).Code);
        TraceFindings.AnswerSecurity(app.Root, "X-0001", "not-security", "sam", "wrong line");
        var e = Entry(app);
        Assert.Equal("not-security", TraceFindings.SecurityAnswer(e));
        Assert.Equal("sam", (string)e["security_note"]!["by"]!);
    }

    [Fact]
    public void Only_a_flag_can_be_answered_and_only_with_its_two_words()
    {
        using var app = App();
        Save(app, Conflict());
        CheckIt(app);
        Assert.Equal("not_flagged", Assert.Throws<StangentException>(() => TraceFindings.AnswerSecurity(app.Root, "X-0001", "confirmed", "pat", null)).Code);
        Assert.Equal("not_found", Assert.Throws<StangentException>(() => TraceFindings.AnswerSecurity(app.Root, "X-9999", "confirmed", "pat", null)).Code);
        Assert.Equal("usage", Assert.Throws<StangentException>(() => TraceFindings.AnswerSecurity(app.Root, "X-0001", "closed", "pat", null)).Code);
    }

    [Fact]
    public void A_typed_answer_counts_for_nothing_and_is_a_problem_and_a_person_can_still_answer()
    {
        using var app = App();
        Flagged(app);
        Edit(app, e =>
        {
            e["security_state"] = "not-security";
            e["security_note"] = new JsonObject { ["by"] = "someone", ["why"] = "typed" };
        });
        var e = Entry(app);
        Assert.Null(TraceFindings.SecurityAnswer(e));
        Assert.True(TraceFindings.SecurityOpen(e));
        var report = CheckIt(app);
        Assert.False((bool)report["ok"]!);
        Assert.Contains(Problems(report), p => p.Contains("security answer isn't what code wrote") && p.Contains("X-0001"));
        Assert.Equal("X-0001", (string)report["queues"]!["security"]![0]!);
        Assert.Equal("unanswered", (string)report["findings"]![0]!["security"]!["answer"]!);

        TraceFindings.AnswerSecurity(app.Root, "X-0001", "not-security", "pat", null);
        Assert.Equal("unanswered", StateChanges.History(app.Root, StateKinds.FindingSecurity, (string)e["id"]!).Last().From);
        Assert.True((bool)CheckIt(app)["ok"]!);
    }

    [Fact]
    public void An_answer_hash_from_another_flag_answers_nothing()
    {
        using var app = App();
        Flagged(app);
        TraceFindings.AnswerSecurity(app.Root, "X-0001", "not-security", "pat", null);
        Edit(app, e => e["security_answer_hash"] = TraceFile.SecurityAnswerHash("not-security", e["security_note"] as JsonObject, TraceFile.SecurityHash(new JsonObject { ["why"] = "other" })));
        Assert.True(TraceFindings.SecurityOpen(Entry(app)));
        Assert.False((bool)CheckIt(app)["ok"]!);
    }

    [Fact]
    public void A_typed_flag_is_a_problem_stays_in_sight_and_cant_be_answered()
    {
        using var app = App();
        Flagged(app);
        Edit(app, e => e["security"]!["why"] = "someone typed this");
        var report = CheckIt(app);
        Assert.Contains(Problems(report), p => p.Contains("security flag isn't what code wrote"));
        Assert.Equal("X-0001", (string)report["queues"]!["security"]![0]!);
        Assert.Equal("flag_typed", Assert.Throws<StangentException>(() => TraceFindings.AnswerSecurity(app.Root, "X-0001", "not-security", "pat", null)).Code);
    }

    [Fact]
    public void Taking_a_flag_out_of_the_file_is_a_problem_against_the_base()
    {
        using var app = App();
        Flagged(app);
        CheckIt(app);
        var baseTrace = TraceFile.Load(app.Root);
        Edit(app, e =>
        {
            e.Remove("security");
            e.Remove("security_hash");
            e.Remove("security_state");
        });
        Assert.Contains(Problems(CheckIt(app, baseTrace: baseTrace)), p => p.Contains("security flag was removed"));

        Save(app);
        Assert.Contains(Problems(CheckIt(app, baseTrace: baseTrace)), p => p.Contains("had a security flag"));
    }

    [Fact]
    public void An_answer_drops_when_the_statement_changes_as_a_decision_does_and_the_flag_is_open_again()
    {
        using var app = App();
        Flagged(app);
        TraceFindings.AnswerSecurity(app.Root, "X-0001", "not-security", "pat", null);
        Assert.NotNull(Entry(app)["security_note"]!["on"]);
        var id = (string)Entry(app)["id"]!;

        StatementIdTests.Regenerate(app, StatementIdTests.Writer(rules: [StatementIdTests.S(Page100 + " unless someone signs them off.", SpecApp.Place)]));
        var waiting = CheckIt(app);
        Assert.False((bool)waiting["ok"]!);
        Assert.Contains(Problems(waiting), p => p.Contains("answered its security flag"));

        var fixedIt = CheckIt(app, fix: true);
        var e = Entry(app);
        Assert.Equal("unanswered", (string)e["security_state"]!);
        Assert.Null(e["security_note"]);
        Assert.Null(e["security_answer_hash"]);
        Assert.True(TraceFindings.SecurityOpen(e));
        Assert.Equal("X-0001", (string)fixedIt["queues"]!["security"]![0]!);
        var move = StateChanges.History(app.Root, StateKinds.FindingSecurity, id).Last();
        Assert.Equal(("unanswered", StateActor.CodeKind), (move.To, move.Who.Kind));
    }

    [Fact]
    public void An_answer_drops_when_the_fsd_quote_is_no_longer_there()
    {
        using var app = App();
        Flagged(app);
        TraceFindings.AnswerSecurity(app.Root, "X-0001", "confirmed", "pat", null);
        var file = Path.Combine(FsdImport.DocDir(app.Root, "shop"), "document.json");
        File.WriteAllText(file, File.ReadAllText(file).Replace(Fsd50, "Orders over 60 need approval.", StringComparison.Ordinal));

        Assert.Contains(Problems(CheckIt(app)), p => p.Contains("answered its security flag"));
        CheckIt(app, fix: true);
        Assert.True(TraceFindings.SecurityOpen(Entry(app)));
    }

    [Fact]
    public void An_unchanged_statement_keeps_an_answer_through_a_check()
    {
        using var app = App();
        Flagged(app);
        TraceFindings.AnswerSecurity(app.Root, "X-0001", "not-security", "pat", null);
        CheckIt(app, fix: true);
        Assert.Equal("not-security", TraceFindings.SecurityAnswer(Entry(app)));
    }

    [Fact]
    public void A_later_flag_on_another_line_lets_go_of_an_answer_and_one_on_the_same_line_keeps_it()
    {
        using var app = App();
        Flagged(app);
        TraceFindings.AnswerSecurity(app.Root, "X-0001", "not-security", "pat", null);
        var again = TraceFile.Load(app.Root);
        var entry = again["entries"]![0]!.AsObject();

        var same = new JsonObject { ["why"] = "Reworded.", ["path"] = "Core/OrderService.cs", ["start"] = 9, ["end"] = 9, ["quote"] = RealLine, ["symbol"] = "s", ["commit"] = "c", ["inputs"] = "i" };
        TraceFindings.Flag(app.Root, entry, same, StateActor.Code("t"));
        Assert.Equal("not-security", TraceFindings.SecurityAnswer(entry));
        Assert.Equal(Why, (string)entry["security"]!["why"]!);

        var other = new JsonObject { ["why"] = "Another line.", ["path"] = "Core/OrderService.cs", ["start"] = 3, ["end"] = 3, ["quote"] = "another line of code", ["symbol"] = "s", ["commit"] = "c", ["inputs"] = "i" };
        TraceFindings.Flag(app.Root, entry, other, StateActor.Code("t"));
        Assert.True(TraceFindings.SecurityOpen(entry));
        Assert.Equal("Another line.", (string)entry["security"]!["why"]!);
        Assert.Null(entry["security_note"]);
    }

    [Fact]
    public void The_command_takes_its_name_from_git_and_has_no_way_to_type_one()
    {
        using var app = App();
        Flagged(app);
        var refused = WholeFlow.RunIn(app.Root, "trace", "security", "X-0001", "confirmed", "--by", "someone");
        Assert.NotEqual(0, refused.Exit);
        Assert.True(TraceFindings.SecurityOpen(Entry(app)));
        Assert.Equal(ExitCodes.Usage, WholeFlow.RunIn(app.Root, "trace", "security", "X-0001", "closed").Exit);

        var run = WholeFlow.RunIn(app.Root, "trace", "security", "X-0001", "not-security", "--why", "wrong line");
        Assert.Equal(0, run.Exit);
        var e = Entry(app);
        Assert.Equal("not-security", TraceFindings.SecurityAnswer(e));
        Assert.Equal((string)run.Envelope["result"]!["by"]!, (string)e["security_note"]!["by"]!);
        Assert.Equal("wrong line", (string)e["security_note"]!["why"]!);
        Assert.Equal(StateActor.PersonKind, StateChanges.History(app.Root, StateKinds.FindingSecurity, (string)e["id"]!).Last().Who.Kind);
    }

    // ---- trace links ----

    [Fact]
    public void Trace_links_shows_it_and_says_nothing_of_it_when_theres_none()
    {
        using var app = App();
        Save(app, Conflict());
        var plain = TraceLinks.Run(app.Root, "shop", null);
        Assert.DoesNotContain("security", plain.ToJsonString());

        Save(app);
        Flagged(app);
        var links = TraceLinks.Run(app.Root, "shop", null);
        Assert.Equal("unanswered", (string)links["links"]![0]!["security"]!["answer"]!);
        Assert.Equal(Why, (string)links["links"]![0]!["security"]!["why"]!);
        Assert.Equal(1, (int)links["counts"]!["securityUnanswered"]!);

        TraceFindings.AnswerSecurity(app.Root, "X-0001", "confirmed", "pat", null);
        var answered = TraceLinks.Run(app.Root, "shop", null);
        Assert.Equal(("confirmed", "pat"), ((string)answered["links"]![0]!["security"]!["answer"]!, (string)answered["links"]![0]!["security"]!["answerBy"]!));
        Assert.Equal(0, (int)answered["counts"]!["securityUnanswered"]!);
    }

    [Fact]
    public void No_table_lets_code_answer_or_an_agent_move_it_or_anything_close_a_flag()
    {
        using var app = App();
        foreach (var to in TraceFindings.SecurityAnswers)
        {
            Assert.Throws<StangentException>(() => StateChanges.Move(app.Root, StateKinds.FindingSecurity, "L1", "unanswered", to, StateActor.Code("x"), "no"));
            Assert.Throws<StangentException>(() => StateChanges.Move(app.Root, StateKinds.FindingSecurity, "L1", "unanswered", to, StateActor.Agent("x"), "no"));
            Assert.Throws<StangentException>(() => StateChanges.Move(app.Root, StateKinds.FindingSecurity, "L1", to, "unflagged", StateActor.Person("x"), "no"));
        }
        Assert.Throws<StangentException>(() => StateChanges.Move(app.Root, StateKinds.FindingSecurity, "L1", "unanswered", "unflagged", StateActor.Code("x"), "no"));
        Assert.Throws<StangentException>(() => StateChanges.Move(app.Root, StateKinds.FindingSecurity, "L1", "unflagged", "unanswered", StateActor.Agent("x"), "no"));
    }
}

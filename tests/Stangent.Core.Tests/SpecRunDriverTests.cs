namespace Stangent.Core.Tests;

/// <summary>plugin/scripts/spec-run.sh with fake claude and stangent (D112). No test here can reach the real claude.</summary>
public sealed class SpecRunDriverTests : IDisposable
{
    const string Script = "plugin/scripts/spec-run.sh";
    const string Step = """{"ok":true,"result":{"status":"prepared","next":{"action":"write","agent":"stangent-page-writer","prompt":"Input: .stangent/in.md\nWrite your result to: .stangent/out.json","output":".stangent/out.json","schema":"page-writer-output.v1.json"}}}""";
    const string Done = """{"ok":true,"result":{"status":"written"}}""";
    const string Off = """{"ok":true,"result":{"status":"off","next":{"action":"none"}}}""";
    const string Current = """{"ok":true,"result":{"status":"current","next":{"action":"none"}}}""";
    const string Disprover = """{"ok":true,"result":{"status":"confirm","next":{"action":"confirm","agent":"stangent-finding-disprover","prompt":"Finding and code: .stangent/in.md\nWrite your JSON to: .stangent/out.json","output":".stangent/out.json","schema":"finding-disprover-output.v1.json"}}}""";
    const string DisproveDone = """{"ok":true,"result":{"status":"recorded","next":{"action":"none"}}}""";
    const string Sections = """{"ok":true,"result":[{"doc":"d1","section":"s1"}]}""";

    readonly DriverRig rig = new();

    public SpecRunDriverTests() => rig.Put(".stangent/in.md", "INPUT-TEXT");
    public void Dispose() => rig.Dispose();

    void OnePage(string id = "p1") => rig.Stangent($"""
        "spec prepare {id}"*) echo '{Step}' ;;
        "spec check {id} --usage "*) echo '{Done}' ;;
        """);

    [Fact]
    public void Safety_the_rig_only_reaches_the_fakes()
    {
        if (OperatingSystem.IsWindows()) return;
        var probe = rig.Run(rig.WriteScript("probe.sh", """command -v claude; command -v stangent; echo "key=[${ANTHROPIC_API_KEY-unset}]"; echo "path=$PATH" """));
        var lines = probe.Out.Split('\n');
        Assert.Equal(Path.Combine(rig.Bin, "claude"), lines[0]);
        Assert.Equal(Path.Combine(rig.Bin, "stangent"), lines[1]);
        Assert.Equal("key=[unset]", lines[2]);
        Assert.Equal($"path={rig.Bin}:/usr/bin:/bin", lines[3]);
    }

    [Fact]
    public void Safety_the_fake_is_the_claude_a_driver_calls()
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage();
        Assert.Equal(0, rig.Run(Script, "p1").Exit);
        Assert.Single(rig.ClaudeCalls());
    }

    [Fact]
    public void A_page_runs_prepare_then_claude_with_its_input_then_check()
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage();
        var result = rig.Run(Script, "--model", "sonnet", "p1");

        Assert.Equal(0, result.Exit);
        Assert.Equal("written\tp1\n", result.Out);
        var claude = Assert.Single(rig.ClaudeCalls());
        Assert.Contains("--model sonnet", claude);
        Assert.Contains("--json-schema {\"type\":\"object\"}", claude);
        Assert.Contains("--no-session-persistence", claude);
        Assert.Contains("INPUT-TEXT", rig.ClaudeStdin);
        Assert.Equal("""{"done":true}""", File.ReadAllText(Path.Combine(rig.Work, ".stangent/out.json")).Trim());
        Assert.Equal(["stangent spec prepare p1", "stangent spec schema page-writer-output.v1.json"], rig.StangentCalls().Take(2));
        Assert.StartsWith("stangent spec check p1 --usage ", rig.StangentCalls()[2]);
    }

    [Fact]
    public void A_failed_check_gets_the_retry_it_asks_for()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "spec prepare p1"*) echo '{{{Step}}}' ;;
            "spec check p1 --usage "*)
              if [ -f "$FAKE_DIR/checked" ]; then echo '{{{Done}}}'; else touch "$FAKE_DIR/checked"; echo '{{{Step}}}'; exit 1; fi ;;
            """);
        var result = rig.Run(Script, "p1");
        Assert.Equal(2, rig.ClaudeCalls().Count);
        Assert.Equal("written\tp1\n", result.Out);
    }

    [Fact]
    public void Retry_failed_reaches_prepare()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($"""
            "spec prepare p1 --retry-failed") echo '{Done}' ;;
            """);
        Assert.Equal("written\tp1\n", rig.Run(Script, "--retry-failed", "p1").Out);
    }

    [Fact]
    public void A_page_with_nothing_to_do_makes_no_AI_call()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent("""
            "spec prepare p1") echo '{"ok":true,"result":{"status":"current"}}' ;;
            """);
        var result = rig.Run(Script, "p1");
        Assert.Equal("current\tp1\n", result.Out);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void All_with_jobs_runs_every_page_from_the_list()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "spec pages") echo '{"ok":true,"result":{"pages":[{"id":"a"},{"id":"ANY /api/x"},{"id":"c"}]}}' ;;
            "spec prepare "*) echo '{{{Done}}}' ;;
            """);
        var result = rig.Run(Script, "--jobs", "2", "--all");
        Assert.Equal(0, result.Exit);
        Assert.Equal(["current", "written\tANY /api/x", "written\ta", "written\tc"], new[] { "current" }.Concat(result.Out.Trim().Split('\n').Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void Stale_runs_stale_and_new_pages_and_nothing_stale_means_no_AI_call()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent("""
            "spec stale") echo '{"ok":true,"result":{"stale":[],"new":[]}}' ;;
            """);
        var none = rig.Run(Script, "--stale");
        Assert.Equal(0, none.Exit);
        Assert.Empty(none.Out);
        Assert.Empty(rig.ClaudeCalls());

        rig.Stangent($$$"""
            "spec stale") echo '{"ok":true,"result":{"stale":[{"id":"old"}],"new":[{"id":"fresh"}]}}' ;;
            "spec prepare "*) echo '{{{Done}}}' ;;
            """);
        Assert.Equal(2, rig.Run(Script, "--stale").Out.Trim().Split('\n').Length);
    }

    [Theory]
    [InlineData("--fsd", "--chapter", "2")]
    [InlineData("--disprove", "--chapter", "2")]
    public void A_chapter_limits_a_run_to_what_stangent_lists_for_it_whichever_way_round_it_comes(string flag, string chapter, string n)
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "fsd sections --doc d1 --chapter 2") echo '{"ok":true,"result":[{"doc":"d1","section":"2.1"}]}' ;;
            "fsd prepare d1 2.1") echo '{{{Step}}}' ;;
            "fsd check d1 2.1 --usage "*) echo '{{{Done}}}' ;;
            "fsd disprove prepare d1 2.1") echo '{{{Disprover}}}' ;;
            "fsd disprove check d1 2.1 --usage "*) echo '{{{DisproveDone}}}' ;;
            """);
        var first = rig.Run(Script, flag, "d1", chapter, n);
        var calls = rig.ClaudeCalls().Count;
        Assert.Equal(0, first.Exit);
        Assert.Contains("2.1", first.Out);
        Assert.DoesNotContain("1.1", first.Out);
        Assert.True(calls >= 1);
        var second = rig.Run(Script, chapter, n, flag, "d1");
        Assert.Equal((0, first.Out), (second.Exit, second.Out));
    }

    [Fact]
    public void A_chapter_stangent_doesnt_know_stops_the_run_and_calls_no_claude()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent("""
            "fsd sections --doc d1 --chapter 9") echo '{"ok":false,"error":{"code":"not_found"}}'; exit 3 ;;
            """);
        var result = rig.Run(Script, "--fsd", "d1", "--chapter", "9");
        Assert.Equal(1, result.Exit);
        Assert.Contains("not_found", result.Err);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Theory]
    [InlineData("--chapter", "2", "p1")]
    [InlineData("--req", "d1", "--chapter", "2")]
    public void A_chapter_is_only_for_fsd_and_disprove(params string[] args)
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent("""
            "req sections --doc d1") echo '{"ok":true,"result":[]}' ;;
            """);
        Assert.Equal(2, rig.Run(Script, args).Exit);
    }

    [Fact]
    public void A_chapter_with_no_number_is_usage()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(2, rig.Run(Script, "--fsd", "d1", "--chapter").Exit);
    }

    [Theory]
    [InlineData("fsd", "--fsd", "fsd sections --doc d1")]
    [InlineData("req", "--req", "req sections --doc d1")]
    public void Fsd_and_req_run_only_the_sections_asked_for(string mode, string flag, string list)
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "{{{list}}}") echo '{"ok":true,"result":[{"doc":"d1","section":"s1"},{"doc":"d1","section":"s 2"}]}' ;;
            "{{{mode}}} prepare d1 s 2") echo '{{{Step}}}' ;;
            "{{{mode}}} check d1 s 2 --usage "*) echo '{{{Done}}}' ;;
            "fsd disprove prepare d1 s 2") echo '{{{Off}}}' ;;
            """);
        var result = rig.Run(Script, flag, "d1", "--only", "s 2");
        Assert.Equal(0, result.Exit);
        Assert.Equal("written\td1\ts 2\n", result.Out);
        Assert.Single(rig.ClaudeCalls());
    }

    [Fact]
    public void The_disprove_stage_follows_the_links_when_it_is_on()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "fsd sections --doc d1") echo '{{{Sections}}}' ;;
            "fsd prepare d1 s1") echo '{{{Current}}}' ;;
            "fsd disprove prepare d1 s1") echo '{{{Disprover}}}' ;;
            "fsd disprove check d1 s1 --usage "*) echo '{{{DisproveDone}}}' ;;
            """);
        var result = rig.Run(Script, "--fsd", "d1");
        Assert.Equal(0, result.Exit);
        Assert.Equal("current\td1\ts1\ndisprove:recorded\td1\ts1\n", result.Out);
        var claude = Assert.Single(rig.ClaudeCalls());
        Assert.Contains("--tools ", claude);
        Assert.Contains("INPUT-TEXT", rig.ClaudeStdin);
        Assert.Contains("stangent spec schema finding-disprover-output.v1.json", rig.StangentCalls());
    }

    [Fact]
    public void Off_the_stage_prints_nothing_and_calls_nothing()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "fsd sections --doc d1") echo '{{{Sections}}}' ;;
            "fsd prepare d1 s1") echo '{{{Current}}}' ;;
            "fsd disprove prepare d1 s1") echo '{{{Off}}}' ;;
            """);
        var result = rig.Run(Script, "--fsd", "d1");
        Assert.Equal("current\td1\ts1\n", result.Out);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void Ask_runs_one_turn_and_prints_the_status_then_the_result()
    {
        if (OperatingSystem.IsWindows()) return;
        const string asker = """{"ok":true,"result":{"status":"confirm","next":{"action":"confirm","agent":"stangent-page-asker","prompt":"Question and pages: .stangent/in.md\nWrite your JSON to: .stangent/out.json","output":".stangent/out.json","schema":"page-asker-output.v1.json"}}}""";
        const string answered = """{"ok":true,"result":{"status":"answered","next":{"action":"none"},"answer":{"label":"as the pages describe it"}}}""";
        rig.Stangent($$$"""
            "spec ask how does it work") echo '{{{asker}}}' ;;
            "spec ask-check how does it work --usage "*) echo '{{{answered}}}' ;;
            "spec schema "*) echo '{"type":"object"}' ;;
            """);
        var result = rig.Run(Script, "--ask", "how does it work");
        Assert.Equal(0, result.Exit);
        Assert.StartsWith("answered\thow does it work\n", result.Out);
        Assert.Contains("\"label\":\"as the pages describe it\"", result.Out);
        Assert.Contains("--json-schema", Assert.Single(rig.ClaudeCalls()));
        Assert.Contains("INPUT-TEXT", rig.ClaudeStdin);
    }

    [Fact]
    public void Ask_with_it_off_or_nothing_to_ask_makes_no_AI_call()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "spec ask how does it work") echo '{{{Off}}}' ;;
            """);
        var result = rig.Run(Script, "--ask", "how does it work");
        Assert.StartsWith("off\thow does it work\n", result.Out);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void Disprove_alone_never_touches_the_links()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "fsd sections --doc d1") echo '{{{Sections}}}' ;;
            "fsd disprove prepare d1 s1") echo '{{{Disprover}}}' ;;
            "fsd disprove check d1 s1 --usage "*) echo '{{{DisproveDone}}}' ;;
            """);
        var result = rig.Run(Script, "--disprove", "d1");
        Assert.Equal("disprove:recorded\td1\ts1\n", result.Out);
        Assert.DoesNotContain(rig.StangentCalls(), c => c.StartsWith("stangent fsd prepare", StringComparison.Ordinal));
    }

    [Fact]
    public void Agents_mode_uses_the_plugins_subagent_with_rules_for_stangents_folder_only()
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage();
        Assert.Equal("written\tp1\n", rig.Run(Script, "--agents", "p1").Out);
        var claude = Assert.Single(rig.ClaudeCalls());
        Assert.Contains("--agent stangent:stangent-page-writer", claude);
        Assert.Contains("--permission-mode dontAsk", claude);
        Assert.Contains($"Read(/{rig.RealPath(rig.Work)}/.stangent/**)", claude);
    }

    [Theory]
    [InlineData("""{"type":"result","is_error":true,"result":"no login"}""", 0, "no login")]
    [InlineData("not json", 1, "claude exited without a result")]
    public void Claude_failing_stops_the_batch_says_which_page_and_records_nothing(string answer, int exit, string said)
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($"""
            "spec prepare "*) echo '{Step}' ;;
            """);
        rig.ClaudeAnswers(answer, exit);
        var result = rig.Run(Script, "p1", "p2");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains($"error\tp1\t{said}", result.Out);
        Assert.DoesNotContain(rig.StangentCalls(), c => c.Contains(" check "));
        Assert.DoesNotContain("stangent spec prepare p2", rig.StangentCalls());
    }

    [Theory]
    [InlineData("--all", "spec pages")]
    [InlineData("--stale", "spec stale")]
    public void A_list_that_fails_is_an_error_not_an_empty_list(string flag, string list)
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "{{{list}}}") echo '{"ok":false,"error":{"code":"no_graph","message":"No code graph yet."}}'; exit 3 ;;
            """);
        var result = rig.Run(Script, flag);
        Assert.NotEqual(0, result.Exit);
        Assert.Contains(list, result.Err);
        Assert.Contains("no_graph", result.Err);
    }

    [Theory]
    [InlineData("exit 134")]
    [InlineData("echo 'Unhandled exception'; exit 134")]
    public void A_prepare_that_crashes_stops_without_calling_claude(string crash)
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($"""
            "spec prepare p1") {crash} ;;
            """);
        var result = rig.Run(Script, "p1");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("error\tp1\tstangent prepare gave no result", result.Out);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void A_check_that_crashes_stops_and_says_so()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($"""
            "spec prepare p1") echo '{Step}' ;;
            "spec check p1 --usage "*) exit 134 ;;
            """);
        var result = rig.Run(Script, "p1");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("error\tp1\tstangent check gave no result", result.Out);
        Assert.Single(rig.ClaudeCalls());
    }

    /// <summary>I.10: a refused page is a status line and the batch goes on, and the run exits 1 at the end.</summary>
    [Fact]
    public void A_page_stangent_refuses_is_reported_by_its_code_and_the_batch_goes_on()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "spec prepare p1") echo '{"ok":false,"error":{"code":"not_found","message":"no such page"}}'; exit 3 ;;
            "spec prepare p2") echo '{{{Done}}}' ;;
            """);
        var result = rig.Run(Script, "p1", "p2");
        Assert.Equal(1, result.Exit);
        Assert.Equal("not_found\tp1\nwritten\tp2\n", result.Out);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Theory]
    [InlineData]
    [InlineData("--jobs", "2")]
    [InlineData("--jobs")]
    [InlineData("--jobs", "many", "p1")]
    [InlineData("--jobs", "0", "p1")]
    [InlineData("--model")]
    [InlineData("--fsd")]
    [InlineData("--bogus", "p1")]
    public void Bad_arguments_give_usage_and_run_nothing(params string[] args)
    {
        if (OperatingSystem.IsWindows()) return;
        var result = rig.Run(Script, args);
        Assert.Equal(ExitCodes.Usage, result.Exit);
        Assert.Contains("spec-run.sh [--jobs N]", result.Err);
        Assert.Empty(rig.Calls());
    }
}

/// <summary>The hard stop (T.2, I.10): cost-guard.sh through spec-run.sh, with fakes only.</summary>
public sealed class CostGuardDriverTests : IDisposable
{
    const string Script = "plugin/scripts/spec-run.sh";
    const string Step = """{"ok":true,"result":{"status":"prepared","next":{"action":"write","agent":"w","prompt":"Input: .stangent/in.md\nWrite your result to: .stangent/out.json","output":".stangent/out.json","schema":"s.json"}}}""";
    const string Done = """{"ok":true,"result":{"status":"written"}}""";
    const string Paid = """{"type":"result","is_error":false,"total_cost_usd":0.6,"structured_output":{"done":true}}""";

    readonly DriverRig rig = new();

    public CostGuardDriverTests()
    {
        rig.Put(".stangent/in.md", "IN");
        rig.Stangent($"""
            "spec prepare "*) echo '{Step}' ;;
            "spec check "*) echo '{Done}' ;;
            "cost estimate "*) echo '{Estimate}' ;;
            """);
    }

    const string Estimate = """{"ok":true,"result":{"estimateUsd":null,"basis":"none yet"}}""";

    public void Dispose() => rig.Dispose();

    [Fact]
    public void No_choice_of_cap_stops_before_the_first_paid_call_and_says_what_to_pass()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Env["STANGENT_MAX_COST"] = "";
        var result = rig.Run(Script, "p1");
        Assert.Equal(2, result.Exit);
        Assert.Empty(rig.ClaudeCalls());
        Assert.Contains("--max-cost", result.Err);
        Assert.Contains("stopped\tp1\tno_cap", result.Out);
    }

    [Fact]
    public void None_is_a_choice_and_runs_with_no_budget_flag()
    {
        if (OperatingSystem.IsWindows()) return;
        var result = rig.Run(Script, "--max-cost", "none", "p1");
        Assert.Equal(0, result.Exit);
        Assert.DoesNotContain("--max-budget-usd", Assert.Single(rig.ClaudeCalls()));
    }

    [Fact]
    public void Each_call_gets_what_is_left_and_the_run_stops_when_the_next_could_go_over()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.ClaudeAnswers(Paid);
        var result = rig.Run(Script, "--max-cost", "1", "p1", "p2", "p3");
        Assert.Equal(5, result.Exit);
        // $0.60 spent leaves $0.40, less than the run's average call, so the second doesn't start.
        Assert.Contains("--max-budget-usd 1.0000", Assert.Single(rig.ClaudeCalls()));
        Assert.Contains("stopped\tp", result.Out);
        Assert.Contains("cap_reached", result.Err);
        Assert.Contains("spent: ", result.Err);
        Assert.Contains("To resume, run the same command again", result.Err);
        Assert.Contains("spec-run.sh", result.Err);
    }

    [Fact]
    public void An_estimate_over_the_cap_stops_with_nothing_spent()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent("""
            "cost estimate "*) echo '{"ok":true,"result":{"estimateUsd":9.5,"basis":"19 earlier calls"}}' ;;
            """);
        var result = rig.Run(Script, "--max-cost", "1", "p1");
        Assert.Equal(5, result.Exit);
        Assert.Empty(rig.ClaudeCalls());
        Assert.Contains("over_estimate", result.Err);
        Assert.Contains("about $9.5 (19 earlier calls)", result.Err);
    }

    [Fact]
    public void Estimate_prints_and_stops_with_no_AI_call()
    {
        if (OperatingSystem.IsWindows()) return;
        var result = rig.Run(Script, "--estimate", "p1", "p2");
        Assert.Equal(0, result.Exit);
        Assert.Empty(rig.ClaudeCalls());
        Assert.Contains("estimate: at least 4 AI calls, cost unknown (none yet)", result.Err);
    }

    [Theory]
    [InlineData("""{"type":"result","is_error":true,"result":"You've hit your weekly limit · resets Nov 7"}""")]
    [InlineData("""{"type":"result","is_error":true,"result":"Claude usage limit reached"}""")]
    public void A_limit_message_stops_cleanly_and_says_how_to_resume(string answer)
    {
        if (OperatingSystem.IsWindows()) return;
        rig.ClaudeAnswers(answer, exit: 1);
        var result = rig.Run(Script, "--max-cost", "5", "p1", "p2");
        Assert.Equal(5, result.Exit);
        Assert.Single(rig.ClaudeCalls());
        Assert.Contains("usage_limit", result.Err);
        Assert.Contains("Claude Code said:", result.Err);
        Assert.Contains("To resume", result.Err);
        Assert.DoesNotContain(rig.StangentCalls(), c => c.StartsWith("stangent spec check"));
    }

    [Fact]
    public void A_refused_rate_limit_event_stops_even_with_no_words_to_match()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.ClaudeAnswers("""
            {"type":"rate_limit_event","rate_limit_info":{"status":"rejected","rateLimitType":"seven_day","resetsAt":1794045600}}
            {"type":"result","is_error":true,"result":"stopped"}
            """, exit: 1);
        var result = rig.Run(Script, "--max-cost", "none", "p1");
        Assert.Equal(5, result.Exit);
        Assert.Contains("seven_day limit, which resets 2026-", result.Err);
    }

    [Fact]
    public void A_page_about_rate_limits_is_not_a_limit()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.ClaudeAnswers("""{"type":"result","is_error":false,"result":"the rate limit middleware","structured_output":{"done":true}}""");
        Assert.Equal(0, rig.Run(Script, "--max-cost", "none", "p1").Exit);
    }

    [Fact]
    public void Another_failure_is_not_called_a_limit_and_still_stops_the_batch()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.ClaudeAnswers("""{"type":"result","is_error":true,"result":"no login"}""", exit: 1);
        var result = rig.Run(Script, "--max-cost", "none", "p1", "p2");
        Assert.NotEqual(0, result.Exit);
        Assert.NotEqual(5, result.Exit);
        Assert.Contains("error\tp1\tno login", result.Out);
    }

    [Fact]
    public void Usage_a_call_reports_is_shown_and_a_persons_stop_at_usage_stops_before_the_next_call()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.ClaudeAnswers("""
            {"type":"rate_limit_event","rate_limit_info":{"status":"allowed_warning","rateLimitType":"seven_day","utilization":0.94,"resetsAt":1794045600}}
            {"type":"result","is_error":false,"structured_output":{"done":true}}
            """);
        var result = rig.Run(Script, "--max-cost", "none", "--stop-at-usage", "90", "p1", "p2");
        Assert.Equal(5, result.Exit);
        Assert.Single(rig.ClaudeCalls());
        Assert.Contains("usage: allowed_warning, seven_day window 94% used", result.Err);
        Assert.Contains("--stop-at-usage 90", result.Err);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("lots")]
    public void A_bad_cap_is_a_usage_error_and_runs_nothing(string cap)
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(ExitCodes.Usage, rig.Run(Script, "--max-cost", cap, "p1").Exit);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void Parallel_jobs_cannot_each_spend_the_whole_cap()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.ClaudeAnswers("""{"type":"result","is_error":false,"total_cost_usd":0.1,"structured_output":{"done":true}}""");
        var result = rig.Run(Script, "--max-cost", "1", "--jobs", "4", "p1", "p2", "p3", "p4");
        var budgets = rig.ClaudeCalls().Select(c => double.Parse(c.Split("--max-budget-usd ")[1].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        // Held money is held: whatever the timing, no call is promised more than a share of what isn't held by others.
        Assert.All(budgets.Take(1), b => Assert.True(b <= 0.25 + 0.0001, $"{b}"));
    }

    [Fact]
    public void A_refused_page_exits_one_and_the_others_still_run()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "spec prepare p1") echo '{"ok":false,"error":{"code":"not_found","message":"no"}}'; exit 3 ;;
            "spec prepare p2") echo '{{{Step}}}' ;;
            "spec check "*) echo '{{{Done}}}' ;;
            """);
        var result = rig.Run(Script, "p1", "p2");
        Assert.Equal(1, result.Exit);
        Assert.Contains("not_found\tp1", result.Out);
        Assert.Contains("written\tp2", result.Out);
        Assert.Contains("refused or failed: p1", result.Err);
    }

    [Fact]
    public void No_stangent_command_starts_Claude_Code_only_the_drivers_do()
    {
        // Why benchmark/run.sh, which runs only stangent commands, has no cap of its own.
        var src = Path.Combine(DriverRig.RepoRoot, "src");
        var starts = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f) is var t && (t.Contains("ProcessStartInfo(\"claude\"") || t.Contains("FileName = \"claude\"")))
            .ToList();
        Assert.Empty(starts);
    }
}

/// <summary>The estimate itself: telemetry only.</summary>
public sealed class CostHistoryTests : IDisposable
{
    readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("stangent-cost");
    public void Dispose() => dir.Delete(true);

    [Fact]
    public void An_estimate_is_calls_times_this_steps_average_and_no_history_says_so()
    {
        var none = Stangent.Core.Telemetry.CostHistory.Estimate(dir.FullName, "spec check", 10);
        Assert.Null(none["estimateUsd"]);
        Assert.Contains("no earlier", (string)none["basis"]!);

        var telemetry = Directory.CreateDirectory(Path.Combine(dir.FullName, ".stangent", "telemetry")).FullName;
        File.WriteAllLines(Path.Combine(telemetry, "d.jsonl"),
        [
            """{"attributes":{"stangent.command":"spec check","gen_ai.usage.cost_usd":0.1}}""",
            """{"attributes":{"stangent.command":"spec check","gen_ai.usage.cost_usd":0.3}}""",
            """{"attributes":{"stangent.command":"ticket check","gen_ai.usage.cost_usd":9}}""",
            """{"attributes":{"stangent.command":"spec check"}}""",
            "not json gen_ai.usage.cost_usd",
        ]);
        var some = Stangent.Core.Telemetry.CostHistory.Estimate(dir.FullName, "spec check", 10);
        Assert.Equal(2.0, (double)some["estimateUsd"]!);
        Assert.Equal(2, (int)some["earlierCalls"]!);
    }
}

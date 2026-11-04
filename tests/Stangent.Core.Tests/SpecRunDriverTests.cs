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
    [InlineData("""{"type":"result","is_error":true,"result":"usage limit hit"}""", 0, "usage limit hit")]
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

    /// <summary>Pins today's behaviour, a known gap: a refused page is a status line, so the batch still exits 0.</summary>
    [Fact]
    public void A_page_stangent_refuses_is_reported_by_its_code_and_the_batch_goes_on()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($$$"""
            "spec prepare p1") echo '{"ok":false,"error":{"code":"not_found","message":"no such page"}}'; exit 3 ;;
            "spec prepare p2") echo '{{{Done}}}' ;;
            """);
        var result = rig.Run(Script, "p1", "p2");
        Assert.Equal(0, result.Exit);
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

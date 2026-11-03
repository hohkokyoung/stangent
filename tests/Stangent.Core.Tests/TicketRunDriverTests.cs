namespace Stangent.Core.Tests;

/// <summary>plugin/scripts/ticket-run.sh with fake claude and stangent (D112).</summary>
public sealed class TicketRunDriverTests : IDisposable
{
    const string Script = "plugin/scripts/ticket-run.sh";
    const string Ok = """{"ok":true,"result":{}}""";
    const string NoAi = """{"ok":true,"result":{"ai":{"status":"none"}}}""";
    const string Written = """{"ok":true,"result":{"status":"written"}}""";
    const string Writer = """{"ok":true,"result":{"status":"prepared","next":{"action":"write","agent":"w","prompt":"Input: .stangent/in.md\nWrite your result to: .stangent/out.json","output":".stangent/out.json","schema":"s.json"}}}""";
    const string Limits = """{"permissionMode":"dontAsk","maxTurns":7,"maxBudgetUsd":1,"allowedTools":["Read","Bash(dotnet test *)"]}""";
    const string Reproducer = """{"ok":true,"result":{"status":"prepared","next":{"action":"reproduce","agent":"stangent-bug-reproducer","prompt":"Reproduce it.","limits":""" + Limits + "}}}";

    const string Needed = """{"ok":true,"result":{"ai":{"status":"needed"}}}""";
    const string FinderPrepare = """
        echo "{\"ok\":true,\"result\":{\"status\":\"prepared\",\"next\":{\"action\":\"search\",\"agent\":\"f\",\"prompt\":\"Input: .stangent/in.md\",\"output\":\".stangent/out.json\",\"schema\":\"s.json\",\"limits\":{\"root\":\"/$(pwd -P)\",\"tools\":\"Read,Grep,Glob\",\"allowedTools\":[\"Grep\",\"Read(/$(pwd -P)/**)\"],\"permissionMode\":\"dontAsk\",\"maxTurns\":5,\"maxBudgetUsd\":1}}}}" ;;
        """;

    readonly DriverRig rig = new();

    public TicketRunDriverTests() => rig.Put(".stangent/in.md", "TICKET-TEXT");
    public void Dispose() => rig.Dispose();

    /// <summary>The steps every ticket takes, plus whatever a test adds in front.</summary>
    void Steps(string more = "") => rig.Stangent($"""
        {more}
        "ticket pull K-1") echo '{Ok}' ;;
        "ticket points K-1"*) echo '{NoAi}' ;;
        "ticket bundle K-1") echo '{Ok}' ;;
        """);

    void IsBug(bool bug) => rig.Put(".stangent/tickets/K-1/ticket.json", bug ? """{"isBug":true}""" : """{"isBug":false}""");

    [Fact]
    public void A_ticket_pulls_finds_points_and_bundles_with_no_AI_call_when_none_is_needed()
    {
        if (OperatingSystem.IsWindows()) return;
        Steps();
        var result = rig.Run(Script, "K-1");
        Assert.Equal(0, result.Exit);
        Assert.Equal("pulled\tK-1\tpull\nbuilt\tK-1\tbundle\n", result.Out);
        Assert.Equal(["stangent ticket pull K-1", "stangent ticket points K-1", "stangent ticket bundle K-1"], rig.StangentCalls());
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void No_pull_and_refind_reach_stangent()
    {
        if (OperatingSystem.IsWindows()) return;
        Steps();
        Assert.Equal(0, rig.Run(Script, "--no-pull", "--refind", "K-1").Exit);
        Assert.Equal(["stangent ticket points K-1 --refind", "stangent ticket bundle K-1"], rig.StangentCalls());
    }

    [Fact]
    public void A_failed_pull_fails_says_why_and_stops_that_ticket()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent("""
            "ticket pull K-1") echo '{"ok":false,"error":{"code":"jira_down","message":"Jira did not answer"}}'; exit 1 ;;
            """);
        var result = rig.Run(Script, "K-1");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("jira_down\tK-1\tpull", result.Out);
        Assert.Contains("error\tK-1\tpull\tJira did not answer", result.Err);
        Assert.Single(rig.StangentCalls());
    }

    [Fact]
    public void A_failed_bundle_fails_and_no_paid_step_runs_after_it()
    {
        if (OperatingSystem.IsWindows()) return;
        IsBug(true);
        rig.Stangent($$$"""
            "ticket pull K-1") echo '{{{Ok}}}' ;;
            "ticket points K-1") echo '{{{NoAi}}}' ;;
            "ticket bundle K-1") echo '{"ok":false,"error":{"code":"no_graph","message":"No code graph yet."}}'; exit 3 ;;
            """);
        var result = rig.Run(Script, "--prove", "K-1");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("no_graph\tK-1\tbundle", result.Out);
        Assert.Contains("error\tK-1\tbundle\tNo code graph yet.", result.Err);
        Assert.DoesNotContain(rig.StangentCalls(), c => c.Contains("ticket prove"));
        Assert.Empty(rig.ClaudeCalls());
    }

    string FinderSteps() => $$$"""
        "ticket pull K-1") echo '{{{Ok}}}' ;;
        "ticket points K-1"*) echo '{{{Needed}}}' ;;
        "ticket prepare K-1 --step points")
        {{{FinderPrepare}}}
        "ticket check K-1 --step points --usage "*) echo '{{{Written}}}' ;;
        "ticket bundle K-1") echo '{{{Ok}}}' ;;
        """;

    [Fact]
    public void The_file_finder_searches_a_throwaway_worktree_under_the_limits_given()
    {
        if (OperatingSystem.IsWindows()) return;
        var repo = rig.NewRepo("repo");
        rig.Put(Path.Combine(repo, ".stangent/in.md"), "TICKET-TEXT");
        rig.Stangent(FinderSteps());

        var result = rig.RunIn(repo, Script, "K-1");

        Assert.Equal(0, result.Exit);
        Assert.Equal("pulled\tK-1\tpull\nwritten\tK-1\tpoints\nbuilt\tK-1\tbundle\n", result.Out);
        var realRepo = rig.RealPath(repo);
        var cwd = Assert.Single(rig.Calls(), c => c.StartsWith("claude-cwd "));
        Assert.DoesNotContain(realRepo, cwd);
        var claude = Assert.Single(rig.ClaudeCalls());
        Assert.DoesNotContain($"Read(/{realRepo}/", claude);
        Assert.Contains("--max-turns 5", claude);
        Assert.Contains("TICKET-TEXT", rig.ClaudeStdin);
        Assert.Equal("""{"done":true}""", File.ReadAllText(Path.Combine(repo, ".stangent/out.json")).Trim());
        // The worktree is gone and the conversation is kept.
        Assert.Single(rig.Git(repo, "worktree", "list").Split('\n'));
        Assert.Single(Directory.GetFiles(Path.Combine(repo, ".stangent/tickets/K-1/transcripts")));
    }

    [Fact]
    public void Outside_git_the_file_finder_searches_the_folder_itself()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent(FinderSteps());
        Assert.Equal(0, rig.Run(Script, "--no-pull", "K-1").Exit);
        Assert.Equal($"claude-cwd {rig.RealPath(rig.Work)}", Assert.Single(rig.Calls(), c => c.StartsWith("claude-cwd ")));
    }

    [Fact]
    public void No_find_skips_the_file_finder_even_when_needed()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent(FinderSteps());
        Assert.Equal(0, rig.Run(Script, "--no-find", "K-1").Exit);
        Assert.Empty(rig.ClaudeCalls());
        Assert.DoesNotContain(rig.StangentCalls(), c => c.Contains("ticket prepare"));
    }

    [Fact]
    public void Prove_runs_the_reproducer_on_a_bug_under_its_caps()
    {
        if (OperatingSystem.IsWindows()) return;
        IsBug(true);
        Steps($"""
            "ticket prove K-1 --hint look at the cart") echo '{Reproducer}' ;;
            "ticket check K-1 --step prove --usage "*) cp "$7" "$FAKE_DIR/usage"; echo '{Written}' ;;
            """);
        var result = rig.Run(Script, "--prove", "--hint", "look at the cart", "K-1");

        Assert.Equal(0, result.Exit);
        Assert.Contains("written\tK-1\tprove", result.Out);
        var claude = Assert.Single(rig.ClaudeCalls());
        Assert.Contains("--agent stangent:stangent-bug-reproducer", claude);
        Assert.Contains("--max-turns 7", claude);
        Assert.Contains("--allowedTools Read Bash(dotnet test *)", claude);
        // The usage handed to check counts rounds from the conversation, which is kept.
        Assert.Contains("\"rounds\":1", File.ReadAllText(Path.Combine(rig.Dir, "usage")));
        Assert.Single(Directory.GetFiles(Path.Combine(rig.Work, ".stangent/tickets/K-1/prove"), "transcript-*.jsonl"));
    }

    [Fact]
    public void Prove_on_a_ticket_that_isnt_a_bug_makes_no_AI_call()
    {
        if (OperatingSystem.IsWindows()) return;
        IsBug(false);
        Steps();
        Assert.Equal(0, rig.Run(Script, "--prove", "K-1").Exit);
        Assert.DoesNotContain(rig.StangentCalls(), c => c.Contains("ticket prove"));
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void A_reproducer_that_hits_its_cap_still_goes_to_check()
    {
        if (OperatingSystem.IsWindows()) return;
        IsBug(true);
        Steps($"""
            "ticket prove K-1") echo '{Reproducer}' ;;
            "ticket check K-1 --step prove --usage "*) echo '{Written}' ;;
            """);
        rig.ClaudeAnswers("""{"type":"result","is_error":true,"subtype":"error_max_turns"}""");
        Assert.Contains("written\tK-1\tprove", rig.Run(Script, "--prove", "K-1").Out);
    }

    [Fact]
    public void Claude_failing_stops_the_batch_and_records_nothing()
    {
        if (OperatingSystem.IsWindows()) return;
        IsBug(true);
        Steps($"""
            "ticket prove K-1") echo '{Reproducer}' ;;
            """);
        rig.ClaudeAnswers("""{"type":"result","is_error":true,"result":"no login"}""");
        var result = rig.Run(Script, "--prove", "K-1", "K-2");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("error\tK-1\tprove\tno login", result.Out);
        Assert.DoesNotContain(rig.StangentCalls(), c => c.Contains("--step prove") || c.Contains("K-2"));
    }

    [Fact]
    public void A_blocked_setup_stops_the_batch()
    {
        if (OperatingSystem.IsWindows()) return;
        IsBug(true);
        Steps($$$"""
            "ticket prove K-1") echo '{{{Reproducer}}}' ;;
            "ticket check K-1 --step prove --usage "*) echo '{"ok":false,"error":{"code":"setup_blocked","message":"rules refused dotnet"}}'; exit 1 ;;
            """);
        var result = rig.Run(Script, "--prove", "K-1", "K-2");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("error\tK-1\tprove\trules refused dotnet", result.Err);
        Assert.DoesNotContain(rig.StangentCalls(), c => c.Contains("K-2"));
    }

    [Fact]
    public void An_app_that_wont_start_stops_with_no_attempt()
    {
        if (OperatingSystem.IsWindows()) return;
        IsBug(true);
        Directory.CreateDirectory(Path.Combine(rig.Work, ".stangent/tickets/K-1/tools"));
        var step = """{"ok":true,"result":{"next":{"action":"reproduce","agent":"r","prompt":"p","limits":""" + Limits.TrimEnd('}') + ""","app":{"serve":true}}}}}""";
        Steps($$$"""
            "ticket prove K-1") echo '{{{step}}}' ;;
            "ticket serve K-1") echo 'port busy'; exit 1 ;;
            """);
        var result = rig.Run(Script, "--prove", "K-1");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("error\tK-1\tprove\tthe app did not start for the attempt: port busy", result.Err);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void A_step_that_crashes_stops_without_calling_claude()
    {
        if (OperatingSystem.IsWindows()) return;
        Steps("""
            "ticket draft K-1 --kind questions") exit 134 ;;
            """);
        var result = rig.Run(Script, "--questions", "K-1");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("error\tK-1\tquestions\tstangent gave no result", result.Out);
        Assert.Empty(rig.ClaudeCalls());
    }

    /// <summary>Pins today's behaviour, a known gap: a step stangent refuses is a status line and the run exits 0.</summary>
    [Fact]
    public void A_step_stangent_refuses_is_reported_by_its_code_and_the_ticket_goes_on()
    {
        if (OperatingSystem.IsWindows()) return;
        Steps("""
            "ticket match K-1") echo '{"ok":false,"error":{"code":"no_requirements","message":"none"}}'; exit 1 ;;
            """);
        var result = rig.Run(Script, "--match", "K-1");
        Assert.Equal(0, result.Exit);
        Assert.Contains("no_requirements\tK-1\tmatch", result.Out);
    }

    [Fact]
    public void Match_questions_and_resolution_each_run_their_step_with_their_arguments()
    {
        if (OperatingSystem.IsWindows()) return;
        Steps($"""
            "ticket match K-1") echo '{Writer}' ;;
            "ticket draft K-1 --kind questions --tone plain") echo '{Writer}' ;;
            "ticket draft K-1 --kind resolution --pr a..b --tone plain") echo '{Writer}' ;;
            "ticket check K-1 --step "*) echo '{Written}' ;;
            """);
        var result = rig.Run(Script, "--match", "--questions", "--resolution", "a..b", "--tone", "plain", "--model", "haiku", "K-1");
        Assert.Equal(0, result.Exit);
        foreach (var step in new[] { "match", "questions", "resolution" })
            Assert.Contains($"written\tK-1\t{step}", result.Out);
        Assert.Equal(3, rig.ClaudeCalls().Count);
        Assert.All(rig.ClaudeCalls(), c => Assert.Contains("--model haiku", c));
        Assert.All(rig.ClaudeCalls(), c => Assert.Contains("autoMemoryEnabled", c));
    }

    [Fact]
    public void Jobs_run_every_ticket_but_prove_runs_one_at_a_time()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent($"""
            "ticket pull "*) echo '{Ok}' ;;
            "ticket points "*) echo '{NoAi}' ;;
            "ticket bundle "*) echo '{Ok}' ;;
            """);
        var parallel = rig.Run(Script, "--jobs", "2", "K-1", "K-2", "K-3");
        Assert.Equal(6, parallel.Out.Trim().Split('\n').Length);
        Assert.DoesNotContain("one ticket at a time", parallel.Err);

        Assert.Contains("--prove runs one ticket at a time", rig.Run(Script, "--jobs", "2", "--prove", "K-1").Err);
    }

    [Theory]
    [InlineData]
    [InlineData("--prove")]
    [InlineData("--jobs")]
    [InlineData("--jobs", "x", "K-1")]
    [InlineData("--tone")]
    [InlineData("--bogus", "K-1")]
    public void Bad_arguments_give_usage_and_run_nothing(params string[] args)
    {
        if (OperatingSystem.IsWindows()) return;
        var result = rig.Run(Script, args);
        Assert.Equal(ExitCodes.Usage, result.Exit);
        Assert.Contains("ticket-run.sh [--jobs N]", result.Err);
        Assert.Empty(rig.Calls());
    }
}

namespace Stangent.Core.Tests;

/// <summary>plugin/scripts/fix-run.sh with fake claude and stangent (D112): the fix loop's driver (D350).</summary>
public sealed class FixRunDriverTests : IDisposable
{
    const string Script = "plugin/scripts/fix-run.sh";
    const string Started = """{"ok":true,"result":{"key":"K-1","branch":"stangent/fix-K-1","warnings":["the base moved"]}}""";

    readonly DriverRig rig = new();
    readonly string wt;

    public FixRunDriverTests()
    {
        rig.Put(".stangent/fix/K-1/work/input.md", "FIX-INPUT");
        wt = Path.GetDirectoryName(rig.Put(".stangent/fix/K-1/worktree/README.md", "wt"))!;
    }

    public void Dispose() => rig.Dispose();

    string Step(string status, string action, double budget = 1) => new System.Text.Json.Nodes.JsonObject
    {
        ["ok"] = status != "retry",
        ["result"] = new System.Text.Json.Nodes.JsonObject
        {
            ["key"] = "K-1", ["status"] = status,
            ["next"] = new System.Text.Json.Nodes.JsonObject
            {
                ["action"] = action, ["agent"] = "stangent-fixer", ["schema"] = "fixer-output.v1.json",
                ["input"] = ".stangent/fix/K-1/work/input.md", ["output"] = ".stangent/fix/K-1/work/output.json",
                ["limits"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["cwd"] = wt, ["tools"] = "Read,Grep,Glob,Edit,Write,Bash",
                    ["allowedTools"] = new System.Text.Json.Nodes.JsonArray($"Read(/{wt}/**)", $"Edit(/{wt}/**)", "Bash(dotnet build T.csproj:*)"),
                    ["disallowedTools"] = new System.Text.Json.Nodes.JsonArray($"Edit(/{wt}/T/Test.cs)"),
                    ["permissionMode"] = "dontAsk", ["maxTurns"] = 30, ["maxBudgetUsd"] = budget,
                },
            },
        },
    }.ToJsonString();

    static string Report(string reason) => "{\"ok\":true,\"result\":{\"key\":\"K-1\",\"stop\":{\"reason\":\"" + reason + "\",\"detail\":\"x\"}}}";

    void Steps(string checks, string reason = "fixed") => rig.Stangent($"""
        "fix start K-1") echo '{Started}' ;;
        "fix prepare K-1 --context files") echo '{Step("prepared", "run")}' ;;
        "fix prepare K-1 --context ticket") echo '{Step("prepared", "run", 0.6)}' ;;
        {checks}
        "fix report K-1") echo '{Report(reason)}' ;;
        """);

    [Fact]
    public void A_ticket_runs_the_fixer_in_its_worktree_under_its_caps_then_checks_and_reports()
    {
        if (OperatingSystem.IsWindows()) return;
        Steps($"""
            "fix check K-1 --usage "*) cp "$5" "$FAKE_DIR/usage"; echo '{Step("fixed", "none")}' ;;
            """);
        var result = rig.Run(Script, "K-1");

        Assert.Equal(0, result.Exit);
        Assert.Contains("started\tK-1\tstart", result.Out);
        Assert.Contains("fixed\tK-1\tcheck", result.Out);
        Assert.EndsWith("fixed\tK-1\tfix\n", result.Out);
        Assert.Contains("note: K-1: the base moved", result.Err);
        var claude = Assert.Single(rig.ClaudeCalls());
        Assert.DoesNotContain("--agent", claude);   // D354: an agent's tools list drops StructuredOutput
        Assert.DoesNotContain("--plugin-dir", claude);
        Assert.Contains("--model claude-sonnet-5-5 --effort medium", claude);   // D348, unless a flag says otherwise
        Assert.Contains("--max-turns 30 --max-budget-usd 1", claude);
        Assert.Contains($"--disallowedTools Edit(/{wt}/T/Test.cs)", claude);
        Assert.Contains("--json-schema", claude);
        Assert.Contains("--setting-sources  --settings {\"autoMemoryEnabled\":false}", claude);
        Assert.Contains("claude-cwd " + rig.RealPath(wt), rig.Calls());
        Assert.Equal("FIX-INPUT", rig.ClaudeStdin);                               // on stdin: it never reads stangent's folder
        // Its plan is written where the step wants it, and the usage handed to check has the rounds and the settings.
        Assert.Contains("\"done\":true", File.ReadAllText(Path.Combine(rig.Work, ".stangent/fix/K-1/work/output.json")));
        var usage = File.ReadAllText(Path.Combine(rig.Dir, "usage"));
        Assert.Contains("\"rounds\":1", usage);
        Assert.Contains("\"agent\":\"stangent-fixer\"", usage);
        Assert.Single(Directory.GetFiles(Path.Combine(rig.Work, ".stangent/fix/K-1/transcripts"), "fixer-*.jsonl"));
        Assert.Contains(rig.StangentCalls(), c => c == "stangent fix report K-1");
    }

    [Fact]
    public void A_failed_check_gets_one_retry_and_the_context_and_model_can_be_chosen()
    {
        if (OperatingSystem.IsWindows()) return;
        Steps($$"""
            "fix check K-1 --usage "*) n=$(cat "$FAKE_DIR/n" 2>/dev/null || echo 0); echo $((n + 1)) >"$FAKE_DIR/n"
              if [ "$n" = 0 ]; then echo '{{Step("retry", "run", 0.4)}}'; else echo '{{Step("check-failed", "none")}}'; fi ;;
            """, "check-failed");
        var result = rig.Run(Script, "--context", "ticket", "--model", "m2", "--effort", "high", "K-1");

        Assert.Equal(1, result.Exit);   // a ticket that isn't fixed is a failure the batch reports
        Assert.Contains("retry\tK-1\tcheck", result.Out);
        Assert.Contains("check-failed\tK-1\tfix", result.Out);
        var calls = rig.ClaudeCalls();
        Assert.Equal(2, calls.Count);
        Assert.Contains("--max-budget-usd 0.6", calls[0]);
        Assert.Contains("--max-budget-usd 0.4", calls[1]);   // the retry's cap is what check said is left
        Assert.All(calls, c => Assert.Contains("--model m2 --effort high", c));
    }

    [Fact]
    public void No_failing_test_stops_before_any_call_and_still_writes_the_report()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent("""
            "fix start K-1") echo '{"ok":false,"error":{"code":"no_failing_test","message":"no proof"}}' ;;
            "fix report K-1") echo '{"ok":true,"result":{"stop":{"reason":"no-failing-test"}}}' ;;
            """);
        var result = rig.Run(Script, "K-1");
        Assert.Equal(1, result.Exit);
        Assert.Contains("no_failing_test\tK-1\tstart", result.Out);
        Assert.Empty(rig.ClaudeCalls());
        Assert.Contains(rig.StangentCalls(), c => c == "stangent fix report K-1");
    }

    [Fact]
    public void With_no_cap_chosen_nothing_is_paid()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Env["STANGENT_MAX_COST"] = "";
        Steps("");
        var result = rig.Run(Script, "K-1");
        Assert.Equal(2, result.Exit);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void A_call_whose_cap_isnt_left_doesnt_start()
    {
        if (OperatingSystem.IsWindows()) return;
        Steps("");
        var result = rig.Run(Script, "--max-cost", "0.5", "K-1");
        Assert.Equal(5, result.Exit);
        Assert.Contains("stopped\tK-1", result.Out);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void The_driver_never_pushes_or_opens_a_pull_request()
    {
        var text = File.ReadAllText(Path.Combine(DriverRig.RepoRoot, Script));
        var code = string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
        Assert.DoesNotContain("git push", code);
        Assert.DoesNotContain("gh pr", code);
    }
}

namespace Stangent.Core.Tests;

/// <summary>D161 in the drivers: pinned settings on every call, a refusal when none are pinned, every one-turn call kept as
/// sent, and the repeat check. Fake claude and stangent only (D112).</summary>
public sealed class AiRunDriverTests : IDisposable
{
    const string Spec = "plugin/scripts/spec-run.sh";
    const string Tickets = "plugin/scripts/ticket-run.sh";
    const string Step = """{"ok":true,"result":{"status":"prepared","next":{"action":"write","agent":"stangent-page-writer","prompt":"Input: .stangent/in.md\nWrite your result to: .stangent/out.json","output":".stangent/out.json","schema":"page-writer-output.v1.json"}}}""";
    const string Done = """{"ok":true,"result":{"status":"written"}}""";

    readonly DriverRig rig = new();

    public AiRunDriverTests() => rig.Put(".stangent/in.md", "INPUT-TEXT");
    public void Dispose() => rig.Dispose();

    void OnePage(string settings = "") => rig.Stangent($$"""
        {{settings}}
        "spec prepare p1"*) echo '{{Step}}' ;;
        "spec check p1 --usage "*) cp "${@: -1}" "$FAKE_DIR/usage-seen"; echo '{{Done}}' ;;
        """);

    string Seen => File.ReadAllText(Path.Combine(rig.Dir, "usage-seen"));

    [Fact]
    public void Every_call_gets_the_steps_model_and_effort()
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage();
        Assert.Equal(0, rig.Run(Spec, "--effort", "high", "p1").Exit);
        Assert.Contains("stangent ai settings stangent-page-writer --effort high", rig.Calls());
        var claude = Assert.Single(rig.ClaudeCalls());
        Assert.Contains("--model fake-model", claude);
        Assert.Contains("--effort high", claude);
    }

    [Fact]
    public void No_settings_pinned_stops_the_run_before_any_call()
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage("""
            "ai settings "*) echo '{"ok":false,"error":{"code":"ai_settings_missing","message":"No ai.model or ai.effort for stangent-page-writer"}}'; exit 2 ;;
            """);
        var run = rig.Run(Spec, "--max-cost", "none", "p1");
        Assert.NotEqual(0, run.Exit);
        Assert.Empty(rig.ClaudeCalls());
        Assert.Contains("error\tp1\tNo ai.model or ai.effort for stangent-page-writer", run.Out);
        Assert.DoesNotContain(rig.Calls(), c => c.StartsWith("stangent spec check"));
    }

    [Fact]
    public void The_result_check_reads_says_what_the_call_ran_with()
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage();
        Assert.Equal(0, rig.Run(Spec, "--model", "sonnet", "--effort", "low", "p1").Exit);
        var run = System.Text.Json.Nodes.JsonNode.Parse(Seen)!["stangent_run"]!;
        Assert.Equal("stangent-page-writer", (string?)run["agent"]);
        Assert.Equal("sonnet", (string?)run["model"]);
        Assert.Equal("low", (string?)run["effort"]);
        Assert.Equal("batch", (string?)run["mode"]);
        Assert.Equal("9.9.9 (Claude Code)", (string?)run["claudeVersion"]);
    }

    [Fact]
    public void Agents_mode_says_so_and_passes_both_settings()
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage();
        rig.ClaudeAnswers("""{"type":"result","is_error":false}""");
        rig.Run(Spec, "--max-cost", "none", "--agents", "p1");
        Assert.Contains("--effort medium", Assert.Single(rig.ClaudeCalls()));
        Assert.Equal("agents", (string?)System.Text.Json.Nodes.JsonNode.Parse(Seen)!["stangent_run"]!["mode"]);
    }

    [Fact]
    public void A_one_turn_call_is_kept_exactly_as_sent()
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage();
        Assert.Equal(0, rig.Run(Spec, "p1").Exit);
        var dir = Assert.Single(Directory.GetDirectories(Path.Combine(rig.Work, ".stangent/calls")));
        Assert.Contains("INPUT-TEXT", File.ReadAllText(Path.Combine(dir, "input.txt")));
        Assert.Equal(rig.ClaudeStdin, File.ReadAllText(Path.Combine(dir, "input.txt")));
        Assert.Equal("""{"type":"object"}""", File.ReadAllText(Path.Combine(dir, "schema.json")));
        var call = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "call.json")))!;
        Assert.Equal("stangent-page-writer", (string?)call["agent"]);
        Assert.Equal("fake-model", (string?)call["model"]);
        Assert.Equal("medium", (string?)call["effort"]);
        Assert.Equal("p1", (string?)call["target"]);
        Assert.Equal("""{"done":true}""", File.ReadAllText(Path.Combine(dir, "answer.json")).Trim());
    }

    [Fact]
    public void The_same_call_twice_is_one_folder()
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage();
        rig.Run(Spec, "p1");
        rig.Run(Spec, "p1");
        Assert.Single(Directory.GetDirectories(Path.Combine(rig.Work, ".stangent/calls")));
    }

    string RecordedCall()
    {
        OnePage();
        Assert.Equal(0, rig.Run(Spec, "--model", "opus", "--effort", "high", "p1").Exit);
        File.Delete(Path.Combine(rig.Dir, "log"));
        File.Delete(Path.Combine(rig.Dir, "claude-stdin"));
        return Path.GetFileName(Assert.Single(Directory.GetDirectories(Path.Combine(rig.Work, ".stangent/calls"))));
    }

    [Fact]
    public void Repeat_asks_the_recorded_call_again_with_its_own_settings_and_records_nothing()
    {
        if (OperatingSystem.IsWindows()) return;
        var id = RecordedCall();
        var run = rig.Run(Spec, "--max-cost", "none", "--model", "sonnet", "--repeat", "3", "--call", id);

        Assert.Equal(0, run.Exit);
        Assert.Equal($"repeat:steady\t{id}\t1/1\n", run.Out);
        var calls = rig.ClaudeCalls();
        Assert.Equal(3, calls.Count);
        Assert.All(calls, c => { Assert.Contains("--model opus", c); Assert.Contains("--effort high", c); });
        Assert.Contains("INPUT-TEXT", rig.ClaudeStdin);
        Assert.DoesNotContain(rig.Calls(), c => c.StartsWith("stangent spec prepare") || c.StartsWith("stangent spec check"));
        var compare = Assert.Single(rig.Calls(), c => c.StartsWith("stangent ai compare "));
        Assert.Equal(3, compare.Split(' ').Count(p => p.EndsWith(".json") && !p.EndsWith("schema.json")));
        Assert.EndsWith($"--schema .stangent/calls/{id}/schema.json", compare);
        var sent = File.ReadAllText(Path.Combine(rig.Work, ".stangent/calls", id, "input.txt"));
        Assert.EndsWith("\n\n", sent);
        Assert.Equal(sent + sent + sent, rig.ClaudeStdin);
        var repeat = Assert.Single(Directory.GetDirectories(Path.Combine(rig.Work, ".stangent/calls", id), "repeat-*"));
        Assert.True(File.Exists(Path.Combine(repeat, "compare.json")));
        Assert.Equal("""{"done":true}""", File.ReadAllText(Path.Combine(repeat, "2.json")).Trim());
    }

    [Fact]
    public void Repeat_says_when_the_answers_differ()
    {
        if (OperatingSystem.IsWindows()) return;
        var id = RecordedCall();
        OnePage("""
            "ai compare "*) echo '{"ok":true,"result":{"steady":false,"agreed":2,"decisions":3}}' ;;
            """);
        Assert.Equal($"repeat:unsteady\t{id}\t2/3\n", rig.Run(Spec, "--max-cost", "none", "--repeat", "2", "--call", id).Out);
    }

    [Fact]
    public void Repeat_of_a_call_never_recorded_fails_and_calls_nothing()
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage();
        var run = rig.Run(Spec, "--max-cost", "none", "--repeat", "3", "--call", "nope");
        Assert.Equal(1, run.Exit);
        Assert.Equal("repeat:failed\tnope\tno recorded call\n", run.Out);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Theory]
    [InlineData("--repeat", "3")]
    [InlineData("--repeat", "1", "--call", "x")]
    [InlineData("--repeat", "3", "--call", "x", "p1")]
    [InlineData("--call", "x", "p1")]
    public void Repeat_needs_a_count_above_one_and_calls_only(params string[] args)
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage();
        Assert.Equal(2, rig.Run(Spec, args).Exit);
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void Ticket_writers_get_both_settings_and_are_kept_too()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent("""
            "ticket pull K-1") echo '{"ok":true,"result":{"status":"pulled"}}' ;;
            "ticket points K-1") echo '{"ok":true,"result":{"status":"found"}}' ;;
            "ticket bundle K-1") echo '{"ok":true,"result":{"status":"built"}}' ;;
            "ticket draft K-1 --kind questions") echo '{"ok":true,"result":{"status":"prepared","next":{"action":"write","agent":"stangent-question-writer","prompt":"Input: .stangent/in.md\nWrite your result to: .stangent/out.json","output":".stangent/out.json","schema":"draft-output.v2.json"}}}' ;;
            "ticket check K-1 --step questions --usage "*) cp "${@: -1}" "$FAKE_DIR/usage-seen"; echo '{"ok":true,"result":{"status":"drafted"}}' ;;
            """);
        Assert.Equal(0, rig.Run(Tickets, "--max-cost", "none", "--effort", "max", "--questions", "K-1").Exit);
        var claude = Assert.Single(rig.ClaudeCalls());
        Assert.Contains("--effort max", claude);
        Assert.Contains("autoMemoryEnabled", claude);
        Assert.Equal("max", (string?)System.Text.Json.Nodes.JsonNode.Parse(Seen)!["stangent_run"]!["effort"]);
        var dir = Assert.Single(Directory.GetDirectories(Path.Combine(rig.Work, ".stangent/calls")));
        Assert.Equal("K-1 questions", (string?)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "call.json")))!["target"]);
    }

    [Fact]
    public void Pinning_only_some_steps_stops_before_the_first_call()
    {
        if (OperatingSystem.IsWindows()) return;
        OnePage("""
            "ai settings --all"*) echo '{"ok":false,"error":{"code":"ai_settings_missing","message":"No ai.model or ai.effort for stangent-page-verifier"}}'; exit 2 ;;
            """);
        var run = rig.Run(Spec, "--max-cost", "none", "p1");
        Assert.NotEqual(0, run.Exit);
        Assert.Empty(rig.ClaudeCalls());
        Assert.Contains("error\tp1\tNo ai.model or ai.effort for stangent-page-verifier", run.Out);
    }

    [Fact]
    public void A_run_with_nothing_to_do_needs_no_settings()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent("""
            "ai settings "*) echo '{"ok":false,"error":{"code":"ai_settings_missing","message":"none"}}'; exit 2 ;;
            "spec prepare p1") echo '{"ok":true,"result":{"status":"current"}}' ;;
            """);
        var run = rig.Run(Spec, "p1");
        Assert.Equal(0, run.Exit);
        Assert.Equal("current\tp1\n", run.Out);
    }

    [Fact]
    public void A_repeat_whose_calls_failed_is_failed_never_steady()
    {
        if (OperatingSystem.IsWindows()) return;
        var id = RecordedCall();
        rig.ClaudeAnswers("""{"type":"result","is_error":true,"result":"Not logged in"}""");
        var run = rig.Run(Spec, "--max-cost", "none", "--repeat", "3", "--call", id);
        Assert.Equal(1, run.Exit);
        Assert.Equal($"repeat:failed\t{id}\t3 of 3 calls gave no answer\n", run.Out);
        Assert.DoesNotContain(rig.Calls(), c => c.StartsWith("stangent ai compare"));
    }

    [Fact]
    public void A_failed_compare_fails_the_run()
    {
        if (OperatingSystem.IsWindows()) return;
        var id = RecordedCall();
        OnePage("""
            "ai compare "*) exit 2 ;;
            """);
        var run = rig.Run(Spec, "--max-cost", "none", "--repeat", "2", "--call", id);
        Assert.Equal(1, run.Exit);
        Assert.Equal($"repeat:failed\t{id}\tcompare gave no result\n", run.Out);
    }

    [Fact]
    public void A_repeat_uses_the_calls_own_options_whichever_driver_runs_it()
    {
        if (OperatingSystem.IsWindows()) return;
        Ticket_writers_get_both_settings_and_are_kept_too();
        var id = Path.GetFileName(Assert.Single(Directory.GetDirectories(Path.Combine(rig.Work, ".stangent/calls"))));
        var call = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(rig.Work, ".stangent/calls", id, "call.json")))!;
        Assert.Equal(["--settings", """{"autoMemoryEnabled":false}"""], call["extraArgs"]!.AsArray().Select(x => (string)x!));
        File.Delete(Path.Combine(rig.Dir, "log"));
        OnePage();
        Assert.Equal(0, rig.Run(Spec, "--max-cost", "none", "--repeat", "2", "--call", id).Exit);
        Assert.All(rig.ClaudeCalls(), c => { Assert.Contains("autoMemoryEnabled", c); Assert.Contains("--effort max", c); });
        Assert.Equal(2, rig.ClaudeCalls().Count);
    }
}

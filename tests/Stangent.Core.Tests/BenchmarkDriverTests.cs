using System.Text.Json.Nodes;

namespace Stangent.Core.Tests;

/// <summary>benchmark/run.sh, from a copy beside its own repos.tsv: the real list names real repos, and results would
/// land in the repo (D112).</summary>
public sealed class BenchmarkRunDriverTests : IDisposable
{
    readonly DriverRig rig = new();
    readonly string bench;
    readonly string script;
    readonly string commit;

    public BenchmarkRunDriverTests()
    {
        bench = Path.Combine(rig.Dir, "bench");
        Directory.CreateDirectory(bench);
        script = Path.Combine(bench, "run.sh");
        File.Copy(Path.Combine(DriverRig.RepoRoot, "benchmark/run.sh"), script);
        var origin = rig.NewRepo("origin");
        commit = rig.Git(origin, "rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(bench, "repos.tsv"), $"# name\turl\tcommit\nshop\t{origin}\t{commit}\nother\t{origin}\t{commit}\n");
        Fake("");
    }

    public void Dispose() => rig.Dispose();

    void Fake(string more) => rig.Stangent($$$"""
        {{{more}}}
        "--version") echo '9.9.9' ;;
        "init --repo "*) echo '{"ok":true}' ;;
        "status") echo '{"gen_ai.usage.input_tokens":7,"gen_ai.usage.output_tokens":3}' >>"$STANGENT_TELEMETRY_DIR/e.jsonl"; echo '{"ok":true}' ;;
        """);

    JsonObject Results() => JsonNode.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(bench, "results")))))!.AsObject();

    [Fact]
    public void Each_pinned_repo_is_run_and_recorded()
    {
        if (OperatingSystem.IsWindows()) return;
        Fake("");
        var result = rig.Run(script, "t1", "status");
        Assert.Equal(0, result.Exit);
        var report = Results();
        Assert.Equal("t1", (string)report["label"]!);
        Assert.Equal("status", (string)report["command"]!);
        Assert.Equal("9.9.9", (string)report["stangentVersion"]!);
        var shop = report["results"]![0]!;
        Assert.Equal("shop", (string)shop["repo"]!);
        Assert.Equal(commit, (string)shop["commit"]!);
        Assert.Equal(0, (int)shop["exitCode"]!);
        Assert.Equal(7, (int)shop["inputTokens"]!);
        Assert.Equal(3, (int)shop["outputTokens"]!);
        Assert.Equal(2, report["results"]!.AsArray().Count);
    }

    [Fact]
    public void Only_limits_the_repos_and_a_repos_config_replaces_the_default()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Path.Combine(bench, "configs"));
        File.WriteAllText(Path.Combine(bench, "configs/other.yaml"), "from: config\n");
        rig.Env["BENCH_ONLY"] = "other";
        Assert.Equal(0, rig.Run(script, "t2", "status").Exit);
        Assert.Equal("other", (string)Assert.Single(Results()["results"]!.AsArray())!["repo"]!);
        Assert.Equal("from: config\n", File.ReadAllText(Path.Combine(bench, ".cache/other/stangent.yaml")));
    }

    [Fact]
    public void A_command_that_fails_is_recorded_not_fatal()
    {
        if (OperatingSystem.IsWindows()) return;
        Fake("""
            "boom") echo '{}' >>"$STANGENT_TELEMETRY_DIR/e.jsonl"; exit 7 ;;
            """);
        var result = rig.Run(script, "t3", "boom");
        Assert.Equal(0, result.Exit);
        Assert.Equal(7, (int)Results()["results"]![0]!["exitCode"]!);
        Assert.Contains("shop: exit 7", result.Err);
    }

    [Fact]
    public void An_init_that_fails_stops_the_run_and_says_where()
    {
        if (OperatingSystem.IsWindows()) return;
        Fake("""
            "init --repo "*) exit 5 ;;
            """);
        var result = rig.Run(script, "t4", "status");
        Assert.Equal(5, result.Exit);
        Assert.Contains("init --repo", result.Err);
    }

    [Fact]
    public void Bad_arguments_give_usage()
    {
        if (OperatingSystem.IsWindows()) return;
        var none = rig.Run(script);
        Assert.NotEqual(0, none.Exit);
        Assert.Contains("usage: run.sh <label>", none.Err);
        var noCommand = rig.Run(script, "t5");
        Assert.Equal(ExitCodes.Usage, noCommand.Exit);
        Assert.Contains("usage: run.sh <label>", noCommand.Err);
        Assert.Empty(rig.Calls());
    }
}

/// <summary>benchmark/tickets/run.sh on a throwaway clone (D112). It calls the real ticket-run.sh and spec-run.sh, which
/// reach only the fakes.</summary>
public sealed class BenchmarkTicketsDriverTests : IDisposable
{
    const string Script = "benchmark/tickets/run.sh";
    const string Ok = """{"ok":true,"result":{}}""";
    const string NoAi = """{"ok":true,"result":{"ai":{"status":"none"}}}""";
    const string Written = """{"ok":true,"result":{"status":"written"}}""";
    const string Writer = """{"ok":true,"result":{"status":"prepared","next":{"action":"write","agent":"w","prompt":"Input: .stangent/in.md","output":".stangent/out.json","schema":"s.json"}}}""";

    readonly DriverRig rig = new();
    readonly string clone;
    readonly string tickets;
    readonly string pinned;
    readonly string older;

    public BenchmarkTicketsDriverTests()
    {
        rig.Env["STANGENT"] = "stangent";
        clone = rig.NewRepo("clone", ("stangent.yaml", "tickets:\n  jira: x\n"), ("src/a.txt", "one\n"));
        older = rig.Git(clone, "rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(clone, "src/a.txt"), "two\n");
        pinned = rig.Commit(clone);
        rig.Put(Path.Combine(clone, ".stangent/in.md"), "TEXT");
        tickets = Path.Combine(rig.Dir, "tix");
        Directory.CreateDirectory(tickets);
        Selection(new JsonObject { ["1"] = "K-1", ["2"] = "K-2" });
        File.WriteAllText(Path.Combine(tickets, "answer-key.json"), $$$$"""{"issues":{"1":{"fixCommit":"{{{{pinned}}}}","parent":"{{{{older}}}}"}}}""");
        rig.Stangent(Everyday);
    }

    public void Dispose() => rig.Dispose();

    void Selection(JsonObject keys)
    {
        File.WriteAllText(Path.Combine(tickets, "selection.json"), $$"""
            {"pinned":"{{pinned}}","issues":[{"issue":1,"kind":"bug","runCommit":"{{older}}"},{"issue":2,"kind":"notbug","runCommit":"{{older}}"}]}
            """);
        File.WriteAllText(Path.Combine(tickets, "jira-keys.json"), keys.ToJsonString());
    }

    const string Everyday = $$$"""
        "ticket pull "*) echo '{{{Ok}}}' ;;
        "ticket points "*) echo '{{{NoAi}}}' ;;
        "ticket bundle "*) echo '{{{Ok}}}' ;;
        """;

    DriverResult Run(params string[] args) => rig.RunIn(clone, Script, [clone, tickets, .. args]);

    string Log => File.ReadAllText(Path.Combine(tickets, "run/run.log"));
    JsonNode Record(string key) => JsonNode.Parse(File.ReadAllText(Path.Combine(tickets, "run", key + ".json")))!;

    [Fact]
    public void Points_runs_each_ticket_without_the_finder_and_ends_on_the_pinned_commit()
    {
        if (OperatingSystem.IsWindows()) return;
        var result = Run("points");
        Assert.Equal(0, result.Exit);
        Assert.Contains("stangent ticket pull K-1", rig.StangentCalls());
        Assert.Contains("stangent ticket bundle K-2", rig.StangentCalls());
        Assert.NotNull(Record("K-1")["steps"]!["points"]!["ms"]);
        Assert.Contains("built\tK-2\tbundle", Log);
        Assert.Equal(pinned, rig.Git(clone, "rev-parse", "HEAD"));
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void Only_limits_the_tickets()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(0, Run("bundles", "--only", "K-2").Exit);
        Assert.Equal(["stangent ticket bundle K-2"], rig.StangentCalls());
    }

    [Fact]
    public void A_step_that_fails_stops_the_run_and_says_which()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent("""
            "ticket pull K-1") echo '{"ok":false,"error":{"code":"jira_down","message":"no"}}'; exit 1 ;;
            """ + "\n" + Everyday);
        var result = Run("points");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("STOPPED: points K-1 exited", result.Err);
        Assert.DoesNotContain(rig.StangentCalls(), c => c.Contains("K-2"));
    }

    [Fact]
    public void A_ticket_with_no_jira_key_stops_the_run()
    {
        if (OperatingSystem.IsWindows()) return;
        Selection(new JsonObject { ["1"] = "K-1" });
        var result = Run("bundles");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("no Jira key for #2", result.Err);
    }

    [Theory]
    [InlineData]
    [InlineData("nonsense")]
    public void Bad_arguments_give_usage_and_run_nothing(params string[] args)
    {
        if (OperatingSystem.IsWindows()) return;
        var tooFew = rig.RunIn(clone, Script, args);
        Assert.Equal(ExitCodes.Usage, tooFew.Exit);
        Assert.Contains("run.sh <clone> <tickets dir> <phase>", tooFew.Err);
        Assert.Empty(rig.Calls());
    }

    [Theory]
    [InlineData("bundles", "typo")]
    [InlineData("--jobs", "2")]
    [InlineData("--bogus", "bundles")]
    public void An_unknown_phase_is_refused_before_any_phase_runs(params string[] args)
    {
        if (OperatingSystem.IsWindows()) return;
        var result = Run(args);
        Assert.Equal(ExitCodes.Usage, result.Exit);
        Assert.Contains("run.sh <clone> <tickets dir> <phase>", result.Err);
        Assert.Empty(rig.Calls());
    }

    [Fact]
    public void Files_skips_a_ticket_already_searched_at_its_own_commit()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Put(Path.Combine(clone, ".stangent/tickets/K-1/points.json"), $$$"""{"ai":{"commit":"{{{older}}}","status":"found"}}""");
        var result = Run("files");
        Assert.Equal(0, result.Exit);
        Assert.Contains("files K-1: already searched", Log);
        Assert.DoesNotContain(rig.StangentCalls(), c => c.Contains("K-1"));
        Assert.Contains("stangent ticket points K-2 --refind", rig.StangentCalls());
        Assert.DoesNotContain(rig.StangentCalls(), c => c.StartsWith("stangent ticket pull"));
    }

    [Fact]
    public void Pages_with_nothing_missing_makes_no_AI_call()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(0, Run("pages").Exit);
        Assert.Contains("pages: 0 to generate", Log);
        Assert.Empty(rig.StangentCalls());
    }

    [Fact]
    public void Pages_generates_each_missing_page_once_keeping_spaces_in_ids()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Put(Path.Combine(clone, ".stangent/tickets/K-1/bundle.json"), """{"missingPages":["ANY /api/x","p"]}""");
        rig.Put(Path.Combine(clone, ".stangent/tickets/K-2/bundle.json"), """{"missingPages":["p"]}""");
        rig.Stangent($$$"""
            "spec prepare "*) echo '{{{Written}}}' ;;
            """);
        Assert.Equal(0, Run("pages").Exit);
        Assert.Equal(["stangent spec prepare ANY /api/x", "stangent spec prepare p"], rig.StangentCalls());
    }

    string Drafts() => $$$"""
        "ticket draft "*) echo '{{{Writer}}}' ;;
        "ticket check "*) echo '{{{Written}}}' ;;
        {{{Everyday}}}
        """;

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public void Drafts_give_every_ticket_questions_and_a_bug_its_resolution_from_the_fix_range(string jobs)
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent(Drafts());
        Assert.Equal(0, Run("drafts", "--jobs", jobs).Exit);
        Assert.Contains("stangent ticket draft K-1 --kind questions", rig.StangentCalls());
        Assert.Contains("stangent ticket draft K-2 --kind questions", rig.StangentCalls());
        Assert.Contains($"stangent ticket draft K-1 --kind resolution --pr {older}..{pinned}", rig.StangentCalls());
        Assert.DoesNotContain(rig.StangentCalls(), c => c.Contains("K-2 --kind resolution"));
        Assert.Equal(3, rig.ClaudeCalls().Count);
    }

    [Fact]
    public void Drafts_in_parallel_list_the_ticket_that_failed_and_stop()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Stangent(Drafts());
        rig.ClaudeAnswers("""{"type":"result","is_error":true,"result":"limit"}""");
        var result = Run("drafts", "--jobs", "2");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("STOPPED: drafts: a step failed for", result.Err);
    }

    [Fact]
    public void Prove_keeps_a_recorded_outcome_and_makes_no_AI_call()
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var key in new[] { "K-1", "K-2" })
            rig.Put(Path.Combine(clone, $".stangent/tickets/{key}/bug-proof.json"), """{"outcome":"not-reproduced"}""");
        Assert.Equal(0, Run("prove").Exit);
        Assert.Contains("prove K-1: already recorded", Log);
        Assert.Empty(rig.ClaudeCalls());
        Assert.DoesNotContain(rig.StangentCalls(), c => c.Contains("ticket prove"));
    }

    [Fact]
    public void Prove_runs_a_reproduction_again_on_the_fix_commit()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Put(Path.Combine(clone, ".stangent/tickets/K-1/ticket.json"), """{"isBug":true}""");
        rig.Put(Path.Combine(clone, ".stangent/tickets/K-2/bug-proof.json"), """{"outcome":"not-reproduced"}""");
        rig.Stangent($$$"""
            "ticket prove K-1 --verify")
              git rev-parse HEAD >"$FAKE_DIR/verified-at"
              echo '{"outcome":"reproduced","attempts":[{"testFile":"src/t.spec"}],"verification":{"result":"passed","runs":[{"result":"passed"}]}}' >.stangent/tickets/K-1/bug-proof.json
              echo '{{{Ok}}}' ;;
            "ticket prove K-1")
              echo proof >src/t.spec
              echo '{"outcome":"reproduced","attempts":[{"testFile":"src/t.spec"}]}' >.stangent/tickets/K-1/bug-proof.json
              echo '{{{Written}}}' ;;
            {{{Everyday}}}
            """);
        Assert.Equal(0, Run("prove", "--only", "K-1").Exit);
        Assert.Equal(pinned, File.ReadAllText(Path.Combine(rig.Dir, "verified-at")).Trim());
        Assert.Equal("reproduced", (string)Record("K-1")["proofOutcome"]!);
        Assert.Equal("passed", (string)Record("K-1")["proofOnFix"]!);
    }

    [Fact]
    public void Prove_stops_when_stangents_own_setup_blocked_it()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Put(Path.Combine(clone, ".stangent/tickets/K-1/ticket.json"), """{"isBug":true}""");
        rig.Stangent($$$"""
            "ticket prove K-1")
              echo '{"outcome":"failed","attempts":[{"setupBlocked":true,"reason":"rules refused dotnet"}]}' >.stangent/tickets/K-1/bug-proof.json
              echo '{{{Written}}}' ;;
            {{{Everyday}}}
            """);
        var result = Run("prove");
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("STOPPED: prove K-1: rules refused dotnet", result.Err);
        Assert.DoesNotContain(rig.StangentCalls(), c => c.Contains("K-2"));
    }

    [Fact]
    public void Replay_finds_identical_bundles_no_AI_needed_and_puts_the_config_back()
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var key in new[] { "K-1", "K-2" })
            rig.Put(Path.Combine(clone, $".stangent/tickets/{key}/bundle.json"), "same\n");
        rig.Stangent($$$"""
            "ticket prepare "*) echo '{"ok":true,"result":{"status":"current"}}' ;;
            "ticket draft "*) echo '{"ok":true,"result":{"status":"current"}}' ;;
            "ticket pull "*) grep -q 'replay: ' stangent.yaml && echo '{{{Ok}}}' ;;
            {{{Everyday}}}
            """);
        var before = File.ReadAllText(Path.Combine(clone, "stangent.yaml"));
        Assert.Equal(0, Run("replay").Exit);
        Assert.True((bool)Record("K-1")["replayIdentical"]!);
        Assert.Equal(0, (int)Record("K-1")["rerunAiCalls"]!);
        Assert.Equal(before, File.ReadAllText(Path.Combine(clone, "stangent.yaml")));
        Assert.Empty(rig.ClaudeCalls());
    }

    [Fact]
    public void Replay_counts_a_step_that_would_need_AI_and_a_bundle_that_changed()
    {
        if (OperatingSystem.IsWindows()) return;
        rig.Put(Path.Combine(clone, ".stangent/tickets/K-2/bundle.json"), "before\n");
        rig.Stangent($$$"""
            "ticket prepare "*) echo '{"ok":true,"result":{"status":"prepared"}}' ;;
            "ticket draft "*) echo '{"ok":true,"result":{"status":"current"}}' ;;
            "ticket bundle "*) echo after >.stangent/tickets/K-2/bundle.json; echo '{{{Ok}}}' ;;
            {{{Everyday}}}
            """);
        Assert.Equal(0, Run("replay", "--only", "K-2").Exit);
        Assert.False((bool)Record("K-2")["replayIdentical"]!);
        Assert.Equal(1, (int)Record("K-2")["rerunAiCalls"]!);
    }
}

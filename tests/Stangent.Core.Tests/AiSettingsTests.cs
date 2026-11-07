using System.Text.Json.Nodes;
using Stangent.Spec.Pages;

namespace Stangent.Core.Tests;

/// <summary>D161 in code: which settings a step runs with, comparing repeated answers, and what a record says it ran with.</summary>
public sealed class AiSettingsTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("stangent-ai").FullName;
    public void Dispose() => Directory.Delete(root, true);

    static StangentConfig Config(string? model = null, string? effort = null, Dictionary<string, StangentConfig.AiStepConfig>? steps = null) =>
        new() { Ai = new StangentConfig.AiConfig { Model = model, Effort = effort, Steps = steps ?? [] } };

    [Fact]
    public void Nothing_pinned_refuses_never_a_default()
    {
        var e = Assert.Throws<StangentException>(() => AiSettings.Resolve(Config(), "stangent-link-judge"));
        Assert.Equal("ai_settings_missing", e.Code);
        Assert.Contains("ai.model or ai.effort", e.Message);
        Assert.Equal("ai_settings_missing", Assert.Throws<StangentException>(() => AiSettings.Resolve(Config(model: "opus"), "x")).Code);
        Assert.Contains("ai.effort", Assert.Throws<StangentException>(() => AiSettings.Resolve(Config(model: "opus"), "x")).Message);
    }

    [Fact]
    public void A_flag_wins_then_the_step_then_the_global_setting()
    {
        var config = Config("sonnet", "medium", new() { ["stangent-link-judge"] = new() { Model = "opus" } });
        var judge = AiSettings.Resolve(config, "stangent-link-judge");
        Assert.Equal(("opus", "ai.steps.stangent-link-judge.model", "medium", "ai.effort"), (judge.Model, judge.ModelFrom, judge.Effort, judge.EffortFrom));
        var other = AiSettings.Resolve(config, "stangent-page-writer");
        Assert.Equal(("sonnet", "ai.model"), (other.Model, other.ModelFrom));
        var flagged = AiSettings.Resolve(config, "stangent-link-judge", "haiku", "high");
        Assert.Equal(("haiku", "--model", "high", "--effort"), (flagged.Model, flagged.ModelFrom, flagged.Effort, flagged.EffortFrom));
        Assert.Equal("opus", AiSettings.Resolve(Config(effort: "low"), "x", "opus").Model);
    }

    [Fact]
    public void An_effort_claude_code_doesnt_know_is_refused()
    {
        Assert.Equal("ai_effort_unknown", Assert.Throws<StangentException>(() => AiSettings.Resolve(Config("opus", "huge"), "x")).Code);
        foreach (var e in AiSettings.Efforts) Assert.Equal(e, AiSettings.Resolve(Config("opus", e), "x").Effort);
    }

    [Fact]
    public void Steps_are_read_from_stangent_yaml()
    {
        File.WriteAllText(Path.Combine(root, "stangent.yaml"), "version: 1\nai:\n  model: sonnet\n  effort: medium\n  steps:\n    stangent-link-judge:\n      effort: high\n");
        var repo = StangentRepo.Discover(root);
        Assert.Equal(("sonnet", "high"), (AiSettings.Resolve(repo.Config, "stangent-link-judge").Model, AiSettings.Resolve(repo.Config, "stangent-link-judge").Effort));
    }

    static JsonNode? J(string s) => JsonNode.Parse(s);

    /// <summary>A judge-like schema: a note in words, a statement id with a pattern.</summary>
    static readonly JsonNode Judge = JsonNode.Parse("""
        {"type":"object","properties":{"links":{"type":"array","items":{"type":"object","properties":{
          "claim":{"type":"string","pattern":"^C-"},"statement":{"type":"string","pattern":"^[SARE][0-9]+$"},
          "verdict":{"enum":["agree","gap"]},"note":{"type":"string"}}}},"why":{"type":"string"},"status":{"type":"string"}}}
        """)!;

    /// <summary>A writer-like schema: the statement is the AI's own sentence.</summary>
    static readonly JsonNode Writer = JsonNode.Parse("""
        {"type":"object","properties":{"rules":{"type":"array","items":{"type":"object","properties":{
          "statement":{"type":"string"},"cites":{"type":"array","items":{"type":"string"}}}}}}}
        """)!;

    [Fact]
    public void Same_decisions_in_other_words_and_order_are_steady()
    {
        var a = J("""{"links":[{"claim":"C-1","verdict":"agree","note":"fine"},{"claim":"C-2","verdict":"gap","note":"x"}],"why":"a"}""");
        var b = J("""{"links":[{"claim":"C-2","verdict":"gap","note":"other words"},{"verdict":"agree","claim":"C-1","note":"y"}],"why":"b"}""");
        var r = AiCompare.Compare([a, b, a], Judge);
        Assert.True((bool)r["steady"]!);
        Assert.Equal(2, (int)r["decisions"]!);
        Assert.Equal(["note", "why"], r["ignored"]!.AsArray().Select(x => (string)x!));
    }

    [Fact]
    public void A_changed_verdict_shows_which_runs_gave_which()
    {
        var a = J("""{"links":[{"claim":"C-1","verdict":"agree"}],"status":"done"}""");
        var b = J("""{"links":[{"claim":"C-1","verdict":"gap"}],"status":"done"}""");
        var r = AiCompare.Compare([a, a, b], Judge);
        Assert.False((bool)r["steady"]!);
        Assert.Equal(1, (int)r["agreed"]!);
        var differ = r["differ"]!.AsArray().Select(d => ((string)d!["item"]!, string.Join(",", d["inRuns"]!.AsArray().Select(x => (int)x!)))).ToList();
        Assert.Contains(("links[]: {\"claim\":\"C-1\",\"verdict\":\"agree\"}", "1,2"), differ);
        Assert.Contains(("links[]: {\"claim\":\"C-1\",\"verdict\":\"gap\"}", "3"), differ);
    }

    [Fact]
    public void A_statement_id_is_a_decision_but_a_written_statement_is_words()
    {
        var r3 = J("""{"links":[{"claim":"C-1","statement":"R3","verdict":"agree"}]}""");
        var r7 = J("""{"links":[{"claim":"C-1","statement":"R7","verdict":"agree"}]}""");
        Assert.False((bool)AiCompare.Compare([r3, r7], Judge)["steady"]!);
        var one = J("""{"rules":[{"statement":"Saving needs a name.","cites":["M:A.Save"]}]}""");
        var two = J("""{"rules":[{"statement":"A name is required to save.","cites":["M:A.Save"]}]}""");
        Assert.True((bool)AiCompare.Compare([one, two], Writer)["steady"]!);
        Assert.False((bool)AiCompare.Compare([one, J("""{"rules":[{"statement":"x","cites":["M:A.Other"]}]}""")], Writer)["steady"]!);
    }

    [Fact]
    public void A_name_constrained_anywhere_in_a_schema_is_never_dropped()
    {
        var mixed = JsonNode.Parse("""{"properties":{"a":{"properties":{"note":{"type":"string"}}},"b":{"properties":{"note":{"enum":["x","y"]}}}}}""")!;
        Assert.DoesNotContain("note", AiCompare.Droppable(mixed));
        Assert.Contains("note", AiCompare.Droppable(JsonNode.Parse("""{"properties":{"note":{"type":"string"}}}""")!));
    }

    [Fact]
    public void Every_output_schemas_ids_and_checked_words_stay_decisions()
    {
        foreach (var f in Directory.GetFiles(Path.Combine(DriverRig.RepoRoot, "schemas"), "*output*.json"))
        {
            var dropped = AiCompare.Droppable(JsonNode.Parse(File.ReadAllText(f))!);
            Assert.DoesNotContain("extra", dropped);
            Assert.DoesNotContain("name", dropped);
            Assert.DoesNotContain("quote", dropped);
            Assert.DoesNotContain("fsdQuote", dropped);
            if (Path.GetFileName(f).StartsWith("link-judge") || Path.GetFileName(f).StartsWith("page-verifier") || Path.GetFileName(f).StartsWith("agree-checker"))
                Assert.DoesNotContain("statement", dropped);
        }
    }

    [Fact]
    public void A_missing_answer_is_never_steady()
    {
        var a = J("""{"links":[{"fsdQuote":"The user can save.","verdict":"agree"}]}""");
        var r = AiCompare.Compare([null, null, null], Judge);
        Assert.False((bool)r["steady"]!);
        Assert.Equal([1, 2, 3], r["missing"]!.AsArray().Select(x => (int)x!));
        Assert.False((bool)AiCompare.Compare([a, null], Judge)["steady"]!);
        Assert.False((bool)AiCompare.Compare([a, J("""{"links":[{"fsdQuote":"The user can delete.","verdict":"agree"}]}""")], Judge)["steady"]!);
        Assert.Equal("too_few_answers", Assert.Throws<StangentException>(() => AiCompare.Compare([a], Judge)).Code);
    }

    [Fact]
    public void The_agent_list_is_the_plugins_agents()
    {
        var agents = Directory.GetFiles(Path.Combine(DriverRig.RepoRoot, "plugin/agents"), "*.md").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal);
        Assert.Equal(agents, AiSettings.Agents);
    }

    [Fact]
    public void Pinning_only_some_steps_refuses_them_all()
    {
        var config = Config(steps: new() { ["stangent-page-writer"] = new() { Model = "m", Effort = "low" } });
        Assert.Equal("m", AiSettings.Resolve(config, "stangent-page-writer").Model);
        Assert.Equal("ai_settings_missing", Assert.Throws<StangentException>(() => AiSettings.ResolveAll(config)).Code);
        Assert.Equal(AiSettings.Agents.Count, AiSettings.ResolveAll(Config("m", "high")).Count);
    }

    [Fact]
    public void A_record_says_what_its_calls_ran_with()
    {
        var result = J("""
            {"type":"result","usage":{"input_tokens":5,"output_tokens":2},"total_cost_usd":0.1,"modelUsage":{"claude-opus-5-5":{"outputTokens":2}},
             "stangent_run":{"agent":"stangent-link-judge","model":"opus","effort":"high","mode":"batch","claudeVersion":"2.1"}}
            """);
        var u = Usage.FromClaudeResult(result);
        Assert.Equal(new RunSettings("stangent-link-judge", "opus", "high", "batch", "2.1"), u.Run);
        Assert.Equal(u.Run, Usage.FromJson(u.ToJson()).Run);
        Assert.Equal("claude-opus-5-5", u.Model);
    }

    [Fact]
    public void A_call_no_driver_described_is_unknown_and_mixed_calls_say_so()
    {
        var plain = Usage.FromClaudeResult(J("""{"type":"result","total_cost_usd":0.1}"""));
        Assert.Equal(RunSettings.Unknown, plain.Run);
        var high = Usage.FromClaudeResult(J("""{"type":"result","stangent_run":{"agent":"a","model":"m","effort":"high","mode":"batch","claudeVersion":"1"}}"""));
        var low = Usage.FromClaudeResult(J("""{"type":"result","stangent_run":{"agent":"a","model":"m","effort":"low","mode":"batch","claudeVersion":"1"}}"""));
        Assert.Equal(high.Run, Usage.None.Add(high).Run);
        Assert.Equal(high.Run, high.Add(Usage.None).Run);
        var both = high.Add(low).Run!;
        Assert.Equal(("a", "m", "mixed"), (both.Agent, both.Model, both.Effort));
        Assert.Null(Usage.FromJson(J("""{"calls":1,"costUsd":0.1}""")).Run);
    }

    [Fact]
    public void Runs_lists_saved_answers_and_flags_other_settings()
    {
        File.WriteAllText(Path.Combine(root, "stangent.yaml"), "version: 1\nai:\n  model: opus\n  effort: high\n");
        Directory.CreateDirectory(Path.Combine(root, "specs/reference"));
        var same = new Usage("m", 1, 1, 1, 0, 0, 0.1, 1) { Run = new RunSettings("stangent-page-writer", "opus", "high", "batch", "1") };
        var other = new Usage("m", 1, 1, 1, 0, 0, 0.1, 1) { Run = new RunSettings("stangent-page-verifier", "opus", "medium", "batch", "1") };
        var old = new Usage("m", 1, 1, 1, 0, 0, 0.1, 1);
        File.WriteAllText(Path.Combine(root, "specs/reference/p.json"), new JsonObject
        {
            ["generated"] = new JsonObject { ["writer"] = same.ToJson(), ["verifier"] = other.ToJson(), ["old"] = old.ToJson(), ["none"] = Usage.None.ToJson() },
        }.ToJsonString());
        var r = AiRuns.List(StangentRepo.Discover(root), differsOnly: false);
        Assert.Equal(3, (int)r["records"]!);
        Assert.Equal(1, (int)r["unknown"]!);
        Assert.Equal(2, (int)r["differs"]!);
        var differs = AiRuns.List(StangentRepo.Discover(root), differsOnly: true)["runs"]!.AsArray();
        Assert.Contains(differs, d => (string?)d!["why"] == "effort medium, pinned high" && (string?)d["path"] == "$.generated.verifier");
        Assert.Contains(differs, d => (string?)d!["why"] == "agent unknown");
    }

    [Fact]
    public void No_plugin_command_starts_an_ai_step_in_the_session_d162()
    {
        foreach (var f in Directory.GetFiles(Path.Combine(DriverRig.RepoRoot, "plugin/commands"), "*.md"))
        {
            var text = File.ReadAllText(f);
            var tools = text.Split('\n').FirstOrDefault(l => l.StartsWith("allowed-tools:")) ?? "";
            Assert.DoesNotContain("Task", tools);
            Assert.DoesNotContain("start the subagent", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}


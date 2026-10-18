using System.Text.Json.Nodes;
using Stangent.Core.Phases;
using Stangent.Core.Telemetry;

namespace Stangent.Core.Tests;

public class TelemetryTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_written_event_matches_the_telemetry_contract()
    {
        using var temp = new TempRepo();
        var writer = new TelemetryWriter(temp.Repo.TelemetryDir, SchemaRegistry.Embedded, new FixedTime(Now));
        var span = writer.Start("stangent.store.put");
        span.Attributes["stangent.command"] = "store put";
        span.Attributes["stangent.exit_code"] = 0;
        span.Attributes["stangent.artifact.count"] = 2;
        span.Attributes["gen_ai.usage.input_tokens"] = 1200;

        Assert.NotNull(writer.End(span, ok: true));

        var line = Assert.Single(File.ReadAllLines(Path.Combine(temp.Repo.TelemetryDir, "2026-10-18.jsonl")));
        Assert.Empty(SchemaRegistry.Embedded.Validate(TelemetryWriter.Schema, line));
        Assert.Equal("STATUS_CODE_OK", (string?)JsonNode.Parse(line)!["status"]!["code"]);
    }

    [Fact]
    public void An_event_carrying_anything_outside_the_contract_is_dropped()
    {
        using var temp = new TempRepo();
        var warnings = new StringWriter();
        var writer = new TelemetryWriter(temp.Repo.TelemetryDir, SchemaRegistry.Embedded, new FixedTime(Now), warnings);
        var span = writer.Start("stangent.store.put");
        span.Attributes["prompt"] = "public class SecretClientCode {}";

        Assert.Null(writer.End(span, ok: true));

        Assert.False(Directory.Exists(temp.Repo.TelemetryDir) && Directory.EnumerateFiles(temp.Repo.TelemetryDir).Any());
        Assert.Contains("dropped", warnings.ToString());
        Assert.DoesNotContain("SecretClientCode", warnings.ToString());
    }
}

public class RepoTests
{
    [Fact]
    public void Init_is_safe_to_run_twice_and_never_overwrites_the_config()
    {
        using var temp = new TempRepo("version: 1\nai:\n  enabled: false\n");

        var (repo, created) = StangentRepo.Init(temp.Root);

        Assert.False(created);
        Assert.False(repo.Config.Ai.Enabled);
        var gitignore = File.ReadAllLines(Path.Combine(temp.Root, ".gitignore"));
        Assert.All(StangentRepo.GitIgnoreLines, line => Assert.Single(gitignore, line));
    }

    [Fact]
    public void Ai_steps_are_blocked_when_the_repo_switches_ai_off()
    {
        using var temp = new TempRepo("version: 1\nai:\n  enabled: false\n");
        var repo = StangentRepo.Discover(Path.Combine(temp.Root));

        var error = Assert.Throws<StangentException>(() => AiPolicy.EnsureAllowed(repo.Config, "draft requirements"));

        Assert.Equal("ai_disabled", error.Code);
        Assert.Equal(ExitCodes.AiDisabled, error.ExitCode);
        AiPolicy.EnsureAllowed(new StangentConfig(), "draft requirements");
    }

    [Fact]
    public void Discover_finds_the_config_from_a_subfolder_and_rejects_unknown_keys()
    {
        using var temp = new TempRepo();
        var sub = Directory.CreateDirectory(Path.Combine(temp.Root, "src", "deep")).FullName;
        Assert.Equal(temp.Root, StangentRepo.Discover(sub).Root);

        File.WriteAllText(Path.Combine(temp.Root, StangentRepo.ConfigFile), "version: 1\naii: {}\n");
        Assert.Equal("invalid_config", Assert.Throws<StangentException>(() => StangentRepo.Discover(sub)).Code);
    }
}

public class PhaseCheckTests
{
    const string Roadmap = """
        | Phase | Delivers | Consumes contracts from | Provides contracts | Status |
        |---|---|---|---|---|
        | **0. Foundation** | x | — | `store`, `artifact-base` | done |
        | **1. Code graph** | x | 0 | `code-graph` | done |
        """;

    [Fact]
    public void This_repos_phase_files_and_roadmap_agree()
    {
        var root = RepoRoot.Find();

        var report = PhaseCheck.Run(Path.Combine(root, "phases"), Path.Combine(root, "roadmap.md"));

        Assert.True(report["ok"]!.GetValue<bool>(), report.ToJsonString());
    }

    [Fact]
    public void A_phase_on_an_old_contract_version_is_flagged_until_marked_needs_review()
    {
        using var dir = new PhaseDir(Roadmap);
        dir.Phase("0-foundation", "phase: 0\nname: foundation\nstatus: done\nprovides: { store: 2, artifact-base: 1 }");
        dir.Phase("1-code-graph", "phase: 1\nname: code-graph\nstatus: done\nconsumes: { store: 1 }\nprovides: { code-graph: 1 }");

        var report = dir.Check();

        Assert.False(report["ok"]!.GetValue<bool>());
        var stale = report["phases"]![1]!["stale"]![0]!;
        Assert.Equal("store", (string?)stale["contract"]);
        Assert.Equal(2, (int)stale["current"]!);
        Assert.Contains(Problems(report), p => p.Contains("1-code-graph.md") && p.Contains("needs review"));

        dir.Phase("1-code-graph", "phase: 1\nname: code-graph\nstatus: needs review\nconsumes: { store: 1 }\nprovides: { code-graph: 1 }");
        dir.Roadmap(Roadmap.Replace("`code-graph` | done", "`code-graph` | needs review"));
        Assert.True(dir.Check()["ok"]!.GetValue<bool>(), dir.Check().ToJsonString());
    }

    [Fact]
    public void Typos_duplicates_and_roadmap_drift_are_problems()
    {
        using var dir = new PhaseDir(Roadmap);
        dir.Phase("0-foundation", "phase: 0\nname: foundation\nstatus: in progress\nprovides: { store: 1, artifact-base: 1 }");
        dir.Phase("1-code-graph", "phase: 1\nname: code-graph\nstatus: done\nconsumes: { stor: 1 }\nprovides: { store: 1 }");

        var problems = Problems(dir.Check());

        Assert.Contains(problems, p => p.Contains("'stor'"));
        Assert.Contains(problems, p => p.Contains("'store' is also provided"));
        Assert.Contains(problems, p => p.StartsWith("roadmap: phase 0 shows 'done'"));
    }

    static List<string> Problems(JsonObject report) => [.. report["problems"]!.AsArray().Select(p => (string)p!)];

    sealed class PhaseDir : IDisposable
    {
        readonly string root = Directory.CreateTempSubdirectory("stangent-phases-").FullName;

        public PhaseDir(string roadmap)
        {
            Directory.CreateDirectory(Path.Combine(root, "phases"));
            Roadmap(roadmap);
        }

        public void Roadmap(string text) => File.WriteAllText(Path.Combine(root, "roadmap.md"), text);

        public void Phase(string file, string frontMatter) =>
            File.WriteAllText(Path.Combine(root, "phases", file + ".md"), $"---\n{frontMatter}\n---\n\n# body\n");

        public JsonObject Check() => PhaseCheck.Run(Path.Combine(root, "phases"), Path.Combine(root, "roadmap.md"));

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}

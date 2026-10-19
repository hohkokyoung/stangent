using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Graph.History;
using Stangent.Graph.Impact;

namespace Stangent.Core.Tests;

/// <summary>📜 history evidence (D34–D42): parsing, noise filters, counting, the build step and storage.</summary>
public class HistoryTests
{
    // The mechanics are tested with D35's starting values; the tuned defaults are a benchmark result, not a rule.
    static readonly StangentConfig.HistoryConfig Defaults = new() { Window = 20, MinShared = 3, MinShare = 0.3, MaxFiles = 30 };

    static HistoryCommit C(string hash, params string[] files) => new(hash, "dev", "dev@example.invalid", "change", files);

    static HashSet<string> Map(params string[] files) => [.. files];

    // ---- Parsing git log (D36, D38) ---------------------------------------------------------------

    static string Log(params (string Hash, string Status)[] commits) =>
        string.Concat(commits.Select(c => $"\u001e{c.Hash}\u001fdev\u001fdev@example.invalid\u001fmessage\n\u001f\n\n{c.Status}\n"));

    [Fact]
    public void Older_commits_see_a_moved_file_under_its_current_path()
    {
        var commits = HistoryLog.Parse(Log(
            ("c3", "M\tsrc/New/OrderService.cs"),
            ("c2", "R091\tsrc/Old/OrderService.cs\tsrc/New/OrderService.cs"),
            ("c1", "M\tsrc/Old/OrderService.cs\nM\tsrc/Old/Orders.cs")));

        Assert.Equal(["src/New/OrderService.cs"], commits[0].Files);
        Assert.Equal(["src/New/OrderService.cs"], commits[1].Files);
        Assert.Equal(["src/New/OrderService.cs", "src/Old/Orders.cs"], commits[2].Files);
    }

    [Fact]
    public void A_deleted_path_does_not_pass_its_history_to_a_new_file_with_the_same_name()
    {
        var commits = HistoryLog.Parse(Log(
            ("c3", "A\tsrc/Cache.cs"),
            ("c2", "D\tsrc/Cache.cs"),
            ("c1", "M\tsrc/Cache.cs\nM\tsrc/Other.cs")));

        Assert.Equal(["src/Cache.cs"], commits[0].Files);
        Assert.DoesNotContain("src/Cache.cs", commits[2].Files);
        Assert.Contains("src/Other.cs", commits[2].Files);
    }

    [Fact]
    public void Chained_renames_resolve_to_todays_path()
    {
        var commits = HistoryLog.Parse(Log(
            ("c3", "R100\tb/X.cs\tc/X.cs"),
            ("c2", "R100\ta/X.cs\tb/X.cs"),
            ("c1", "M\ta/X.cs")));

        Assert.All(commits, c => Assert.Equal(["c/X.cs"], c.Files));
    }

    // ---- Counting (D35) ---------------------------------------------------------------------------

    [Fact]
    public void A_partner_needs_three_shared_commits_and_thirty_percent_of_the_window()
    {
        // A changed in 10 commits: with B in 3 (30%), with C in 2.
        var commits = new List<HistoryCommit>();
        for (var i = 0; i < 10; i++)
        {
            List<string> files = ["A.cs"];
            if (i < 3) files.Add("B.cs");
            if (i is 3 or 4) files.Add("C.cs");
            commits.Add(C($"h{i}", [.. files]));
        }

        var result = HistoryCounter.Count(commits, Map("A.cs", "B.cs", "C.cs"), Defaults, []);

        var line = Assert.Single(result.Lines, l => l.Target == "A.cs");
        Assert.Equal(new HistoryLine("B.cs", "A.cs", 3, 10), line);
    }

    [Fact]
    public void Direction_matters_a_hub_as_the_starting_point_yields_little()
    {
        // Startup.cs changes in 20 commits, Small.cs in 3 of them, always with Startup.cs.
        var commits = Enumerable.Range(0, 20).Select(i => C($"h{i}", i < 3 ? ["Startup.cs", "Small.cs"] : ["Startup.cs", $"Other{i}.cs"])).ToList();
        var map = Map([.. commits.SelectMany(c => c.Files)]);

        var lines = HistoryCounter.Count(commits, map, Defaults, []).Lines;

        Assert.Contains(new HistoryLine("Startup.cs", "Small.cs", 3, 3), lines);   // Small.cs nearly always drags Startup.cs along
        Assert.DoesNotContain(lines, l => l.Target == "Startup.cs");              // 3 of 20 is under 30%
    }

    [Fact]
    public void Only_each_files_last_twenty_commits_count()
    {
        // Newest first: 20 recent commits of A without B, then 5 old ones with B.
        var commits = Enumerable.Range(0, 20).Select(i => C($"new{i}", "A.cs"))
            .Concat(Enumerable.Range(0, 5).Select(i => C($"old{i}", "A.cs", "B.cs"))).ToList();

        var lines = HistoryCounter.Count(commits, Map("A.cs", "B.cs"), Defaults, []).Lines;

        Assert.DoesNotContain(lines, l => l.Target == "A.cs");                    // B's commits fell out of A's window
        Assert.Contains(new HistoryLine("A.cs", "B.cs", 5, 5), lines);            // but B's own window still sees A
    }

    [Fact]
    public void Files_off_the_map_are_ignored()
    {
        var commits = Enumerable.Range(0, 3).Select(i => C($"h{i}", "A.cs", "package-lock.json")).ToList();

        var result = HistoryCounter.Count(commits, Map("A.cs"), Defaults, []);

        Assert.Empty(result.Lines);
        Assert.Equal(3, result.CommitsUsed);
    }

    [Fact]
    public void Seen_from_a_past_commit_only_older_history_counts()
    {
        // Newest first: a revert of x3, then three commits of A with B, then three of A alone.
        var commits = new List<HistoryCommit>
        {
            new("ddddddd1", "dev", "dev@example.invalid", "This reverts commit eeeeeee3.", ["Z.cs"]),
            C("eeeeeee1", "A.cs", "B.cs"), C("eeeeeee2", "A.cs", "B.cs"), C("eeeeeee3", "A.cs", "B.cs"),
            C("fffffff1", "A.cs"), C("fffffff2", "A.cs"), C("fffffff3", "A.cs"),
        };
        var index = new HistoryIndex(commits, Map("A.cs", "B.cs", "Z.cs"), [], []);
        var settings = new StangentConfig.HistoryConfig { Window = 20, MinShared = 2, MinShare = 0.3, MaxFiles = 30 };

        var beforeRevert = index.LinesFor(["A.cs"], index.Position("ddddddd1")!.Value, settings);
        var today = index.LinesFor(["A.cs"], -1, settings);
        var beforeAll = index.LinesFor(["A.cs"], index.Position("eeeeeee3")!.Value, settings);

        Assert.Equal([new HistoryLine("B.cs", "A.cs", 3, 6)], beforeRevert);   // the revert hadn't happened yet
        Assert.Equal([new HistoryLine("B.cs", "A.cs", 2, 5)], today);          // x3 and its revert both dropped
        Assert.Empty(beforeAll);                                               // only the three commits without B
    }

    // ---- Noise filters (D37) ----------------------------------------------------------------------

    static List<HistoryCommit> ThreeTogether() => [C("aaaaaaa1", "A.cs", "B.cs"), C("aaaaaaa2", "A.cs", "B.cs"), C("aaaaaaa3", "A.cs", "B.cs")];

    [Fact]
    public void A_revert_and_the_commit_it_reverts_are_both_dropped()
    {
        var commits = ThreeTogether();
        commits.Insert(0, new HistoryCommit("ccccccc1", "dev", "dev@example.invalid", "Revert \"x\"\n\nThis reverts commit aaaaaaa1.", ["A.cs", "B.cs"]));

        var result = HistoryCounter.Count(commits, Map("A.cs", "B.cs"), Defaults, []);

        Assert.Equal(2, result.Dropped["reverts"]);
        Assert.Empty(result.Lines); // two shared commits left, under three
    }

    [Fact]
    public void Formatting_sweeps_listed_in_blame_ignore_revs_are_dropped()
    {
        var result = HistoryCounter.Count(ThreeTogether(), Map("A.cs", "B.cs"), Defaults, ["aaaaaaa2"]);

        Assert.Equal(1, result.Dropped["formatting"]);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Bot_commits_are_dropped_by_suffix_and_by_list()
    {
        var commits = ThreeTogether();
        commits[0] = commits[0] with { Author = "dependabot[bot]" };
        commits[1] = commits[1] with { Author = "Weblate" };
        var settings = new StangentConfig.HistoryConfig { Window = 20, MinShared = 3, MinShare = 0.3, MaxFiles = 30, BotAuthors = ["weblate"] };

        var result = HistoryCounter.Count(commits, Map("A.cs", "B.cs"), settings, []);

        Assert.Equal(2, result.Dropped["bots"]);
    }

    [Fact]
    public void Commits_touching_more_than_thirty_files_on_the_map_are_dropped()
    {
        var huge = Enumerable.Range(0, 31).Select(i => $"F{i}.cs").ToArray();
        var commits = Enumerable.Range(0, 3).Select(i => C($"h{i}", huge)).ToList();

        var result = HistoryCounter.Count(commits, Map(huge), Defaults, []);

        Assert.Equal(3, result.Dropped["huge"]);
        Assert.Empty(result.Lines);
    }

    // ---- The build step, on a real repo (D36, D38, D41, D42) ---------------------------------------

    sealed class HistoryRepo : IDisposable
    {
        public HistoryRepo(string config = "version: 1\nhistory: { window: 20, minShared: 2, minShare: 0.3, maxFiles: 30 }\n")
        {
            Temp = new TempRepo(config);
            Git("init -q -b main");
            Commit("init", ("stangent.yaml", File.ReadAllText(Path.Combine(Root, StangentRepo.ConfigFile))));
        }

        public TempRepo Temp { get; }
        public string Root => Temp.Root;

        public void Git(string args) => ShopCopy.Git(Root, "-c user.name=dev -c user.email=dev@example.invalid " + args);

        public void Commit(string message, params (string Path, string Text)[] files)
        {
            foreach (var (path, text) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Root, path))!);
                File.WriteAllText(Path.Combine(Root, path), text);
            }
            Git("add -A");
            Git($"commit -q --allow-empty -m \"{message}\"");
        }

        public CodeGraph Graph(params string[] mapFiles)
        {
            var graph = new CodeGraph { Commit = Stangent.Graph.Git.Head(Root) };
            graph.LinkedNodes.AddRange(mapFiles.Select(f => new Node("file:" + f, NodeKind.File, f, File: f)));
            return graph;
        }

        public void Dispose() => Temp.Dispose();
    }

    static string Body(string name, int version) => $"namespace Shop.{name};\nclass {name} {{ int V = {version}; }}\n" + new string('/', 400) + "\n";

    [Fact]
    public void A_merged_branch_counts_once()
    {
        using var repo = new HistoryRepo();
        repo.Git("checkout -q -b feature");
        for (var i = 1; i <= 3; i++) repo.Commit($"wip {i}", ("A.cs", Body("A", i)), ("B.cs", Body("B", i)));
        repo.Git("checkout -q main");
        repo.Git("merge -q --no-ff feature -m \"Merge feature\"");
        var graph = repo.Graph("A.cs", "B.cs");

        HistoryStep.Apply(repo.Temp.Repo, graph, null, () => null, []);

        Assert.Equal("full", graph.History!.Status);
        Assert.Equal(1, graph.History.CommitsUsed);       // the merge, not its three branch commits
        Assert.Empty(graph.HistoryLines);                 // one shared change is under minShared 2
    }

    [Fact]
    public void A_moved_file_keeps_its_history_even_when_its_namespace_line_changed()
    {
        using var repo = new HistoryRepo();
        repo.Commit("one", ("Old/A.cs", Body("A", 1)), ("B.cs", Body("B", 1)));
        repo.Commit("two", ("Old/A.cs", Body("A", 2)), ("B.cs", Body("B", 2)));
        Directory.CreateDirectory(Path.Combine(repo.Root, "New"));
        repo.Git("mv Old/A.cs New/A.cs");
        File.WriteAllText(Path.Combine(repo.Root, "New/A.cs"), Body("A", 2).Replace("namespace Shop.A", "namespace Shop.New.A"));
        repo.Commit("move");
        var graph = repo.Graph("New/A.cs", "B.cs");

        HistoryStep.Apply(repo.Temp.Repo, graph, null, () => null, []);

        Assert.Contains(new HistoryLine("B.cs", "New/A.cs", 2, 3), graph.HistoryLines);
    }

    [Fact]
    public void Unchanged_inputs_reuse_the_stored_lines_and_a_new_commit_recomputes()
    {
        using var repo = new HistoryRepo();
        repo.Commit("one", ("A.cs", Body("A", 1)), ("B.cs", Body("B", 1)));
        repo.Commit("two", ("A.cs", Body("A", 2)), ("B.cs", Body("B", 2)));
        var first = repo.Graph("A.cs", "B.cs");
        HistoryStep.Apply(repo.Temp.Repo, first, null, () => null, []);
        var reused = repo.Graph("A.cs", "B.cs");
        // A previous graph whose fingerprint matches is reused as is: its (deliberately wrong) line proves it.
        first.HistoryLines.Add(new HistoryLine("X.cs", "A.cs", 9, 9));

        HistoryStep.Apply(repo.Temp.Repo, reused, first, () => null, []);
        repo.Commit("three", ("A.cs", Body("A", 3)));
        var recomputed = repo.Graph("A.cs", "B.cs");
        HistoryStep.Apply(repo.Temp.Repo, recomputed, first, () => null, []);

        Assert.Contains(new HistoryLine("X.cs", "A.cs", 9, 9), reused.HistoryLines);
        Assert.DoesNotContain(new HistoryLine("X.cs", "A.cs", 9, 9), recomputed.HistoryLines);
        Assert.Contains(new HistoryLine("B.cs", "A.cs", 2, 3), recomputed.HistoryLines);
    }

    [Fact]
    public void A_shallow_clone_keeps_the_lines_it_downloaded_and_says_so()
    {
        using var repo = new HistoryRepo();
        for (var i = 1; i <= 3; i++) repo.Commit($"c{i}", ("A.cs", Body("A", i)), ("B.cs", Body("B", i)));
        var downloaded = repo.Graph("A.cs", "B.cs");
        HistoryStep.Apply(repo.Temp.Repo, downloaded, null, () => null, []);
        Assert.NotEmpty(downloaded.HistoryLines);

        using var shallow = new TempRepo("version: 1\nhistory: { window: 20, minShared: 2, minShare: 0.3, maxFiles: 30 }\n");
        Directory.Delete(shallow.Root, recursive: true);
        ShopCopy.Git(Path.GetDirectoryName(shallow.Root)!, $"clone -q --depth 1 file://{repo.Root} {shallow.Root}");
        var repoAtShallow = StangentRepo.Discover(shallow.Root);
        var graph = new CodeGraph { Commit = Stangent.Graph.Git.Head(shallow.Root) };
        graph.LinkedNodes.AddRange(downloaded.LinkedNodes);
        var warnings = new List<string>();

        HistoryStep.Apply(repoAtShallow, graph, null, () => downloaded, warnings);

        Assert.Equal(downloaded.HistoryLines, graph.HistoryLines);
        Assert.Equal(downloaded.History!.Commit, graph.History!.Commit);
        Assert.Contains(warnings, w => w.StartsWith("history_shallow:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_blobless_clone_skips_rename_following_and_says_why()
    {
        using var repo = new HistoryRepo();
        repo.Commit("one", ("A.cs", Body("A", 1)));
        repo.Git("config remote.origin.partialclonefilter blob:none");
        var graph = repo.Graph("A.cs");
        var warnings = new List<string>();

        HistoryStep.Apply(repo.Temp.Repo, graph, null, () => null, warnings);

        Assert.False(graph.History!.RenamesFollowed);
        Assert.Contains(warnings, w => w.StartsWith("history_renames_skipped:", StringComparison.Ordinal));
    }

    [Fact]
    public void Switched_off_means_no_lines()
    {
        using var repo = new HistoryRepo("version: 1\nhistory: { enabled: false }\n");
        var graph = repo.Graph("A.cs");

        HistoryStep.Apply(repo.Temp.Repo, graph, null, () => null, []);

        Assert.Equal("off", graph.History!.Status);
        Assert.Empty(graph.HistoryLines);
    }

    // ---- Impact answers (impact-report v4, D40) ----------------------------------------------------

    const string AM = "App|M:App.A.Run", BM = "App|M:App.B.Run", CM = "App|M:App.C.Run";

    /// <summary>B.Run calls A.Run (✅). History: C.cs and B.cs usually change with A.cs, plus 12 weaker
    /// partners; X.cs usually changes with C.cs, which a walk through 📜 would wrongly reach.</summary>
    static CodeGraph ImpactGraph()
    {
        var graph = new CodeGraph { Commit = "abc1234def", History = new HistoryInfo("full", "abc1234def", "", 0, 0, new Dictionary<string, int>(), true, "") };
        foreach (var f in new[] { "A.cs", "B.cs", "C.cs", "X.cs" })
            graph.LinkedNodes.Add(new Node("file:" + f, NodeKind.File, f, File: f));
        graph.LinkedNodes.Add(new Node(AM, NodeKind.Method, "A.Run", File: "A.cs", Line: 1, EndLine: 5));
        graph.LinkedNodes.Add(new Node(BM, NodeKind.Method, "B.Run", File: "B.cs", Line: 1, EndLine: 5));
        graph.LinkedNodes.Add(new Node(CM, NodeKind.Method, "C.Run", File: "C.cs", Line: 1, EndLine: 5));
        graph.LinkedEdges.Add(new Edge(BM, AM, EdgeKind.Calls, Label.Certain));
        graph.HistoryLines.Add(new HistoryLine("B.cs", "A.cs", 5, 10));
        graph.HistoryLines.Add(new HistoryLine("C.cs", "A.cs", 7, 10));
        graph.HistoryLines.Add(new HistoryLine("X.cs", "C.cs", 9, 9));
        for (var i = 0; i < 12; i++) graph.HistoryLines.Add(new HistoryLine($"W{i:00}.cs", "A.cs", 3, 10));
        return graph;
    }

    static JsonObject Impact(CodeGraph graph, string? expand = null, int? maxDepth = null) =>
        new ImpactAnalyzer(graph).Analyze(new ChangeSpec("symbol", AM), "/nonexistent", expand, maxDepth);

    [Fact]
    public void History_is_its_own_capped_group_strongest_first_with_its_evidence()
    {
        var report = Impact(ImpactGraph());
        var history = report["history"]!;

        Assert.Empty(SchemaRegistry.Embedded.Validate("impact-report.v4.json", report.ToJsonString()));
        Assert.Equal(14, (int)history["total"]!);
        Assert.Equal(10, history["items"]!.AsArray().Count);
        Assert.Equal(4, (int)history["more"]!);
        var first = history["items"]![0]!;
        Assert.Equal(("C.cs", "A.cs", 7, 10), ((string)first["file"]!, (string)first["with"]!, (int)first["shared"]!, (int)first["of"]!));
        Assert.Null((string?)first["found"]);                               // only history finds C.cs
        Assert.Equal("certain", (string?)history["items"]![1]!["found"]);   // B.cs: the graph reached it too
    }

    [Fact]
    public void History_is_never_walked()
    {
        var report = Impact(ImpactGraph(), expand: "history");
        var all = report.ToJsonString();

        Assert.DoesNotContain("X.cs", all);                                  // 📜 of a 📜 partner
        Assert.DoesNotContain(CM, all);                                      // the walk didn't continue from C.cs
        Assert.Equal(14, report["history"]!["items"]!.AsArray().Count);
        Assert.Equal(0, (int)report["history"]!["more"]!);
    }

    [Fact]
    public void Graph_items_in_a_partner_file_are_tagged_also_history_and_keep_their_place()
    {
        var report = Impact(ImpactGraph());
        var code = report["groups"]!.AsArray().Single(g => (string)g!["name"]! == "code")!;

        var b = Assert.Single(code["items"]!.AsArray());
        Assert.Equal(BM, (string)b!["id"]!);
        Assert.Equal(1, (int)b["depth"]!);
        Assert.Equal(5, (int)b["history"]!["shared"]!);
    }

    [Fact]
    public void A_depth_limit_does_not_cut_history_and_markdown_lists_it_last()
    {
        var report = Impact(ImpactGraph(), maxDepth: 1);
        var md = ImpactAnalyzer.Markdown(report);

        Assert.Equal(14, (int)report["history"]!["total"]!);
        Assert.Contains("**📜 14 file(s) usually change with it**", md);
        Assert.Contains("`C.cs`: changed together in 7 of the last 10 commits of `A.cs`", md);
        Assert.Contains("`B.cs`: changed together in 5 of the last 10 commits of `A.cs` · also ✅ above", md);
        Assert.Contains("✅ `B.Run` 📜", md);
        Assert.True(md.IndexOf("📜 14", StringComparison.Ordinal) > md.IndexOf("other methods and types", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_history_the_group_is_empty_and_says_why()
    {
        var graph = ImpactGraph();
        graph.HistoryLines.Clear();
        graph.History = new HistoryInfo("off", "", "", 0, 0, new Dictionary<string, int>(), false, "");

        var report = Impact(graph);

        Assert.Equal("off", (string)report["history"]!["status"]!);
        Assert.Equal(0, (int)report["history"]!["total"]!);
        Assert.Empty(SchemaRegistry.Embedded.Validate("impact-report.v4.json", report.ToJsonString()));
    }

    // ---- Storage (code-graph v3) ------------------------------------------------------------------

    [Fact]
    public void History_survives_save_load_and_export_import_byte_for_byte()
    {
        using var repo = new HistoryRepo();
        for (var i = 1; i <= 3; i++) repo.Commit($"c{i}", ("A.cs", Body("A", i)), ("B.cs", Body("B", i)));
        var graph = repo.Graph("A.cs", "B.cs");
        HistoryStep.Apply(repo.Temp.Repo, graph, null, () => null, []);
        var db = Path.Combine(repo.Root, "g.sqlite");
        var export = Path.Combine(repo.Root, "g.jsonl.gz");

        GraphStore.Save(graph, db);
        var loaded = GraphStore.Load(db)!;
        GraphStore.Export(loaded, export);
        var imported = GraphStore.Import(export);

        Assert.Equal(graph.HistoryLines, loaded.HistoryLines);
        Assert.Equal(graph.History!.Fingerprint, loaded.History!.Fingerprint);
        Assert.Equal(GraphStore.CanonicalLines(graph), GraphStore.CanonicalLines(imported));
        Assert.Contains(GraphStore.CanonicalLines(graph), l => l.Contains("\"t\":\"history\""));
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.History;
using Stangent.Graph.Loading;

namespace Stangent.Benchmark.Split;

/// <summary>
/// Phase 1d part 1, the split check (D45–D49). Walks main-line commits newest first; for each change of
/// ticket size whose parent and change both build, the graph splits its changed members into groups, and each
/// group is applied alone onto the parent: step A builds it, step B runs the unit tests. No AI anywhere (D18).
/// </summary>
sealed class SplitCheck(string root, SplitConfig config, ISplitChecker checker, Func<Task<CodeGraph>> buildGraph,
    HistoryIndex? history, StangentConfig.HistoryConfig historySettings, Action afterCheckout, TextWriter log)
{
    public int MaxSplit { get; init; } = 30;
    public int MaxExamined { get; init; } = 200;
    public int MaxWalk { get; init; } = 2000;
    public int MinMembers { get; init; } = 3;
    public int MinFiles { get; init; } = 2;
    public int MaxFiles { get; init; } = 15;

    static readonly string[] SourceExtensions = [".cs", ".ts", ".tsx", ".js", ".jsx"];
    static readonly string[] BuildFiles = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.config", "nuget.config"];

    string current = "";
    readonly Dictionary<string, GraphView> graphs = new(StringComparer.Ordinal);
    readonly Dictionary<string, BuildResult> health = new(StringComparer.Ordinal);
    readonly Dictionary<(string Commit, string Project), Dictionary<string, bool>> testRuns = [];

    public async Task<JsonObject> RunAsync(string start)
    {
        if (GroupApplier.RecoverInterrupted(root)) log.WriteLine("split: removed files left by an interrupted run");
        current = "";
        EnsureAt(start);
        var excluded = DotnetChecker.Excluded(ProjectScanner.Find(root), config.BuildExclude);
        var excludedDirs = ProjectScanner.Find(root).Where(p => excluded.Contains(p.RelativePath))
            .Select(p => Path.GetDirectoryName(p.RelativePath)!.Replace('\\', '/') + "/").ToList();

        var walk = Encoding.UTF8.GetString(SplitDiff.Git(root, ["rev-list", "--first-parent", $"--max-count={MaxWalk}", start]))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var cases = new JsonArray();
        var skipped = new JsonArray();
        var skipCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var outcomes = new List<CaseOutcome>();
        var walked = 0;
        void Skip(string commit, string reason, IEnumerable<string>? detail = null)
        {
            skipCounts[reason] = skipCounts.GetValueOrDefault(reason) + 1;
            var entry = new JsonObject { ["commit"] = commit[..10], ["reason"] = reason };
            if (detail?.ToList() is { Count: > 0 } d) entry["detail"] = new JsonArray([.. d.Take(3).Select(x => (JsonNode)x)]);
            skipped.Add(entry);
        }

        foreach (var change in walk)
        {
            if (outcomes.Count(o => o.Split(Splitter.Within2)) >= MaxSplit || outcomes.Count >= MaxExamined) break;
            walked++;
            var parentText = Encoding.UTF8.GetString(SplitDiff.Git(root, ["rev-list", "--parents", "-n", "1", change])).Trim().Split(' ');
            if (parentText.Length < 2) { Skip(change, "no parent"); continue; }
            var parent = parentText[1];
            var clock = Stopwatch.StartNew();

            // ---- Size filter, cheap part (D47): file counts, then changed C# members by syntax.
            var fileCount = SplitDiff.FileCount(root, parent, change);
            if (fileCount > MaxFiles) { Skip(change, "more than 15 files"); continue; }
            var files = SplitDiff.Read(root, parent, change);
            if (files.Count(f => IsSource(f.Path)) < MinFiles) { Skip(change, "fewer than 2 source files"); continue; }
            if (files.Any(f => excludedDirs.Any(d => f.Path.StartsWith(d, StringComparison.Ordinal))))
            { Skip(change, "outside the build scope", config.BuildExclude); continue; }
            var frontendSource = files.Any(f => config.IsFrontend(f.Path) && IsSource(f.Path));
            if (!frontendSource && !SyntaxGate(files)) { Skip(change, "fewer than 3 changed members"); continue; }

            // ---- Graphs at the change and at its parent; exact changed members (D48).
            GraphView afterGraph, parentGraph;
            try
            {
                afterGraph = await Graph(change);
                parentGraph = await Graph(parent);
            }
            catch (Exception e) when (e is not OperationCanceledException) { Skip(change, "graph build failed", [e.Message]); continue; }
            var splitter = new Splitter(files, afterGraph, parentGraph);
            if (splitter.Members.Count < MinMembers || splitter.MemberFiles.Count < MinFiles) { Skip(change, "fewer than 3 changed members"); continue; }

            var rules = Rules(splitter, change);
            var anySplit = rules.Any(r => r.Split);
            var dotnet = files.Any(f => !config.IsFrontend(f.Path));
            var frontend = files.Any(f => config.IsFrontend(f.Path));

            // ---- Health (D46, D47): the parent and the change both build.
            var parentHealth = Health(parent, dotnet, frontend);
            if (!parentHealth.Ok) { Skip(change, "parent doesn't build", parentHealth.Errors); continue; }
            var changeHealth = Health(change, dotnet, frontend);
            if (!changeHealth.Ok) { Skip(change, "change doesn't build", changeHealth.Errors); continue; }

            var record = new JsonObject
            {
                ["commit"] = change,
                ["subject"] = Encoding.UTF8.GetString(SplitDiff.Git(root, ["log", "-1", "--format=%s", change])).Trim(),
                ["files"] = files.Count,
                ["changedMembers"] = splitter.Members.Count,
                ["memberFiles"] = splitter.MemberFiles.Count,
                ["hunks"] = splitter.Hunks,
                ["hunksSplitAtMembers"] = splitter.HunksSplit,
                ["shared"] = new JsonArray([.. splitter.SharedFiles.Select(f => (JsonNode)f)]),
            };
            var outcome = new CaseOutcome(change);
            if (!anySplit)
            {
                foreach (var r in rules) outcome.Rules[r.Rule] = (false, true);
                record["rules"] = RulesJson(rules, new Dictionary<string, GroupResult>());
                outcomes.Add(outcome);
                cases.Add(record);
                log.WriteLine($"split: {change[..10]} not split ({clock.Elapsed.TotalSeconds:F0} s)");
                continue;
            }

            // ---- Step B's baseline: tests passing on both the parent and the change (others are screened out).
            var testProjects = TestProjects(change, files);
            var changeTests = Tests(change, testProjects);
            var parentTests = Tests(parent, testProjects);
            var kept = parentTests.Where(t => t.Value && changeTests.GetValueOrDefault(t.Key)).Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
            record["tests"] = new JsonObject
            {
                ["projects"] = new JsonArray([.. testProjects.Select(p => (JsonNode)p)]),
                ["kept"] = kept.Count,
                ["screenedOut"] = parentTests.Keys.Union(changeTests.Keys).Count() - kept.Count,
            };

            // ---- Each group alone onto the parent: step A builds, step B tests (D46). Same text, same result.
            EnsureAt(parent);
            var applier = new GroupApplier(root, parent, change);
            var results = new Dictionary<string, GroupResult>(StringComparer.Ordinal);
            foreach (var r in rules.Where(r => r.Split))
                foreach (var g in r.Groups)
                {
                    var key = PatchKey(g);
                    if (results.ContainsKey(key)) continue;
                    var created = applier.Apply(files, splitter.Pieces, g);
                    try
                    {
                        afterCheckout();
                        var build = checker.Build(dotnet, frontend);
                        if (!build.Ok) { results[key] = new GroupResult(false, null, build.Errors, []); continue; }
                        var run = testProjects.Count == 0 || kept.Count == 0 ? [] : checker.Test(testProjects);
                        var failed = kept.Where(t => !run.GetValueOrDefault(t)).Order(StringComparer.Ordinal).ToList();
                        results[key] = new GroupResult(true, failed.Count == 0, [], failed);
                    }
                    finally
                    {
                        applier.Restore(created);
                        afterCheckout();
                    }
                }
            foreach (var r in rules)
                outcome.Rules[r.Rule] = (r.Split, !r.Split || r.Groups.All(g => results[PatchKey(g)].HoldsUp));
            record["rules"] = RulesJson(rules, results);
            outcomes.Add(outcome);
            cases.Add(record);
            log.WriteLine($"split: {change[..10]} split; {string.Join(", ", rules.Select(r => $"{r.Rule}: {r.Groups.Count} groups{(r.Split ? outcome.Rules[r.Rule].HoldsUp ? " hold" : " FAIL" : "")}"))} ({clock.Elapsed.TotalSeconds:F0} s)");
        }

        EnsureAt(start);
        return new JsonObject
        {
            ["startCommit"] = start,
            ["config"] = config.ToJson(),
            ["sizeFilter"] = $"≥ {MinMembers} changed members across ≥ {MinFiles} files, ≤ {MaxFiles} files changed",
            ["stop"] = $"{MaxSplit} split cases (judged rule) or {MaxExamined} examined",
            ["commitsWalked"] = walked,
            ["skipped"] = new JsonObject([.. skipCounts.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))]),
            ["verdict"] = SplitScore.Verdict(outcomes),
            ["rules"] = SplitScore.ByRule(outcomes),
            ["cases"] = cases,
            ["skippedCases"] = skipped,
        };
    }

    static bool IsSource(string path) => SourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The rules for one case (D48): judged, then the two reported beside it, then the baseline.</summary>
    List<RuleSplit> Rules(Splitter splitter, string change)
    {
        List<(string, string)> historyPairs = [];
        if (history?.Position(change) is { } at)
            historyPairs = [.. history.LinesFor(splitter.MemberFiles, at, historySettings)
                .Where(l => splitter.MemberFiles.Contains(l.Source) && splitter.MemberFiles.Contains(l.Target)).Select(l => (l.Source, l.Target))];
        return [splitter.Group(Splitter.Within2, 2), splitter.Group(Splitter.Direct, 1), splitter.Group(Splitter.Within2History, 2, historyPairs), splitter.ByFolder()];
    }

    /// <summary>The cheap half of the size filter: changed C# members by syntax (as Test 2 finds them).</summary>
    bool SyntaxGate(List<FileChange> files)
    {
        var withMembers = 0;
        var members = new HashSet<(string, ChangedMembers.Key)>();
        foreach (var f in files.Where(f => f.Path.EndsWith(".cs", StringComparison.Ordinal) && !f.Binary))
        {
            var keys = new HashSet<ChangedMembers.Key>();
            var added = f.Hunks.Where(h => h.NewCount > 0).Select(h => (h.NewStart, h.NewStart + h.NewCount - 1)).ToList();
            var removed = f.Hunks.Where(h => h.OldCount > 0).Select(h => (h.OldStart, h.OldStart + h.OldCount - 1)).ToList();
            if (added.Count > 0) keys.UnionWith(ChangedMembers.At(Encoding.UTF8.GetString([.. f.NewLines.SelectMany(l => l)]), added));
            if (removed.Count > 0) keys.UnionWith(ChangedMembers.At(Encoding.UTF8.GetString([.. f.OldLines.SelectMany(l => l)]), removed));
            if (keys.Count > 0) withMembers++;
            foreach (var k in keys) members.Add((f.Path, k));
        }
        return members.Count >= MinMembers && withMembers >= MinFiles;
    }

    void EnsureAt(string commit)
    {
        if (current == commit) return;
        SplitDiff.Git(root, ["checkout", "-q", "-f", commit]);
        afterCheckout();
        current = commit;
    }

    async Task<GraphView> Graph(string commit)
    {
        if (graphs.TryGetValue(commit, out var view)) return view;
        EnsureAt(commit);
        var clock = Stopwatch.StartNew();
        view = new GraphView(await buildGraph());
        log.WriteLine($"split: graph at {commit[..10]} in {clock.Elapsed.TotalSeconds:F0} s");
        // A walk only ever needs a commit and its parent; the next case's change is this case's parent.
        if (graphs.Count >= 3) graphs.Remove(graphs.Keys.First());
        graphs[commit] = view;
        return view;
    }

    BuildResult Health(string commit, bool dotnet, bool frontend)
    {
        var key = $"{commit}:{dotnet}:{frontend}";
        if (health.TryGetValue(key, out var cached)) return cached;
        EnsureAt(commit);
        return health[key] = checker.Build(dotnet, frontend);
    }

    Dictionary<string, bool> Tests(string commit, IReadOnlyList<string> projects)
    {
        if (projects.Count == 0) return [];
        var missing = projects.Where(p => !testRuns.ContainsKey((commit, p))).ToList();
        if (missing.Count > 0)
        {
            EnsureAt(commit);
            checker.Build(dotnet: true, frontend: false); // the outputs on disk may be another commit's
            var run = checker.Test(missing);
            foreach (var p in missing)
                testRuns[(commit, p)] = run.Where(t => t.Key.StartsWith(p + "::", StringComparison.Ordinal)).ToDictionary(StringComparer.Ordinal);
        }
        return projects.SelectMany(p => testRuns[(commit, p)]).ToDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Unit-test projects that reference a changed project, directly or not, minus integration tests (D46) and
    /// projects this machine can't build. A changed build file outside every project selects them all.
    /// </summary>
    List<string> TestProjects(string change, List<FileChange> files)
    {
        EnsureAt(change);
        var projects = ProjectScanner.Find(root);
        var excluded = DotnetChecker.Excluded(projects, config.BuildExclude);
        var pattern = new Regex(config.TestProjects);
        var dirs = projects.Select(p => (Dir: Path.GetDirectoryName(p.RelativePath)!.Replace('\\', '/'), p.RelativePath))
            .OrderByDescending(p => p.Dir.Length).ToList();
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var everything = false;
        foreach (var f in files.Where(f => !config.IsFrontend(f.Path)))
        {
            var owner = dirs.FirstOrDefault(d => d.Dir.Length == 0 || f.Path.StartsWith(d.Dir + "/", StringComparison.Ordinal));
            if (owner.RelativePath is not null && owner.Dir.Length > 0) changed.Add(owner.RelativePath);
            else if (BuildFiles.Contains(Path.GetFileName(f.Path)) || f.Path.EndsWith(".props", StringComparison.Ordinal) || f.Path.EndsWith(".targets", StringComparison.Ordinal))
                everything = true;
        }
        var byPath = projects.ToDictionary(p => p.RelativePath, StringComparer.Ordinal);
        bool Reaches(RepoProject p, HashSet<string> seen) =>
            changed.Contains(p.RelativePath) || p.References.Any(r => seen.Add(r) && byPath.TryGetValue(r, out var q) && Reaches(q, seen));
        return [.. projects
            .Where(p => pattern.IsMatch(p.Name) && !config.IntegrationMarkers.Any(m => p.Name.Contains(m, StringComparison.Ordinal)) && !excluded.Contains(p.RelativePath))
            .Where(p => everything || Reaches(p, new HashSet<string>(StringComparer.Ordinal)))
            .Select(p => p.RelativePath).Order(StringComparer.Ordinal)];
    }

    static string PatchKey(SplitGroup g) =>
        string.Join(',', g.Pieces.Order()) + "|" + string.Join(',', g.WholeFiles.Order(StringComparer.Ordinal));

    static JsonObject RulesJson(List<RuleSplit> rules, Dictionary<string, GroupResult> results)
    {
        var json = new JsonObject();
        foreach (var r in rules)
        {
            var groups = new JsonArray();
            foreach (var g in r.Groups)
            {
                var entry = new JsonObject
                {
                    ["members"] = g.Members.Count,
                    ["sample"] = new JsonArray([.. g.Members.Take(5).Select(m => (JsonNode)m)]),
                    ["files"] = new JsonArray([.. g.Files.Select(f => (JsonNode)f)]),
                };
                if (r.Split && results.TryGetValue(PatchKey(g), out var res))
                {
                    entry["compiles"] = res.Compiles;
                    entry["testsPass"] = res.TestsPass;
                    if (res.Errors.Count > 0) entry["errors"] = new JsonArray([.. res.Errors.Take(5).Select(e => (JsonNode)e)]);
                    if (res.FailedTests.Count > 0)
                    {
                        entry["failedTests"] = new JsonArray([.. res.FailedTests.Take(5).Select(t => (JsonNode)t)]);
                        entry["failedTestCount"] = res.FailedTests.Count;
                    }
                }
                groups.Add(entry);
            }
            json[r.Rule] = new JsonObject
            {
                ["groups"] = groups,
                ["split"] = r.Split,
                ["holdsUp"] = !r.Split || r.Groups.All(g => results[PatchKey(g)].HoldsUp),
                ["joinedBySharedHunk"] = r.JoinedBySharedHunk,
            };
        }
        return json;
    }
}

sealed record GroupResult(bool Compiles, bool? TestsPass, IReadOnlyList<string> Errors, IReadOnlyList<string> FailedTests)
{
    public bool HoldsUp => Compiles && TestsPass != false;
}

/// <summary>One examined case: per rule, whether it split and whether every group held up.</summary>
sealed class CaseOutcome(string commit)
{
    public string Commit { get; } = commit;
    public Dictionary<string, (bool Split, bool HoldsUp)> Rules { get; } = new(StringComparer.Ordinal);
    public bool Split(string rule) => Rules.TryGetValue(rule, out var r) && r.Split;
}

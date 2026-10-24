using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.History;
using Stangent.Graph.Impact;
using Stangent.Benchmark.Split;

// Scores impact analysis against a repo's own git history (decisions.md D28). No AI, no labels:
//   1. finds the rest of the change — from one file of a past commit, how many of its other files?
//   2. predicts what broke — for change → later-fix pairs (SZZ: blame the lines a fix touched),
//      does impact on the change include what the fix touched elsewhere?
//   3. stable and fast — two full builds byte-identical; full vs incremental build time.
//
// Each test is scored for several predictors on the same cases, so the graph is compared with
// baselines that cost nothing:
//   graph (lines)      the methods each commit changed, found by name in today's graph (line level)
//   graph (lines+deps) the same, plus what those methods directly use
//   graph (files)      every point in the changed file (the old, pessimistic seeding)
//   same project / same folder / co-change   no graph at all
// --exact-sample N rebuilds the graph at N past commits and compares the by-name answers with
// exact ones, which measures the error left by scoring old commits against today's graph.
//
//   dotnet run --project benchmark/Stangent.Benchmark -- <clone> [--months 0 (all)] [--max-commits 200]
//     [--max-fixes 400] [--exact-sample 0] [--sdk-override file] [--skip-build-test] [--out file]
//     [--compile-break N] [--only-compile-break]   (test 4: rename N methods, see what fails to compile)
//     [--openapi path/in/repo.json] [--ts-compile-break N] [--only-frontend]   (Phase 1b, D32)
//     [--split --split-config configs/<name>.split.json [--max-split 30] [--max-examined 200]]   (Phase 1d, D45–D49)
//     [--spec-stale N]   (Phase 2a: reference pages each of the last N main-line commits makes stale, D55)

var argList = args.ToList();
// Phase 2b: write the planted FSD from its outline (`--fsd-docx <outline.md> <out.docx>`), and score findings
// against the answer key (`--score-fsd <repo> <answer-key.json> [--hand-check <file>] [--out file]`).
if (argList.Count >= 3 && argList[0] == "--fsd-docx")
{
    File.WriteAllBytes(argList[2], Stangent.Spec.Fsd.DocxWriter.Write(File.ReadAllText(argList[1]), Path.GetDirectoryName(Path.GetFullPath(argList[1]))));
    return 0;
}
if (argList.Count >= 2 && argList[0] == "--page-stats")
{
    var stats = Stangent.Benchmark.Spec.PageStats.Run(Path.GetFullPath(argList[1]));
    var statsText = stats.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    if (argList.IndexOf("--out") is var po and >= 0) File.WriteAllText(argList[po + 1], statsText + "\n");
    Console.WriteLine(statsText);
    return 0;
}
if (argList.Count >= 3 && argList[0] == "--score-fsd")
{
    var hand = argList.IndexOf("--hand-check") is var hc and >= 0 ? argList[hc + 1] : null;
    var score = Stangent.Benchmark.Spec.FsdScore.Run(Path.GetFullPath(argList[1]), argList[2], hand);
    var scoreText = score.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    if (argList.IndexOf("--out") is var so and >= 0) File.WriteAllText(argList[so + 1], scoreText + "\n");
    Console.WriteLine(scoreText);
    return 0;
}
// Picks 📜's defaults from tuning runs (D43): `--select-tuning <tune.json>... [--out file]`.
if (argList.Count > 0 && argList[0] == "--select-tuning")
    return Tuning.Select([.. argList.Skip(1).TakeWhile(a => !a.StartsWith('-'))], argList.IndexOf("--out") is var o and >= 0 ? argList[o + 1] : null);
if (argList.Count == 0 || argList[0].StartsWith('-'))
{
    Console.Error.WriteLine("usage: Stangent.Benchmark <clone> [--months 0] [--max-commits 200] [--max-fixes 400] [--exact-sample 0] [--sdk-override file] [--skip-build-test] [--out file]");
    return 2;
}
string Opt(string name, string fallback) => argList.IndexOf(name) is var i and >= 0 && i + 1 < argList.Count ? argList[i + 1] : fallback;
var root = Path.GetFullPath(argList[0]);
var months = int.Parse(Opt("--months", "0")); // 0 = all history (D33)
var maxCommits = int.Parse(Opt("--max-commits", "200"));
var maxFixes = int.Parse(Opt("--max-fixes", "400"));
var exactSample = int.Parse(Opt("--exact-sample", "0"));
var sdkOverride = Opt("--sdk-override", "");
var skipBuildTest = argList.Contains("--skip-build-test");
var compileBreakCount = int.Parse(Opt("--compile-break", "0"));
var onlyCompileBreak = argList.Contains("--only-compile-break");
var openApi = Opt("--openapi", "");
var tsCompileBreakCount = int.Parse(Opt("--ts-compile-break", "0"));
var onlyFrontend = argList.Contains("--only-frontend");
var expectedLinks = Opt("--expected", "");
var tune = argList.Contains("--tune");
var historyOverride = Opt("--history-settings", ""); // "window,minShared,minShare,maxFiles"
var buildCount = 0;

// A run that was stopped mid-rename or mid-touch leaves its edit behind: put such files back first.
foreach (var marker in new[] { "__StangentBroken", "stangent benchmark touch" })
    foreach (var leftover in (Git.Run(root, $"grep -l \"{marker}\"") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        Console.Error.WriteLine($"benchmark: restoring {leftover}, left edited by an interrupted run");
        Git.Run(root, $"checkout -- \"{leftover}\"");
    }

var repo = StangentRepo.Discover(root);
if (argList.Contains("--spec-stale"))
{
    var report = await Stangent.Benchmark.Spec.SpecStale.RunAsync(root, repo, int.Parse(Opt("--spec-stale", "50")), ApplySdkOverride, Console.Error);
    var reportText = report.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    if (Opt("--out", "") is { Length: > 0 } reportFile) File.WriteAllText(reportFile, reportText + "\n");
    Console.WriteLine(reportText);
    return 0;
}
var graph = GraphStore.Load(GraphStore.PathFor(repo)) ?? throw new InvalidOperationException("Build the graph first: stangent graph build");
// History tests need every commit; the frontend and compile-break checks don't.
if (!onlyFrontend && !onlyCompileBreak && Run("rev-parse --is-shallow-repository").Trim() == "true")
    throw new InvalidOperationException("Needs full history: git fetch --filter=blob:none --unshallow");
var analyzer = new ImpactAnalyzer(graph);
var members = new MemberIndex(graph);
var fileNodes = graph.AllNodes.Where(n => n.Kind == NodeKind.File && IsSource(n.Name))
    .GroupBy(n => n.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

// Commits on main's own line, newest first: hash, subject, source files that still exist on the map.
// A merged PR counts once, diffed against main before it; its branch commits aren't listed (D36).
var commits = new List<(string Hash, string Subject, List<string> Files)>();
var since = months > 0 ? $"--since=\"{months} months ago\" " : "";
foreach (var block in Run($"log --first-parent --diff-merges=first-parent {since}--name-only --format=@@%H%x09%s HEAD").Split("@@", StringSplitOptions.RemoveEmptyEntries))
{
    var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var head = lines[0].Split('\t', 2);
    commits.Add((head[0], head.Length > 1 ? head[1] : "", [.. lines.Skip(1).Where(fileNodes.ContainsKey).Distinct().Order(StringComparer.Ordinal)]));
}
var commitIndex = commits.Select((c, i) => (c.Hash, i)).ToDictionary(x => x.Hash, x => x.i, StringComparer.Ordinal);

// 📜 as graph build computes it, but seen from just before each scored change: no peeking ahead (D44).
var historySettings = repo.Config.History;
if (historyOverride.Length > 0)
{
    var h = historyOverride.Split(',');
    historySettings = new StangentConfig.HistoryConfig
    {
        Window = int.Parse(h[0]), MinShared = int.Parse(h[1]), MinShare = double.Parse(h[2], System.Globalization.CultureInfo.InvariantCulture),
        MaxFiles = int.Parse(h[3]), BotAuthors = repo.Config.History.BotAuthors, FollowRenames = repo.Config.History.FollowRenames,
    };
}
var mapFiles = graph.AllNodes.Where(n => n.Kind is NodeKind.File or NodeKind.Template && n.File is not null).Select(n => n.File!).ToHashSet(StringComparer.Ordinal);
var renamesFollowed = graph.History?.RenamesFollowed ?? historySettings.FollowRenames;
var ignoreRevsPath = Path.Combine(root, ".git-blame-ignore-revs");
var ignoreRevs = File.Exists(ignoreRevsPath)
    ? File.ReadAllLines(ignoreRevsPath).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length >= 7).Select(l => l.Split(' ')[0]).ToList() : [];
var historyIndex = new HistoryIndex(
    HistoryLog.Read(root, renamesFollowed, mapFiles.Select(Path.GetExtension).Where(e => !string.IsNullOrEmpty(e)).Distinct().Select(e => e!)) ?? [],
    mapFiles, ignoreRevs, historySettings.BotAuthors);
// The index at today's commit must give exactly the lines graph build stored (same code, same inputs).
var historySelfCheck = historyOverride.Length == 0 && graph.History is { Status: "full" }
    ? historyIndex.Count(historySettings).Lines.SequenceEqual(graph.HistoryLines) : (bool?)null;
Console.Error.WriteLine($"history: self-check {historySelfCheck?.ToString() ?? "skipped"}");

// ---- Phase 1d part 1: the split check (D45–D49) ----------------------------------------------------
if (argList.Contains("--split"))
{
    // An interrupted run leaves the tree at some past commit: start again from the commit it started at.
    var startFile = Path.Combine(root, ".stangent", "split-start");
    var start = File.Exists(startFile) ? File.ReadAllText(startFile).Trim() : graph.Commit;
    File.WriteAllText(startFile, start);
    var splitConfig = Opt("--split-config", "") is { Length: > 0 } cfg ? SplitConfig.Load(cfg) : new SplitConfig();
    var check = new SplitCheck(root, splitConfig, new DotnetChecker(root, splitConfig, Path.Combine(root, ".stangent", "split"), Console.Error),
        async () => (await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: true))).Graph,
        historyIndex, historySettings, ApplySdkOverride, Console.Error)
    {
        MaxSplit = int.Parse(Opt("--max-split", "30")),
        MaxExamined = int.Parse(Opt("--max-examined", "200")),
        MaxWalk = int.Parse(Opt("--max-walk", "2000")),
        ProgressFile = Path.Combine(root, ".stangent", "split-progress.jsonl"),
        // `--groups-only <results.json>`: re-group the cases that run examined, applying nothing.
        GroupsOnly = Opt("--groups-only", "") is { Length: > 0 } earlier
            ? JsonNode.Parse(File.ReadAllText(earlier))!["cases"]!.AsArray().Select(c => (string)c!["commit"]!).ToHashSet(StringComparer.Ordinal) : null,
    };
    var split = await check.RunAsync(start);
    split.Insert(0, "repo", Path.GetFileName(root));
    split.Insert(1, "note", "Phase 1d part 1 (D45–D49): each change's members grouped by the graph at the change; each group applied alone onto the parent, then built (step A) and unit-tested (step B). Judged on 'within 2 steps (✅+🟡)'.");
    split.Insert(2, "historySettings", $"window {historySettings.Window}, minShared {historySettings.MinShared}, minShare {historySettings.MinShare.ToString(System.Globalization.CultureInfo.InvariantCulture)}, maxFiles {historySettings.MaxFiles} (📜 from commits older than each change)");
    // Back at the start commit: rebuild its graph, so the clone's stored graph is the pinned one again.
    await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: true));
    File.Delete(startFile);
    File.Delete(Path.Combine(root, ".stangent", "split-progress.jsonl"));
    var splitText = split.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    if (Opt("--out", "") is { Length: > 0 } splitFile) File.WriteAllText(splitFile, splitText + "\n");
    Console.WriteLine(splitText);
    return 0;
}

// ---- Seeds: what a commit changed in one file -----------------------------------------------
var matchStats = new Dictionary<string, int> { ["sameFile"] = 0, ["moved"] = 0, ["unmatched"] = 0 };
var seedCache = new Dictionary<(string, string), List<Node>>();
List<Node> ChangedNodes(string commit, string file)
{
    if (seedCache.TryGetValue((commit, file), out var cached)) return cached;
    var diff = Run($"diff -U0 {commit}^ {commit} -- \"{file}\"");
    var keys = new HashSet<ChangedMembers.Key>();
    var added = NewRanges(diff).ToList();
    if (added.Count > 0) keys.UnionWith(ChangedMembers.At(Run($"show {commit}:\"{file}\""), added));
    var removed = OldRanges(diff).ToList();
    if (removed.Count > 0) keys.UnionWith(ChangedMembers.At(Run($"show {commit}^:\"{file}\""), removed));
    var nodes = new List<Node>();
    foreach (var key in keys)
    {
        var (found, how) = members.Find(file, key);
        matchStats[how switch { MemberIndex.Match.SameFile => "sameFile", MemberIndex.Match.Moved => "moved", _ => "unmatched" }]++;
        nodes.AddRange(found);
    }
    return seedCache[(commit, file)] = [.. nodes.DistinctBy(n => n.Id)];
}

// ---- Predictors ------------------------------------------------------------------------------
Dictionary<string, Label> FromNodes(IEnumerable<(Node Node, Label Label)> affected, IReadOnlySet<string> exclude)
{
    var predicted = new Dictionary<string, Label>(StringComparer.Ordinal);
    foreach (var (node, label) in affected)
        if (node.File is { } f && IsSource(f) && !exclude.Contains(f) && (!predicted.TryGetValue(f, out var l) || label < l))
            predicted[f] = label;
    return predicted;
}

var outgoing = graph.AllEdges.Where(e => e.Kind is not (EdgeKind.Contains or EdgeKind.DispatchesTo)).ToLookup(e => e.Source, StringComparer.Ordinal);
var nodeById = graph.AllNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

Dictionary<string, Label> GraphLines(List<Node> seeds, IReadOnlySet<string> exclude) => FromNodes(analyzer.Affected(seeds), exclude);
// Only points within this many lines of the change: direct callers are far likelier to break than distant ones.
Dictionary<string, Label> GraphLinesDepth(List<Node> seeds, IReadOnlySet<string> exclude, int maxDepth) =>
    FromNodes(analyzer.AffectedWithDepth(seeds).Where(a => a.Depth <= maxDepth).Select(a => (a.Node, a.Label)), exclude);
Dictionary<string, Label> GraphLinesDeps(List<Node> seeds, IReadOnlySet<string> exclude)
{
    var predicted = GraphLines(seeds, exclude);
    var deps = seeds.SelectMany(s => outgoing[s.Id]).Where(e => nodeById.ContainsKey(e.Target)).Select(e => (nodeById[e.Target], e.Label));
    foreach (var (file, label) in FromNodes(deps, exclude))
        if (!predicted.TryGetValue(file, out var l) || label < l) predicted[file] = label;
    return predicted;
}
Dictionary<string, Label> GraphFiles(IEnumerable<string> files, IReadOnlySet<string> exclude) =>
    FromNodes(analyzer.Affected([.. files.SelectMany(analyzer.InFile).DistinctBy(n => n.Id)]), exclude);
Dictionary<string, Label> SameProject(IEnumerable<string> files, IReadOnlySet<string> exclude)
{
    var projects = files.Select(f => fileNodes[f].Project).ToHashSet();
    return fileNodes.Values.Where(n => projects.Contains(n.Project) && !exclude.Contains(n.Name)).ToDictionary(n => n.Name, _ => Label.Certain, StringComparer.Ordinal);
}
Dictionary<string, Label> SameFolder(IEnumerable<string> files, IReadOnlySet<string> exclude)
{
    var folders = files.Select(Path.GetDirectoryName).ToHashSet();
    return fileNodes.Keys.Where(f => folders.Contains(Path.GetDirectoryName(f)) && !exclude.Contains(f)).ToDictionary(f => f, _ => Label.Certain, StringComparer.Ordinal);
}
// Files that changed together with these at least twice in commits *older* than this one (no peeking ahead).
Dictionary<string, int> CoChangeCounts(IEnumerable<string> files, string commit, IReadOnlySet<string> exclude)
{
    var seeds = files.ToHashSet(StringComparer.Ordinal);
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var c in commits.Skip(commitIndex[commit] + 1).Where(c => c.Files.Count <= 30 && c.Files.Any(seeds.Contains)))
        foreach (var f in c.Files.Where(f => !seeds.Contains(f))) counts[f] = counts.GetValueOrDefault(f) + 1;
    return counts.Where(kv => kv.Value >= 2 && !exclude.Contains(kv.Key)).ToDictionary(StringComparer.Ordinal);
}
// A fix blamed on several changes is one case: each change predicts on its own history, and the case
// takes the highest count per file (D33).
Dictionary<string, int> CoChangeCountsFor(Case c)
{
    var merged = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var (commit, files) in c.Parts ?? [(c.Commit, c.SeedFiles)])
        foreach (var (f, n) in CoChangeCounts(files, commit, c.Exclude))
            merged[f] = Math.Max(merged.GetValueOrDefault(f), n);
    return merged;
}
Dictionary<string, Label> CoChange(Case c) => CoChangeCountsFor(c).ToDictionary(kv => kv.Key, _ => Label.Certain, StringComparer.Ordinal);
// The top n by count (ties by path), as a capped impact group would show them (D40, Gate 1 in D44).
Dictionary<string, Label> CoChangeTop(Case c, int n) => CoChangeCountsFor(c).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
    .Take(n).ToDictionary(kv => kv.Key, _ => Label.Guessed, StringComparer.Ordinal);
// 📜's top n for a case, as an impact answer would show it, from history older than each change (D40).
Dictionary<string, Label> HistoryTop(Case c, int n, StangentConfig.HistoryConfig? settings = null)
{
    var best = new Dictionary<string, HistoryLine>(StringComparer.Ordinal);
    foreach (var (commit, files) in c.Parts ?? [(c.Commit, c.SeedFiles)])
    {
        if (historyIndex.Position(commit) is not { } at) continue;
        var seeds = files.ToHashSet(StringComparer.Ordinal);
        foreach (var l in historyIndex.LinesFor(files, at, settings ?? historySettings))
            if (!seeds.Contains(l.Source) && !c.Exclude.Contains(l.Source) && IsSource(l.Source)
                && (!best.TryGetValue(l.Source, out var have) || ImpactAnalyzer.Stronger(l, have)))
                best[l.Source] = l;
    }
    return ImpactAnalyzer.Rank(best.Values).Take(n).ToDictionary(l => l.Source, _ => Label.History, StringComparer.Ordinal);
}
static Dictionary<string, Label> Union(Dictionary<string, Label> a, Dictionary<string, Label> b)
{
    var u = new Dictionary<string, Label>(a, StringComparer.Ordinal);
    foreach (var (f, l) in b) if (!u.TryGetValue(f, out var have) || l < have) u[f] = l;
    return u;
}

// ---- Scoring ---------------------------------------------------------------------------------
JsonObject Metrics(IEnumerable<(HashSet<string> Actual, Dictionary<string, Label> Predicted)> cases, Label max)
{
    int hits = 0, actual = 0, predicted = 0, n = 0;
    var sizes = new List<int>();
    var perCasePrecision = new List<double>();
    foreach (var (a, p) in cases)
    {
        var set = p.Where(kv => kv.Value <= max).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        var h = a.Count(set.Contains);
        hits += h; actual += a.Count; predicted += set.Count; n++;
        sizes.Add(set.Count);
        if (set.Count > 0) perCasePrecision.Add((double)h / set.Count);
    }
    sizes.Sort();
    perCasePrecision.Sort();
    return new JsonObject
    {
        ["recall"] = actual == 0 ? 0 : Math.Round((double)hits / actual, 3),
        ["precision"] = predicted == 0 ? 0 : Math.Round((double)hits / predicted, 3),
        // Pooled precision is dominated by a few huge answers; the median case shows the typical one.
        ["medianCasePrecision"] = perCasePrecision.Count == 0 ? 0 : Math.Round(perCasePrecision[perCasePrecision.Count / 2], 3),
        ["found"] = hits, ["of"] = actual,
        ["medianPredictedFiles"] = sizes.Count == 0 ? 0 : sizes[sizes.Count / 2],
        ["emptyAnswers"] = sizes.Count(s => s == 0),
    };
}

JsonObject Compare(List<Case> cases)
{
    var result = new JsonObject { ["cases"] = cases.Count };
    // Backend change → frontend files: the same cases, scored only on the frontend files they touched.
    var crossStack = cases.Select(c => c with { Actual = c.Actual.Where(f => !IsCSharp(f)).ToHashSet(StringComparer.Ordinal) })
        .Where(c => c.Actual.Count > 0).ToList();
    if (crossStack.Count > 0)
        result["backendToFrontend"] = new JsonObject
        {
            ["cases"] = crossStack.Count,
            ["graph (lines)"] = Metrics(crossStack.Select(c => (c.Actual, GraphLines(c.Seeds, c.Exclude))), Label.Guessed),
            ["same folder"] = Metrics(crossStack.Select(c => (c.Actual, SameFolder(c.SeedFiles, c.Exclude))), Label.Guessed),
            ["co-change"] = Metrics(crossStack.Select(c => (c.Actual, CoChange(c))), Label.Guessed),
        };
    foreach (var (name, pick) in Predictors())
    {
        var scored = cases.Select(c => (c.Actual, pick(c))).ToList();
        result[name] = name.StartsWith("graph", StringComparison.Ordinal)
            ? name.Contains("📜", StringComparison.Ordinal)
                ? new JsonObject { ["certain+guessed"] = Metrics(scored, Label.Guessed), ["certain+guessed+history"] = Metrics(scored, Label.History) }
                : new JsonObject { ["certain"] = Metrics(scored, Label.Certain), ["certain+guessed"] = Metrics(scored, Label.Guessed) }
            : Metrics(scored, Label.History);
    }
    return result;
}

// Gate 1 (D44): does adding the co-change baseline's top 10 to "graph within 2 lines" raise recall?
// 95% ranges come from resampling fixes (1,000 times, fixed seed), so a re-run prints the same numbers.
JsonObject Gate1(List<Case> cases) => Gate(cases, "co-change (top 10)", c => CoChangeTop(c, 10), gate2: false);
JsonObject Gate2(List<Case> cases) => Gate(cases, "📜 (top 10)", c => HistoryTop(c, 10), gate2: true);

// Gate 1 and Gate 2 (D44) on the same cases: "graph within 2 lines" with and without a history predictor.
// Gate 2 also checks 📜 against the plain co-change baseline's top 10 and how much longer answers get.
JsonObject Gate(List<Case> cases, string historyName, Func<Case, Dictionary<string, Label>> historyOf, bool gate2)
{
    const int Floor = 30, Resamples = 1000;
    var graphOnly = cases.Select(c => GraphLinesDepth(c.Seeds, c.Exclude, 2).Keys.ToHashSet(StringComparer.Ordinal)).ToList();
    var history = cases.Select(c => historyOf(c).Keys.ToHashSet(StringComparer.Ordinal)).ToList();
    var withHistory = cases.Select((c, i) => graphOnly[i].Union(history[i]).ToHashSet(StringComparer.Ordinal)).ToList();
    var hitsG = cases.Select((c, i) => c.Actual.Count(graphOnly[i].Contains)).ToArray();
    var hitsGH = cases.Select((c, i) => c.Actual.Count(withHistory[i].Contains)).ToArray();
    var hitsH = cases.Select((c, i) => c.Actual.Count(history[i].Contains)).ToArray();
    var actual = cases.Select(c => c.Actual.Count).ToArray();

    double Ratio(IEnumerable<int> idx, int[] num, Func<int, int> den) { long n = 0, d = 0; foreach (var i in idx) { n += num[i]; d += den(i); } return d == 0 ? 0 : (double)n / d; }
    var rng = new Random(20261019);
    var draws = Enumerable.Range(0, Resamples).Select(_ => Enumerable.Range(0, cases.Count).Select(_ => rng.Next(cases.Count)).ToArray()).ToList();
    JsonArray Range95(Func<int[], double> stat)
    {
        if (cases.Count == 0) return [0, 0];
        var values = draws.Select(stat).Order().ToList();
        return [Math.Round(values[(int)(0.025 * Resamples)], 3), Math.Round(values[(int)(0.975 * Resamples) - 1], 3)];
    }
    var all = Enumerable.Range(0, cases.Count).ToArray();
    JsonObject Score(int[] hits, List<HashSet<string>> predicted) => new()
    {
        ["recall"] = Math.Round(Ratio(all, hits, i => actual[i]), 3),
        ["recallRange95"] = Range95(d => Ratio(d, hits, i => actual[i])),
        ["precision"] = Math.Round(Ratio(all, hits, i => predicted[i].Count), 3),
        ["precisionRange95"] = Range95(d => Ratio(d, hits, i => predicted[i].Count)),
        ["medianPredictedFiles"] = predicted.Count == 0 ? 0 : predicted.Select(p => p.Count).Order().ElementAt(predicted.Count / 2),
    };
    var gainRange = Range95(d => Ratio(d, hitsGH, i => actual[i]) - Ratio(d, hitsG, i => actual[i]));
    var result = new JsonObject
    {
        ["fixes"] = cases.Count,
        ["countsTowardVerdict"] = cases.Count >= Floor,
        ["graph (lines, depth ≤ 2)"] = Score(hitsG, graphOnly),
        [historyName] = Score(hitsH, history),
        [$"graph (lines, depth ≤ 2) + {historyName}"] = Score(hitsGH, withHistory),
        ["recallGain"] = Math.Round(Ratio(all, hitsGH, i => actual[i]) - Ratio(all, hitsG, i => actual[i]), 3),
        ["recallGainRange95"] = gainRange,
        ["historyHelps"] = cases.Count >= Floor && (double)gainRange[0]! > 0,
    };
    if (!gate2) return result;

    var baseline = cases.Select(c => CoChangeTop(c, 10).Keys.ToHashSet(StringComparer.Ordinal)).ToList();
    var hitsB = cases.Select((c, i) => c.Actual.Count(baseline[i].Contains)).ToArray();
    var (recallH, precisionH) = (Ratio(all, hitsH, i => actual[i]), Ratio(all, hitsH, i => history[i].Count));
    var (recallB, precisionB) = (Ratio(all, hitsB, i => actual[i]), Ratio(all, hitsB, i => baseline[i].Count));
    var added = cases.Select((_, i) => withHistory[i].Count - graphOnly[i].Count).Order().ToList();
    var medianAdded = added.Count == 0 ? 0 : added[added.Count / 2];
    result["co-change (top 10)"] = Score(hitsB, baseline);
    result["medianFilesAdded"] = medianAdded;
    result["checks"] = new JsonObject
    {
        ["addsSomething"] = cases.Count >= Floor && (double)gainRange[0]! > 0,
        ["filtersHelpNotHurt"] = precisionH >= precisionB && recallH >= 0.9 * recallB,
        ["answersStayShort"] = medianAdded <= 10,
    };
    return result;
}

IEnumerable<(string, Func<Case, Dictionary<string, Label>>)> Predictors() =>
[
    ("graph (lines)", c => GraphLines(c.Seeds, c.Exclude)),
    ("graph (lines, depth ≤ 1)", c => GraphLinesDepth(c.Seeds, c.Exclude, 1)),
    ("graph (lines, depth ≤ 2)", c => GraphLinesDepth(c.Seeds, c.Exclude, 2)),
    ("graph (lines, depth ≤ 3)", c => GraphLinesDepth(c.Seeds, c.Exclude, 3)),
    ("graph (lines+deps)", c => GraphLinesDeps(c.Seeds, c.Exclude)),
    ("graph (files)", c => GraphFiles(c.SeedFiles, c.Exclude)),
    ("same project", c => SameProject(c.SeedFiles, c.Exclude)),
    ("same folder", c => SameFolder(c.SeedFiles, c.Exclude)),
    ("co-change", c => CoChange(c)),
    ("co-change (top 10)", c => CoChangeTop(c, 10)),
    ("graph (lines, depth ≤ 2) + co-change (top 10)", c => Union(GraphLinesDepth(c.Seeds, c.Exclude, 2), CoChangeTop(c, 10))),
    ("📜 (top 10)", c => HistoryTop(c, 10)),
    ("graph (lines, depth ≤ 2) + 📜 (top 10)", c => Union(GraphLinesDepth(c.Seeds, c.Exclude, 2), HistoryTop(c, 10))),
];

// ---- Frontend checks alone (D32): OpenAPI answer key and TypeScript compile-break ---------------
if (onlyFrontend)
{
    var feOut = new JsonObject
    {
        ["repo"] = Path.GetFileName(root), ["graphCommit"] = graph.Commit,
        ["openApiCheck"] = openApi.Length > 0 ? OpenApiCheck.Run(graph, Path.Combine(root, openApi)) : null,
        ["test4TypeScriptCompileBreak"] = tsCompileBreakCount > 0 ? TsCompileBreak.Run(root, graph, tsCompileBreakCount, GraphPredict, SameFolder) : null,
        ["expectedLinks"] = expectedLinks.Length > 0 ? ExpectedLinks(expectedLinks) : null,
    };
    var feText = feOut.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    if (Opt("--out", "") is { Length: > 0 } feFile) File.WriteAllText(feFile, feText + "\n");
    Console.WriteLine(feText);
    return 0;
}

// ---- Test 4 alone (slow tests skipped) -------------------------------------------------------
if (onlyCompileBreak)
{
    var onlyOut = new JsonObject
    {
        ["repo"] = Path.GetFileName(root), ["graphCommit"] = graph.Commit,
        ["test4CompileBreak"] = await CompileBreakTest(),
    };
    var onlyText = onlyOut.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    if (Opt("--out", "") is { Length: > 0 } onlyFile) File.WriteAllText(onlyFile, onlyText + "\n");
    Console.WriteLine(onlyText);
    return 0;
}

// ---- Test 1: leave one out --------------------------------------------------------------------
var test1Commits = Sample(commits.Where(c => c.Files.Count is >= 2 and <= 15).ToList(), maxCommits);
var test1 = new List<Case>();
var test1NoSeeds = 0;
foreach (var c in test1Commits)
    foreach (var seed in c.Files.Where(IsCSharp)) // changed members are found by C# syntax; frontend files count as targets
    {
        var nodes = ChangedNodes(c.Hash, seed);
        if (nodes.Count == 0) { test1NoSeeds++; continue; }
        test1.Add(new Case(c.Hash, [seed], nodes, [.. c.Files.Where(f => f != seed)], [seed]));
    }
Console.Error.WriteLine($"test 1: {test1Commits.Count} commits, {test1.Count} cases ({test1NoSeeds} skipped: no changed member found)");

// ---- Test 2: SZZ pairs ------------------------------------------------------------------------
var fixPattern = new Regex(@"\b(fix(e[sd])?|bug|regression|broke|broken|crash(es|ed)?|revert(s|ed)?)\b", RegexOptions.IgnoreCase);
var byHash = commits.ToDictionary(c => c.Hash, c => c, StringComparer.Ordinal);
var fixCandidates = commits.Where(c => fixPattern.IsMatch(c.Subject) && c.Files.Count is >= 1 and <= 15).ToList();
var fixCommitsMatching = fixCandidates.Count;
var fixes = Sample(fixCandidates, maxFixes);
// Mixed squash-merged PRs ("A collection of bug fixes", "Polish 6") are scored as their own slice too (D33).
var bundlePattern = new Regex(@"\b(fixes|polish(es)?|misc|bunch|collection|various|random|several|a few|lots? of|and more|tech debt)\b", RegexOptions.IgnoreCase);
// One case per fix, however many changes it's blamed on (D33): seeds from every change, targets are
// the fix's files outside all of them. Blame follows main's own line, so a change is the PR that landed it (D36).
var pairCount = 0;
var trivial = 0;
var fixCases = new List<(string Fix, List<string> Changes, HashSet<string> Elsewhere)>();
foreach (var fix in fixes)
{
    var introducing = new HashSet<string>(StringComparer.Ordinal);
    foreach (var file in fix.Files)
        foreach (var (from, to) in OldRanges(Run($"diff -U0 {fix.Hash}^ {fix.Hash} -- \"{file}\"")))
            foreach (var line in Run($"blame --first-parent -l -s -L {from},{to} {fix.Hash}^ -- \"{file}\"").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var hash = line.TrimStart('^').Split(' ')[0];
                if (hash != fix.Hash && byHash.ContainsKey(hash)) introducing.Add(hash);
            }
    var changes = introducing.Where(h => byHash[h].Files.Count is > 0 and <= 30).Order(StringComparer.Ordinal).ToList();
    pairCount += changes.Count;
    if (changes.Count == 0) continue;
    var changeFiles = changes.SelectMany(h => byHash[h].Files).ToHashSet(StringComparer.Ordinal);
    var elsewhere = fix.Files.Where(f => !changeFiles.Contains(f)).ToHashSet(StringComparer.Ordinal);
    if (elsewhere.Count == 0) { trivial++; continue; }
    fixCases.Add((fix.Hash, changes, elsewhere));
}
var test2 = new List<Case>();
foreach (var (fix, changes, elsewhere) in fixCases)
{
    var nodes = changes.SelectMany(h => byHash[h].Files.Where(IsCSharp).SelectMany(f => ChangedNodes(h, f))).DistinctBy(n => n.Id).ToList();
    if (nodes.Count == 0) continue;
    var files = changes.SelectMany(h => byHash[h].Files).Distinct().Order(StringComparer.Ordinal).ToList();
    var parts = changes.Select(h => (h, (IReadOnlyList<string>)byHash[h].Files)).ToList();
    test2.Add(new Case(changes[0], files, nodes, elsewhere, [.. files], parts, fix, bundlePattern.IsMatch(byHash[fix].Subject)));
}
Console.Error.WriteLine($"test 2: {fixes.Count} fixes, {pairCount} change → fix pairs, {fixCases.Count} fixes with files elsewhere (+{trivial} trivial), {test2.Count} with a changed member found");

// ---- Exact check: graphs rebuilt at past commits ----------------------------------------------
JsonObject? exact = null;
if (exactSample > 0) exact = await ExactCheck(Sample(test1Commits, exactSample));

// ---- Test 3: stable and fast -----------------------------------------------------------------
JsonObject? test3 = null;
if (!skipBuildTest)
{
    var a = await Build(full: true);
    var b = await Build(full: true);
    var target = fileNodes.Keys.Order(StringComparer.Ordinal).First();
    var path = Path.Combine(root, target);
    var original = File.ReadAllBytes(path); // bytes, so a BOM or CRLF survives the restore
    try
    {
        File.WriteAllBytes(path, [.. original, .. "\n// stangent benchmark touch\n"u8]);
        var incremental = await Build(full: false);
        var full = await Build(full: true);
        test3 = new JsonObject
        {
            ["identicalFullBuilds"] = a.Hash == b.Hash, ["incrementalEqualsFull"] = incremental.Hash == full.Hash,
            ["fullBuildMs"] = a.Stats.TotalMs, ["incrementalBuildMs"] = incremental.Stats.TotalMs, ["historyMs"] = a.Stats.HistoryMs,
            ["incrementalExtracted"] = incremental.Stats.Extracted, ["touchedFile"] = target,
        };
    }
    finally
    {
        File.WriteAllBytes(path, original);
        await Build(full: false);
    }
}

var output = new JsonObject
{
    ["repo"] = Path.GetFileName(root),
    ["graphCommit"] = graph.Commit,
    ["windowMonths"] = months > 0 ? months : "all",
    ["history"] = "main line only (git log --first-parent, merges diffed against their first parent; D36)",
    ["predictors"] = "graph (lines): changed methods found by name in today's graph · graph (lines+deps): plus what they directly use · graph (files): every point in the changed files · same project / same folder / co-change: baselines without the graph",
    ["labels"] = "certain = ✅ only; certain+guessed = ✅ and 🟡; certain+guessed+history adds 📜 (Phase 1c).",
    ["memberMatching"] = new JsonObject { ["sameFile"] = matchStats["sameFile"], ["moved"] = matchStats["moved"], ["unmatched"] = matchStats["unmatched"] },
    ["test1FindsTheRest"] = Compare(test1),
    ["test2PredictsWhatBroke"] = Compare(test2),
    ["historySettings"] = $"window {historySettings.Window}, minShared {historySettings.MinShared}, minShare {historySettings.MinShare.ToString(System.Globalization.CultureInfo.InvariantCulture)}, maxFiles {historySettings.MaxFiles}, renames {(renamesFollowed ? "followed" : "not followed")}",
    ["historySelfCheck"] = historySelfCheck,
    ["gate2"] = new JsonObject
    {
        ["note"] = "D44 Gate 2: 📜 from history older than each change; adds something when the gain's 95% range is above zero on ≥ 30 fixes",
        ["all"] = Gate2(test2),
        ["singleTopic"] = Gate2([.. test2.Where(c => !c.Bundle)]),
        ["bundle"] = Gate2([.. test2.Where(c => c.Bundle)]),
    },
    ["tuning"] = tune ? Tuning.Run(test2, historySettings, (c, s) => HistoryTop(c, 10, s).Keys.ToHashSet(StringComparer.Ordinal),
        c => GraphLinesDepth(c.Seeds, c.Exclude, 2).Keys.ToHashSet(StringComparer.Ordinal), c => CoChangeTop(c, 10).Keys.ToHashSet(StringComparer.Ordinal)) : null,
    ["gate1"] = new JsonObject
    {
        ["note"] = "D44 Gate 1: one case per fix; history helps when the 95% range of the recall gain is above zero on ≥ 30 fixes",
        ["all"] = Gate1(test2),
        ["singleTopic"] = Gate1([.. test2.Where(c => !c.Bundle)]),
        ["bundle"] = Gate1([.. test2.Where(c => c.Bundle)]),
        ["bundlePattern"] = bundlePattern.ToString(),
    },
    ["test2Fixes"] = new JsonObject
    {
        ["fixCommitsSampled"] = fixes.Count, ["fixCommitsMatching"] = fixCommitsMatching, ["changeFixPairs"] = pairCount,
        ["fixesWithFilesElsewhere"] = fixCases.Count, ["trivialFixesExcluded"] = trivial, ["cases"] = test2.Count,
        ["handCheckSample"] = new JsonArray([.. Sample(test2, 20).Select(c => (JsonNode)new JsonObject
        {
            ["fix"] = c.Fix, ["fixSubject"] = byHash[c.Fix!].Subject, ["bundle"] = c.Bundle,
            ["changes"] = new JsonArray([.. c.Parts!.Select(p => (JsonNode)$"{p.Commit[..10]} {byHash[p.Commit].Subject}")]),
            ["fixFilesElsewhere"] = new JsonArray([.. c.Actual.Order(StringComparer.Ordinal).Select(f => (JsonNode)f)]),
            ["genuine"] = null,
        })]),
    },
    ["exactCheck"] = exact,
    ["test4CompileBreak"] = compileBreakCount > 0 ? await CompileBreakTest() : null,
    ["test3StableAndFast"] = test3,
};
var text = output.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
if (Opt("--out", "") is { Length: > 0 } outFile) File.WriteAllText(outFile, text + "\n");
Console.WriteLine(text);
return 0;

// Rebuild the graph at each sampled commit and compare the exact answer (changed lines mapped on
// that commit's own graph) with the by-name answer on today's graph, case by case.
async Task<JsonObject> ExactCheck(List<(string Hash, string Subject, List<string> Files)> sample)
{
    var original = Run("rev-parse HEAD").Trim();
    var exactCases = new List<(HashSet<string> Actual, Dictionary<string, Label> Exact, Dictionary<string, Label> ByName)>();
    var skipped = new JsonArray();
    try
    {
        foreach (var c in sample)
        {
            Run($"checkout -q -f {c.Hash}");
            ApplySdkOverride();
            try
            {
                var (pastGraph, stats) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: true));
                Console.Error.WriteLine($"exact: {c.Hash[..8]} built in {stats.TotalMs} ms ({stats.Extracted} projects re-extracted)");
                var past = new ImpactAnalyzer(pastGraph);
                foreach (var seed in c.Files)
                {
                    var byName = ChangedNodes(c.Hash, seed);
                    if (byName.Count == 0) continue;
                    var starts = NewRanges(Run($"diff -U0 {c.Hash}^ {c.Hash} -- \"{seed}\"")).SelectMany(r => past.AtLines(seed, r.From, r.To)).DistinctBy(n => n.Id).ToList();
                    if (starts.Count == 0) continue;
                    HashSet<string> exclude = [seed];
                    exactCases.Add(([.. c.Files.Where(f => f != seed)], FromNodes(past.Affected(starts), exclude), GraphLines(byName, exclude)));
                }
            }
            catch (StangentException e)
            {
                skipped.Add($"{c.Hash[..8]}: {e.Code}");
            }
        }
    }
    finally
    {
        Run($"checkout -q -f {original}");
        ApplySdkOverride();
        await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: true));
    }

    double Jaccard(Dictionary<string, Label> a, Dictionary<string, Label> b)
    {
        var union = a.Keys.Union(b.Keys).Count();
        return union == 0 ? 1 : (double)a.Keys.Intersect(b.Keys).Count() / union;
    }
    var jaccards = exactCases.Select(x => Jaccard(x.Exact, x.ByName)).Order().ToList();
    return new JsonObject
    {
        ["commits"] = sample.Count, ["cases"] = exactCases.Count, ["skipped"] = skipped,
        ["medianAgreement"] = jaccards.Count == 0 ? 0 : Math.Round(jaccards[jaccards.Count / 2], 3),
        ["meanAgreement"] = jaccards.Count == 0 ? 0 : Math.Round(jaccards.Average(), 3),
        ["identicalAnswers"] = jaccards.Count(j => j == 1),
        ["exact"] = Metrics(exactCases.Select(x => (x.Actual, x.Exact)), Label.Guessed),
        ["byName"] = Metrics(exactCases.Select(x => (x.Actual, x.ByName)), Label.Guessed),
        ["note"] = "agreement = |exact ∩ by-name| / |exact ∪ by-name| over predicted files (✅ and 🟡), per case",
    };
}

Dictionary<string, Label> GraphPredict(List<Node> seeds, HashSet<string> exclude, int? maxDepth, Label maxLabel) =>
    FromNodes(analyzer.AffectedWithDepth(seeds).Where(a => (maxDepth is null || a.Depth <= maxDepth) && a.Label <= maxLabel).Select(a => (a.Node, a.Label)), exclude);

Task<JsonObject> CompileBreakTest() => CompileBreak.Run(repo, graph, analyzer, compileBreakCount, GraphPredict, SameFolder, SameProject);

// Generated-client calls vs a hand-written answer key (benchmark/expected/*.json): every call to a
// client method should reach exactly the expected endpoint, or none when the endpoint isn't in the repo.
JsonObject ExpectedLinks(string file)
{
    var key = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
    var nodesById = graph.AllNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    var linksFrom = graph.AllEdges.Where(e => e.Kind == EdgeKind.CallsHttp && e.Detector == "frontend-http").ToLookup(e => e.Source);
    var calls = graph.AllFacts.Where(f => f.Detector == "frontend-http" && f.Kind == "generated-call").ToList();
    int right = 0, wrong = 0, missing = 0, correctlyUnlinked = 0;
    var problems = new JsonArray();
    foreach (var entry in key["links"]!.AsArray())
    {
        var client = (string)entry!["client"]!; var method = (string)entry["method"]!; var expected = (string?)entry["endpoint"];
        foreach (var call in calls.Where(c => c["client"] == client && c["method"] == method))
        {
            // Links are per calling method; keep those this client method could explain (same verb + path family).
            var reached = linksFrom[call.Owner].Select(e => nodesById.GetValueOrDefault(e.Target)?.Name).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var blind = graph.AllBlindSpots.Any(b => b.Owner == call.Owner && b.Line == call.Line && b.Detector == "frontend-http");
            if (expected is null) { if (blind) correctlyUnlinked++; else { wrong++; problems.Add($"{client}.{method} at {call.File}:{call.Line}: expected ❓, got a link"); } }
            else if (reached.Contains(expected)) right++;
            else if (blind) { missing++; problems.Add($"{client}.{method} at {call.File}:{call.Line}: expected {expected}, got ❓"); }
            else { wrong++; problems.Add($"{client}.{method} at {call.File}:{call.Line}: expected {expected}, got {string.Join(", ", reached)}"); }
        }
    }
    return new JsonObject
    {
        ["calls"] = right + wrong + missing + correctlyUnlinked, ["right"] = right, ["correctlyUnlinked"] = correctlyUnlinked,
        ["wrong"] = wrong, ["missing"] = missing, ["problems"] = problems, ["checkedBy"] = key["checkedBy"]?.DeepClone(),
    };
}

void ApplySdkOverride()
{
    if (sdkOverride.Length > 0) File.Copy(sdkOverride, Path.Combine(root, "global.json"), overwrite: true);
}

async Task<(string Hash, BuildStats Stats)> Build(bool full)
{
    var (g, stats) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false, Full: full));
    var export = Path.Combine(root, ".stangent", $"bench-build-{++buildCount}.jsonl.gz");
    GraphStore.Export(g, export);
    var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(export)));
    Console.Error.WriteLine($"build (full={full}): {stats.TotalMs} ms, extracted {stats.Extracted}");
    return (hash, stats);
}

string Run(string gitArgs) => Git.Run(root, gitArgs) ?? "";

static bool IsSource(string f) => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".ts", StringComparison.Ordinal)
    || f.EndsWith(".tsx", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal) || f.EndsWith(".jsx", StringComparison.Ordinal);
static bool IsCSharp(string f) => f.EndsWith(".cs", StringComparison.Ordinal);

// Deterministic sample: every k-th item of a list already in a fixed order.
static List<T> Sample<T>(List<T> items, int max) =>
    items.Count <= max ? items : [.. Enumerable.Range(0, max).Select(i => items[(int)((long)i * items.Count / max)])];

static IEnumerable<(int From, int To)> OldRanges(string diff) => Ranges(diff, @"^@@ -(\d+)(?:,(\d+))? \+");
static IEnumerable<(int From, int To)> NewRanges(string diff) => Ranges(diff, @"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@");

static IEnumerable<(int From, int To)> Ranges(string diff, string pattern)
{
    foreach (Match m in Regex.Matches(diff, pattern, RegexOptions.Multiline))
    {
        var start = int.Parse(m.Groups[1].Value);
        var count = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
        if (count > 0) yield return (start, start + count - 1);
    }
}

/// <summary>One scored case: what changed (files and the members found for them), and the files that should be predicted.</summary>
// Parts: for a Test 2 fix blamed on several changes, each change's commit and files (D33).
sealed record Case(string Commit, IReadOnlyList<string> SeedFiles, List<Node> Seeds, HashSet<string> Actual, HashSet<string> Exclude,
    IReadOnlyList<(string Commit, IReadOnlyList<string> Files)>? Parts = null, string? Fix = null, bool Bundle = false);

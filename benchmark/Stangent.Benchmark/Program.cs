using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.Impact;

// Scores impact analysis against a repo's own git history (decisions.md D28). No AI, no labels:
//   1. finds the rest of the change — given one file of a past commit, how many of its other files?
//   2. predicts what broke — for change → later-fix pairs (SZZ: blame the lines a fix touched),
//      does impact on the change include what the fix touched elsewhere?
//   3. stable and fast — two full builds byte-identical; full vs incremental build time.
// Run against a benchmark clone with full history and an up-to-date graph:
//   dotnet run --project benchmark/Stangent.Benchmark -- <repo> [--months 36] [--max-commits 200] [--max-fixes 60] [--out file]
// Caveat, reported in the output: every commit is scored against the graph of the pinned commit,
// not the graph as it was at that commit.

var argList = args.ToList();
if (argList.Count == 0 || argList[0].StartsWith('-'))
{
    Console.Error.WriteLine("usage: Stangent.Benchmark <repo> [--months 36] [--max-commits 200] [--max-fixes 60] [--out file] [--skip-build-test]");
    return 2;
}
string Opt(string name, string fallback) => argList.IndexOf(name) is var i and >= 0 && i + 1 < argList.Count ? argList[i + 1] : fallback;
var root = Path.GetFullPath(argList[0]);
var months = int.Parse(Opt("--months", "36"));
var maxCommits = int.Parse(Opt("--max-commits", "200"));
var maxFixes = int.Parse(Opt("--max-fixes", "60"));
var skipBuildTest = argList.Contains("--skip-build-test");
var buildCount = 0;

var repo = StangentRepo.Discover(root);
var graph = GraphStore.Load(GraphStore.PathFor(repo)) ?? throw new InvalidOperationException("Build the graph first: stangent graph build");
if (Run("rev-parse --is-shallow-repository").Trim() == "true")
    throw new InvalidOperationException("Needs full history: git fetch --filter=blob:none --unshallow");
var analyzer = new ImpactAnalyzer(graph);
var graphFiles = graph.AllNodes.Where(n => n.Kind == NodeKind.File).Select(n => n.Name).ToHashSet(StringComparer.Ordinal);

// Commits in the window, newest first: hash, subject, C# files that still exist on the map.
var commits = new List<(string Hash, string Subject, List<string> Files)>();
var log = Run($"log --no-merges --since=\"{months} months ago\" --name-only --format=@@%H%x09%s HEAD");
foreach (var block in log.Split("@@", StringSplitOptions.RemoveEmptyEntries))
{
    var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var head = lines[0].Split('\t', 2);
    var files = lines.Skip(1).Where(f => f.EndsWith(".cs", StringComparison.Ordinal) && graphFiles.Contains(f)).Distinct().Order(StringComparer.Ordinal).ToList();
    commits.Add((head[0], head.Length > 1 ? head[1] : "", files));
}

var labelOrder = new[] { "certain", "certain+guessed" };
var predictCache = new Dictionary<string, Dictionary<string, Label>>(StringComparer.Ordinal);
Dictionary<string, Label> Predict(IEnumerable<string> files)
{
    var key = string.Join("\n", files.Order(StringComparer.Ordinal));
    if (predictCache.TryGetValue(key, out var hit)) return hit;
    var starts = files.SelectMany(analyzer.InFile).DistinctBy(n => n.Id).ToList();
    var predicted = new Dictionary<string, Label>(StringComparer.Ordinal);
    foreach (var (node, label) in analyzer.Affected(starts))
        if (node.File is { } f && f.EndsWith(".cs", StringComparison.Ordinal) && (!predicted.TryGetValue(f, out var l) || label < l))
            predicted[f] = label;
    foreach (var f in files) predicted.Remove(f);
    return predictCache[key] = predicted;
}

// What the changed code itself uses, one step out: callees, used types, base types. Impact answers
// "what depends on this"; a commit also edits what it depends on, so test 1 reports both.
var outgoing = graph.AllEdges.Where(e => e.Kind is not (EdgeKind.Contains or EdgeKind.DispatchesTo))
    .ToLookup(e => e.Source, StringComparer.Ordinal);
var nodeFiles = graph.AllNodes.Where(n => n.File is not null).GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First().File!, StringComparer.Ordinal);
Dictionary<string, Label> PredictWithDependencies(IEnumerable<string> files)
{
    var predicted = new Dictionary<string, Label>(Predict(files), StringComparer.Ordinal);
    var seeds = files.ToHashSet(StringComparer.Ordinal);
    foreach (var start in files.SelectMany(analyzer.InFile))
        foreach (var e in outgoing[start.Id])
            if (nodeFiles.TryGetValue(e.Target, out var f) && f.EndsWith(".cs", StringComparison.Ordinal) && !seeds.Contains(f)
                && (!predicted.TryGetValue(f, out var l) || e.Label < l))
                predicted[f] = e.Label;
    return predicted;
}

JsonObject Score(List<(HashSet<string> Actual, Dictionary<string, Label> Predicted)> cases)
{
    var result = new JsonObject { ["cases"] = cases.Count };
    foreach (var (name, max) in new[] { ("certain", Label.Certain), ("certain+guessed", Label.Guessed) })
    {
        int hits = 0, actual = 0, predicted = 0;
        var sizes = new List<int>();
        foreach (var c in cases)
        {
            var p = c.Predicted.Where(kv => kv.Value <= max).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
            hits += c.Actual.Count(p.Contains);
            actual += c.Actual.Count;
            predicted += p.Count;
            sizes.Add(p.Count);
        }
        sizes.Sort();
        result[name] = new JsonObject
        {
            ["recall"] = actual == 0 ? 0 : Math.Round((double)hits / actual, 3),
            ["precision"] = predicted == 0 ? 0 : Math.Round((double)hits / predicted, 3),
            ["found"] = hits, ["of"] = actual,
            ["medianPredictedFiles"] = sizes.Count == 0 ? 0 : sizes[sizes.Count / 2],
        };
    }
    return result;
}

// Test 1 — leave one out: each file of a 2–15 file commit predicts the rest.
var test1Commits = Sample(commits.Where(c => c.Files.Count is >= 2 and <= 15).ToList(), maxCommits);
var test1Cases = new List<(HashSet<string>, Dictionary<string, Label>)>();
var test1WithDeps = new List<(HashSet<string>, Dictionary<string, Label>)>();
foreach (var c in test1Commits)
    foreach (var seed in c.Files)
    {
        var rest = c.Files.Where(f => f != seed).ToHashSet(StringComparer.Ordinal);
        test1Cases.Add((rest, Predict([seed])));
        test1WithDeps.Add((rest, PredictWithDependencies([seed])));
    }
Console.Error.WriteLine($"test 1: {test1Commits.Count} commits, {test1Cases.Count} cases");

// Test 2 — SZZ: blame the old lines each fix changed to find the commits that introduced them.
var fixPattern = new Regex(@"\b(fix(e[sd])?|bug|regression|broke|broken|crash(es|ed)?|revert(s|ed)?)\b", RegexOptions.IgnoreCase);
var inWindow = commits.ToDictionary(c => c.Hash, c => c, StringComparer.Ordinal);
var fixes = Sample(commits.Where(c => fixPattern.IsMatch(c.Subject) && c.Files.Count is >= 1 and <= 15).ToList(), maxFixes);
var pairs = new List<(string Change, string Fix, List<string> ChangeFiles, HashSet<string> Elsewhere)>();
int trivial = 0;
foreach (var fix in fixes)
{
    var introducing = new HashSet<string>(StringComparer.Ordinal);
    foreach (var file in fix.Files)
        foreach (var (from, to) in OldRanges(Run($"diff -U0 {fix.Hash}^ {fix.Hash} -- \"{file}\"")))
            foreach (var line in Run($"blame -l -s -L {from},{to} {fix.Hash}^ -- \"{file}\"").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var hash = line.TrimStart('^').Split(' ')[0];
                if (hash != fix.Hash && inWindow.ContainsKey(hash)) introducing.Add(hash);
            }
    foreach (var change in introducing.Order(StringComparer.Ordinal))
    {
        var changeFiles = inWindow[change].Files;
        if (changeFiles.Count is 0 or > 30) continue;
        var elsewhere = fix.Files.Where(f => !changeFiles.Contains(f)).ToHashSet(StringComparer.Ordinal);
        if (elsewhere.Count == 0) { trivial++; continue; }
        pairs.Add((change, fix.Hash, changeFiles, elsewhere));
    }
}
var test2Cases = pairs.Select(p => (p.Elsewhere, Predict(p.ChangeFiles))).ToList();
Console.Error.WriteLine($"test 2: {fixes.Count} fixes, {pairs.Count} pairs (+{trivial} trivial: the fix only touched the change's own files)");

// Test 3 — two full builds give the same bytes; an incremental build after touching one file.
JsonObject? test3 = null;
if (!skipBuildTest)
{
    var a = await Build(full: true);
    var b = await Build(full: true);
    var target = graph.AllNodes.Where(n => n.Kind == NodeKind.File && n.Name.EndsWith(".cs", StringComparison.Ordinal))
        .Select(n => n.Name).Order(StringComparer.Ordinal).First();
    var path = Path.Combine(root, target);
    var original = File.ReadAllText(path);
    try
    {
        File.WriteAllText(path, original + "\n// stangent benchmark touch\n");
        var incremental = await Build(full: false);
        var full = await Build(full: true);
        test3 = new JsonObject
        {
            ["identicalFullBuilds"] = a.Hash == b.Hash,
            ["incrementalEqualsFull"] = incremental.Hash == full.Hash,
            ["fullBuildMs"] = a.Stats.TotalMs,
            ["incrementalBuildMs"] = incremental.Stats.TotalMs,
            ["incrementalExtracted"] = incremental.Stats.Extracted,
            ["touchedFile"] = target,
        };
    }
    finally
    {
        File.WriteAllText(path, original);
        await Build(full: false);
    }
}

var output = new JsonObject
{
    ["repo"] = Path.GetFileName(root),
    ["graphCommit"] = graph.Commit,
    ["windowMonths"] = months,
    ["caveat"] = "Every commit is scored against the graph of the pinned commit, not the graph at that commit.",
    ["labels"] = "certain = ✅ only; certain+guessed = ✅ and 🟡. 📜 (Phase 1c) not built yet.",
    ["test1FindsTheRest"] = Score(test1Cases),
    ["test1FindsTheRestPlusDirectDependencies"] = Score(test1WithDeps),
    ["test2PredictsWhatBroke"] = Score(test2Cases),
    ["test2Pairs"] = new JsonObject
    {
        ["fixCommits"] = fixes.Count, ["pairs"] = pairs.Count, ["trivialPairsExcluded"] = trivial,
        ["handCheckSample"] = new JsonArray([.. Sample(pairs, 20).Select(p => (JsonNode)new JsonObject
        {
            ["change"] = p.Change, ["changeSubject"] = inWindow[p.Change].Subject,
            ["fix"] = p.Fix, ["fixSubject"] = inWindow[p.Fix].Subject,
            ["fixFilesElsewhere"] = new JsonArray([.. p.Elsewhere.Order(StringComparer.Ordinal).Select(f => (JsonNode)f)]),
            ["genuine"] = null,
        })]),
    },
    ["test3StableAndFast"] = test3,
};
var text = output.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
if (Opt("--out", "") is { Length: > 0 } outFile) File.WriteAllText(outFile, text + "\n");
Console.WriteLine(text);
return 0;

async Task<(string Hash, BuildStats Stats)> Build(bool full)
{
    var (g, stats) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false, Full: full));
    // Kept beside the graph so a mismatch can be diffed (gunzip -c a b | diff).
    var export = Path.Combine(root, ".stangent", $"bench-build-{++buildCount}.jsonl.gz");
    GraphStore.Export(g, export);
    var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(export)));
    Console.Error.WriteLine($"build (full={full}): {stats.TotalMs} ms, extracted {stats.Extracted}");
    return (hash, stats);
}

string Run(string gitArgs) => Git.Run(root, gitArgs) ?? "";

// Deterministic sample: every k-th item of a list already in a fixed order.
static List<T> Sample<T>(List<T> items, int max) =>
    items.Count <= max ? items : [.. Enumerable.Range(0, max).Select(i => items[(int)((long)i * items.Count / max)])];

static IEnumerable<(int From, int To)> OldRanges(string diff)
{
    foreach (Match m in Regex.Matches(diff, @"^@@ -(\d+)(?:,(\d+))? \+", RegexOptions.Multiline))
    {
        var start = int.Parse(m.Groups[1].Value);
        var count = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
        if (count > 0) yield return (start, start + count - 1);
    }
}

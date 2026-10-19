using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Graph;

/// <summary>
/// Test 4 for the frontend (D32): rename a sampled TypeScript member in place, run the frontend's
/// own `tsc --noEmit`, and record which other files now fail to type-check. Angular templates
/// aren't type-checked by tsc, so template files are left out of both sides of the score.
/// </summary>
static partial class TsCompileBreak
{
    const string Suffix = "__StangentBroken";

    [GeneratedRegex(@"^(?<file>[^(]+)\((?<line>\d+),\d+\): error (?<code>TS\d+): (?<msg>.*)$")]
    private static partial Regex ErrorLine();

    public static JsonObject Run(string root, CodeGraph graph, int count,
        Func<List<Node>, HashSet<string>, int?, Label, Dictionary<string, Label>> graphPredict,
        Func<IEnumerable<string>, IReadOnlySet<string>, Dictionary<string, Label>> sameFolder)
    {
        var results = new JsonObject();
        var frontends = graph.AllNodes.Where(n => n.Kind == NodeKind.Project && n.Id.EndsWith(".json", StringComparison.Ordinal)).DistinctBy(n => n.Id).ToList();
        foreach (var fe in frontends)
        {
            var tsconfig = Path.Combine(root, fe.Id["project:".Length..]);
            var dir = Path.GetDirectoryName(tsconfig)!;
            var tsc = new[] { Path.Combine(dir, "node_modules", "typescript", "bin", "tsc"), Path.Combine(root, "node_modules", "typescript", "bin", "tsc") }.FirstOrDefault(File.Exists);
            if (tsc is null) { results[fe.Id] = new JsonObject { ["skipped"] = "no TypeScript in node_modules" }; continue; }

            Console.Error.WriteLine($"ts-compile-break: baseline type-check of {fe.Id}");
            var baseline = Errors(root, dir, tsc, tsconfig);
            var candidates = graph.AllNodes
                .Where(n => n.Project == fe.Id && n.Id.StartsWith("ts|", StringComparison.Ordinal) && !n.IsTest
                    && n.Kind is NodeKind.Method or NodeKind.Property or NodeKind.Field or NodeKind.Component
                    && n.File is { } f && (f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal))
                    && Member(n) is { } m && m != "constructor" && !m.StartsWith("test:", StringComparison.Ordinal))
                .DistinctBy(n => n.Id).OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
            var step = Math.Max(1, candidates.Count / Math.Max(1, count * 2));
            var cases = new List<(Node Node, HashSet<string> Broken)>();
            for (var i = 0; i < candidates.Count && cases.Count < count; i += step)
            {
                var node = candidates[i];
                var path = Path.Combine(root, node.File!);
                var original = File.ReadAllBytes(path);
                var lines = File.ReadAllLines(path);
                if (node.Line < 1 || node.Line > lines.Length) continue;
                var member = Member(node)!;
                var rx = new Regex($@"\b{Regex.Escape(member)}\b");
                if (!rx.IsMatch(lines[node.Line - 1])) continue;
                lines[node.Line - 1] = rx.Replace(lines[node.Line - 1], member + Suffix, 1);
                try
                {
                    File.WriteAllLines(path, lines);
                    var broken = Errors(root, dir, tsc, tsconfig).Except(baseline).Select(e => e.Split('\t')[0]).ToHashSet(StringComparer.Ordinal);
                    broken.Remove(node.File!);
                    cases.Add((node, broken));
                    Console.Error.WriteLine($"ts-compile-break: {cases.Count}/{count} {node.Name}: {broken.Count} file(s) break");
                }
                finally
                {
                    File.WriteAllBytes(path, original); // byte for byte, so a BOM or CRLF survives
                }
            }

            static bool Ts(string f) => f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal);
            JsonObject Score(Func<(Node Node, HashSet<string> Broken), Dictionary<string, Label>> predict)
            {
                int hits = 0, actual = 0, predicted = 0;
                foreach (var c in cases)
                {
                    var p = predict(c).Keys.Where(Ts).ToHashSet(StringComparer.Ordinal);
                    hits += c.Broken.Count(p.Contains); actual += c.Broken.Count; predicted += p.Count;
                }
                return new JsonObject
                {
                    ["recall"] = actual == 0 ? 0 : Math.Round((double)hits / actual, 3),
                    ["precision"] = predicted == 0 ? 0 : Math.Round((double)hits / predicted, 3),
                    ["found"] = hits, ["of"] = actual,
                };
            }
            HashSet<string> Exclude((Node Node, HashSet<string>) c) => [c.Node.File!];
            var misses = new JsonArray();
            foreach (var c in cases)
                foreach (var f in c.Broken.Where(f => !graphPredict([c.Node], Exclude(c), 1, Label.Certain).ContainsKey(f)).Take(2))
                    if (misses.Count < 15) misses.Add($"{c.Node.Name} → {f}");
            results[fe.Id] = new JsonObject
            {
                ["members"] = cases.Count,
                ["membersWithBreakage"] = cases.Count(c => c.Broken.Count > 0),
                ["brokenFiles"] = cases.Sum(c => c.Broken.Count),
                ["graph ✅ depth ≤ 1"] = Score(c => graphPredict([c.Node], Exclude(c), 1, Label.Certain)),
                ["graph ✅ depth ≤ 2"] = Score(c => graphPredict([c.Node], Exclude(c), 2, Label.Certain)),
                ["graph ✅ unlimited"] = Score(c => graphPredict([c.Node], Exclude(c), null, Label.Certain)),
                ["same folder"] = Score(c => sameFolder([c.Node.File!], Exclude(c))),
                ["graphDirectMisses"] = misses,
            };
        }
        results["note"] = "Ground truth = .ts/.tsx files with new tsc errors after renaming the member; its own file excluded. Angular templates aren't type-checked by tsc, so they're out of this test.";
        return results;
    }

    static string? Member(Node n)
    {
        var name = n.Id[(n.Id.IndexOf('#') + 1)..];
        var last = name[(name.LastIndexOf('.') + 1)..];
        return Regex.IsMatch(last, @"^[A-Za-z_$][\w$]*$") ? last : null;
    }

    /// <summary>tsc's errors as "file\tcode\tline\tmessage", file relative to the repo root.</summary>
    static HashSet<string> Errors(string root, string dir, string tsc, string tsconfig)
    {
        var psi = new ProcessStartInfo("node", $"\"{tsc}\" --noEmit --pretty false -p \"{tsconfig}\"")
        {
            WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in stdout.Result.Split('\n'))
        {
            var m = ErrorLine().Match(line.Trim());
            if (!m.Success) continue;
            var file = Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(dir, m.Groups["file"].Value))).Replace('\\', '/');
            set.Add($"{file}\t{m.Groups["code"].Value}\t{m.Groups["line"].Value}\t{m.Groups["msg"].Value}");
        }
        return set;
    }
}

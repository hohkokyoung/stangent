using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;
using Stangent.Tickets.Proof;

namespace Stangent.Benchmark.Tickets;

/// <summary>
/// Scores Phase 3 on the frozen set (D77–D79, D81): <c>--score-tickets &lt;clone&gt; &lt;tickets dir&gt; [--out file]</c>.
/// Reads what the run left in the clone (<c>.stangent/tickets/&lt;KEY&gt;/</c>) and in <c>&lt;tickets dir&gt;/run/&lt;KEY&gt;.json</c>,
/// and the answer key, which nothing in the run reads. 95% ranges from 1,000 resamples, seed 42. The bars are D79's and
/// aren't moved after a run.
/// </summary>
public static class TicketScore
{
    public const int ProofBugs = 30;
    public const int KeywordTop = 5;

    public static JsonObject Run(string clone, string ticketsDir)
    {
        var selection = Load(Path.Combine(ticketsDir, "selection.json"));
        var key = Load(Path.Combine(ticketsDir, "answer-key.json"))["issues"]!.AsObject();
        var keys = Load(Path.Combine(ticketsDir, "jira-keys.json")).AsObject();
        var repo = StangentRepo.Discover(clone);
        var graph = GraphStore.Load(GraphStore.PathFor(repo)) ?? throw new InvalidOperationException("Build the graph first.");
        var index = SpecIndex.Load(clone);
        var nodes = graph.AllNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var symbolsByFile = graph.AllNodes.Where(n => n.File is not null && n.Kind is NodeKind.Type or NodeKind.Method or NodeKind.Component or NodeKind.Property)
            .GroupBy(n => n.File!).ToDictionary(g => g.Key, g => g.Select(n => n.Name).ToList(), StringComparer.Ordinal);

        var issues = selection["issues"]!.AsArray().OfType<JsonObject>().ToList();
        var bugs = issues.Where(i => (string)i["kind"]! == "bug").ToList();
        var notBugs = issues.Where(i => (string)i["kind"]! == "not-bug").ToList();
        string Key(JsonObject i) => (string?)keys[((int)i["issue"]!).ToString(System.Globalization.CultureInfo.InvariantCulture)]
            ?? throw new InvalidOperationException($"No Jira key for #{i["issue"]} in jira-keys.json.");
        string Dir(JsonObject i) => Path.Combine(clone, ".stangent", "tickets", Key(i));

        // Right place, and the keyword baseline beside it (D79).
        var cases = new JsonArray();
        var hits = new List<double>();
        var baseline = new List<double>();
        var coverage = new List<double>();
        foreach (var bug in bugs)
        {
            var fix = key[((int)bug["issue"]!).ToString(System.Globalization.CultureInfo.InvariantCulture)]!["files"]!.AsArray().Select(f => (string)f!).ToHashSet(StringComparer.Ordinal);
            var points = Load(Path.Combine(Dir(bug), "points.json"));
            var ticket = Load(Path.Combine(Dir(bug), "ticket.json"));
            var files = StartingFiles(points, index, nodes);
            var top = KeywordTop5(TicketWords(ticket), symbolsByFile);
            var bundle = TryLoad(Path.Combine(Dir(bug), "bundle.json"));
            var bundleFiles = bundle?["pointers"]?.AsArray().Select(p => (string?)p!["file"]).OfType<string>().ToHashSet(StringComparer.Ordinal) ?? [];
            var hit = files.Overlaps(fix);
            var keywordHit = top.Overlaps(fix);
            hits.Add(hit ? 1 : 0);
            baseline.Add(keywordHit ? 1 : 0);
            coverage.Add(fix.Count == 0 ? 0 : (double)fix.Count(bundleFiles.Contains) / fix.Count);
            cases.Add(new JsonObject
            {
                ["issue"] = (int)bug["issue"]!, ["key"] = Key(bug), ["area"] = (string?)bug["area"], ["hit"] = hit, ["keywordHit"] = keywordHit,
                ["points"] = points["points"]!.AsArray().Count, ["pointsBy"] = points["counts"]!.DeepClone(), ["startingFiles"] = files.Count,
                ["bundleChars"] = (int?)bundle?["cap"]?["chars"], ["fixCoverage"] = Math.Round(coverage[^1], 3),
            });
        }
        var gain = hits.Zip(baseline, (a, b) => a - b).ToArray();

        // The proof (D79, D81): the 30 newest bugs and the 10 not-bugs.
        var proofBugs = bugs.Take(ProofBugs).ToList();
        JsonObject? Proof(JsonObject i) => TryLoad(Path.Combine(Dir(i), "bug-proof.json"));
        JsonObject? Record(JsonObject i) => TryLoad(Path.Combine(ticketsDir, "run", Key(i) + ".json"));
        // D89: an attempt our own permission rules blocked measures the setup, not the reproducer: still not reproduced,
        // but counted apart so a score never hides it.
        bool SetupBlocked(JsonObject i) => (bool?)Proof(i)?["attempts"]?.AsArray().LastOrDefault()?["setupBlocked"] == true;
        string Kind(JsonObject? proof) => (string?)proof?["attempts"]?.AsArray().LastOrDefault()?["kind"] ?? "none";
        var reproduced = proofBugs.Where(b => (string?)Proof(b)?["outcome"] == "reproduced").ToList();
        string? OnFix(JsonObject i) => TicketScore.OnFix(Proof(i), (string?)key[((int)i["issue"]!).ToString(System.Globalization.CultureInfo.InvariantCulture)]?["fixCommit"]);
        var wrongReports = notBugs.Count(n => (string?)Proof(n)?["outcome"] == "reproduced");
        var outcomes = proofBugs.Concat(notBugs).Select(i => new JsonObject
        {
            ["issue"] = (int)i["issue"]!, ["kind"] = (string)i["kind"]!, ["area"] = (string?)i["area"], ["outcome"] = (string?)Proof(i)?["outcome"] ?? "not run",
            ["test"] = Kind(Proof(i)), ["proofOnFix"] = OnFix(i),
            ["outside"] = (bool?)Proof(i)?["attempts"]?.AsArray().LastOrDefault()?["outside"] == true,
            ["setupBlocked"] = SetupBlocked(i),
        }).ToList();

        // Grounded and repeatable are must-pass (D79): read from the artifacts and the run's own records.
        var records = issues.Select(Record).ToList();
        var replayed = records.Count(r => r?["replayIdentical"] is not null);
        var identical = records.Count(r => (bool?)r?["replayIdentical"] == true);
        var rerunCalls = records.Sum(r => (int?)r?["rerunAiCalls"] ?? 0);
        var failedSteps = issues.SelectMany(i => new[]
        {
            ((string?)TryLoad(Path.Combine(Dir(i), "points.json"))?["ai"]?["status"]) == "failed" ? $"{Key(i)} points" : null,
            ((string?)Proof(i)?["outcome"]) == "failed" ? $"{Key(i)} prove" : null,
        }).OfType<string>().ToList();

        double Share(IEnumerable<double> v) { var a = v.ToArray(); return a.Length == 0 ? 0 : Math.Round(a.Average(), 3); }
        JsonArray R((double, double) r) => [r.Item1, r.Item2];
        var rightPlace = Share(hits);
        var gainRange = Range(gain);
        var unit = proofBugs.Where(b => Kind(Proof(b)) == "unit").ToList();
        var playwright = proofBugs.Where(b => Kind(Proof(b)) == "playwright").ToList();
        double Rate(List<JsonObject> list) => list.Count == 0 ? 0 : Math.Round((double)list.Count(b => (string?)Proof(b)?["outcome"] == "reproduced") / list.Count, 3);

        var bars = new JsonObject
        {
            ["rightPlace"] = new JsonObject { ["share"] = rightPlace, ["range"] = R(Range([.. hits])), ["bar"] = 0.7, ["passed"] = rightPlace >= 0.7, ["of"] = bugs.Count },
            ["beatsKeywords"] = new JsonObject
            {
                ["stangent"] = rightPlace, ["keywords"] = Share(baseline), ["gain"] = Share(gain), ["gainRange"] = R(gainRange),
                ["passed"] = rightPlace > Share(baseline) && gainRange.Item1 > 0,
            },
            ["proofIsReal"] = ProofIsReal([.. reproduced.Select(b => (Key(b), OnFix(b)))]),
            ["proofFindsBugs"] = new JsonObject
            {
                ["share"] = Rate(proofBugs), ["range"] = R(Range([.. proofBugs.Select(b => (string?)Proof(b)?["outcome"] == "reproduced" ? 1.0 : 0.0)])), ["of"] = proofBugs.Count,
                ["unit"] = new JsonObject { ["of"] = unit.Count, ["share"] = Rate(unit) }, ["playwright"] = new JsonObject { ["of"] = playwright.Count, ["share"] = Rate(playwright) },
                ["setupBlocked"] = proofBugs.Count(SetupBlocked), ["bar"] = 0.5, ["passed"] = Rate(proofBugs) >= 0.5,
            },
            ["catchesWrongReports"] = new JsonObject { ["reproduced"] = wrongReports, ["of"] = notBugs.Count, ["bar"] = "at most 1", ["passed"] = wrongReports <= 1 },
            ["grounded"] = new JsonObject { ["failedSteps"] = new JsonArray([.. failedSteps.Select(f => (JsonNode)f)]), ["note"] = "every recorded output passed its code check; tests show a made-up quote, a line not in its file and a file not in the repo are rejected" },
            ["repeatable"] = new JsonObject { ["replayed"] = replayed, ["identical"] = identical, ["rerunAiCalls"] = rerunCalls, ["passed"] = replayed == issues.Count && identical == replayed && rerunCalls == 0 },
        };
        var stages = PlaceStages.Run(clone, ticketsDir);
        return new JsonObject
        {
            ["schemaVersion"] = 1, ["clone"] = Path.GetFileName(clone), ["commit"] = graph.Commit, ["bugs"] = bugs.Count, ["notBugs"] = notBugs.Count,
            ["bars"] = bars,
            ["reported"] = new JsonObject
            {
                ["fixCoverage"] = new JsonObject { ["mean"] = Share(coverage), ["range"] = R(Range([.. coverage])) },
                ["bundleChars"] = Stats(cases.Select(c => (double?)(int?)c!["bundleChars"]).OfType<double>()),
                ["cost"] = Cost(issues.Select(i => Dir(i)).ToList(), records),
                // Where right place loses each fix, stage by stage (owner's request, 2026-11-01; no bar).
                ["rightPlaceStages"] = stages,
                // Would the nearby-page rule explain fixes no page cites? (retrieval redesign, 2026-11-02; no bar)
                ["nearbyWalk"] = NearbyWalk.Run(clone, ticketsDir, stages),
            },
            ["cases"] = cases,
            ["proofs"] = new JsonArray([.. outcomes]),
        };
    }

    /// <summary>What the test did on the fix (D105), or null if that wasn't the proof's own test on the answer key's fix
    /// commit: a hand-run --verify elsewhere, or on an edited test, says nothing about it. A record from before D105 is
    /// classified from its saved runs.</summary>
    public static string? OnFix(JsonObject? proof, string? fixCommit)
    {
        if (proof?["verification"] is not JsonObject v || fixCommit is null) return null;
        if (((string?)v["commit"])?.StartsWith(fixCommit, StringComparison.Ordinal) != true || (bool?)v["testChanged"] == true) return null;
        return (string?)v["result"] ?? BugProof.OnFixResult(proof["attempts"]!.AsArray()[^1]!.AsObject(), (v["runs"]?.AsArray() ?? []).OfType<JsonObject>(), v["newFailures"]?.AsArray().Count ?? 0);
    }

    /// <summary>D79's "proof is real": only <c>passed</c> counts towards the bar. Code can't tell whether the unclear ones
    /// are real, so they're the top of a range beside it, never part of it (D105). <c>same-failure</c> is neither.</summary>
    public static JsonObject ProofIsReal(IReadOnlyList<(string Key, string? OnFix)> reproduced)
    {
        var real = reproduced.Count(r => r.OnFix == "passed");
        var unclear = reproduced.Count(r => r.OnFix is not ("passed" or "same-failure"));
        var groups = reproduced.GroupBy(r => r.OnFix ?? "none").OrderBy(g => g.Key, StringComparer.Ordinal);
        var range = Range([.. reproduced.Select(r => r.OnFix == "passed" ? 1.0 : 0.0)]);
        return new JsonObject
        {
            ["reproduced"] = reproduced.Count, ["passOnFix"] = real, ["share"] = reproduced.Count == 0 ? 0 : Math.Round((double)real / reproduced.Count, 3),
            ["range"] = new JsonArray(range.Item1, range.Item2), ["bar"] = 0.9,
            ["passed"] = reproduced.Count > 0 && (double)real / reproduced.Count >= 0.9,
            ["shareIfUnclearReal"] = reproduced.Count == 0 ? 0 : Math.Round((double)(real + unclear) / reproduced.Count, 3),
            ["onFix"] = new JsonObject([.. groups.Select(g => new KeyValuePair<string, JsonNode?>(g.Key, new JsonArray([.. g.Select(r => (JsonNode)r.Key)])))]),
        };
    }

    /// <summary>"Starting points' files" (D79, clarified; D101): the file points themselves, the files of the symbols the
    /// starting pages' statements cite, and the files of matched symbols.</summary>
    public static HashSet<string> StartingFiles(JsonNode points, SpecIndex index, IReadOnlyDictionary<string, Node> nodes)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var direct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in points["points"]!.AsArray())
        {
            var id = (string)p!["id"]!;
            if ((string)p["kind"]! == "file") direct.Add(id);
            else if ((string)p["kind"]! == "symbol") ids.Add(id);
            else if (index.Pages.TryGetValue(id, out var page))
                foreach (var (_, _, s) in PageChecks.Statements(page)) ids.UnionWith(s["cites"]!.AsArray().Select(c => (string)c!));
        }
        return [.. ids.Select(i => nodes.GetValueOrDefault(i)?.File).OfType<string>().Concat(direct)];
    }

    public static HashSet<string> TicketWords(JsonNode ticket) =>
        Candidates.Words(string.Join("\n", [(string?)ticket["ticket"]?["summary"], (string?)ticket["description"],
            .. (ticket["comments"]?.AsArray() ?? []).Select(c => (string?)c?["text"])]));

    /// <summary>The keyword baseline (D79): each file scored by the ticket words in its path and its symbols' names; the
    /// top 5, ties by path. Files with no shared word aren't picked.</summary>
    public static HashSet<string> KeywordTop5(HashSet<string> words, IReadOnlyDictionary<string, List<string>> symbolsByFile) =>
        [.. symbolsByFile.Select(f => (File: f.Key, Score: Candidates.Words(f.Key.Replace('/', ' ') + " " + string.Join(" ", f.Value)).Intersect(words).Count()))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.File, StringComparer.Ordinal).Take(KeywordTop).Select(x => x.File)];

    /// <summary>95% range of a mean by resampling (1,000 resamples, seed 42), as in D47, D57 and D79.</summary>
    public static (double, double) Range(double[] values)
    {
        if (values.Length == 0) return (0, 0);
        var rng = new Random(42);
        var means = new double[1000];
        for (var r = 0; r < means.Length; r++)
        {
            double sum = 0;
            for (var i = 0; i < values.Length; i++) sum += values[rng.Next(values.Length)];
            means[r] = sum / values.Length;
        }
        Array.Sort(means);
        return (Math.Round(means[25], 3), Math.Round(means[974], 3));
    }

    static JsonObject Stats(IEnumerable<double> v)
    {
        var a = v.Order().ToArray();
        return a.Length == 0 ? new JsonObject { ["n"] = 0 } : new JsonObject { ["n"] = a.Length, ["median"] = a[a.Length / 2], ["max"] = a[^1], ["mean"] = Math.Round(a.Average(), 1) };
    }

    /// <summary>Cost and time per step (reported, no bar): AI usage from the recorded artifacts, time from the run's records.</summary>
    static JsonObject Cost(List<string> dirs, List<JsonObject?> records)
    {
        Usage Sum(Func<string, IEnumerable<JsonNode?>> pick) => dirs.SelectMany(pick).Aggregate(Usage.None, (a, u) => a.Add(Usage.FromJson(u)));
        var points = Sum(d => [TryLoad(Path.Combine(d, "points.json"))?["ai"]?["usage"]]);
        var prove = Sum(d => TryLoad(Path.Combine(d, "bug-proof.json"))?["attempts"]?.AsArray().Select(a => a?["usage"]) ?? []);
        var drafts = Sum(d => Directory.Exists(Path.Combine(d, "drafts")) ? Directory.EnumerateFiles(Path.Combine(d, "drafts"), "*.json").Select(f => TryLoad(f)?["run"]) : []);
        var ms = new JsonObject();
        foreach (var step in new[] { "pull", "points", "bundle", "prove", "questions", "resolution" })
            ms[step] = Stats(records.Select(r => (double?)(long?)r?["steps"]?[step]?["ms"]).OfType<double>());
        return new JsonObject
        {
            ["points"] = points.ToJson(), ["prove"] = prove.ToJson(), ["drafts"] = drafts.ToJson(),
            ["totalUsd"] = Math.Round(points.CostUsd + prove.CostUsd + drafts.CostUsd, 2), ["ms"] = ms,
        };
    }

    static JsonObject Load(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    static JsonObject? TryLoad(string path) => File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
}

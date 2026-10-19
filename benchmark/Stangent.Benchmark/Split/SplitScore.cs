using System.Text.Json.Nodes;

namespace Stangent.Benchmark.Split;

/// <summary>
/// D49's done-when, agreed before any run: safe (≥ 90% of split cases hold up, on each repo, at least 30 split
/// cases), useful (≥ 25% of examined cases split), better than split-by-folder. 95% ranges come from 1,000
/// resamples with a fixed seed, so a re-run prints the same numbers.
/// </summary>
static class SplitScore
{
    public const double SafeBar = 0.90, UsefulBar = 0.25, NaiveSplitSlack = 0.05;
    public const int Floor = 30, Resamples = 1000, Seed = 20261019;

    public static JsonObject Verdict(List<CaseOutcome> cases)
    {
        var graph = Stats(cases, Splitter.Within2);
        var folder = Stats(cases, Splitter.Folder);
        var draws = Draws(cases.Count);
        var gain = graph.Safe is { } g && folder.Safe is { } f ? Math.Round(g - f, 3) : (double?)null;
        var gainRange = Range(draws.Select(d => Safe(d.Select(i => cases[i]).ToList(), Splitter.Within2) - Safe(d.Select(i => cases[i]).ToList(), Splitter.Folder)));
        var safePasses = graph.Split >= Floor && graph.Safe >= SafeBar;
        var usefulPasses = graph.SplitRate >= UsefulBar;
        var naivePasses = gain > 0 && folder.SplitRate - graph.SplitRate <= NaiveSplitSlack + 1e-9;
        return new JsonObject
        {
            ["judgedRule"] = Splitter.Within2,
            ["safe"] = new JsonObject
            {
                ["splitCases"] = graph.Split, ["holdUp"] = graph.HoldUp, ["share"] = graph.Safe, ["range95"] = graph.SafeRange,
                ["bar"] = SafeBar, ["belowTheFloor"] = graph.Split < Floor, ["passes"] = safePasses,
            },
            ["useful"] = new JsonObject
            {
                ["examined"] = cases.Count, ["splitCases"] = graph.Split, ["share"] = graph.SplitRate, ["range95"] = graph.SplitRange,
                ["bar"] = UsefulBar, ["passes"] = usefulPasses,
            },
            ["betterThanNaive"] = new JsonObject
            {
                ["graphSafe"] = graph.Safe, ["folderSafe"] = folder.Safe, ["safeGain"] = gain, ["safeGainRange95"] = gainRange,
                ["graphSplitRate"] = graph.SplitRate, ["folderSplitRate"] = folder.SplitRate,
                ["rule"] = $"graph safer than split-by-folder, whose split rate is at most {NaiveSplitSlack * 100:F0} points above the graph's",
                ["passes"] = naivePasses,
            },
            ["allPass"] = safePasses && usefulPasses && naivePasses,
        };
    }

    public static JsonObject ByRule(List<CaseOutcome> cases)
    {
        var json = new JsonObject();
        foreach (var rule in Splitter.Rules)
        {
            var s = Stats(cases, rule);
            json[rule] = new JsonObject
            {
                ["splitCases"] = s.Split, ["splitRate"] = s.SplitRate, ["splitRateRange95"] = s.SplitRange,
                ["holdUp"] = s.HoldUp, ["safe"] = s.Safe, ["safeRange95"] = s.SafeRange,
            };
        }
        return json;
    }

    sealed record RuleStats(int Split, int HoldUp, double? Safe, JsonArray SafeRange, double SplitRate, JsonArray SplitRange);

    static RuleStats Stats(List<CaseOutcome> cases, string rule)
    {
        var split = cases.Where(c => c.Split(rule)).ToList();
        var holdUp = split.Count(c => c.Rules[rule].HoldsUp);
        var safeDraws = Draws(split.Count).Select(d => (double)d.Count(i => split[i].Rules[rule].HoldsUp) / d.Length);
        var rateDraws = Draws(cases.Count).Select(d => (double)d.Count(i => cases[i].Split(rule)) / d.Length);
        return new RuleStats(split.Count, holdUp, split.Count == 0 ? null : Math.Round((double)holdUp / split.Count, 3), Range(safeDraws),
            cases.Count == 0 ? 0 : Math.Round((double)split.Count / cases.Count, 3), Range(rateDraws));
    }

    static double Safe(List<CaseOutcome> cases, string rule)
    {
        var split = cases.Where(c => c.Split(rule)).ToList();
        return split.Count == 0 ? double.NaN : (double)split.Count(c => c.Rules[rule].HoldsUp) / split.Count;
    }

    /// <summary>1,000 resamples of n cases, with replacement, from a fixed seed.</summary>
    static List<int[]> Draws(int n)
    {
        if (n == 0) return [];
        var rng = new Random(Seed);
        return [.. Enumerable.Range(0, Resamples).Select(_ => Enumerable.Range(0, n).Select(_ => rng.Next(n)).ToArray())];
    }

    static JsonArray Range(IEnumerable<double> values)
    {
        var sorted = values.Where(v => !double.IsNaN(v)).Order().ToList();
        if (sorted.Count == 0) return [];
        return [Math.Round(sorted[(int)(0.025 * sorted.Count)], 3), Math.Round(sorted[Math.Max(0, (int)(0.975 * sorted.Count) - 1)], 3)];
    }
}

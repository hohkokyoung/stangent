using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Core;

// Tuning 📜's defaults (D43, plan step 5): a small grid scored on one repo's Test 2 cases, written as raw
// counts so the tuning repos can be added up; then one rule, applied by code, picks the defaults.
static class Tuning
{
    static readonly int[] Windows = [10, 20, 40];
    static readonly int[] MinShareds = [2, 3];
    static readonly double[] MinShares = [0.2, 0.3, 0.5];
    static readonly int[] MaxFiles = [20, 30, 50];

    static string Key(int w, int ms, double share, int max) => $"{w}/{ms}/{share.ToString(CultureInfo.InvariantCulture)}/{max}";

    public static JsonObject Run(List<Case> cases, StangentConfig.HistoryConfig current,
        Func<Case, StangentConfig.HistoryConfig, HashSet<string>> history, Func<Case, HashSet<string>> graph, Func<Case, HashSet<string>> baseline)
    {
        var graphSets = cases.Select(graph).ToList();
        var baseSets = cases.Select(baseline).ToList();
        var settings = new JsonArray();
        foreach (var w in Windows)
            foreach (var ms in MinShareds)
                foreach (var share in MinShares)
                    foreach (var max in MaxFiles)
                    {
                        var s = new StangentConfig.HistoryConfig
                        {
                            Window = w, MinShared = ms, MinShare = share, MaxFiles = max,
                            BotAuthors = current.BotAuthors, FollowRenames = current.FollowRenames,
                        };
                        int hits = 0, predicted = 0, hitsWithGraph = 0;
                        for (var i = 0; i < cases.Count; i++)
                        {
                            var h = history(cases[i], s);
                            hits += cases[i].Actual.Count(h.Contains);
                            predicted += h.Count;
                            hitsWithGraph += cases[i].Actual.Count(f => h.Contains(f) || graphSets[i].Contains(f));
                        }
                        settings.Add(new JsonObject
                        {
                            ["key"] = Key(w, ms, share, max), ["hitsHistory"] = hits, ["predictedHistory"] = predicted, ["hitsGraphPlusHistory"] = hitsWithGraph,
                        });
                    }
        return new JsonObject
        {
            ["note"] = "key = window/minShared/minShare/maxFiles; history = 📜 top 10 from history older than each change",
            ["cases"] = cases.Count,
            ["actual"] = cases.Sum(c => c.Actual.Count),
            ["hitsGraph"] = cases.Select((c, i) => c.Actual.Count(graphSets[i].Contains)).Sum(),
            ["hitsBaseline"] = cases.Select((c, i) => c.Actual.Count(baseSets[i].Contains)).Sum(),
            ["predictedBaseline"] = baseSets.Sum(b => b.Count),
            ["settings"] = settings,
        };
    }

    /// <summary>
    /// The rule: the highest pooled recall of graph + 📜 among settings whose 📜 precision is at least the plain
    /// co-change baseline's on every tuning repo; ties go to higher 📜 precision, then grid order.
    /// </summary>
    public static int Select(List<string> files, string? outFile)
    {
        var runs = files.Select(f => (File: Path.GetFileName(f), Tuning: JsonNode.Parse(File.ReadAllText(f))!["tuning"]
            ?? throw new InvalidOperationException($"{f} has no tuning block (run with --tune)"))).ToList();
        var keys = runs[0].Tuning["settings"]!.AsArray().Select(x => (string)x!["key"]!).ToList();
        double Div(long a, long b) => b == 0 ? 0 : (double)a / b;
        JsonObject Of(JsonNode run, string key) => run["settings"]!.AsArray().Single(x => (string)x!["key"]! == key)!.AsObject();

        var rows = keys.Select((key, order) =>
        {
            var perRepo = runs.Select(r =>
            {
                var x = Of(r.Tuning, key);
                var precision = Div((int)x["hitsHistory"]!, (int)x["predictedHistory"]!);
                var baselinePrecision = Div((int)r.Tuning["hitsBaseline"]!, (int)r.Tuning["predictedBaseline"]!);
                return (Eligible: precision >= baselinePrecision, Precision: precision, BaselinePrecision: baselinePrecision);
            }).ToList();
            long actual = runs.Sum(r => (int)r.Tuning["actual"]!);
            var recall = Div(runs.Sum(r => (int)Of(r.Tuning, key)["hitsGraphPlusHistory"]!), actual);
            var precision = Div(runs.Sum(r => (int)Of(r.Tuning, key)["hitsHistory"]!), runs.Sum(r => (int)Of(r.Tuning, key)["predictedHistory"]!));
            return (Key: key, Order: order, Eligible: perRepo.All(p => p.Eligible), Recall: recall, Precision: precision, PerRepo: perRepo);
        }).ToList();
        var chosen = rows.Where(r => r.Eligible).OrderByDescending(r => r.Recall).ThenByDescending(r => r.Precision).ThenBy(r => r.Order).FirstOrDefault();

        var output = new JsonObject
        {
            ["repos"] = new JsonArray([.. runs.Select(r => (JsonNode)r.File)]),
            ["rule"] = "highest pooled graph + 📜 recall among settings whose 📜 precision ≥ the co-change top-10 baseline's on every tuning repo; ties: higher 📜 precision, then grid order",
            ["graphOnlyRecall"] = Math.Round(Div(runs.Sum(r => (int)r.Tuning["hitsGraph"]!), runs.Sum(r => (int)r.Tuning["actual"]!)), 3),
            ["chosen"] = chosen.Key,
            ["settings"] = new JsonArray([.. rows.OrderByDescending(r => r.Recall).ThenByDescending(r => r.Precision).ThenBy(r => r.Order).Select(r => (JsonNode)new JsonObject
            {
                ["key"] = r.Key, ["eligible"] = r.Eligible,
                ["graphPlusHistoryRecall"] = Math.Round(r.Recall, 3), ["historyPrecision"] = Math.Round(r.Precision, 3),
                ["perRepo"] = new JsonArray([.. r.PerRepo.Select(p => (JsonNode)new JsonObject
                {
                    ["historyPrecision"] = Math.Round(p.Precision, 3), ["baselinePrecision"] = Math.Round(p.BaselinePrecision, 3),
                })]),
            })]),
        };
        var text = output.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if (outFile is not null) File.WriteAllText(outFile, text + "\n");
        Console.WriteLine(text);
        return chosen.Key is null ? 1 : 0;
    }
}

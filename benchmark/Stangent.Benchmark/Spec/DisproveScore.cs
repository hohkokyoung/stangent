using System.Text.Json.Nodes;
using Stangent.Spec.Trace;

namespace Stangent.Benchmark.Spec;

/// <summary>
/// The disprove step against the frozen key (D119): the findings before it, and what still reaches the BA after it, by <see cref="FsdScore"/>'s own
/// rule, so nothing about the scoring moves. A finding the step closed or sent to developers isn't in front of the BA. The bars are 2c's, set before
/// any run: at least 27 of 30 found, false alarms at or below 30%. Code only.
/// </summary>
public static class DisproveScore
{
    public static readonly JsonObject Before2c = new() { ["found"] = 24, ["of"] = 30, ["falseAlarmShare"] = 0.44, ["results"] = "benchmark/results/2026-11-03-phase2c-sonarr-links-score.json" };

    public static JsonObject Run(string root, string keyPath, string? handCheckPath)
    {
        var before = FsdScore.Run(root, keyPath, handCheckPath);
        var after = FsdScore.Run(root, keyPath, handCheckPath, keep: e => TraceFindings.GoesTo(e) == "ba");
        var doc = (string)before["doc"]!;
        var findings = TraceFile.Load(root)["entries"]!.AsArray().OfType<JsonObject>()
            .Where(TraceFindings.IsFinding).Where(e => e["fsd"] is null || (string?)e["fsd"]!["doc"] == doc).ToList();
        var byId = findings.ToDictionary(e => (string)e["id"]!, StringComparer.Ordinal);
        var handCheck = handCheckPath is not null && File.Exists(handCheckPath) ? JsonNode.Parse(File.ReadAllText(handCheckPath))!.AsObject() : null;

        // A planted item the step took out of the BA's queue: the worst thing it can do, so each is named.
        var lost = before["items"]!.AsArray().OfType<JsonObject>().Where(i => (bool)i["found"]! && (string?)i["by"] is { } by && TraceFindings.GoesTo(byId[by]) != "ba")
            .Select(i => (JsonNode)new JsonObject
            {
                ["item"] = i["id"]!.DeepClone(), ["kind"] = i["kind"]!.DeepClone(), ["entry"] = i["by"]!.DeepClone(),
                ["verdict"] = (string?)byId[(string)i["by"]!]["verdict"], ["goesTo"] = TraceFindings.GoesTo(byId[(string)i["by"]!]),
                ["why"] = (string?)byId[(string)i["by"]!]["disproof"]?["why"],
            }).ToList();

        // The false alarms a person already checked by hand: the only ones known without paying for another check.
        JsonObject Hand(string verdict)
        {
            var ids = handCheck is null ? [] : handCheck.Where(h => h.Value is JsonObject o && (string?)o["verdict"] == verdict && byId.ContainsKey(h.Key)).Select(h => h.Key).ToList();
            return new JsonObject
            {
                ["checked"] = ids.Count,
                ["leftTheQueue"] = ids.Count(id => TraceFindings.GoesTo(byId[id]) != "ba"),
                ["stillInTheQueue"] = ids.Count(id => TraceFindings.GoesTo(byId[id]) == "ba"),
            };
        }
        var falseOnes = Hand("false");
        var realOnes = Hand("real");

        // After: the share of unplanted findings still queued, times the share of the hand-checked ones still queued that were false.
        var inQueue = (int)falseOnes["stillInTheQueue"]! + (int)realOnes["stillInTheQueue"]!;
        var unplantedShare = (double)after["noise"]!["worstCaseShare"]!;
        double? falseAfter = handCheck is null || inQueue == 0 ? null : Math.Round(unplantedShare * (int)falseOnes["stillInTheQueue"]! / inQueue, 3);
        var foundAfter = (int)after["found"]!["overall"]!["found"]!;

        return new JsonObject
        {
            ["doc"] = doc,
            ["note"] = "The disprove step (D119): the frozen key before and after. falseAlarmShare uses the hand check of the 20 sampled before the step; a fresh hand check of what's left is still owed before a bar is called met (D61).",
            ["before2c"] = Before2c.DeepClone(),
            ["before"] = new JsonObject { ["found"] = before["found"]!["overall"]!.DeepClone(), ["noise"] = before["noise"]!.DeepClone() },
            ["after"] = new JsonObject
            {
                ["found"] = after["found"]!.DeepClone(), ["foundAsKind"] = after["foundAsKind"]!.DeepClone(),
                ["findings"] = after["noise"]!["findings"]!.DeepClone(), ["unplanted"] = after["noise"]!["unplanted"]!.DeepClone(),
                ["falseAlarmShare"] = falseAfter,
            },
            ["handChecked"] = new JsonObject { ["false"] = falseOnes, ["real"] = realOnes },
            ["byVerdict"] = new JsonObject(new[] { "unverified", "stands", "refuted", "page-gap", "unreachable", "cant-tell" }
                .Select(v => KeyValuePair.Create(v, (JsonNode?)findings.Count(e => ((string?)e["verdict"] ?? "unverified") == v)))),
            ["byQueue"] = new JsonObject(new[] { "ba", "developer", "closed", "page-owner", "decided" }
                .Select(q => KeyValuePair.Create(q, (JsonNode?)findings.Count(e => TraceFindings.GoesTo(e) == q)))),
            ["plantedLost"] = new JsonArray([.. lost]),
            ["bars"] = new JsonObject
            {
                ["found27of30"] = foundAfter >= 27,
                ["falseAlarmsAtMost30Percent"] = falseAfter is { } share ? share <= 0.30 : null,
            },
            ["cost"] = before["cost"]!.DeepClone(),
        };
    }
}

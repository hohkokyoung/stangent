using System.Text.Json;
using System.Text.Json.Nodes;

namespace Stangent.Core.Telemetry;

/// <summary>
/// An estimate of a run before it starts (T.2): the calls it expects times what that step has cost per call in this repo,
/// read from telemetry. No price list and no guess from characters, so no number here can go stale. A repo with no history has
/// no estimate, and says so; the driver's cap still holds.
/// </summary>
public static class CostHistory
{
    public static JsonObject Estimate(string repoRoot, string step, int calls)
    {
        var dir = Environment.GetEnvironmentVariable(TelemetryWriter.DirVariable) is { Length: > 0 } set ? set : Path.Combine(repoRoot, ".stangent", "telemetry");
        var (seen, total) = Read(dir, step);
        var perCall = seen == 0 ? (double?)null : total / seen;
        return new JsonObject
        {
            ["step"] = step,
            ["calls"] = calls,
            ["earlierCalls"] = seen,
            ["earlierCostUsd"] = Math.Round(total, 4),
            ["perCallUsd"] = perCall is null ? null : Math.Round(perCall.Value, 4),
            ["estimateUsd"] = perCall is null ? null : Math.Round(perCall.Value * calls, 2),
            ["basis"] = seen == 0 ? $"no earlier `{step}` call with a cost in this repo's telemetry" : $"{seen} earlier `{step}` calls in this repo, average ${perCall:0.####}",
        };
    }

    static (int Calls, double Usd) Read(string dir, string step)
    {
        if (!Directory.Exists(dir)) return (0, 0);
        var (calls, usd) = (0, 0.0);
        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
            foreach (var line in File.ReadLines(file))
            {
                // Most lines are other commands: skip them without parsing.
                if (!line.Contains("gen_ai.usage.cost_usd", StringComparison.Ordinal)) continue;
                JsonNode? node;
                try { node = JsonNode.Parse(line); } catch (JsonException) { continue; }
                var attributes = node?["attributes"];
                if ((string?)attributes?["stangent.command"] != step) continue;
                calls++;
                usd += (double?)attributes?["gen_ai.usage.cost_usd"] ?? 0;
            }
        return (calls, usd);
    }
}

using System.Text.Json.Nodes;

namespace Stangent.Spec.Pages;

/// <summary>
/// What one AI call cost, read from Claude Code's JSON result (<c>claude -p --output-format json</c>, D60). Also the
/// running total per role on a page (writer, verifier, judge).
/// </summary>
public sealed record Usage(string? Model, int Calls, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, double CostUsd, long Ms)
{
    public static readonly Usage None = new(null, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Reads a Claude Code result. The model is the one that wrote the most output (a call may also use a small
    /// model for housekeeping).</summary>
    public static Usage FromClaudeResult(JsonNode? result)
    {
        if (result is not JsonObject o) return None with { Calls = 1 };
        var u = o["usage"] as JsonObject;
        string? model = null;
        if (o["modelUsage"] is JsonObject models && models.Count > 0)
            model = models.OrderByDescending(m => Long(m.Value?["outputTokens"])).ThenBy(m => m.Key, StringComparer.Ordinal).First().Key;
        return new Usage(model, 1, Long(u?["input_tokens"]), Long(u?["output_tokens"]), Long(u?["cache_read_input_tokens"]),
            Long(u?["cache_creation_input_tokens"]), Double(o["total_cost_usd"]), Long(o["duration_ms"]));
    }

    public Usage Add(Usage other) => new(other.Model ?? Model, Calls + other.Calls, InputTokens + other.InputTokens, OutputTokens + other.OutputTokens,
        CacheReadTokens + other.CacheReadTokens, CacheWriteTokens + other.CacheWriteTokens, Math.Round(CostUsd + other.CostUsd, 6), Ms + other.Ms);

    public JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["calls"] = Calls, ["inputTokens"] = InputTokens, ["outputTokens"] = OutputTokens,
            ["cacheReadTokens"] = CacheReadTokens, ["cacheWriteTokens"] = CacheWriteTokens, ["costUsd"] = CostUsd, ["ms"] = Ms,
        };
        if (Model is not null) o["model"] = Model;
        return o;
    }

    public static Usage FromJson(JsonNode? n) => n is not JsonObject o ? None : new Usage((string?)o["model"], (int)Long(o["calls"]), Long(o["inputTokens"]),
        Long(o["outputTokens"]), Long(o["cacheReadTokens"]), Long(o["cacheWriteTokens"]), Double(o["costUsd"]), Long(o["ms"]));

    static long Long(JsonNode? n) => (long)Double(n);

    static double Double(JsonNode? n) =>
        n is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? double.Parse(v.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture) : 0;
}

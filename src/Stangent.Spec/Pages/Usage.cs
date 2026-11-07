using System.Text.Json.Nodes;

namespace Stangent.Spec.Pages;

/// <summary>
/// What one AI call cost, read from Claude Code's JSON result (<c>claude -p --output-format json</c>, D60). Also the
/// running total per role on a page (writer, verifier, judge).
/// </summary>
public sealed record Usage(string? Model, int Calls, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, double CostUsd, long Ms)
{
    public static readonly Usage None = new(null, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>The settings the driver ran the call with (D161), from the <c>stangent_run</c> block it adds to Claude Code's
    /// result. Null when no driver said: an in-session run, a hand run, or a record from before D161.</summary>
    public RunSettings? Run { get; init; }

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
            Long(u?["cache_creation_input_tokens"]), Double(o["total_cost_usd"]), Long(o["duration_ms"]))
        { Run = RunSettings.FromJson(o["stangent_run"]) ?? RunSettings.Unknown };
    }

    /// <summary>Sums two. Settings that differ between the calls read <c>mixed</c>; a side with no calls takes nothing.</summary>
    public Usage Add(Usage other) => new(other.Model ?? Model, Calls + other.Calls, InputTokens + other.InputTokens, OutputTokens + other.OutputTokens,
        CacheReadTokens + other.CacheReadTokens, CacheWriteTokens + other.CacheWriteTokens, Math.Round(CostUsd + other.CostUsd, 6), Ms + other.Ms)
    { Run = Calls == 0 ? other.Run : other.Calls == 0 ? Run : RunSettings.Merge(Run, other.Run) };

    public JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["calls"] = Calls, ["inputTokens"] = InputTokens, ["outputTokens"] = OutputTokens,
            ["cacheReadTokens"] = CacheReadTokens, ["cacheWriteTokens"] = CacheWriteTokens, ["costUsd"] = CostUsd, ["ms"] = Ms,
        };
        if (Model is not null) o["model"] = Model;
        if (Run is not null) o["settings"] = Run.ToJson();
        return o;
    }

    public static Usage FromJson(JsonNode? n) => n is not JsonObject o ? None : new Usage((string?)o["model"], (int)Long(o["calls"]), Long(o["inputTokens"]),
        Long(o["outputTokens"]), Long(o["cacheReadTokens"]), Long(o["cacheWriteTokens"]), Double(o["costUsd"]), Long(o["ms"]))
    { Run = RunSettings.FromJson(o["settings"]) };

    static long Long(JsonNode? n) => (long)Double(n);

    static double Double(JsonNode? n) =>
        n is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? double.Parse(v.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture) : 0;
}

/// <summary>What a driver ran one AI call with (D161): the agent, the model asked for, effort, mode (<c>batch</c>, <c>agents</c>,
/// <c>agentic</c>) and Claude Code's version. A field nobody said is <c>unknown</c>, never guessed; two calls that differ read <c>mixed</c>.</summary>
public sealed record RunSettings(string Agent, string Model, string Effort, string Mode, string ClaudeVersion)
{
    public const string UnknownValue = "unknown";
    public const string MixedValue = "mixed";
    public static readonly RunSettings Unknown = new(UnknownValue, UnknownValue, UnknownValue, UnknownValue, UnknownValue);

    public static RunSettings? FromJson(JsonNode? n) => n is not JsonObject o ? null
        : new RunSettings(Field(o, "agent"), Field(o, "model"), Field(o, "effort"), Field(o, "mode"), Field(o, "claudeVersion"));

    static string Field(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim().Length > 0 ? s.Trim() : UnknownValue;

    public static RunSettings Merge(RunSettings? a, RunSettings? b)
    {
        a ??= Unknown; b ??= Unknown;
        static string M(string x, string y) => x == y ? x : MixedValue;
        return new RunSettings(M(a.Agent, b.Agent), M(a.Model, b.Model), M(a.Effort, b.Effort), M(a.Mode, b.Mode), M(a.ClaudeVersion, b.ClaudeVersion));
    }

    public JsonObject ToJson() => new() { ["agent"] = Agent, ["model"] = Model, ["effort"] = Effort, ["mode"] = Mode, ["claudeVersion"] = ClaudeVersion };
}


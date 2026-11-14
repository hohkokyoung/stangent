using System.Text.Json.Nodes;

namespace Stangent.Core;

/// <summary>
/// The model and effort one AI step runs with (D161). A run's flag wins, then the step's own setting in <c>stangent.yaml</c>,
/// then the global one. Nothing set means the run refuses: there is no default, so no answer is ever made under settings
/// nobody chose.
/// </summary>
public sealed record AiSettings(string Agent, string Model, string Effort, string ModelFrom, string EffortFrom)
{
    /// <summary>Claude Code's <c>--effort</c> levels.</summary>
    public static readonly IReadOnlyList<string> Efforts = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>Every AI step's agent, as <c>plugin/agents/</c> names them (a test holds the two equal).</summary>
    public static readonly IReadOnlyList<string> Agents = ["stangent-agree-checker", "stangent-bug-reproducer", "stangent-candidate-finder", "stangent-file-finder", "stangent-finding-disprover", "stangent-link-judge", "stangent-match-checker", "stangent-page-asker", "stangent-page-verifier", "stangent-page-writer", "stangent-question-writer", "stangent-requirement-checker", "stangent-requirement-judge", "stangent-requirement-matcher", "stangent-requirement-writer", "stangent-summary-writer"];

    /// <summary>Every step's settings at once, so a run with one step unpinned stops before its first call, not halfway (D161).</summary>
    public static IReadOnlyList<AiSettings> ResolveAll(StangentConfig config, string? modelFlag = null, string? effortFlag = null) =>
        [.. Agents.Select(a => Resolve(config, a, modelFlag, effortFlag))];

    public static AiSettings Resolve(StangentConfig config, string agent, string? modelFlag = null, string? effortFlag = null)
    {
        var step = config.Ai.Steps.GetValueOrDefault(agent);
        var (model, modelFrom) = Pick(modelFlag, step?.Model, config.Ai.Model, agent, "model");
        var (effort, effortFrom) = Pick(effortFlag, step?.Effort, config.Ai.Effort, agent, "effort");
        var missing = new[] { model is null ? "ai.model" : null, effort is null ? "ai.effort" : null }.OfType<string>().ToList();
        if (missing.Count > 0)
            throw new StangentException("ai_settings_missing",
                $"No {string.Join(" or ", missing)} for {agent}: set it in stangent.yaml (`ai: {{ model: …, effort: … }}`, or `ai.steps.{agent}`), or pass --model/--effort for one run. A run never falls back to a default (D161).",
                ExitCodes.Usage);
        if (!Efforts.Contains(effort))
            throw new StangentException("ai_effort_unknown", $"Effort '{effort}' ({effortFrom}) isn't one of {string.Join(", ", Efforts)}.", ExitCodes.Usage);
        return new AiSettings(agent, model!, effort!, modelFrom, effortFrom);
    }

    static (string? Value, string From) Pick(string? flag, string? step, string? global, string agent, string field) =>
        !string.IsNullOrWhiteSpace(flag) ? (flag.Trim(), $"--{field}")
        : !string.IsNullOrWhiteSpace(step) ? (step.Trim(), $"ai.steps.{agent}.{field}")
        : !string.IsNullOrWhiteSpace(global) ? (global.Trim(), $"ai.{field}")
        : (null, "");

    public JsonObject ToJson() => new()
    {
        ["agent"] = Agent, ["model"] = Model, ["effort"] = Effort, ["modelFrom"] = ModelFrom, ["effortFrom"] = EffortFrom,
    };
}

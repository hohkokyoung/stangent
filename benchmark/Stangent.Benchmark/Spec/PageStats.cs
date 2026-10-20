using System.Text.Json.Nodes;
using Stangent.Spec.Pages;

namespace Stangent.Benchmark.Spec;

/// <summary>Phase 2a, step 7: what generating every page cost and how the checks went, from the recorded pages
/// (specs/reference) and the failed list. Code only, read from committed files.</summary>
public static class PageStats
{
    public static JsonObject Run(string root)
    {
        var paths = new SpecPaths(root);
        var pages = Directory.EnumerateFiles(paths.ReferenceDir, "*.json", SearchOption.AllDirectories)
            .Where(f => f != paths.FailedFile).Select(f => JsonNode.Parse(File.ReadAllText(f))!.AsObject())
            .Where(p => p["generated"] is not null).ToList();
        var failed = PageRun.Failed(paths).OfType<JsonObject>().ToList();

        JsonObject Dist(IEnumerable<double> values)
        {
            var v = values.Order().ToList();
            if (v.Count == 0) return new JsonObject { ["n"] = 0 };
            double Q(double q) => v[(int)Math.Min(v.Count - 1, Math.Floor(q * v.Count))];
            return new JsonObject { ["n"] = v.Count, ["median"] = Math.Round(Q(0.5), 1), ["p90"] = Math.Round(Q(0.9), 1), ["max"] = Math.Round(v[^1], 1), ["mean"] = Math.Round(v.Average(), 1), ["total"] = Math.Round(v.Sum(), 1) };
        }
        JsonObject Role(string role) => new()
        {
            ["inputTokens"] = Dist(pages.Select(p => Usage.FromJson(p["generated"]![role])).Select(u => (double)(u.InputTokens + u.CacheReadTokens + u.CacheWriteTokens))),
            ["outputTokens"] = Dist(pages.Select(p => (double)Usage.FromJson(p["generated"]![role]).OutputTokens)),
            ["seconds"] = Dist(pages.Select(p => Usage.FromJson(p["generated"]![role]).Ms / 1000.0)),
            ["costUsd"] = Dist(pages.Select(p => Usage.FromJson(p["generated"]![role]).CostUsd)),
            ["calls"] = Dist(pages.Select(p => (double)Usage.FromJson(p["generated"]![role]).Calls)),
            ["models"] = new JsonArray([.. pages.Select(p => (string?)p["generated"]![role]!["model"]).OfType<string>().Distinct().Order().Select(m => (JsonNode)m)]),
        };
        var findings = pages.SelectMany(p => p["verification"]!["findings"]!.AsArray().OfType<JsonObject>()).ToList();
        var byKind = pages.GroupBy(p => (string)p["kind"]!).ToDictionary(g => g.Key, g => g.Count());
        return new JsonObject
        {
            ["repo"] = Path.GetFileName(root),
            ["note"] = "Phase 2a step 7: every recorded page and every failed one (specs/reference), with what the code checks and the fresh verifier found, and the cost per page (writer and verifier apart).",
            ["recorded"] = pages.Count,
            ["byKind"] = new JsonObject(byKind.OrderBy(k => k.Key).Select(k => KeyValuePair.Create(k.Key, (JsonNode?)k.Value))),
            ["failed"] = failed.Count,
            ["failedReasons"] = new JsonObject(failed.GroupBy(f => (string)f["reason"]!).OrderBy(g => g.Key).Select(g => KeyValuePair.Create(g.Key, (JsonNode?)g.Count()))),
            ["failedPages"] = new JsonArray([.. failed.Select(f => (JsonNode)new JsonObject { ["id"] = f["id"]!.DeepClone(), ["reason"] = f["reason"]!.DeepClone() })]),
            ["codeCheck"] = new JsonObject
            {
                ["passedFirstTry"] = pages.Count(p => (int)p["generated"]!["checkRetries"]! == 0),
                ["passedAfterRetry"] = pages.Count(p => (int)p["generated"]!["checkRetries"]! > 0),
                ["failedTwice"] = failed.Count(f => ((string)f["reason"]!).Contains("twice")),
            },
            ["verifier"] = new JsonObject
            {
                ["pagesWithFindings"] = pages.Count(p => p["verification"]!["findings"]!.AsArray().Count > 0),
                ["findings"] = findings.Count,
                ["findingsPerPage"] = Dist(pages.Select(p => (double)p["verification"]!["findings"]!.AsArray().Count)),
                ["fixed"] = findings.Count(f => (string)f["outcome"]! == "fixed"),
                ["rejected"] = findings.Count(f => (string)f["outcome"]! == "rejected"),
                ["contradicted"] = findings.Count(f => (string)f["kind"]! == "contradicted"),
                ["unsupported"] = findings.Count(f => (string)f["kind"]! == "unsupported"),
                ["droppedWithoutEvidence"] = pages.Sum(p => (int)p["verification"]!["dropped"]!),
            },
            ["statements"] = Dist(pages.Select(p => (double)Stangent.Spec.Pages.PageChecks.Statements(p).Count())),
            ["sliceChars"] = Dist(pages.Select(p => (double)(int)p["generated"]!["slice"]!["chars"]!)),
            ["writer"] = Role("writer"),
            ["verifier_cost"] = Role("verifier"),
        };
    }
}

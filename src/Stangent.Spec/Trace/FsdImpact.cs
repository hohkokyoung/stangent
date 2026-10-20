using System.Text.Json.Nodes;
using Stangent.Graph.Impact;

namespace Stangent.Spec.Trace;

/// <summary>
/// Impact answers reach FSD sections (D52, impact-report v5): a reference page is reached when a symbol one of its
/// statements cites is changed or affected; its links in <c>specs/trace.yaml</c> lead to sections. Code only.
/// </summary>
public static class FsdImpact
{
    public const int Shown = 10;

    public static JsonObject Sections(string root, IReadOnlyCollection<string> reached, bool expand)
    {
        var index = SpecIndex.Load(root);
        var trace = TraceFile.Load(root)["entries"]!.AsArray().OfType<JsonObject>().ToList();
        if (index.Pages.Count == 0 || trace.Count == 0) return ImpactAnalyzer.NoFsdSections();
        var hit = reached.ToHashSet(StringComparer.Ordinal);
        var pages = index.Pages.Where(p => Pages.PageChecks.Statements(p.Value).Any(s => s.Statement["cites"]!.AsArray().Any(c => hit.Contains((string)c!))))
            .Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var items = trace.Where(e => e["fsd"] is JsonObject && (string?)e["page"]?["id"] is { } p && pages.Contains(p))
            .GroupBy(e => ((string)e["fsd"]!["doc"]!, (string)e["fsd"]!["section"]!))
            .OrderBy(g => g.Key.Item1, StringComparer.Ordinal).ThenBy(g => TraceFile.SectionKey(g.Key.Item2), StringComparer.Ordinal)
            .Select(g => (JsonNode)new JsonObject
            {
                ["doc"] = g.Key.Item1, ["section"] = g.Key.Item2, ["title"] = (string?)g.First()["fsd"]!["title"] ?? "",
                ["pages"] = new JsonArray([.. g.Select(e => (string)e["page"]!["id"]!).Distinct().Order(StringComparer.Ordinal).Select(p => (JsonNode)p)]),
                ["states"] = new JsonArray([.. g.Select(e => (string)e["state"]!).Distinct().Order(StringComparer.Ordinal).Select(s => (JsonNode)s)]),
                ["verified"] = g.All(e => e["decision"] is not null),
            }).ToList();
        var shown = expand ? items : [.. items.Take(Shown)];
        return new JsonObject { ["status"] = "ok", ["total"] = items.Count, ["items"] = new JsonArray([.. shown.Select(i => i!.DeepClone())]), ["more"] = items.Count - shown.Count };
    }
}

using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.Evidence;
using Stangent.Graph.Detectors;

namespace Stangent.Graph;

/// <summary>
/// What the graph knows about tests that open screens (D115): the 🟡 lines and the ❓ calls. Each line's citation fills an
/// <c>evidence</c> record of strength <c>read</c>: the spec exists. Only a real run can make it <c>ran</c>, and the graph never runs anything.
/// </summary>
public static class PlaywrightLinks
{
    public static JsonObject Answer(CodeGraph graph, string repoName, string? screen = null)
    {
        var pages = graph.AllNodes.Where(n => n.Kind == NodeKind.Page).DistinctBy(n => n.Id).ToDictionary(n => n.Id, StringComparer.Ordinal);
        if (screen is not null && !pages.Values.Any(p => Is(p, screen)))
            throw new StangentException("not_found", $"No screen on the map is '{screen}'.", ExitCodes.NotFound);
        var commit = SymbolLines.CommitOf(graph);
        var blind = graph.AllBlindSpots.Where(b => b.Detector == PlaywrightDetector.Detector).ToList();
        var links = graph.ScreenTests.Where(t => screen is null || (pages.TryGetValue(t.Page, out var p) && Is(p, screen)))
            .OrderBy(t => t.Spec, StringComparer.Ordinal).ThenBy(t => t.Line).ThenBy(t => t.Page, StringComparer.Ordinal).ToList();
        return new JsonObject
        {
            ["commit"] = commit, ["dirty"] = graph.DirtyFiles is null ? null : graph.DirtyFiles.Count > 0,
            ["specs"] = graph.ScreenTests.Select(t => t.Spec).Concat(blind.Select(b => b.File)).Distinct(StringComparer.Ordinal).Count(),
            ["gotosResolved"] = graph.ScreenTests.Select(t => (t.Spec, t.Line)).Distinct().Count(),
            ["blindSpots"] = blind.Count,
            ["links"] = new JsonArray([.. links.Select(t => (JsonNode)Link(graph, t, pages.GetValueOrDefault(t.Page), repoName, commit))]),
            ["unresolved"] = screen is not null ? new JsonArray() : new JsonArray([.. blind
                .OrderBy(b => b.File, StringComparer.Ordinal).ThenBy(b => b.Line).ThenBy(b => b.Reason, StringComparer.Ordinal)
                .Select(b => (JsonNode)new JsonObject { ["file"] = b.File, ["line"] = b.Line, ["reason"] = b.Reason })]),
        };
    }

    static bool Is(Node page, string screen) => page.Id == screen || page.Name == screen;

    static JsonObject Link(CodeGraph graph, ScreenTest t, Node? page, string repoName, string? commit)
    {
        var matches = commit is not null && graph.DirtyFiles?.Contains(t.Spec.Normalize(), StringComparer.Ordinal) == false;
        return new JsonObject
        {
            ["kind"] = EdgeKind.OpensScreen, ["label"] = t.Label == Label.Certain ? "certain" : "guessed", ["detector"] = PlaywrightDetector.Detector,
            ["spec"] = t.Spec, ["line"] = t.Line, ["goto"] = t.Goto, ["page"] = t.Page, ["route"] = page?.Name,
            ["strength"] = "read", ["linesMatchCommit"] = matches,
            ["citation"] = !matches ? null : SymbolLines.CitationJson(new Citation
            {
                Kind = CitationKind.Test, Ref = $"{repoName}/{t.Spec}", Lines = new LineRange(t.Line, t.Line),
            }),
        };
    }
}

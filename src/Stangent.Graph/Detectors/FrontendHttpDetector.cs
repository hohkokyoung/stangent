using System.Text.Json;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// Frontend → backend (D31). Links the TypeScript extractor's facts to the C# graph, all 🟡:
/// - `call` facts (Angular HttpClient, fetch, axios, in-house wrappers) → the endpoints whose path
///   and verb fit best; a leading base URL ({base}) is matched against the end of the path;
/// - `generated-call` facts (NSwag client whose file isn't in the repo) → by NSwag's naming:
///   `TodoListsClient.getTodoLists()` → the `GetTodoLists` action of the `TodoLists` group;
/// - typed results and bodies → `maps-field` lines from each frontend property to the backend
///   property with the same JSON name, so a renamed backend field reaches the code that reads it.
/// Calls that match nothing are ❓.
/// </summary>
public sealed class FrontendHttpDetector : IDetector
{
    public string Name => "frontend-http";
    const string Routes = "aspnetcore-routes";
    const int MaxFieldDepth = 3;

    public void Extract(ExtractContext ctx) { } // facts come from the TypeScript extractor

    sealed record Endpoint(Node Node, string Verb, string[] Segments, List<string> Targets);
    sealed record Prop(string Name, string Id, string Type);

    public void Link(LinkContext ctx)
    {
        var facts = ctx.Facts(Name).ToList();
        if (facts.Count == 0) return;
        var routesTo = ctx.Graph.AllEdges.Where(e => e.Kind == EdgeKind.RoutesTo).GroupBy(e => e.Source)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Target).Distinct().ToList(), StringComparer.Ordinal);
        var nodes = ctx.Graph.AllNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var endpoints = ctx.Graph.AllNodes.Where(n => n.Kind == NodeKind.Endpoint).DistinctBy(n => n.Id)
            .Select(n => new Endpoint(n, n.Name[..n.Name.IndexOf(' ')], Segments(n.Name[(n.Name.IndexOf(' ') + 1)..]), routesTo.GetValueOrDefault(n.Id) ?? []))
            .OrderBy(e => e.Node.Id, StringComparer.Ordinal).ToList();
        var types = ctx.Facts(Routes).Where(f => f.Kind == "endpoint-types")
            .GroupBy(f => f.Owner).ToDictionary(g => g.Key, g => (Response: g.First()["response"], Body: g.First()["body"]), StringComparer.Ordinal);
        var csShapes = ctx.Facts(Routes).Where(f => f.Kind == "json-shape").GroupBy(f => f.Owner)
            .ToDictionary(g => g.Key, g => Props(g.First()["props"], "json"), StringComparer.Ordinal);
        var tsShapes = facts.Where(f => f.Kind == "shape").GroupBy(f => f.Owner)
            .ToDictionary(g => g.Key, g => Props(g.First()["props"], "name"), StringComparer.Ordinal);

        foreach (var call in facts.Where(f => f.Kind == "call"))
        {
            var matches = Match(call["verb"], call["url"], endpoints);
            if (matches.Count == 0)
            {
                ctx.AddBlindSpot(call.Owner, call.File, call.Line, $"HTTP {call["verb"]} {call["url"]} matched no backend endpoint", Name);
                continue;
            }
            foreach (var e in matches)
            {
                ctx.AddEdge(call.Owner, e.Node.Id, EdgeKind.CallsHttp, Label.Guessed, Name);
                foreach (var target in e.Targets)
                {
                    if (!types.TryGetValue(target, out var t)) continue;
                    if (call["responseType"].Length > 0 && t.Response.Length > 0)
                        MapFields(ctx, call["responseType"], t.Response, tsShapes, csShapes, 0, []);
                    if (call["bodyType"].Length > 0 && t.Body.Length > 0)
                        MapFields(ctx, call["bodyType"], t.Body, tsShapes, csShapes, 0, []);
                }
            }
        }

        foreach (var call in facts.Where(f => f.Kind == "generated-call"))
        {
            var matches = Generated(call["client"], call["method"], endpoints, nodes);
            if (matches.Count == 0)
                ctx.AddBlindSpot(call.Owner, call.File, call.Line,
                    $"generated client call {call["client"]}.{call["method"]}() matched no backend endpoint by {call["generator"]} naming", Name);
            foreach (var e in matches) ctx.AddEdge(call.Owner, e.Node.Id, EdgeKind.CallsHttp, Label.Guessed, Name);
        }
    }

    static string[] Segments(string path) => path.Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The endpoints that fit a call best: same verb (or ANY), path scored segment by segment.</summary>
    static List<Endpoint> Match(string verb, string url, List<Endpoint> endpoints)
    {
        var path = url;
        var scheme = path.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) path = path[(scheme + 3)..] is var rest && rest.IndexOf('/') is var slash and >= 0 ? rest[slash..] : "/";
        var suffix = path.StartsWith("{base}", StringComparison.Ordinal);
        if (suffix) path = path["{base}".Length..];
        var segments = Segments(path);
        if (segments.Length == 0 || segments.All(s => s.Contains("{*}", StringComparison.Ordinal))) return [];

        var scored = new List<(Endpoint E, int Score)>();
        foreach (var e in endpoints)
        {
            if (!(e.Verb == verb || e.Verb == "ANY" || verb == "ANY")) continue;
            var candidates = new List<string[]> { e.Segments };
            // Optional trailing parameters ({id?}) may be left off by the caller.
            var trimmed = e.Segments.Reverse().SkipWhile(s => s.StartsWith('{') && s.EndsWith("?}", StringComparison.Ordinal)).Reverse().ToArray();
            if (trimmed.Length != e.Segments.Length) candidates.Add(trimmed);
            var best = -1;
            foreach (var template in candidates)
            {
                if (suffix && template.Length < segments.Length) continue;
                var aligned = suffix ? template[^segments.Length..] : template;
                if (!aligned.Zip(segments).Any(p => !p.First.StartsWith('{') && string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase))) continue;
                best = Math.Max(best, HttpServicesDetector.Score("/" + string.Join('/', aligned), "/" + string.Join('/', segments)));
            }
            if (best >= 0) scored.Add((e, best));
        }
        if (scored.Count == 0) return [];
        var top = scored.Max(s => s.Score);
        return [.. scored.Where(s => s.Score == top).Select(s => s.E)];
    }

    /// <summary>NSwag: client "TodoListsClient" → group "TodoLists"; method "getTodoLists" (or "infoGET") → action "GetTodoLists" ("Info").</summary>
    static List<Endpoint> Generated(string client, string method, List<Endpoint> endpoints, Dictionary<string, Node> nodes)
    {
        var group = client.EndsWith("Client", StringComparison.Ordinal) ? client[..^"Client".Length] : client;
        var names = new List<string> { method };
        var verbSuffix = System.Text.RegularExpressions.Regex.Match(method, "(GET|POST|PUT|DELETE|PATCH)$");
        if (verbSuffix.Success) names.Add(method[..^verbSuffix.Length]);
        bool NameFits(string targetId) =>
            nodes.TryGetValue(targetId, out var n) && names.Any(m => string.Equals(n.Name[(n.Name.LastIndexOf('.') + 1)..], m, StringComparison.OrdinalIgnoreCase));
        bool GroupFits(Endpoint e, string targetId) =>
            (nodes.TryGetValue(targetId, out var n) && n.Name.Contains('.')
                && n.Name[..n.Name.LastIndexOf('.')] is var type && (string.Equals(type, group, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, group + "Controller", StringComparison.OrdinalIgnoreCase)))
            || e.Segments.Any(s => string.Equals(s, group, StringComparison.OrdinalIgnoreCase));
        var matches = endpoints.Where(e => e.Targets.Any(t => NameFits(t) && GroupFits(e, t))).ToList();
        if (verbSuffix.Success) matches = [.. matches.Where(e => e.Verb == verbSuffix.Value || e.Verb == "ANY").DefaultIfEmpty().OfType<Endpoint>()];
        return matches;
    }

    static List<Prop> Props(string json, string nameKey)
    {
        var list = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json) ?? [];
        return [.. list.Select(p => new Prop(p.GetValueOrDefault(nameKey) ?? "", p.GetValueOrDefault("id") ?? "", p.GetValueOrDefault("type") ?? ""))];
    }

    /// <summary>Each frontend property → the backend property with the same JSON name, recursing into nested types.</summary>
    void MapFields(LinkContext ctx, string tsType, string csType, Dictionary<string, List<Prop>> ts, Dictionary<string, List<Prop>> cs, int depth, HashSet<(string, string)> seen)
    {
        if (depth > MaxFieldDepth || !seen.Add((tsType, csType)) || !ts.TryGetValue(tsType, out var front) || !cs.TryGetValue(csType, out var back)) return;
        var byJson = back.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var f in front)
        {
            if (!byJson.TryGetValue(f.Name, out var b)) continue;
            ctx.AddEdge(f.Id, b.Id, EdgeKind.MapsField, Label.Guessed, Name);
            if (f.Type.Length > 0 && b.Type.Length > 0) MapFields(ctx, f.Type, b.Type, ts, cs, depth + 1, seen);
        }
    }
}

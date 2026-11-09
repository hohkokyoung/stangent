using System.Text.Json;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// Frontend → backend (D31). Links the TypeScript extractor's facts to the C# graph, 🟡 unless code proves the match (D175: ✅
/// for a constant verb, path and API root that exactly one endpoint fits); each call's verdict and reason is an `http-match` fact:
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

        // D175: a line is ✅ when any call behind it is proved; each call's own verdict and reason is an http-match fact.
        var calls = facts.Where(f => f.Kind == "call").Select(call => (Call: call, Matches: Match(call["verb"], call["url"], endpoints))).ToList();
        var proved = new HashSet<(string, string)>();
        foreach (var (call, matches) in calls)
        {
            if (matches.Count == 0) continue;
            var reason = Proof(call, matches, endpoints);
            if (reason.Length == 0) proved.Add((call.Owner, matches[0].Node.Id));
            foreach (var e in matches)
                ctx.AddFact(Name, "http-match", call.Owner, call.File, call.Line, [("endpoint", e.Node.Id), ("label", reason.Length == 0 ? "certain" : "guessed"),
                    ("reason", reason), ("verb", call["verb"]), ("url", call["url"]), .. (call["from"] == "parameter" ? [("origin", call["origin"])] : Array.Empty<(string, string)>())]);
        }
        // D190: a call written once with a URL its callers give is several facts, one per value; values that fit no endpoint are
        // said once, and only when none fits.
        var paramMatched = calls.Where(c => c.Call["from"] == "parameter" && c.Matches.Count > 0).Select(c => (c.Call.Owner, c.Call.File, c.Call.Line)).ToHashSet();
        var paramSaid = new HashSet<(string, string?, int)>();
        foreach (var (call, matches) in calls)
        {
            if (matches.Count == 0 && call["from"] == "parameter")
            {
                var at = (call.Owner, call.File, call.Line);
                if (!paramMatched.Contains(at) && paramSaid.Add(at))
                    ctx.AddBlindSpot(call.Owner, call.File, call.Line, $"HTTP call to a URL its callers give ({call["of"]} {(call["of"] == "1" ? "value" : "values")}) matched no backend endpoint", Name);
                continue;
            }
            if (matches.Count == 0)
            {
                ctx.AddBlindSpot(call.Owner, call.File, call.Line, $"HTTP {call["verb"]} {call["url"]} matched no backend endpoint", Name);
                continue;
            }
            foreach (var e in matches)
            {
                ctx.AddEdge(call.Owner, e.Node.Id, EdgeKind.CallsHttp, proved.Contains((call.Owner, e.Node.Id)) ? Label.Certain : Label.Guessed, Name);
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
            foreach (var e in matches)
            {
                ctx.AddEdge(call.Owner, e.Node.Id, EdgeKind.CallsHttp, Label.Guessed, Name);
                ctx.AddFact(Name, "http-match", call.Owner, call.File, call.Line, ("endpoint", e.Node.Id), ("label", "guessed"),
                    ("reason", $"matched by {call["generator"]}'s naming of {call["client"]}.{call["method"]}(), not by a path"), ("verb", ""), ("url", ""));
            }
        }
    }

    /// <summary>
    /// D175: "" when code proves the call reaches its one endpoint: (1) a constant verb, (2) a constant path, runtime values only
    /// filling route parameters, (3) a constant API root, (4) exactly one endpoint fitting. Otherwise each failed test, in words.
    /// </summary>
    static string Proof(Fact call, List<Endpoint> matches, List<Endpoint> endpoints)
    {
        var failed = new List<string>();
        var verb = call["verb"];
        if (call["from"] == "parameter")
            failed.Add($"the URL is a parameter: one of {call["of"]} values its callers pass, this one given in {Short(call["origin"])}");
        switch (call["verbFrom"])
        {
            case "literal" or "library" or "setting" when verb != "ANY": break;
            case "name": failed.Add("verb read off the wrapper's name (a setting's @VERB would state it)"); break;
            default: failed.Add("verb unknown"); break;
        }
        var (path, suffix) = PathOf(call["url"]);
        var segments = Segments(path);
        var fits = Fits(verb, segments, suffix, endpoints);
        if (fits.Count != 1)
            failed.Add($"{fits.Count} endpoints fit ({string.Join(" and ", fits.Take(3).Select(e => e.Node.Name))}{(fits.Count > 3 ? " …" : "")})");
        var mixed = segments.FirstOrDefault(x => x.Contains("{*}", StringComparison.Ordinal) && x != "{*}");
        if (mixed is not null) failed.Add($"path built at runtime ('{mixed}')");
        if (fits.Count == 1 && matches.Count == 1 && fits[0] == matches[0] && Aligned(fits[0], segments, suffix) is var (template, offset))
        {
            for (var i = 0; i < segments.Length && offset + i < template.Length; i++)
                if (segments[i] == "{*}" && !template[offset + i].StartsWith('{'))
                {
                    failed.Add($"path built at runtime (a value where '{template[offset + i]}' is fixed)");
                    break;
                }
            if (suffix && offset > 0) failed.Add($"API root read at runtime ({{base}} stands for /{string.Join('/', template[..offset])})");
        }
        else if (fits.Count == 1 && !(matches.Count == 1 && fits[0] == matches[0])) failed.Add("the best match isn't the one endpoint that fits");
        return string.Join("; ", failed);
    }

    static string Short(string id) => id.Length == 0 ? "a caller" : id[(Math.Max(id.IndexOf('#'), id.IndexOf('|')) + 1)..];

    static (string Path, bool Suffix) PathOf(string url)
    {
        var path = url;
        var scheme = path.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) path = path[(scheme + 3)..] is var rest && rest.IndexOf('/') is var slash and >= 0 ? rest[slash..] : "/";
        var suffix = path.StartsWith("{base}", StringComparison.Ordinal);
        return (suffix ? path["{base}".Length..] : path, suffix);
    }

    /// <summary>The template a call lines up with, and where the call's first segment sits in it (after a {base} root).</summary>
    static (string[] Template, int Offset)? Aligned(Endpoint e, string[] segments, bool suffix)
    {
        foreach (var template in Candidates(e))
        {
            if (suffix && template.Length >= segments.Length && HttpServicesDetector.Score("/" + string.Join('/', template[^segments.Length..]), "/" + string.Join('/', segments)) >= 0)
                return (template, template.Length - segments.Length);
            if (!suffix && HttpServicesDetector.Score("/" + string.Join('/', template), "/" + string.Join('/', segments)) >= 0) return (template, 0);
        }
        return null;
    }

    static IEnumerable<string[]> Candidates(Endpoint e)
    {
        yield return e.Segments;
        var trimmed = e.Segments.Reverse().SkipWhile(s => s.StartsWith('{') && s.EndsWith("?}", StringComparison.Ordinal)).Reverse().ToArray();
        if (trimmed.Length != e.Segments.Length) yield return trimmed;
    }

    /// <summary>
    /// Every endpoint a call could reach: verb (or ANY) and path fit, with routing's own preference for a fixed segment over a
    /// parameter where the call's text is fixed. A runtime value could be any segment's text, so it never rules one out.
    /// </summary>
    static List<Endpoint> Fits(string verb, string[] segments, bool suffix, List<Endpoint> endpoints)
    {
        if (segments.Length == 0) return [];
        var fits = new List<(Endpoint E, int Fixed)>();
        foreach (var e in endpoints)
        {
            if (!(e.Verb == verb || e.Verb == "ANY" || verb == "ANY")) continue;
            if (Aligned(e, segments, suffix) is not var (template, offset)) continue;
            if (!RouteConstraintsHold(template, offset, segments)) continue;
            var fixedHits = segments.Select((x, i) => (x, i)).Count(p => offset + p.i < template.Length && !p.x.Contains("{*}", StringComparison.Ordinal)
                && !template[offset + p.i].StartsWith('{') && string.Equals(template[offset + p.i], p.x, StringComparison.OrdinalIgnoreCase));
            fits.Add((e, fixedHits));
        }
        if (fits.Count == 0) return [];
        var best = fits.Max(f => f.Fixed);
        // ASP.NET prefers a verb-specific endpoint over ANY at the same path.
        var kept = fits.Where(f => f.Fixed == best).Select(f => f.E).ToList();
        var withVerb = kept.Where(e => e.Verb != "ANY").Select(e => string.Join('/', e.Segments)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. kept.Where(e => !(e.Verb == "ANY" && withVerb.Contains(string.Join('/', e.Segments))))];
    }

    /// <summary>A fixed segment against a constrained parameter: <c>{id:int}</c> takes only digits, <c>{id:guid}</c> only a guid.</summary>
    static bool RouteConstraintsHold(string[] template, int offset, string[] segments)
    {
        for (var i = 0; i < segments.Length && offset + i < template.Length; i++)
        {
            var t = template[offset + i];
            var x = segments[i];
            if (!t.StartsWith('{') || x.Contains("{*}", StringComparison.Ordinal)) continue;
            var constraints = t.Trim('{', '}').TrimEnd('?').Split(':').Skip(1).ToList();
            if (constraints.Contains("int") || constraints.Contains("long")) { if (!x.All(char.IsAsciiDigit)) return false; }
            if (constraints.Contains("guid") && !Guid.TryParse(x, out _)) return false;
            if (constraints.Contains("bool") && !bool.TryParse(x, out _)) return false;
        }
        return true;
    }

    /// <summary>Path segments, the query cut at a <c>?</c> outside braces only, so <c>{id:int?}</c> stays whole (F4).</summary>
    static string[] Segments(string path) => StripQuery(path).Split('/', StringSplitOptions.RemoveEmptyEntries);

    static string StripQuery(string path)
    {
        var depth = 0;
        for (var i = 0; i < path.Length; i++)
            if (path[i] == '{') depth++;
            else if (path[i] == '}') depth--;
            else if (path[i] == '?' && depth == 0) return path[..i];
        return path;
    }

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

using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Graph;

/// <summary>
/// D32's answer key: a project's own OpenAPI file. Checks, without AI or history:
/// 1. endpoints: our extracted verb + path vs the file's operations;
/// 2. fields: the JSON field names we derive for each endpoint's response type vs the file's schema;
/// 3. frontend links: every frontend call we linked should land on an endpoint the file lists.
/// </summary>
static class OpenApiCheck
{
    public static JsonObject Run(CodeGraph graph, string openApiPath)
    {
        var doc = JsonNode.Parse(File.ReadAllText(openApiPath))!.AsObject();
        var operations = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, item) in doc["paths"]!.AsObject())
            foreach (var (verb, op) in item!.AsObject())
                if (verb is "get" or "post" or "put" or "delete" or "patch" or "head")
                    operations[Key(verb.ToUpperInvariant(), path)] = op!.AsObject();
        var schemas = doc["components"]?["schemas"]?.AsObject() ?? [];

        var endpoints = graph.AllNodes.Where(n => n.Kind == NodeKind.Endpoint && !n.Id.Contains("{*}", StringComparison.Ordinal))
            .DistinctBy(n => n.Id).ToList();
        var ours = endpoints.ToDictionary(n => Key(n.Name[..n.Name.IndexOf(' ')], n.Name[(n.Name.IndexOf(' ') + 1)..]), n => n, StringComparer.OrdinalIgnoreCase);
        var both = ours.Keys.Where(operations.ContainsKey).ToList();
        var missing = operations.Keys.Where(k => !ours.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        var extra = ours.Keys.Where(k => !operations.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();

        // Fields: our json-shape for the endpoint's response type vs the OpenAPI response schema.
        var routesTo = graph.AllEdges.Where(e => e.Kind == EdgeKind.RoutesTo).ToLookup(e => e.Source, e => e.Target);
        var types = graph.AllFacts.Where(f => f.Detector == "aspnetcore-routes" && f.Kind == "endpoint-types")
            .GroupBy(f => f.Owner).ToDictionary(g => g.Key, g => g.First()["response"], StringComparer.Ordinal);
        var shapes = graph.AllFacts.Where(f => f.Detector == "aspnetcore-routes" && f.Kind == "json-shape")
            .GroupBy(f => f.Owner).ToDictionary(g => g.Key,
                g => (JsonSerializer.Deserialize<List<Dictionary<string, string>>>(g.First()["props"]) ?? []).Select(p => p["json"]).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        int fieldsMatched = 0, fieldsTheirs = 0, fieldsOurs = 0, endpointsCompared = 0;
        var fieldMismatches = new JsonArray();
        foreach (var key in both)
        {
            var schema = ResponseSchema(operations[key], schemas);
            if (schema is null) continue;
            var theirs = schema.Select(p => p).ToHashSet(StringComparer.Ordinal);
            var target = routesTo[ours[key].Id].FirstOrDefault(t => types.ContainsKey(t));
            if (target is null || !shapes.TryGetValue(types[target], out var mine)) continue;
            endpointsCompared++;
            fieldsTheirs += theirs.Count;
            fieldsOurs += mine.Count;
            fieldsMatched += theirs.Count(mine.Contains);
            if (!theirs.SetEquals(mine) && fieldMismatches.Count < 10)
                fieldMismatches.Add($"{key}: only in OpenAPI [{string.Join(", ", theirs.Except(mine).Take(4))}], only ours [{string.Join(", ", mine.Except(theirs).Take(4))}]");
        }

        // Frontend links: does every endpoint a frontend call reaches exist in the OpenAPI file?
        var linked = graph.AllEdges.Where(e => e.Kind == EdgeKind.CallsHttp && e.Detector == "frontend-http").ToList();
        var nodes = graph.AllNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var linkedReal = linked.Count(e => nodes.TryGetValue(e.Target, out var n)
            && operations.ContainsKey(Key(n.Name[..n.Name.IndexOf(' ')], n.Name[(n.Name.IndexOf(' ') + 1)..])));
        var callsWithLinks = linked.GroupBy(e => e.Source).ToList();
        var unmatched = graph.AllBlindSpots.Count(b => b.Detector == "frontend-http" && b.Reason.Contains("matched no backend endpoint", StringComparison.Ordinal));

        return new JsonObject
        {
            ["openApiOperations"] = operations.Count,
            ["ourEndpoints"] = ours.Count,
            ["endpoints"] = new JsonObject
            {
                ["recall"] = Round(both.Count, operations.Count), ["precision"] = Round(both.Count, ours.Count),
                ["missingSample"] = new JsonArray([.. missing.Take(10).Select(m => (JsonNode)m)]),
                ["extraSample"] = new JsonArray([.. extra.Take(10).Select(m => (JsonNode)m)]),
            },
            ["fields"] = new JsonObject
            {
                ["endpointsCompared"] = endpointsCompared,
                ["recall"] = Round(fieldsMatched, fieldsTheirs), ["precision"] = Round(fieldsMatched, fieldsOurs),
                ["mismatchSample"] = fieldMismatches,
            },
            ["frontendLinks"] = new JsonObject
            {
                ["links"] = linked.Count,
                ["toRealEndpoints"] = Round(linkedReal, linked.Count),
                ["callersLinked"] = callsWithLinks.Count,
                ["callersWithOneEndpoint"] = callsWithLinks.Count(g => g.Select(e => e.Target).Distinct().Count() == 1),
                ["unmatchedCalls"] = unmatched,
            },
            ["note"] = "Paths compared case-insensitively with route constraints removed ({id:int} → {id}).",
        };
    }

    static double Round(int a, int b) => b == 0 ? 0 : Math.Round((double)a / b, 3);

    /// <summary>"GET /api/Series/{seriesId:int}" and "get /api/series/{seriesId}" give the same key.</summary>
    static string Key(string verb, string path)
    {
        var segments = path.Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.StartsWith('{') ? "{" + s.Trim('{', '}').Split(':')[0].TrimEnd('?') + "}" : s.ToLowerInvariant());
        return $"{verb.ToUpperInvariant()} /{string.Join('/', segments)}";
    }

    /// <summary>The property names of an operation's 200 JSON response (following $ref and array items).</summary>
    static List<string>? ResponseSchema(JsonObject op, JsonObject schemas)
    {
        var schema = op["responses"]?["200"]?["content"]?["application/json"]?["schema"];
        for (var depth = 0; schema is not null && depth < 5; depth++)
        {
            if (schema["$ref"] is { } r) { schema = schemas[((string)r!).Split('/').Last()]; continue; }
            if ((string?)schema["type"] == "array") { schema = schema["items"]; continue; }
            if (schema["properties"] is JsonObject props) return [.. props.Select(p => p.Key)];
            if (schema["allOf"] is JsonArray all) { schema = all.FirstOrDefault(); continue; }
            return null;
        }
        return null;
    }
}

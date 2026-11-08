using System.Text.Json;
using System.Text.Json.Nodes;

namespace Stangent.Graph;

/// <summary>What the cross-check compared and how many disagreements it found (D164).</summary>
public sealed record CrossCheckStats(int Sources, int Disagreements);

/// <summary>
/// D164: the framework's own answer as the key. Every build compares the graph's endpoints with each OpenAPI file in the
/// repo (an operation no endpoint fits; an endpoint of the file's own project it doesn't list), and the tables our rules
/// find with EF's snapshot. Every disagreement is
/// written to <c>.stangent/graph-crosscheck.json</c>; nothing here changes the graph, since a stale file would mislead it.
/// </summary>
public static class CrossCheck
{
    public static string PathFor(string root) => Path.Combine(root, ".stangent", "graph-crosscheck.json");

    static readonly HashSet<string> Skip = ["node_modules", "bin", "obj", ".git", ".stangent", "dist", "artifacts"];
    static readonly string[] Verbs = ["get", "post", "put", "delete", "patch", "head", "options"];

    public static CrossCheckStats? Run(string root, CodeGraph graph)
    {
        var report = new JsonObject { ["commit"] = graph.Commit };
        var sources = 0;
        var disagreements = 0;

        var openapi = new JsonArray();
        var endpoints = graph.AllNodes.Where(n => n.Kind == NodeKind.Endpoint).ToList();
        foreach (var file in OpenApiFiles(root))
        {
            if (Read(file) is not { } doc || doc["paths"] is not JsonObject paths) continue;
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            var scope = ProjectOf(root, file);
            var inScope = endpoints.Where(e => scope is null || e.Project == scope).ToList();
            var basePath = (string?)doc["basePath"] ?? "";
            var documented = new HashSet<(string Verb, string Path)>();
            foreach (var (route, item) in paths)
                if (item is JsonObject ops)
                    foreach (var verb in Verbs.Where(ops.ContainsKey))
                        documented.Add((verb.ToUpperInvariant(), Normalize(basePath.TrimEnd('/') + route)));
            static (string Verb, string Path, string Name) Shape(Node e) => (e.Name[..e.Name.IndexOf(' ')], Normalize(e.Name[(e.Name.IndexOf(' ') + 1)..]), e.Name);
            var mapped = inScope.Select(Shape).ToList();
            // A host's OpenAPI documents every project it serves (Sonarr's lists Sonarr.Http's login and ping): an operation is
            // missing only when no endpoint anywhere fits it; an endpoint is undocumented only when it's the file's own project's.
            var everywhere = endpoints.Select(Shape).ToList();
            var notInOpenapi = mapped.Where(m => !documented.Any(d => d.Path == m.Path && (m.Verb == "ANY" || d.Verb == m.Verb))).Select(m => m.Name).Distinct().Order(StringComparer.Ordinal).ToList();
            var missing = documented.Where(d => !everywhere.Any(m => m.Path == d.Path && (m.Verb == "ANY" || m.Verb == d.Verb))).Select(d => $"{d.Verb} {d.Path}").Order(StringComparer.Ordinal).ToList();
            sources++;
            disagreements += notInOpenapi.Count + missing.Count;
            openapi.Add(new JsonObject
            {
                ["file"] = rel, ["scope"] = scope ?? "(whole repo)", ["operations"] = documented.Count, ["endpoints"] = inScope.Count,
                ["missingFromGraph"] = new JsonArray([.. missing.Select(m => (JsonNode)m)]),
                ["notInOpenapi"] = new JsonArray([.. notInOpenapi.Select(m => (JsonNode)m)]),
            });
        }
        report["openapi"] = openapi;

        var ef = new JsonArray();
        var entities = graph.AllFacts.Where(f => f.Detector == "entity-tables" && f.Kind == "entity").ToList();
        foreach (var assembly in entities.Where(f => f["source"] == "snapshot").Select(f => f["assembly"]).Distinct().Order(StringComparer.Ordinal))
        {
            var snapshot = entities.Where(f => f["assembly"] == assembly && f["source"] == "snapshot").ToDictionary(f => f["entity"], f => f["table"], StringComparer.Ordinal);
            var byRules = entities.Where(f => f["assembly"] == assembly && f["source"] != "snapshot").Select(f => f["entity"]).ToHashSet(StringComparer.Ordinal);
            var rulesOnly = byRules.Where(e => !snapshot.ContainsKey(e)).Order(StringComparer.Ordinal).ToList();
            var snapshotOnly = snapshot.Keys.Where(e => !byRules.Contains(e)).Order(StringComparer.Ordinal).Select(e => $"{e} ({snapshot[e]})").ToList();
            sources++;
            disagreements += rulesOnly.Count + snapshotOnly.Count;
            ef.Add(new JsonObject
            {
                ["assembly"] = assembly, ["tables"] = snapshot.Count,
                ["foundByRulesOnly"] = new JsonArray([.. rulesOnly.Select(e => (JsonNode)e)]),
                ["inSnapshotOnly"] = new JsonArray([.. snapshotOnly.Select(e => (JsonNode)e)]),
            });
        }
        report["efSnapshot"] = ef;

        var path = PathFor(root);
        if (sources == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return null;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return new CrossCheckStats(sources, disagreements);
    }

    /// <summary>A path compared by shape: lower case, no trailing slash, every <c>{…}</c> the same.</summary>
    public static string Normalize(string path)
    {
        // Split at slashes outside braces: {**path:regex(^(?!api/).*)} is one segment.
        var segments = new List<string>();
        var depth = 0;
        var current = new System.Text.StringBuilder();
        foreach (var c in path)
        {
            if (c == '{') depth++;
            else if (c == '}') depth--;
            if (c == '/' && depth == 0) { if (current.Length > 0) segments.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        if (current.Length > 0) segments.Add(current.ToString());
        return "/" + string.Join("/", segments.Select(s => s.StartsWith('{') && s.EndsWith('}') ? "{}" : s.ToLowerInvariant()));
    }

    static IEnumerable<string> OpenApiFiles(string root)
    {
        var stack = new Stack<string>([root]);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir); } catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
            foreach (var entry in entries.Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry)) { if (!Skip.Contains(name)) stack.Push(entry); continue; }
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && (name.Contains("openapi", StringComparison.OrdinalIgnoreCase) || name.Contains("swagger", StringComparison.OrdinalIgnoreCase)))
                    yield return entry;
            }
        }
    }

    static JsonObject? Read(string file)
    {
        try
        {
            if (new FileInfo(file).Length > 50_000_000) return null;
            return JsonNode.Parse(File.ReadAllText(file)) is JsonObject o && (o.ContainsKey("openapi") || o.ContainsKey("swagger")) ? o : null;
        }
        catch (Exception e) when (e is JsonException or IOException) { return null; }
    }

    /// <summary>The project whose folder holds the file (its nearest .csproj up to the repo root), or null for the whole repo.</summary>
    static string? ProjectOf(string root, string file)
    {
        var full = Path.GetFullPath(root);
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(file)); dir is not null && dir.Length > full.Length; dir = Path.GetDirectoryName(dir))
            if (Directory.EnumerateFiles(dir, "*.csproj").Order(StringComparer.Ordinal).FirstOrDefault() is { } csproj)
                return "project:" + Path.GetRelativePath(root, csproj).Replace('\\', '/');
        return null;
    }
}

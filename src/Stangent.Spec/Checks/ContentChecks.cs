using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Checks;

/// <summary>
/// T.6: broken links and duplicate ids in what's generated. Code only. Reads the page files, <c>specs/trace.yaml</c>, the imported
/// documents and the requirements; writes nothing. Quotes and decisions are <c>trace check</c>'s job; this only asks whether
/// what's pointed at is there, and whether an id means one thing.
/// </summary>
public static partial class ContentChecks
{
    [GeneratedRegex(@"(?<!\!)\[[^\]\n]*\]\(<?([^)\s>#]+)[^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.-]*:")]
    private static partial Regex HasScheme();

    const string TraceFileName = "specs/trace.yaml";

    /// <param name="pagePaths">Every page the graph calls for, by its path under the reference folder, when there's a graph. A link to one not written yet isn't broken: pages are made as needed (I.1), so it's counted apart.</param>
    public static JsonObject Run(string root, IReadOnlySet<string>? pagePaths = null)
    {
        var notWritten = new SortedSet<string>(StringComparer.Ordinal);
        var problems = new List<JsonObject>();
        void Problem(string kind, string where, string detail) => problems.Add(new JsonObject { ["kind"] = kind, ["where"] = where, ["detail"] = detail });

        var index = SpecIndex.Load(root);
        var dir = new SpecPaths(root).ReferenceDir;
        var seenPages = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                if (Path.GetFileName(file) == "failed.json" || PageRun.LoadJson(file) is not JsonObject page || page["sections"] is null) continue;
                var (id, here) = ((string)page["id"]!, Path.GetRelativePath(root, file));
                if (seenPages.TryGetValue(id, out var first)) Problem("duplicate-page-id", here, $"page id `{id}` is also in {first}");
                else seenPages[id] = here;
                foreach (var repeat in PageChecks.Statements(page).GroupBy(s => s.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
                    Problem("duplicate-statement-id", here, $"statement id `{repeat.Key}` appears {repeat.Count()} times");
            }
            foreach (var file in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                foreach (var target in MarkdownLink().Matches(File.ReadAllText(file)).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal))
                {
                    if (HasScheme().IsMatch(target) || target.StartsWith('/')) continue;
                    var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, Uri.UnescapeDataString(target)));
                    if (File.Exists(full) || Directory.Exists(full)) continue;
                    var underReference = Path.ChangeExtension(Path.GetRelativePath(dir, full), null).Replace(Path.DirectorySeparatorChar, '/');
                    if (pagePaths?.Contains(underReference) == true) notWritten.Add(underReference);
                    else Problem("broken-link", Path.GetRelativePath(root, file), $"links to `{target}`, which isn't there");
                }
        }

        foreach (var doc in FsdImport.Documents(root))
            foreach (var repeat in doc["sections"]!.AsArray().OfType<JsonObject>().GroupBy(s => (string)s["id"]!, StringComparer.Ordinal).Where(g => g.Count() > 1))
                Problem("duplicate-section-id", (string)doc["id"]!, $"section id `{repeat.Key}` appears {repeat.Count()} times");

        var trace = TraceFile.Load(root);
        var entries = trace["entries"]!.AsArray().OfType<JsonObject>().ToList();
        foreach (var repeat in entries.GroupBy(e => (string?)e["id"] ?? "", StringComparer.Ordinal).Where(g => g.Key.Length > 0 && g.Count() > 1))
            Problem("duplicate-link-id", TraceFileName, $"link id `{repeat.Key}` appears {repeat.Count()} times");
        foreach (var e in entries)
        {
            var id = (string?)e["id"] ?? "(no id)";
            // A removed section keeps its links under a marked id, on purpose (2.2), and a retired statement is `trace check`'s to list.
            if ((string?)e["fsd"]?["doc"] is { } docId && (string?)e["fsd"]?["section"] is { } section && !section.StartsWith("(removed", StringComparison.Ordinal) && !index.Sections.ContainsKey((docId, section)))
                Problem("broken-link", TraceFileName, $"link `{id}` points at section `{section}` of `{docId}`, which isn't there");
            if ((string?)e["page"]?["id"] is not { } pageId) continue;
            if (!index.Pages.ContainsKey(pageId)) Problem("broken-link", TraceFileName, $"link `{id}` points at page `{pageId}`, which isn't recorded");
            else if ((string?)e["page"]?["statement"] is { Length: > 0 } statement && index.Statement(pageId, statement) is null && !index.Retired(root, pageId, statement))
                Problem("broken-link", TraceFileName, $"link `{id}` points at {statement} of `{pageId}`, which that page doesn't have");
        }

        var requirementIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var docId in index.Sections.Keys.Select(k => k.Doc).Distinct(StringComparer.Ordinal))
            foreach (var feature in RequirementFile.Features(root, docId))
                foreach (var id in Ids(feature))
                {
                    var here = (string)feature["id"]!;
                    if (requirementIds.TryGetValue(id, out var first)) Problem("duplicate-requirement-id", here, $"id `{id}` is also in {first}");
                    else requirementIds[id] = here;
                }

        return new JsonObject
        {
            ["ok"] = problems.Count == 0,
            ["counts"] = new JsonObject { ["pages"] = seenPages.Count, ["links"] = entries.Count, ["requirementIds"] = requirementIds.Count, ["problems"] = problems.Count, ["linksToPagesNotWritten"] = notWritten.Count },
            ["problems"] = new JsonArray([.. problems.Select(p => (JsonNode)p)]),
        };
    }

    /// <summary>Every <c>id</c> nested in a feature, not its own. A reference to an id elsewhere is stored under another key.</summary>
    static IEnumerable<string> Ids(JsonObject feature)
    {
        IEnumerable<string> Walk(JsonNode? node) => node switch
        {
            JsonObject o => (o["id"] is JsonValue v && v.TryGetValue<string>(out var id) ? [id] : Array.Empty<string>()).Concat(o.Where(p => p.Key != "id").SelectMany(p => Walk(p.Value))),
            JsonArray a => a.SelectMany(Walk),
            _ => [],
        };
        return feature.Where(p => p.Key != "id").SelectMany(p => Walk(p.Value));
    }
}

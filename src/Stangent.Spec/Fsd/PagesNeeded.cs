using System.Text.Json.Nodes;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>I.1 (D131): the pages not written yet that something needs. Code only: it lists, and a driver makes them.</summary>
public static class PagesNeeded
{
    public static JsonObject ForDocument(PageSet set, SpecIndex index, string root, string doc, string? chapter)
    {
        var document = FsdOutline.Load(root, doc);
        var sections = (chapter is null ? document["sections"]!.AsArray().OfType<JsonObject>().ToList() : FsdOutline.Chapter(document, chapter))
            .Where(s => ((string)s["text"]!).Trim().Length > 0);
        var forSections = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var s in sections)
        {
            var parent = (string?)s["parent"] is { } p ? index.Sections.GetValueOrDefault((doc, p)) : null;
            foreach (var candidate in Candidates.ForSection(s, set, index.Pages, parent).Where(c => !index.Pages.ContainsKey(c.Page)))
                (forSections.TryGetValue(candidate.Page, out var list) ? list : forSections[candidate.Page] = []).Add((string)s["id"]!);
        }
        var result = Rows(set, forSections.Select(k => (k.Key, (IReadOnlyList<string>?)k.Value)));
        result["doc"] = doc;
        return result;
    }

    public static JsonObject Listed(PageSet set, IEnumerable<string> pages) => Rows(set, pages.Distinct(StringComparer.Ordinal).Select(p => (p, (IReadOnlyList<string>?)null)));

    static JsonObject Rows(PageSet set, IEnumerable<(string Page, IReadOnlyList<string>? Sections)> rows)
    {
        var list = new JsonArray();
        foreach (var (id, sections) in rows)
        {
            // A page the graph no longer calls for can't be made.
            if (set.Find(id) is not { } page) continue;
            var o = new JsonObject { ["id"] = page.Id, ["kind"] = page.Kind, ["title"] = page.Title, ["chars"] = Bundles.Build(set, page, null).Chars };
            if (sections is not null) o["sections"] = new JsonArray([.. sections.Select(s => (JsonNode)s)]);
            list.Add(o);
        }
        return new JsonObject { ["pages"] = list, ["outOf"] = set.Pages.Count };
    }
}

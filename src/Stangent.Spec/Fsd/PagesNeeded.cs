using System.Text.Json.Nodes;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>I.1 (D131): the pages not written yet that a ticket's bundle needs. Code only: it lists, and a driver makes them. The FSD side went with the link route (D342).</summary>
public static class PagesNeeded
{
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

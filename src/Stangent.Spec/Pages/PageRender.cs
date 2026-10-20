using System.Text;
using System.Text.Json.Nodes;

namespace Stangent.Spec.Pages;

/// <summary>A recorded page as Markdown (D52): rendered by code from the page's JSON, never written by the AI.</summary>
public static class PageRender
{
    static readonly (string Key, string Title)[] Titles =
        [("shows", "Shows"), ("actions", "Actions"), ("rules", "Rules"), ("sideEffects", "Side effects")];

    /// <param name="pathOf">The page path for an id that has a page of its own (endpoints, shared components), or null.</param>
    public static string Markdown(JsonObject page, Func<string, string?> pathOf)
    {
        var md = new StringBuilder();
        var self = pathOf((string)page["id"]!) ?? "";
        md.Append($"# {(string)page["title"]!}\n\n");
        md.Append($"`{(string)page["id"]!}` · {(string)page["kind"]!} · {(string)page["group"]!}\n\n");
        md.Append($"From the code: {(string)page["codeTitle"]!}\n\n");
        md.Append("> Generated from the code by AI and checked by code: every statement cites the symbols it comes from, all inside this page's slice. ");
        md.Append("Generated is not verified: nobody has approved it yet.\n");

        var cited = new List<string>();
        foreach (var (key, title) in Titles)
        {
            var list = page["sections"]![key]!.AsArray();
            md.Append($"\n## {title}\n\n");
            if (list.Count == 0) { md.Append("Nothing found in the code.\n"); continue; }
            foreach (var s in list.OfType<JsonObject>())
            {
                var refs = s["cites"]!.AsArray().Select(c => (string)c!).Select(c =>
                {
                    if (!cited.Contains(c)) cited.Add(c);
                    return $"[{cited.IndexOf(c) + 1}]";
                });
                md.Append($"- **{(string)s["id"]!}** {(string)s["text"]!} {string.Join("", refs)}");
                if (s["endpoints"] is JsonArray eps && eps.Count > 0)
                    md.Append(" → " + string.Join(", ", eps.Select(e => Link((string)e!, pathOf, self))));
                md.Append('\n');
            }
        }

        var unknowns = page["unknowns"]!.AsArray();
        var cutOff = (int?)page["generated"]?["slice"]?["cutOff"] ?? 0;
        md.Append("\n## Couldn't see\n\n");
        if (unknowns.Count == 0 && cutOff == 0) md.Append("Nothing: every line in this page's slice was resolved.\n");
        foreach (var u in unknowns.OfType<JsonObject>()) md.Append($"- ❓ `{(string)u["file"]!}:{(int)u["line"]!}` {(string)u["reason"]!}\n");
        if (cutOff > 0) md.Append($"- the depth limit left out {cutOff} more point(s) one line further on\n");

        var findings = page["verification"]!["findings"]!.AsArray();
        if (findings.Count > 0)
        {
            md.Append("\n## Reviewer's findings\n\nA separate agent checked this page against its slice (D62). Each finding was fixed or rejected with a reason.\n\n");
            foreach (var f in findings.OfType<JsonObject>())
                md.Append($"- {(string)f["id"]!} on {(string)f["statement"]!} ({(string)f["kind"]!}): {(string)f["explanation"]!} **{(string)f["outcome"]!}**: {(string)f["reason"]!}\n");
        }

        md.Append("\n## Cited symbols\n\n");
        for (var i = 0; i < cited.Count; i++) md.Append($"{i + 1}. {Link(cited[i], pathOf, self)}\n");

        var g = page["generated"]!;
        md.Append($"\n<sub>commit {Short((string)g["commit"]!)} · inputs {Short((string)g["bundleHash"]!)} · slice of {(int)g["slice"]!["points"]!} points, ");
        md.Append($"{(int)g["slice"]!["depth"]!} lines deep · written by {(string?)g["writer"]!["model"] ?? "?"}, checked by {(string?)g["verifier"]!["model"] ?? "?"}</sub>\n");
        return md.ToString();
    }

    static string Link(string id, Func<string, string?> pathOf, string self)
    {
        var path = pathOf(id);
        if (path is null || path == self) return $"`{id}`";
        var depth = self.Count(c => c == '/');
        return $"[`{id}`]({string.Concat(Enumerable.Repeat("../", depth))}{path}.md)";
    }

    static string Short(string s) => s.Length > 7 ? s[..7] : s;
}

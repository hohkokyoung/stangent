using System.Text;
using System.Text.Json.Nodes;

namespace Stangent.Spec.Pages;

/// <summary>A recorded page as Markdown (D52): rendered by code from the page's JSON, never written by the AI.</summary>
public static class PageRender
{
    static readonly (string Key, string Title)[] Titles =
        [("shows", "Shows"), ("actions", "Actions"), ("rules", "Rules"), ("sideEffects", "Side effects")];

    /// <param name="pathOf">The page path for an id that has a page of its own (endpoints, shared components), or null.</param>
    /// <param name="lines">The page's line citations (<see cref="PageLines"/>), or null before it has any.</param>
    /// <param name="marks">A person's corrections on this page (T.1), so a reader can see which words are a person's.</param>
    public static string Markdown(JsonObject page, Func<string, string?> pathOf, JsonObject? lines = null, (IReadOnlyDictionary<string, string> Corrected, IReadOnlyList<JsonObject> Removed)? marks = null)
    {
        var md = new StringBuilder();
        var self = pathOf((string)page["id"]!) ?? "";
        var up = string.Concat(Enumerable.Repeat("../", self.Count(c => c == '/'))) + "index.md";
        md.Append($"[All pages]({up})\n\n");
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
                if (marks?.Corrected.GetValueOrDefault((string)s["id"]!) is { } correction) md.Append($" *(corrected by a person, {correction})*");
                md.Append('\n');
            }
        }
        if (marks?.Removed is { Count: > 0 } removed)
        {
            md.Append("\n## Removed by a person\n\n");
            foreach (var c in removed) md.Append($"- {(string)c["id"]!}: \"{(string)c["was"]!}\", because {(string)c["why"]!}\n");
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
        var wholePage = (string?)lines?["note"];
        if (wholePage is not null) md.Append($"No lines are cited: {wholePage}.\n\n");
        else if ((string?)lines?["commit"] is { } at) md.Append($"Lines are at commit {Short(at)}, filled by code from the graph.\n\n");
        for (var i = 0; i < cited.Count; i++)
        {
            md.Append($"{i + 1}. {Link(cited[i], pathOf, self)}");
            var symbol = lines?["symbols"]?[cited[i]] as JsonObject;
            if (PageLines.Cite(symbol) is { } cite) md.Append($" · `{cite}`");
            if ((string?)symbol?["note"] is { } note) md.Append($" · {note}");
            if (wholePage is null && (string?)symbol?["why"] is { } why) md.Append($" · no lines: {why}");
            md.Append('\n');
        }

        var g = page["generated"]!;
        md.Append($"\n<sub>commit {Short((string)g["commit"]!)} · inputs {Short((string)g["bundleHash"]!)} · slice of {(int)g["slice"]!["points"]!} points, ");
        md.Append($"{(int)g["slice"]!["depth"]!} lines deep · written by {(string?)g["writer"]!["model"] ?? "?"}, checked by {(string?)g["verifier"]!["model"] ?? "?"}</sub>\n");
        md.Append($"\n---\n\n[All pages]({up}) · Generated from the code, so don't edit this file. If it's wrong, `stangent spec correct` records a correction.\n");
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

/// <summary>T.7 (D131): <c>specs/reference/index.md</c>, every written page by kind and group. From the pages' JSON by code, read by people and nothing else.</summary>
public static class PageNavigation
{
    public static string FileIn(SpecPaths paths) => Path.Combine(paths.ReferenceDir, "index.md");

    /// <summary>Rewrites the index. Side-by-side runs take turns, and the file is whole at any moment. Returns how many pages it lists.</summary>
    public static int Write(SpecPaths paths)
    {
        using var _ = PageRun.Lock(paths);
        var (text, count) = Render(paths);
        Quotes.WriteAtomic(FileIn(paths), text);
        return count;
    }

    static (string Text, int Count) Render(SpecPaths paths)
    {
        var pages = new List<(string Kind, string Group, string Title, string Id, string Path)>();
        if (Directory.Exists(paths.ReferenceDir))
            foreach (var file in Directory.EnumerateFiles(paths.ReferenceDir, "*.json", SearchOption.AllDirectories))
                if (file != paths.FailedFile && PageRun.LoadJson(file) is JsonObject p && p["sections"] is not null && (string?)p["id"] is { } id)
                    pages.Add(((string)p["kind"]!, (string?)p["group"] ?? "", (string?)p["title"] ?? id, id, Path.GetRelativePath(paths.ReferenceDir, file)[..^".json".Length].Replace('\\', '/') + ".md"));
        var md = new StringBuilder("# Reference pages\n\nGenerated by code from the pages. Don't edit this file: `stangent spec nav` writes it again. `stangent spec coverage` says what isn't written.\n");
        foreach (var (kind, title) in new[] { (PageKind.Screen, "Screens"), (PageKind.Component, "Shared components"), (PageKind.Backend, "Backend") })
        {
            var ofKind = pages.Where(p => p.Kind == kind).ToList();
            if (ofKind.Count == 0) continue;
            md.Append($"\n## {title} ({ofKind.Count})\n");
            foreach (var group in ofKind.GroupBy(p => p.Group).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                if (group.Key.Length > 0) md.Append($"\n### {group.Key}\n\n");
                else md.Append('\n');
                foreach (var p in group.OrderBy(p => p.Id, StringComparer.Ordinal))
                    md.Append($"- [{p.Title.Replace("[", "\\[").Replace("]", "\\]")}]({p.Path})\n");
            }
        }
        return (md.ToString(), pages.Count);
    }
}

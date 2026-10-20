using System.Text;
using System.Text.Json.Nodes;
using Stangent.Graph.Impact;

namespace Stangent.Spec.Pages;

/// <summary>
/// Which pages need regenerating (D55): code only. A page is stale when its input hash today differs from the one it
/// was recorded with; new when the graph calls for a page that has none; removed when a recorded page no longer
/// matches anything in the graph (a renamed route, a deleted endpoint).
/// </summary>
public static class Staleness
{
    public static JsonObject Report(PageSet set, SpecPaths paths, string? diff)
    {
        var changedFiles = diff is null ? null : DiffMapper.ChangedLines(paths.Root, diff).Select(c => c.File).ToHashSet(StringComparer.Ordinal);
        var stale = new JsonArray();
        var fresh = new JsonArray();
        var current = 0;
        foreach (var page in set.Pages)
        {
            var recorded = PageRun.LoadJson(paths.PageJson(page)) as JsonObject;
            if (recorded is null) { fresh.Add(Item(page, null)); continue; }
            var hash = Bundles.Build(set, page, null).InputHash;
            if ((string?)recorded["generated"]?["bundleHash"] == hash) { current++; continue; }
            var item = Item(page, recorded);
            if (changedFiles is not null)
                item["changedFiles"] = new JsonArray([.. set.Slices[page.Id].Nodes.Select(n => n.Node.File).OfType<string>()
                    .Where(changedFiles.Contains).Distinct().Order(StringComparer.Ordinal).Select(f => (JsonNode)f)]);
            stale.Add(item);
        }

        var known = set.Pages.Select(p => paths.PageJson(p)).ToHashSet(StringComparer.Ordinal);
        var removed = new JsonArray();
        if (Directory.Exists(paths.ReferenceDir))
            foreach (var file in Directory.EnumerateFiles(paths.ReferenceDir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                if (file == paths.FailedFile || known.Contains(file)) continue;
                if (PageRun.LoadJson(file) is JsonObject o && o["schemaVersion"] is not null)
                    removed.Add(new JsonObject { ["id"] = (string?)o["id"], ["path"] = paths.Relative(file) });
            }

        return new JsonObject
        {
            ["commit"] = set.Commit,
            ["diff"] = diff,
            ["counts"] = new JsonObject { ["pages"] = set.Pages.Count, ["current"] = current, ["stale"] = stale.Count, ["new"] = fresh.Count, ["removed"] = removed.Count },
            ["stale"] = stale, ["new"] = fresh, ["removed"] = removed,
        };
    }

    static JsonObject Item(PageDef page, JsonObject? recorded)
    {
        var o = new JsonObject { ["id"] = page.Id, ["kind"] = page.Kind, ["title"] = (string?)recorded?["title"] ?? page.Title, ["path"] = page.Path };
        if (recorded is not null) o["recordedAt"] = (string?)recorded["generated"]?["commit"];
        return o;
    }

    /// <summary>The PR comment (D55 step 1): what this change makes stale. FSD sections linked to those pages are added by 2b.</summary>
    public static string Markdown(JsonObject report)
    {
        var md = new StringBuilder();
        var c = report["counts"]!;
        md.Append($"**Reference pages:** {c["stale"]} stale, {c["new"]} new, {c["removed"]} removed, {c["current"]} current");
        md.Append(" (code only, no AI; stale pages are regenerated when this merges)\n\n");
        void List(string key, string title)
        {
            var items = report[key]!.AsArray();
            if (items.Count == 0) return;
            md.Append($"**{title}**\n\n");
            foreach (var i in items.Take(50))
            {
                md.Append($"- `{(string)i!["id"]!}` {(string)i["title"]!}");
                if (i["changedFiles"] is JsonArray { Count: > 0 } files)
                    md.Append(" (changed: " + string.Join(", ", files.Take(5).Select(f => $"`{f}`")) + (files.Count > 5 ? $" and {files.Count - 5} more" : "") + ")");
                if (i["fsdSections"] is JsonArray { Count: > 0 } sections)
                    md.Append(" · FSD " + string.Join(", ", sections.Select(s => $"§{s}")));
                md.Append('\n');
            }
            if (items.Count > 50) md.Append($"- and {items.Count - 50} more\n");
            md.Append('\n');
        }
        List("stale", "Stale");
        List("new", "New");
        List("removed", "Removed");
        return md.ToString();
    }
}

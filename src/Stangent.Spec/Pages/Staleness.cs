using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Graph.Impact;

namespace Stangent.Spec.Pages;

/// <summary>
/// Which pages need regenerating (D55): code only. A page is stale when its input hash today differs from the one it
/// was recorded with; new when the graph calls for a page that has none; removed when a recorded page no longer
/// matches anything in the graph (a renamed route, a deleted endpoint). A page a person marked <c>page-wrong</c> (D119) is stale
/// too, until it's regenerated: that's the one case where its inputs are the same and it still has to run.
/// </summary>
public static class Staleness
{
    /// <summary>T.4: whether a recorded page's inputs have moved on from the code, for wherever a page is read. Not recorded isn't behind: it has nothing to be behind with.</summary>
    public static bool Behind(PageSet set, SpecPaths paths, PageDef page) =>
        PageRun.LoadJson(paths.PageJson(page)) is JsonObject recorded && Bundles.Build(set, page, null).InputHash is var hash
        && (string?)recorded["generated"]?["bundleHash"] != hash && !PageMoves.OnlyMoved(set, paths, page, recorded, hash);

    /// <summary>The warning itself, in one form everywhere.</summary>
    public static string BehindWarning(string id, string? writtenAt, string? codeAt) =>
        $"Behind the code: page `{id}` was written at {Short(writtenAt)} and what it describes has changed since (code at {Short(codeAt)}). Treat it with care; `stangent spec stale` lists these.";

    static string Short(string? commit) => string.IsNullOrEmpty(commit) ? "an unknown commit" : commit[..Math.Min(commit.Length, 10)];

    public static JsonObject Report(PageSet set, SpecPaths paths, string? diff)
    {
        var changedFiles = diff is null ? null : DiffMapper.ChangedLines(paths.Root, diff).Select(c => c.File).ToHashSet(StringComparer.Ordinal);
        // The FSD sections linked to each page (D55 step 1: the PR lists them with the stale pages).
        var entries = Entries(paths.Root, out var traceProblem);
        var linked = entries
            .Where(e => e["fsd"] is JsonObject && (string?)e["page"]?["id"] is not null)
            .GroupBy(e => (string)e["page"]!["id"]!)
            .ToDictionary(g => g.Key, g => g.Select(e => (string)e["fsd"]!["section"]!).Distinct()
                .OrderBy(Trace.TraceFile.SectionKey, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var wrong = ReportedWrong(entries);
        var stale = new JsonArray();
        var fresh = new JsonArray();
        var moved = new JsonArray();
        var current = 0;
        foreach (var page in set.Pages)
        {
            var recorded = PageRun.LoadJson(paths.PageJson(page)) as JsonObject;
            if (recorded is null) { fresh.Add(Item(page, null)); continue; }
            var hash = Bundles.Build(set, page, null).InputHash;
            var reported = wrong.GetValueOrDefault(page.Id);
            if (reported is null && (string?)recorded["generated"]?["bundleHash"] == hash) { current++; continue; }
            if (reported is null && PageMoves.OnlyMoved(set, paths, page, recorded, hash)) { moved.Add(Item(page, recorded)); continue; }
            var item = Item(page, recorded);
            if (reported is not null) item["reportedWrong"] = new JsonArray([.. reported.Select(e => (JsonNode)new JsonObject
            {
                ["entry"] = (string)e["id"]!, ["finding"] = (string?)e["finding"], ["statement"] = (string?)e["page"]?["statement"], ["by"] = (string?)e["decided_by"],
            })]);
            if (linked.TryGetValue(page.Id, out var sections))
                item["fsdSections"] = new JsonArray([.. sections.Select(x => (JsonNode)x)]);
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

        var result = new JsonObject
        {
            ["commit"] = set.Commit,
            ["diff"] = diff,
            ["counts"] = new JsonObject { ["pages"] = set.Pages.Count, ["current"] = current, ["stale"] = stale.Count, ["new"] = fresh.Count, ["removed"] = removed.Count },
            ["stale"] = stale, ["new"] = fresh, ["removed"] = removed,
        };
        if (traceProblem is not null) result["traceProblem"] = traceProblem;
        // Only with follow-moves on, so the report stays as it was with it off.
        if (set.Settings.FollowMoves) result["moved"] = moved;
        return result;
    }

    /// <summary>The entries of <c>specs/trace.yaml</c>, for the page steps. Pages don't depend on the links, so a broken file is said, in <paramref name="problem"/>, and never stops one.</summary>
    static List<JsonObject> Entries(string root, out string? problem)
    {
        problem = null;
        try
        {
            var entries = Trace.TraceFile.Load(root)["entries"]!.AsArray().OfType<JsonObject>().ToList();
            // Reading what the page steps use, here, so a wrong shape is seen here too.
            foreach (var e in entries) _ = ((string?)e["decision"], (string?)e["page"]?["id"], (string?)e["fsd"]?["section"]);
            return entries;
        }
        catch (Exception e) when (e is StangentException or InvalidOperationException or NullReferenceException or InvalidCastException or ArgumentException or FormatException)
        {
            problem = $"specs/trace.yaml can't be read, so page-wrong marks and linked sections were left out: {e.Message} Run `stangent trace check`.";
            return [];
        }
    }

    /// <summary>The entries marked <c>page-wrong</c>, by page (D119). A broken trace file marks nothing, and says so in <paramref name="problem"/>.</summary>
    public static Dictionary<string, List<JsonObject>> ReportedWrong(string root, out string? problem) => ReportedWrong(Entries(root, out problem));

    static Dictionary<string, List<JsonObject>> ReportedWrong(List<JsonObject> entries) => entries
        .Where(e => (string?)e["decision"] == "page-wrong" && (string?)e["page"]?["id"] is not null)
        .GroupBy(e => (string)e["page"]!["id"]!).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    /// <summary>What the writer reads about it. It goes after the hashed part of the bundle, so it changes no hash.</summary>
    public static string ReportedWrongNote(List<JsonObject>? entries)
    {
        if (entries is null) return "";
        var sb = new StringBuilder("\n## A person said this page is wrong\n\nCheck these against the source above. Say only what the source shows.\n\n");
        foreach (var e in entries)
        {
            var fsd = (string?)e["fsd"]?["quote"];
            sb.Append((string?)e["page"]?["statement"] is { } statement
                ? $"- statement {statement} \"{(string?)e["page"]?["quote"]}\", {(fsd is null ? "which the FSD doesn't mention" : $"against the FSD's \"{fsd}\"")}\n"
                : $"- the page has no statement on this; the FSD says \"{fsd}\"\n");
        }
        return sb.ToString();
    }

    /// <summary>The page was written again, so what a person said about the old one has been dealt with: it goes back to them to look at. Returns what stopped that, if the trace file can't be read:
    /// the page is already paid for and recorded, and the mark stays until the file is fixed and the page runs again.</summary>
    public static string? ClearReportedWrong(string root, string pageId, string? statement = null, string reason = "the page was regenerated")
    {
        if (!File.Exists(Trace.TraceFile.PathIn(root))) return null;
        using var _ = Fsd.LinkRun.Lock(root);
        Entries(root, out var problem);
        if (problem is not null) return problem;
        var trace = Trace.TraceFile.Load(root);
        var marked = trace["entries"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["decision"] == "page-wrong" && (string?)e["page"]?["id"] == pageId && (statement is null || (string?)e["page"]?["statement"] == statement)).ToList();
        foreach (var e in marked)
            Trace.TraceFindings.DropDecision(root, e, StateActor.Code("stangent spec"), reason);
        if (marked.Count > 0) Trace.TraceFile.Save(root, trace);
        return null;
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
        if ((string?)report["traceProblem"] is { } problem) md.Append($"**Warning:** {problem}\n\n");
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
                if (i["reportedWrong"] is JsonArray { Count: > 0 } reported) md.Append($" (a person said this page is wrong: {reported.Count} decision(s))");
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

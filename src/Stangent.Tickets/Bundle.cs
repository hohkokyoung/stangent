using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.Impact;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Tickets;

/// <summary>
/// <c>stangent ticket bundle</c> (plan step 4, D71): the pulled ticket, its starting points, the chosen reference pages
/// (statements with citations), their linked FSD sections with each link's state, the impact answer from the starting
/// points within 2 lines, and pointers to files and symbols. No source code. Over <c>tickets.bundleChars</c> the
/// lowest-ranked pages become pointers. Missing and stale pages are flagged, never generated. Code only: the same
/// inputs give the same bytes, and the result is validated against <c>ticket-bundle.v1.json</c>.
/// </summary>
public static class TicketBundle
{
    public const string Schema = "ticket-bundle.v1.json";
    public const int ImpactDepth = 2;

    public static JsonObject Build(StangentRepo repo, TicketContext ctx, string key)
    {
        var paths = new TicketPaths(repo.Root, key);
        var pulled = TicketPull.LoadOrFail(repo.Root, key);
        var points = TicketPoints.LoadOrFail(repo.Root, key);
        var list = points["points"]!.AsArray().Select(p => StartingPoint.FromJson(p!)).ToList();
        var specPaths = new SpecPaths(repo.Root);

        // Pages, highest ranked first: the developer's own additions, then in the order code and the AI found them.
        var ranked = list.Where(p => p.Kind == StartingPoint.Page).OrderBy(p => p.Signal == "developer" ? 0 : 1).Select(p => p.Id).ToList();
        var defs = ranked.Select(ctx.Set.Find).OfType<PageDef>().ToList();
        var missing = defs.Where(d => !ctx.Index.Pages.ContainsKey(d.Id)).Select(d => d.Id).ToList();
        var stale = defs.Where(d => ctx.Index.Pages.TryGetValue(d.Id, out var page)
            && (string?)page["generated"]?["bundleHash"] != Bundles.Build(ctx.Set, d, null).InputHash).Select(d => d.Id).ToList();
        var written = defs.Where(d => ctx.Index.Pages.ContainsKey(d.Id)).ToList();

        // The impact answer starts from what the written pages' statements cite and from the matched symbols (D70, D71).
        var starts = written.SelectMany(d => PageChecks.Statements(ctx.Index.Pages[d.Id]).SelectMany(s => s.Statement["cites"]!.AsArray().Select(c => (string)c!)))
            .Concat(list.Where(p => p.Kind == StartingPoint.Symbol).Select(p => p.Id))
            .Distinct(StringComparer.Ordinal).Select(ctx.Analyzer.Find).OfType<Node>().ToList();
        var impact = starts.Count == 0 ? NoImpact(ctx.Graph.Commit)
            : ctx.Analyzer.AnalyzeStarts(starts, "starting-points", $"{starts.Count} point(s) from the ticket's starting points", repo.Root, ImpactDepth,
                (reached, expand) => FsdImpact.Sections(repo.Root, reached, expand));

        JsonObject Compose(IReadOnlySet<string> cut)
        {
            var included = written.Where(d => !cut.Contains(d.Id)).ToList();
            var pages = new JsonArray();
            foreach (var d in defs)
            {
                var o = new JsonObject
                {
                    ["id"] = d.Id, ["kind"] = d.Kind, ["title"] = ctx.Title(d), ["path"] = specPaths.Relative(specPaths.PageMarkdown(d)),
                    ["state"] = !ctx.Index.Pages.ContainsKey(d.Id) ? "missing" : cut.Contains(d.Id) ? "cut" : "included",
                };
                if (stale.Contains(d.Id)) o["stale"] = true;
                if ((string)o["state"]! == "included")
                    o["statements"] = new JsonArray([.. PageChecks.Statements(ctx.Index.Pages[d.Id]).Select(s => (JsonNode)new JsonObject
                    {
                        ["id"] = s.Id, ["section"] = s.Section, ["text"] = (string)s.Statement["text"]!, ["cites"] = s.Statement["cites"]!.DeepClone(),
                    })]);
                pages.Add(o);
            }
            var bundle = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["key"] = paths.Key,
                ["source"] = pulled["source"]!.DeepClone(),
                ["raw"] = pulled["raw"]!.DeepClone(),
                ["inputs"] = new JsonObject
                {
                    ["ticket"] = (string)points["ticketHash"]!,
                    ["points"] = "sha256:" + CanonicalJson.Hash(CanonicalJson.Serialize(points)),
                    ["commit"] = ctx.Graph.Commit,
                },
                ["ticket"] = Ticket(pulled),
                ["points"] = points["points"]!.DeepClone(),
                ["changes"] = points["changes"]!.DeepClone(),
                ["pages"] = pages,
                ["sections"] = Sections(repo, ctx, included.Select(d => d.Id).ToHashSet(StringComparer.Ordinal)),
                ["impact"] = impact.DeepClone(),
                ["pointers"] = Pointers(ctx, included, list, impact),
                ["cap"] = new JsonObject { ["limit"] = repo.Config.Tickets.BundleChars, ["chars"] = 0, ["cut"] = new JsonArray([.. cut.Select(c => (JsonNode)c)]), ["over"] = false },
                ["missingPages"] = new JsonArray([.. missing.Select(m => (JsonNode)m)]),
                ["stalePages"] = new JsonArray([.. stale.Select(m => (JsonNode)m)]),
                ["findings"] = repo.Config.Fsd.Findings,
            };
            bundle["cap"]!["chars"] = Size(bundle);
            return bundle;
        }

        // Over the cap: the lowest-ranked written page becomes a pointer, until it fits or none is left (D71).
        var limit = repo.Config.Tickets.BundleChars;
        var cutSet = new List<string>();
        var result = Compose(new HashSet<string>());
        foreach (var d in Enumerable.Reverse(written))
        {
            if ((int)result["cap"]!["chars"]! <= limit) break;
            cutSet.Add(d.Id);
            result = Compose(cutSet.ToHashSet(StringComparer.Ordinal));
        }
        result["cap"]!["over"] = (int)result["cap"]!["chars"]! > limit;

        var errors = SchemaRegistry.Embedded.Validate(Schema, result.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_bundle", "The bundle broke its contract (a stangent bug).", ExitCodes.Failed, errors);
        Quotes.WriteAtomic(paths.BundleJson, CanonicalJson.Serialize(result));
        Quotes.WriteAtomic(paths.BundleMarkdown, Markdown(result));
        return result;
    }

    /// <summary>The size the cap is measured on: the bundle as compact JSON, with its own size field at zero.</summary>
    static int Size(JsonObject bundle)
    {
        var copy = bundle.DeepClone().AsObject();
        copy["cap"]!["chars"] = 0;
        return CanonicalJson.SerializeCompact(copy).Length;
    }

    static JsonObject Ticket(JsonObject pulled)
    {
        var t = pulled["ticket"]!;
        var o = new JsonObject
        {
            ["type"] = t["type"]!.DeepClone(), ["status"] = t["status"]!.DeepClone(), ["summary"] = t["summary"]!.DeepClone(),
            ["description"] = pulled["description"]!.DeepClone(), ["priority"] = t["priority"]?.DeepClone(), ["reporter"] = t["reporter"]?.DeepClone(),
            ["labels"] = t["labels"]!.DeepClone(), ["comments"] = pulled["comments"]!.DeepClone(), ["links"] = t["links"]!.DeepClone(),
            ["attachments"] = new JsonArray([.. pulled["attachments"]!.AsArray().Select(a =>
            {
                var c = a!.DeepClone().AsObject();
                c.Remove("chars");
                c.Remove("bytes");
                return (JsonNode)c;
            })]),
            ["isBug"] = pulled["isBug"]!.DeepClone(),
        };
        if (pulled["bug"] is JsonObject bug) o["bug"] = bug.DeepClone();
        return o;
    }

    /// <summary>FSD sections linked to the included pages (D53): the section's text and each link's state, whether a
    /// person decided it. Findings (gap, conflict, undocumented) only when <c>fsd.findings</c> is on, marked unconfirmed.</summary>
    static JsonArray Sections(StangentRepo repo, TicketContext ctx, IReadOnlySet<string> pages)
    {
        var findings = repo.Config.Fsd.Findings;
        var entries = ctx.Trace.Where(e => e["fsd"] is JsonObject && (string?)e["page"]?["id"] is { } p && pages.Contains(p))
            .Where(e => findings || (string?)e["state"] == "agree");
        var result = new JsonArray();
        foreach (var g in entries.GroupBy(e => ((string)e["fsd"]!["doc"]!, (string)e["fsd"]!["section"]!))
                     .OrderBy(g => g.Key.Item1, StringComparer.Ordinal).ThenBy(g => TraceFile.SectionKey(g.Key.Item2), StringComparer.Ordinal))
        {
            var section = ctx.Index.Sections.GetValueOrDefault(g.Key);
            if (section is null) continue;
            result.Add(new JsonObject
            {
                ["doc"] = g.Key.Item1, ["id"] = g.Key.Item2, ["title"] = (string)section["title"]!, ["text"] = (string)section["text"]!,
                ["links"] = new JsonArray([.. g.OrderBy(e => (string)e["page"]!["id"]!, StringComparer.Ordinal).ThenBy(e => (string?)e["page"]?["statement"], StringComparer.Ordinal)
                    .Select(e =>
                    {
                        var link = new JsonObject
                        {
                            ["state"] = (string)e["state"]!, ["page"] = (string)e["page"]!["id"]!, ["statement"] = (string?)e["page"]?["statement"],
                            ["check"] = (string?)e["check"], ["decision"] = (string?)e["decision"], ["decided"] = e["decision"] is not null,
                        };
                        if ((string)e["state"]! != "agree") link["unconfirmed"] = e["decision"] is null;
                        return (JsonNode)link;
                    })]),
            });
        }
        return result;
    }

    /// <summary>Files and symbols to open: what the included pages cite, the matched symbols, and the impact answer's
    /// items, each once, by file and line.</summary>
    static JsonArray Pointers(TicketContext ctx, List<PageDef> included, List<StartingPoint> points, JsonObject impact)
    {
        var from = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in points.Where(p => p.Kind == StartingPoint.Symbol)) from.TryAdd(p.Id, "point");
        foreach (var d in included)
            foreach (var (_, _, s) in PageChecks.Statements(ctx.Index.Pages[d.Id]))
                foreach (var c in s["cites"]!.AsArray().Select(c => (string)c!)) from.TryAdd(c, d.Id);
        foreach (var g in impact["groups"]!.AsArray())
            foreach (var i in g!["items"]!.AsArray()) from.TryAdd((string)i!["id"]!, "impact");
        return new JsonArray([.. from.Select(kv => (Node: ctx.Analyzer.Find(kv.Key), From: kv.Value)).Where(x => x.Node is not null)
            .OrderBy(x => x.Node!.File ?? "~", StringComparer.Ordinal).ThenBy(x => x.Node!.Line).ThenBy(x => x.Node!.Id, StringComparer.Ordinal)
            .Select(x => (JsonNode)new JsonObject
            {
                ["id"] = x.Node!.Id, ["name"] = x.Node.Name, ["kind"] = x.Node.Kind, ["file"] = x.Node.File, ["line"] = x.Node.Line, ["from"] = x.From,
            })]);
    }

    static JsonObject NoImpact(string commit) => new()
    {
        ["schemaVersion"] = 5, ["graph"] = new JsonObject { ["commit"] = commit },
        ["change"] = new JsonObject { ["kind"] = "starting-points", ["value"] = "none" }, ["groups"] = new JsonArray(), ["unresolved"] = new JsonArray(),
        ["fsdSections"] = ImpactAnalyzer.NoFsdSections(), ["maxDepth"] = ImpactDepth,
    };

    public static JsonObject LoadOrFail(string root, string key) => PageRun.LoadJson(new TicketPaths(root, key).BundleJson) as JsonObject
        ?? throw new StangentException("no_bundle", $"No bundle for {key} yet. Run `stangent ticket bundle {key}` first.", ExitCodes.NotFound);

    // ---- the developer's view ----

    public static string Markdown(JsonObject b)
    {
        var t = b["ticket"]!;
        var md = new StringBuilder($"# {b["key"]}: {t["summary"]}\n\n");
        md.Append($"{t["type"]} · {t["status"]}{((bool)t["isBug"]! ? " · bug" : "")} · {b["pages"]!.AsArray().Count} page(s), {b["pointers"]!.AsArray().Count} pointer(s) · {b["cap"]!["chars"]} of {b["cap"]!["limit"]} characters\n\n");

        if (t["bug"] is JsonObject bug)
        {
            md.Append("## The bug\n\n");
            foreach (var part in TicketPull.BugParts)
                md.Append($"- **{part}:** {((string?)bug[part] is { } v ? Inline(v) : "_missing_")}\n");
            md.Append('\n');
        }

        md.Append("## Starting points\n\n");
        var points = b["points"]!.AsArray();
        if (points.Count == 0) md.Append("None: code found no signal and no AI pick passed the checks. Add some with `stangent ticket points <KEY> --add <page|symbol>`.\n\n");
        else
        {
            md.Append("| | point | why | by |\n|---|---|---|---|\n");
            foreach (var p in points)
                md.Append($"| {p!["kind"]} | `{Cell((string)p["title"]!)}` | {p["signal"]}: {Cell(Inline((string)p["evidence"]!))} | {p["by"]} |\n");
            md.Append('\n');
        }

        md.Append("## Reference pages\n\n");
        foreach (var p in b["pages"]!.AsArray())
        {
            var state = (string)p!["state"]!;
            md.Append($"### {p["title"]} (`{p["id"]}`)\n\n");
            if (state == "missing") { md.Append("⚠️ No written page yet. Generate it with `/stangent:spec-generate`.\n\n"); continue; }
            if ((bool?)p["stale"] == true) md.Append("⚠️ Stale: the code changed since this page was written.\n\n");
            if (state == "cut") { md.Append($"Cut to stay under the size cap: read [{p["path"]}]({p["path"]}).\n\n"); continue; }
            foreach (var s in p["statements"]!.AsArray()) md.Append($"- **{s!["id"]}** {s["text"]}\n");
            md.Append('\n');
        }

        var sections = b["sections"]!.AsArray();
        if (sections.Count > 0)
        {
            md.Append("## FSD sections\n\n");
            foreach (var s in sections)
            {
                md.Append($"### §{s!["id"]} {s["title"]} ({s["doc"]})\n\n");
                foreach (var l in s["links"]!.AsArray())
                    md.Append($"- {l!["state"]} with `{l["page"]}`{(l["statement"] is null ? "" : $" {l["statement"]}")}{((bool)l["decided"]! ? $" · decided: {l["decision"]}" : " · not decided yet")}{((bool?)l["unconfirmed"] == true ? " · unconfirmed" : "")}\n");
                md.Append('\n');
            }
        }

        md.Append("## Impact\n\n");
        md.Append(b["impact"]!["groups"]!.AsArray().Count == 0 ? "No symbols to start from.\n\n" : ImpactAnalyzer.Markdown(b["impact"]!.AsObject()) + "\n");

        var files = t["attachments"]!.AsArray();
        if (files.Count > 0)
        {
            md.Append("## Attachments\n\n");
            foreach (var a in files)
                md.Append($"- `{a!["fileName"]}` ({a["kind"]}{((bool?)a["truncated"] == true ? ", cut to the cap" : "")}){(a["path"] is null ? "" : $": {a["path"]}")}\n");
            md.Append('\n');
        }

        var missing = b["missingPages"]!.AsArray();
        var cut = b["cap"]!["cut"]!.AsArray();
        if (missing.Count > 0 || cut.Count > 0 || (bool)b["cap"]!["over"]!)
        {
            md.Append("## Notes\n\n");
            if (missing.Count > 0) md.Append($"- {missing.Count} chosen page(s) have no written version; the bundle doesn't generate them (D71).\n");
            if (cut.Count > 0) md.Append($"- {cut.Count} page(s) were cut to pointers to stay under {b["cap"]!["limit"]} characters.\n");
            if ((bool)b["cap"]!["over"]!) md.Append("- Still over the cap with every page cut.\n");
        }
        return md.ToString();
    }

    static string Inline(string s) => s.Replace("\n", " ").Trim();
    static string Cell(string s) => s.Replace("|", "\\|");
}

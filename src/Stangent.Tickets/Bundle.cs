using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.Impact;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Tickets;

/// <summary>
/// <c>stangent ticket bundle</c> (plan step 4, D71, D101): the pulled ticket, its starting points (the files the file
/// finder named, each with its ticket quote and the line from the file; or code's and the developer's pages and
/// symbols), the reference pages that explain them (statements with citations), their linked FSD sections with each
/// link's state, the impact answer from the starting points within 2 lines, and pointers to files and symbols. No
/// source code. Over <c>tickets.bundleChars</c>, first the impact answer's item lists go (its counts stay), then the
/// lowest-ranked pages become pointers; the starting points are never cut. Missing and stale pages are flagged, never
/// generated. Code only: the same inputs give the same bytes, and the result is validated against
/// <c>ticket-bundle.v2.json</c>.
/// </summary>
public static class TicketBundle
{
    public const string Schema = "ticket-bundle.v2.json";
    public const int ImpactDepth = 2;

    public static JsonObject Build(StangentRepo repo, TicketContext ctx, string key)
    {
        var paths = new TicketPaths(repo.Root, key);
        var pulled = TicketPull.LoadOrFail(repo.Root, key);
        var points = TicketPoints.LoadOrFail(repo.Root, key);
        var list = points["points"]!.AsArray().Select(p => StartingPoint.FromJson(p!)).ToList();
        var specPaths = new SpecPaths(repo.Root);

        // Pages, highest ranked first: page points (the developer's own first), then the pages explaining the file points.
        var files = list.Where(p => p.Kind == StartingPoint.File).Select(p => p.Id).ToList();
        var pagePoints = list.Where(p => p.Kind == StartingPoint.Page).OrderBy(p => p.Signal == "developer" ? 0 : 1).Select(p => p.Id).Distinct(StringComparer.Ordinal).ToList();
        var explains = Explaining(ctx, files);
        var ranked = pagePoints.Concat(explains.Keys.Where(id => !pagePoints.Contains(id))).ToList();
        var defs = ranked.Select(ctx.Set.Find).OfType<PageDef>().ToList();
        // Missing: a page point with no written page, or (D101) an unwritten page whose slice holds a found file no
        // written page explains, so writing it would explain that file.
        var unexplained = files.Where(f => !explains.Values.Any(h => h.Any(x => x.File == f))).ToHashSet(StringComparer.Ordinal);
        var missing = defs.Where(d => !ctx.Index.Pages.ContainsKey(d.Id)).Select(d => d.Id)
            .Concat(unexplained.Order(StringComparer.Ordinal).SelectMany(f => NearestSlices(ctx, f, written: false)))
            .Distinct(StringComparer.Ordinal).ToList();
        // Stale is worked out only for pages that end up in the bundle (a slice bundle per page is slow).
        var staleCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        var checkStale = false;   // only for the final build: the size search doesn't need it (a few characters a page)
        bool IsStale(PageDef d) => checkStale && (staleCache.TryGetValue(d.Id, out var v) ? v : staleCache[d.Id] = ctx.Index.Pages.TryGetValue(d.Id, out var page)
            && (string?)page["generated"]?["bundleHash"] != Bundles.Build(ctx.Set, d, null).InputHash);
        var written = defs.Where(d => ctx.Index.Pages.ContainsKey(d.Id)).ToList();

        // The impact answer starts from the starting points themselves (D27, D101): the file points' code, the symbol
        // points, and what the page points' statements cite. Pages that only explain a file don't widen it.
        var starts = files.SelectMany(ctx.Analyzer.InFile)
            .Concat(written.Where(d => pagePoints.Contains(d.Id))
                .SelectMany(d => PageChecks.Statements(ctx.Index.Pages[d.Id]).SelectMany(s => s.Statement["cites"]!.AsArray().Select(c => (string)c!))).Select(ctx.Analyzer.Find).OfType<Node>())
            .Concat(list.Where(p => p.Kind == StartingPoint.Symbol).Select(p => ctx.Analyzer.Find(p.Id)).OfType<Node>())
            .DistinctBy(n => n.Id).ToList();
        var impact = starts.Count == 0 ? NoImpact(ctx.Graph.Commit)
            : ctx.Analyzer.AnalyzeStarts(starts, "starting-points", $"{starts.Count} point(s) from the ticket's starting points", repo.Root, ImpactDepth,
                (reached, expand) => FsdImpact.Sections(repo.Root, reached, expand));

        // Whether the finder's answer is stale is judged now, against the code the bundle is built on (D101).
        var codeNow = points["ai"]?["commit"] is null ? "" : TicketPoints.CodeState(repo.Root);

        JsonObject Compose(IReadOnlySet<string> cut, bool trimImpact)
        {
            var included = written.Where(d => !cut.Contains(d.Id)).ToList();
            var shownImpact = trimImpact ? TrimLists(impact) : impact;
            var pages = new JsonArray();
            // A cut page that only explains a file is left out, only counted: listing hundreds of them would itself
            // break the cap. A cut page point stays listed, as a pointer to its page.
            var dropped = defs.Count(d => cut.Contains(d.Id) && !pagePoints.Contains(d.Id));
            foreach (var d in defs.Where(d => !(cut.Contains(d.Id) && !pagePoints.Contains(d.Id))))
            {
                var o = new JsonObject
                {
                    ["id"] = d.Id, ["kind"] = d.Kind, ["title"] = ctx.Title(d), ["path"] = specPaths.Relative(specPaths.PageMarkdown(d)),
                    ["state"] = !ctx.Index.Pages.ContainsKey(d.Id) ? "missing" : cut.Contains(d.Id) ? "cut" : "included",
                };
                if (explains.TryGetValue(d.Id, out var how))
                    o["explains"] = new JsonArray([.. how.Select(h => (JsonNode)new JsonObject { ["file"] = h.File, ["how"] = h.How })]);
                if (IsStale(d)) o["stale"] = true;
                if ((string)o["state"]! == "included")
                    o["statements"] = new JsonArray([.. PageChecks.Statements(ctx.Index.Pages[d.Id]).Select(s => (JsonNode)new JsonObject
                    {
                        ["id"] = s.Id, ["section"] = s.Section, ["text"] = (string)s.Statement["text"]!, ["cites"] = s.Statement["cites"]!.DeepClone(),
                    })]);
                pages.Add(o);
            }
            var bundle = new JsonObject
            {
                ["schemaVersion"] = 2,
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
                ["outside"] = points["outside"]?.DeepClone() ?? new JsonArray(),
                // Files the finder named that failed code's checks (D101): shown, never dropped silently.
                ["rejected"] = points["ai"]?["rejected"]?.DeepClone() ?? new JsonArray(),
                // The commit the finder searched, and whether the code has moved on since (D101).
                ["finder"] = points["ai"]?["commit"] is JsonValue c
                    ? new JsonObject { ["commit"] = (string)c!, ["stale"] = (string)c! != codeNow }
                    : null,
                ["pages"] = pages,
                ["sections"] = Sections(repo, ctx, included.Select(d => d.Id).ToHashSet(StringComparer.Ordinal)),
                ["impact"] = shownImpact.DeepClone(),
                // A cut page point keeps its citations (it is a starting point); a cut page that only explains a file
                // takes its citations with it: the file it explains is a pointer of its own.
                ["pointers"] = Pointers(ctx, included.Concat(written.Where(d => cut.Contains(d.Id) && pagePoints.Contains(d.Id))).ToList(), list, shownImpact),
                ["cap"] = new JsonObject
                {
                    ["limit"] = repo.Config.Tickets.BundleChars, ["chars"] = 0, ["impactTrimmed"] = trimImpact,
                    ["cut"] = new JsonArray([.. cut.Where(pagePoints.Contains).Select(c => (JsonNode)c)]), ["dropped"] = dropped, ["over"] = false,
                },
                ["missingPages"] = new JsonArray([.. missing.Select(m => (JsonNode)m)]),
                ["stalePages"] = new JsonArray([.. included.Concat(written.Where(d => cut.Contains(d.Id) && pagePoints.Contains(d.Id))).Where(IsStale).Select(d => (JsonNode)d.Id)]),
                ["findings"] = repo.Config.Fsd.Findings,
            };
            bundle["cap"]!["chars"] = Size(bundle);
            return bundle;
        }

        // Over the cap (D101): the impact answer's lists go first, then the lowest-ranked written page becomes a pointer,
        // until it fits or none is left. The starting points always stay.
        // The pages that only explain a file are kept best-first while they fit, found by halving (a handful of builds,
        // however many pages cite a widely used file); then page points are cut one at a time, lowest first.
        var limit = repo.Config.Tickets.BundleChars;
        var explainers = written.Where(d => !pagePoints.Contains(d.Id)).ToList();
        HashSet<string> CutAfter(int keep, IEnumerable<string> more) =>
            [.. explainers.Skip(keep).Select(d => d.Id).Concat(more)];
        bool Fits(JsonObject b) => (int)b["cap"]!["chars"]! <= limit;
        var trim = false;
        var result = Compose(CutAfter(explainers.Count, []), trim);
        if (!Fits(result)) { trim = true; result = Compose(CutAfter(explainers.Count, []), trim); }
        var kept = explainers.Count;
        if (!Fits(result))
        {
            int lo = 0, hi = explainers.Count;   // the most explaining pages that fit lies in [lo, hi)
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (Fits(Compose(CutAfter(mid, []), trim))) lo = mid; else hi = mid - 1;
            }
            kept = lo;
        }
        var cutPoints = new List<string>();
        result = Compose(CutAfter(kept, cutPoints), trim);
        foreach (var d in Enumerable.Reverse(written.Where(d => pagePoints.Contains(d.Id)).ToList()))
        {
            if (Fits(result)) break;
            cutPoints.Add(d.Id);
            result = Compose(CutAfter(kept, cutPoints), trim);
        }
        // The final build, with the stale check; it adds a few characters a page, so trim again if that tips it over.
        checkStale = true;
        result = Compose(CutAfter(kept, cutPoints), trim);
        while (!Fits(result) && kept > 0) result = Compose(CutAfter(--kept, cutPoints), trim);
        foreach (var d in Enumerable.Reverse(written.Where(d => pagePoints.Contains(d.Id) && !cutPoints.Contains(d.Id)).ToList()))
        {
            if (Fits(result)) break;
            cutPoints.Add(d.Id);
            result = Compose(CutAfter(kept, cutPoints), trim);
        }
        result["cap"]!["over"] = !Fits(result);

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

    /// <summary>The written pages that explain the file points (D101), best first, each with the files it explains:
    /// pages whose statements cite code in a file ("cites"), ranked by how many of the files they cite, then by the
    /// first file's rank; for a file no page cites, the pages whose slice holds it ("slice"). A nearby page from a graph
    /// walk isn't used: it explained the fix in 1 of 12 Sonarr bugs (phases/3-tickets.md, 2026-11-02).</summary>
    static Dictionary<string, List<(string File, string How)>> Explaining(TicketContext ctx, List<string> files)
    {
        var result = new Dictionary<string, List<(string File, string How)>>(StringComparer.Ordinal);
        if (files.Count == 0) return result;
        var rank = files.Select((f, i) => (f, i)).ToDictionary(x => x.f, x => x.i, StringComparer.Ordinal);
        var cites = new List<(string Page, List<string> Files)>();
        foreach (var p in ctx.Set.Pages.Where(p => ctx.Index.Pages.ContainsKey(p.Id)))
        {
            var cited = PageChecks.Statements(ctx.Index.Pages[p.Id]).SelectMany(s => s.Statement["cites"]!.AsArray().Select(c => (string)c!))
                .Select(id => ctx.Analyzer.Find(id)?.File).OfType<string>().Where(rank.ContainsKey).Distinct(StringComparer.Ordinal)
                .OrderBy(f => rank[f]).ToList();
            if (cited.Count > 0) cites.Add((p.Id, cited));
        }
        foreach (var (page, cited) in cites.OrderByDescending(c => c.Files.Count).ThenBy(c => rank[c.Files[0]]).ThenBy(c => c.Page, StringComparer.Ordinal))
            result[page] = [.. cited.Select(f => (f, "cites"))];
        var covered = cites.SelectMany(c => c.Files).ToHashSet(StringComparer.Ordinal);
        foreach (var file in files.Where(f => !covered.Contains(f)))
            foreach (var id in NearestSlices(ctx, file, written: true))
            {
                if (!result.TryGetValue(id, out var how)) result[id] = how = [];
                how.Add((file, "slice"));
            }
        return result;
    }

    /// <summary>The pages (written, or not yet) whose slice reaches the file in the fewest lines from their entry: the
    /// closest descriptions of it. Ties all kept, by id. Not every page that passes through it: a file inside a widely
    /// used service sits in hundreds of slices.</summary>
    static IEnumerable<string> NearestSlices(TicketContext ctx, string file, bool written)
    {
        var at = ctx.Set.Pages.Where(p => ctx.Index.Pages.ContainsKey(p.Id) == written && ctx.Set.Slices.TryGetValue(p.Id, out _))
            .Select(p => (p.Id, Depth: ctx.Set.Slices[p.Id].Nodes.Where(n => n.Role == "walk" && n.Node.File == file).Select(n => (int?)n.Depth).Min()))
            .Where(x => x.Depth is not null).ToList();
        if (at.Count == 0) return [];
        var best = at.Min(x => x.Depth);
        return at.Where(x => x.Depth == best).Select(x => x.Id).Order(StringComparer.Ordinal);
    }

    /// <summary>The impact answer with its item lists dropped and their counts kept (every list holding <c>items</c> and a
    /// <c>total</c>): what the cap gives way first (D101). <c>stangent graph impact</c> lists them in full.</summary>
    static JsonObject TrimLists(JsonObject impact)
    {
        var copy = impact.DeepClone().AsObject();
        void Walk(JsonNode? n)
        {
            if (n is JsonObject o)
            {
                if (o["items"] is JsonArray items && o["total"] is JsonValue total && total.TryGetValue<int>(out var t))
                {
                    items.Clear();
                    o["more"] = t;
                }
                foreach (var (_, v) in o.ToList()) Walk(v);
            }
            else if (n is JsonArray a) foreach (var v in a.ToList()) Walk(v);
        }
        Walk(copy);
        return copy;
    }

    /// <summary>Files and symbols to open: the file points first, the matched symbols, what the given pages cite, and the
    /// impact answer's items, each once, by file and line.</summary>
    static JsonArray Pointers(TicketContext ctx, List<PageDef> written, List<StartingPoint> points, JsonObject impact)
    {
        var from = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new JsonArray();
        var fileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in points.Where(p => p.Kind == StartingPoint.File))
        {
            // The graph's own file node when it has one (same id), so a page or the impact answer citing it adds nothing twice.
            var id = "file:" + p.Id;
            if (!fileIds.Add(id)) continue;
            files.Add(new JsonObject { ["id"] = id, ["name"] = Path.GetFileName(p.Id), ["kind"] = "file", ["file"] = p.Id, ["from"] = "point" });
        }
        foreach (var p in points.Where(p => p.Kind == StartingPoint.Symbol)) from.TryAdd(p.Id, "point");
        foreach (var d in written)
            foreach (var (_, _, s) in PageChecks.Statements(ctx.Index.Pages[d.Id]))
                foreach (var c in s["cites"]!.AsArray().Select(c => (string)c!)) from.TryAdd(c, d.Id);
        foreach (var g in impact["groups"]!.AsArray())
            foreach (var i in g!["items"]!.AsArray()) from.TryAdd((string)i!["id"]!, "impact");
        foreach (var x in from.Where(kv => !fileIds.Contains(kv.Key)).Select(kv => (Node: ctx.Analyzer.Find(kv.Key), From: kv.Value)).Where(x => x.Node is not null)
            .OrderBy(x => x.Node!.File ?? "~", StringComparer.Ordinal).ThenBy(x => x.Node!.Line).ThenBy(x => x.Node!.Id, StringComparer.Ordinal))
            files.Add(new JsonObject
            {
                ["id"] = x.Node!.Id, ["name"] = x.Node.Name, ["kind"] = x.Node.Kind, ["file"] = x.Node.File, ["line"] = x.Node.Line, ["from"] = x.From,
            });
        return files;
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

    /// <param name="forProof">For the reproducer's input (D107): leaves out the finder's reasons. They are a guess at the
    /// cause from reading the code, nothing has run yet, and the test should check what the ticket reports, not that guess.</param>
    public static string Markdown(JsonObject b, bool forProof = false)
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
        var outside = OutsidePart.Of(b);
        if (points.Count == 0 && outside.Count > 0) md.Append($"**None in the code:** every part of this ticket is outside the code (below){((bool)t["isBug"]! ? ", so bug proof is skipped" : "")}. If one is really in the code, remove it with `stangent ticket points <KEY> --remove-outside <area>` or add a page with `--add`.\n\n");
        else if (points.Count == 0) md.Append("**No confident starting point:** none was found in the ticket, picked, or kept. The questions draft asks the reporter where it happens. Add some with `stangent ticket points <KEY> --add <page|symbol>`.\n\n");
        else
        {
            md.Append("| | point | why | by |\n|---|---|---|---|\n");
            foreach (var p in points)
                md.Append((string)p!["kind"]! == StartingPoint.File && p["line"] is not null
                    ? $"| file | `{Cell((string)p["id"]!)}` | {(forProof || (string?)p["why"] is not { Length: > 0 } why ? "" : $"The finder's guess, not checked: {Cell(Inline(why))} ")}The ticket: \"{Cell(Inline((string)p["evidence"]!))}\" · the code: `{Cell(Inline((string)p["line"]!))}` | {p["by"]} |\n"
                    : (string)p["kind"]! == StartingPoint.File ? $"| file | `{Cell((string)p["id"]!)}` | added by hand | {p["by"]} |\n"
                    : $"| {p["kind"]} | `{Cell((string)p["title"]!)}` | {p["signal"]}: {Cell(Inline((string)p["evidence"]!))} | {p["by"]} |\n");
            md.Append('\n');
        }

        var rejected = b["rejected"] as JsonArray ?? [];
        if (rejected.Count > 0)
        {
            md.Append("## Named, not shown in code\n\nThe file finder named these, but its quote isn't in the ticket or its line isn't in the file (D101). Check them by hand before relying on them.\n\n");
            foreach (var r in rejected) md.Append($"- `{Cell((string)r!["path"]!)}`: the ticket \"{Cell(Inline((string)r["quote"]!))}\" · the code `{Cell(Inline((string)r["line"]!))}`\n");
            md.Append('\n');
        }

        if (outside.Count > 0)
        {
            md.Append("## Outside the code\n\nParts whose cause lives where the repo doesn't: no code here can show or fix them (D83).\n\n| area | what | by |\n|---|---|---|\n");
            foreach (var o in outside) md.Append($"| {o.Area} | {Cell(Inline(o.Quote))} | {o.By} |\n");
            md.Append('\n');
        }

        md.Append("## Reference pages\n\n");
        foreach (var p in b["pages"]!.AsArray())
        {
            var state = (string)p!["state"]!;
            md.Append($"### {p["title"]} (`{p["id"]}`)\n\n");
            if (p["explains"] is JsonArray ex && ex.Count > 0)
                md.Append($"Explains {string.Join(", ", ex.Select(e => $"`{e!["file"]}` ({((string)e["how"]! == "cites" ? "its statements cite it" : "it's in the code this page describes")})"))}.\n\n");
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
        var trimmed = (bool?)b["cap"]!["impactTrimmed"] == true;
        var staleFinder = (bool?)b["finder"]?["stale"] == true;
        var droppedPages = (int?)b["cap"]!["dropped"] ?? 0;
        if (missing.Count > 0 || cut.Count > 0 || droppedPages > 0 || trimmed || staleFinder || (bool)b["cap"]!["over"]!)
        {
            md.Append("## Notes\n\n");
            if (staleFinder) md.Append($"- The files were found at commit {((string)b["finder"]!["commit"]!)[..Math.Min(10, ((string)b["finder"]!["commit"]!).Length)]}; the code has changed since. `stangent ticket points {b["key"]} --refind` searches again.\n");
            if (trimmed) md.Append($"- The impact answer's lists were left out to stay under {b["cap"]!["limit"]} characters; its counts are kept. `stangent graph impact` lists them.\n");
            if (missing.Count > 0) md.Append($"- {missing.Count} chosen page(s) have no written version; the bundle doesn't generate them (D71).\n");
            if (cut.Count > 0) md.Append($"- {cut.Count} page(s) were cut to pointers to stay under {b["cap"]!["limit"]} characters.\n");
            if (droppedPages > 0) md.Append($"- {droppedPages} more page(s) that explain the found files were left out to stay under the cap.\n");
            if ((bool)b["cap"]!["over"]!) md.Append("- Still over the cap with every page cut; the starting points are never cut.\n");
        }
        return md.ToString();
    }

    static string Inline(string s) => s.Replace("\n", " ").Trim();
    static string Cell(string s) => s.Replace("|", "\\|");
}

using System.Globalization;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Pages;
using Stangent.Tickets;

namespace Stangent.Benchmark.Tickets;

/// <summary>
/// Would the "nearby page" rule explain a fix that no page cites? (Retrieval redesign, decisions/answered-questions.md,
/// 2026-11-02; no AI, no bar.) For each bug, start from the files the real fix changed, so this tests explaining, not
/// finding. Walk the slice walk's own lines in reverse (<see cref="SliceBuilder.Forward"/>: no <c>tests</c>,
/// <c>triggered-by</c> or <c>maps</c>, which point back at callers or at side effects), by shortest path, and stop at
/// hubs as slices do (a hub is reached but its callers aren't: a slice never walks through a hub unless the hub is its
/// entry, so a hub that is a page's entry is still a candidate). No depth limit: a pick deeper than its page's own
/// slice depth is flagged (<c>withinOwnDepth</c>). The nearest
/// entry points with a written page are the candidates; ties are ranked by the ticket's words (the D87 page index).
/// Outcomes, in order:
/// <list type="bullet">
/// <item><c>no start points</c>: no fix file has points in the graph.</item>
/// <item><c>dead end</c>: the walk reaches no entry point with a written page.</item>
/// <item><c>no right page: judge the pick</c>: no page is a D87 right page for this bug (its walk at its own depth + 2
/// reaches a fix file), so code can't score the pick.</item>
/// <item><c>right page not written</c>: right pages exist, none is written, so none can be picked.</item>
/// <item><c>lands on a right page</c> / <c>right page among the ties, ranked lower</c> / <c>wrong area</c>.</item>
/// </list>
/// "Lands on a right page" is close to guaranteed when the nearest page is within the right-page margin, so it says
/// little about whether the page is about the ticket: a fresh agent judges the page-gap picks (results write-up).
/// Not modelled: slices also stop at shared components and, for screens, at endpoints, so a few candidates may be
/// pages whose own slice stops short of the fix. Only reach is an upper bound; the pick can move either way.
/// </summary>
public static class NearbyWalk
{
    public static readonly string[] Outcomes =
        ["no start points", "dead end", "no right page: judge the pick", "right page not written", "lands on a right page",
         "right page among the ties, ranked lower", "wrong area"];

    /// <summary>The page-gap stages: the fix file can be found, but no picked page cites it.</summary>
    static readonly HashSet<string> PageGap = [PlaceStages.Stages[0], PlaceStages.Stages[4], PlaceStages.Stages[5]];

    public static JsonObject Run(string clone, string ticketsDir, JsonObject stages)
    {
        var repo = StangentRepo.Discover(clone);
        var ctx = TicketContext.Load(repo);
        var index = PageIndex.Build(ctx, repo.Root, repo.Config);
        var rightWalk = IndexScore.WalkFiles(ctx, -2);
        var writtenIds = ctx.Set.Pages.Where(p => ctx.Index.Pages.ContainsKey(p.Id)).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var byEntry = ctx.Set.Pages.Where(p => writtenIds.Contains(p.Id))
            .GroupBy(p => p.Entry, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var callers = ctx.Graph.AllEdges.Where(e => SliceBuilder.Forward.Contains(e.Kind) && ctx.Set.Builder.Find(e.Source) is not null)
            .GroupBy(e => e.Target, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var hubs = ctx.Set.Builder.Hubs;
        var stageOf = stages["cases"]!.AsArray().OfType<JsonObject>().ToDictionary(c => (string)c["key"]!, c => (string)c["stage"]!, StringComparer.Ordinal);
        var selection = Load(Path.Combine(ticketsDir, "selection.json"));
        var answers = Load(Path.Combine(ticketsDir, "answer-key.json"))["issues"]!.AsObject();
        var keys = Load(Path.Combine(ticketsDir, "jira-keys.json"));

        var rows = new JsonArray();
        var all = Outcomes.ToDictionary(o => o, _ => 0, StringComparer.Ordinal);
        var gap = Outcomes.ToDictionary(o => o, _ => 0, StringComparer.Ordinal);
        foreach (var bug in selection["issues"]!.AsArray().OfType<JsonObject>().Where(i => (string)i["kind"]! == "bug"))
        {
            var number = ((int)bug["issue"]!).ToString(CultureInfo.InvariantCulture);
            var jira = (string)keys[number]!;
            var fix = answers[number]!["files"]!.AsArray().Select(f => (string)f!).ToHashSet(StringComparer.Ordinal);
            var right = ctx.Set.Pages.Where(p => rightWalk[p.Id].Overlaps(fix)).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

            // Shortest path backwards along the slice's lines; a hub is reached, never walked through.
            var starts = fix.Order(StringComparer.Ordinal).SelectMany(ctx.Analyzer.InFile).Select(n => n.Id).Distinct(StringComparer.Ordinal).ToList();
            var depth = starts.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
            var queue = new Queue<string>(starts);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (depth[id] > 0 && hubs.Contains(id)) continue;
                foreach (var c in callers.GetValueOrDefault(id) ?? [])
                    if (depth.TryAdd(c, depth[id] + 1)) queue.Enqueue(c);
            }

            var candidates = depth.Where(kv => byEntry.ContainsKey(kv.Key))
                .SelectMany(kv => byEntry[kv.Key].Select(p => (Page: p.Id, Depth: kv.Value))).ToList();
            var nearest = candidates.Count == 0 ? -1 : candidates.Min(c => c.Depth);
            var ranking = index.Rank(TicketText.From(Load(Path.Combine(clone, ".stangent", "tickets", jira, "ticket.json")))).Pages;
            var rank = ranking.Select((r, i) => (r.Page.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
            var score = ranking.ToDictionary(r => r.Page.Id, r => r.Score, StringComparer.Ordinal);
            var ties = candidates.Where(c => c.Depth == nearest).Select(c => c.Page).Distinct(StringComparer.Ordinal)
                .OrderBy(id => rank.GetValueOrDefault(id, int.MaxValue)).ThenBy(id => id, StringComparer.Ordinal).ToList();
            var pick = ties.FirstOrDefault();
            var rightWritten = right.Where(writtenIds.Contains).ToHashSet(StringComparer.Ordinal);
            var rightDepths = candidates.Where(c => right.Contains(c.Page)).Select(c => c.Depth).ToList();

            var outcome = starts.Count == 0 ? Outcomes[0]
                : pick is null ? Outcomes[1]
                : right.Count == 0 ? Outcomes[2]
                : rightWritten.Count == 0 ? Outcomes[3]
                : right.Contains(pick) ? Outcomes[4]
                : ties.Any(right.Contains) ? Outcomes[5]
                : Outcomes[6];
            all[outcome]++;
            var stage = stageOf.GetValueOrDefault(jira, "?");
            var isGap = PageGap.Contains(stage);
            if (isGap) gap[outcome]++;

            var pickScore = pick is null ? 0 : score.GetValueOrDefault(pick);
            var nextScore = ties.Count > 1 ? score.GetValueOrDefault(ties[1]) : double.NaN;
            var row = new JsonObject
            {
                ["key"] = jira, ["issue"] = (int)bug["issue"]!, ["stage"] = stage, ["pageGap"] = isGap, ["outcome"] = outcome,
                ["startPoints"] = starts.Count, ["nearestDepth"] = nearest < 0 ? null : nearest, ["ties"] = ties.Count,
                ["pick"] = pick, ["pickTitle"] = pick is null ? null : ctx.Title(ctx.Set.Find(pick)!),
                ["pickScore"] = Math.Round(pickScore, 3),
                // With several ties and no word match (or an equal score), the pick is the index's order: not a ranking.
                ["tieUnbroken"] = ties.Count > 1 && (pickScore <= 0 || pickScore == nextScore),
                ["withinOwnDepth"] = pick is null ? null : nearest <= IndexScore.OwnDepth(ctx, ctx.Set.Find(pick)!),
                ["rightPages"] = right.Count, ["rightPagesWritten"] = rightWritten.Count,
                ["nearestRightDepth"] = rightDepths.Count == 0 ? null : rightDepths.Min(),
            };
            // Where a dead end stops, to aim the graph work: hubs it couldn't walk through, pages it reached that aren't
            // written, and the farthest non-test points nothing calls along the slice's lines (a missing link, or code
            // only a framework runs).
            if (pick is null && starts.Count > 0)
            {
                row["hubsReached"] = new JsonArray([.. depth.Where(kv => kv.Value > 0 && hubs.Contains(kv.Key)).OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => (JsonNode)new JsonObject
                    {
                        ["id"] = kv.Key, ["depth"] = kv.Value, ["callers"] = callers.GetValueOrDefault(kv.Key)?.Count ?? 0,
                        ["nonTestCallers"] = callers.GetValueOrDefault(kv.Key)?.Count(c => ctx.Analyzer.Find(c) is { IsTest: false }) ?? 0,
                    })]);
                var unwritten = ctx.Set.Pages.Where(p => !writtenIds.Contains(p.Id) && depth.ContainsKey(p.Entry))
                    .Select(p => (p.Id, Depth: depth[p.Entry])).OrderBy(x => x.Depth).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
                row["nearestUnwrittenPage"] = unwritten.Id is null ? null : new JsonObject { ["id"] = unwritten.Id, ["depth"] = unwritten.Depth };
                row["roots"] = new JsonArray([.. depth.Where(kv => !callers.ContainsKey(kv.Key))
                    .Select(kv => (Node: ctx.Analyzer.Find(kv.Key), Depth: kv.Value)).Where(x => x.Node is { IsTest: false })
                    .Where(x => x.Node!.Kind is NodeKind.Method or NodeKind.Type or NodeKind.Component)
                    .OrderByDescending(x => x.Depth).ThenBy(x => x.Node!.Id, StringComparer.Ordinal).Take(8)
                    .Select(x => (JsonNode)new JsonObject { ["id"] = x.Node!.Id, ["kind"] = x.Node.Kind, ["file"] = x.Node.File, ["depth"] = x.Depth })]);
            }
            rows.Add(row);
        }
        return new JsonObject
        {
            ["pageGapBugs"] = gap.Values.Sum(),
            ["pageGap"] = new JsonObject([.. Outcomes.Select(o => KeyValuePair.Create(o, (JsonNode?)gap[o]))]),
            ["allBugs"] = new JsonObject([.. Outcomes.Select(o => KeyValuePair.Create(o, (JsonNode?)all[o]))]),
            ["note"] = "starts from the real fix files (tests explaining, not finding); walk = the slice's own lines in reverse, shortest path, stopping at hubs; ties ranked by the D87 page index; no depth limit (withinOwnDepth flags picks past their page's slice depth); right page = D87's (own depth + 2), so 'lands on a right page' is close to guaranteed within that margin and a fresh agent judges whether page-gap picks are about the ticket; slice stops at shared components and endpoints not modelled",
            ["cases"] = rows,
        };
    }

    static JsonObject Load(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
}

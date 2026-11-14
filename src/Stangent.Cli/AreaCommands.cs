using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Flows;

/// <summary><c>stangent areas …</c>: group start flows into the app's parts, proposed by code and confirmed once by a person (D218–D226, docs/areas.md).</summary>
static class AreaCommands
{
    public static void Add(RootCommand root)
    {
        var areas = new Command("areas", "Areas: the app's parts as its users name them, read from its menu, folders the fallback; proposed by code, confirmed once in specs/areas.yaml (D218–D226).");
        root.Subcommands.Add(areas);

        var freshOption = new Option<bool>("--fresh") { Description = "Propose from scratch even when specs/areas.yaml exists (by default a proposal builds on it, D234)." };
        var propose = new Command("propose", "Propose areas from the graph and specs/flows/index.json, built on specs/areas.yaml when it exists; writes specs/areas.proposed.yaml, never specs/areas.yaml.") { freshOption };
        propose.SetAction(pr => Cli.Run(pr, "areas propose", ctx =>
        {
            var a = Areas.Load(Cli.LoadGraph(ctx), ctx.Repo.Root);
            var confirmed = Path.Combine(ctx.Repo.Root, AreaFiles.Confirmed);
            var onConfirmed = !pr.GetValue(freshOption) && File.Exists(confirmed);
            List<string> added = [], dropped = [];
            AreasFile file;
            Placement placement;
            if (onConfirmed) (file, placement, added, dropped) = AreaFiles.Extend(a, AreaFiles.Parse(File.ReadAllText(confirmed)));
            else (file, placement) = AreaFiles.Propose(a);
            AreaFiles.WriteText(ctx.Repo.Root, AreaFiles.Proposed, AreaFiles.Render(file, a.Commit, placement, onConfirmed));
            var counts = placement.AreaOf.Values.GroupBy(x => x, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
            ctx.Span.Attributes["stangent.artifact.count"] = file.Areas.Count;
            return new JsonObject
            {
                ["commit"] = a.Commit, ["file"] = AreaFiles.Proposed, ["startFlows"] = a.Starts.Count, ["menus"] = new JsonArray([.. a.KeptMenus.Select(m => (JsonNode)m)]),
                ["areas"] = new JsonArray([.. file.Areas.Select(x => (JsonNode)new JsonObject { ["name"] = x.Name, ["flows"] = counts.GetValueOrDefault(x.Name) })]),
                ["ties"] = file.Ties.Count, ["unanswered"] = file.Ties.Count(t => t.Answer == ""), ["unplaced"] = placement.Uncovered.Count,
                ["builtOn"] = onConfirmed ? AreaFiles.Confirmed : null,
                ["added"] = new JsonArray([.. added.Select(x => (JsonNode)x)]), ["droppedAnswers"] = new JsonArray([.. dropped.Select(x => (JsonNode)x)]),
                ["large"] = new JsonArray([.. file.Large.Select(l => (JsonNode)new JsonObject { ["area"] = l.Area, ["flows"] = l.Flows })]),
            };
        }));
        areas.Subcommands.Add(propose);

        var check = new Command("check", "Check specs/areas.yaml: every start flow in exactly one area, nothing uncovered, every tie answered, flow files where it puts them; writes specs/areas.json and specs/areas/<area>/page.md.");
        check.SetAction(pr => Cli.Run(pr, "areas check", ctx =>
        {
            var path = Path.Combine(ctx.Repo.Root, AreaFiles.Confirmed);
            if (!File.Exists(path))
                throw new StangentException("not_found", $"No {AreaFiles.Confirmed}: run `stangent areas propose`, then copy {AreaFiles.Proposed} there and confirm it.", ExitCodes.NotFound);
            var a = Areas.Load(Cli.LoadGraph(ctx), ctx.Repo.Root);
            var file = AreaFiles.Parse(File.ReadAllText(path));
            var layout = AreaLayout.ByAreas(a, file);
            var (report, json) = AreaFiles.Check(a, file, layout);
            AreaFiles.WriteText(ctx.Repo.Root, AreaFiles.Json, CanonicalJson.Serialize(json));
            // D230: one page per area, specs/areas/<slug>/page.md; a stale page holding layer 4's words is kept and reported.
            // D285: with components.pages on, each page holds the developer page by code (D278–D284).
            var developer = StangentComponents.IsOn(ctx.Repo.Config, "pages");
            var (written, removed, unchanged, kept) = AreaPages.Write(ctx.Repo.Root, a, layout, developer ? PageCommands.Charts(ctx, a, layout) : null);
            report["pages"] = new JsonObject { ["written"] = written, ["removed"] = removed, ["unchanged"] = unchanged, ["developerPages"] = developer };
            foreach (var k in kept) report["problems"]!.AsArray().Add((JsonNode)$"{k} belongs to no area now but holds layer 4's words: move them, then delete it");
            if (kept.Count > 0) report["ok"] = false;
            ctx.Span.Attributes["stangent.artifact.count"] = (int)report["placed"]!;
            if (!(bool)report["ok"]!) throw new CheckFailed("areas_check_failed", "Areas failed a check: see problems.", report);
            return report;
        }));
        areas.Subcommands.Add(check);
    }
}

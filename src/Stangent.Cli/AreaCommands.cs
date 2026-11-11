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

        var propose = new Command("propose", "Propose areas from the graph and specs/flows/index.json; writes specs/areas.proposed.yaml, never specs/areas.yaml.");
        propose.SetAction(pr => Cli.Run(pr, "areas propose", ctx =>
        {
            var a = Areas.Load(Cli.LoadGraph(ctx), ctx.Repo.Root);
            var (file, placement) = AreaFiles.Propose(a);
            AreaFiles.WriteText(ctx.Repo.Root, AreaFiles.Proposed, AreaFiles.Render(file, a.Commit, placement));
            var counts = placement.AreaOf.Values.GroupBy(x => x, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
            ctx.Span.Attributes["stangent.artifact.count"] = file.Areas.Count;
            return new JsonObject
            {
                ["commit"] = a.Commit, ["file"] = AreaFiles.Proposed, ["startFlows"] = a.Starts.Count, ["menus"] = new JsonArray([.. a.KeptMenus.Select(m => (JsonNode)m)]),
                ["areas"] = new JsonArray([.. file.Areas.Select(x => (JsonNode)new JsonObject { ["name"] = x.Name, ["flows"] = counts.GetValueOrDefault(x.Name) })]),
                ["ties"] = file.Ties.Count, ["unplaced"] = placement.Uncovered.Count,
                ["large"] = new JsonArray([.. file.Large.Select(l => (JsonNode)new JsonObject { ["area"] = l.Area, ["flows"] = l.Flows })]),
            };
        }));
        areas.Subcommands.Add(propose);

        var check = new Command("check", "Check specs/areas.yaml: every start flow in exactly one area, nothing uncovered, every tie answered; writes specs/areas.json.");
        check.SetAction(pr => Cli.Run(pr, "areas check", ctx =>
        {
            var path = Path.Combine(ctx.Repo.Root, AreaFiles.Confirmed);
            if (!File.Exists(path))
                throw new StangentException("not_found", $"No {AreaFiles.Confirmed}: run `stangent areas propose`, then copy {AreaFiles.Proposed} there and confirm it.", ExitCodes.NotFound);
            var a = Areas.Load(Cli.LoadGraph(ctx), ctx.Repo.Root);
            var (report, json) = AreaFiles.Check(a, AreaFiles.Parse(File.ReadAllText(path)));
            AreaFiles.WriteText(ctx.Repo.Root, AreaFiles.Json, CanonicalJson.Serialize(json));
            ctx.Span.Attributes["stangent.artifact.count"] = (int)report["placed"]!;
            if (!(bool)report["ok"]!) throw new CheckFailed("areas_check_failed", "Areas failed a check: see problems.", report);
            return report;
        }));
        areas.Subcommands.Add(check);
    }
}

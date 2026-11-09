using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Flows;

/// <summary><c>stangent flows …</c>: rebuild layer 3's flows, laid out from the code graph by code (D170–D184, docs/flows.md).</summary>
static class FlowCommands
{
    public static void Add(RootCommand root)
    {
        var flows = new Command("flows", "Flows: what a click, a screen opening or a system job does, laid out from the code graph by code, no AI (D170–D184).");
        root.Subcommands.Add(flows);

        var build = new Command("build", "Build every flow from the graph and write specs/flows/ (D180): <id>.json, <id>.md and index.json.");
        build.SetAction(pr => Cli.Run(pr, "flows build", ctx =>
        {
            var graph = Cli.LoadGraph(ctx);
            var set = FlowBuilder.Build(graph, ctx.Repo.Root);
            var (written, removed, unchanged) = FlowFiles.Write(ctx.Repo.Root, set);
            ctx.Span.Attributes["stangent.artifact.count"] = set.Flows.Count;
            return new JsonObject
            {
                ["commit"] = set.Commit, ["folder"] = FlowFiles.Folder, ["flows"] = set.Flows.Count, ["skipped"] = set.Skipped.Count,
                ["byKind"] = new JsonObject([.. set.Flows.GroupBy(f => f.Kind).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => KeyValuePair.Create(x.Key, (JsonNode?)x.Count()))]),
                ["guesses"] = set.Flows.Sum(f => f.Guesses.Count), ["unknowns"] = set.Flows.Sum(f => f.Unknowns.Count),
                ["files"] = new JsonObject { ["written"] = written, ["removed"] = removed, ["unchanged"] = unchanged },
            };
        }));
        flows.Subcommands.Add(build);

        var idArg = new Argument<string>("id") { Description = "A flow's id, as index.json lists it." };
        var formatOption = new Option<string>("--format") { Description = "json (default) or md (prints the flow's Markdown).", DefaultValueFactory = _ => "json" };
        var show = new Command("show", "One flow from specs/flows/, as written by the last build.") { idArg, formatOption };
        show.SetAction(pr =>
        {
            var md = pr.GetValue(formatOption) == "md";
            return Cli.Run(pr, "flows show", ctx =>
            {
                var id = pr.GetValue(idArg)!;
                var path = Path.Combine(ctx.Repo.Root, FlowFiles.Folder, $"{id}.json");
                if (id.Contains('/') || id.Contains('\\') || !File.Exists(path))
                    throw new StangentException("not_found", $"No flow '{id}' in {FlowFiles.Folder}/ (run `stangent flows build`).", ExitCodes.NotFound);
                var flow = JsonNode.Parse(File.ReadAllText(path))!;
                return md ? new JsonObject { ["md"] = File.ReadAllText(Path.ChangeExtension(path, ".md")) } : flow;
            }, md ? r => (string)r!["md"]! : null);
        });
        flows.Subcommands.Add(show);

        var check = new Command("check", "D177's bar items 3–5 by code: every 🟡 has a reason and every ❓ says why; the files are byte for byte what the graph builds; no start is skipped silently.");
        check.SetAction(pr => Cli.Run(pr, "flows check", ctx =>
        {
            var report = FlowFiles.Check(ctx.Repo.Root, Cli.LoadGraph(ctx), new FlowSettings());
            ctx.Span.Attributes["stangent.artifact.count"] = (int)report["flows"]!;
            if (!(bool)report["ok"]!) throw new CheckFailed("flows_check_failed", "Flows failed a check: see problems.", report);
            return report;
        }));
        flows.Subcommands.Add(check);
    }
}

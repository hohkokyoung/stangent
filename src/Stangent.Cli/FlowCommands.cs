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

        var build = new Command("build", "Build every flow from the graph and write <id>.json and <id>.md under its area in specs/areas/ once specs/areas.yaml exists, else in specs/flows/, plus specs/flows/index.json (D180, D230).");
        build.SetAction(pr => Cli.Run(pr, "flows build", ctx =>
        {
            var graph = Cli.LoadGraph(ctx);
            var set = FlowBuilder.Build(graph, ctx.Repo.Root, Settings(ctx));
            var layout = AreaLayout.For(graph, ctx.Repo.Root, set);
            var (written, removed, unchanged) = FlowFiles.Write(ctx.Repo.Root, set, layout);
            ctx.Span.Attributes["stangent.artifact.count"] = set.Flows.Count;
            return new JsonObject
            {
                ["commit"] = set.Commit, ["folder"] = layout.ByArea ? AreaLayout.Root : FlowFiles.Folder, ["index"] = FlowFiles.IndexFile,
                ["layout"] = layout.ByArea ? Layout(layout) : "flat: no specs/areas.yaml yet",
                ["flows"] = set.Flows.Count, ["skipped"] = set.Skipped.Count,
                ["byKind"] = new JsonObject([.. set.Flows.GroupBy(f => f.Kind).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => KeyValuePair.Create(x.Key, (JsonNode?)x.Count()))]),
                ["guesses"] = set.Flows.Sum(f => f.Guesses.Count), ["unknowns"] = set.Flows.Sum(f => f.Unknowns.Count),
                ["files"] = new JsonObject { ["written"] = written, ["removed"] = removed, ["unchanged"] = unchanged },
            };
        }));
        flows.Subcommands.Add(build);

        var idArg = new Argument<string>("id") { Description = "A flow's id, as index.json lists it." };
        var formatOption = new Option<string>("--format") { Description = "json (default) or md (prints the flow's Markdown).", DefaultValueFactory = _ => "json" };
        var show = new Command("show", "One flow by id, from the file specs/flows/index.json maps it to, as written by the last build.") { idArg, formatOption };
        show.SetAction(pr =>
        {
            var md = pr.GetValue(formatOption) == "md";
            return Cli.Run(pr, "flows show", ctx =>
            {
                var id = pr.GetValue(idArg)!;
                // D230: the index maps every id to its file, wherever its area put it.
                var index = Path.Combine(ctx.Repo.Root, FlowFiles.IndexFile);
                var file = File.Exists(index)
                    ? JsonNode.Parse(File.ReadAllText(index))!["flows"]!.AsArray().FirstOrDefault(f => (string?)f!["id"] == id)?["file"]?.GetValue<string>()
                    : null;
                if (file is null && !id.Contains('/') && !id.Contains('\\') && File.Exists(Path.Combine(ctx.Repo.Root, FlowFiles.Folder, $"{id}.json"))) file = $"{FlowFiles.Folder}/{id}.json";   // an index from before D230
                var path = file is null ? "" : Path.Combine(ctx.Repo.Root, file);
                if (file is null || file.Contains("..", StringComparison.Ordinal) || !File.Exists(path))
                    throw new StangentException("not_found", $"No flow '{id}' in {FlowFiles.IndexFile} (run `stangent flows build`).", ExitCodes.NotFound);
                var flow = JsonNode.Parse(File.ReadAllText(path))!;
                return md ? new JsonObject { ["md"] = File.ReadAllText(Path.ChangeExtension(path, ".md")) } : flow;
            }, md ? r => (string)r!["md"]! : null);
        });
        flows.Subcommands.Add(show);

        var check = new Command("check", "D177's bar items 3–5 by code: every 🟡 has a reason and every ❓ says why; the files are byte for byte what the graph builds; no start is skipped silently.");
        check.SetAction(pr => Cli.Run(pr, "flows check", ctx =>
        {
            var report = FlowFiles.Check(ctx.Repo.Root, Cli.LoadGraph(ctx), Settings(ctx));
            ctx.Span.Attributes["stangent.artifact.count"] = (int)report["flows"]!;
            if (!(bool)report["ok"]!) throw new CheckFailed("flows_check_failed", "Flows failed a check: see problems.", report);
            return report;
        }));
        flows.Subcommands.Add(check);

        var topOption = new Option<int>("--top") { Description = "How many lines to list, most start flows touched first (default 20).", DefaultValueFactory = _ => 20 };
        var causeOption = new Option<string?>("--cause") { Description = "List every line of one cause (an id from byCause) instead of the top lines." };
        var guesses = new Command("guesses", "Step 6 (D235–D237): every 🟡 and ❓ line on a flow, by code line, in a cause and one of D166's four buckets by a fixed table, ranked by start flows touched; fails when a line fits no cause or a reason sits on the wrong kind of step.") { topOption, causeOption };
        guesses.SetAction(pr => Cli.Run(pr, "flows guesses", ctx =>
        {
            var set = FlowBuilder.Build(Cli.LoadGraph(ctx), ctx.Repo.Root, Settings(ctx));
            var report = GuessReport.Report(set, pr.GetValue(topOption), pr.GetValue(causeOption));
            ctx.Span.Attributes["stangent.artifact.count"] = (int)report["guessed"]!["total"]!["lines"]! + (int)report["unknown"]!["total"]!["lines"]!;
            if (!(bool)report["ok"]!) throw new CheckFailed("flows_guesses_failed", "A 🟡 or ❓ line fits no cause, or its reason sits on the wrong kind of step: see problems.", report);
            return report;
        }));
        flows.Subcommands.Add(guesses);
    }

    /// <summary>What <c>flows build</c> says about a layout by area (D230): folders, files per area, the shared folder, flows in no area.</summary>
    /// <summary>The walk's settings from <c>stangent.yaml</c> (D302's outside libraries).</summary>
    static FlowSettings Settings(CliContext ctx) => new() { OutsideLibraries = [.. ctx.Repo.Config.Flows.OutsideLibraries] };

    static JsonObject Layout(AreaLayout layout)
    {
        var byFolder = layout.FolderOf.Values.GroupBy(x => x, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        return new JsonObject
        {
            ["areas"] = layout.SlugOf.Count,
            ["folders"] = byFolder.Count,
            ["shared"] = byFolder.GetValueOrDefault($"{AreaLayout.Root}/{AreaLayout.SharedSlug}"),
            ["unplaced"] = new JsonArray([.. layout.NotPlaced.Select(x => (JsonNode)x)]),
            ["flowsByFolder"] = new JsonObject([.. byFolder.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => KeyValuePair.Create(x.Key, (JsonNode?)x.Value))]),
        };
    }
}

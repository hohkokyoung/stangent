using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Flows;

/// <summary><c>stangent pages …</c>: layer 4, each area's developer page by code (D278–D285, docs/pages.md), and the item summary layer 5 reads (D251–D253).</summary>
static class PageCommands
{
    public static void Add(RootCommand root)
    {
        var pages = new Command("pages", "Pages: each area's developer page by code, no AI (D278–D285), written into specs/areas/<area>/page.md by `stangent areas check` while components.pages is on; and the item summary layer 5 reads.");
        root.Subcommands.Add(pages);

        var areaArg = new Argument<string>("area") { Description = "The area's name as specs/areas.yaml has it (\"Series › Add New\"), or its folder (series-add-new)." };
        var proposedOption = new Option<bool>("--proposed") { Description = "Place flows by specs/areas.proposed.yaml instead (a look before areas.yaml is confirmed; open ties leave their flows out)." };
        var formatOption = new Option<string>("--format") { Description = "json (default), md (the items as a person reads them) or counts (only how many).", DefaultValueFactory = _ => "json" };

        var summary = new Command("summary", "The area's summary by code, no AI (D251–D253): its sections in page order, each with its numbered items and every condition on the way to them. The same flows and areas.yaml give the same bytes.") { areaArg, proposedOption, formatOption };
        summary.SetAction(pr =>
        {
            var format = pr.GetValue(formatOption);
            return Cli.Run(pr, "pages summary", ctx =>
            {
                var s = Load(ctx, pr.GetValue(areaArg)!, pr.GetValue(proposedOption));
                ctx.Span.Attributes["stangent.artifact.count"] = s.Sections.Count;
                return format switch
                {
                    "md" => new JsonObject { ["md"] = PageSummary.Markdown(s) },
                    "counts" => Counts(s),
                    _ => s.ToJson(),
                };
            }, format == "md" ? r => (string)r!["md"]! : null);
        });
        pages.Subcommands.Add(summary);

        var settingsNote = "Settings: pages.frameworkShare (D281, default 10) and pages.chartCap (D282, D286, default 60) in stangent.yaml.";
        var chartFormatOption = new Option<string>("--format") { Description = "md (default: the area's developer page body, as page.md holds it), counts (a Markdown table of nodes per chart) or json.", DefaultValueFactory = _ => "md" };
        var chart = new Command("chart", $"The area's developer page by code, no AI (D278–D284): per changing click a flowchart (up to the chart cap), a scenario table and its lists, with flow-file step numbers; display-only flows one line each. Writes nothing; works with components.pages off. {settingsNote}") { areaArg, proposedOption, chartFormatOption };
        chart.SetAction(pr =>
        {
            var format = pr.GetValue(chartFormatOption);
            return Cli.Run(pr, "pages chart", ctx =>
            {
                var (a, layout, name, folder) = FindLaid(ctx, pr.GetValue(areaArg)!, pr.GetValue(proposedOption));
                var c = PageChart.Build(ctx.Repo.Root, a, layout.Placement!, name, folder, PageSettings.From(ctx.Repo.Config), layout: layout);
                ctx.Span.Attributes["stangent.artifact.count"] = c.Charts.Count;
                return format switch
                {
                    "json" => PageChart.ToJson(c),
                    "counts" => new JsonObject { ["md"] = PageChart.Counts(c) },
                    _ => new JsonObject { ["md"] = PageChart.Note(c) + PageChart.Body(c) },
                };
            }, format == "json" ? null : r => (string)r!["md"]!);
        });
        pages.Subcommands.Add(chart);

        var verifyAreaArg = new Argument<string?>("area") { Description = "One area, by name or folder; every area when left out.", Arity = ArgumentArity.ZeroOrOne };
        var verify = new Command("verify", "Gate 1 of D280, free: every change and every stop of each changing click's flow file is on its area's page, matched by step number (D284); the page rebuilds byte for byte and is what's on disk. Needs components.pages on (`stangent areas check` writes the pages).") { verifyAreaArg };
        verify.SetAction(pr => Cli.Run(pr, "pages verify", ctx =>
        {
            if (!StangentComponents.IsOn(ctx.Repo.Config, "pages"))
                throw new StangentException("component_off", "pages is off, so no area has a developer page to check. Turn on `components.pages` in stangent.yaml, then run `stangent areas check`.", ExitCodes.Usage);
            var (a, layout) = Layout(ctx);
            var report = PageGate.Verify(ctx.Repo.Root, a, layout, PageSettings.From(ctx.Repo.Config), pr.GetValue(verifyAreaArg));
            ctx.Span.Attributes["stangent.artifact.count"] = report["areas"]!.AsArray().Count;
            if (!(bool)report["ok"]!) throw new CheckFailed("pages_verify_failed", "A page is missing a change or a stop, doesn't rebuild byte for byte, or isn't current on disk: see areas.", report);
            return report;
        }));
        pages.Subcommands.Add(verify);
    }

    /// <summary>Each area's developer page for <c>areas check</c> (D285), the flow files read once for all of them.</summary>
    public static Func<string, string, AreaCharts> Charts(CliContext ctx, Areas a, AreaLayout layout)
        => PageChart.Pages(ctx.Repo.Root, a, layout, PageSettings.From(ctx.Repo.Config));

    /// <summary>The flows and their layout by specs/areas.yaml.</summary>
    static (Areas, AreaLayout) Layout(CliContext ctx)
    {
        var path = Path.Combine(ctx.Repo.Root, AreaFiles.Confirmed);
        if (!File.Exists(path))
            throw new StangentException("not_found", $"No {AreaFiles.Confirmed}: confirm the areas first (`stangent areas propose`, then copy it there).", ExitCodes.NotFound);
        var a = Areas.Load(Cli.LoadGraph(ctx), ctx.Repo.Root);
        return (a, AreaLayout.ByAreas(a, AreaFiles.Parse(File.ReadAllText(path))));
    }

    static JsonObject Counts(AreaSummary s)
    {
        var c = s.Counts();
        c["area"] = s.Area;
        c["folder"] = s.Folder;
        c["perSection"] = new JsonArray([.. s.Sections.Select(x => (JsonNode)new JsonObject
        {
            ["section"] = x.Id, ["title"] = x.Title, ["flows"] = x.Flows.Count, ["items"] = x.Items.Count, ["mustCite"] = x.Items.Count(i => i.MustCite),
            ["chars"] = x.Items.Sum(i => PageSummary.ItemLine(i, true).Length),   // what the writer reads of it, before the rules
        })]);
        c["chars"] = s.Sections.Sum(x => x.Items.Sum(i => PageSummary.ItemLine(i, true).Length));
        c["missing"] = new JsonArray([.. s.Missing.Select(m => (JsonNode)m)]);
        return c;
    }

    /// <summary>The area's summary, placed by specs/areas.yaml (or the proposal); the area found by its name or folder.</summary>
    static AreaSummary Load(CliContext ctx, string area, bool proposed)
    {
        var (a, placement, name, folder) = Find(ctx, area, proposed);
        return PageSummary.Build(ctx.Repo.Root, a, placement, name, folder);
    }

    /// <summary>The flows, their placement, and the area's name and folder, from specs/areas.yaml (or the proposal).</summary>
    static (Areas, Placement, string, string) Find(CliContext ctx, string area, bool proposed)
    {
        var (a, layout, name, folder) = FindLaid(ctx, area, proposed);
        return (a, layout.Placement!, name, folder);
    }

    /// <summary>As <see cref="Find"/>, with the whole layout (D314: the page links its parts and receivers by it).</summary>
    static (Areas, AreaLayout, string, string) FindLaid(CliContext ctx, string area, bool proposed)
    {
        var name = proposed ? AreaFiles.Proposed : AreaFiles.Confirmed;
        var path = Path.Combine(ctx.Repo.Root, name);
        if (!File.Exists(path))
            throw new StangentException("not_found", proposed ? $"No {name}: run `stangent areas propose`." : $"No {name}: confirm the areas first (`stangent areas propose`, then copy it there), or pass --proposed for a look.", ExitCodes.NotFound);
        var a = Areas.Load(Cli.LoadGraph(ctx), ctx.Repo.Root);
        var file = AreaFiles.Parse(File.ReadAllText(path));
        var layout = AreaLayout.ByAreas(a, file);
        var match = layout.SlugOf.FirstOrDefault(x => x.Key == area || x.Value == area);
        if (match.Key is null)
        {
            var near = layout.SlugOf.Keys.Where(k => k.Contains(area, StringComparison.OrdinalIgnoreCase)).Take(5).ToList();
            throw new StangentException("not_found", $"No area '{area}' in {name}.{(near.Count > 0 ? $" Did you mean: {string.Join(", ", near)}?" : "")}", ExitCodes.NotFound);
        }
        return (a, layout, match.Key, match.Value);
    }
}

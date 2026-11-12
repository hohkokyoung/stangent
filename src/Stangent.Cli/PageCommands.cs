using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Flows;
using Stangent.Spec.Words;

/// <summary><c>stangent pages …</c>: rebuild layer 4, each area's page in plain words (D251–D258, docs/pages.md).</summary>
static class PageCommands
{
    public static void Add(RootCommand root)
    {
        var pages = new Command("pages", "Pages: each area's page in plain words for people. Code cuts every flow into numbered items; an AI words one section per call; code checks every sentence (D251–D258).");
        root.Subcommands.Add(pages);

        var areaArg = new Argument<string>("area") { Description = "The area's name as specs/areas.yaml has it (\"Series › Add New\"), or its folder (series-add-new)." };
        var sectionArg = new Argument<string>("section") { Description = "The section's id, as `pages sections` lists it." };
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

        var sections = new Command("sections", "Each section of the area's page and its state against its current items: new, changed, checked, fallback. \"In short\" is last; the batch driver runs it alone after the others.") { areaArg };
        sections.SetAction(pr => Cli.Run(pr, "pages sections", ctx => AreaWords.Sections(ctx.Repo, Load(ctx, pr.GetValue(areaArg)!, false))));
        pages.Subcommands.Add(sections);

        var retryFailedOption = new Option<bool>("--retry-failed") { Description = "Try again a section that kept the code's list with these same items." };
        var prepare = new Command("prepare", "Write one section's bundle and say what runs next: nothing when its items, rules and model are unchanged (D254), else the writer. Off unless components.pages is on.") { areaArg, sectionArg, retryFailedOption };
        prepare.SetAction(pr => Cli.Run(pr, "pages prepare", ctx =>
        {
            var s = Load(ctx, pr.GetValue(areaArg)!, false);
            return SpecCommands.Finish(AreaWords.Prepare(ctx.Repo, s, pr.GetValue(sectionArg)!, pr.GetValue(retryFailedOption)), $"{s.Area}#{pr.GetValue(sectionArg)}");
        }));
        pages.Subcommands.Add(prepare);

        var usageOption = new Option<string?>("--usage") { Description = "Claude Code's JSON result for the step just run." };
        var check = new Command("check", "Check the section just written (every sentence cites an item, 🟡/❓ hedged, every condition marked, every must-cite item covered), one retry, else keep the code's list; record it in specs/pages-runs.json and rewrite the page's words.") { areaArg, sectionArg, usageOption };
        check.SetAction(pr => Cli.Run(pr, "pages check", ctx =>
        {
            var s = Load(ctx, pr.GetValue(areaArg)!, false);
            return SpecCommands.Finish(AreaWords.Check(ctx.Repo, s, pr.GetValue(sectionArg)!, SpecCommands.ReadUsage(ctx, pr.GetValue(usageOption))), $"{s.Area}#{pr.GetValue(sectionArg)}");
        }));
        pages.Subcommands.Add(check);
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
        return PageSummary.Build(ctx.Repo.Root, a, layout.Placement!, match.Key, match.Value);
    }
}

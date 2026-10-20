using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Spec;
using Stangent.Spec.Pages;

/// <summary><c>stangent spec …</c>: reference pages from the code graph (Phase 2a, D54–D55, D60).</summary>
static class SpecCommands
{
    public static void Add(RootCommand root)
    {
        var spec = new Command("spec", "Reference pages generated from the code graph (D52–D55).");
        root.Subcommands.Add(spec);

        var depthOption = new Option<int?>("--depth") { Description = "Override spec.depth from stangent.yaml: how many lines deep each slice walks." };
        var pages = new Command("pages", "List every page the graph calls for: screens, shared components, backend entry points (D54).") { depthOption };
        pages.SetAction(pr => Cli.Run(pr, "spec pages", ctx =>
        {
            var set = Load(ctx, pr.GetValue(depthOption));
            ctx.Span.Attributes["stangent.artifact.count"] = set.Pages.Count;
            return new JsonObject
            {
                ["depth"] = set.Settings.Depth,
                ["counts"] = new JsonObject
                {
                    ["screen"] = set.Pages.Count(p => p.Kind == PageKind.Screen),
                    ["component"] = set.Pages.Count(p => p.Kind == PageKind.Component),
                    ["backend"] = set.Pages.Count(p => p.Kind == PageKind.Backend),
                },
                ["pages"] = new JsonArray([.. set.Pages.Select(p =>
                {
                    var bundle = Bundles.Build(set, p, null);
                    var slice = set.Slices[p.Id];
                    return (JsonNode)new JsonObject
                    {
                        ["id"] = p.Id, ["kind"] = p.Kind, ["title"] = p.Title, ["group"] = p.Group, ["path"] = p.Path,
                        ["points"] = slice.Nodes.Count, ["chars"] = bundle.Chars, ["overCap"] = bundle.Chars > set.Settings.MaxBundleChars,
                        ["unknowns"] = slice.Unknowns.Count, ["cutOff"] = slice.CutOff,
                        ["endpoints"] = slice.Nodes.Count(n => n.Node.Kind == Stangent.Graph.NodeKind.Endpoint),
                    };
                })]),
            };
        }));
        spec.Subcommands.Add(pages);

        var pageArg = new Argument<string>("page") { Description = "A page id (page:/series/:id), its path under specs/reference, or its entry point's graph id." };
        var bundle = new Command("bundle", "Print a page's bundle as the writer would read it (for looking, not for AI runs).") { pageArg, depthOption };
        bundle.SetAction(pr => Cli.Run(pr, "spec bundle", ctx =>
        {
            var set = Load(ctx, pr.GetValue(depthOption));
            var page = set.Find(pr.GetValue(pageArg)!) ?? throw new Stangent.Core.StangentException("not_found", $"No page '{pr.GetValue(pageArg)}'.", Stangent.Core.ExitCodes.NotFound);
            return new JsonObject { ["text"] = Bundles.Build(set, page, null).Text };
        }, r => (string)r!["text"]!));
        spec.Subcommands.Add(bundle);

        var retryFailedOption = new Option<bool>("--retry-failed") { Description = "Try again a page that failed with these same inputs." };
        var prepare = new Command("prepare", "Write a page's bundle and say what runs next: nothing if its inputs are unchanged (D18), else the writer (D60).") { pageArg, retryFailedOption };
        prepare.SetAction(pr => Cli.Run(pr, "spec prepare", ctx =>
        {
            var (set, page) = LoadPage(ctx, pr.GetValue(pageArg)!);
            var step = PageRun.Prepare(set, page, ctx.Repo, pr.GetValue(retryFailedOption));
            ctx.Span.Attributes["stangent.artifact.count"] = step.Action == "none" ? 0 : 1;
            return Finish(step, page);
        }));
        spec.Subcommands.Add(prepare);

        var usageOption = new Option<string?>("--usage") { Description = "Claude Code's JSON result for the step just run (claude -p --output-format json): tokens, cost, model, time." };
        var check = new Command("check", "Check the output of the step just run (writer: schema and citations; verifier: evidence), record the page when it passes, and say what runs next.") { pageArg, usageOption };
        check.SetAction(pr => Cli.Run(pr, "spec check", ctx =>
        {
            var (set, page) = LoadPage(ctx, pr.GetValue(pageArg)!);
            var usage = pr.GetValue(usageOption) is { } file ? Usage.FromClaudeResult(Cli.ReadJson(file)) : null;
            if (usage is not null)
            {
                if (usage.Model is { } model) ctx.Span.Attributes["gen_ai.request.model"] = model;
                ctx.Span.Attributes["gen_ai.provider.name"] = "anthropic";
                ctx.Span.Attributes["gen_ai.usage.input_tokens"] = usage.InputTokens + usage.CacheReadTokens + usage.CacheWriteTokens;
                ctx.Span.Attributes["gen_ai.usage.output_tokens"] = usage.OutputTokens;
            }
            var step = PageRun.Check(set, page, ctx.Repo, usage);
            ctx.Span.Attributes["stangent.artifact.count"] = step.Status == "recorded" ? 1 : 0;
            return Finish(step, page);
        }));
        spec.Subcommands.Add(check);

        var diffOption = new Option<string?>("--diff") { Description = "The PR's range (base..head): lists the changed files inside each stale page's slice." };
        var formatOption = new Option<string>("--format") { Description = "json (default) or md (the list for a PR comment).", DefaultValueFactory = _ => "json" };
        var stale = new Command("stale", "Pages whose inputs changed since they were recorded, with new and removed pages (D55). Code only, no AI.") { diffOption, formatOption };
        stale.SetAction(pr =>
        {
            var markdown = pr.GetValue(formatOption) == "md";
            return Cli.Run(pr, "spec stale", ctx =>
            {
                var set = Load(ctx, null);
                var report = Staleness.Report(set, new SpecPaths(ctx.Repo.Root), pr.GetValue(diffOption));
                ctx.Span.Attributes["stangent.artifact.count"] = (int)report["counts"]!["stale"]! + (int)report["counts"]!["new"]!;
                return report;
            }, markdown ? r => Staleness.Markdown(r!.AsObject()) : null);
        });
        spec.Subcommands.Add(stale);
    }

    static (PageSet, PageDef) LoadPage(CliContext ctx, string id)
    {
        var set = Load(ctx, null);
        var page = set.Find(id) ?? throw new Stangent.Core.StangentException("not_found", $"No page '{id}'. `stangent spec pages` lists them.", Stangent.Core.ExitCodes.NotFound);
        return (set, page);
    }

    /// <summary>A step that ends the page as failed, or asks for a retry, is a check that found problems: exit 1, with the next step in result.</summary>
    static JsonNode Finish(RunStep step, PageDef page)
    {
        var json = step.ToJson(page.Id);
        if (step.Status == "retry")
            throw new CheckFailed("page_check_failed", $"{page.Id}: the code check failed; the writer gets one retry with the errors.", json);
        if (step.Status == "failed")
            throw new CheckFailed("page_failed", $"{page.Id} failed: {step.Errors?.FirstOrDefault()}", json);
        return json;
    }

    static PageSet Load(CliContext ctx, int? depth) =>
        PageList.Build(Cli.LoadGraph(ctx), ctx.Repo.Root, SpecSettings.From(ctx.Repo.Config, depth));
}

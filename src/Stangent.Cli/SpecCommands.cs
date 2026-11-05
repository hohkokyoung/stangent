using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Spec;
using Stangent.Spec.Checks;
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

        var assembleFile = new Argument<string>("file") { Description = "A step's input file, as `next.prompt` names it." };
        var assembleInPlace = new Option<bool>("--in-place") { Description = "Write the full text back to the file, for a subagent that reads the file itself." };
        var assembleBatch = new Option<bool>("--for-batch") { Description = "This call also sends the schema through Claude Code's option: with schema-once on, it's left out of the text (J.4)." };
        var assemble = new Command("assemble", "Print a step's whole input: its stored file with the shared instructions and schema put back, byte for byte (D130). Any other file comes back as it is.") { assembleFile, assembleInPlace, assembleBatch };
        assemble.SetAction(pr => Cli.Run(pr, "spec assemble", ctx =>
        {
            var file = pr.GetValue(assembleFile)!;
            // A step's "previous attempt" may be missing when the agent wrote nothing: an empty input part, as `cat` gave.
            var stored = File.Exists(file) ? File.ReadAllText(file) : "";
            var once = pr.GetValue(assembleBatch) && ctx.HasRepo && Stangent.Core.StangentComponents.IsOn(ctx.Repo.Config, "schema-once");
            var text = SharedHead.Assemble(stored, once);
            if (pr.GetValue(assembleInPlace) && !ReferenceEquals(text, stored)) File.WriteAllText(file, text);
            return new JsonObject { ["text"] = pr.GetValue(assembleInPlace) ? "" : text };
        }, r => (string)r!["text"]!));
        spec.Subcommands.Add(assemble);

        var schemaArg = new Argument<string>("name") { Description = "A step's output schema, as `next.schema` names it (page-writer-output.v1.json)." };
        var schema = new Command("schema", "Print an AI step's output schema (for drivers that ask Claude Code for structured output, D66).") { schemaArg };
        schema.SetAction(pr => Cli.Run(pr, "spec schema", ctx =>
            new JsonObject { ["text"] = Stangent.Core.SchemaRegistry.EmbeddedText(pr.GetValue(schemaArg)!) }, r => (string)r!["text"]!));
        spec.Subcommands.Add(schema);

        var citePage = new Argument<string?>("page") { Description = "One recorded page's id or path; every recorded page if left out.", Arity = ArgumentArity.ZeroOrOne };
        var cite = new Command("cite", "Fill recorded pages' line citations from the graph and make their statement ids permanent. Code only: no AI, and no page's JSON changes (2.3).") { citePage };
        cite.SetAction(pr => Cli.Run(pr, "spec cite", ctx =>
        {
            var report = PageLines.Refresh(ctx.Repo.Root, Cli.LoadGraph(ctx), pr.GetValue(citePage), Stangent.Core.StangentComponents.IsOn(ctx.Repo.Config, "line-citations"), ctx.Repo.Config);
            ctx.Span.Attributes["stangent.artifact.count"] = (int)report["pages"]!;
            return report;
        }));
        spec.Subcommands.Add(cite);

        var correctStatement = new Argument<string>("statement") { Description = "The statement's id on the page." };
        var correctText = new Option<string?>("--text") { Description = "The words it should say." };
        var correctRemove = new Option<bool>("--remove") { Description = "The statement shouldn't be there at all." };
        var correctWhy = new Option<string>("--why") { Description = "Why, in a person's words.", Required = true };
        var correctBy = new Option<string>("--by") { Description = "Who says so.", Required = true };
        var correct = new Command("correct", "Record a person's correction to a page statement (T.1). It takes effect once approved, and is put back every time the page is written. No AI.") { pageArg, correctStatement, correctText, correctRemove, correctWhy, correctBy };
        correct.SetAction(pr => Cli.Run(pr, "spec correct", ctx =>
        {
            var text = pr.GetValue(correctText);
            if ((text is null) != pr.GetValue(correctRemove)) throw new Stangent.Core.StangentException("usage", "Give --text or --remove, not both.", Stangent.Core.ExitCodes.Usage);
            return PageCorrections.Propose(Load(ctx, null), ctx.Repo, pr.GetValue(pageArg)!, pr.GetValue(correctStatement)!, text, pr.GetValue(correctWhy)!, pr.GetValue(correctBy)!);
        }));
        spec.Subcommands.Add(correct);
        var correctionId = new Argument<string>("correction") { Description = "A correction's id, C-0001." };
        var approveCorrection = new Command("correct-approve", "A person approves a correction: the page says it from now on, with no AI run.") { correctionId, correctBy };
        approveCorrection.SetAction(pr => Cli.Run(pr, "spec correct-approve", ctx => PageCorrections.Approve(Load(ctx, null), ctx.Repo, pr.GetValue(correctionId)!, pr.GetValue(correctBy)!)));
        spec.Subcommands.Add(approveCorrection);
        var rejectCorrection = new Command("correct-reject", "Refuse a proposed correction, or withdraw an approved one (the page keeps its words until it's next written).") { correctionId, correctBy, correctWhy };
        rejectCorrection.SetAction(pr => Cli.Run(pr, "spec correct-reject", ctx => PageCorrections.Reject(ctx.Repo, pr.GetValue(correctionId)!, pr.GetValue(correctBy)!, pr.GetValue(correctWhy)!)));
        spec.Subcommands.Add(rejectCorrection);
        var correctionsPage = new Argument<string?>("page") { Description = "Only this page's.", Arity = ArgumentArity.ZeroOrOne };
        var corrections = new Command("corrections", "List the corrections, saying which the code moved under and which couldn't be put back. No AI.") { correctionsPage };
        corrections.SetAction(pr => Cli.Run(pr, "spec corrections", ctx => PageCorrections.Report(Load(ctx, null), ctx.Repo.Root, pr.GetValue(correctionsPage))));
        spec.Subcommands.Add(corrections);

        var excludeWhy = new Option<string>("--why") { Description = "Why this isn't a page, in a person's words.", Required = true };
        var exclude = new Command("exclude", "Say a page the graph calls for isn't one, with a reason: a tooltip the graph took for a screen, say (T.3). No AI.") { pageArg, excludeWhy, correctBy };
        exclude.SetAction(pr => Cli.Run(pr, "spec exclude", ctx => PageExclusions.Exclude(Load(ctx, null), ctx.Repo, pr.GetValue(pageArg)!, pr.GetValue(excludeWhy)!, pr.GetValue(correctBy)!)));
        spec.Subcommands.Add(exclude);
        var include = new Command("include", "Take an exclusion back: the page is called for again. No AI.") { pageArg };
        include.SetAction(pr => Cli.Run(pr, "spec include", ctx => PageExclusions.Include(ctx.Repo, pr.GetValue(pageArg)!)));
        spec.Subcommands.Add(include);
        var coverageStrict = new Option<bool>("--strict") { Description = "Exit 1 if anything is neither written nor excluded." };
        var coverage = new Command("coverage", "Every screen, shared component and backend entry point: written, excluded with a reason, or neither (T.3). No AI.") { coverageStrict };
        coverage.SetAction(pr => Cli.Run(pr, "spec coverage", ctx =>
        {
            var report = PageExclusions.Coverage(Load(ctx, null), new SpecPaths(ctx.Repo.Root));
            ctx.Span.Attributes["stangent.artifact.count"] = (int)report["counts"]!["uncovered"]!;
            if (pr.GetValue(coverageStrict) && (int)report["counts"]!["uncovered"]! > 0) throw new CheckFailed("not_covered", $"{report["counts"]!["uncovered"]} page(s) are neither written nor excluded.", report);
            return report;
        }));
        spec.Subcommands.Add(coverage);

        var nav = new Command("nav", "Write specs/reference/index.md, every written page by kind and group (T.7). `spec cite` puts the links and footers on pages written earlier. No AI.");
        nav.SetAction(pr => Cli.Run(pr, "spec nav", ctx => new JsonObject { ["pages"] = PageNavigation.Write(new SpecPaths(ctx.Repo.Root)), ["file"] = "specs/reference/index.md" }));
        spec.Subcommands.Add(nav);

        var follow = new Command("follow", "Pages whose code only moved: their citations and saved hash follow it, with no AI (I.4). Off unless follow-moves is on.");
        follow.SetAction(pr => Cli.Run(pr, "spec follow", ctx => PageMoves.Run(Load(ctx, null), ctx.Repo.Config, new SpecPaths(ctx.Repo.Root))));
        spec.Subcommands.Add(follow);

        var neededDoc = new Option<string?>("--doc") { Description = "The pages this document's sections have as candidates." };
        var neededChapter = new Option<string?>("--chapter") { Description = "With --doc: only this top-level heading." };
        var neededTicket = new Option<string?>("--ticket") { Description = "The pages this ticket's bundle says it's missing. `ticket bundle` first." };
        var needed = new Command("needed", "The pages not written yet that a document or a ticket needs (I.1): what `spec-run.sh --for-fsd` and `--for-ticket` make. No AI.") { neededDoc, neededChapter, neededTicket };
        needed.SetAction(pr => Cli.Run(pr, "spec needed", ctx =>
        {
            var (doc, ticket) = (pr.GetValue(neededDoc), pr.GetValue(neededTicket));
            if ((doc is null) == (ticket is null)) throw new Stangent.Core.StangentException("usage", "Give --doc or --ticket.", Stangent.Core.ExitCodes.Usage);
            var set = Load(ctx, null);
            JsonObject result;
            if (doc is not null) result = Stangent.Spec.Fsd.PagesNeeded.ForDocument(set, Stangent.Spec.Trace.SpecIndex.Load(ctx.Repo.Root), ctx.Repo.Root, doc, pr.GetValue(neededChapter));
            else
            {
                var file = new Stangent.Tickets.TicketPaths(ctx.Repo.Root, ticket!).BundleJson;
                if (PageRun.LoadJson(file) is not JsonObject saved) throw new Stangent.Core.StangentException("not_found", $"No bundle for {ticket}. Run `stangent ticket bundle {ticket}` first.", Stangent.Core.ExitCodes.NotFound);
                result = Stangent.Spec.Fsd.PagesNeeded.Listed(set, (saved["missingPages"] as JsonArray ?? []).Select(p => (string)p!));
            }
            ctx.Span.Attributes["stangent.artifact.count"] = result["pages"]!.AsArray().Count;
            return result;
        }));
        spec.Subcommands.Add(needed);

        var retryFailedOption = new Option<bool>("--retry-failed") { Description = "Try again a page that failed with these same inputs." };
        var prepare = new Command("prepare", "Write a page's bundle and say what runs next: nothing if its inputs are unchanged (D18), else the writer (D60).") { pageArg, retryFailedOption };
        prepare.SetAction(pr => Cli.Run(pr, "spec prepare", ctx =>
        {
            var (set, page) = LoadPage(ctx, pr.GetValue(pageArg)!);
            var step = PageRun.Prepare(set, page, ctx.Repo, pr.GetValue(retryFailedOption));
            ctx.Span.Attributes["stangent.artifact.count"] = step.Action == "none" ? 0 : 1;
            return Finish(step, page.Id);
        }));
        spec.Subcommands.Add(prepare);

        var usageOption = new Option<string?>("--usage") { Description = "Claude Code's JSON result for the step just run (claude -p --output-format json): tokens, cost, model, time." };
        var check = new Command("check", "Check the output of the step just run (writer: schema and citations; verifier: evidence), record the page when it passes, and say what runs next.") { pageArg, usageOption };
        check.SetAction(pr => Cli.Run(pr, "spec check", ctx =>
        {
            var (set, page) = LoadPage(ctx, pr.GetValue(pageArg)!);
            var usage = ReadUsage(ctx, pr.GetValue(usageOption));
            var step = PageRun.Check(set, page, ctx.Repo, usage);
            ctx.Span.Attributes["stangent.artifact.count"] = step.Status == "recorded" ? 1 : 0;
            return Finish(step, page.Id);
        }));
        spec.Subcommands.Add(check);

        var questionArg = new Argument<string>("question") { Description = "The question, as one argument." };
        var ask = new Command("ask", "Ask how the app works today, from the reference pages (E.2, D124): code picks the pages and says what runs next, nothing when it's been answered. Off unless spec.ask is on.") { questionArg };
        ask.SetAction(pr => Cli.Run(pr, "spec ask", ctx =>
        {
            var question = pr.GetValue(questionArg)!;
            return Answered(PageAsk.Prepare(ctx.Repo, question), question);
        }));
        spec.Subcommands.Add(ask);
        var askCheck = new Command("ask-check", "Check the answer just written: every statement id on a page in the bundle, every sentence citing one. Adds the code's lines. With nothing cited it says the pages don't cover this.") { questionArg, usageOption };
        askCheck.SetAction(pr => Cli.Run(pr, "spec ask-check", ctx =>
        {
            var question = pr.GetValue(questionArg)!;
            var done = PageAsk.Check(ctx.Repo, question, ReadUsage(ctx, pr.GetValue(usageOption)));
            if (done.Answer is { } answer) NoteBehind(ctx, answer);
            return Answered(done, question);
        }));
        spec.Subcommands.Add(askCheck);

        var readFormat = new Option<string>("--format") { Description = "json (default) or md (the page's Markdown, under the warning).", DefaultValueFactory = _ => "json" };
        var read = new Command("read", "Print a recorded page, with a warning at the top when it's behind the code (T.4). Code only, no AI.") { pageArg, readFormat };
        read.SetAction(pr => Cli.Run(pr, "spec read", ctx =>
        {
            var (set, page) = LoadPage(ctx, pr.GetValue(pageArg)!);
            var paths = new SpecPaths(ctx.Repo.Root);
            if (PageRun.LoadJson(paths.PageJson(page)) is not JsonObject recorded || !File.Exists(paths.PageMarkdown(page)))
                throw new Stangent.Core.StangentException("not_found", $"Page '{page.Id}' isn't written yet. `spec-run.sh` writes it.", Stangent.Core.ExitCodes.NotFound);
            var written = (string?)recorded["generated"]?["commit"];
            var behind = Staleness.Behind(set, paths, page);
            var warning = behind ? Staleness.BehindWarning(page.Id, written, set.Commit) : null;
            if (warning is not null) Console.Error.WriteLine("warning: " + warning);
            return new JsonObject
            {
                ["id"] = page.Id, ["path"] = paths.Relative(paths.PageMarkdown(page)), ["behind"] = behind, ["warning"] = warning, ["writtenAt"] = written, ["codeAt"] = set.Commit,
                ["markdown"] = File.ReadAllText(paths.PageMarkdown(page)),
            };
        }, pr.GetValue(readFormat) == "md" ? r => ((string?)r!["warning"] is { } w ? $"> **{w}**\n\n" : "") + (string)r["markdown"]! : null));
        spec.Subcommands.Add(read);

        var contentCheck = new Command("content-check", "Broken links and duplicate ids in the generated pages, links, documents and requirements (T.6). Code only, no AI. Exit 1 when it finds any.");
        contentCheck.SetAction(pr => Cli.Run(pr, "spec content-check", ctx =>
        {
            // The graph tells a page not written yet from a broken link; without one, every missing target is broken.
            IReadOnlySet<string>? known = null;
            try { known = Load(ctx, null).Pages.Select(p => p.Path).ToHashSet(StringComparer.Ordinal); }
            catch (Stangent.Core.StangentException) { Console.Error.WriteLine("warning: no graph, so a link to a page not written yet counts as broken."); }
            var report = ContentChecks.Run(ctx.Repo.Root, known);
            if ((bool)report["ok"]! == false) throw new CheckFailed("content_check_failed", $"{report["counts"]!["problems"]} broken links or duplicate ids.", report);
            return report;
        }));
        spec.Subcommands.Add(contentCheck);

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

    /// <summary>Tokens, model and time of the AI step just run, from Claude Code's JSON result; also into telemetry (D21).</summary>
    public static Usage? ReadUsage(CliContext ctx, string? file)
    {
        if (file is null) return null;
        var usage = Usage.FromClaudeResult(Cli.ReadJson(file));
        if (usage.Model is { } model) ctx.Span.Attributes["gen_ai.request.model"] = model;
        ctx.Span.Attributes["gen_ai.provider.name"] = "anthropic";
        ctx.Span.Attributes["gen_ai.usage.input_tokens"] = usage.InputTokens + usage.CacheReadTokens + usage.CacheWriteTokens;
        ctx.Span.Attributes["gen_ai.usage.output_tokens"] = usage.OutputTokens;
        ctx.Span.Attributes["gen_ai.usage.cost_usd"] = usage.CostUsd;
        return usage;
    }

    public static PageSet LoadSet(CliContext ctx) => Load(ctx, null);

    static (PageSet, PageDef) LoadPage(CliContext ctx, string id)
    {
        var set = Load(ctx, null);
        var page = set.Find(id) ?? throw new Stangent.Core.StangentException("not_found", $"No page '{id}'. `stangent spec pages` lists them.", Stangent.Core.ExitCodes.NotFound);
        return (set, page);
    }

    /// <summary>T.4: an answer read from a page that's behind the code says so, beside the answer. Added here, so the asker's input and the saved answer don't change.</summary>
    static void NoteBehind(CliContext ctx, JsonObject answer)
    {
        var ids = (answer["pages"] as JsonArray ?? []).Select(p => p is JsonObject o ? (string?)o["id"] : (string?)p).OfType<string>().ToList();
        try
        {
            var set = Load(ctx, null);
            var paths = new SpecPaths(ctx.Repo.Root);
            var behind = ids.Select(set.Find).OfType<PageDef>().Where(d => Staleness.Behind(set, paths, d)).Select(d => d.Id).ToList();
            answer["pagesBehind"] = new JsonArray([.. behind.Select(b => (JsonNode)b)]);
            foreach (var id in behind) Console.Error.WriteLine("warning: " + Staleness.BehindWarning(id, null, set.Commit));
        }
        catch (Stangent.Core.StangentException e)
        {
            answer["pagesBehindNote"] = "couldn't check whether these pages are behind the code: " + e.Message;
        }
    }

    /// <summary>A step that ends the page as failed, or asks for a retry, is a check that found problems: exit 1, with the next step in result.</summary>
    static JsonNode Answered((RunStep Step, JsonObject? Answer) done, string question)
    {
        var json = Finish(done.Step, PageAsk.Id(question)).AsObject();
        json["question"] = question;
        if (done.Answer is not null) json["answer"] = done.Answer;
        return json;
    }

    public static JsonNode Finish(RunStep step, string id)
    {
        var json = step.ToJson(id);
        if (step.Status == "retry")
            throw new CheckFailed("check_failed", $"{id}: the code check failed; the subagent gets one retry with the errors.", json);
        if (step.Status == "failed")
            throw new CheckFailed("step_failed", $"{id} failed: {step.Errors?.FirstOrDefault()}", json);
        // D89: stangent's own setup refused what it allows; a batch stops here rather than repeat it on every ticket.
        if (step.Status == "blocked")
            throw new CheckFailed("setup_blocked", $"{id}: {step.Errors?.FirstOrDefault()}", json);
        return json;
    }

    static PageSet Load(CliContext ctx, int? depth) =>
        PageList.Build(Cli.LoadGraph(ctx), ctx.Repo.Root, SpecSettings.From(ctx.Repo.Config, depth));
}

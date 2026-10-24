using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Spec;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

/// <summary><c>stangent fsd …</c>: the agreed FSD, imported by code and linked to the reference pages (Phase 2b, D52–D53).</summary>
static class FsdCommands
{
    public static void Add(RootCommand root)
    {
        var fsd = new Command("fsd", "The agreed FSD: import it (code only) and link it to the reference pages (D52, D53).");
        root.Subcommands.Add(fsd);

        var fileArg = new Argument<string>("file") { Description = "The .docx to import." };
        var idOption = new Option<string?>("--id") { Description = "The document's id (default: from its file name)." };
        var import = new Command("import", "Convert a .docx to Markdown by code, archive the original (D16) and record its sections (source-document v1).") { fileArg, idOption };
        import.SetAction(pr => Cli.Run(pr, "fsd import", ctx =>
        {
            var result = FsdImport.Import(ctx.Repo, pr.GetValue(fileArg)!, pr.GetValue(idOption));
            ctx.Span.Attributes["stangent.artifact.count"] = (int?)result["sections"] ?? 0;
            return result;
        }));
        fsd.Subcommands.Add(import);

        var docOption = new Option<string?>("--doc") { Description = "Only this document." };
        var sections = new Command("sections", "List the imported sections that have text to judge.") { docOption };
        sections.SetAction(pr => Cli.Run(pr, "fsd sections", ctx => new JsonArray([.. FsdImport.Documents(ctx.Repo.Root)
            .Where(d => pr.GetValue(docOption) is not { } only || (string)d["id"]! == only)
            .SelectMany(d => d["sections"]!.AsArray().OfType<JsonObject>().Where(s => ((string)s["text"]!).Trim().Length > 0)
                .Select(s => (JsonNode)new JsonObject { ["doc"] = (string)d["id"]!, ["section"] = (string)s["id"]!, ["title"] = (string)s["title"]! }))])));
        fsd.Subcommands.Add(sections);

        var candidates = new Command("candidates", "Candidate pages for each section, found by code: names, field labels, endpoints (D53). No AI.") { docOption };
        candidates.SetAction(pr => Cli.Run(pr, "fsd candidates", ctx =>
        {
            var set = SpecCommands.LoadSet(ctx);
            var index = SpecIndex.Load(ctx.Repo.Root);
            var result = new JsonArray();
            foreach (var d in FsdImport.Documents(ctx.Repo.Root).Where(d => pr.GetValue(docOption) is not { } only || (string)d["id"]! == only))
                foreach (var s in d["sections"]!.AsArray().OfType<JsonObject>().Where(s => ((string)s["text"]!).Trim().Length > 0))
                    result.Add(new JsonObject
                    {
                        ["doc"] = (string)d["id"]!, ["section"] = (string)s["id"]!, ["title"] = (string)s["title"]!,
                        ["candidates"] = new JsonArray([.. Candidates.ForSection(s, set, index.Pages, (string?)s["parent"] is { } par ? index.Sections.GetValueOrDefault(((string)d["id"]!, par)) : null)
                            .Select(c => (JsonNode)new JsonObject { ["page"] = c.Page, ["why"] = c.Why, ["recorded"] = index.Pages.ContainsKey(c.Page) })]),
                    });
            ctx.Span.Attributes["stangent.artifact.count"] = result.Count;
            return new JsonObject
            {
                ["sections"] = result.Count,
                ["withCandidates"] = result.Count(r => r!["candidates"]!.AsArray().Count > 0),
                ["result"] = result,
            };
        }));
        fsd.Subcommands.Add(candidates);

        var docArg = new Argument<string>("doc") { Description = "The document's id." };
        var sectionArg = new Argument<string>("section") { Description = "The section's id: its number (4.2.3) or heading path." };
        var prepare = new Command("prepare", "Start judging one section: says what runs next (finder, judge, or nothing when its inputs are unchanged).") { docArg, sectionArg };
        prepare.SetAction(pr => Cli.Run(pr, "fsd prepare", ctx =>
            SpecCommands.Finish(LinkRun.Prepare(SpecCommands.LoadSet(ctx), ctx.Repo, pr.GetValue(docArg)!, pr.GetValue(sectionArg)!), LinkRun.Key(pr.GetValue(docArg)!, pr.GetValue(sectionArg)!))));
        fsd.Subcommands.Add(prepare);

        var usageOption = new Option<string?>("--usage") { Description = "Claude Code's JSON result for the step just run." };
        var check = new Command("check", "Check the step just run (quotes word for word, ids, cited symbols), record links in specs/trace.yaml, and say what runs next.") { docArg, sectionArg, usageOption };
        check.SetAction(pr => Cli.Run(pr, "fsd check", ctx =>
        {
            var usage = SpecCommands.ReadUsage(ctx, pr.GetValue(usageOption));
            var set = SpecCommands.LoadSet(ctx);
            return SpecCommands.Finish(LinkRun.Check(set, ctx.Repo, Cli.LoadGraph(ctx), pr.GetValue(docArg)!, pr.GetValue(sectionArg)!, usage), LinkRun.Key(pr.GetValue(docArg)!, pr.GetValue(sectionArg)!));
        }));
        fsd.Subcommands.Add(check);

        var undocumented = new Command("undocumented", "List screens no FSD section links to as undocumented, in specs/trace.yaml (code, no AI). Needs fsd.findings: true.");
        undocumented.SetAction(pr => Cli.Run(pr, "fsd undocumented", ctx =>
        {
            var scope = ctx.Repo.Config.Fsd.Scope;
            var findings = ctx.Repo.Config.Fsd.Findings;
            var n = Undocumented.Apply(ctx.Repo.Root, SpecCommands.LoadSet(ctx), scope.Count == 0 ? null : scope, findings);
            ctx.Span.Attributes["stangent.artifact.count"] = n;
            return new JsonObject { ["undocumented"] = n, ["findings"] = findings };
        }));
        fsd.Subcommands.Add(undocumented);

        var trace = new Command("trace", "specs/trace.yaml: the links, findings and decisions (D58).");
        root.Subcommands.Add(trace);
        var baseOption = new Option<string?>("--base") { Description = "The PR's base ref: decided_by must not change in the PR." };
        var fixOption = new Option<bool>("--fix") { Description = "Drop the decision of entries whose quotes no longer match, and save." };
        var traceCheck = new Command("check", "CI checks: schema and values, ids, quotes still in the FSD and pages, cited symbols, decided_by never typed.") { baseOption, fixOption };
        traceCheck.SetAction(pr => Cli.Run(pr, "trace check", ctx =>
        {
            var graph = Stangent.Graph.GraphStore.Load(Stangent.Graph.GraphStore.PathFor(ctx.Repo));
            var baseTrace = pr.GetValue(baseOption) is { } b ? BaseTrace(ctx, b) : null;
            var report = TraceCheck.Run(ctx.Repo.Root, graph, baseTrace, pr.GetValue(fixOption));
            ctx.Span.Attributes["stangent.artifact.count"] = (int)report["counts"]!["entries"]!;
            if (report["ok"]!.GetValue<bool>()) return report;
            throw new CheckFailed("trace_needs_attention", "specs/trace.yaml needs attention; see result.problems.", report);
        }));
        trace.Subcommands.Add(traceCheck);

        var chapterArg = new Argument<string>("chapter") { Description = "A section id: the chapter and everything under it." };
        var approve = new Command("approve", "Bulk approval (D53): decision agree on every confirmed agree link under a chapter. Commit it in a PR.") { docArg, chapterArg };
        approve.SetAction(pr => Cli.Run(pr, "trace approve", ctx => new JsonObject { ["approved"] = TraceCheck.ApproveChapter(ctx.Repo.Root, pr.GetValue(docArg)!, pr.GetValue(chapterArg)!) }));
        trace.Subcommands.Add(approve);

        var byOption = new Option<string>("--by") { Description = "Who approved or merged the PR, from the code host (D24): CI passes it.", Required = true };
        var attribute = new Command("attribute", "After a merge: fill decided_by on every decision the merged PR changed (D58).") { baseOption, byOption };
        attribute.SetAction(pr => Cli.Run(pr, "trace attribute", ctx =>
        {
            var b = pr.GetValue(baseOption) ?? throw new Stangent.Core.StangentException("usage", "--base is required: the commit before the merge.", Stangent.Core.ExitCodes.Usage);
            return new JsonObject { ["attributed"] = TraceCheck.Attribute(ctx.Repo.Root, BaseTrace(ctx, b) ?? TraceFile.Empty(), pr.GetValue(byOption)!) };
        }));
        trace.Subcommands.Add(attribute);
    }

    static JsonObject? BaseTrace(CliContext ctx, string gitRef)
    {
        if (gitRef.StartsWith('-') || gitRef.Contains(' ')) throw new Stangent.Core.StangentException("usage", $"'{gitRef}' is not a git ref.", Stangent.Core.ExitCodes.Usage);
        var text = Stangent.Graph.Git.Run(ctx.Repo.Root, $"show {gitRef}:specs/trace.yaml");
        return text is null ? null : TraceFile.Parse(text);
    }
}

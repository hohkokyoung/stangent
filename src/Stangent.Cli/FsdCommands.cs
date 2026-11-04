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

        var fileArg = new Argument<string>("file") { Description = "The .docx or .pdf to import." };
        var idOption = new Option<string?>("--id") { Description = "The document's id (default: from its file name)." };
        var flatOption = new Option<bool>("--flat") { Description = "Import the whole document as one labelled section with no structure, for a document with no headings or bookmarks." };
        var acceptOption = new Option<bool>("--accept-changes") { Description = "For a .docx with tracked changes, which otherwise stops the import: import the accepted view (inserted text kept, deleted text gone, moved text once), listing how many were accepted." };
        var allowLabelOption = new Option<string[]>("--allow-label") { Description = "Import a file with this sensitivity label, by its name or id. Without it, a label stops the import. It's kept in the document's record with your name, from git. Repeat for several." };
        var import = new Command("import", "Convert a .docx or .pdf to Markdown by code, archive the original (D16) and record its sections (source-document v3), with a verdict and a report. A changed file for a document already here comes in as a candidate with a section diff (D117).") { fileArg, idOption, flatOption, acceptOption, allowLabelOption };
        import.SetAction(pr => Cli.Run(pr, "fsd import", ctx =>
        {
            var result = Stopping(() => FsdImport.Import(ctx.Repo, pr.GetValue(fileArg)!, pr.GetValue(idOption), pr.GetValue(flatOption), pr.GetValue(acceptOption), pr.GetValue(allowLabelOption)));
            ctx.Span.Attributes["stangent.artifact.count"] = (int?)result["sections"] ?? 0;
            return result;
        }));
        fsd.Subcommands.Add(import);

        var docArgument = new Argument<string>("doc") { Description = "The document's id." };
        var reread = new Command("reread", "Read the same original from the archive again with this build (D122). Different text comes in as a candidate; the same text reads unchanged.") { docArgument, allowLabelOption };
        reread.SetAction(pr => Cli.Run(pr, "fsd reread", ctx => Stopping(() => FsdImport.Reread(ctx.Repo, pr.GetValue(docArgument)!, pr.GetValue(allowLabelOption)))));
        fsd.Subcommands.Add(reread);

        var approveFirst = new Command("approve", "A person approves a first import waiting with first-import-approval on (D122). Who decided comes from git. Stangent opens no PR and pushes nothing.") { docArgument };
        approveFirst.SetAction(pr => Cli.Run(pr, "fsd approve", ctx => FsdVersions.Approve(ctx.Repo.Root, pr.GetValue(docArgument)!, FsdVersions.GitPerson(ctx.Repo.Root))));
        fsd.Subcommands.Add(approveFirst);

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

        var diffFormat = new Option<string>("--format") { Description = "json (default) or md (SECTION_DIFF.md).", DefaultValueFactory = _ => "json" };
        var diff = new Command("diff", "The waiting candidate's section diff against the current version (D117). Code only.") { docArg, diffFormat };
        diff.SetAction(pr =>
        {
            var markdown = pr.GetValue(diffFormat) == "md";
            return Cli.Run(pr, "fsd diff", ctx => FsdVersions.Diff(ctx.Repo.Root, pr.GetValue(docArg)!), markdown ? r => FsdSectionDiff.Markdown(r!.AsObject()) : null);
        });
        fsd.Subcommands.Add(diff);

        var candidateSection = new Argument<string>("candidate-section") { Description = "The doubtful section's id in the candidate; with --removed, the S-id of a doubtful section of the current version." };
        var sameAsOption = new Option<string?>("--same-as") { Description = "It's the current version's section with this permanent id (S-0001)." };
        var newOption = new Option<bool>("--new") { Description = "It's a new section: it gets a new permanent id on promotion." };
        var removedOption = new Option<bool>("--removed") { Description = "The current version's section is gone: its id is marked removed on promotion." };
        var resolve = new Command("resolve", "Settle one doubtful section (D117). Who decided comes from git.") { docArg, candidateSection, sameAsOption, newOption, removedOption };
        resolve.SetAction(pr => Cli.Run(pr, "fsd resolve", ctx => FsdVersions.Resolve(ctx.Repo.Root, pr.GetValue(docArg)!, pr.GetValue(candidateSection)!,
            pr.GetValue(sameAsOption), pr.GetValue(newOption), pr.GetValue(removedOption), FsdVersions.GitPerson(ctx.Repo.Root))));
        fsd.Subcommands.Add(resolve);

        var promote = new Command("promote", "Make the candidate the current version, carrying links and requirements across by code. Refused while a doubtful section remains (D117).") { docArg };
        promote.SetAction(pr => Cli.Run(pr, "fsd promote", ctx => FsdVersions.Promote(ctx.Repo, pr.GetValue(docArg)!, FsdVersions.GitPerson(ctx.Repo.Root))));
        fsd.Subcommands.Add(promote);

        var discard = new Command("discard", "Throw the waiting candidate away; the current version stays. Who decided comes from git (D117).") { docArg };
        discard.SetAction(pr => Cli.Run(pr, "fsd discard", ctx => FsdVersions.Discard(ctx.Repo.Root, pr.GetValue(docArg)!, FsdVersions.GitPerson(ctx.Repo.Root))));
        fsd.Subcommands.Add(discard);

        var usageOption = new Option<string?>("--usage") { Description = "Claude Code's JSON result for the step just run." };
        var check = new Command("check", "Check the step just run (quotes word for word, ids, cited symbols), record links in specs/trace.yaml, and say what runs next.") { docArg, sectionArg, usageOption };
        check.SetAction(pr => Cli.Run(pr, "fsd check", ctx =>
        {
            var usage = SpecCommands.ReadUsage(ctx, pr.GetValue(usageOption));
            var set = SpecCommands.LoadSet(ctx);
            return SpecCommands.Finish(LinkRun.Check(set, ctx.Repo, Cli.LoadGraph(ctx), pr.GetValue(docArg)!, pr.GetValue(sectionArg)!, usage), LinkRun.Key(pr.GetValue(docArg)!, pr.GetValue(sectionArg)!));
        }));
        fsd.Subcommands.Add(check);

        var disprove = new Command("disprove", "A fresh agent tries to refute each finding from the code (D119). Off unless fsd.disprove is on.");
        fsd.Subcommands.Add(disprove);
        var disprovePrepare = new Command("prepare", "Start on one section's findings: says what runs next, or nothing when none needs an answer.") { docArg, sectionArg };
        disprovePrepare.SetAction(pr => Cli.Run(pr, "fsd disprove prepare", ctx =>
            SpecCommands.Finish(DisproveRun.Prepare(SpecCommands.LoadSet(ctx), ctx.Repo, pr.GetValue(docArg)!, pr.GetValue(sectionArg)!), LinkRun.Key(pr.GetValue(docArg)!, pr.GetValue(sectionArg)!))));
        disprove.Subcommands.Add(disprovePrepare);
        var disproveCheck = new Command("check", "Check the answer (its quote word for word at its lines), write the verdict through the state module, and say what runs next.") { docArg, sectionArg, usageOption };
        disproveCheck.SetAction(pr => Cli.Run(pr, "fsd disprove check", ctx =>
        {
            var usage = SpecCommands.ReadUsage(ctx, pr.GetValue(usageOption));
            return SpecCommands.Finish(DisproveRun.Check(SpecCommands.LoadSet(ctx), ctx.Repo, pr.GetValue(docArg)!, pr.GetValue(sectionArg)!, usage), LinkRun.Key(pr.GetValue(docArg)!, pr.GetValue(sectionArg)!));
        }));
        disprove.Subcommands.Add(disproveCheck);
        var disproveEstimate = new Command("estimate", "What a run would send, from the bundles alone: no AI, nothing written.") { docArg };
        disproveEstimate.SetAction(pr => Cli.Run(pr, "fsd disprove estimate", ctx => DisproveRun.Estimate(SpecCommands.LoadSet(ctx), ctx.Repo, pr.GetValue(docArg)!)));
        disprove.Subcommands.Add(disproveEstimate);

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
        var traceCheck = new Command("check", "CI checks: schema and values, ids, quotes still in the FSD and pages, cited symbols, decided_by never typed. Issues findings' ids and evidence, lists them with their severity (D118).") { baseOption, fixOption };
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
        approve.SetAction(pr => Cli.Run(pr, "trace approve", ctx => new JsonObject { ["approved"] = TraceCheck.ApproveChapter(ctx.Repo.Root, pr.GetValue(docArg)!, pr.GetValue(chapterArg)!, TicketCommands.Who(ctx)) }));
        trace.Subcommands.Add(approve);

        var findingArg = new Argument<string>("finding") { Description = "The finding's X- id, or its entry id." };
        var whyOption = new Option<string?>("--why") { Description = "Why, in your words. It's kept beside your name." };
        var confirm = new Command("confirm", "A person closes a finding the AI called refuted or unreachable, having read its quote at its lines (D119). Who comes from git.") { findingArg, whyOption };
        confirm.SetAction(pr => Cli.Run(pr, "trace confirm", ctx => TraceFindings.Review(ctx.Repo.Root, pr.GetValue(findingArg)!, "confirmed", TicketCommands.Who(ctx), pr.GetValue(whyOption))));
        trace.Subcommands.Add(confirm);
        var sendBack = new Command("send-back", "A person sends a finding the AI called refuted or unreachable back to the BA's queue (D119). Who comes from git.") { findingArg, whyOption };
        sendBack.SetAction(pr => Cli.Run(pr, "trace send-back", ctx => TraceFindings.Review(ctx.Repo.Root, pr.GetValue(findingArg)!, "sent-back", TicketCommands.Who(ctx), pr.GetValue(whyOption))));
        trace.Subcommands.Add(sendBack);

        var byOption = new Option<string>("--by") { Description = "Who approved or merged the PR, from the code host (D24): CI passes it.", Required = true };
        var attribute = new Command("attribute", "After a merge: fill decided_by on every decision, and severity_by on every severity, the merged PR changed (D58, D118).") { baseOption, byOption };
        attribute.SetAction(pr => Cli.Run(pr, "trace attribute", ctx =>
        {
            var b = pr.GetValue(baseOption) ?? throw new Stangent.Core.StangentException("usage", "--base is required: the commit before the merge.", Stangent.Core.ExitCodes.Usage);
            return new JsonObject { ["attributed"] = TraceCheck.Attribute(ctx.Repo.Root, BaseTrace(ctx, b) ?? TraceFile.Empty(), pr.GetValue(byOption)!) };
        }));
        trace.Subcommands.Add(attribute);
    }

    /// <summary>A file that was read and couldn't come in gets the verdict too (D122), in the envelope, as nothing was written to hold it. The error stays what it was.</summary>
    static JsonObject Stopping(Func<JsonObject> import)
    {
        try { return import(); }
        catch (Stangent.Core.StangentException e) when (ImportReport.Stops.Contains(e.Code))
        {
            throw new CheckFailed(e.Code, e.Message, new JsonObject { ["verdict"] = ImportReport.Failed, ["stoppedBy"] = e.Code }, e.ExitCode, e.Details);
        }
    }

    static JsonObject? BaseTrace(CliContext ctx, string gitRef)
    {
        if (gitRef.StartsWith('-') || gitRef.Contains(' ')) throw new Stangent.Core.StangentException("usage", $"'{gitRef}' is not a git ref.", Stangent.Core.ExitCodes.Usage);
        var text = Stangent.Graph.Git.Run(ctx.Repo.Root, $"show {gitRef}:specs/trace.yaml");
        return text is null ? null : TraceFile.Parse(text);
    }
}

using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Spec;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

/// <summary><c>stangent fsd …</c>: the agreed FSD, imported by code (Phase 2b, D52) and reviewed against the source (D341). The old link route was cut (D342).</summary>
static class FsdCommands
{
    public static void Add(RootCommand root)
    {
        var fsd = new Command("fsd", "The agreed FSD: import it (code only) and review it against the source (D52, D341).");
        root.Subcommands.Add(fsd);

        var fileArg = new Argument<string>("file") { Description = "The .docx, .pdf, .xlsx or .xlsm to import." };
        var idOption = new Option<string?>("--id") { Description = "The document's id (default: from its file name)." };
        var flatOption = new Option<bool>("--flat") { Description = "Import the whole document as one labelled section with no structure, for a document with no headings or bookmarks." };
        var acceptOption = new Option<bool>("--accept-changes") { Description = "For a .docx with tracked changes, which otherwise stops the import: import the accepted view (inserted text kept, deleted text gone, moved text once), listing how many were accepted." };
        var allowLabelOption = new Option<string[]>("--allow-label") { Description = "Import a file with this sensitivity label, by its name or id. Without it, a label stops the import. It's kept in the document's record with your name, from git. Repeat for several." };
        var import = new Command("import", "Convert a .docx, .pdf, .xlsx or .xlsm to Markdown by code, archive the original (D16) and write the FSD's chapter files (source-document v4), with a verdict and a report; the commit approves it. A new file for a document already committed changes no chapter: it is compared three ways, section by section, in .stangent/fsd/<doc>/compare/ (D385), and `fsd take` copies in what you pick.") { fileArg, idOption, flatOption, acceptOption, allowLabelOption };
        import.SetAction(pr => Cli.Run(pr, "fsd import", ctx =>
        {
            var result = Stopping(() => FsdImport.Import(ctx.Repo, pr.GetValue(fileArg)!, pr.GetValue(idOption), pr.GetValue(flatOption), pr.GetValue(acceptOption), pr.GetValue(allowLabelOption)));
            ctx.Span.Attributes["stangent.artifact.count"] = (int?)result["sections"] ?? 0;
            return result;
        }));
        fsd.Subcommands.Add(import);

        var docArgument = new Argument<string>("doc") { Description = "The document's id." };
        var reread = new Command("reread", "Read the same original from the archive again with this build (D122). Different text is compared with the committed FSD as a new file is (D385); the same text reads unchanged.") { docArgument, allowLabelOption };
        reread.SetAction(pr => Cli.Run(pr, "fsd reread", ctx => Stopping(() => FsdImport.Reread(ctx.Repo, pr.GetValue(docArgument)!, pr.GetValue(allowLabelOption)))));
        fsd.Subcommands.Add(reread);

        var docOption = new Option<string?>("--doc") { Description = "Only this document." };
        var chapterOption = new Option<string?>("--chapter") { Description = "Only this top-level heading: its number or permanent id, or its place in `fsd outline` where chapters have no numbers." };
        var sections = new Command("sections", "List the imported sections that have text to judge.") { docOption, chapterOption };
        sections.SetAction(pr => Cli.Run(pr, "fsd sections", ctx =>
        {
            if (pr.GetValue(chapterOption) is not null && pr.GetValue(docOption) is null) throw new Stangent.Core.StangentException("usage", "--chapter needs --doc.", Stangent.Core.ExitCodes.Usage);
            // One document named is read or refused as itself; all of them leave out, with a warning, any that can't be read.
            return new JsonArray([.. (pr.GetValue(docOption) is { } only ? [FsdDocument.Load(ctx.Repo.Root, only)] : FsdImport.Documents(ctx.Repo.Root))
                .SelectMany(d => (pr.GetValue(chapterOption) is { } chapter ? FsdOutline.Chapter(d, chapter) : d["sections"]!.AsArray().OfType<JsonObject>()).Where(s => ((string)s["text"]!).Trim().Length > 0)
                    .Select(s => (JsonNode)new JsonObject { ["doc"] = (string)d["id"]!, ["section"] = (string)s["id"]!, ["title"] = (string)s["title"]! }))]);
        }));
        fsd.Subcommands.Add(sections);

        var viewFormat = new Option<string>("--format") { Description = "json (default) or text.", DefaultValueFactory = _ => "json" };
        var outline = new Command("outline", "A document's sections: level, id, title and size. Code only.") { docArgument, viewFormat };
        outline.SetAction(pr => Cli.Run(pr, "fsd outline", ctx => FsdOutline.Outline(ctx.Repo.Root, pr.GetValue(docArgument)!), pr.GetValue(viewFormat) == "text" ? FsdOutline.OutlineText : null));
        fsd.Subcommands.Add(outline);
        var showSection = new Argument<string>("section") { Description = "The section's permanent id (S-0001), or its number or heading path." };
        var show = new Command("show", "One section's text. Code only.") { docArgument, showSection, viewFormat };
        show.SetAction(pr => Cli.Run(pr, "fsd show", ctx => FsdOutline.Show(ctx.Repo.Root, pr.GetValue(docArgument)!, pr.GetValue(showSection)!), pr.GetValue(viewFormat) == "text" ? FsdOutline.ShowText : null));
        fsd.Subcommands.Add(show);
        var exportOut = new Option<string?>("--out") { Description = "Where to write (default: .stangent/fsd/<doc>/export/, which git ignores)." };
        var exportMax = new Option<int?>("--max-lines") { Description = "A chapter over this many lines is cut at its next heading level (default: fsd.exportMaxLines, 1500)." };
        var exportWhole = new Option<bool>("--whole") { Description = "One file, <doc>.md, holding the whole FSD (D378)." };
        var export = new Command("export", "The FSD's chapters without their id markers, one Markdown file per top-level heading or, with --whole, one file; generated, outside git, never read back (D122, D378). Code only.") { docArgument, exportOut, exportMax, exportWhole };
        export.SetAction(pr => Cli.Run(pr, "fsd export", ctx => FsdExport.Run(ctx.Repo.Root, pr.GetValue(docArgument)!, pr.GetValue(exportOut), pr.GetValue(exportMax) ?? ctx.Repo.Config.Fsd.ExportMaxLines, pr.GetValue(exportWhole))));
        fsd.Subcommands.Add(export);
        var migrate = new Command("migrate", "Turn a document imported before phase fs into chapter files: specs/fsd/<doc>/chapters/, each heading with its permanent id, and no document.md (D377, D378, D389). Refused with uncommitted changes under specs/fsd/<doc>/. Code only.") { docArgument };
        migrate.SetAction(pr => Cli.Run(pr, "fsd migrate", ctx => FsdMigrate.Run(ctx.Repo, pr.GetValue(docArgument)!)));
        fsd.Subcommands.Add(migrate);
        var goneOption = new Option<string[]>("--removed") { Description = "A section gone since HEAD on purpose, by its permanent id (S-0007): said when a heading also has no marker, or when rulings.yaml holds a ruling on it (its entries stay there, listed). Repeat for several." };
        var sameOption = new Option<string[]>("--same") { Description = "A ruled section whose heading and words both changed and that is still that section, by its permanent id: its rulings stay on it. Repeat for several." };
        var check = new Command("check", "Before a commit (D380, D382, D384): reads the working tree's chapter files, gives each heading with no marker the next id from the ledger (the only thing it writes), refuses what could put a ruling on the wrong section or lose one, warns about numbers, and lists every section added, removed, renamed, moved or changed against HEAD: what the commit approves. Code only.") { docArgument, goneOption, sameOption };
        check.SetAction(pr => Cli.Run(pr, "fsd check", ctx =>
        {
            var report = FsdCheck.Run(ctx.Repo, pr.GetValue(docArgument)!, pr.GetValue(goneOption), pr.GetValue(sameOption));
            if (report["ok"]!.GetValue<bool>()) return report;
            var refused = report["refused"]!.AsArray();
            throw new CheckFailed("fsd_check_refused", $"specs/fsd/{pr.GetValue(docArgument)} can't be committed as it is: {refused.Count} problem(s), nothing written. See result.refused.", report,
                details: [.. refused.Select(r => (string)r!["message"]!)]);
        }));
        fsd.Subcommands.Add(check);
        var docArg = new Argument<string>("doc") { Description = "The document's id." };
        var sectionArg = new Argument<string>("section") { Description = "The section's id: its number (4.2.3) or heading path." };
        var tables = new Command("tables", "A document's tables and attachments' sheets, with their T- or A- ids and columns, for writing table-map.yaml. Code only.") { docArg };
        tables.SetAction(pr => Cli.Run(pr, "fsd tables", ctx => FsdTables.Listing(ctx.Repo.Root, pr.GetValue(docArg)!)));
        fsd.Subcommands.Add(tables);
        var compareTables = new Command("compare-tables", "Compare a document's tables with facts from the code, as specs/fsd/<doc>/table-map.yaml says, by code with no AI. Each difference is a table-fact finding. (C.2, D124; on since D381).") { docArg };
        compareTables.SetAction(pr => Cli.Run(pr, "fsd compare-tables", ctx => TableFacts.Compare(ctx.Repo.Root, pr.GetValue(docArg)!, Cli.LoadGraph(ctx))));
        fsd.Subcommands.Add(compareTables);

        var diffFormat = new Option<string>("--format") { Description = "json (default) or md (comparison.md).", DefaultValueFactory = _ => "json" };
        var diff = new Command("diff", "The new file's comparison with the committed FSD (D385): per section, changed only in the file (offered), only in the repo (kept), in both (side by side, the repo's kept), new (offered), gone (listed), doubtful (a person settles). Code only.") { docArg, diffFormat };
        diff.SetAction(pr => Cli.Run(pr, "fsd diff", ctx => FsdCompare.Show(ctx.Repo.Root, pr.GetValue(docArg)!, pr.GetValue(diffFormat) == "md"),
            pr.GetValue(diffFormat) == "md" ? r => (string)r!["markdown"]! : null));
        fsd.Subcommands.Add(diff);

        var candidateSection = new Argument<string>("section") { Description = "The doubtful section: its number in the new file, or with --removed the S-id of a doubtful section of the repo." };
        var sameAsOption = new Option<string?>("--same-as") { Description = "It's the repo's section with this permanent id (S-0001)." };
        var newOption = new Option<bool>("--new") { Description = "It's a new section: `fsd take` can add it, with a new permanent id." };
        var removedOption = new Option<bool>("--removed") { Description = "The repo's section isn't in the new file: it's listed as gone, never deleted." };
        var resolve = new Command("resolve", "Settle one doubtful section of the new file's comparison (D117, D385). Who decided comes from git.") { docArg, candidateSection, sameAsOption, newOption, removedOption };
        resolve.SetAction(pr => Cli.Run(pr, "fsd resolve", ctx => FsdCompare.Resolve(ctx.Repo.Root, pr.GetValue(docArg)!, pr.GetValue(candidateSection)!,
            pr.GetValue(sameAsOption), pr.GetValue(newOption), pr.GetValue(removedOption), FsdVersions.GitPerson(ctx.Repo.Root))));
        fsd.Subcommands.Add(resolve);

        var takeSections = new Argument<string[]>("section") { Description = "A section the comparison offers: its permanent id (S-0012), or a new section's number in the new file. Repeat for several.", Arity = ArgumentArity.ZeroOrMore };
        var allUpdates = new Option<bool>("--all-updates") { Description = "Every section only the new file changed. New sections are named one by one." };
        var take = new Command("take", "Copy the new file's heading and text into the chapter files, uncommitted, for sections the comparison offers (D385): changed only in the file, or new. A section the repo changed is never taken. Then `fsd check` and commit: the commit approves (D380). Code only.") { docArg, takeSections, allUpdates };
        take.SetAction(pr => Cli.Run(pr, "fsd take", ctx => FsdCompare.Take(ctx.Repo, pr.GetValue(docArg)!, pr.GetValue(takeSections) ?? [], pr.GetValue(allUpdates))));
        fsd.Subcommands.Add(take);

        var status = new Command("status", "Where a document stands: its version, a new file's comparison, doubtful sections, uncovered sections, who has what, and the next command. No AI.") { docArgument };
        status.SetAction(pr => Cli.Run(pr, "fsd status", ctx => FsdStatus.Of(ctx.Repo.Root, pr.GetValue(docArgument)!)));
        fsd.Subcommands.Add(status);

        // Layer 5's command (D341): the source-reading reviewer, checked by code, two runs merged.
        var usageOption = new Option<string?>("--usage") { Description = "Claude Code's JSON result for the step just run." };
        var runOption = new Option<int>("--run") { Description = "Which run, 1 (the default) to fsd.reviewRuns (2 unless set).", DefaultValueFactory = _ => 1 };
        var againOption = new Option<bool>("--again") { Description = "Prepare the run again although its input and the commit are unchanged." };
        var review = new Command("review", "Prepare one review run (D341): the FSD's sections with their ids, the scope and the output schema for stangent-fsd-reviewer, which reads the repo itself, read-only, in a worktree of HEAD. Says what runs next, with the agent's limits.") { docArgument, runOption, againOption };
        review.SetAction(pr => Cli.Run(pr, "fsd review", ctx =>
        {
            var (doc, run) = (pr.GetValue(docArgument)!, pr.GetValue(runOption));
            var step = FsdReview.Prepare(ctx.Repo, doc, run, pr.GetValue(againOption));
            var json = SpecCommands.Finish(step, $"{doc} review run {run}").AsObject();
            if (step.Agent is not null) json["next"]!["limits"] = FsdReview.DriverLimits(ctx.Repo, doc, run);
            json["runs"] = ctx.Repo.Config.Fsd.ReviewRuns;
            return json;
        }));
        fsd.Subcommands.Add(review);
        var reviewCheck = new Command("review-check", "Check one review run's findings (D341): a known state, a real section, every FSD quote word for word in its section, every file:line at the reviewed commit. One retry with the errors; after it, failing findings are dropped and listed.") { docArgument, runOption, usageOption };
        reviewCheck.SetAction(pr => Cli.Run(pr, "fsd review-check", ctx =>
        {
            var (doc, run) = (pr.GetValue(docArgument)!, pr.GetValue(runOption));
            var step = FsdReview.Check(ctx.Repo, doc, run, SpecCommands.ReadUsage(ctx, pr.GetValue(usageOption)));
            // The retry runs under what's left of the run's cap.
            if (step.Status == "retry")
            {
                var retry = step.ToJson($"{doc} review run {run}");
                retry["next"]!["limits"] = FsdReview.DriverLimits(ctx.Repo, doc, run);
                throw new CheckFailed("check_failed", $"{doc} review run {run}: the code check failed; the reviewer gets one retry with the errors.", retry);
            }
            return SpecCommands.Finish(step, $"{doc} review run {run}");
        }));
        fsd.Subcommands.Add(reviewCheck);
        var reviewStatus = new Command("review-status", "Where each review run stands (D341): its stage and commit, how many runs merge takes, whether review.json exists. Reads only.") { docArgument };
        reviewStatus.SetAction(pr => Cli.Run(pr, "fsd review-status", ctx => FsdReview.Status(ctx.Repo, pr.GetValue(docArgument)!)));
        fsd.Subcommands.Add(reviewStatus);
        var reviewMerge = new Command("review-merge", "Union the checked review runs (D341): exact duplicates joined, each finding saying which runs found it. Writes specs/fsd/<doc>/review.json (contract fsd-review v2) and review.md. Code only.") { docArgument };
        reviewMerge.SetAction(pr => Cli.Run(pr, "fsd review-merge", ctx =>
        {
            var result = FsdReview.Merge(ctx.Repo, pr.GetValue(docArgument)!);
            ctx.Span.Attributes["stangent.artifact.count"] = (int)result["findings"]!;
            return result;
        }));
        fsd.Subcommands.Add(reviewMerge);
        var links = new Command("links", "The truth map's links (D362): each finding of review.json linked to a flow, else a screen (with the App frame), else a graph node, else its file; prints the share linked by fallback. Reads only.") { docArgument };
        links.SetAction(pr => Cli.Run(pr, "fsd links", ctx => Stangent.Spec.Fsd.TruthLinks.Run(ctx.Repo, pr.GetValue(docArgument)!)));
        fsd.Subcommands.Add(links);
        var rulings = new Command("rulings", "Draft or refresh specs/fsd/<doc>/rulings.yaml (contract rulings v2, D363, D386) from review.json, by FSD section: an entry per difference and match, undocumented code by its link; rulings carried over by key, an old ruling no run finds again kept as notFound, a changed section's rulings blanked, a ruled entry whose link changed flagged. Nobody needs to edit it: the PM answers through `fsd rule` (D371). Code only.") { docArgument };
        rulings.SetAction(pr => Cli.Run(pr, "fsd rulings", ctx =>
        {
            var result = Rulings.Refresh(ctx.Repo, pr.GetValue(docArgument)!);
            ctx.Span.Attributes["stangent.artifact.count"] = (int)result["entries"]!;
            return result;
        }));
        fsd.Subcommands.Add(rulings);
        var rulingsCheck = new Command("rulings-check", "Check rulings.yaml (each value one of its state's, restated with a statement, every entry keyed), take who ruled and when from git blame, list medium and low matches first, count, and write specs/fsd/<doc>/truth-map.json (contract truth-map v2) and truth-map.md (D366). Problems write nothing. Code only.") { docArgument };
        rulingsCheck.SetAction(pr => Cli.Run(pr, "fsd rulings-check", ctx =>
        {
            var report = TruthMap.Check(ctx.Repo, pr.GetValue(docArgument)!, out var ok);
            ctx.Span.Attributes["stangent.artifact.count"] = (int)report["counts"]!["entries"]!;
            if (ok) return report;
            throw new CheckFailed("rulings_need_attention", $"{report["file"]} needs attention; see result.problems.", report);
        }));
        fsd.Subcommands.Add(rulingsCheck);
        var ruleNumber = new Argument<int>("number") { Description = "The question's number in truth-map.md." };
        var ruleAnswer = new Argument<string?>("answer") { Description = "yes or no (a match, or a section's matches); code or fsd (which is right); keep or remove (code the FSD doesn't mention); clear (undo); drop (an item the review didn't find again).", Arity = ArgumentArity.ZeroOrOne };
        var ruleWords = new Option<string?>("--words") { Description = "The rule in your own words (restated, or with any answer)." };
        var ruleNote = new Option<string?>("--note") { Description = "A note to keep with the answer (replaces an earlier one)." };
        var ruleMap = new Option<string?>("--map") { Description = "The map id at the top of the truth-map.md the PM read: refused if the map was rebuilt since." };
        var rule = new Command("rule", "Record the PM's answer to a numbered question of truth-map.md in rulings.yaml (D371), so nobody edits the file: the number is read back through truth-map.json, the answer checked against the item, the file rewritten keeping every other line. The truth map isn't rebuilt and nothing is committed. Code only.") { docArgument, ruleNumber, ruleAnswer, ruleWords, ruleNote, ruleMap };
        rule.SetAction(pr => Cli.Run(pr, "fsd rule", ctx => PmAnswers.Apply(ctx.Repo, pr.GetValue(docArgument)!, pr.GetValue(ruleNumber), pr.GetValue(ruleAnswer), pr.GetValue(ruleWords), pr.GetValue(ruleNote), pr.GetValue(ruleMap))));
        fsd.Subcommands.Add(rule);
        var writeCost = new Option<string?>("--max-cost") { Description = "The cost cap in dollars for the AI that drafts the FSD's words for rulings that the code is right (rule 9), or `none` for the drafter's own cap. Without it those rulings are listed; the PM's own words are written either way." };
        var writeSection = new Option<string?>("--section") { Description = "Where the PM's words about code no section covers go, by permanent id (S-0012) or number: added at the end of that section. Without it they are listed, never guessed." };
        var writeItem = new Option<int?>("--item") { Description = "With --section, when more than one is waiting: the code's number in truth-map.md." };
        var write = new Command("write", "Turn rulings into the FSD's words (D379, D386, D387), uncommitted: the PM's own words replace the quoted sentence, or go at the end of the --section named; a ruling that the code is right goes to an AI step under --max-cost (D60), checked by `fsd write-check`. Each written entry becomes a confirmed match with writtenFrom, and its section's recorded hash moves to the new text. A ruling drafted from Jira waits until committed. Then `fsd check`, read the diff, commit.") { docArgument, writeCost, writeSection, writeItem, ruleMap };
        write.SetAction(pr => Cli.Run(pr, "fsd write", ctx =>
        {
            var doc = pr.GetValue(docArgument)!;
            var (result, step) = FsdWrite.Run(ctx.Repo, doc, pr.GetValue(writeCost), pr.GetValue(writeSection), pr.GetValue(writeItem), pr.GetValue(ruleMap));
            ctx.Span.Attributes["stangent.artifact.count"] = result["written"]!.AsArray().Count;
            if (step is null) return result;
            var json = SpecCommands.Finish(step, $"{doc} write").AsObject();
            json["next"]!["limits"] = FsdWrite.DriverLimits(ctx.Repo, doc);
            foreach (var (k, v) in result.ToList()) if (k != "next") json[k] = v?.DeepClone();
            json["after"] = result["next"]?.DeepClone();
            return json;
        }));
        fsd.Subcommands.Add(write);
        var writeCheck = new Command("write-check", "Check the AI's drafts of `fsd write` (D379): each replaces the ruled sentence word for word and only it, its words go in as one sentence, every cited path:line is real at the run's commit and inside the entry's linked code. Passing drafts are written; one retry for the rest, then they are listed.") { docArgument, usageOption };
        writeCheck.SetAction(pr => Cli.Run(pr, "fsd write-check", ctx =>
        {
            var doc = pr.GetValue(docArgument)!;
            var (result, step) = FsdWrite.Check(ctx.Repo, doc, SpecCommands.ReadUsage(ctx, pr.GetValue(usageOption)));
            ctx.Span.Attributes["stangent.artifact.count"] = result["written"]?.AsArray().Count ?? 0;
            if (step.Status == "retry")
            {
                var retry = step.ToJson($"{doc} write");
                retry["next"]!["limits"] = FsdWrite.DriverLimits(ctx.Repo, doc);
                foreach (var (k, v) in result.ToList()) if (k != "next") retry[k] = v?.DeepClone();
                throw new CheckFailed("check_failed", $"{doc} write: some drafts failed the code check; the drafter gets one retry with the errors.", retry);
            }
            var json = SpecCommands.Finish(step, $"{doc} write").AsObject();
            foreach (var (k, v) in result.ToList()) if (k != "next") json[k] = v?.DeepClone();
            json["after"] = result["next"]?.DeepClone();
            return json;
        }));
        fsd.Subcommands.Add(writeCheck);

        var trace = new Command("trace", "specs/trace.yaml: the links, findings and decisions (D58), frozen since the link route was cut (D342); table facts still write to it.");
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

        var linkSection = new Option<string?>("--section") { Description = "Only this section, by its number." };
        var traceLinks = new Command("links", "Each link of a document with its page statement and the code lines that statement cites (D133). Reads only: no AI, no project file written, not even telemetry. The lines come from the page's citations, so they show where to look and don't prove the code matches.") { docArg, linkSection };
        traceLinks.SetAction(pr => Cli.Run(pr, "trace links", ctx => TraceLinks.Run(ctx.Repo.Root, pr.GetValue(docArg)!, pr.GetValue(linkSection)), null));
        trace.Subcommands.Add(traceLinks);

        var findingsOut = new Option<string?>("--out") { Description = "Where to write (default: specs/fsd/<doc>/findings.md)." };
        var traceFindings = new Command("findings", "The BA's queue for a document as one readable Markdown file, strongest evidence first (D152). By code, no AI; nothing reads it back, and answers still go in specs/trace.yaml.") { docArg, findingsOut };
        traceFindings.SetAction(pr => Cli.Run(pr, "trace findings", ctx => FindingsReport.Run(ctx.Repo.Root, pr.GetValue(docArg)!, pr.GetValue(findingsOut))));
        trace.Subcommands.Add(traceFindings);

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

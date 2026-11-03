using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Spec;
using Stangent.Spec.Fsd;
using Stangent.Spec.Requirements;

/// <summary><c>stangent req …</c>: the structured spec built on the FSD (Phase 2c, D99).</summary>
static class ReqCommands
{
    public static void Add(RootCommand root)
    {
        var req = new Command("req", "The structured spec: requirements with stable ids, built from the FSD word for word; the BA approves each one (D99).");
        root.Subcommands.Add(req);

        var docOption = new Option<string?>("--doc") { Description = "Only this document." };
        var sections = new Command("sections", "List the imported sections that have text to build items from.") { docOption };
        sections.SetAction(pr => Cli.Run(pr, "req sections", ctx => new JsonArray([.. FsdImport.Documents(ctx.Repo.Root)
            .Where(d => pr.GetValue(docOption) is not { } only || (string)d["id"]! == only)
            .SelectMany(d => d["sections"]!.AsArray().OfType<JsonObject>().Where(s => ((string)s["text"]!).Trim().Length > 0)
                .Select(s => (JsonNode)new JsonObject { ["doc"] = (string)d["id"]!, ["section"] = (string)s["id"]!, ["title"] = (string)s["title"]! }))])));
        req.Subcommands.Add(sections);

        var docArg = new Argument<string>("doc") { Description = "The document's id." };
        var sectionArg = new Argument<string>("section") { Description = "The section's id: its number (4.2.3) or heading path." };
        var retryFailedOption = new Option<bool>("--retry-failed") { Description = "Try again a section that failed with these same inputs." };
        var prepare = new Command("prepare", "Start building one section's items: says what runs next, or nothing when its inputs are unchanged (D18).") { docArg, sectionArg, retryFailedOption };
        prepare.SetAction(pr => Cli.Run(pr, "req prepare", ctx =>
            SpecCommands.Finish(RequirementRun.Prepare(ctx.Repo, pr.GetValue(docArg)!, pr.GetValue(sectionArg)!, pr.GetValue(retryFailedOption)), RequirementRun.Key(pr.GetValue(docArg)!, pr.GetValue(sectionArg)!))));
        req.Subcommands.Add(prepare);

        var usageOption = new Option<string?>("--usage") { Description = "Claude Code's JSON result for the step just run." };
        var check = new Command("check", "Check the step just run (quotes word for word, every sentence cited or background), record the items, and say what runs next.") { docArg, sectionArg, usageOption };
        check.SetAction(pr => Cli.Run(pr, "req check", ctx =>
            SpecCommands.Finish(RequirementRun.Check(ctx.Repo, pr.GetValue(docArg)!, pr.GetValue(sectionArg)!, SpecCommands.ReadUsage(ctx, pr.GetValue(usageOption))), RequirementRun.Key(pr.GetValue(docArg)!, pr.GetValue(sectionArg)!))));
        req.Subcommands.Add(check);

        var idArg = new Argument<string>("id") { Description = "An item's id: R-0042, F-0007, D-0003, J-0001, S-0001 or Q-0002." };
        var show = new Command("show", "One item by id, with its quotes, criteria and links; never a whole file (D99).") { idArg };
        show.SetAction(pr => Cli.Run(pr, "req show", ctx =>
            RequirementFile.Show(ctx.Repo.Root, pr.GetValue(idArg)!) ?? throw new Stangent.Core.StangentException("not_found", $"No item '{pr.GetValue(idArg)}'.", Stangent.Core.ExitCodes.NotFound)));
        req.Subcommands.Add(show);
    }
}

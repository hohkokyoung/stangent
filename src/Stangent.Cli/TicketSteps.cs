using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Spec.Pages;
using Stangent.Tickets;

/// <summary>The ticket commands after the starting points: bundle, prove, drafts and posting (filled in step by step).</summary>
static class TicketSteps
{
    public static void Add(Command ticket)
    {
        var formatOption = new Option<string>("--format") { Description = "json (default) or md (the developer's view).", DefaultValueFactory = _ => "json" };
        var bundle = new Command("bundle", "Build the ticket's context bundle: pages, FSD sections, impact, pointers; no source code (D71). Code only.") { TicketCommands.KeyArg, formatOption };
        bundle.SetAction(pr =>
        {
            var markdown = pr.GetValue(formatOption) == "md";
            return Cli.Run(pr, "ticket bundle", ctx =>
            {
                var result = TicketBundle.Build(ctx.Repo, TicketContext.Load(ctx.Repo), pr.GetValue(TicketCommands.KeyArg)!);
                ctx.Span.Attributes["stangent.artifact.count"] = result["pages"]!.AsArray().Count;
                return result;
            }, markdown ? r => TicketBundle.Markdown(r!.AsObject()) : null);
        });
        ticket.Subcommands.Add(bundle);
    }

    public static RunStep Prepare(CliContext ctx, StepSpec step, string key, ParseResult pr) =>
        throw new Stangent.Core.StangentException("usage", $"--step {step.Name} isn't built yet.", Stangent.Core.ExitCodes.Usage);

    public static RunStep Check(CliContext ctx, StepSpec step, string key, Usage? usage) =>
        throw new Stangent.Core.StangentException("usage", $"--step {step.Name} isn't built yet.", Stangent.Core.ExitCodes.Usage);
}

using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Spec.Pages;
using Stangent.Tickets;
using Stangent.Tickets.Proof;

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

        var hintOption = new Option<string?>("--hint") { Description = "Another way to reproduce it, from the developer: a new attempt with this added (D75)." };
        var confirmOption = new Option<bool>("--confirm") { Description = "Confirm the reproduced failure matches the reported actual result (D75)." };
        var verifyOption = new Option<bool>("--verify") { Description = "On the fix branch: the test passes and nothing else started failing (D18)." };
        var prove = new Command("prove", "Prove a bug before it's fixed: prepare the reproducer's input (D75, D76); --confirm or --verify afterwards.") { TicketCommands.KeyArg, hintOption, confirmOption, verifyOption };
        prove.SetAction(pr => Cli.Run(pr, "ticket prove", ctx =>
        {
            var key = pr.GetValue(TicketCommands.KeyArg)!;
            if (pr.GetValue(confirmOption)) return BugProof.Confirm(ctx.Repo, key, TicketCommands.Who(ctx));
            if (pr.GetValue(verifyOption)) return TicketCommands.Async(() => BugProof.VerifyAsync(ctx.Repo, key, new ShellRunner()));
            return Prove(ctx, key, pr.GetValue(hintOption));
        }));
        ticket.Subcommands.Add(prove);
    }

    static JsonNode Prove(CliContext ctx, string key, string? hint)
    {
        var step = BugProof.Prepare(ctx.Repo, TicketContext.Load(ctx.Repo), key, hint);
        var json = SpecCommands.Finish(step, key).AsObject();
        if (step.Agent is not null) json["next"]!["limits"] = BugProof.DriverLimits(ctx.Repo, key);
        json["proof"] = BugProof.Load(new TicketPaths(ctx.Repo.Root, key))?.DeepClone();
        return json;
    }

    public static RunStep Prepare(CliContext ctx, StepSpec step, string key, ParseResult pr) => step.Name switch
    {
        "prove" => BugProof.Prepare(ctx.Repo, TicketContext.Load(ctx.Repo), key, null),
        _ => throw new Stangent.Core.StangentException("usage", $"--step {step.Name} isn't built yet.", Stangent.Core.ExitCodes.Usage),
    };

    public static RunStep Check(CliContext ctx, StepSpec step, string key, Usage? usage, JsonNode? claudeResult) => step.Name switch
    {
        "prove" => TicketCommands.Async(() => BugProof.CheckAsync(ctx.Repo, key, claudeResult, new ShellRunner())),
        _ => throw new Stangent.Core.StangentException("usage", $"--step {step.Name} isn't built yet.", Stangent.Core.ExitCodes.Usage),
    };
}

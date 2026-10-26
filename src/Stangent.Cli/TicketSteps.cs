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

        var serve = new Command("serve", "Keep the app up while the reproducer works on a Playwright proof: start it, run the related specs once, write tools/app-ready.json, stop on SIGTERM or Ctrl+C (D86).") { TicketCommands.KeyArg };
        serve.SetAction(pr => Cli.Run(pr, "ticket serve", ctx => TicketCommands.Async(async () =>
        {
            using var stop = new CancellationTokenSource();
            using var term = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, c => { c.Cancel = true; stop.Cancel(); });
            using var interrupt = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGINT, c => { c.Cancel = true; stop.Cancel(); });
            return (JsonNode)await AppSession.ServeAsync(ctx.Repo, pr.GetValue(TicketCommands.KeyArg)!, new ShellRunner(), stop.Token);
        })));
        ticket.Subcommands.Add(serve);

        var kindOption = new Option<string>("--kind") { Description = "questions (before work: missing details, clashes, a bug not reproduced) or resolution (from the merged PR).", Required = true };
        var toneOption = new Option<string?>("--tone") { Description = "internal or client (default: tickets.tone). Client drafts may hold no code names or file paths (D74)." };
        var prOption = new Option<string?>("--pr") { Description = "resolution: the merged PR's range, base..head." };
        var prUrlOption = new Option<string?>("--pr-url") { Description = "resolution: a link to the PR, for the comment." };
        var draft = new Command("draft", "Prepare a draft comment for the ticket: the AI words what code found; code checks every claim (D74).") { TicketCommands.KeyArg, kindOption, toneOption, prOption, prUrlOption };
        draft.SetAction(pr => Cli.Run(pr, "ticket draft", ctx =>
        {
            var key = pr.GetValue(TicketCommands.KeyArg)!;
            var step = TicketDrafts.Prepare(ctx.Repo, TicketContext.Load(ctx.Repo), key, pr.GetValue(kindOption)!, pr.GetValue(toneOption), pr.GetValue(prOption), pr.GetValue(prUrlOption));
            return SpecCommands.Finish(step, key);
        }));
        ticket.Subcommands.Add(draft);

        var draftArg = new Argument<string>("draft") { Description = "The draft: questions or resolution (with the default tone), questions-client, or a path to its .json." };
        var againOption = new Option<bool>("--again") { Description = "Post a draft that was already posted." };
        var post = new Command("post", "Post a checked draft under your own Jira login, and record it (D74). Nothing else is written to the ticket.") { TicketCommands.KeyArg, draftArg, againOption };
        post.SetAction(pr => Cli.Run(pr, "ticket post", ctx => TicketCommands.Async(async () =>
        {
            await using var tracker = TicketCommands.Tracker(ctx);
            return (JsonNode)await TicketDrafts.PostAsync(ctx.Repo, tracker, pr.GetValue(TicketCommands.KeyArg)!, pr.GetValue(draftArg)!, TicketCommands.Who(ctx), pr.GetValue(againOption), TimeProvider.System);
        })));
        ticket.Subcommands.Add(post);
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
        "questions" => TicketDrafts.Prepare(ctx.Repo, TicketContext.Load(ctx.Repo), key, "questions", null, null, null),
        _ => throw new Stangent.Core.StangentException("usage", $"--step {step.Name} isn't built yet.", Stangent.Core.ExitCodes.Usage),
    };

    public static RunStep Check(CliContext ctx, StepSpec step, string key, Usage? usage, JsonNode? claudeResult) => step.Name switch
    {
        "prove" => TicketCommands.Async(() => BugProof.CheckAsync(ctx.Repo, key, claudeResult, new ShellRunner())),
        "questions" or "resolution" => TicketDrafts.Check(ctx.Repo, TicketContext.Load(ctx.Repo), key, step.Name, usage),
        _ => throw new Stangent.Core.StangentException("usage", $"--step {step.Name} isn't built yet.", Stangent.Core.ExitCodes.Usage),
    };
}

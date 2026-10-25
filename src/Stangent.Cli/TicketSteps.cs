using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Spec.Pages;
using Stangent.Tickets;

/// <summary>The ticket commands after the starting points: bundle, prove, drafts and posting (filled in step by step).</summary>
static class TicketSteps
{
    public static void Add(Command ticket) { }

    public static RunStep Prepare(CliContext ctx, StepSpec step, string key, ParseResult pr) =>
        throw new Stangent.Core.StangentException("usage", $"--step {step.Name} isn't built yet.", Stangent.Core.ExitCodes.Usage);

    public static RunStep Check(CliContext ctx, StepSpec step, string key, Usage? usage) =>
        throw new Stangent.Core.StangentException("usage", $"--step {step.Name} isn't built yet.", Stangent.Core.ExitCodes.Usage);
}

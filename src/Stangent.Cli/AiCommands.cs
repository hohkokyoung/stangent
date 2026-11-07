using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;

/// <summary><c>stangent ai …</c>: the settings every AI step runs with, the saved answers made under them, and the repeat check (D161).</summary>
static class AiCommands
{
    public static void Add(RootCommand root)
    {
        var ai = new Command("ai", "The model and effort every AI step runs with, and whether a step gives the same answer twice (D161).");
        root.Subcommands.Add(ai);

        var agentArg = new Argument<string?>("agent") { Description = "The step's agent, as a step's next.agent names it: stangent-link-judge.", Arity = ArgumentArity.ZeroOrOne };
        var allOption = new Option<bool>("--all") { Description = "Every step at once: refuses if any one has no model or effort (a driver asks this before its first call)." };
        var modelOption = new Option<string?>("--model") { Description = "This run's model, over stangent.yaml's." };
        var effortOption = new Option<string?>("--effort") { Description = "This run's effort (low, medium, high, xhigh, max), over stangent.yaml's." };
        var settings = new Command("settings", "The model and effort one step runs with: a run's flag, then ai.steps.<agent>, then ai. Refuses when either is missing; never a default.") { agentArg, allOption, modelOption, effortOption };
        settings.SetAction(pr => Cli.Run(pr, "ai settings", ctx =>
            pr.GetValue(allOption)
                ? new JsonObject { ["steps"] = new JsonArray([.. AiSettings.ResolveAll(ctx.Repo.Config, pr.GetValue(modelOption), pr.GetValue(effortOption)).Select(s => (JsonNode)s.ToJson())]) }
                : AiSettings.Resolve(ctx.Repo.Config, pr.GetValue(agentArg) ?? throw new StangentException("usage", "Name a step's agent, or pass --all.", ExitCodes.Usage),
                    pr.GetValue(modelOption), pr.GetValue(effortOption)).ToJson()));
        ai.Subcommands.Add(settings);

        var differsOption = new Option<bool>("--differs") { Description = "Only answers made under settings other than those pinned now, or not known." };
        var runs = new Command("runs", "Every saved AI answer under specs/ and the ticket folders, with the model and effort it ran with. Changes nothing.") { differsOption };
        runs.SetAction(pr => Cli.Run(pr, "ai runs", ctx => AiRuns.List(ctx.Repo, pr.GetValue(differsOption))));
        ai.Subcommands.Add(runs);

        var filesArg = new Argument<string[]>("answers") { Description = "Two or more answers to the same call, as JSON files.", Arity = ArgumentArity.OneOrMore };
        var schemaOption = new Option<string>("--schema") { Description = "The call's output schema, as a JSON file: a field is free text only where it leaves it a plain string.", Required = true };
        var compare = new Command("compare", "Compare answers to one identical call by code: free-text fields dropped, lists as sets. Steady only when every run answered and gives every item.") { filesArg, schemaOption };
        compare.SetAction(pr => Cli.Run(pr, "ai compare", ctx =>
            AiCompare.Compare([.. pr.GetValue(filesArg)!.Select(f => File.Exists(f) && File.ReadAllText(f).Trim().Length > 0 ? Cli.ReadJson(f) : null)],
                Cli.ReadJson(pr.GetValue(schemaOption)!) ?? throw new StangentException("usage", "The schema file is empty.", ExitCodes.Usage))));
        ai.Subcommands.Add(compare);
    }
}

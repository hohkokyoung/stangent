using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Tickets;
using Stangent.Tickets.Secrets;

/// <summary><c>stangent ticket …</c>: a Jira ticket becomes a checked context bundle (Phase 3, D69–D82).</summary>
static class TicketCommands
{
    public static readonly Argument<string> KeyArg = new("key") { Description = "The ticket key, e.g. PROJ-123." };

    public static void Add(RootCommand root)
    {
        var ticket = new Command("ticket", "Jira tickets: pull, starting points, the bundle, bug proof, drafts and posting (Phase 3).");
        root.Subcommands.Add(ticket);

        var pull = new Command("pull", "Read a ticket, its comments and attachments into .stangent/tickets/<KEY>/ (code only, D72).") { KeyArg };
        pull.SetAction(pr => Cli.Run(pr, "ticket pull", ctx => Async(async () =>
        {
            await using var tracker = Tracker(ctx);
            var t = await TicketPull.RunAsync(ctx.Repo, tracker, pr.GetValue(KeyArg)!);
            var summary = Summary(ctx, t);
            summary["read"] = tracker.Replaying ? "replay" : "live";
            return (JsonNode)summary;
        })));
        ticket.Subcommands.Add(pull);

        var addOption = new Option<string[]>("--add") { Description = "Add a starting point: a page (id, path or entry point), a symbol (graph id or unique name) or a file (its path in the repo).", AllowMultipleArgumentsPerToken = false };
        var removeOption = new Option<string[]>("--remove") { Description = "Remove a starting point (recorded with who removed it)." };
        var addOutsideOption = new Option<string[]>("--add-outside") { Description = "Mark a part outside the code: '<area>: <what>', area server, network, deployment, data, third-party or other (D83)." };
        var removeOutsideOption = new Option<string[]>("--remove-outside") { Description = "Remove the parts outside the code in an area, or all; with nothing left and no starting point, bug proof runs again (D83)." };
        var refindOption = new Option<bool>("--refind") { Description = "Search for the ticket's files again (D101): the finder's answer is kept while the ticket is unchanged, and marked stale when the code moved on." };
        var points = new Command("points", "The ticket's starting points: the files the file finder found and code checked (D101; code's signals when it's off, D70), your changes; parts outside the code (D83).") { KeyArg, addOption, removeOption, addOutsideOption, removeOutsideOption, refindOption };
        points.SetAction(pr => Cli.Run(pr, "ticket points", ctx =>
        {
            var key = pr.GetValue(KeyArg)!;
            var result = TicketPoints.Update(ctx.Repo, TicketContext.Load(ctx.Repo), key, pr.GetValue(addOption) ?? [], pr.GetValue(removeOption) ?? [], Who(ctx),
                pr.GetValue(addOutsideOption) ?? [], pr.GetValue(removeOutsideOption) ?? [], pr.GetValue(refindOption));
            ctx.Span.Attributes["stangent.artifact.count"] = result["points"]!.AsArray().Count;
            if ((string?)result["ai"]?["status"] == "needed")
                result["next"] = new JsonObject { ["action"] = "prepare", ["command"] = $"stangent ticket prepare {key} --step points" };
            return result;
        }));
        ticket.Subcommands.Add(points);

        var stepOption = new Option<string>("--step") { Description = "The AI step: points, match, prove, questions or resolution.", Required = true };
        var prepare = new Command("prepare", "Write an AI step's input and name the subagent to run (D60).") { KeyArg, stepOption };
        prepare.SetAction(pr => Cli.Run(pr, "ticket prepare", ctx =>
        {
            var key = pr.GetValue(KeyArg)!;
            var step = StepSpec.Named(pr.GetValue(stepOption)!);
            var run = step.Name switch
            {
                "points" => TicketPoints.Prepare(ctx.Repo, TicketContext.Load(ctx.Repo), key),
                _ => TicketSteps.Prepare(ctx, step, key, pr),
            };
            var json = SpecCommands.Finish(run, key);
            // The file finder searches the repo itself (D101): the driver runs it under these limits.
            if (step.Name == "points" && run.Agent is not null) json["next"]!["limits"] = TicketPoints.DriverLimits(ctx.Repo, key);
            return json;
        }));
        ticket.Subcommands.Add(prepare);

        var usageOption = new Option<string?>("--usage") { Description = "Claude Code's JSON result for the step just run." };
        var check = new Command("check", "Check an AI step's output by code (D18), record it, and say what runs next.") { KeyArg, stepOption, usageOption };
        check.SetAction(pr => Cli.Run(pr, "ticket check", ctx =>
        {
            var key = pr.GetValue(KeyArg)!;
            var step = StepSpec.Named(pr.GetValue(stepOption)!);
            var usage = SpecCommands.ReadUsage(ctx, pr.GetValue(usageOption));
            var claudeResult = pr.GetValue(usageOption) is { } file ? Cli.ReadJson(file) : null;
            var run = step.Name switch
            {
                "points" => TicketPoints.Check(ctx.Repo, TicketContext.Load(ctx.Repo), key, usage, Who(ctx)),
                _ => TicketSteps.Check(ctx, step, key, usage, claudeResult),
            };
            if (step.Name == "points" && run.Status == "retry")
            {
                var retry = run.ToJson(key);
                retry["next"]!["limits"] = TicketPoints.DriverLimits(ctx.Repo, key);
                throw new CheckFailed("check_failed", $"{key}: the code check failed; the file finder gets one retry with the errors.", retry);
            }
            // A reproducer's retry runs under what's left of the attempt's cap (D81).
            if (step.Name == "prove" && run.Status == "retry")
            {
                var retry = run.ToJson(key);
                retry["next"]!["limits"] = Stangent.Tickets.Proof.BugProof.DriverLimits(ctx.Repo, key);
                throw new CheckFailed("check_failed", $"{key}: the code check failed; the reproducer gets one retry with the errors.", retry);
            }
            return SpecCommands.Finish(run, key);
        }));
        ticket.Subcommands.Add(check);

        TicketSteps.Add(ticket);

    }

    public static IIssueTracker Tracker(CliContext ctx) =>
        Trackers.Open(ctx.Repo, SecretStores.Os(), Console.Error, new FolderRawSink(TicketPaths.TicketsDir(ctx.Repo.Root)));

    /// <summary>Who made a change, for the record (D70): git's user.name in the repo, else "developer".</summary>
    public static string Who(CliContext ctx)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", "config user.name") { WorkingDirectory = ctx.Repo.Root, RedirectStandardOutput = true, RedirectStandardError = true };
        try
        {
            using var p = System.Diagnostics.Process.Start(psi)!;
            var name = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return name.Length > 0 ? name : "developer";
        }
        catch (System.ComponentModel.Win32Exception) { return "developer"; }
    }

    public static T Async<T>(Func<Task<T>> body) => Task.Run(body).GetAwaiter().GetResult();

    static JsonObject Summary(CliContext ctx, JsonObject t)
    {
        var key = (string)t["key"]!;
        var paths = new TicketPaths(ctx.Repo.Root, key);
        return new JsonObject
        {
            ["key"] = key, ["source"] = t["source"]!.DeepClone(), ["type"] = t["ticket"]!["type"]!.DeepClone(),
            ["summary"] = t["ticket"]!["summary"]!.DeepClone(), ["isBug"] = t["isBug"]!.DeepClone(),
            ["comments"] = t["comments"]!.AsArray().Count,
            ["attachments"] = new JsonArray([.. t["attachments"]!.AsArray().Select(a => (JsonNode)new JsonObject
            {
                ["fileName"] = a!["fileName"]!.DeepClone(), ["kind"] = a["kind"]!.DeepClone(), ["hash"] = a["hash"]?.DeepClone(),
                ["chars"] = a["chars"]?.DeepClone(), ["truncated"] = a["truncated"]?.DeepClone(), ["notDownloaded"] = a["notDownloaded"]?.DeepClone(),
            })]),
            ["missingBugFields"] = t["bug"]?["missing"]?.DeepClone(),
            ["file"] = paths.Relative(paths.TicketJson),
        };
    }
}

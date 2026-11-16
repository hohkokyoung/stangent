using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Tickets.Fix;
using Stangent.Tickets.Proof;

/// <summary><c>stangent fix …</c>: Phase 4's minimal fix loop (D343–D350). <c>plugin/scripts/fix-run.sh</c> drives it; nothing here pushes.</summary>
static class FixCommands
{
    public static void Add(RootCommand root)
    {
        var fix = new Command("fix", "Phase 4's fix loop: one fix agent per bug on its own branch, every check by code (D343–D350). Never pushes.");
        root.Subcommands.Add(fix);
        var key = TicketCommands.KeyArg;

        var baseOption = new Option<string?>("--base") { Description = "The commit or ref the branch starts from (default: HEAD)." };
        var replaceOption = new Option<bool>("--replace") { Description = "Remove this ticket's earlier fix worktree and its stangent/fix-<KEY> branch first." };
        var start = new Command("start", "Branch stangent/fix-<KEY> in a worktree at .stangent/fix/<KEY>/worktree, with the bug proof's failing test committed first (D346, D350).") { key, baseOption, replaceOption };
        start.SetAction(pr => Cli.Run(pr, "fix start", ctx => FixLoop.Start(ctx.Repo, pr.GetValue(key)!, pr.GetValue(baseOption), pr.GetValue(replaceOption))));
        fix.Subcommands.Add(start);

        var contextOption = new Option<string>("--context") { Description = "What the fixer gets (D345, D357): files (the ticket, the code, the failing test and the finder's files, the default), ticket (without the files) or bundle (the whole ticket bundle).", DefaultValueFactory = _ => "files" };
        var prepare = new Command("prepare", "Write the fixer's input and the limits the driver runs it under.") { key, contextOption };
        prepare.SetAction(pr => Cli.Run(pr, "fix prepare", ctx => FixLoop.Prepare(ctx.Repo, pr.GetValue(key)!, pr.GetValue(contextOption)!)));
        fix.Subcommands.Add(prepare);

        var usageOption = new Option<string?>("--usage") { Description = "Claude Code's JSON result for the fixer call just run, with the driver's rounds." };
        var check = new Command("check", "Run the checks in order (plan, files, build, tests, test reach), give one retry, and say what runs next.") { key, usageOption };
        check.SetAction(pr => Cli.Run(pr, "fix check", ctx =>
        {
            var file = pr.GetValue(usageOption);
            SpecCommands.ReadUsage(ctx, file);
            var result = TicketCommands.Async(() => FixLoop.CheckAsync(ctx.Repo, pr.GetValue(key)!, file is null ? null : Cli.ReadJson(file), new ShellRunner(), Rebuild));
            if ((string?)result["status"] == "retry")
                throw new CheckFailed("check_failed", $"{pr.GetValue(key)}: a check failed; the fixer gets one retry with the failures.", result);
            return result;
        }));
        fix.Subcommands.Add(check);

        var report = new Command("report", "Write report.md and agent-run.json: why it stopped, the plan, each check, the branch (D349).") { key };
        report.SetAction(pr => Cli.Run(pr, "fix report", ctx =>
        {
            var run = FixLoop.Report(ctx.Repo, pr.GetValue(key)!);
            // D21, from the record: the run's settings and the fixer's tokens. Its cost stays on each `fix check` event,
            // where cost estimates read it, so it isn't counted twice.
            if ((string?)run["model"] is { } model) ctx.Span.Attributes["gen_ai.request.model"] = model;
            var fixer = run["steps"]!.AsArray().OfType<JsonObject>().Where(s => (string)s["name"]! == "fixer").ToList();
            if (fixer.Count > 0)
            {
                ctx.Span.Attributes["gen_ai.provider.name"] = "anthropic";
                ctx.Span.Attributes["gen_ai.usage.input_tokens"] = fixer.Sum(s => (long?)s["inputTokens"] ?? 0);
                ctx.Span.Attributes["gen_ai.usage.output_tokens"] = fixer.Sum(s => (long?)s["outputTokens"] ?? 0);
            }
            ctx.Span.Attributes["stangent.phase"] = "4";
            ctx.Span.Attributes["stangent.artifact.count"] = run["checks"]!.AsArray().Count;
            var paths = new FixPaths(ctx.Repo.Root, pr.GetValue(key)!);
            return new JsonObject
            {
                ["key"] = run["key"]!.DeepClone(), ["stop"] = run["stop"]!.DeepClone(), ["branch"] = run["branch"]!.DeepClone(),
                ["costUsd"] = run["costUsd"]!.DeepClone(), ["report"] = paths.Relative(paths.Report), ["agentRun"] = paths.Relative(paths.AgentRun),
            };
        }));
        fix.Subcommands.Add(report);
    }

    /// <summary>The graph rebuilt in the worktree from its copy of the base graph: only what changed is extracted again.</summary>
    static async Task<CodeGraph?> Rebuild(StangentRepo worktree, CancellationToken ct) =>
        (await GraphBuilder.BuildAsync(worktree, new BuildOptions(Restore: false, Full: false, Log: TextWriter.Null), ct)).Graph;
}

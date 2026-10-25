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

    }

    public static IIssueTracker Tracker(CliContext ctx) =>
        Trackers.Open(ctx.Repo, SecretStores.Os(), Console.Error, new FolderRawSink(TicketPaths.TicketsDir(ctx.Repo.Root)));

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

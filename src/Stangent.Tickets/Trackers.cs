using Stangent.Core;
using Stangent.Tickets.Jira;
using Stangent.Tickets.Secrets;

namespace Stangent.Tickets;

/// <summary>Jira through Atlassian's MCP server (D73, D82). Opens the connection on first use, so code paths that never
/// reach Jira never sign in.</summary>
public sealed class JiraTracker(StangentConfig.TicketsConfig settings, Func<CancellationToken, Task<IJiraTools>> connect, IRawSink? sink)
    : IIssueTracker
{
    IJiraTools? connection;
    public string Source => "jira";
    public bool Replaying => false;
    string Site => settings.Site ?? throw new StangentException("no_jira_site", "Set tickets.site in stangent.yaml (e.g. acme.atlassian.net).", ExitCodes.Usage);

    async Task<IJiraTools> Connect(CancellationToken ct) => connection ??= await connect(ct);

    public async Task<Ticket> GetAsync(string key, CancellationToken ct = default)
    {
        Trackers.CheckProject(settings, key);
        var c = await Connect(ct);
        var issue = Raw(key, JiraParser.IssueTool, await c.CallAsync(JiraParser.IssueTool, new Dictionary<string, object?>
        {
            ["cloudId"] = Site, ["issueIdOrKey"] = key, ["view"] = "full", ["responseContentFormat"] = "markdown",
        }, ct));
        var pages = new List<RawResult>();
        var (total, embedded) = JiraParser.CommentCounts(issue);
        for (var start = 0; total > embedded; )
        {
            var page = Raw(key, $"{JiraParser.CommentsOperation}-{start}", await c.CallAsync("executeRead", new Dictionary<string, object?>
            {
                ["cloudId"] = Site, ["name"] = JiraParser.CommentsOperation,
                ["inputs"] = new Dictionary<string, object?>
                {
                    ["cloudId"] = Site, ["issueIdOrKey"] = key, ["startAt"] = start, ["maxResults"] = 100,
                    ["orderBy"] = "created", ["responseContentFormat"] = "markdown",
                },
            }, ct));
            pages.Add(page);
            if (JiraParser.IsLastPage(page) || pages.Count >= 50) break;
            start += 100;
        }
        return JiraParser.Parse(issue, pages);
    }

    RawResult Raw(string key, string name, ToolResult result)
    {
        var raw = RawResult.From(name, result.Raw);
        sink?.Save(key, raw);
        return raw;
    }

    public async Task<SavedFile> DownloadAsync(Ticket ticket, TicketAttachment attachment, string destination, CancellationToken ct = default)
    {
        var d = await (await Connect(ct)).DownloadAttachmentAsync(Site, attachment.Id, destination, ct);
        return new SavedFile(d.Path, d.Bytes, d.Hash);
    }

    public async Task<PostedComment> PostCommentAsync(string key, string markdown, CancellationToken ct = default)
    {
        Trackers.CheckProject(settings, key);
        var r = await (await Connect(ct)).CallAsync("addOrEditJiraIssueComment", new Dictionary<string, object?>
        {
            ["cloudId"] = Site, ["issueIdOrKey"] = key, ["commentBody"] = markdown, ["contentFormat"] = "markdown",
        }, ct);
        var id = (string?)JiraParser.Payload(RawResult.From("addOrEditJiraIssueComment", r.Raw))["data"]?["commentId"]
            ?? throw new StangentException("jira_unexpected_output", "addOrEditJiraIssueComment returned no data.commentId; check the ticket before posting again.");
        return new PostedComment(key, id);
    }

    public ValueTask DisposeAsync() => connection?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>
/// Tickets from saved MCP results (D77): <c>&lt;dir&gt;/&lt;KEY&gt;/raw/*.json</c> and <c>&lt;dir&gt;/&lt;KEY&gt;/attachments/&lt;id&gt;/&lt;file&gt;</c>,
/// the layout a live pull leaves in <c>.stangent/tickets/</c>. Read only: posting is refused.
/// </summary>
public sealed class ReplayTracker(string dir, IRawSink? sink = null) : IIssueTracker
{
    /// <summary>Saved results are Atlassian MCP results: the tickets live in Jira.</summary>
    public string Source => "jira";
    public bool Replaying => true;

    public Task<Ticket> GetAsync(string key, CancellationToken ct = default)
    {
        var raw = Path.Combine(dir, TicketKeys.Check(key), "raw");
        var issuePath = Path.Combine(raw, JiraParser.IssueTool + ".json");
        if (!File.Exists(issuePath))
            throw new StangentException("not_found", $"No saved result for {key} in {dir} (expected {key}/raw/{JiraParser.IssueTool}.json).", ExitCodes.NotFound);
        var issue = Load(issuePath);
        // As the live backend does: comment pages only when the issue doesn't embed every comment.
        var (total, embedded) = JiraParser.CommentCounts(issue);
        var pages = total <= embedded ? [] : Directory.EnumerateFiles(raw, JiraParser.CommentsOperation + "-*.json")
            .Select(p => (Start: int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-')[^1], out var s) ? s : -1, Path: p))
            .Where(p => p.Start >= 0).OrderBy(p => p.Start).Select(p => Load(p.Path)).ToList();
        foreach (var r in pages.Prepend(issue)) sink?.Save(key, r);
        return Task.FromResult(JiraParser.Parse(issue, pages));
    }

    /// <summary>The saved file is re-read and stored as canonical JSON, so its hash is the same as when it was first read.</summary>
    static RawResult Load(string path) =>
        RawResult.From(Path.GetFileNameWithoutExtension(path), System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!);

    public async Task<SavedFile> DownloadAsync(Ticket ticket, TicketAttachment attachment, string destination, CancellationToken ct = default)
    {
        var folder = Path.Combine(dir, ticket.Key, "attachments", attachment.Id);
        var source = Directory.Exists(folder) ? Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal).FirstOrDefault() : null;
        if (source is null)
            throw new StangentException("not_found", $"Attachment {attachment.Id} ({attachment.FileName}) of {ticket.Key} wasn't saved in {dir}.", ExitCodes.NotFound);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        if (Path.GetFullPath(source) != Path.GetFullPath(destination)) File.Copy(source, destination, overwrite: true);
        var bytes = await File.ReadAllBytesAsync(destination, ct);
        return new SavedFile(destination, bytes.LongLength, Trackers.Hash(bytes));
    }

    public Task<PostedComment> PostCommentAsync(string key, string markdown, CancellationToken ct = default) =>
        throw new StangentException("replay_read_only", "Tickets come from saved results (tickets.replay), which can't be posted to.", ExitCodes.Usage);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public static class Trackers
{
    /// <summary>The tracker <c>stangent.yaml</c> asks for: the replay folder when <c>tickets.replay</c> is set (or
    /// <c>adapters.tickets: file</c>), otherwise Jira.</summary>
    public static IIssueTracker Open(StangentRepo repo, ISecretStore secrets, TextWriter log, IRawSink? sink)
    {
        var t = repo.Config.Tickets;
        var adapter = repo.Config.Adapters.Tickets ?? (t.Replay is null ? "jira" : "file");
        switch (adapter)
        {
            case "file":
                if (t.Replay is null) throw new StangentException("no_replay_dir", "adapters.tickets is file: set tickets.replay to the folder of saved results.", ExitCodes.Usage);
                return new ReplayTracker(Path.GetFullPath(Path.Combine(repo.Root, t.Replay)), sink);
            case "jira":
                var options = ConnectionOptions(t);
                return new JiraTracker(t, async ct => await JiraConnection.OpenAsync(options, secrets, log, ct), sink);
            default:
                throw new StangentException("unknown_adapter", $"adapters.tickets: '{adapter}' isn't one stangent has (jira, file).", ExitCodes.Usage);
        }
    }

    public static JiraConnectionOptions ConnectionOptions(StangentConfig.TicketsConfig t) => new()
    {
        Endpoint = new Uri(t.Endpoint),
        Auth = t.Auth.Equals("apiToken", StringComparison.OrdinalIgnoreCase) ? JiraAuth.ApiToken : JiraAuth.OAuth,
        Email = t.Email,
    };

    /// <summary>With <c>tickets.projects</c> set, keys from other projects are refused (no reading or posting elsewhere by mistake).</summary>
    public static void CheckProject(StangentConfig.TicketsConfig t, string key)
    {
        TicketKeys.Check(key);
        var project = key[..key.LastIndexOf('-')];
        if (t.Projects.Count > 0 && !t.Projects.Contains(project, StringComparer.Ordinal))
            throw new StangentException("other_project", $"{key} isn't in tickets.projects ({string.Join(", ", t.Projects)}).", ExitCodes.Usage);
    }

    public static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
}

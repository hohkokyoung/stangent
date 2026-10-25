using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Tickets;

/// <summary>
/// One ticket as stangent reads it: only the fields code names (D73), whichever backend it came from. Text fields are
/// as the tracker gave them; <see cref="DescriptionFormat"/> says whether that is Markdown or HTML (Jira returns HTML
/// for bodies Markdown can't hold).
/// </summary>
public sealed record Ticket
{
    public required string Key { get; init; }
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required string Description { get; init; }
    public required string DescriptionFormat { get; init; }
    public string? Environment { get; init; }
    public string? Priority { get; init; }
    public string? Reporter { get; init; }
    public string? Created { get; init; }
    public string? Updated { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
    /// <summary>Affects versions.</summary>
    public IReadOnlyList<string> Versions { get; init; } = [];
    /// <summary>Custom fields by their label, as text.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<TicketComment> Comments { get; init; } = [];
    public IReadOnlyList<TicketLink> Links { get; init; } = [];
    public IReadOnlyList<TicketAttachment> Attachments { get; init; } = [];
    /// <summary>The raw results it was read from, by name, each with its hash (D73).</summary>
    public IReadOnlyList<RawResult> Raw { get; init; } = [];

    public JsonObject ToJson() => new()
    {
        ["key"] = Key, ["id"] = Id, ["type"] = Type, ["status"] = Status, ["summary"] = Summary,
        ["description"] = Description, ["descriptionFormat"] = DescriptionFormat,
        ["environment"] = Environment, ["priority"] = Priority, ["reporter"] = Reporter, ["created"] = Created, ["updated"] = Updated,
        ["labels"] = Strings(Labels), ["versions"] = Strings(Versions),
        ["fields"] = new JsonObject(Fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => KeyValuePair.Create(f.Key, (JsonNode?)f.Value))),
        ["comments"] = new JsonArray([.. Comments.Select(c => (JsonNode)new JsonObject
        {
            ["id"] = c.Id, ["author"] = c.Author, ["created"] = c.Created, ["body"] = c.Body, ["format"] = c.Format,
        })]),
        ["links"] = new JsonArray([.. Links.Select(l => (JsonNode)new JsonObject
        {
            ["type"] = l.Type, ["direction"] = l.Direction, ["key"] = l.Key, ["summary"] = l.Summary, ["status"] = l.Status,
        })]),
        ["attachments"] = new JsonArray([.. Attachments.Select(a => (JsonNode)new JsonObject
        {
            ["id"] = a.Id, ["fileName"] = a.FileName, ["mimeType"] = a.MimeType, ["size"] = a.Size,
        })]),
        ["raw"] = new JsonArray([.. Raw.Select(r => (JsonNode)new JsonObject { ["name"] = r.Name, ["hash"] = r.Hash })]),
    };

    static JsonArray Strings(IEnumerable<string> list) => new([.. list.Select(s => (JsonNode)s)]);
}

public sealed record TicketComment(string Id, string Author, string Created, string Body, string Format);

/// <summary>A link to another issue: <see cref="Direction"/> is <c>outward</c> ("blocks KEY") or <c>inward</c> ("is blocked by KEY").</summary>
public sealed record TicketLink(string Type, string Direction, string Key, string? Summary, string? Status);

public sealed record TicketAttachment(string Id, string FileName, string? MimeType, long Size);

/// <summary>One raw tool result, canonical JSON, and its hash.</summary>
public sealed record RawResult(string Name, string Hash, string Json)
{
    public static RawResult From(string name, JsonNode raw)
    {
        var text = CanonicalJson.Serialize(raw);
        return new RawResult(name, "sha256:" + CanonicalJson.Hash(text), text);
    }
}

/// <summary>A file a ticket attachment was saved to, with its content hash (D72).</summary>
public sealed record SavedFile(string Path, long Bytes, string Hash);

public sealed record PostedComment(string Key, string CommentId);

/// <summary>
/// The issue-tracker adapter (D24, D73): every ticket step reads and writes through this, never through Jira directly.
/// Backends: Jira through Atlassian's MCP server, and a replay of saved results (tests, the benchmark, D77).
/// </summary>
public interface IIssueTracker : IAsyncDisposable
{
    /// <summary><c>jira</c> or <c>replay</c>: recorded in the bundle.</summary>
    string Source { get; }
    Task<Ticket> GetAsync(string key, CancellationToken ct = default);
    Task<SavedFile> DownloadAsync(Ticket ticket, TicketAttachment attachment, string destination, CancellationToken ct = default);
    /// <summary>Posts a comment under the signed-in person's own login (D74). Nothing else is ever written.</summary>
    Task<PostedComment> PostCommentAsync(string key, string markdown, CancellationToken ct = default);
}

/// <summary>Where a ticket's raw results go as they are read, so a replay can read them back (D77).</summary>
public interface IRawSink
{
    void Save(string key, RawResult raw);
}

/// <summary>Raw results under <c>&lt;dir&gt;/&lt;KEY&gt;/raw/&lt;name&gt;.json</c>: the layout the replay backend reads.</summary>
public sealed class FolderRawSink(string dir) : IRawSink
{
    public void Save(string key, RawResult raw)
    {
        var path = Path.Combine(dir, TicketKeys.Check(key), "raw", raw.Name + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, raw.Json);
    }
}

public static class TicketKeys
{
    /// <summary>A Jira key (<c>PROJ-123</c>); anything else is refused, since keys become folder names.</summary>
    public static string Check(string key) =>
        System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Z][A-Z0-9_]*-[0-9]+$") ? key
            : throw new StangentException("bad_ticket_key", $"'{key}' isn't a ticket key like PROJ-123.", ExitCodes.Usage);
}

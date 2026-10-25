using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Tickets.Jira;

/// <summary>
/// Turns Atlassian MCP v2 results into a <see cref="Ticket"/> (D73, D82). Both backends use it: the live Jira backend on
/// what it just read, the replay backend on what was saved, so a replay gives the same ticket byte for byte. It reads
/// only the fields it names; a required one that's missing or of the wrong shape fails with
/// <c>jira_unexpected_output</c>, so a change in Atlassian's output is an error, never a quietly emptier ticket.
/// </summary>
public static class JiraParser
{
    public const string IssueTool = "getJiraIssue";
    public const string CommentsOperation = "listJiraIssueComments";

    /// <summary>The JSON a tool result carries: its structured content, or the first JSON value in its text
    /// (some operations add prose after the JSON).</summary>
    public static JsonObject Payload(RawResult raw)
    {
        var node = JsonNode.Parse(raw.Json)!;
        if (node["structuredContent"] is JsonObject structured) return structured;
        var text = new StringBuilder();
        foreach (var block in node["content"]?.AsArray() ?? [])
            if ((string?)block?["type"] == "text") text.Append((string?)block["text"]);
        return FirstJson(text.ToString()) as JsonObject
            ?? throw Unexpected(raw.Name, "the result holds no JSON object");
    }

    static JsonNode? FirstJson(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;
        var bytes = Encoding.UTF8.GetBytes(text[start..]);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowTrailingCommas = false });
        try { return JsonNode.Parse(ref reader); }
        catch (JsonException) { return null; }
    }

    /// <summary>How many comments the issue has, and how many its result carries (getJiraIssue embeds at most 20).</summary>
    public static (int Total, int Embedded) CommentCounts(RawResult issue)
    {
        var comment = Fields(issue)["comment"] as JsonObject;
        var embedded = (comment?["comments"] as JsonArray)?.Count ?? 0;
        return (Int(comment?["total"]) ?? embedded, embedded);
    }

    /// <summary>One page of <c>listJiraIssueComments</c>: whether it's the last one.</summary>
    public static bool IsLastPage(RawResult page)
    {
        var data = Data(page);
        if (data["isLast"] is JsonValue v && v.TryGetValue<bool>(out var last)) return last;
        var start = Int(data["startAt"]) ?? 0;
        var count = (data["comments"] as JsonArray)?.Count ?? 0;
        var total = Int(data["total"]) ?? 0;
        return count == 0 || start + count >= total;
    }

    /// <param name="commentPages">Every page of <c>listJiraIssueComments</c>, when the issue had more comments than it embeds.</param>
    public static Ticket Parse(RawResult issue, IReadOnlyList<RawResult> commentPages)
    {
        var data = Data(issue);
        var f = Fields(issue);
        string Need(JsonNode? n, string path) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : throw Unexpected(issue.Name, $"missing {path}");
        var format = (string?)data["appliedContentFormat"] ?? "markdown";

        var comments = commentPages.Count > 0
            ? [.. commentPages.SelectMany(p => Comments(p, Data(p)["comments"] as JsonArray, (string?)Data(p)["appliedContentFormat"] ?? format))]
            : Comments(issue, (f["comment"] as JsonObject)?["comments"] as JsonArray, format);
        var (total, embedded) = CommentCounts(issue);
        if (commentPages.Count == 0 && total > embedded)
            throw Unexpected(issue.Name, $"{total} comments but only {embedded} embedded, and no {CommentsOperation} pages");

        return new Ticket
        {
            Key = Need(data["key"], "data.key"),
            Id = Need(data["id"], "data.id"),
            Type = Need(f["issuetype"]?["name"], "fields.issuetype.name"),
            Status = Need(f["status"]?["name"], "fields.status.name"),
            Summary = Need(f["summary"], "fields.summary"),
            Description = Text(f["description"]) ?? "",
            DescriptionFormat = format,
            Environment = Text(f["environment"]),
            Priority = (string?)f["priority"]?["name"],
            Reporter = (string?)f["reporter"]?["displayName"],
            Created = (string?)f["created"],
            Updated = (string?)f["updated"],
            Labels = [.. (f["labels"] as JsonArray ?? []).Select(l => (string?)l).OfType<string>()],
            Versions = [.. (f["versions"] as JsonArray ?? []).Select(v => (string?)v?["name"]).OfType<string>()],
            Fields = CustomFields(f["customFields"] as JsonObject),
            Comments = [.. comments.DistinctBy(c => c.Id)],
            Links = Links(issue, f["issuelinks"] as JsonArray),
            Attachments = Attachments(issue, f["attachment"] as JsonArray),
            Raw = [issue, .. commentPages],
        };
    }

    static JsonObject Data(RawResult raw) => Payload(raw)["data"] as JsonObject ?? throw Unexpected(raw.Name, "missing data");

    static JsonObject Fields(RawResult issue) => Data(issue)["fields"] as JsonObject ?? throw Unexpected(issue.Name, "missing data.fields");

    static List<TicketComment> Comments(RawResult raw, JsonArray? list, string format) =>
        [.. (list ?? []).OfType<JsonObject>().Select((c, i) => new TicketComment(
            (string?)c["id"] ?? throw Unexpected(raw.Name, $"comment {i} has no id"),
            (string?)c["author"]?["displayName"] ?? "",
            (string?)c["created"] ?? "",
            Text(c["body"]) ?? throw Unexpected(raw.Name, $"comment {(string?)c["id"]} has no body"),
            format))];

    static List<TicketLink> Links(RawResult raw, JsonArray? list)
    {
        var links = new List<TicketLink>();
        foreach (var l in (list ?? []).OfType<JsonObject>())
        {
            var type = (string?)l["type"]?["name"] ?? throw Unexpected(raw.Name, "an issue link has no type.name");
            var (direction, other) = l["outwardIssue"] is JsonObject o ? ("outward", o) : l["inwardIssue"] is JsonObject i ? ("inward", i)
                : throw Unexpected(raw.Name, $"issue link '{type}' has neither inwardIssue nor outwardIssue");
            links.Add(new TicketLink(type, direction, (string?)other["key"] ?? throw Unexpected(raw.Name, "a linked issue has no key"),
                (string?)other["fields"]?["summary"], (string?)other["fields"]?["status"]?["name"]));
        }
        return links;
    }

    static List<TicketAttachment> Attachments(RawResult raw, JsonArray? list) =>
        [.. (list ?? []).OfType<JsonObject>().Select(a => new TicketAttachment(
            // Ids become folder names: Jira's are numeric, so anything else is refused.
            (string?)a["id"] is { } id && System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9]+$") ? id : throw Unexpected(raw.Name, $"attachment id '{(string?)a["id"]}' isn't numeric"),
            (string?)a["filename"] ?? throw Unexpected(raw.Name, $"attachment {(string?)a["id"]} has no filename"),
            (string?)a["mimeType"],
            Int(a["size"]) ?? 0))];

    /// <summary>Custom fields by label, as text: a string as is; an option or user by its value or name; a list joined.</summary>
    static Dictionary<string, string> CustomFields(JsonObject? custom)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, field) in custom ?? [])
            if (FieldText(field is JsonObject o && o.ContainsKey("value") ? o["value"] : field) is { Length: > 0 } text)
                result[label] = text;
        return result;
    }

    static string? FieldText(JsonNode? n) => n switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v => v.ToJsonString(),
        JsonArray a => string.Join(", ", a.Select(FieldText).OfType<string>()),
        JsonObject o => (string?)o["value"] ?? (string?)o["name"] ?? (string?)o["displayName"] ?? CanonicalJson.SerializeCompact(o),
        _ => n.ToJsonString(),
    };

    static string? Text(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? (int)d : null;

    static StangentException Unexpected(string tool, string what) =>
        new("jira_unexpected_output", $"{tool}: {what}. Atlassian's MCP output may have changed (D73); the saved raw result shows what came back.");
}

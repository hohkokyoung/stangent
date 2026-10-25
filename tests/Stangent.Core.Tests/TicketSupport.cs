using System.Text.Json.Nodes;
using Stangent.Tickets.Jira;

namespace Stangent.Core.Tests;

/// <summary>
/// Made-up tickets in the replay layout (<c>&lt;dir&gt;/&lt;KEY&gt;/raw/getJiraIssue.json</c>, <c>attachments/&lt;id&gt;/&lt;file&gt;</c>), built from
/// SCRUM-7's real MCP v2 output so they have Atlassian's exact shape. No network.
/// </summary>
static class TicketFixtures
{
    public static string Dir => Path.Combine(RepoRoot.Find(), "tests", "fixtures", "tickets");

    public sealed record File(string Id, string Name, string Mime, byte[] Bytes);

    public static void Make(string replayDir, string key, string type, string summary, string description,
        IEnumerable<string>? comments = null, IEnumerable<File>? files = null, Action<JsonObject>? fields = null)
    {
        var raw = JsonNode.Parse(System.IO.File.ReadAllText(Path.Combine(Dir, "SCRUM-7", "raw", JiraParser.IssueTool + ".json")))!;
        var block = raw["content"]![0]!;
        var payload = JsonNode.Parse((string)block["text"]!)!.AsObject();
        var data = payload["data"]!.AsObject();
        data["key"] = key;
        data["id"] = (10000 + Math.Abs(key.GetHashCode() % 9000)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var f = data["fields"]!.AsObject();
        f["issuetype"]!["name"] = type;
        f["summary"] = summary;
        f["description"] = description;
        var list = (comments ?? []).Select((c, i) => (JsonNode)new JsonObject
        {
            ["id"] = (20000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), ["author"] = new JsonObject { ["displayName"] = "Pat Reporter" },
            ["body"] = c, ["created"] = "2026-10-25T10:00:00.000+0800",
        }).ToList();
        f["comment"] = new JsonObject { ["comments"] = new JsonArray([.. list]), ["total"] = list.Count, ["maxResults"] = list.Count, ["startAt"] = 0 };
        var attachments = (files ?? []).ToList();
        f["attachment"] = new JsonArray([.. attachments.Select(a => (JsonNode)new JsonObject
        {
            ["id"] = a.Id, ["filename"] = a.Name, ["mimeType"] = a.Mime, ["size"] = a.Bytes.Length,
        })]);
        fields?.Invoke(f);
        block["text"] = payload.ToJsonString();
        var ticketDir = Path.Combine(replayDir, key);
        Directory.CreateDirectory(Path.Combine(ticketDir, "raw"));
        System.IO.File.WriteAllText(Path.Combine(ticketDir, "raw", JiraParser.IssueTool + ".json"), raw.ToJsonString());
        foreach (var a in attachments)
        {
            Directory.CreateDirectory(Path.Combine(ticketDir, "attachments", a.Id));
            System.IO.File.WriteAllBytes(Path.Combine(ticketDir, "attachments", a.Id, a.Name), a.Bytes);
        }
    }

    /// <summary>A repo whose tickets come from a replay folder inside it.</summary>
    public static TempRepo Repo(string extra = "") => new($"version: 1\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n{extra}");

    public static string ReplayDir(TempRepo repo) => Path.Combine(repo.Root, "replay");
}

using System.Text.Json.Nodes;
using ModelContextProtocol.Authentication;
using Stangent.Core;
using Stangent.Tickets;
using Stangent.Tickets.Jira;
using Stangent.Tickets.Secrets;

namespace Stangent.Core.Tests;

/// <summary>
/// Phase 3 step 1: the issue-tracker adapter (D24, D73, D82). No network: SCRUM-7's real MCP v2 output, captured during
/// the spike with personal details replaced, is replayed, and a fake connection stands in for Atlassian.
/// </summary>
public class TicketAdapterTests
{
    static string Fixtures => Path.Combine(RepoRoot.Find(), "tests", "fixtures", "tickets");
    static RawResult Fixture(string name) => RawResult.From(name, JsonNode.Parse(File.ReadAllText(Path.Combine(Fixtures, "SCRUM-7", "raw", name + ".json")))!);

    [Fact]
    public async Task Replay_reads_the_captured_ticket_field_by_field()
    {
        await using var tracker = new ReplayTracker(Fixtures);
        var t = await tracker.GetAsync("SCRUM-7", TestContext.Current.CancellationToken);
        Assert.Equal("SCRUM-7", t.Key);
        Assert.Equal("10006", t.Id);
        Assert.Equal("Task", t.Type);
        Assert.Equal("To Do", t.Status);
        Assert.Equal("stangent spike: MCP round trip (safe to delete)", t.Summary);
        Assert.StartsWith("Test ticket for stangent Phase 3 step 0 (D73)", t.Description);
        Assert.Equal("markdown", t.DescriptionFormat);
        Assert.Equal("Pat Reporter", t.Reporter);
        Assert.Equal(["stangent-spike"], t.Labels);
        Assert.Equal("Medium", t.Priority);
        Assert.Equal("0|i0000w:", t.Fields["Rank"]);
        var a = Assert.Single(t.Attachments);
        Assert.Equal(new TicketAttachment("10000", "error.log", "text/plain", 619), a);
        Assert.Equal(["10000", "10001", "10002"], t.Comments.Select(c => c.Id));
        Assert.Equal("Setup comment for the stangent spike: the log in error.log shows the failure when saving series 12.", t.Comments[0].Body);
        Assert.Empty(t.Links);
        Assert.Equal(JiraParser.IssueTool, Assert.Single(t.Raw).Name);
    }

    [Fact]
    public async Task Replaying_twice_gives_the_same_bytes_and_hashes()
    {
        await using var tracker = new ReplayTracker(Fixtures);
        var a = await tracker.GetAsync("SCRUM-7", TestContext.Current.CancellationToken);
        var b = await tracker.GetAsync("SCRUM-7", TestContext.Current.CancellationToken);
        Assert.Equal(CanonicalJson.Serialize(a.ToJson()), CanonicalJson.Serialize(b.ToJson()));
        Assert.Matches("^sha256:[0-9a-f]{64}$", a.Raw[0].Hash);
        Assert.Equal(Fixture(JiraParser.IssueTool).Hash, a.Raw[0].Hash);
    }

    [Theory]
    [InlineData("issuetype", "fields.issuetype.name")]
    [InlineData("status", "fields.status.name")]
    [InlineData("summary", "fields.summary")]
    public void A_missing_field_in_Atlassians_output_fails_instead_of_emptying_the_ticket(string field, string path)
    {
        var raw = Edit(Fixture(JiraParser.IssueTool), d => d["data"]!["fields"]!.AsObject().Remove(field));
        var e = Assert.Throws<StangentException>(() => JiraParser.Parse(raw, []));
        Assert.Equal("jira_unexpected_output", e.Code);
        Assert.Contains(path, e.Message);
    }

    [Fact]
    public void More_comments_than_embedded_need_the_comment_pages()
    {
        var raw = Edit(Fixture(JiraParser.IssueTool), d => d["data"]!["fields"]!["comment"]!["total"] = 25);
        Assert.Equal("jira_unexpected_output", Assert.Throws<StangentException>(() => JiraParser.Parse(raw, [])).Code);
        var t = JiraParser.Parse(raw, [Fixture("listJiraIssueComments-0")]);
        Assert.Equal(["10000", "10001", "10002"], t.Comments.Select(c => c.Id));
        Assert.Equal(2, t.Raw.Count);
    }

    [Fact]
    public void Issue_links_and_custom_fields_are_read_by_name()
    {
        var raw = Edit(Fixture(JiraParser.IssueTool), d =>
        {
            var f = d["data"]!["fields"]!.AsObject();
            f["issuelinks"] = JsonNode.Parse("""
                [{"type":{"name":"Blocks"},"outwardIssue":{"key":"SCRUM-2","fields":{"summary":"Task 2","status":{"name":"In Progress"}}}},
                 {"type":{"name":"Relates"},"inwardIssue":{"key":"SCRUM-3","fields":{"summary":"Task 3"}}}]
                """);
            f["customFields"]!["Steps to Reproduce"] = JsonNode.Parse("""{"id":"customfield_10100","value":"1. Open series 12\n2. Save"}""");
            f["customFields"]!["Severity"] = JsonNode.Parse("""{"id":"customfield_10101","value":{"value":"High"}}""");
            f["versions"] = JsonNode.Parse("""[{"name":"4.0.1"}]""");
            f["environment"] = "Docker on Linux";
        });
        var t = JiraParser.Parse(raw, []);
        Assert.Equal([new TicketLink("Blocks", "outward", "SCRUM-2", "Task 2", "In Progress"), new TicketLink("Relates", "inward", "SCRUM-3", "Task 3", null)], t.Links);
        Assert.Equal("1. Open series 12\n2. Save", t.Fields["Steps to Reproduce"]);
        Assert.Equal("High", t.Fields["Severity"]);
        Assert.Equal(["4.0.1"], t.Versions);
        Assert.Equal("Docker on Linux", t.Environment);
    }

    [Fact]
    public async Task Replay_download_gives_the_saved_bytes_with_their_hash()
    {
        var dir = Directory.CreateTempSubdirectory("stangent-tickets-").FullName;
        try
        {
            await using var tracker = new ReplayTracker(Fixtures);
            var t = await tracker.GetAsync("SCRUM-7", TestContext.Current.CancellationToken);
            var saved = await tracker.DownloadAsync(t, t.Attachments[0], Path.Combine(dir, "error.log"), TestContext.Current.CancellationToken);
            Assert.Equal(619, saved.Bytes);
            Assert.Equal("sha256:8993f769d86fc741ddaf7c84b42d64e1a501f9574003a6c7b3f48fc17284bf4b", saved.Hash);
            var missing = new TicketAttachment("999", "gone.png", "image/png", 1);
            Assert.Equal("not_found", (await Assert.ThrowsAsync<StangentException>(() => tracker.DownloadAsync(t, missing, Path.Combine(dir, "x"), TestContext.Current.CancellationToken))).Code);
            Assert.Equal("replay_read_only", (await Assert.ThrowsAsync<StangentException>(() => tracker.PostCommentAsync("SCRUM-7", "hi", TestContext.Current.CancellationToken))).Code);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task The_Jira_backend_asks_for_the_full_view_pages_comments_and_saves_every_raw_result()
    {
        var withMore = Edit(Fixture(JiraParser.IssueTool), d => d["data"]!["fields"]!["comment"]!["total"] = 25);
        var fake = new FakeJira(new() { [JiraParser.IssueTool] = withMore, ["executeRead"] = Fixture("listJiraIssueComments-0") });
        var sink = new MemorySink();
        await using var tracker = new JiraTracker(new StangentConfig.TicketsConfig { Site = "acme.atlassian.net" }, _ => Task.FromResult<IJiraTools>(fake), sink);
        var t = await tracker.GetAsync("SCRUM-7", TestContext.Current.CancellationToken);

        var (tool, args) = fake.Calls[0];
        Assert.Equal(JiraParser.IssueTool, tool);
        Assert.Equal("full", args["view"]);
        Assert.Equal("acme.atlassian.net", args["cloudId"]);
        Assert.Equal("executeRead", fake.Calls[1].Tool);
        Assert.Equal(JiraParser.CommentsOperation, fake.Calls[1].Args["name"]);
        Assert.Equal(2, fake.Calls.Count); // the page says isLast
        Assert.Equal([JiraParser.IssueTool, JiraParser.CommentsOperation + "-0"], sink.Saved.Select(s => s.Name));
        Assert.Equal(3, t.Comments.Count);
    }

    [Fact]
    public async Task Posting_sends_markdown_and_returns_the_new_comment_id()
    {
        var reply = RawResult.From("addOrEditJiraIssueComment", JsonNode.Parse("""{"content":[{"type":"text","text":"{\"data\":{\"commentId\":\"10003\"}}"}]}""")!);
        var fake = new FakeJira(new() { ["addOrEditJiraIssueComment"] = reply });
        await using var tracker = new JiraTracker(new StangentConfig.TicketsConfig { Site = "acme" }, _ => Task.FromResult<IJiraTools>(fake), null);
        var posted = await tracker.PostCommentAsync("SCRUM-7", "**hi**", TestContext.Current.CancellationToken);
        Assert.Equal(new PostedComment("SCRUM-7", "10003"), posted);
        Assert.Equal("markdown", fake.Calls[0].Args["contentFormat"]);
        Assert.Equal("**hi**", fake.Calls[0].Args["commentBody"]);
    }

    [Fact]
    public async Task Keys_outside_the_projects_setting_and_malformed_keys_are_refused()
    {
        var fake = new FakeJira([]);
        await using var tracker = new JiraTracker(new StangentConfig.TicketsConfig { Site = "acme", Projects = ["SCRUM"] }, _ => Task.FromResult<IJiraTools>(fake), null);
        Assert.Equal("other_project", (await Assert.ThrowsAsync<StangentException>(() => tracker.PostCommentAsync("OPS-1", "x", TestContext.Current.CancellationToken))).Code);
        Assert.Equal("bad_ticket_key", (await Assert.ThrowsAsync<StangentException>(() => tracker.GetAsync("../SCRUM-7", TestContext.Current.CancellationToken))).Code);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void The_tickets_settings_read_from_stangent_yaml()
    {
        using var repo = new TempRepo("""
            version: 1
            tickets:
              site: peachpersonal.atlassian.net
              projects: [SCRUM]
              bugTypes: [Bug, Defect]
              fields: { steps: Steps to Reproduce, expected: Expected Result }
              bundleChars: 30000
              app: { build: dotnet build, start: dotnet run, url: "http://localhost:8989", seed: ./seed.sh }
            """);
        var t = repo.Repo.Config.Tickets;
        Assert.Equal("peachpersonal.atlassian.net", t.Site);
        Assert.True(t.IsBugType("defect"));
        Assert.Equal("Steps to Reproduce", t.Fields["steps"]);
        Assert.Equal(30000, t.BundleChars);
        Assert.Equal("http://localhost:8989", t.App!.Url);
        Assert.Equal("https://mcp.atlassian.com/v2/mcp", t.Endpoint);
        Assert.Equal(1.0, t.ProveBudget.Usd);
        Assert.Equal(15, t.ProveBudget.Turns);
        Assert.True(t.AiPoints);
        Assert.Equal("internal", t.Tone);
        Assert.False(Trackers.Open(repo.Repo, new MemorySecretStore(), TextWriter.Null, null).Replaying);
    }

    [Fact]
    public async Task The_keychain_token_cache_keeps_the_refresh_token_and_client_across_processes()
    {
        var secrets = new MemorySecretStore();
        var first = new KeychainTokenCache(secrets, "stangent.jira", "oauth:mcp.atlassian.com/v2/mcp");
        Assert.Null(await first.GetTokensAsync(TestContext.Current.CancellationToken));
        await first.StoreTokensAsync(new TokenContainer { AccessToken = "a", RefreshToken = "r", ClientId = "c", TokenType = "bearer", ObtainedAt = DateTimeOffset.UnixEpoch }, TestContext.Current.CancellationToken);
        var second = new KeychainTokenCache(secrets, "stangent.jira", "oauth:mcp.atlassian.com/v2/mcp");
        var t = await second.GetTokensAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("r", "c"), (t!.RefreshToken, t.ClientId));
    }

    [Fact]
    public void Only_Jira_scopes_are_requested()
    {
        string[] offered = ["read:jira:agent-interface", "write:jira:agent-interface", "delete:jira:agent-interface", "read:loom:agent-interface",
            "write:bitbucket:agent-interface", "offline_access", "read:me"];
        Assert.Equal(["read:jira:agent-interface", "write:jira:agent-interface", "offline_access", "read:me"], offered.Where(JiraScopes.Wanted));
    }

    static RawResult Edit(RawResult raw, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(raw.Json)!;
        var block = node["content"]![0]!;
        var payload = JsonNode.Parse((string)block["text"]!)!.AsObject();
        change(payload);
        block["text"] = payload.ToJsonString();
        return RawResult.From(raw.Name, node);
    }

    sealed class MemorySink : IRawSink
    {
        public List<RawResult> Saved { get; } = [];
        public void Save(string key, RawResult raw) => Saved.Add(raw);
    }

    sealed class FakeJira(Dictionary<string, RawResult> replies) : IJiraTools
    {
        public List<(string Tool, IReadOnlyDictionary<string, object?> Args)> Calls { get; } = [];

        public Task<ToolResult> CallAsync(string tool, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
        {
            Calls.Add((tool, args));
            var raw = replies[tool];
            return Task.FromResult(new ToolResult(tool, JsonNode.Parse(raw.Json)!.AsObject(), "", null));
        }

        public Task<DownloadedAttachment> DownloadAttachmentAsync(string cloudId, string attachmentId, string destination, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

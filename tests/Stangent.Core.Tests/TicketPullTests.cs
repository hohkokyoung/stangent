using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Fsd;
using Stangent.Tickets;

namespace Stangent.Core.Tests;

/// <summary>Phase 3 step 2: <c>stangent ticket pull</c> (D72, D75). Tickets come from replay folders; no network.</summary>
public class TicketPullTests
{
    static async Task<JsonObject> Pull(TempRepo repo, string key)
    {
        await using var tracker = Trackers.Open(repo.Repo, new Stangent.Tickets.Secrets.MemorySecretStore(), TextWriter.Null,
            new FolderRawSink(TicketPaths.TicketsDir(repo.Root)));
        return await TicketPull.RunAsync(repo.Repo, tracker, key, TestContext.Current.CancellationToken);
    }

    const string SonarrStyle = """
        ### Is there an existing issue for this?

        - [X] I have searched the existing open and closed issues

        ### Current Behavior

        Saving series 12 after changing its root folder fails with a NullReferenceException.

        ### Expected Behavior

        The series is saved and moved to the new root folder.

        ### Steps To Reproduce

        1. Open series 12
        2. Change the root folder
        3. Save

        ### Environment

        _No response_

        ### What branch are you running?

        Main
        """;

    [Fact]
    public async Task A_bug_gets_its_parts_from_the_description_and_lists_what_is_missing()
    {
        using var repo = TicketFixtures.Repo();
        TicketFixtures.Make(TicketFixtures.ReplayDir(repo), "SON-1", "Bug", "Saving a series fails", SonarrStyle);
        var t = await Pull(repo, "SON-1");
        Assert.True((bool)t["isBug"]!);
        var bug = t["bug"]!;
        Assert.Equal("Saving series 12 after changing its root folder fails with a NullReferenceException.", (string?)bug["actual"]);
        Assert.Equal("The series is saved and moved to the new root folder.", (string?)bug["expected"]);
        Assert.Equal("1. Open series 12\n2. Change the root folder\n3. Save", (string?)bug["steps"]);
        Assert.Equal("description: Current Behavior", (string?)bug["from"]!["actual"]);
        Assert.Equal(["version", "environment"], bug["missing"]!.AsArray().Select(m => (string)m!)); // "_No response_" isn't an answer
    }

    [Fact]
    public async Task A_version_given_in_the_environment_counts_but_the_machines_versions_do_not()
    {
        // D93: issue templates often list the product's version among the environment lines.
        const string Template = """
            ### Current Behavior

            The export misses archived rows.

            ### Expected Behavior

            The export includes archived rows.

            ### Environment

            ```markdown
            - OS: Ubuntu 22.04
            - OS Version: 22.04
            - Shop: 3.2.1
            - Browser: Firefox v128.0.3
            - Chrome: 120.0.6099
            - .NET Version: 8.0.1
            - Host: 192.168.1.5:8989
            - Database: Postgres 16.2
            ```
            """;
        using var repo = TicketFixtures.Repo();
        TicketFixtures.Make(TicketFixtures.ReplayDir(repo), "SON-9", "Bug", "Export misses rows", Template);
        var bug = (await Pull(repo, "SON-9"))["bug"]!;
        Assert.Equal("Shop: 3.2.1", (string?)bug["version"]);
        Assert.Equal("environment: a line with a version number", (string?)bug["from"]!["version"]);
        Assert.DoesNotContain("version", bug["missing"]!.AsArray().Select(m => (string)m!));

        // Only the machine's versions: the product's version is still missing.
        TicketFixtures.Make(TicketFixtures.ReplayDir(repo), "SON-10", "Bug", "Export misses rows", Template.Replace("- Shop: 3.2.1\n", ""));
        var other = (await Pull(repo, "SON-10"))["bug"]!;
        Assert.Null((string?)other["version"]);
        Assert.Contains("version", other["missing"]!.AsArray().Select(m => (string)m!));
    }

    [Fact]
    public async Task Jira_fields_named_in_the_settings_win_over_the_description()
    {
        using var repo = TicketFixtures.Repo("  fields: { expected: Expected Result }\n");
        TicketFixtures.Make(TicketFixtures.ReplayDir(repo), "SON-2", "Bug", "x", "**Expected:** from the description\n**Actual:** it crashes", fields: f =>
        {
            f["customFields"]!["Expected Result"] = JsonNode.Parse("""{"id":"customfield_1","value":"from the field"}""");
            f["versions"] = JsonNode.Parse("""[{"name":"4.0.1"}]""");
            f["environment"] = "Docker";
        });
        var bug = (await Pull(repo, "SON-2"))["bug"]!;
        Assert.Equal("from the field", (string?)bug["expected"]);
        Assert.Equal("field: Expected Result", (string?)bug["from"]!["expected"]);
        Assert.Equal("it crashes", (string?)bug["actual"]);
        Assert.Equal("4.0.1", (string?)bug["version"]);
        Assert.Equal("Docker", (string?)bug["environment"]);
        Assert.Equal(["steps"], bug["missing"]!.AsArray().Select(m => (string)m!));
    }

    [Fact]
    public async Task Attachments_are_saved_with_hashes_text_goes_in_up_to_the_cap_and_the_rest_is_listed()
    {
        using var repo = TicketFixtures.Repo("  attachmentChars: 40\n");
        var log = Encoding.UTF8.GetBytes("System.NullReferenceException: Object reference not set\n   at Series.Save() in Series.cs:line 12\n");
        var docx = DocxWriter.Write("# Steps\nOpen the series page and press Save.");
        TicketFixtures.Make(TicketFixtures.ReplayDir(repo), "SON-3", "Task", "x", "See screen.png and trace.log", comments: ["the log is trace.log"], files:
        [
            new("1", "trace.log", "text/plain", log), new("2", "screen.png", "image/png", [137, 80, 78, 71]),
            new("3", "spec.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", docx),
            new("4", "export.xlsx", "application/vnd.ms-excel", [1, 2, 3]),
        ]);
        var t = await Pull(repo, "SON-3");
        var a = t["attachments"]!.AsArray().Select(x => x!.AsObject()).ToDictionary(x => (string)x["fileName"]!);

        Assert.Equal("text", (string?)a["trace.log"]["kind"]);
        Assert.True((bool)a["trace.log"]["truncated"]!);
        Assert.Equal(40, ((string)a["trace.log"]["text"]!).Length);
        Assert.Equal(log.Length, (int)a["trace.log"]["chars"]!);
        Assert.Equal(Trackers.Hash(log), (string?)a["trace.log"]["hash"]);
        Assert.Equal(["description", "comment 20000"], a["trace.log"]["mentionedIn"]!.AsArray().Select(m => (string)m!));
        Assert.True(File.Exists(Path.Combine(repo.Root, (string)a["trace.log"]["path"]!)));

        Assert.Equal("image", (string?)a["screen.png"]["kind"]);
        Assert.Null(a["screen.png"]["text"]);
        Assert.Equal("docx", (string?)a["spec.docx"]["kind"]);
        var converted = DocxConverter.Convert(docx).Markdown;
        Assert.Equal(converted[..40], (string?)a["spec.docx"]["text"]);
        Assert.Contains("Open the series page and press Save.", converted);
        Assert.Equal("other", (string?)a["export.xlsx"]["kind"]);
        Assert.False((bool)t["isBug"]!);
        Assert.Null(t["bug"]);
    }

    [Fact]
    public async Task Pulling_twice_gives_the_same_bytes()
    {
        using var repo = TicketFixtures.Repo();
        TicketFixtures.Make(TicketFixtures.ReplayDir(repo), "SON-4", "Bug", "x", SonarrStyle, comments: ["one", "two"],
            files: [new("1", "a.txt", "text/plain", "hello"u8.ToArray())]);
        await Pull(repo, "SON-4");
        var path = new TicketPaths(repo.Root, "SON-4").TicketJson;
        var first = File.ReadAllText(path);
        await Pull(repo, "SON-4");
        Assert.Equal(first, File.ReadAllText(path));
        Assert.True(File.Exists(Path.Combine(repo.Root, ".stangent", "tickets", "SON-4", "raw", "getJiraIssue.json")));
    }

    [Fact]
    public async Task A_moved_issue_stays_under_the_key_asked_for()
    {
        using var repo = TicketFixtures.Repo();
        TicketFixtures.Make(TicketFixtures.ReplayDir(repo), "OLD-5", "Task", "x", "moved", files: [new("1", "a.txt", "text/plain", "hi"u8.ToArray())],
            fields: _ => { });
        var raw = Path.Combine(TicketFixtures.ReplayDir(repo), "OLD-5", "raw", "getJiraIssue.json");
        var node = JsonNode.Parse(File.ReadAllText(raw))!;
        var payload = JsonNode.Parse((string)node["content"]![0]!["text"]!)!;
        payload["data"]!["key"] = "NEW-9";
        node["content"]![0]!["text"] = payload.ToJsonString();
        File.WriteAllText(raw, node.ToJsonString());
        var t = await Pull(repo, "OLD-5");
        Assert.Equal("OLD-5", (string?)t["key"]);
        Assert.Equal("NEW-9", (string?)t["jiraKey"]);
        Assert.Equal("text", (string?)t["attachments"]![0]!["kind"]);
    }

    [Theory]
    [InlineData("**Steps to reproduce:**\n1. a\n2. b\n\n**Expected result:** c\n**Actual result:** d", "1. a\n2. b", "c", "d")]
    [InlineData("Steps:\n- a\nExpected:\nc\nActual:\nd", "- a", "c", "d")]
    [InlineData("## Steps to Reproduce\na\n## Expected behaviour\nc\n## Observed\nd", "a", "c", "d")]
    public void Description_labels_in_common_shapes(string description, string steps, string expected, string actual)
    {
        var s = TicketPull.DescriptionSections(description);
        Assert.Equal(steps, s["steps"].Text);
        Assert.Equal(expected, s["expected"].Text);
        Assert.Equal(actual, s["actual"].Text);
    }

    [Fact]
    public void Labels_in_single_emphasis_and_quoted_headings_that_end_nothing()
    {
        Assert.Equal("it saves", TicketPull.DescriptionSections("_Expected result:_ it saves")["expected"].Text);
        var s = TicketPull.DescriptionSections("### Actual Result\n\nAn error page:\n> # Error: invalid_request\n> Request ID: 1\n\n### Expected Result\n\nSigns in");
        Assert.Equal("An error page:\n> # Error: invalid_request\n> Request ID: 1", s["actual"].Text);
        Assert.Equal("Signs in", s["expected"].Text);
    }

    [Fact]
    public void An_HTML_body_becomes_Markdown_with_its_media_named()
    {
        var text = TicketPull.HtmlToMarkdown("<h2>Steps</h2><ol><li>Open</li><li>Save &amp; close</li></ol><p>See</p><div data-type=\"media\" data-id=\"x\">screen.png</div>");
        Assert.Equal("## Steps\n\n1. Open\n2. Save & close\n\nSee\n\n[attachment: screen.png]", text);
    }
}

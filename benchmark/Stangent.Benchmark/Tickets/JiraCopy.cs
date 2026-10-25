using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Tickets;
using Stangent.Tickets.Jira;
using Stangent.Tickets.Secrets;

namespace Stangent.Benchmark.Tickets;

/// <summary>
/// Copies the frozen set into the owner's Jira, once, asked for first (D77):
/// <c>--copy-to-jira &lt;tickets dir&gt; --site &lt;site&gt; --project &lt;KEY&gt; [--issue-type Bug] [--dry-run]</c>.
/// Each issue becomes one Jira issue (summary, description, the comments frozen with it, its attachments), labelled
/// <c>gh-&lt;number&gt;</c> so its original issue number is noted without adding words to its text. Resumable: issues already
/// in <c>jira-keys.json</c> are skipped. Signs in as the person running it (the browser sign-in in the keychain).
/// </summary>
public static class JiraCopy
{
    public static async Task<int> RunAsync(string ticketsDir, string site, string project, string issueType, bool dryRun, TextWriter log)
    {
        var selection = JsonNode.Parse(File.ReadAllText(Path.Combine(ticketsDir, "selection.json")))!;
        var keysPath = Path.Combine(ticketsDir, "jira-keys.json");
        var keys = File.Exists(keysPath) ? JsonNode.Parse(File.ReadAllText(keysPath))!.AsObject() : new JsonObject();
        var todo = selection["issues"]!.AsArray().OfType<JsonObject>().Where(i => !keys.ContainsKey(((int)i["issue"]!).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
        log.WriteLine($"{todo.Count} issue(s) to copy into {project} on {site}{(dryRun ? " (dry run: nothing is written)" : "")}");
        if (dryRun || todo.Count == 0)
        {
            foreach (var i in todo)
            {
                var frozen = Frozen(ticketsDir, i);
                log.WriteLine($"  #{i["issue"]} {i["kind"]}: \"{frozen["title"]}\", {frozen["comments"]!.AsArray().Count} comment(s), {frozen["attachments"]!.AsArray().Count} attachment(s)");
            }
            return 0;
        }
        await using var jira = await JiraConnection.OpenAsync(new JiraConnectionOptions(), SecretStores.Os(), log);
        foreach (var i in todo)
        {
            var n = ((int)i["issue"]!).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var frozen = Frozen(ticketsDir, i);
            var created = await jira.CallAsync("createJiraIssue", new Dictionary<string, object?>
            {
                ["cloudId"] = site, ["projectKey"] = project, ["issueType"] = issueType, ["summary"] = (string)frozen["title"]!,
                ["description"] = (string)frozen["body"]!, ["labels"] = new[] { "gh-" + n },
            });
            var key = (string?)created.Json?["data"]?["key"] ?? throw new InvalidOperationException($"#{n}: no key in createJiraIssue's result.");
            keys[n] = key;
            File.WriteAllText(keysPath, keys.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"); // saved at once: resumable
            foreach (var c in frozen["comments"]!.AsArray())
                await jira.CallAsync("addOrEditJiraIssueComment", new Dictionary<string, object?>
                {
                    ["cloudId"] = site, ["issueIdOrKey"] = key, ["commentBody"] = $"_{(string?)c!["author"]} wrote:_\n\n{(string)c["body"]!}",
                });
            foreach (var a in frozen["attachments"]!.AsArray())
                await jira.UploadAttachmentAsync(site, key, Path.Combine(ticketsDir, "issues", n, (string)a!["file"]!));
            log.WriteLine($"#{n} → {key}");
        }
        return 0;
    }

    static JsonNode Frozen(string ticketsDir, JsonObject i) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(ticketsDir, "issues", ((int)i["issue"]!).ToString(System.Globalization.CultureInfo.InvariantCulture), "issue.json")))!;
}

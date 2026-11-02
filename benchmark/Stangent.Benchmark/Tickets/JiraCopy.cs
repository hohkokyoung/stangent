using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Core;
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
            var created = await Laid((string)frozen["body"]!, $"#{n} description", log, body => jira.CallAsync("createJiraIssue", new Dictionary<string, object?>
            {
                ["cloudId"] = site, ["projectKey"] = project, ["issueType"] = issueType, ["summary"] = (string)frozen["title"]!,
                ["description"] = body, ["labels"] = new[] { "gh-" + n },
            }));
            var key = (string?)created.Json?["data"]?["key"] ?? throw new InvalidOperationException($"#{n}: no key in createJiraIssue's result.");
            keys[n] = key;
            File.WriteAllText(keysPath, keys.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n"); // saved at once: resumable
            foreach (var c in frozen["comments"]!.AsArray())
                await Laid($"_{(string?)c!["author"]} wrote:_\n\n{(string)c["body"]!}", $"#{n} comment", log, body => jira.CallAsync("addOrEditJiraIssueComment", new Dictionary<string, object?>
                {
                    ["cloudId"] = site, ["issueIdOrKey"] = key, ["commentBody"] = body,
                }));
            foreach (var a in frozen["attachments"]!.AsArray())
                await jira.UploadAttachmentAsync(site, key, Path.Combine(ticketsDir, "issues", n, (string)a!["file"]!));
            log.WriteLine($"#{n} → {key}");
        }
        return 0;
    }

    static JsonNode Frozen(string ticketsDir, JsonObject i) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(ticketsDir, "issues", ((int)i["issue"]!).ToString(System.Globalization.CultureInfo.InvariantCulture), "issue.json")))!;

    /// <summary>
    /// Sends a text; if Jira refuses its layout (structure its format can't hold, such as a heading in a quote in a
    /// list), sends it once more with only the lines Jira names inside code blocks, every character kept. When Jira
    /// names no line, the whole text goes as one code block. Each fallback is logged.
    /// </summary>
    static async Task<T> Laid<T>(string text, string what, TextWriter log, Func<string, Task<T>> send)
    {
        try { return await send(text); }
        catch (StangentException e) when (e.Message.Contains("Failed to convert markdown to ADF", StringComparison.Ordinal))
        {
            var lines = RefusedLines(e.Message);
            log.WriteLine(lines.Count > 0
                ? $"  {what}: Jira can't hold line(s) {string.Join(", ", lines)} as written; sent as code"
                : $"  {what}: Jira can't hold its layout; sent as one code block");
            return await send(lines.Count > 0 ? Fence(text, lines) : Fence(text));
        }
    }

    /// <summary>The 1-based line numbers in Jira's refusal ("… (line 11); …").</summary>
    public static IReadOnlyList<int> RefusedLines(string message) =>
        [.. System.Text.RegularExpressions.Regex.Matches(message, @"\(line (\d+)\)").Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order()];

    /// <summary>
    /// Puts each named line's block in a code fence: the run of non-blank lines around it at its own indentation, fenced
    /// at that indentation (so it stays inside its list item). Lines are kept as they are, line endings included.
    /// </summary>
    public static string Fence(string text, IReadOnlyList<int> lines)
    {
        var all = text.Split('\n').ToList();
        static string Indent(string l) => l[..(l.Length - l.TrimStart(' ', '\t').Length)];
        static bool Blank(string l) => l.Trim().Length == 0;
        var blocks = new List<(int From, int To)>();
        foreach (var n in lines)
        {
            var i = n - 1;
            if (i < 0 || i >= all.Count || Blank(all[i]) || blocks.Any(b => i >= b.From && i <= b.To)) continue;
            var indent = Indent(all[i]);
            int from = i, to = i;
            while (from > 0 && !Blank(all[from - 1]) && Indent(all[from - 1]) == indent) from--;
            while (to < all.Count - 1 && !Blank(all[to + 1]) && Indent(all[to + 1]) == indent) to++;
            blocks.Add((from, to));
        }
        if (blocks.Count == 0) return Fence(text);
        var eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r" : "";
        foreach (var (from, to) in blocks.OrderByDescending(b => b.From))
        {
            var indent = Indent(all[from]);
            var fence = FenceFor(all.Skip(from).Take(to - from + 1));
            all.Insert(to + 1, indent + fence + eol);
            all.Insert(from, indent + fence + eol);
        }
        return string.Join('\n', all);
    }

    /// <summary>The whole text as one code block.</summary>
    public static string Fence(string text) { var f = FenceFor([text]); return f + "\n" + text + "\n" + f; }

    /// <summary>A backtick fence longer than any backtick run in the lines, so none of them can close it.</summary>
    static string FenceFor(IEnumerable<string> lines) =>
        new('`', Math.Max(3, lines.Select(l => System.Text.RegularExpressions.Regex.Matches(l, "`+").Select(m => m.Length).DefaultIfEmpty(0).Max()).DefaultIfEmpty(0).Max() + 1));
}

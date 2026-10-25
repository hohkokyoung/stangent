using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Tickets;

/// <summary>Where one ticket's working files live: <c>.stangent/tickets/&lt;KEY&gt;/</c>, git-ignored (plan: "Where the files live").</summary>
public sealed class TicketPaths(string root, string key)
{
    public string Key { get; } = TicketKeys.Check(key);
    public static string TicketsDir(string root) => Path.Combine(root, ".stangent", "tickets");
    public string Dir => Path.Combine(TicketsDir(root), Key);
    public string Raw => Path.Combine(Dir, "raw");
    public string Attachments => Path.Combine(Dir, "attachments");
    public string TicketJson => Path.Combine(Dir, "ticket.json");
    public string PointsJson => Path.Combine(Dir, "points.json");
    public string BundleJson => Path.Combine(Dir, "bundle.json");
    public string BundleMarkdown => Path.Combine(Dir, "bundle.md");
    public string Work(string step) => Path.Combine(Dir, step);
    public string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}

/// <summary>
/// <c>stangent ticket pull &lt;KEY&gt;</c> (plan step 2, D72, D75): the ticket through the adapter, every attachment saved
/// with its hash, text attachments in up to a cap, <c>.docx</c> through 2b's converter, images and anything else listed,
/// and a bug's steps, expected and actual result, version and environment in their own fields. All code: the same
/// ticket and files give the same <c>ticket.json</c>, byte for byte.
/// </summary>
public static partial class TicketPull
{
    /// <summary>Files bigger than this are listed, not downloaded (a plan detail: a video can be gigabytes).</summary>
    public const long MaxDownloadBytes = 100L * 1024 * 1024;
    public static readonly string[] BugParts = ["steps", "expected", "actual", "version", "environment"];

    static readonly HashSet<string> TextExtensions = [".txt", ".log", ".json", ".csv", ".tsv", ".xml", ".yaml", ".yml", ".md", ".ini", ".cfg",
        ".conf", ".config", ".properties", ".trace", ".out", ".err", ".stacktrace", ".har", ".sql", ".cs", ".ts", ".js", ".html", ".htm"];
    static readonly HashSet<string> ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".tif", ".tiff", ".heic"];

    public static async Task<JsonObject> RunAsync(StangentRepo repo, IIssueTracker tracker, string key, CancellationToken ct = default)
    {
        var paths = new TicketPaths(repo.Root, key);
        // A live read replaces what was saved before; a replay may be reading this very folder, so it's left alone.
        if (!tracker.Replaying && Directory.Exists(paths.Raw)) Directory.Delete(paths.Raw, recursive: true);
        var ticket = await tracker.GetAsync(paths.Key, ct);
        // A moved issue answers with its new key: files stay under the key asked for, and Jira's is recorded beside it.
        var jiraKey = ticket.Key;
        ticket = ticket with { Key = paths.Key };
        var settings = repo.Config.Tickets;

        var description = ticket.DescriptionFormat == "html" ? HtmlToText(ticket.Description) : ticket.Description;
        var comments = ticket.Comments.Select(c => (c, Text: c.Format == "html" ? HtmlToText(c.Body) : c.Body)).ToList();

        var files = new JsonArray();
        foreach (var a in ticket.Attachments.OrderBy(a => a.Id, StringComparer.Ordinal))
            files.Add(await Attachment(paths, tracker, ticket, a, settings.AttachmentChars, description, comments, ct));

        var isBug = settings.IsBugType(ticket.Type);
        var result = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["key"] = ticket.Key,
            ["source"] = tracker.Source,
            ["jiraKey"] = jiraKey == paths.Key ? null : jiraKey,
            ["site"] = settings.Site,
            ["raw"] = new JsonArray([.. ticket.Raw.Select(r => (JsonNode)new JsonObject { ["name"] = r.Name, ["hash"] = r.Hash })]),
            ["ticket"] = Without(ticket.ToJson(), "raw", "description", "comments"),
            ["description"] = description,
            ["comments"] = new JsonArray([.. comments.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.c.Id, ["author"] = c.c.Author, ["created"] = c.c.Created, ["text"] = c.Text,
            })]),
            ["attachments"] = files,
            ["isBug"] = isBug,
        };
        if (isBug) result["bug"] = BugFields(ticket, description, settings.Fields);
        Quotes.WriteAtomic(paths.TicketJson, CanonicalJson.Serialize(result));
        return result;
    }

    public static JsonObject? Load(string root, string key) =>
        File.Exists(new TicketPaths(root, key).TicketJson) ? JsonNode.Parse(File.ReadAllText(new TicketPaths(root, key).TicketJson)) as JsonObject : null;

    public static JsonObject LoadOrFail(string root, string key) => Load(root, key)
        ?? throw new StangentException("not_pulled", $"{key} hasn't been pulled. Run `stangent ticket pull {key}` first.", ExitCodes.NotFound);

    static async Task<JsonNode> Attachment(TicketPaths paths, IIssueTracker tracker, Ticket ticket, TicketAttachment a, int cap,
        string description, List<(TicketComment c, string Text)> comments, CancellationToken ct)
    {
        var kind = Kind(a);
        var o = new JsonObject { ["id"] = a.Id, ["fileName"] = a.FileName, ["mimeType"] = a.MimeType, ["size"] = a.Size, ["kind"] = kind };
        var mentioned = new JsonArray();
        if (description.Contains(a.FileName, StringComparison.Ordinal)) mentioned.Add("description");
        foreach (var (c, _) in comments.Where(c => c.Text.Contains(a.FileName, StringComparison.Ordinal))) mentioned.Add($"comment {c.Id}");
        o["mentionedIn"] = mentioned;
        if (a.Size > MaxDownloadBytes)
        {
            o["notDownloaded"] = $"over {MaxDownloadBytes / 1024 / 1024} MB";
            return o;
        }
        var dest = Path.Combine(paths.Attachments, a.Id, SafeName(a.FileName));
        var saved = await tracker.DownloadAsync(ticket, a, dest, ct);
        o["path"] = paths.Relative(saved.Path);
        o["hash"] = saved.Hash;
        o["bytes"] = saved.Bytes;
        string? text = kind switch
        {
            "text" => Decode(await File.ReadAllBytesAsync(saved.Path, ct)),
            "docx" => DocxConverter.Convert(await File.ReadAllBytesAsync(saved.Path, ct)).Markdown,
            _ => null,
        };
        if (text is not null)
        {
            text = text.Replace("\r\n", "\n");
            o["chars"] = text.Length;
            o["text"] = text.Length > cap ? text[..cap] : text;
            o["truncated"] = text.Length > cap;
        }
        return o;
    }

    public static string Kind(TicketAttachment a)
    {
        var ext = Path.GetExtension(a.FileName).ToLowerInvariant();
        var mime = a.MimeType ?? "";
        if (ext == ".docx") return "docx";
        if (ImageExtensions.Contains(ext) || mime.StartsWith("image/", StringComparison.Ordinal)) return "image";
        if (TextExtensions.Contains(ext) || mime.StartsWith("text/", StringComparison.Ordinal) || mime is "application/json" or "application/xml")
            return "text";
        return "other";
    }

    static string SafeName(string name)
    {
        var n = Path.GetFileName(name.Replace('\\', '/'));
        n = string.Concat(n.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ':' ? '_' : ch)).Trim().TrimStart('.');
        return n.Length == 0 ? "attachment" : n;
    }

    static string Decode(byte[] bytes)
    {
        var text = new UTF8Encoding(false, false).GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    static JsonObject Without(JsonObject o, params string[] keys)
    {
        foreach (var k in keys) o.Remove(k);
        return o;
    }

    // ---- bug fields (D75) ----

    static readonly (string Part, string[] Labels)[] Labels =
    [
        ("steps", ["steps to reproduce", "steps to repro", "steps", "reproduction steps", "repro steps", "how to reproduce", "to reproduce", "reproduce"]),
        ("expected", ["expected result", "expected results", "expected behavior", "expected behaviour", "expected"]),
        ("actual", ["actual result", "actual results", "actual behavior", "actual behaviour", "current behavior", "current behaviour", "actual", "observed behavior", "observed"]),
        ("version", ["version", "versions", "affected version", "affects version", "app version"]),
        ("environment", ["environment", "environments", "platform"]),
    ];

    /// <summary>Each part from, in order: the Jira field <c>tickets.fields</c> names; Jira's own environment and affects-version
    /// fields; a heading or label in the description ("### Expected Behavior", "**Actual:**"). Missing parts are listed for
    /// the questions draft (D74).</summary>
    public static JsonObject BugFields(Ticket ticket, string description, IReadOnlyDictionary<string, string> fieldNames)
    {
        var sections = DescriptionSections(description);
        var o = new JsonObject();
        var from = new JsonObject();
        var missing = new JsonArray();
        foreach (var part in BugParts)
        {
            string? value = null, source = null;
            if (fieldNames.TryGetValue(part, out var label))
            {
                value = label.ToLowerInvariant() switch
                {
                    "environment" => ticket.Environment,
                    "versions" or "affects versions" or "affects version/s" => ticket.Versions.Count > 0 ? string.Join(", ", ticket.Versions) : null,
                    _ => ticket.Fields.GetValueOrDefault(label),
                };
                if (!string.IsNullOrWhiteSpace(value)) source = $"field: {label}";
            }
            if (source is null && part == "environment" && !string.IsNullOrWhiteSpace(ticket.Environment)) (value, source) = (ticket.Environment, "field: environment");
            if (source is null && part == "version" && ticket.Versions.Count > 0) (value, source) = (string.Join(", ", ticket.Versions), "field: affects versions");
            if (source is null && sections.TryGetValue(part, out var s)) (value, source) = (s.Text, $"description: {s.Label}");
            if (source is null) { missing.Add(part); o[part] = null; continue; }
            o[part] = value!.Trim();
            from[part] = source;
        }
        o["from"] = from;
        o["missing"] = missing;
        return o;
    }

    /// <summary>The description cut at its labelled parts. A label is a Markdown heading, a bold label, or a short line
    /// ending in a colon, whose words name a part; its text runs to the next label or heading.</summary>
    public static Dictionary<string, (string Label, string Text)> DescriptionSections(string description)
    {
        var found = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        string? part = null, label = null;
        var text = new StringBuilder();
        void Close()
        {
            if (part is not null && !found.ContainsKey(part) && text.ToString().Trim() is { Length: > 0 } t && !IsPlaceholder(t)) found[part] = (label!, t);
            text.Clear();
        }
        foreach (var line in description.Replace("\r\n", "\n").Split('\n'))
        {
            var (heading, labelText, rest) = SplitLabel(line);
            var matched = labelText is null ? null : PartOf(labelText);
            if (matched is not null)
            {
                Close();
                (part, label) = (matched, labelText!.Trim());
                if (rest.Trim() is { Length: > 0 } r) text.Append(r).Append('\n');
                continue;
            }
            if (heading) { Close(); part = null; continue; }
            if (part is not null) text.Append(line).Append('\n');
        }
        Close();
        return found;
    }

    /// <summary>A line's label, if it has the shape of one: <c>## Label</c>, <c>**Label:** rest</c>, <c>**Label**: rest</c> or
    /// <c>Label: rest</c> (a label of letters and spaces, at most 40 characters).</summary>
    static (bool Heading, string? Label, string After) SplitLabel(string line)
    {
        var l = line.Trim();
        var heading = l.StartsWith('#');
        if (heading) l = l.TrimStart('#').Trim();
        foreach (var marker in new[] { "**", "__" })
        {
            if (!l.StartsWith(marker, StringComparison.Ordinal)) continue;
            var close = l.IndexOf(marker, 2, StringComparison.Ordinal);
            if (close < 0) break;
            return (heading, l[2..close].Trim().TrimEnd(':'), l[(close + 2)..].TrimStart().TrimStart(':').TrimStart());
        }
        var colon = l.IndexOf(':');
        if (colon > 0 && colon <= 40 && l[..colon].All(c => char.IsLetter(c) || c is ' ' or '/' or '\''))
            return (heading, l[..colon], l[(colon + 1)..]);
        return heading ? (true, l, "") : (false, null, "");
    }

    static string? PartOf(string label)
    {
        var l = Regex.Replace(label.ToLowerInvariant(), @"\s+", " ").Trim().TrimEnd(':').Trim();
        foreach (var (part, labels) in Labels)
            if (labels.Contains(l)) return part;
        return null;
    }

    /// <summary>A template's untouched prompt ("_No response_", "N/A") isn't an answer.</summary>
    static bool IsPlaceholder(string t) => Regex.IsMatch(t.Trim(), @"^(_no response_|no response|n/?a|none|-|tbd)$", RegexOptions.IgnoreCase);

    // ---- HTML bodies (Jira returns HTML for what Markdown can't hold) ----

    /// <summary>A plain-text view of an HTML body, by code: block elements become line breaks, list items "- ", headings
    /// "#", embedded media "[attachment: name]"; other tags are dropped and entities decoded.</summary>
    public static string HtmlToText(string html)
    {
        var s = Regex.Replace(html, @"<div[^>]*data-type=""media""[^>]*>(.*?)</div>", m => $"\n[attachment: {StripTags(m.Groups[1].Value).Trim()}]\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<h([1-6])[^>]*>", m => "\n" + new string('#', int.Parse(m.Groups[1].Value)) + " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<li[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<(p|ul|ol|pre|blockquote|table)(\s[^>]*)?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</(p|div|h[1-6]|ul|ol|pre|blockquote|tr|table)>", "\n", RegexOptions.IgnoreCase);
        s = WebUtility.HtmlDecode(StripTags(s));
        s = Regex.Replace(s, @"[ \t]+\n", "\n");
        return Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
    }

    static string StripTags(string s) => Regex.Replace(s, "<[^>]+>", "");
}

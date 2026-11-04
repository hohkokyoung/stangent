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
    public string Root => root;
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

        var description = ticket.DescriptionFormat == "html" ? HtmlToMarkdown(ticket.Description) : ticket.Description;
        var comments = ticket.Comments.Select(c => (c, Text: c.Format == "html" ? HtmlToMarkdown(c.Body) : c.Body)).ToList();

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
            "docx" => Word(await File.ReadAllBytesAsync(saved.Path, ct), o),
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

    /// <summary>A ticket's attachment isn't the agreed FSD, so nothing about it stops the pull (D72): a tracked change reads as accepted, no headings
    /// reads flat, and a file the converter can't take is listed with why.</summary>
    static string? Word(byte[] bytes, JsonObject attachment)
    {
        try
        {
            try { return DocxConverter.Convert(bytes, acceptChanges: true).Markdown; }
            catch (StangentException e) when (e.Code == "no_structure") { return DocxConverter.Convert(bytes, flat: true, acceptChanges: true).Markdown; }
        }
        catch (StangentException e)
        {
            attachment["notConverted"] = e.Message;
            return null;
        }
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
        // D93: many issue templates give the version inside the environment ("- Sonarr: 4.0.11.2815", "App: 2.3.1")
        // rather than as a field of its own: with no version found, such a line counts. Lines naming the machine around
        // the product (OS, browser, database, runtime) don't: their numbers aren't the product's version.
        if (o["version"] is null && (string?)o["environment"] is { } env && VersionLines(env) is { Count: > 0 } found)
        {
            o["version"] = string.Join("; ", found);
            from["version"] = "environment: a line with a version number";
            missing = new JsonArray([.. missing.Select(m => (string)m!).Where(m => m != "version").Select(m => (JsonNode)m)]);
        }
        o["from"] = from;
        o["missing"] = missing;
        return o;
    }

    /// <summary>Words that name the machine around the product (its OS, browser, runtime, database, network), not the
    /// product. A label with any of them ("OS Version", "Chrome", ".NET Version", "Host") isn't the product's version.</summary>
    static readonly HashSet<string> AroundTheProduct = new(StringComparer.OrdinalIgnoreCase)
    {
        "os", "operating", "system", "platform", "kernel", "distro", "distribution", "windows", "macos", "mac", "osx", "linux",
        "ubuntu", "debian", "fedora", "centos", "rhel", "alpine", "arch", "unraid", "truenas", "synology", "ios", "android",
        "browser", "chrome", "chromium", "firefox", "safari", "edge", "brave", "opera", "vivaldi", "webview",
        "database", "db", "sqlite", "postgres", "postgresql", "mysql", "mariadb", "sql", "mssql", "redis", "mongodb",
        "docker", "container", "podman", "kubernetes", "k8s", "helm", "compose", "install", "installation",
        "node", "nodejs", "npm", "yarn", "pnpm", ".net", "dotnet", "net", "runtime", "framework", "mono", "sdk",
        "python", "java", "jdk", "jre", "php", "ruby", "go", "rust", "device", "phone", "mobile", "hardware", "cpu", "ram",
        "memory", "disk", "nginx", "apache", "iis", "caddy", "traefik", "reverse", "proxy", "nas", "server", "host", "ip",
        "address", "url", "port", "network",
    };

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(?:[-*•]\s*)?\**(?<label>[^:\n*]{1,40}?)\**\s*:\s*\**(?<value>[^\n]*?\bv?\d+(?:\.\d+){1,3}\b[^\n]*)$", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex VersionLine();

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})\b")]
    private static partial System.Text.RegularExpressions.Regex FourNumbers();

    /// <summary>"Label: value" lines in an environment text whose value holds a version number and whose label names
    /// the product rather than the machine around it (any word of the label, "version" itself aside). An IP address
    /// (four numbers, each 255 or less) isn't a version.</summary>
    static List<string> VersionLines(string environment) =>
        [.. VersionLine().Matches(environment)
            .Where(m => !System.Text.RegularExpressions.Regex.Split(m.Groups["label"].Value.Trim(), @"[\s/()_-]+")
                .Where(w => w.Length > 0 && !w.Equals("version", StringComparison.OrdinalIgnoreCase) && !w.Equals("ver", StringComparison.OrdinalIgnoreCase))
                .Any(AroundTheProduct.Contains))
            .Where(m => !FourNumbers().Matches(m.Groups["value"].Value).Any(ip => Enumerable.Range(1, 4).All(i => int.Parse(ip.Groups[i].Value) <= 255)))
            .Select(m => $"{m.Groups["label"].Value.Trim()}: {m.Groups["value"].Value.Trim()}")];

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

    /// <summary>A line's label, if it has the shape of one: <c>## Label</c>, <c>**Label:** rest</c>, <c>**Label**: rest</c>,
    /// <c>_Label:_ rest</c> or <c>Label: rest</c> (a label of letters and spaces, at most 40 characters). A quoted line is
    /// never a label: a quoted error page's own heading mustn't end the part it is quoted in.</summary>
    static (bool Heading, string? Label, string After) SplitLabel(string line)
    {
        var l = line.Trim();
        var heading = l.StartsWith('#');
        if (heading) l = l.TrimStart('#').Trim();
        foreach (var marker in new[] { "**", "__", "*", "_" })
        {
            if (!l.StartsWith(marker, StringComparison.Ordinal) || l.StartsWith(marker + " ", StringComparison.Ordinal)) continue;
            var close = l.IndexOf(marker, marker.Length, StringComparison.Ordinal);
            if (close < 0) break;
            return (heading, l[marker.Length..close].Trim().TrimEnd(':'), l[(close + marker.Length)..].TrimStart().TrimStart(':').TrimStart());
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

    /// <summary>The Markdown Jira would have sent for an HTML body (D102): links, images, list numbers, task ticks and
    /// code marks kept; media blocks "[attachment: name]".</summary>
    public static string HtmlToMarkdown(string html) => Jira.JiraHtml.ToMarkdown(html);
}

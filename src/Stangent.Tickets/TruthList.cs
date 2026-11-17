using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Tickets;

/// <summary>
/// The truth map's entries that apply to a ticket (truth map layer 4, D361, D364, D366; <c>ticket-bundle</c> v3's
/// <c>truth</c>): from every <c>specs/fsd/&lt;doc&gt;/truth-map.json</c>, each entry one of whose evidence lines is in a
/// ticket file (a file point, or a file the bundle points at for a reason other than impact), with its ruling, statement,
/// standing and whether it can back a ticket, and the full text of each section they sit in. Code only. <c>off</c> with
/// the <c>truth-verdict</c> component off; <c>none</c> when the repo has no truth map, so nothing about the FSD is judged
/// (today's behaviour); <c>built</c> otherwise, with no entries meaning nothing the PM agreed covers the ticket.
/// </summary>
public static class TruthList
{
    public const string Component = "truth-verdict";

    public static bool On(StangentConfig config) => StangentComponents.IsOn(config, Component);

    /// <summary>Whether an entry can back a ticket, and why not (D364, D366 (4)): ruled, or a confirmed match, or an
    /// unconfirmed match of high confidence (shown as not confirmed); never blank, flagged, not found again, not a match,
    /// or an unconfirmed medium or low match.</summary>
    public static (bool Usable, string? Why) Usable(string standing, string? confidence) => standing switch
    {
        "ruled" or "confirmed" => (true, null),
        "unconfirmed" when confidence == "high" => (true, "a match of high confidence the PM hasn't confirmed yet"),
        "unconfirmed" => (false, $"a match of {confidence ?? "unknown"} confidence the PM hasn't confirmed"),
        "blank" => (false, "nobody has ruled on it"),
        "flagged" => (false, "its ruling needs another look"),
        "not-found" => (false, "the last FSD review didn't find it again"),
        "not-a-match" => (false, "the PM said the code doesn't do what the FSD says, and no rule says what it should do"),
        _ => (false, standing),
    };

    public static JsonObject Build(StangentRepo repo, IReadOnlyCollection<string> files)
    {
        if (!On(repo.Config)) return new JsonObject { ["state"] = "off", ["docs"] = new JsonArray(), ["entries"] = new JsonArray(), ["sections"] = new JsonArray() };
        var dir = Path.Combine(repo.Root, "specs", "fsd");
        var maps = Directory.Exists(dir)
            ? Directory.GetDirectories(dir).Select(d => Path.Combine(d, "truth-map.json")).Where(File.Exists).Order(StringComparer.Ordinal).ToList()
            : [];
        var docs = new JsonArray();
        var entries = new JsonArray();
        var sections = new JsonArray();
        var fileSet = files.Select(Clean).ToHashSet(StringComparer.Ordinal);
        var n = 0;
        foreach (var path in maps)
        {
            // A truth map that doesn't read is an error, never a map with nothing in it (that would stop every ticket).
            if (PageRun.LoadJson(path) is not JsonObject map || map["sections"] is not JsonArray || map["undocumented"] is not JsonArray)
                throw new StangentException("bad_truth_map", $"{Path.GetRelativePath(repo.Root, path)} isn't a truth map stangent can read: run `stangent fsd rulings-check` for its doc again.", ExitCodes.Failed);
            var doc = (string)map["doc"]!;
            docs.Add((JsonNode)doc);
            var all = map["sections"]!.AsArray().OfType<JsonObject>().SelectMany(s => s["entries"]!.AsArray().OfType<JsonObject>().Select(e => ((JsonObject?)s, e)))
                .Concat(map["undocumented"]!.AsArray().OfType<JsonObject>().Select(e => ((JsonObject?)null, e)));
            var used = new List<JsonObject>();
            foreach (var (s, e) in all)
            {
                var evidence = e["evidence"]!.AsArray().Select(v => (string)v!).ToList();
                if (!evidence.Any(v => fileSet.Contains(FileOf(v)))) continue;
                var standing = (string)e["standing"]!;
                var (usable, why) = Usable(standing, (string?)e["confidence"]);
                // D364, D374: a ruling drafted from a Jira reply counts once a person confirms it by committing.
                if (usable && e["jira"] is JsonObject jira && (bool?)e["committed"] != true)
                    (usable, why) = (false, $"its ruling was drafted from a Jira reply ({(string?)jira["key"]}, comment {(string?)jira["comment"]}) and isn't committed yet: commit {Path.GetRelativePath(repo.Root, Path.Combine(Path.GetDirectoryName(path)!, "rulings.yaml")).Replace('\\', '/')}, then run `stangent fsd rulings-check {doc}`");
                entries.Add(new JsonObject
                {
                    ["id"] = $"T{++n}", ["doc"] = doc, ["n"] = (int)e["n"]!,
                    ["section"] = (string?)s?["section"], ["sectionId"] = (string?)s?["sectionId"], ["title"] = (string?)s?["title"],
                    ["key"] = e["key"]!.DeepClone(), ["state"] = (string)e["state"]!, ["standing"] = standing,
                    ["ruling"] = (string?)e["ruling"], ["statement"] = (string?)e["statement"], ["confidence"] = (string?)e["confidence"],
                    ["by"] = (string?)e["by"], ["notes"] = e["notes"]!.DeepClone(), ["evidence"] = new JsonArray([.. evidence.Select(v => (JsonNode)v)]),
                    ["usable"] = usable, ["why"] = why,
                });
                if (s is not null && !used.Contains(s)) used.Add(s);
            }
            foreach (var s in used)
                sections.Add(new JsonObject
                {
                    ["doc"] = doc, ["section"] = (string)s["section"]!, ["sectionId"] = (string)s["sectionId"]!, ["title"] = (string)s["title"]!, ["text"] = (string?)s["text"],
                });
        }
        return new JsonObject { ["state"] = maps.Count == 0 ? "none" : "built", ["docs"] = docs, ["entries"] = entries, ["sections"] = sections };
    }

    /// <summary>The file of an evidence line: <c>src/a.cs:10-12</c> → <c>src/a.cs</c>.</summary>
    public static string FileOf(string evidence)
    {
        var e = Clean(evidence);
        // The first ":<digit>" after the file name starts the lines (path:10, path:10-12, path:10,12, path:10:5).
        for (var i = 1; i < e.Length - 1; i++)
            if (e[i] == ':' && char.IsDigit(e[i + 1])) return e[..i];
        return e;
    }

    /// <summary>A path as the bundle compares it: trimmed, forward slashes, no leading <c>./</c>.</summary>
    static string Clean(string path)
    {
        var p = path.Trim().Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
        return p;
    }

    /// <summary>The texts an entry's quote may come from: its FSD sentence, the PM's statement, its section's text, the
    /// review's notes.</summary>
    public static IEnumerable<string> Quotable(JsonObject truth, JsonObject entry)
    {
        if ((string?)entry["key"]?["quote"] is { } q) yield return q;
        if ((string?)entry["statement"] is { } st) yield return st;
        foreach (var note in entry["notes"]?.AsArray() ?? []) if ((string?)note is { } t) yield return t;
        if (Section(truth, entry) is { } s && (string?)s["text"] is { } text) yield return text;
    }

    public static JsonObject? Section(JsonObject truth, JsonObject entry) =>
        (string?)entry["sectionId"] is { } sid
            ? truth["sections"]!.AsArray().OfType<JsonObject>().FirstOrDefault(s => (string)s["doc"]! == (string)entry["doc"]! && (string)s["sectionId"]! == sid)
            : null;

    static string Heading(JsonObject e) => (string?)e["section"] is { } s ? $"§{s} {(string?)e["title"]}" : "code no section covers";

    /// <summary>The bundle's view: each entry with what it says and whether it can back a ticket, then the sections' text.</summary>
    public static void Markdown(StringBuilder md, JsonObject? truth)
    {
        if (truth is null || (string)truth["state"]! != "built") return;
        md.Append("## The truth map (what the PM agreed)\n\n");
        var entries = truth["entries"]!.AsArray().OfType<JsonObject>().ToList();
        if (entries.Count == 0) { md.Append("Nothing in the truth map covers this ticket's files: no rule the PM agreed says what should happen here.\n\n"); return; }
        foreach (var e in entries)
        {
            md.Append($"- **{e["id"]}** ({e["doc"]} {Heading(e)}, item {e["n"]}): {TruthMap.StateWords((string)e["state"]!)}");
            if ((string?)e["confidence"] is { } c) md.Append($" ({c} confidence)");
            md.Append((string?)e["ruling"] is { } r ? $" · ruled: {TruthMap.RulingWords(r)}" : "");
            md.Append((bool)e["usable"]! ? (string?)e["why"] is { } w ? $" · can back a ticket ({w})" : " · can back a ticket" : $" · **can't back a ticket**: {e["why"]}").Append('\n');
            if ((string?)e["key"]!["quote"] is { } q) md.Append($"  - The FSD says: “{Quotes.Normalize(q)}”\n");
            if ((string?)e["statement"] is { } st) md.Append($"  - The PM's words: {st}\n");
            foreach (var note in e["notes"]!.AsArray()) md.Append($"  - The review: {(string)note!}\n");
            md.Append($"  - The code: {string.Join(", ", e["evidence"]!.AsArray().Select(v => $"`{(string)v!}`"))}\n");
        }
        md.Append('\n');
        foreach (var s in truth["sections"]!.AsArray().OfType<JsonObject>())
        {
            md.Append($"### {s["doc"]} §{s["section"]} {s["title"]}\n\n");
            md.Append((string?)s["text"] is { } text ? $"````text\n{text.TrimEnd()}\n````\n\n" : "_Not in the FSD any more._\n\n");
        }
    }
}

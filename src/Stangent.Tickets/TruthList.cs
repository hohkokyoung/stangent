using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Tickets;

/// <summary>
/// The truth map's entries that apply to a ticket (truth map layer 4, D361, D364, D366; <c>ticket-bundle</c> v3's
/// <c>truth</c>): from every <c>specs/fsd/&lt;doc&gt;/truth-map.json</c>, each entry one of whose evidence lines is in a
/// ticket file (a file point, or a file the bundle points at for a reason other than impact), or (D410) whose link is a
/// flow the ticket's own code sits in, or the screen such a flow opens; with its ruling, statement,
/// standing and whether it can back a ticket, and the full text of each section they sit in. Code only. <c>off</c> with
/// the <c>truth-verdict</c> component off; <c>none</c> when the repo has no truth map, so nothing about the FSD is judged
/// (today's behaviour); <c>built</c> otherwise, with no entries meaning nothing the PM agreed covers the ticket.
/// </summary>
public static class TruthList
{

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

    /// <summary>A stretch of the ticket's own code (D410): the method the finder's quoted line sits in, or a symbol
    /// point's span. Lines are 1-based and inclusive.</summary>
    public sealed record Span(string File, int Line, int EndLine);

    /// <summary>The most flows (shared ones aside) the ticket's code may sit in for their flows and screens to bring in
    /// entries (D410). Above it the code is shared too widely to tell which screen the ticket is about, so only entries
    /// on the ticket's own files count, and the flows are listed.</summary>
    public const int MaxFlows = 5;

    /// <summary>The smallest graph node below a type (a method, mostly) holding the line of <paramref name="file"/> that
    /// reads <paramref name="line"/>, trimmed (the file finder quotes one line of its file, D101). None when the line is
    /// in no such node (a field, a <c>using</c>: the whole class would be too wide) or reads the same in two of them
    /// (<c>return result;</c>: which one the finder meant can't be told).</summary>
    public static IReadOnlyList<Span> SpansOfLine(string root, string file, string line, Func<string, IReadOnlyList<Node>> inFile)
    {
        var path = Path.Combine(root, file);
        var want = line.Trim();
        if (want.Length == 0 || !File.Exists(path)) return [];
        var nodes = inFile(file).Where(n => n.Line > 0 && n.EndLine >= n.Line && n.Kind is not NodeKind.Type and not NodeKind.File and not NodeKind.Project).ToList();
        var lines = File.ReadAllLines(path);
        var spans = new List<Span>();
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].Trim() == want && nodes.Where(n => n.Line <= i + 1 && i + 1 <= n.EndLine).MinBy(n => n.EndLine - n.Line) is { } node)
                spans.Add(new Span(Clean(file), node.Line, node.EndLine));
        spans = [.. spans.Distinct()];
        return spans.Count == 1 ? spans : [];
    }

    /// <summary>The flows the ticket's code sits in (D410): every flow with a step (or a step's condition) on a line
    /// inside one of <paramref name="spans"/>, plus, for a shared flow among them, the flows that continue into it (one
    /// hop); and the screens those flows open, by the screen-opens flow's point. Nothing when the repo has no flows.</summary>
    sealed record Reach(HashSet<string> Flows, Dictionary<string, string> Screens, List<string> All, bool TooMany, int Own = 0);

    static Reach FlowsOf(string root, IReadOnlyCollection<Span> spans)
    {
        var none = new Reach([], [], [], false);
        spans = [.. spans.Select(s => s with { File = Clean(s.File) }).Distinct()];
        var indexPath = Path.Combine(root, "specs", "flows", "index.json");
        if (spans.Count == 0 || !File.Exists(indexPath) || PageRun.LoadJson(indexPath) is not JsonObject index || index["flows"] is not JsonArray list) return none;
        // An index line or a flow file that doesn't read is skipped, never a failed bundle: matching through flows only adds.
        var flows = list.OfType<JsonObject>().Select(f => (Id: Str(f["id"]), File: Str(f["file"]), Kind: Str(f["kind"]), Point: Str(f["point"]),
                Links: (f["links"] as JsonArray ?? []).Select(Str).OfType<string>().ToList()))
            .Where(f => f.Id is not null && f.File is not null).ToList();
        // A flow file may write a path with JSON escapes (\u002B for +), so both spellings are looked for.
        var needles = spans.Select(s => s.File).Distinct(StringComparer.Ordinal)
            .SelectMany(f => new[] { f, System.Text.Json.JsonSerializer.Serialize(f).Trim('"') }).Distinct(StringComparer.Ordinal).ToList();
        var direct = new List<(string? Id, string? File, string? Kind, string? Point, List<string> Links)>();
        foreach (var f in flows)
        {
            var path = Path.Combine(root, f.File!);
            if (!File.Exists(path)) continue;
            var text = File.ReadAllText(path);
            if (!needles.Any(text.Contains)) continue;   // cheap: most flows never mention the ticket's files
            JsonNode? steps;
            try { steps = (JsonNode.Parse(text) as JsonObject)?["steps"]; }
            catch (System.Text.Json.JsonException) { continue; }
            if (steps is not null && Ats(steps).Any(at => Inside(at, spans))) direct.Add(f);
        }
        var shared = direct.Where(f => f.Kind == "shared").Select(f => f.Id!).ToHashSet(StringComparer.Ordinal);
        var directIds = direct.Select(f => f.Id!).ToHashSet(StringComparer.Ordinal);
        var parents = flows.Where(f => f.Kind != "shared" && !directIds.Contains(f.Id!) && f.Links.Any(shared.Contains)).ToList();
        var reached = direct.Concat(parents).ToList();
        var all = reached.Select(f => f.Id!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var own = reached.Where(f => f.Kind != "shared").Select(f => f.Id!).Distinct(StringComparer.Ordinal).Count();
        if (own > MaxFlows) return new Reach([], [], all, true, own);
        var screens = reached.Where(f => f.Kind == "screen-opens" && f.Point is not null)
            .GroupBy(f => f.Point!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Id!).Order(StringComparer.Ordinal).First(), StringComparer.Ordinal);
        return new Reach(all.ToHashSet(StringComparer.Ordinal), screens, all, false, own);
    }

    /// <summary>A JSON string's value, or null for anything else.</summary>
    static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    static IEnumerable<string> Ats(JsonNode node)
    {
        if (node is JsonObject o)
        {
            foreach (var (k, v) in o)
                if (k == "at" && v is JsonValue at && at.TryGetValue<string>(out var s)) yield return s;
                else if (v is not null) foreach (var x in Ats(v)) yield return x;
        }
        else if (node is JsonArray a)
            foreach (var v in a.OfType<JsonNode>()) foreach (var x in Ats(v)) yield return x;
    }

    /// <summary>Whether <c>path:line</c> falls inside one of the spans.</summary>
    static bool Inside(string at, IReadOnlyCollection<Span> spans)
    {
        var file = FileOf(at);
        var rest = Clean(at)[file.Length..].TrimStart(':');
        var digits = new string(rest.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var line) && spans.Any(s => s.File == file && s.Line <= line && line <= s.EndLine);
    }

    public static JsonObject Build(StangentRepo repo, IReadOnlyCollection<string> files) => Build(repo, files, []);

    public static JsonObject Build(StangentRepo repo, IReadOnlyCollection<string> files, IReadOnlyCollection<Span> spans)
    {
        var dir = Path.Combine(repo.Root, "specs", "fsd");
        var maps = Directory.Exists(dir)
            ? Directory.GetDirectories(dir).Select(d => Path.Combine(d, "truth-map.json")).Where(File.Exists).Order(StringComparer.Ordinal).ToList()
            : [];
        var docs = new JsonArray();
        var entries = new JsonArray();
        var sections = new JsonArray();
        var fileSet = files.Select(Clean).ToHashSet(StringComparer.Ordinal);
        var reach = maps.Count == 0 ? new Reach([], [], [], false) : FlowsOf(repo.Root, spans);
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
                var via = Via(e, evidence, fileSet, reach);
                if (via is null) continue;
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
                    ["usable"] = usable, ["why"] = why, ["via"] = via,
                });
                if (s is not null && !used.Contains(s)) used.Add(s);
            }
            foreach (var s in used)
                sections.Add(new JsonObject
                {
                    ["doc"] = doc, ["section"] = (string)s["section"]!, ["sectionId"] = (string)s["sectionId"]!, ["title"] = (string)s["title"]!, ["text"] = (string?)s["text"],
                });
        }
        return new JsonObject
        {
            ["state"] = maps.Count == 0 ? "none" : "built", ["docs"] = docs, ["entries"] = entries, ["sections"] = sections,
            ["reach"] = new JsonObject { ["flows"] = new JsonArray([.. reach.All.Select(f => (JsonNode)f)]), ["tooMany"] = reach.TooMany },
        };
    }

    /// <summary>How an entry reaches the ticket, or null: <c>file</c> (an evidence line in a ticket file), else
    /// <c>flow:&lt;id&gt;</c> (its link is a flow the ticket's code sits in), else <c>screen:&lt;id&gt;</c> (its link is the
    /// screen that screen-opens flow opens) (D410).</summary>
    static string? Via(JsonObject e, List<string> evidence, HashSet<string> fileSet, Reach reach)
    {
        if (evidence.Any(v => fileSet.Contains(FileOf(v)))) return "file";
        if (e["link"] is not JsonObject link || Str(link["id"]) is not { } id) return null;
        return Str(link["kind"]) switch
        {
            "flow" when reach.Flows.Contains(id) => $"flow:{id}",
            "screen" when reach.Screens.TryGetValue(id, out var flow) => $"screen:{flow}",
            _ => null,
        };
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
        if (truth["reach"] is JsonObject { } reach && (bool?)reach["tooMany"] == true)
            md.Append($"The ticket's code sits in more than {MaxFlows} flows (shared code aside), too many to tell which screen it's about, so only entries on its own files are listed.\n\n");
        if (entries.Count == 0) { md.Append("Nothing in the truth map covers this ticket's files, or the flows and screens its code is on: no rule the PM agreed says what should happen here.\n\n"); return; }
        foreach (var e in entries)
        {
            md.Append($"- **{e["id"]}** ({e["doc"]} {Heading(e)}, item {e["n"]}): {TruthMap.StateWords((string)e["state"]!)}");
            if ((string?)e["confidence"] is { } c) md.Append($" ({c} confidence)");
            md.Append((string?)e["ruling"] is { } r ? $" · ruled: {TruthMap.RulingWords(r)}" : "");
            md.Append((bool)e["usable"]! ? (string?)e["why"] is { } w ? $" · can back a ticket ({w})" : " · can back a ticket" : $" · **can't back a ticket**: {e["why"]}").Append('\n');
            if ((string?)e["key"]!["quote"] is { } q) md.Append($"  - The FSD says: “{Quotes.Normalize(q)}”\n");
            if ((string?)e["statement"] is { } st)
                md.Append((string?)e["ruling"] == "keep" ? $"  - The PM's words (how the kept code should work): {st}\n" : $"  - The PM's words: {st}\n");
            foreach (var note in e["notes"]!.AsArray()) md.Append($"  - The review: {(string)note!}\n");
            md.Append($"  - The code: {string.Join(", ", e["evidence"]!.AsArray().Select(v => $"`{(string)v!}`"))}\n");
            if ((string?)e["via"] is { } via && via.IndexOf(':') is var colon and > 0)
                md.Append(via[..colon] == "flow"
                    ? $"  - Found through the flow `{via[(colon + 1)..]}`, which the ticket's code is on (not by file)\n"
                    : $"  - Found through its screen, which the flow `{via[(colon + 1)..]}` opens and the ticket's code is on (not by file)\n");
        }
        md.Append('\n');
        foreach (var s in truth["sections"]!.AsArray().OfType<JsonObject>())
        {
            md.Append($"### {s["doc"]} §{s["section"]} {s["title"]}\n\n");
            md.Append((string?)s["text"] is { } text ? $"````text\n{text.TrimEnd()}\n````\n\n" : "_Not in the FSD any more._\n\n");
        }
    }
}

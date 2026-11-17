using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Pages;
using YamlDotNet.RepresentationModel;

namespace Stangent.Spec.Fsd;

/// <summary>One entry of <c>rulings.yaml</c>: a difference (gap, conflict), code the FSD leaves out (undocumented) or a
/// match, with the PM's ruling. Keyed by its section's permanent id and its quote, or for undocumented code by its D362
/// link (D363, D365 (2)).</summary>
public sealed class RulingEntry
{
    public string? KeySection;
    public string? KeyQuote;
    public string? KeyLink;
    public string State = "";
    public TruthLink Link = new("file", "", "(no evidence)", CanonicalJson.Hash(""));
    public List<string> Evidence = [];
    public SortedSet<int> Runs = [];
    public List<string> Notes = [];
    public string? Confidence;
    public bool Disagrees;
    public string? NotFound;
    public string? Flagged;
    public bool New;
    public bool InFsd;
    public JsonObject? Jira;
    public string? Ruling;
    public string? Statement;
    public string? PmNote;
    /// <summary>The <c>ruling:</c> line's value as the PM typed it, written back unchanged so <c>git blame</c> keeps naming them.</summary>
    public string? RulingRaw;
    /// <summary>The <c>ruling:</c> key's line in the file read (1-based), 0 when not read from a file.</summary>
    public int RulingLine;
    /// <summary>The <c>statement:</c> and <c>jira:</c> lines (1-based, 0 when absent): a ruling from Jira counts as committed only with them (D374).</summary>
    public int StatementLine;
    public int JiraLine;
    /// <summary>A problem found reading the entry (no key, an unknown state).</summary>
    public string? Problem;

    public bool IsMatch => State == "match";
    public bool IsUndocumented => State == "undocumented";
    public bool IsDifference => State is "gap" or "conflict";

    /// <summary>The key compared across review runs: a quote as the merge joins it, a link by its kind and id.</summary>
    public string Key => IsUndocumented || (KeyQuote is null && KeyLink is not null)
        ? $"{KeySection ?? "-"}\u0001link\u0001{KeyLink}"
        : $"{KeySection ?? "-"}\u0001quote\u0001{FsdReview.NormalizeQuote(KeyQuote)}";

    /// <summary>For people: "S-0012 · “A refund sends …”" or "S-0012 · screen:page:…".</summary>
    public string Describe(int quoteChars = 60)
    {
        var where = KeySection ?? "no section";
        if (KeyQuote is { } q)
        {
            var n = Quotes.Normalize(q);
            return $"{where} · “{(n.Length > quoteChars ? n[..quoteChars] + "…" : n)}”";
        }
        return $"{where} · {KeyLink}";
    }
}

/// <summary>One FSD section's block: its matches' confirmation and its entries.</summary>
public sealed class RulingSection
{
    public string Section = "";
    public string SectionId = "";
    public string Title = "";
    public string TextHash = "";
    public string? Matches;
    public string? MatchesRaw;
    public int MatchesLine;
    public List<RulingEntry> Entries = [];
    public bool MatchesConfirmed => Matches == "confirmed";
}

/// <summary><c>specs/fsd/&lt;doc&gt;/rulings.yaml</c> in memory.</summary>
public sealed class RulingsFile
{
    public string Doc = "";
    public string ReviewCommit = "";
    public string FsdVersion = "";
    public string ReviewHash = "";
    public int Runs;
    public List<RulingSection> Sections = [];
    public List<RulingEntry> Undocumented = [];
    public IEnumerable<RulingEntry> All => Sections.SelectMany(s => s.Entries).Concat(Undocumented);
    public IEnumerable<(RulingSection? Section, RulingEntry Entry)> WithSections =>
        Sections.SelectMany(s => s.Entries.Select(e => ((RulingSection?)s, e))).Concat(Undocumented.Select(e => ((RulingSection?)null, e)));
}

/// <summary>
/// The truth map's rulings (D363, D365, D366; contract <c>rulings</c> v1): <c>stangent fsd rulings &lt;doc&gt;</c> drafts or
/// refreshes <c>specs/fsd/&lt;doc&gt;/rulings.yaml</c> from <c>review.json</c>, grouped by FSD section, carrying each ruling
/// over by its key, keeping an old ruling no run finds again as <c>notFound</c>, blanking a section whose text changed, and
/// flagging a ruled entry whose link's fingerprint changed. Code writes every field but the PM's: <c>ruling</c>,
/// <c>statement</c>, <c>pmNote</c> and a section's <c>matches</c>; the PM deletes a <c>flagged</c> or <c>new</c> line once
/// they have looked. Who ruled and when come from git (<see cref="TruthMap"/>), never from the file.
/// </summary>
public static partial class Rulings
{
    public const string Contract = "rulings";
    public const int Version = 1;
    public const string Schema = "rulings.v1.json";
    public static readonly string[] DifferenceRulings = ["code-right", "fsd-right", "restated"];
    public static readonly string[] UndocumentedRulings = ["keep", "remove", "restated"];
    public static readonly string[] MatchRulings = ["confirmed", "not-a-match"];
    public const string LinkChanged = "link changed since ruled";

    public static string[] RulingsFor(string state) => state switch
    {
        "undocumented" => UndocumentedRulings,
        "match" => MatchRulings,
        _ => DifferenceRulings,
    };

    public static string PathFor(string root, string doc) => Path.Combine(FsdImport.DocDir(root, doc), "rulings.yaml");

    static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    // ---- reading ------------------------------------------------------------------------------------------------------

    /// <summary>The file, or null when there is none. Problems: not YAML (thrown), the schema's errors, entries with no key.</summary>
    public static (RulingsFile? File, List<string> Problems) Load(string root, string doc)
    {
        var path = PathFor(root, doc);
        if (!File.Exists(path)) return (null, []);
        return Parse(File.ReadAllText(path));
    }

    public static (RulingsFile File, List<string> Problems) Parse(string yaml)
    {
        var stream = new YamlStream();
        try { stream.Load(new StringReader(yaml)); }
        catch (YamlDotNet.Core.YamlException e)
        {
            throw new StangentException("invalid_rulings", $"rulings.yaml isn't valid YAML at line {e.Start.Line}: {e.Message} Text holding a colon or a # goes in quotes: pmNote: \"a: b\".", ExitCodes.Failed);
        }
        var problems = new List<string>();
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode rootNode)
            throw new StangentException("invalid_rulings", "rulings.yaml must be a mapping.", ExitCodes.Failed);
        var json = ToJson(rootNode, "") as JsonObject ?? [];
        foreach (var e in SchemaRegistry.Embedded.Validate(Schema, json.ToJsonString())) problems.Add($"schema: {e}");
        var lines = yaml.Replace("\r\n", "\n").Split('\n');

        var file = new RulingsFile
        {
            Doc = Str(json["doc"]) ?? "",
            ReviewCommit = Str(json["review"]?["commit"]) ?? "",
            FsdVersion = Str(json["review"]?["fsdVersion"]) ?? "",
            ReviewHash = Str(json["review"]?["hash"]) ?? "",
            Runs = json["review"]?["runs"] is JsonValue rv && rv.TryGetValue<int>(out var runs) ? runs : 0,
        };
        var sectionNodes = Child(rootNode, "sections") as YamlSequenceNode;
        var sectionsJson = json["sections"] as JsonArray ?? [];
        for (var i = 0; i < sectionsJson.Count; i++)
        {
            if (sectionsJson[i] is not JsonObject s) continue;
            var node = sectionNodes?.Children.ElementAtOrDefault(i) as YamlMappingNode;
            var block = new RulingSection
            {
                Section = Str(s["section"]) ?? "", SectionId = Str(s["sectionId"]) ?? "", Title = Str(s["title"]) ?? "",
                TextHash = Str(s["textHash"]) ?? "", Matches = Str(s["matches"]),
            };
            (block.MatchesLine, block.MatchesRaw) = Raw(node, "matches", lines, block.Matches);
            var entryNodes = node is null ? null : Child(node, "entries") as YamlSequenceNode;
            var entriesJson = s["entries"] as JsonArray ?? [];
            for (var j = 0; j < entriesJson.Count; j++)
                if (entriesJson[j] is JsonObject e) block.Entries.Add(ReadEntry(e, entryNodes?.Children.ElementAtOrDefault(j) as YamlMappingNode, lines));
            file.Sections.Add(block);
        }
        var undocNodes = Child(rootNode, "undocumented") as YamlSequenceNode;
        var undocJson = json["undocumented"] as JsonArray ?? [];
        for (var j = 0; j < undocJson.Count; j++)
            if (undocJson[j] is JsonObject e) file.Undocumented.Add(ReadEntry(e, undocNodes?.Children.ElementAtOrDefault(j) as YamlMappingNode, lines));
        return (file, problems);
    }

    static RulingEntry ReadEntry(JsonObject e, YamlMappingNode? node, string[] lines)
    {
        var entry = new RulingEntry
        {
            State = Str(e["state"]) ?? "",
            Evidence = [.. (e["evidence"] as JsonArray ?? []).Select(x => Str(x) ?? "")],
            Notes = [.. (e["notes"] as JsonArray ?? []).Select(x => Str(x) ?? "")],
            Confidence = Str(e["confidence"]),
            Disagrees = e["disagrees"] is JsonValue d && d.TryGetValue<bool>(out var dv) && dv,
            NotFound = Str(e["notFound"]),
            Flagged = Str(e["flagged"]),
            New = e["new"] is JsonValue n && n.TryGetValue<bool>(out var nv) && nv,
            InFsd = e["inFsd"] is JsonValue f && f.TryGetValue<bool>(out var fv) && fv,
            Jira = e["jira"] as JsonObject is { } j ? (JsonObject)j.DeepClone() : null,
            Ruling = Str(e["ruling"]),
            Statement = Str(e["statement"]),
            PmNote = Str(e["pmNote"]),
        };
        foreach (var r in (e["runs"] as JsonArray ?? []).OfType<JsonValue>()) if (r.TryGetValue<int>(out var run)) entry.Runs.Add(run);
        if (e["key"] is JsonObject key)
        {
            entry.KeySection = Str(key["section"]);
            entry.KeyQuote = Str(key["quote"]);
            entry.KeyLink = Str(key["link"]);
            if (entry.KeyQuote is null && entry.KeyLink is null) entry.Problem = "its key has neither a quote nor a link";
        }
        else entry.Problem = "it has no key (an entry is written by `stangent fsd rulings`, never added by hand)";
        if (e["link"] is JsonObject link && Str(link["kind"]) is { } kind)
            entry.Link = new TruthLink(kind, Str(link["id"]) ?? "", Str(link["title"]) ?? "", Str(link["hash"]) ?? "", link["exact"] is JsonValue x && x.TryGetValue<bool>(out var xv) && xv);
        (entry.RulingLine, entry.RulingRaw) = Raw(node, "ruling", lines, entry.Ruling);
        entry.StatementLine = Raw(node, "statement", lines, null).Line;
        entry.JiraLine = Raw(node, "jira", lines, null).Line;
        return entry;
    }

    /// <summary>A key's line, and its value as typed when it sits on that one line and reads as <paramref name="value"/>.</summary>
    static (int Line, string? Raw) Raw(YamlMappingNode? node, string key, string[] lines, string? value)
    {
        if (node is null) return (0, null);
        foreach (var (k, v) in node.Children)
        {
            if (k is not YamlScalarNode { Value: var name } || name != key) continue;
            var line = (int)k.Start.Line;
            if (value is null || v is not YamlScalarNode sv || sv.Start.Line != k.Start.Line || sv.End.Line != k.Start.Line || line < 1 || line > lines.Length)
                return (line, null);
            var m = RawValue().Match(lines[line - 1]);
            return (line, m.Success && m.Groups["key"].Value == key ? m.Groups["rest"].Value.TrimEnd() : null);
        }
        return (0, null);
    }

    [GeneratedRegex(@"^\s*(?<key>[A-Za-z]+):[ \t]*(?<rest>.*)$")]
    private static partial Regex RawValue();

    static YamlNode? Child(YamlMappingNode node, string key) =>
        node.Children.FirstOrDefault(c => c.Key is YamlScalarNode { Value: var v } && v == key).Value;

    static readonly HashSet<string> IntKeys = ["version", "runs"];
    static readonly HashSet<string> BoolKeys = ["exact", "disagrees", "new", "inFsd"];

    /// <summary>YAML → the JSON the schema checks: plain scalars typed by their key (numbers in <c>version</c> and <c>runs</c>,
    /// booleans in <c>exact</c>, <c>disagrees</c>, <c>new</c>, <c>inFsd</c>), blanks as null, everything else a string.</summary>
    static JsonNode? ToJson(YamlNode node, string key) => node switch
    {
        YamlMappingNode m => new JsonObject(m.Children.Select(c => KeyValuePair.Create(((YamlScalarNode)c.Key).Value ?? "", ToJson(c.Value, ((YamlScalarNode)c.Key).Value ?? "")))),
        YamlSequenceNode s => new JsonArray([.. s.Children.Select(c => ToJson(c, key))]),
        YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain, Value: null or "" or "~" or "null" or "Null" or "NULL" } => null,
        YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain } sc when IntKeys.Contains(key) && int.TryParse(sc.Value, out var i) => i,
        YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain } sc when BoolKeys.Contains(key) && sc.Value is "true" or "false" => sc.Value == "true",
        YamlScalarNode sc => sc.Value,
        _ => null,
    };

    static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? (string.IsNullOrWhiteSpace(s) ? null : s.Trim()) : null;

    // ---- writing ------------------------------------------------------------------------------------------------------

    /// <summary>The file in one fixed form, so the same rulings give the same bytes and a diff shows only what changed.
    /// Code-written values are JSON strings (valid YAML); the PM's <c>ruling</c> and <c>matches</c> lines keep their text as
    /// typed, so <c>git blame</c> on them keeps naming who ruled.</summary>
    public static string Write(RulingsFile file)
    {
        var sb = new StringBuilder();
        sb.Append($"# The PM's rulings on the FSD review of {file.Doc} (contract rulings v1; D361, D363, D366). Written by `stangent fsd rulings`.\n");
        sb.Append("# Nobody needs to edit this file (D371): read truth-map.md and tell Claude the answers (`/stangent:rule " + file.Doc + "`),\n");
        sb.Append("# or run `stangent fsd rule " + file.Doc + " <number> <answer>`. A hand edit is still read; then change only these:\n");
        sb.Append("#   ruling:    a gap or conflict: code-right, fsd-right or restated; undocumented code: keep, remove or restated;\n");
        sb.Append("#              a match: confirmed or not-a-match\n");
        sb.Append("#   statement: the rule in your words; required for restated\n");
        sb.Append("#   pmNote:    anything else\n");
        sb.Append("#   matches:   confirmed, once a section's matches look right (a match added later shows `new: true`)\n");
        sb.Append("# and delete a `flagged:` or `new:` line once you have looked. Put text holding a colon or a # in quotes. An entry marked\n");
        sb.Append("# `notFound:` stays until it is dropped. Comments outside the ruling and matches lines aren't kept.\n");
        sb.Append("# Who answered and when come from git, never from this file:\n");
        sb.Append("# commit, then run `stangent fsd rulings-check " + file.Doc + "`.\n");
        sb.Append($"contract: {Rulings.Contract}\nversion: {Version}\n");
        sb.Append($"doc: {S(file.Doc)}\n");
        sb.Append($"review: {{ commit: {S(file.ReviewCommit)}, fsdVersion: {S(file.FsdVersion)}, runs: {file.Runs}, hash: {S(file.ReviewHash)} }}\n");
        sb.Append("sections:");
        if (file.Sections.Count == 0) sb.Append(" []");
        sb.Append('\n');
        foreach (var s in file.Sections)
        {
            sb.Append($"  - section: {S(s.Section)}\n");
            sb.Append($"    sectionId: {S(s.SectionId)}\n");
            sb.Append($"    title: {S(s.Title)}\n");
            sb.Append($"    textHash: {S(s.TextHash)}\n");
            if (s.Matches is not null || s.Entries.Any(e => e.IsMatch)) sb.Append(Pm("    ", "matches", s.Matches, s.MatchesRaw));
            sb.Append("    entries:");
            if (s.Entries.Count == 0) sb.Append(" []");
            sb.Append('\n');
            foreach (var e in s.Entries) WriteEntry(sb, e, "      ");
        }
        sb.Append("undocumented:");
        if (file.Undocumented.Count == 0) sb.Append(" []");
        sb.Append('\n');
        foreach (var e in file.Undocumented) WriteEntry(sb, e, "  ");
        return sb.ToString();
    }

    static void WriteEntry(StringBuilder sb, RulingEntry e, string indent)
    {
        var key = e.KeyQuote is not null ? $"{{ section: {S(e.KeySection)}, quote: {S(e.KeyQuote)} }}" : $"{{ section: {S(e.KeySection)}, link: {S(e.KeyLink)} }}";
        sb.Append($"{indent}- key: {key}\n");
        var p = indent + "  ";
        sb.Append($"{p}state: {S(e.State)}\n");
        sb.Append($"{p}link: {{ kind: {S(e.Link.Kind)}, id: {S(e.Link.Id)}, title: {S(e.Link.Title)}, hash: {S(e.Link.Hash)}, exact: {(e.Link.Exact ? "true" : "false")} }}\n");
        sb.Append($"{p}evidence: [{string.Join(", ", e.Evidence.Select(S))}]\n");
        sb.Append($"{p}runs: [{string.Join(", ", e.Runs)}]\n");
        if (e.Confidence is not null) sb.Append($"{p}confidence: {S(e.Confidence)}\n");
        if (e.Disagrees) sb.Append($"{p}disagrees: true\n");
        sb.Append($"{p}notes:");
        if (e.Notes.Count == 0) sb.Append(" []");
        sb.Append('\n');
        foreach (var n in e.Notes) sb.Append($"{p}  - {S(n)}\n");
        if (e.NotFound is not null) sb.Append($"{p}notFound: {S(e.NotFound)}\n");
        if (e.Flagged is not null) sb.Append($"{p}flagged: {S(e.Flagged)}\n");
        if (e.New) sb.Append($"{p}new: true\n");
        if (e.InFsd) sb.Append($"{p}inFsd: true\n");
        if (e.Jira is not null) sb.Append($"{p}jira: {e.Jira.ToJsonString(Json)}\n");
        sb.Append(Pm(p, "ruling", e.Ruling, e.RulingRaw));
        sb.Append($"{p}statement:{(e.Statement is null ? "" : " " + S(e.Statement))}\n");
        sb.Append($"{p}pmNote:{(e.PmNote is null ? "" : " " + S(e.PmNote))}\n");
    }

    /// <summary>A PM's one-word value: as typed when it still reads the same, else plain (the words are plain-safe).</summary>
    static string Pm(string indent, string key, string? value, string? raw) =>
        value is null ? $"{indent}{key}:\n" : $"{indent}{key}: {raw ?? (PlainSafe().IsMatch(value) ? value : S(value))}\n";

    [GeneratedRegex(@"^[a-z][a-z-]*$")]
    private static partial Regex PlainSafe();

    static string S(string? s) => s is null ? "null" : JsonSerializer.Serialize(s, Json);

    static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // ---- fsd rulings <doc> --------------------------------------------------------------------------------------------

    /// <summary>Ruled by a person: a ruling, or a match its section's confirmation covers.</summary>
    public static bool Ruled(RulingEntry e, RulingSection? section) =>
        e.Ruling is not null || (e.IsMatch && section is { MatchesConfirmed: true } && !e.New);

    /// <summary>Holds something a person wrote, so it is kept when no run finds it again.</summary>
    static bool PersonWork(RulingEntry e, RulingSection? section) =>
        Ruled(e, section) || e.Statement is not null || e.PmNote is not null || e.Jira is not null;

    /// <summary><c>fsd rulings &lt;doc&gt;</c>: drafts <c>rulings.yaml</c> from <c>review.json</c>, or refreshes it.</summary>
    public static JsonObject Refresh(StangentRepo repo, string doc)
    {
        var root = repo.Root;
        var reviewPath = FsdReview.ReviewJson(root, doc);
        var reviewText = File.Exists(reviewPath) ? File.ReadAllText(reviewPath) : null;
        var review = reviewText is null ? null : JsonNode.Parse(reviewText) as JsonObject;
        if (review is null)
            throw new StangentException("not_reviewed", $"{doc} has no merged review yet. Run `stangent fsd review-merge {doc}` first.", ExitCodes.NotFound);
        JsonObject document;
        try { document = FsdOutline.Load(root, doc); }
        catch (StangentException) { throw new StangentException("not_imported", $"{doc} isn't imported: rulings are kept by FSD section, so `stangent fsd import` it first.", ExitCodes.NotFound); }

        var (old, oldProblems) = Load(root, doc);
        if (old is not null)
        {
            var dup = old.All.Where(e => e.Problem is null).GroupBy(e => e.Key).Where(g => g.Count() > 1).Select(g => g.First().Describe()).ToList();
            var broken = old.All.Where(e => e.Problem is not null).Select(e => $"{e.Describe()}: {e.Problem}").ToList();
            var problems = oldProblems.Concat(broken).Concat(dup.Select(d => $"{d}: two entries have this key")).ToList();
            if (problems.Count > 0)
                throw new StangentException("invalid_rulings", $"{Rel(root, PathFor(root, doc))} needs fixing before it is refreshed (nothing was written).", ExitCodes.Failed, problems);
        }

        var built = Build(repo, doc, review, reviewText!, document);
        var file = built.File;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal) { ["new"] = 0, ["carried"] = 0, ["foundAgain"] = 0, ["notFoundNow"] = 0, ["stillNotFound"] = 0, ["flaggedNow"] = 0 };
        var dropped = new JsonArray();
        var changedSections = new JsonArray();
        var oldByKey = (old?.WithSections ?? []).ToDictionary(x => x.Entry.Key, x => x);
        var oldSections = (old?.Sections ?? []).ToDictionary(s => s.SectionId, StringComparer.Ordinal);
        var blocks = file.Sections.ToDictionary(s => s.SectionId, StringComparer.Ordinal);
        var oldHash = new Dictionary<RulingEntry, (string Hash, bool Ruled)>();

        foreach (var (section, e) in file.WithSections.ToList())
        {
            var oldSection = section is null ? null : oldSections.GetValueOrDefault(section.SectionId);
            if (oldByKey.Remove(e.Key, out var was))
            {
                Carry(was.Entry, e);
                oldHash[e] = (was.Entry.Link.Hash, Ruled(was.Entry, was.Section));
                counts["carried"]++;
                if (was.Entry.NotFound is not null) counts["foundAgain"]++;
            }
            else
            {
                counts["new"]++;
                if (e.IsMatch && oldSection is { MatchesConfirmed: true }) e.New = true;
            }
        }
        // Old entries no run found again: kept when they hold a person's work, marked with the review's commit (D363).
        foreach (var (oldSection, e) in oldByKey.Values)
        {
            if (!PersonWork(e, oldSection))
            {
                dropped.Add((JsonNode)e.Describe());
                continue;
            }
            // An entry a ticket's Jira reply added (D374) was never a review finding, so no run "not finding it again" says anything.
            if (JiraRulings.FromTicket(e)) { }
            else if (e.NotFound is null) { e.NotFound = file.ReviewCommit; counts["notFoundNow"]++; }
            else counts["stillNotFound"]++;
            if (e.KeySection is null || oldSection is null) { file.Undocumented.Add(e); continue; }
            if (!blocks.TryGetValue(oldSection.SectionId, out var block))
            {
                block = new RulingSection { Section = oldSection.Section, SectionId = oldSection.SectionId, Title = oldSection.Title, TextHash = built.TextHashes.GetValueOrDefault(oldSection.SectionId) ?? oldSection.TextHash };
                blocks[block.SectionId] = block;
                file.Sections.Add(block);
            }
            block.Entries.Add(e);
        }
        // A section's matches confirmation carries over by its permanent id.
        foreach (var block in file.Sections)
            if (oldSections.TryGetValue(block.SectionId, out var os)) (block.Matches, block.MatchesRaw) = (os.Matches, os.MatchesRaw);

        // A section whose own text changed drops its rulings to blank, and so does an entry whose quote (perhaps from a
        // subsection) is no longer word for word in it; unless the new text holds a restated statement (D363). The section's
        // own text, not its subsections': an edit in 1.1 doesn't blank 1's rulings on sentences that didn't change.
        foreach (var block in file.Sections)
        {
            if (!oldSections.TryGetValue(block.SectionId, out var os)) continue;
            var full = built.Texts.GetValueOrDefault(block.SectionId) ?? "";
            var changed = os.TextHash.Length > 0 && os.TextHash != block.TextHash;
            var wasConfirmed = block.MatchesConfirmed;
            if (changed)
            {
                changedSections.Add((JsonNode)block.SectionId);
                (block.Matches, block.MatchesRaw) = (null, null);
            }
            foreach (var e in block.Entries)
            {
                var quoteGone = e.KeyQuote is { } q && !FsdReview.Quoted(q, full);
                if (!changed && !quoteGone) continue;
                if (e.Ruling == "restated" && e.Statement is { } st && FsdReview.Quoted(st, full)) { e.InFsd = true; continue; }
                e.InFsd = false;
                var why = changed ? "FSD section changed" : "its FSD quote is no longer in the section";
                if (e.Ruling is { } r)
                {
                    e.Flagged = $"{why} since ruled (was {r})";
                    (e.Ruling, e.RulingRaw) = (null, null);
                    counts["flaggedNow"]++;
                }
                else if (e.IsMatch && wasConfirmed && !e.New)
                {
                    e.Flagged = $"{why} since its matches were confirmed";
                    counts["flaggedNow"]++;
                }
                if (changed) e.New = false;
            }
        }
        // A ruled entry whose link's fingerprint changed is flagged for the PM (D362 (2), D365 (3)).
        foreach (var (section, e) in file.WithSections)
        {
            if (!oldHash.TryGetValue(e, out var h) || h.Hash == e.Link.Hash || !h.Ruled || !Ruled(e, section)) continue;
            if (e.Flagged is null) { e.Flagged = LinkChanged; counts["flaggedNow"]++; }
        }
        foreach (var block in file.Sections)
        {
            if (!block.MatchesConfirmed) foreach (var e in block.Entries) e.New = false;
            Sort(block.Entries, built.Texts.GetValueOrDefault(block.SectionId) ?? "");
        }
        file.Sections.Sort((a, b) => built.Order.GetValueOrDefault(a.SectionId, int.MaxValue).CompareTo(built.Order.GetValueOrDefault(b.SectionId, int.MaxValue)) is var c and not 0
            ? c : string.CompareOrdinal(a.SectionId, b.SectionId));
        file.Undocumented.Sort((a, b) => string.CompareOrdinal(a.KeyLink, b.KeyLink));

        var path = PathFor(root, doc);
        // Line endings as the file had them: a refresh that rewrote CRLF as LF would change every line, and git blame would
        // then name whoever committed the refresh, not the PM.
        var text = Write(file);
        if (File.Exists(path) && File.ReadAllText(path).Contains("\r\n", StringComparison.Ordinal)) text = text.Replace("\n", "\r\n");
        Quotes.WriteAtomic(path, text);
        var warnings = new JsonArray([.. built.Warnings.Select(w => (JsonNode)w)]);
        var all = file.All.ToList();
        return new JsonObject
        {
            ["doc"] = doc, ["file"] = Rel(root, path), ["created"] = old is null,
            ["entries"] = all.Count, ["sections"] = file.Sections.Count, ["undocumented"] = file.Undocumented.Count,
            ["counts"] = new JsonObject([.. counts.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))]),
            ["blank"] = file.WithSections.Count(x => !Ruled(x.Entry, x.Section)),
            ["flagged"] = all.Count(e => e.Flagged is not null),
            ["sectionsChanged"] = changedSections,
            ["droppedBlank"] = dropped,
            ["summary"] = $"{all.Count} entries in {file.Sections.Count} sections and {file.Undocumented.Count} undocumented; {counts["new"]} new, {counts["carried"]} carried over, "
                + $"{counts["notFoundNow"] + counts["stillNotFound"]} not found again, {all.Count(e => e.Flagged is not null)} flagged",
            ["warnings"] = warnings,
        };
    }

    static void Carry(RulingEntry from, RulingEntry to)
    {
        to.Ruling = from.Ruling;
        to.RulingRaw = from.RulingRaw;
        to.Statement = from.Statement;
        to.PmNote = from.PmNote;
        to.Flagged = from.Flagged;
        to.New = from.New;
        to.InFsd = from.InFsd;
        to.Jira = from.Jira;
        // A gap that became a match (or the other way) can't keep a ruling of the other kind: blank, and say why.
        if (from.Ruling is { } r && !RulingsFor(to.State).Contains(r) && RulingsFor(from.State).Contains(r))
        {
            (to.Ruling, to.RulingRaw) = (null, null);
            to.Flagged = $"the review now calls this a {to.State}; it was a {from.State}, ruled {r}";
        }
    }

    /// <summary>Differences in the order their quotes appear in the section, then undocumented code by link, then matches
    /// in reading order: a stable order, so a refresh moves no line and git blame stays with whoever ruled.</summary>
    static void Sort(List<RulingEntry> entries, string sectionText)
    {
        var text = FsdReview.NormalizeQuote(sectionText);
        int Group(RulingEntry e) => e.IsDifference ? 0 : e.IsUndocumented ? 1 : 2;
        int At(RulingEntry e) => e.KeyQuote is { } q && text.IndexOf(FsdReview.NormalizeQuote(q), StringComparison.Ordinal) is var i and >= 0 ? i : int.MaxValue;
        entries.Sort((a, b) =>
        {
            var c = Group(a).CompareTo(Group(b));
            if (c == 0) c = At(a).CompareTo(At(b));
            if (c == 0) c = string.CompareOrdinal(a.Key, b.Key);
            return c;
        });
    }

    sealed record Built(RulingsFile File, Dictionary<string, string> Texts, Dictionary<string, string> TextHashes, Dictionary<string, int> Order, List<string> Warnings);

    /// <summary>The entries <c>review.json</c> gives today, linked, grouped by key, in their section blocks.</summary>
    static Built Build(StangentRepo repo, string doc, JsonObject review, string reviewText, JsonObject document)
    {
        var root = repo.Root;
        var sections = document["sections"]!.AsArray().OfType<JsonObject>().ToList();
        var full = FsdReview.SectionTexts(document);
        var permanent = sections.ToDictionary(s => (string)s["id"]!, s => (string?)s["permanentId"] ?? (string)s["id"]!, StringComparer.Ordinal);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (s, i) in sections.Select((s, i) => (s, i)))
        {
            var pid = permanent[(string)s["id"]!];
            texts[pid] = full[(string)s["id"]!];
            hashes[pid] = CanonicalJson.Hash(((string)s["text"]!).Replace("\r\n", "\n"));
            order[pid] = i;
        }
        var warnings = new List<string>();
        if ((string?)document["original"]?["hash"] is { } current && current != (string?)review["fsdVersion"])
            warnings.Add($"review.json was made on an older version of {doc}; its quotes are read against today's sections. Run the review again.");
        var linker = new TruthLinks(root, GraphStore.Load(GraphStore.PathFor(repo)));
        if (!linker.HasFlows) warnings.Add($"No flows in {Stangent.Flows.FlowFiles.Folder}: run `stangent flows build`; nothing links to a flow.");
        if (!linker.HasInventory) warnings.Add($"No {Stangent.Flows.InventoryFiles.JsonFile}: run `stangent inventory`; nothing links to a screen.");
        var reviewRuns = review["runs"]?.AsArray().Count ?? 0;

        var groups = new Dictionary<string, List<(JsonObject Finding, TruthLink Link, string? Section)>>(StringComparer.Ordinal);
        var keys = new List<string>();
        foreach (var f in review["findings"]!.AsArray().OfType<JsonObject>())
        {
            var state = (string)f["state"]!;
            var section = (string?)f["sectionId"] ?? ((string?)f["section"] is { } s ? permanent.GetValueOrDefault(s, s) : null);
            var link = linker.Link(f).Link;
            var key = state == "undocumented"
                ? $"{section ?? "-"}\u0001link\u0001{link.Key}"
                : $"{section ?? "-"}\u0001quote\u0001{FsdReview.NormalizeQuote((string?)f["fsdQuote"])}";
            if (!groups.TryGetValue(key, out var list)) { groups[key] = list = []; keys.Add(key); }
            list.Add((f, link, section));
        }

        var file = new RulingsFile { Doc = doc, ReviewCommit = (string?)review["commit"] ?? "", FsdVersion = (string?)review["fsdVersion"] ?? "", ReviewHash = CanonicalJson.Hash(reviewText), Runs = reviewRuns };
        var blocks = new Dictionary<string, RulingSection>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var list = groups[key];
            var differences = list.Where(x => (string)x.Finding["state"]! is "gap" or "conflict").ToList();
            var matches = list.Where(x => (string)x.Finding["state"]! == "match").ToList();
            var undocumented = list.Where(x => (string)x.Finding["state"]! == "undocumented").ToList();
            var use = undocumented.Count > 0 ? undocumented : differences.Count > 0 ? differences : matches;
            var state = undocumented.Count > 0 ? "undocumented"
                : differences.Count > 0 ? differences.Select(x => (string)x.Finding["state"]!).OrderBy(s => Array.IndexOf(FsdReview.States, s)).First() : "match";
            var first = use[0];
            var e = new RulingEntry
            {
                KeySection = first.Section, State = state,
                KeyQuote = state == "undocumented" ? null : (string?)first.Finding["fsdQuote"],
                Link = use.Select(x => x.Link).OrderBy(l => l.Precision).ThenBy(l => l.Key, StringComparer.Ordinal).First(),   // not by finding order: a re-review that reorders them mustn't flag
                Disagrees = differences.Count > 0 && matches.Count > 0,
            };
            if (state == "undocumented") e.KeyLink = e.Link.Key;
            foreach (var (f, _, _) in use)
            {
                foreach (var ev in f["evidence"]!.AsArray().Select(x => ((string)x!).Trim())) if (!e.Evidence.Contains(ev)) e.Evidence.Add(ev);
                foreach (var r in f["runs"]?.AsArray().Select(x => (int)x!) ?? []) e.Runs.Add(r);
                if ((string?)f["note"] is { } note && !e.Notes.Contains(note)) e.Notes.Add(note);
            }
            if (e.IsMatch) e.Confidence = Confidence(e.Runs.Count >= reviewRuns && reviewRuns > 0, !e.Link.Fallback);
            if (e.KeySection is null) { file.Undocumented.Add(e); continue; }
            if (!blocks.TryGetValue(e.KeySection, out var block))
            {
                var s = sections.FirstOrDefault(x => permanent[(string)x["id"]!] == e.KeySection);
                block = new RulingSection
                {
                    SectionId = e.KeySection, Section = (string?)s?["id"] ?? e.KeySection, Title = (string?)s?["title"] ?? "",
                    TextHash = hashes.GetValueOrDefault(e.KeySection) ?? "",
                };
                if (s is null) warnings.Add($"review.json names section {e.KeySection}, which today's {doc} doesn't have.");
                blocks[e.KeySection] = block;
                file.Sections.Add(block);
            }
            block.Entries.Add(e);
        }
        return new Built(file, texts, hashes, order, warnings);
    }

    /// <summary>D366 (4): high when every run found the match and its line is on a flow or a screen, medium for one, low for neither.</summary>
    public static string Confidence(bool everyRun, bool flowOrScreen) => (everyRun, flowOrScreen) switch
    {
        (true, true) => "high",
        (false, false) => "low",
        _ => "medium",
    };
}

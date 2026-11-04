using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;
using YamlDotNet.RepresentationModel;

namespace Stangent.Spec.Trace;

/// <summary>
/// <c>specs/trace.yaml</c> (D58, contract <c>trace-links</c> v4): one entry per link or finding between an FSD section
/// and a reference page, with both quotes, the state, and the decision a person makes by editing the file in a PR.
/// Written by code in one fixed form, so a re-run with the same content gives the same bytes; the dashboard (Phase 8)
/// will edit the same file.
/// </summary>
public static class TraceFile
{
    public const string Schema = "trace-links.v4.json";
    public static readonly string[] States = ["agree", "gap", "conflict", "undocumented", "table-fact"];
    /// <summary>A difference found by comparing a table with the code, by code (C.2, D124): it has no page, and it holds what was compared in <c>table_fact</c>.</summary>
    public const string TableFact = "table-fact";
    public static readonly string[] Decisions = ["agree", "code-wrong", "fsd-wrong", "page-wrong", "defer", "accepted", "not-a-difference"];

    /// <summary>What a finding carries beyond its link (D118).</summary>
    public static readonly string[] FindingFields = ["finding", "severity", "severity_by", "evidence", "evidence_hash", "verdict", "disproof", "disproof_hash", "review", "review_note", "review_hash", "standing", "standing_note", "standing_hash"];

    public static string PathIn(string root) => Path.Combine(root, "specs", "trace.yaml");

    public static JsonObject Empty() => new() { ["version"] = 3, ["entries"] = new JsonArray() };

    public static JsonObject Load(string root) => File.Exists(PathIn(root)) ? Parse(File.ReadAllText(PathIn(root))) : Empty();

    /// <summary>YAML → the same JSON the schema checks. Every scalar stays a string except null, numbers in <c>version</c>.</summary>
    public static JsonObject Parse(string yaml)
    {
        var stream = new YamlStream();
        try { stream.Load(new StringReader(yaml)); }
        catch (YamlDotNet.Core.YamlException e) { throw new StangentException("invalid_trace", $"trace.yaml isn't valid YAML: {e.Message}"); }
        if (stream.Documents.Count == 0) return Empty();
        return ToJson(stream.Documents[0].RootNode, "") as JsonObject ?? throw new StangentException("invalid_trace", "trace.yaml must be a mapping.");
    }

    internal static JsonNode? ToJson(YamlNode node, string key) => node switch
    {
        YamlMappingNode m => new JsonObject(m.Children.Select(c => KeyValuePair.Create(((YamlScalarNode)c.Key).Value!, ToJson(c.Value, ((YamlScalarNode)c.Key).Value!)))),
        YamlSequenceNode s => new JsonArray([.. s.Children.Select(c => ToJson(c, ""))]),
        YamlScalarNode { Value: null or "" or "~" or "null" } sc when sc.Style == YamlDotNet.Core.ScalarStyle.Plain => null,
        YamlScalarNode sc when key == "version" && int.TryParse(sc.Value, out var v) => v,
        // An evidence record's lines; the contract wants numbers.
        YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain } sc when key is "start" or "end" or "row" && int.TryParse(sc.Value, out var line) => line,
        YamlScalarNode sc => sc.Value,
        _ => null,
    };

    /// <summary>The file, in one fixed form: entries sorted by document, section, page and id; keys in a fixed order.</summary>
    public static string Write(JsonObject trace)
    {
        var sb = new StringBuilder();
        sb.Append("# FSD ↔ reference page links and findings (trace-links v3, D58, D99, D118). Edit `decision` only:\n");
        sb.Append("# agree, code-wrong, fsd-wrong, page-wrong, defer, accepted or not-a-difference. On a finding, also `severity`: blocker, major, minor, cosmetic or ungraded.\n");
        sb.Append("# `decided_by` and `severity_by` are filled from git after merge, never typed. Code fills the rest.\n");
        sb.Append("version: 4\nentries:");
        var entries = Sorted(trace["entries"]?.AsArray() ?? []).ToList();
        if (entries.Count == 0) sb.Append(" []");
        sb.Append('\n');
        foreach (var e in entries)
        {
            sb.Append($"  - id: {S(e["id"])}\n");
            sb.Append($"    state: {S(e["state"])}\n");
            foreach (var end in new[] { "fsd", "page" })
            {
                if (e[end] is not JsonObject o) { sb.Append($"    {end}: null\n"); continue; }
                sb.Append($"    {end}:\n");
                foreach (var k in end == "fsd" ? new[] { "doc", "section", "sectionId", "title", "requirement", "quote", "quoteHash", "attachment", "attachmentFile", "attachmentSheet", "attachmentRow" } : ["id", "statement", "quote", "quoteHash"])
                    if (o[k] is not null) sb.Append($"      {k}: {S(o[k])}\n");
            }
            sb.Append($"    by: {S(e["by"])}\n");
            if (e["check"] is not null) sb.Append($"    check: {S(e["check"])}\n");
            if (e["note"] is not null) sb.Append($"    note: {S(e["note"])}\n");
            if (e["table_fact"] is JsonObject tableFact) sb.Append($"    table_fact: {tableFact.ToJsonString(Json)}\n");
            sb.Append($"    decision: {S(e["decision"])}\n");
            sb.Append($"    decided_by: {S(e["decided_by"])}\n");
            if (e["decided_on"] is not null) sb.Append($"    decided_on: {S(e["decided_on"])}\n");
            if (e["finding"] is not null) sb.Append($"    finding: {S(e["finding"])}\n");
            if (e["severity"] is not null) sb.Append($"    severity: {S(e["severity"])}\n");
            if (e["severity_by"] is not null) sb.Append($"    severity_by: {S(e["severity_by"])}\n");
            if (e["evidence"] is JsonArray { Count: > 0 } evidence)
            {
                // One record a line, as JSON, which is valid YAML: only code writes these, so a diff stays short.
                sb.Append("    evidence:\n");
                foreach (var record in evidence) sb.Append($"      - {record?.ToJsonString(Json) ?? "null"}\n");
            }
            if (e["evidence_hash"] is not null) sb.Append($"    evidence_hash: {S(e["evidence_hash"])}\n");
            if (e["verdict"] is not null) sb.Append($"    verdict: {S(e["verdict"])}\n");
            if (e["disproof"] is JsonObject disproof) sb.Append($"    disproof: {disproof.ToJsonString(Json)}\n");
            if (e["disproof_hash"] is not null) sb.Append($"    disproof_hash: {S(e["disproof_hash"])}\n");
            if (e["review"] is not null) sb.Append($"    review: {S(e["review"])}\n");
            if (e["review_note"] is JsonObject note) sb.Append($"    review_note: {note.ToJsonString(Json)}\n");
            if (e["review_hash"] is not null) sb.Append($"    review_hash: {S(e["review_hash"])}\n");
            if (e["standing"] is not null) sb.Append($"    standing: {S(e["standing"])}\n");
            if (e["standing_note"] is JsonObject standing) sb.Append($"    standing_note: {standing.ToJsonString(Json)}\n");
            if (e["standing_hash"] is not null) sb.Append($"    standing_hash: {S(e["standing_hash"])}\n");
        }
        return sb.ToString();
    }

    public static void Save(string root, JsonObject trace)
    {
        Quotes.WriteAtomic(PathIn(root), Write(trace));
    }

    public static IEnumerable<JsonObject> Sorted(JsonArray entries) => entries.OfType<JsonObject>()
        .OrderBy(e => (string?)e["fsd"]?["doc"] ?? "~", StringComparer.Ordinal)
        .ThenBy(e => SectionKey((string?)e["fsd"]?["section"]), StringComparer.Ordinal)
        .ThenBy(e => (string?)e["page"]?["id"] ?? "", StringComparer.Ordinal)
        .ThenBy(e => (string?)e["id"] ?? "", StringComparer.Ordinal);

    /// <summary>"4.10" sorts after "4.9": numbers are padded; heading paths sort after numbered sections.</summary>
    public static string SectionKey(string? id) => id is null ? "~" :
        System.Text.RegularExpressions.Regex.IsMatch(id, @"^\d+(\.\d+)*$") ? string.Join(".", id.Split('.').Select(p => p.PadLeft(4, '0'))) : "~" + id;

    /// <summary>A JSON string is a valid YAML double-quoted scalar, so every value is written that way (or null).</summary>
    static string S(JsonNode? n) => n is null ? "null" : JsonSerializer.Serialize((string)n!, Json);

    static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>An entry's id: the same link and quotes always get the same id, so decisions survive a re-run.</summary>
    /// <summary>The id an entry should have, with its section as given. A table fact has no page quote, so what was compared stands in for it: two members missing from one table cite the same header.</summary>
    public static string IdOf(JsonObject e, string? section) => EntryId((string)e["state"]!, (string?)e["fsd"]?["doc"], section, (string?)e["page"]?["id"], (string?)e["fsd"]?["quote"],
        (string?)e["table_fact"]?["key"] ?? (string?)e["page"]?["quote"], (string?)e["fsd"]?["requirement"]);

    public static string EntryId(string state, string? doc, string? section, string? page, string? fsdQuote, string? pageQuote, string? requirement = null) =>
        "L" + CanonicalJson.Hash(string.Join("\u0001", [state, doc ?? "", section ?? "", page ?? "", Quotes.Normalize(fsdQuote ?? ""), Quotes.Normalize(pageQuote ?? ""), .. requirement is null ? Array.Empty<string>() : [requirement]]))[..10];

    /// <summary>A link without its section. Its id holds the section's number, so a section that moved has a new id; this is how a base
    /// file's copy of the entry is still found (D117). Two entries sharing a key are left out, because it can't tell them apart.</summary>
    public static IReadOnlyDictionary<string, JsonObject> ByLink(JsonArray entries) => entries.OfType<JsonObject>().GroupBy(LinkKey)
        .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);

    public static string LinkKey(JsonObject e) => string.Join("\u0001", (string?)e["state"] ?? "", (string?)e["fsd"]?["doc"] ?? "", (string?)e["fsd"]?["requirement"] ?? "", (string?)e["page"]?["id"] ?? "",
        (string?)e["page"]?["statement"] ?? "", Quotes.Normalize((string?)e["fsd"]?["quote"] ?? ""), Quotes.Normalize((string?)e["table_fact"]?["key"] ?? (string?)e["page"]?["quote"] ?? ""));

    /// <summary>A removed section's mark (D117). It keeps what cited the section on file, out of reach of any section that gets the same number later.</summary>
    public static string RemovedSection(string permanentId, string section) => $"(removed {permanentId}) {section}";

    public static bool IsRemovedSection(string? section) => section is not null && System.Text.RegularExpressions.Regex.IsMatch(section, @"^\(removed S-\d+\) ");

    public static string QuoteHash(string quote) => CanonicalJson.Hash(Quotes.Normalize(quote));

    /// <summary>The evidence as code wrote it. Without a base to compare with, this is how an out-of-date record is told from a typed one.</summary>
    public static string EvidenceHash(JsonArray evidence) => CanonicalJson.Hash(CanonicalJson.SerializeCompact(evidence));

    /// <summary>A verdict as code wrote it (D119), so one typed by hand to clear a finding out of a queue is seen.</summary>
    public static string DisproofHash(string? verdict, JsonObject? disproof) =>
        CanonicalJson.Hash((verdict ?? "") + "\n" + (disproof is null ? "" : CanonicalJson.SerializeCompact(disproof)));

    /// <summary>A finding's standing as code wrote it (D122), so one typed by hand to take a finding out of a queue is seen.</summary>
    public static string StandingHash(string? standing, JsonObject? note) =>
        CanonicalJson.Hash((standing ?? "") + "\n" + (note is null ? "" : CanonicalJson.SerializeCompact(note)));

    /// <summary>A review as code wrote it, on the verdict it was about, so one typed by hand or left over from an older verdict closes nothing.</summary>
    public static string ReviewHash(string? review, JsonObject? note, string? disproofHash) =>
        CanonicalJson.Hash((review ?? "") + "\n" + (note is null ? "" : CanonicalJson.SerializeCompact(note)) + "\n" + (disproofHash ?? ""));
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;
using YamlDotNet.RepresentationModel;

namespace Stangent.Spec.Trace;

/// <summary>
/// <c>specs/trace.yaml</c> (D58, contract <c>trace-links</c> v1): one entry per link or finding between an FSD section
/// and a reference page, with both quotes, the state, and the decision a person makes by editing the file in a PR.
/// Written by code in one fixed form, so a re-run with the same content gives the same bytes; the dashboard (Phase 8)
/// will edit the same file.
/// </summary>
public static class TraceFile
{
    public const string Schema = "trace-links.v1.json";
    public static readonly string[] States = ["agree", "gap", "conflict", "undocumented"];
    public static readonly string[] Decisions = ["agree", "code-wrong", "fsd-wrong", "defer"];

    public static string PathIn(string root) => Path.Combine(root, "specs", "trace.yaml");

    public static JsonObject Empty() => new() { ["version"] = 1, ["entries"] = new JsonArray() };

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

    static JsonNode? ToJson(YamlNode node, string key) => node switch
    {
        YamlMappingNode m => new JsonObject(m.Children.Select(c => KeyValuePair.Create(((YamlScalarNode)c.Key).Value!, ToJson(c.Value, ((YamlScalarNode)c.Key).Value!)))),
        YamlSequenceNode s => new JsonArray([.. s.Children.Select(c => ToJson(c, ""))]),
        YamlScalarNode { Value: null or "" or "~" or "null" } sc when sc.Style == YamlDotNet.Core.ScalarStyle.Plain => null,
        YamlScalarNode sc when key == "version" && int.TryParse(sc.Value, out var v) => v,
        YamlScalarNode sc => sc.Value,
        _ => null,
    };

    /// <summary>The file, in one fixed form: entries sorted by document, section, page and id; keys in a fixed order.</summary>
    public static string Write(JsonObject trace)
    {
        var sb = new StringBuilder();
        sb.Append("# FSD ↔ reference page links and findings (trace-links v1, D58). Edit `decision` only:\n");
        sb.Append("# agree, code-wrong, fsd-wrong or defer. `decided_by` is filled from git after merge, never typed.\n");
        sb.Append("version: 1\nentries:");
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
                foreach (var k in end == "fsd" ? new[] { "doc", "section", "title", "quote", "quoteHash" } : ["id", "statement", "quote", "quoteHash"])
                    if (o[k] is not null) sb.Append($"      {k}: {S(o[k])}\n");
            }
            sb.Append($"    by: {S(e["by"])}\n");
            if (e["check"] is not null) sb.Append($"    check: {S(e["check"])}\n");
            if (e["note"] is not null) sb.Append($"    note: {S(e["note"])}\n");
            sb.Append($"    decision: {S(e["decision"])}\n");
            sb.Append($"    decided_by: {S(e["decided_by"])}\n");
        }
        return sb.ToString();
    }

    public static void Save(string root, JsonObject trace)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathIn(root))!);
        File.WriteAllText(PathIn(root), Write(trace));
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
    public static string EntryId(string state, string? doc, string? section, string? page, string? fsdQuote, string? pageQuote) =>
        "L" + CanonicalJson.Hash(string.Join("\u0001", state, doc ?? "", section ?? "", page ?? "", Quotes.Normalize(fsdQuote ?? ""), Quotes.Normalize(pageQuote ?? "")))[..10];

    public static string QuoteHash(string quote) => CanonicalJson.Hash(Quotes.Normalize(quote));
}

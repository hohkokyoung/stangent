using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Trace;

/// <summary>What the links can point at: the imported FSD sections and the recorded reference pages.</summary>
public sealed class SpecIndex
{
    public required IReadOnlyDictionary<(string Doc, string Section), JsonObject> Sections { get; init; }
    public required IReadOnlyDictionary<string, JsonObject> Pages { get; init; }

    public static SpecIndex Load(string root)
    {
        var sections = new Dictionary<(string, string), JsonObject>();
        foreach (var doc in FsdImport.Documents(root))
            foreach (var s in doc["sections"]!.AsArray().OfType<JsonObject>())
                sections[((string)doc["id"]!, (string)s["id"]!)] = s;
        var pages = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var dir = Path.Combine(root, "specs", "reference");
        if (Directory.Exists(dir))
            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                if (Path.GetFileName(file) != "failed.json" && JsonNode.Parse(File.ReadAllText(file)) is JsonObject p && p["sections"] is not null)
                    pages[(string)p["id"]!] = p;
        return new SpecIndex { Sections = sections, Pages = pages };
    }

    public JsonObject? Statement(string page, string statement) =>
        Pages.TryGetValue(page, out var p) ? PageChecks.Statements(p).FirstOrDefault(s => s.Id == statement).Statement : null;
}

/// <summary>
/// The CI checks on <c>specs/trace.yaml</c> (D58): the schema and allowed values; every id still the hash of its link;
/// both quotes still in the current FSD section and page statement (else the decision drops back to undecided);
/// every symbol a quoted statement cites in the graph; and <c>decided_by</c> never typed in a PR.
/// </summary>
public static class TraceCheck
{
    /// <param name="baseTrace">trace.yaml as it is on the PR's base: a <c>decided_by</c> that differs from it was typed.</param>
    /// <param name="fix">Drop the decision of every entry whose quotes no longer match, and write the file.</param>
    public static JsonObject Run(string root, CodeGraph? graph, JsonObject? baseTrace, bool fix)
    {
        var trace = TraceFile.Load(root);
        var problems = new List<string>();
        problems.AddRange(SchemaRegistry.Embedded.Validate(TraceFile.Schema, trace.ToJsonString()).Select(e => $"schema: {e}"));
        if (problems.Count > 0) return Report(trace, problems, [], []);

        var index = SpecIndex.Load(root);
        var nodes = graph?.AllNodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var typedBefore = (baseTrace?["entries"]?.AsArray() ?? []).OfType<JsonObject>().ToDictionary(e => (string)e["id"]!, e => (string?)e["decided_by"]);
        var stale = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in trace["entries"]!.AsArray().OfType<JsonObject>())
        {
            var id = (string)e["id"]!;
            if (!seen.Add(id)) problems.Add($"{id}: the id is used twice");
            var fsd = e["fsd"] as JsonObject;
            var page = e["page"] as JsonObject;
            var expected = TraceFile.EntryId((string)e["state"]!, (string?)fsd?["doc"], (string?)fsd?["section"], (string?)page?["id"], (string?)fsd?["quote"], (string?)page?["quote"]);
            if (id != expected) problems.Add($"{id}: the state, an end or a quote was edited by hand (only `decision` is edited; the id would be {expected})");

            var quotesMatch = true;
            if (fsd is not null)
            {
                if (!index.Sections.TryGetValue(((string)fsd["doc"]!, (string)fsd["section"]!), out var section))
                { problems.Add($"{id}: FSD section {fsd["doc"]} §{fsd["section"]} doesn't exist"); quotesMatch = false; }
                else if (fsd["quote"] is { } q && !Quotes.InSource((string)q!, [Quotes.Normalize((string)section["text"]!)]))
                    quotesMatch = false;
            }
            if (page is not null)
            {
                if (!index.Pages.ContainsKey((string)page["id"]!)) { problems.Add($"{id}: page {page["id"]} has no recorded reference page"); quotesMatch = false; }
                else if (page["statement"] is { } st)
                {
                    var statement = index.Statement((string)page["id"]!, (string)st!);
                    if (statement is null || (page["quote"] is { } pq && !Quotes.InSource((string)pq!, [Quotes.Normalize((string)statement["text"]!)])))
                        quotesMatch = false;
                    else if (nodes is not null)
                        foreach (var cite in statement["cites"]!.AsArray().Select(c => (string)c!).Where(c => !nodes.Contains(c)))
                            problems.Add($"{id}: statement {page["id"]} {st} cites `{cite}`, which is no longer in the code graph");
                }
            }
            if (!quotesMatch)
            {
                stale.Add(id);
                if (fix) { e["decision"] = null; e["decided_by"] = null; }
            }

            if (baseTrace is not null && (string?)e["decided_by"] is { } by && (!typedBefore.TryGetValue(id, out var before) || before != by))
                problems.Add($"{id}: decided_by was set in this change ({by}); it's filled from git after merge, never typed");
        }
        var undecided = trace["entries"]!.AsArray().OfType<JsonObject>().Where(e => stale.Contains((string)e["id"]!) && e["decision"] is not null).Select(e => (string)e["id"]!).ToList();
        if (fix && stale.Count > 0) TraceFile.Save(root, trace);
        foreach (var id in undecided)
            problems.Add($"{id}: a quote no longer matches the FSD or the page, so its decision must drop back to undecided (run `stangent trace check --fix`)");
        return Report(trace, problems, stale, undecided);
    }

    static JsonObject Report(JsonObject trace, List<string> problems, List<string> stale, List<string> undecided)
    {
        var entries = trace["entries"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
        return new JsonObject
        {
            ["ok"] = problems.Count == 0,
            ["counts"] = new JsonObject
            {
                ["entries"] = entries.Count,
                ["byState"] = new JsonObject(TraceFile.States.Select(s => KeyValuePair.Create(s, (JsonNode?)entries.Count(e => (string?)e["state"] == s)))),
                ["undecided"] = entries.Count(e => e["decision"] is null),
                ["staleQuotes"] = stale.Count,
            },
            ["problems"] = new JsonArray([.. problems.Select(p => (JsonNode)p)]),
            ["staleQuotes"] = new JsonArray([.. stale.Select(s => (JsonNode)s)]),
        };
    }

    /// <summary>Bulk approval (D53): every confirmed "agree" link under a chapter gets <c>decision: agree</c>, in one edit.</summary>
    public static int ApproveChapter(string root, string doc, string chapter)
    {
        var trace = TraceFile.Load(root);
        var n = 0;
        foreach (var e in trace["entries"]!.AsArray().OfType<JsonObject>())
        {
            var section = (string?)e["fsd"]?["section"];
            if ((string?)e["state"] != "agree" || (string?)e["check"] != "confirmed" || e["decision"] is not null) continue;
            if ((string?)e["fsd"]?["doc"] != doc || section is null || !(section == chapter || section.StartsWith(chapter + ".", StringComparison.Ordinal) || section.StartsWith(chapter + " / ", StringComparison.Ordinal))) continue;
            e["decision"] = "agree";
            n++;
        }
        if (n > 0) TraceFile.Save(root, trace);
        return n;
    }

    /// <summary>After a PR merges, CI names who decided (D58): every entry whose decision the PR changed gets the
    /// approver or merger the code-host adapter reports. Never typed by a person.</summary>
    public static int Attribute(string root, JsonObject baseTrace, string by)
    {
        var trace = TraceFile.Load(root);
        var before = (baseTrace["entries"]?.AsArray() ?? []).OfType<JsonObject>().ToDictionary(e => (string)e["id"]!, e => (string?)e["decision"]);
        var n = 0;
        foreach (var e in trace["entries"]!.AsArray().OfType<JsonObject>())
        {
            var decision = (string?)e["decision"];
            if (decision is null || (before.TryGetValue((string)e["id"]!, out var old) && old == decision)) continue;
            e["decided_by"] = by;
            n++;
        }
        if (n > 0) TraceFile.Save(root, trace);
        return n;
    }
}

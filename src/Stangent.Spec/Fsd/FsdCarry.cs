using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>
/// On promotion, saved work follows its section by code (D117). Links and items that cite a section that kept its permanent id get its
/// new number and title, with no stored file changing shape. A run's input hash is worked out again only when it was valid and the words the
/// AI read are the same apart from the number, so a renumbering costs no paid run. What cites a removed section takes its mark and is listed, never dropped.
/// </summary>
static class FsdCarry
{
    public sealed record Pair(JsonObject Old, JsonObject New, string Kind)
    {
        public string From => (string)Old["id"]!;
        public string To => (string)New["id"]!;
        public string Title => (string)New["title"]!;
        public bool SameWords => (string)Old["text"]! == (string)New["text"]! && (string)Old["title"]! == Title;
    }

    public static JsonObject Apply(string root, string doc, IReadOnlyList<Pair> pairs, IReadOnlyList<JsonObject> gone, IReadOnlySet<string> numbersNow, bool findings)
    {
        using var links = LinkRun.Lock(root);
        using var items = RequirementFile.Lock(root);
        var map = pairs.ToDictionary(p => p.From, StringComparer.Ordinal);
        var goneBySection = gone.ToDictionary(s => (string)s["id"]!, s => s, StringComparer.Ordinal);
        var rerun = new JsonArray();

        // Before anything moves: these read the old section, the old features and the runs as they stand.
        var linkRuns = CarryRuns(LinkRun.LoadRuns(root), doc, pairs, rerun, (p, run) =>
        {
            var pages = (run["candidates"]?.AsArray() ?? []).Select(c => (string)c!["page"]!).ToList();
            var index = SpecIndex.Load(root);
            var requirements = LinkRun.Requirements(root, doc, p.Old);
            return (LinkRun.JudgeInputs(p.Old, doc, pages, index, requirements, findings), LinkRun.JudgeInputs(p.New, doc, pages, index, requirements, findings));
        });
        var reqRuns = CarryRuns(RequirementRun.LoadRuns(root), doc, pairs, rerun, (p, _) => (RequirementRun.InputsOf(p.Old, doc), RequirementRun.InputsOf(p.New, doc)));

        var trace = TraceFile.Load(root);
        var traceIds = new JsonArray();
        var onRemoved = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        foreach (var e in trace["entries"]!.AsArray().OfType<JsonObject>())
        {
            if (e["fsd"] is not JsonObject fsd || (string?)fsd["doc"] != doc) continue;
            var section = (string)fsd["section"]!;
            string to;
            if (goneBySection.TryGetValue(section, out var g)) to = Mark(g);
            else if (map.TryGetValue(section, out var p))
            {
                fsd["title"] = p.Title;
                if (p.From == p.To) continue;
                to = p.To;
            }
            else continue;
            fsd["section"] = to;
            var from = (string)e["id"]!;
            var id = TraceFile.EntryId((string)e["state"]!, doc, to, (string?)e["page"]?["id"], (string?)fsd["quote"], (string?)e["page"]?["quote"], (string?)fsd["requirement"]);
            e["id"] = id;
            traceIds.Add((JsonNode)new JsonObject { ["from"] = from, ["to"] = id });
            if (goneBySection.ContainsKey(section)) (onRemoved.TryGetValue(section, out var list) ? list : onRemoved[section] = []).Add(e);
        }

        var removed = new JsonArray();
        var features = RequirementFile.Features(root, doc);
        var questions = RequirementFile.Questions(root, doc);
        var before = features.ToDictionary(f => (string)f["id"]!, f => f.ToJsonString(), StringComparer.Ordinal);
        foreach (var f in features) Follow(f, map, goneBySection, title: true);
        foreach (var q in questions) Follow(q, map, goneBySection, title: false);
        foreach (var s in gone)
        {
            var mark = Mark(s);
            var on = features.Where(f => (string)f["section"]! == mark).ToList();
            var entries = onRemoved.GetValueOrDefault((string)s["id"]!) ?? [];
            removed.Add((JsonNode)new JsonObject
            {
                ["permanentId"] = (string)s["permanentId"]!, ["section"] = (string)s["id"]!, ["title"] = (string)s["title"]!,
                ["numberUsedAgain"] = numbersNow.Contains((string)s["id"]!),
                ["traceEntries"] = new JsonArray([.. entries.Select(e => (JsonNode)(string)e["id"]!)]),
                // No move: these stay as a person decided them, until a person deals with the entry.
                ["decisions"] = new JsonArray([.. entries.Where(e => e["decision"] is not null).Select(e => (JsonNode)new JsonObject
                {
                    ["id"] = (string)e["id"]!, ["value"] = (string?)e["decision"], ["by"] = (string?)e["decided_by"],
                })]),
                ["features"] = new JsonArray([.. on.Select(f => (JsonNode)(string)f["id"]!)]),
                ["requirements"] = new JsonArray([.. on.SelectMany(f => f["requirements"]!.AsArray().OfType<JsonObject>()).Select(r => (JsonNode)(string)r["id"]!)]),
                ["questions"] = new JsonArray([.. questions.Where(q => (string)q["section"]! == mark).Select(q => (JsonNode)(string)q["id"]!)]),
            });
        }
        Retree(features, pairs);

        if (trace["entries"]!.AsArray().Count > 0) TraceFile.Save(root, trace);
        foreach (var f in features.Where(f => f.ToJsonString() != before[(string)f["id"]!])) RequirementFile.Save(root, f);
        if (questions.Count > 0) RequirementFile.SaveQuestions(root, doc, questions);
        if (features.Count > 0 || questions.Count > 0) RequirementFile.WriteViews(root, doc);
        if (linkRuns.Count > 0) Quotes.WriteAtomic(LinkRun.RunsFile(root), CanonicalJson.Serialize(linkRuns));
        if (reqRuns.Count > 0) Quotes.WriteAtomic(RequirementFile.RunsFile(root), CanonicalJson.Serialize(reqRuns));

        return new JsonObject
        {
            ["traceIds"] = traceIds,
            ["sectionsFollowed"] = pairs.Count(p => p.From != p.To),
            ["removed"] = removed,
            // These run again at the next prepare: the words the AI read changed, so their saved answers don't count for them.
            ["runAgain"] = new JsonArray([.. rerun.Select(r => r!.DeepClone()).DistinctBy(r => (string)r!)]),
        };
    }

    static string Mark(JsonObject section) => TraceFile.RemovedSection((string)section["permanentId"]!, (string)section["id"]!);

    /// <summary>Every section reference in a feature or question: its own, and each quote's. One pass, so 3 → 4 and 4 → 5 can't run together.</summary>
    static void Follow(JsonObject item, Dictionary<string, Pair> map, Dictionary<string, JsonObject> gone, bool title)
    {
        var section = (string)item["section"]!;
        if (map.TryGetValue(section, out var p))
        {
            item["section"] = p.To;
            if (title) item["title"] = p.Title;
            FollowQuotes(item, map);
        }
        else if (gone.TryGetValue(section, out var g)) item["section"] = Mark(g);
    }

    static void FollowQuotes(JsonNode? node, Dictionary<string, Pair> map)
    {
        switch (node)
        {
            case JsonObject o:
                if (o["section"] is JsonValue && o["hash"] is not null && map.TryGetValue((string)o["section"]!, out var p)) o["section"] = p.To;
                foreach (var (_, value) in o.ToList()) FollowQuotes(value, map);
                break;
            case JsonArray a:
                foreach (var value in a) FollowQuotes(value, map);
                break;
        }
    }

    /// <summary>The feature tree follows the section tree (D99): a moved section's feature goes under its new parent's.</summary>
    static void Retree(List<JsonObject> features, IReadOnlyList<Pair> pairs)
    {
        var bySection = features.ToDictionary(f => (string)f["section"]!, StringComparer.Ordinal);
        var byId = features.ToDictionary(f => (string)f["id"]!, StringComparer.Ordinal);
        foreach (var p in pairs)
        {
            if (!bySection.TryGetValue(p.To, out var f)) continue;
            var parent = (string?)p.New["parent"] is { } up ? bySection.GetValueOrDefault(up) : null;
            var was = (string?)f["parent"];
            if (was == (string?)parent?["id"]) continue;
            if (was is not null && byId.TryGetValue(was, out var old))
                old["children"] = new JsonArray([.. old["children"]!.AsArray().Where(c => (string)c! != (string)f["id"]!).Select(c => (JsonNode)(string)c!)]);
            f["parent"] = (string?)parent?["id"];
            if (parent is not null && parent["children"]!.AsArray().All(c => (string)c! != (string)f["id"]!)) parent["children"]!.AsArray().Add((string)f["id"]!);
        }
    }

    /// <summary>The runs under their new keys. A run's hash moves with it only when it was valid for the old section and the words are the same.</summary>
    static JsonObject CarryRuns(JsonObject runs, string doc, IReadOnlyList<Pair> pairs, JsonArray rerun, Func<Pair, JsonObject, (string? Old, string? New)> hashes)
    {
        var moved = new List<(string Key, JsonNode Run)>();
        foreach (var p in pairs.Where(p => p.From != p.To || !p.SameWords))
        {
            var key = LinkRun.Key(doc, p.From);
            if (runs[key] is not JsonObject run) continue;
            var copy = (JsonObject)run.DeepClone();
            if (p.SameWords && (string?)copy["inputs"] is { } inputs)
            {
                var (before, after) = hashes(p, copy);
                if (before == inputs && after is not null) copy["inputs"] = after;
            }
            if (!p.SameWords) rerun.Add((JsonNode)p.To);
            moved.Add((LinkRun.Key(doc, p.To), copy));
        }
        foreach (var p in pairs.Where(p => p.From != p.To || !p.SameWords)) runs.Remove(LinkRun.Key(doc, p.From));
        foreach (var (key, run) in moved) runs[key] = run;
        return runs;
    }
}

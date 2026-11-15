using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>
/// On promotion, the trace entries follow their section by code (D117): an entry citing a section that kept its permanent id gets its
/// new number and title, with no stored file changing shape. What cites a removed section takes its mark and is listed, never dropped.
/// Since the link route was cut (D342), nothing else carries: no run is saved to re-key.
/// </summary>
static class FsdCarry
{
    public sealed record Pair(JsonObject Old, JsonObject New, string Kind)
    {
        public string From => (string)Old["id"]!;
        public string To => (string)New["id"]!;
        public string Title => (string)New["title"]!;
        public bool SameWords => (string)Old["text"]! == (string)New["text"]! && (string)Old["title"]! == Title && JsonNode.DeepEquals(Old[AttachmentChecks.Key], New[AttachmentChecks.Key]);
    }

    public static JsonObject Apply(string root, string doc, IReadOnlyList<Pair> pairs, IReadOnlyList<JsonObject> gone, IReadOnlySet<string> numbersNow)
    {
        using var links = TraceStore.Lock(root);
        var map = pairs.ToDictionary(p => p.From, StringComparer.Ordinal);
        var goneBySection = gone.ToDictionary(s => (string)s["id"]!, s => s, StringComparer.Ordinal);

        var trace = TraceFile.Load(root);
        var traceIds = new JsonArray();
        var onRemoved = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        var disagree = new JsonArray();
        foreach (var e in trace["entries"]!.AsArray().OfType<JsonObject>())
        {
            if (e["fsd"] is not JsonObject fsd || (string?)fsd["doc"] != doc) continue;
            var section = (string)fsd["section"]!;
            string to;
            // Two ids for one section: the entry's number and its permanent id must name the same one before either moves (D121).
            var was = (string?)fsd["sectionId"];
            var named = goneBySection.TryGetValue(section, out var g) ? g : map.TryGetValue(section, out var pair) ? pair.Old : null;
            if (was is not null && named is not null && was != (string)named["permanentId"]!)
                disagree.Add((JsonNode)new JsonObject { ["entry"] = (string)e["id"]!, ["section"] = section, ["sectionId"] = was, ["numberNames"] = (string)named["permanentId"]! });
            if (named is not null) fsd["sectionId"] = (string)named["permanentId"]!;
            if (g is not null) to = Mark(g);
            else if (map.TryGetValue(section, out var p))
            {
                fsd["title"] = p.Title;
                if (p.From == p.To) continue;
                to = p.To;
            }
            else continue;
            fsd["section"] = to;
            var from = (string)e["id"]!;
            var id = TraceFile.IdOf(e, to);
            e["id"] = id;
            traceIds.Add((JsonNode)new JsonObject { ["from"] = from, ["to"] = id });
            if (goneBySection.ContainsKey(section)) (onRemoved.TryGetValue(section, out var list) ? list : onRemoved[section] = []).Add(e);
        }

        var removed = new JsonArray();
        foreach (var s in gone)
        {
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
            });
        }

        StateChanges.RecordIdMoves(root, StateKinds.TraceDecision, [.. traceIds.Select(m => ((string)m!["from"]!, (string)m["to"]!)).Where(m => m.Item1 != m.Item2)], StateActor.Code("stangent fsd promote"), "its section moved");
        if (trace["entries"]!.AsArray().Count > 0) TraceFile.Save(root, trace);

        return new JsonObject
        {
            ["traceIds"] = traceIds,
            ["sectionsFollowed"] = pairs.Count(p => p.From != p.To),
            ["removed"] = removed,
            ["sectionIdsDisagree"] = disagree,
        };
    }

    static string Mark(JsonObject section) => TraceFile.RemovedSection((string)section["permanentId"]!, (string)section["id"]!);
}

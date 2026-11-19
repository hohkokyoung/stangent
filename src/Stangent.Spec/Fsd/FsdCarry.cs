using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>
/// When a section's number or heading path moves (<c>fsd take</c>, D385), the trace entries follow it by code (D117): an entry citing a section that kept its permanent id gets its
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

    /// <param name="gone">Sections removed (each with <c>id</c>, <c>permanentId</c> and <c>title</c> as HEAD had them): what cites one, by its
    /// permanent id where the entry has one and else by its number, moves to <c>(removed S-0004) 4</c>; an entry whose number is the
    /// removed section's while its permanent id names another is left where it is and listed in <c>sectionIdsDisagree</c>.</param>
    /// <param name="back">Sections a mark was put on and that are on a heading again (the removal undone before it was committed): their
    /// entries go back to the section's number as it is now. A committed removal can't come back (its id is refused, rule 11).</param>
    public static JsonObject Apply(string root, string doc, IReadOnlyList<Pair> pairs, IReadOnlyList<JsonObject> gone, IReadOnlySet<string> numbersNow,
        string actor = "stangent fsd take", IReadOnlyList<JsonObject>? back = null)
    {
        using var links = TraceStore.Lock(root);
        var map = pairs.ToDictionary(p => p.From, StringComparer.Ordinal);
        var goneBySection = gone.ToDictionary(s => (string)s["id"]!, s => s, StringComparer.Ordinal);
        var goneById = gone.ToDictionary(s => (string)s["permanentId"]!, s => s, StringComparer.Ordinal);
        var backById = (back ?? []).ToDictionary(s => (string)s["permanentId"]!, s => s, StringComparer.Ordinal);

        var trace = TraceFile.Load(root);
        var traceIds = new JsonArray();
        var onRemoved = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        var disagree = new JsonArray();
        var restored = new JsonArray();
        var changed = false;
        foreach (var e in trace["entries"]!.AsArray().OfType<JsonObject>())
        {
            if (e["fsd"] is not JsonObject fsd || (string?)fsd["doc"] != doc) continue;
            var section = (string)fsd["section"]!;
            var was = (string?)fsd["sectionId"];
            string to;
            // A removed section: by the entry's permanent id first, as its number may name another section by now.
            var g = was is not null ? goneById.GetValueOrDefault(was) : null;
            if (g is null && !TraceFile.IsRemovedSection(section) && goneBySection.TryGetValue(section, out var byNumber))
            {
                if (was is not null && was != (string)byNumber["permanentId"]!)
                {
                    // Two ids for one section (D121): the number names the removed one, the permanent id another. Never guessed.
                    disagree.Add((JsonNode)new JsonObject { ["entry"] = (string)e["id"]!, ["section"] = section, ["sectionId"] = was, ["numberNames"] = (string)byNumber["permanentId"]! });
                    continue;
                }
                g = byNumber;
            }
            if (g is not null)
            {
                var pid = (string)g["permanentId"]!;
                (onRemoved.TryGetValue(pid, out var list) ? list : onRemoved[pid] = []).Add(e);
                if (section == Mark(g)) continue; // marked already, by an earlier run of the same check
                // Marked by its permanent id while its number named another section: said, not silent (fresh check, D396).
                if (section != (string)g["id"]!)
                    disagree.Add((JsonNode)new JsonObject { ["entry"] = (string)e["id"]!, ["section"] = section, ["sectionId"] = pid, ["markedBy"] = "sectionId", ["removedNumber"] = (string)g["id"]! });
                fsd["sectionId"] = pid;
                to = Mark(g);
            }
            else if (TraceFile.IsRemovedSection(section) && was is not null && backById.TryGetValue(was, out var returned) && section.StartsWith($"(removed {was}) ", StringComparison.Ordinal))
            {
                to = (string)returned["id"]!;
                restored.Add((JsonNode)new JsonObject { ["entry"] = (string)e["id"]!, ["section"] = was, ["to"] = to });
            }
            else
            {
                var named = map.TryGetValue(section, out var pair) ? pair.Old : null;
                if (was is not null && named is not null && was != (string)named["permanentId"]!)
                    disagree.Add((JsonNode)new JsonObject { ["entry"] = (string)e["id"]!, ["section"] = section, ["sectionId"] = was, ["numberNames"] = (string)named["permanentId"]! });
                if (named is null) continue;
                fsd["sectionId"] = (string)named["permanentId"]!;
                fsd["title"] = pair!.Title;
                changed = true;
                if (pair.From == pair.To) continue;
                to = pair.To;
            }
            fsd["section"] = to;
            var from = (string)e["id"]!;
            var id = TraceFile.IdOf(e, to);
            e["id"] = id;
            changed = true;
            traceIds.Add((JsonNode)new JsonObject { ["from"] = from, ["to"] = id });
        }

        var removed = new JsonArray();
        foreach (var s in gone)
        {
            var entries = onRemoved.GetValueOrDefault((string)s["permanentId"]!) ?? [];
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

        StateChanges.RecordIdMoves(root, StateKinds.TraceDecision, [.. traceIds.Select(m => ((string)m!["from"]!, (string)m["to"]!)).Where(m => m.Item1 != m.Item2)], StateActor.Code(actor), gone.Count > 0 || restored.Count > 0 ? "its section was removed, or came back" : "its section moved");
        if (changed) TraceFile.Save(root, trace);

        var result = new JsonObject
        {
            ["traceIds"] = traceIds,
            ["sectionsFollowed"] = pairs.Count(p => p.From != p.To),
            ["removed"] = removed,
            ["sectionIdsDisagree"] = disagree,
        };
        if (restored.Count > 0) result["restored"] = restored;
        return result;
    }

    static string Mark(JsonObject section) => TraceFile.RemovedSection((string)section["permanentId"]!, (string)section["id"]!);
}

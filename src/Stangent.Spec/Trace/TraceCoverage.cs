using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Trace;

/// <summary>
/// Whether every section was dealt with, by code (C.1, D122): each is linked to a page, marked "not in code" by a person, has no text to check, or is listed as uncovered.
/// It reports and never fails a check. A mark holds for the words it was made on, so a section whose text changed is uncovered again, saying so.
/// </summary>
public static class TraceCoverage
{
    static string MarksFile(string root, string doc) => Path.Combine(FsdImport.DocDir(root, doc), "not-in-code.json");

    static JsonObject Marks(string root, string doc) =>
        File.Exists(MarksFile(root, doc)) ? JsonNode.Parse(File.ReadAllText(MarksFile(root, doc)))!.AsObject() : new JsonObject { ["version"] = 1, ["sections"] = new JsonObject() };

    /// <summary>A person says a section isn't about the code, or takes that back. By permanent id, so it follows the section across versions.</summary>
    /// <param name="who">From git, never typed.</param>
    public static JsonObject Mark(string root, string doc, string section, string? why, string who, bool clear)
    {
        var document = FsdOutline.Load(root, doc);
        var s = FsdOutline.Find(document, section);
        var id = (string)s["permanentId"]!;
        var marks = Marks(root, doc);
        var held = marks["sections"]!.AsObject();
        var mark = held[id] as JsonObject ?? new JsonObject();
        var key = $"{doc}#{id}";
        if (clear)
        {
            StateChanges.Apply(root, mark, StateKinds.SectionCoverage, key, "unmarked", StateActor.Person(who), why ?? "the mark was taken off");
            held.Remove(id);
        }
        else
        {
            StateChanges.Apply(root, mark, StateKinds.SectionCoverage, key, "not-in-code", StateActor.Person(who), why ?? "not about the code");
            mark["by"] = who;
            mark["why"] = why;
            mark["on"] = (string)s["textHash"]!;
            held[id] = mark;
        }
        Quotes.WriteAtomic(MarksFile(root, doc), CanonicalJson.Serialize(marks));
        return new JsonObject { ["doc"] = doc, ["section"] = (string)s["id"]!, ["id"] = id, ["state"] = clear ? "unmarked" : "not-in-code", ["by"] = who, ["why"] = why, ["attachments"] = Attachments(document, s) };
    }

    /// <summary>What a section holds besides its words: its pictures, by id, and the files embedded in it, which are only named until they're read (A.7).</summary>
    static JsonArray Attachments(JsonObject document, JsonObject section)
    {
        var id = (string?)section["permanentId"];
        var number = (string)section["id"]!;
        return new JsonArray([
            .. document["images"]!.AsArray().OfType<JsonObject>().Where(i => id is not null && (string?)i["section"] == id).Select(i => (JsonNode)new JsonObject { ["id"] = (string?)i["id"], ["file"] = (string)i["file"]! }),
            .. document["problems"]!.AsArray().OfType<JsonObject>().Where(p => (string?)p["sectionNumber"] == number && ((string)p["detail"]!).Contains("embedded", StringComparison.Ordinal))
                .Select(p => (JsonNode)new JsonObject { ["detail"] = (string)p["detail"]! }),
        ]);
    }

    public static JsonObject Of(string root, JsonObject document, JsonObject trace)
    {
        var doc = (string)document["id"]!;
        var entries = trace["entries"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["fsd"]?["doc"] == doc && !TraceFindings.IsGone(e)).ToList();
        var marks = Marks(root, doc)["sections"]!.AsObject();
        JsonArray linked = [], marked = [], uncovered = [], empty = [];
        foreach (var s in document["sections"]!.AsArray().OfType<JsonObject>())
        {
            var (id, number) = ((string?)s["permanentId"], (string)s["id"]!);
            var links = entries.Count(e => (string?)e["fsd"]!["section"] == number && e["page"] is not null);
            var mark = id is null ? null : marks[id] as JsonObject;
            if (mark is not null && (string?)mark["on"] == (string)s["textHash"]!)
                marked.Add((JsonNode)new JsonObject { ["section"] = number, ["id"] = id, ["by"] = (string?)mark["by"], ["why"] = (string?)mark["why"], ["alsoLinked"] = links, ["attachments"] = Attachments(document, s) });
            else if (links > 0) linked.Add((JsonNode)new JsonObject { ["section"] = number, ["id"] = id, ["links"] = links });
            else if (((string)s["text"]!).Trim().Length == 0) empty.Add((JsonNode)new JsonObject { ["section"] = number, ["id"] = id });
            else
            {
                var item = new JsonObject { ["section"] = number, ["id"] = id, ["title"] = (string)s["title"]! };
                if (mark is not null) { item["markWas"] = (string?)mark["by"]; item["markNote"] = "its text changed after it was marked not in code"; }
                uncovered.Add((JsonNode)item);
            }
        }
        return new JsonObject { ["linked"] = linked, ["notInCode"] = marked, ["empty"] = empty, ["uncovered"] = uncovered };
    }

    public static JsonObject All(string root, JsonObject trace) =>
        new(FsdImport.Documents(root).Select(d => KeyValuePair.Create((string)d["id"]!, (JsonNode?)Of(root, d, trace))));
}

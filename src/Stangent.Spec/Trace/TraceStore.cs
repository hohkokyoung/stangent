using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Trace;

/// <summary>
/// Writing <c>specs/trace.yaml</c>'s entries under its lock: what table facts (D124) and the frozen links still need now the link route is cut (D342).
/// <c>specs/trace-runs.json</c> holds the old link runs' records, read only for their cost.
/// </summary>
public static class TraceStore
{
    public static string Key(string doc, string section) => $"{doc}#{section}";
    public static string RunsFile(string root) => Path.Combine(root, "specs", "trace-runs.json");

    public static JsonObject Entry(string state, string doc, JsonObject? section, string? fsdQuote, string? page, string? statement, string? pageQuote,
        string by, string? check, string? note, string? requirement = null, string? claim = null)
    {
        JsonNode? fsd = section is null ? null : new JsonObject { ["doc"] = doc, ["section"] = (string)section["id"]!, ["title"] = (string)section["title"]! };
        if (fsd is JsonObject known && (string?)section!["permanentId"] is { } sectionId) known["sectionId"] = sectionId;
        if (fsd is JsonObject r && requirement is not null) r["requirement"] = requirement;
        if (fsd is JsonObject onClaim && claim is not null) onClaim["claim"] = claim;
        if (fsd is JsonObject f && fsdQuote is not null)
        {
            f["quote"] = fsdQuote;
            f["quoteHash"] = TraceFile.QuoteHash(fsdQuote);
            if (AttachmentChecks.SourceOf(section!, fsdQuote) is { } source)
                foreach (var (k, v) in source) f[k] = v!.DeepClone();
        }
        JsonNode? pg = page is null ? null : new JsonObject { ["id"] = page };
        if (pg is JsonObject p && statement is not null) p["statement"] = statement;
        if (pg is JsonObject p2 && pageQuote is not null) { p2["quote"] = pageQuote; p2["quoteHash"] = TraceFile.QuoteHash(pageQuote); }
        var e = new JsonObject
        {
            ["id"] = claim is not null
                ? TraceFile.ClaimEntryId(state, doc, (string?)section?["permanentId"], page, claim, statement)
                : TraceFile.EntryId(state, section is null ? null : doc, (string?)section?["id"], page, fsdQuote, pageQuote, requirement),
            ["state"] = state, ["fsd"] = fsd, ["page"] = pg, ["by"] = by,
        };
        if (check is not null) e["check"] = check;
        if (note is not null) e["note"] = note;
        e["decision"] = null;
        e["decided_by"] = null;
        return e;
    }

    static readonly StateActor Who = StateActor.Code("stangent fsd");

    /// <summary>Replaces a section's entries; an entry that comes back with the same id keeps its decision.</summary>
    public static void ApplyEntries(string root, string doc, string? sectionId, IEnumerable<JsonObject> entries, Func<JsonObject, bool>? replaces = null)
    {
        using var _ = Lock(root);
        var trace = TraceFile.Load(root);
        var old = trace["entries"]!.AsArray().OfType<JsonObject>().ToList();
        // A table fact comes from `fsd compare-tables`, so a judge's run neither removes nor marks it (C.2).
        replaces ??= e => (string?)e["fsd"]?["doc"] == doc && (string?)e["fsd"]?["section"] == sectionId && (string?)e["state"] != TraceFile.TableFact;
        var keepDecisions = old.Where(replaces).ToDictionary(e => (string)e["id"]!, e => e);
        var list = old.Where(e => !replaces(e)).Select(e => (JsonNode)e.DeepClone()).ToList();
        foreach (var e in entries.DistinctBy(e => (string)e["id"]!))
        {
            var copy = (JsonObject)e.DeepClone();
            if (keepDecisions.TryGetValue((string)e["id"]!, out var was))
            {
                KeepRuling(copy, was);
                if (was["carried_from"] is not null) copy["carried_from"] = was["carried_from"]!.DeepClone();
                // The same link is the same finding: its id, its grade and its evidence stay with it (D118).
                foreach (var carried in TraceFile.FindingFields.Where(f => was[f] is not null)) copy[carried] = was[carried]!.DeepClone();
                if (TraceFindings.IsGone(copy)) TraceFindings.CameBack(root, copy, Who);
            }
            list.Add(copy);
        }
        // A decided link that didn't come back took its decision with it: its id is the hash of its quotes (D58). A decided finding is
        // kept as gone, holding its X- id for if it comes back (D122). One already gone stays as it is, and one nobody decided goes, as it always has, unless it's flagged: a flag never closes by itself (D148).
        var back = entries.Select(e => (string)e["id"]!).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, left) in keepDecisions.Where(k => !back.Contains(k.Key)))
        {
            var kept = (JsonObject)left.DeepClone();
            if ((TraceFindings.IsFinding(left) || TraceFindings.IsGone(left)) && (left["decision"] is not null || TraceFindings.Standing(left) is not null || TraceFindings.Flagged(left)))
            {
                if (!TraceFindings.IsGone(kept)) TraceFindings.MarkGone(root, kept, Who, "a new build didn't find this link again");
                list.Add(kept);
            }
            else if (left["decision"] is not null)
                StateChanges.Move(root, StateKinds.TraceDecision, id, (string?)left["decision"], "undecided", Who, "a new build didn't find this link again");
        }
        trace["entries"] = new JsonArray([.. list]);
        TraceFile.Save(root, trace);
    }

    /// <summary>The same link keeps a person's decision. What it was decided on stays too, or a re-judged link would tie an old decision to new words.</summary>
    static void KeepRuling(JsonObject to, JsonObject from)
    {
        to["decision"] = from["decision"]?.DeepClone();
        to["decided_by"] = from["decided_by"]?.DeepClone();
        if (from["decided_on"] is not null) to["decided_on"] = from["decided_on"]!.DeepClone();
    }

    public static JsonObject LoadRuns(string root) => PageRun.LoadJson(RunsFile(root)) as JsonObject ?? new JsonObject();

    public static FileStream Lock(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".stangent", "fsd"));
        var path = Path.Combine(root, ".stangent", "fsd", "trace.lock");
        for (var i = 0; ; i++)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 600) { Thread.Sleep(50); }
        }
    }
}

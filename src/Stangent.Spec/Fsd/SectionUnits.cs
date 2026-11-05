using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>
/// The link judge's unit as a run of small neighbouring sections under one parent, so a rule split across two is read as one (C.6, D124). Off by default. A unit has its own input hash
/// and the sections keep their own: turning it on runs the units, turning it off puts each section's saved links back with no call, and turning it on again does the same for the units.
/// Each link still cites its own section. <c>trace-runs.json</c> records the unit on each section, with what each side last saved.
/// </summary>
public static class SectionUnits
{
    /// <summary>The runs of a document's sections, in order. Only a section with text and no requirements in use can join, since those are judged a requirement at a time;
    /// one over the limit stays alone. Greedy from the top, so it's the same every time for the same document.</summary>
    public static List<List<JsonObject>> Of(StangentRepo repo, string doc, IEnumerable<JsonObject> sections)
    {
        var limit = repo.Config.Fsd.GroupMaxChars;
        var units = new List<List<JsonObject>>();
        var run = new List<JsonObject>();
        var chars = 0;
        void Close()
        {
            if (run.Count > 0) units.Add(run);
            (run, chars) = ([], 0);
        }
        foreach (var s in sections)
        {
            var text = ((string)s["text"]!).Trim().Length;
            if (text == 0 || text > limit || LinkRun.RequirementsInUse(repo, doc, s) is not null) { Close(); continue; }
            if (run.Count > 0 && ((string?)run[0]["parent"] != (string?)s["parent"] || chars + text > limit)) Close();
            run.Add(s);
            chars += text;
        }
        Close();
        return units;
    }

    /// <summary>The step for a section in a unit of two or more. Null: judge it alone, because it's in no such unit, or code finds no page for the whole unit (so it takes the finder's road, alone).</summary>
    internal static RunStep? Prepare(PageSet set, StangentRepo repo, SpecIndex index, string doc, string sectionId)
    {
        var ordered = FsdOutline.Load(repo.Root, doc)["sections"]!.AsArray().OfType<JsonObject>().Select(s => index.Sections[(doc, (string)s["id"]!)]);
        if (Of(repo, doc, ordered).FirstOrDefault(u => u.Any(s => (string)s["id"]! == sectionId)) is not { Count: > 1 } unit) return null;
        var found = unit.SelectMany(s => Candidates.ByCode(s, set, index.Pages, (string?)s["parent"] is { } parent ? index.Sections.GetValueOrDefault((doc, parent)) : null)).DistinctBy(c => c.Page).ToList();
        if (found.Count == 0) return null;
        var leader = (string)unit[0]["id"]!;
        if (sectionId != leader) return new RunStep("grouped", "none", Warning: $"judged with the sections from {leader}: `fsd prepare {doc} {leader}`");
        var candidates = found.Where(c => index.Pages.ContainsKey(c.Page)).ToList();
        if (candidates.Count == 0) return new RunStep("failed", "none", Errors: [$"no candidate page has been recorded yet: {string.Join(", ", found.Select(c => c.Page))}"]);

        var finer = repo.Config.Fsd.JudgeFinerVerdicts;
        var bundle = Bundle(unit, doc, candidates.Select(c => c.Page), index, finer);
        var inputs = LinkRun.GroupInputs(bundle, repo.Config.Fsd.Findings);
        var ids = unit.Select(s => (string)s["id"]!).ToList();
        var runs = LinkRun.LoadRuns(repo.Root);
        JsonObject? Run(string id) => runs[LinkRun.Key(doc, id)] as JsonObject;
        if (ids.All(id => (string?)Run(id)?["unit"]?["inputs"] == inputs)) return new RunStep("current", "none");
        // Turned off and on again: what the unit last gave goes back, for the same inputs, with no call.
        if (ids.All(id => (string?)Run(id)?["saved"]?["group"]?["inputs"] == inputs))
        {
            foreach (var s in unit)
            {
                var id = (string)s["id"]!;
                var usage = Run(id)!["saved"]!["group"]!["usage"];
                PutOnFile(repo.Root, doc, s, inputs, leader, ids, (Run(id)!["saved"]!["group"]!["entries"]!.AsArray()).OfType<JsonObject>().Select(e => Fit(e, s)).ToList(), usage as JsonObject);
            }
            return new RunStep("current", "none");
        }

        AiPolicy.EnsureAllowed(repo.Config, "fsd prepare");
        var work = LinkRun.Fresh(repo.Root, doc, leader);
        File.WriteAllText(Path.Combine(work, "judge.md"), SharedHead.Store(bundle));
        var state = new JsonObject
        {
            ["doc"] = doc, ["section"] = leader, ["inputs"] = inputs, ["retryUsed"] = false, ["level"] = "group", ["sections"] = new JsonArray([.. ids.Select(i => (JsonNode)i)]),
            ["candidates"] = new JsonArray([.. candidates.Select(c => (JsonNode)new JsonObject { ["page"] = c.Page, ["why"] = c.Why })]),
            ["candidatesBy"] = "code", ["usage"] = new JsonObject(), ["schema"] = finer ? LinkRun.GroupFinerJudgeSchema : LinkRun.GroupJudgeSchema,
        };
        LinkRun.Stage(repo.Root, state, "judge", "prepared");
        LinkRun.Save(work, state);
        return LinkRun.Step(repo.Root, work, LinkRun.Judge, "judge.md", "judge.json", "Sections and candidate pages", (string?)state["schema"]);
    }

    static string Bundle(List<JsonObject> unit, string doc, IEnumerable<string> pages, SpecIndex index, bool finer)
    {
        var sb = new StringBuilder(SharedHead.Text(false, [finer ? "link-judge-finer.md" : "link-judge.md", "link-judge-group.md"], finer ? LinkRun.GroupFinerJudgeSchema : LinkRun.GroupJudgeSchema));
        foreach (var s in unit) sb.Append(LinkRun.SectionBlock(s, doc)).Append(AttachmentChecks.Block(s)).Append('\n');
        sb.Append(LinkRun.PagesBlock(pages, index));
        return sb.ToString();
    }

    /// <summary>The checked links, each to its own section, and the record of the unit on each section.</summary>
    internal static RunStep Finish(string root, string work, JsonObject state, string doc, IReadOnlyDictionary<string, JsonObject> unit, List<JsonObject> entries, int disputed, int unchecked_, int unclearLeftOut)
    {
        var ids = unit.Keys.ToList();
        var leader = (string)state["section"]!;
        var inputs = (string)state["inputs"]!;
        foreach (var (id, section) in unit)
        {
            var mine = entries.Where(e => (string?)e["fsd"]?["section"] == id).ToList();
            PutOnFile(root, doc, section, inputs, leader, ids, mine, state["usage"] as JsonObject);
            LinkRun.UpdateRun(root, LinkRun.Key(doc, id), r =>
            {
                var u = (JsonObject)r["unit"]!;
                u["retries"] = (int?)state["retries"] ?? 0;
                u["rejected"] = (int?)state["rejected"] ?? 0;
                u["disputed"] = disputed;
                u["unchecked"] = unchecked_;
                if (unclearLeftOut > 0) u["unclearLeftOut"] = unclearLeftOut;
            });
        }
        LinkRun.Stage(root, state, "done", "recorded");
        LinkRun.Save(work, state);
        return new RunStep("recorded", "none");
    }

    /// <summary>A unit's links go on file for a section. What was its own is saved first, if it was its own, so turning the switch off asks for no new call.</summary>
    static void PutOnFile(string root, string doc, JsonObject section, string inputs, string leader, List<string> ids, List<JsonObject> entries, JsonObject? usage)
    {
        var id = (string)section["id"]!;
        var key = LinkRun.Key(doc, id);
        var own = (LinkRun.LoadRuns(root)[key]?["unit"] is null) ? Current(root, doc, id) : null;
        LinkRun.ApplyEntries(root, doc, id, entries);
        LinkRun.UpdateRun(root, key, r =>
        {
            var saved = r["saved"] as JsonObject ?? new JsonObject();
            if (own is not null) saved["own"] = new JsonObject { ["inputs"] = (string?)r["inputs"], ["entries"] = new JsonArray([.. own.Select(e => (JsonNode)Plain(e))]) };
            var group = new JsonObject { ["inputs"] = inputs, ["entries"] = new JsonArray([.. entries.Select(e => (JsonNode)Plain(e))]) };
            if (usage is not null) group["usage"] = usage.DeepClone();
            saved["group"] = group;
            r["saved"] = saved;
            var unit = new JsonObject
            {
                ["kind"] = "group", ["leader"] = leader, ["sections"] = new JsonArray([.. ids.Select(i => (JsonNode)i)]), ["inputs"] = inputs, ["links"] = entries.Count,
            };
            if (usage is not null) unit["usage"] = usage.DeepClone();
            r["unit"] = unit;
        });
    }

    /// <summary>A group's links are on file and this section's own run is current: its own links go back. False when they weren't saved for these inputs, so it has to run.</summary>
    internal static bool RestoreOwn(string root, string doc, JsonObject section, string inputs)
    {
        var id = (string)section["id"]!;
        var key = LinkRun.Key(doc, id);
        var saved = LinkRun.LoadRuns(root)[key]?["saved"]?["own"];
        if ((string?)saved?["inputs"] != inputs) return false;
        LinkRun.ApplyEntries(root, doc, id, saved!["entries"]!.AsArray().OfType<JsonObject>().Select(e => Fit(e, section)).ToList());
        LinkRun.UpdateRun(root, key, r => r.Remove("unit"));
        return true;
    }

    static List<JsonObject> Current(string root, string doc, string id) =>
        [.. TraceFile.Load(root)["entries"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["fsd"]?["doc"] == doc && (string?)e["fsd"]?["section"] == id && (string?)e["state"] != TraceFile.TableFact && !TraceFindings.IsGone(e))];

    /// <summary>What a link is, without what a person or a check added: those stay with the entry on file, and a saved copy never brings an old decision back.</summary>
    static JsonObject Plain(JsonObject e)
    {
        var copy = (JsonObject)e.DeepClone();
        foreach (var field in TraceFile.FindingFields.Concat(["decided_on", "decision", "decided_by"])) copy.Remove(field);
        return copy;
    }

    /// <summary>A saved link back on a section that may have moved since: its number and title as they are now, and the id that follows from them.</summary>
    static JsonObject Fit(JsonObject saved, JsonObject section)
    {
        var e = (JsonObject)saved.DeepClone();
        if (e["fsd"] is JsonObject fsd)
        {
            fsd["section"] = (string)section["id"]!;
            fsd["title"] = (string)section["title"]!;
        }
        e["id"] = TraceFile.IdOf(e, (string)section["id"]!);
        return e;
    }
}

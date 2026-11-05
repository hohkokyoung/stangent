using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.State;

namespace Stangent.Spec.Pages;

/// <summary>
/// A person's corrections to a page (T.1, D131). Recorded, then approved by a person; code puts an approved one back every time the page is written,
/// so regeneration can't undo it. The writer isn't told, so no prompt or input hash changes: the page's statement simply says what the person said.
/// Off by default (<c>page-corrections</c>), because a corrected statement is what a link judge then reads. No AI anywhere in this.
/// </summary>
public static class PageCorrections
{
    public const string Component = "page-corrections", Contract = "page-corrections.v1.json";

    public static string FileIn(string root) => Path.Combine(root, "specs", "page-corrections.json");

    public static JsonObject Load(string root) =>
        PageRun.LoadJson(FileIn(root)) as JsonObject ?? new JsonObject { ["schemaVersion"] = 1, ["corrections"] = new JsonArray() };

    static List<JsonObject> All(JsonObject file) => [.. file["corrections"]!.AsArray().OfType<JsonObject>()];

    static void Save(string root, JsonObject file)
    {
        var errors = SchemaRegistry.Embedded.Validate(Contract, file.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_correction", "The corrections file broke its contract (a stangent bug).", ExitCodes.Failed, errors);
        Quotes.WriteAtomic(FileIn(root), CanonicalJson.Serialize(file));
    }

    static void EnsureOn(StangentRepo repo)
    {
        if (!StangentComponents.IsOn(repo.Config, Component))
            throw new StangentException("component_off", $"{Component} is off, so a correction can't be recorded. Turn on `components.{Component}`: a corrected statement is what link judges then read.", ExitCodes.Usage);
    }

    public static JsonObject Propose(PageSet set, StangentRepo repo, string pageKey, string statement, string? text, string why, string by)
    {
        EnsureOn(repo);
        if (string.IsNullOrWhiteSpace(why) || string.IsNullOrWhiteSpace(by)) throw new StangentException("usage", "A correction needs --why and --by.", ExitCodes.Usage);
        var paths = new SpecPaths(repo.Root);
        var page = set.Find(pageKey) ?? throw new StangentException("not_found", $"No page '{pageKey}'.", ExitCodes.NotFound);
        var recorded = PageRun.LoadJson(paths.PageJson(page)) as JsonObject ?? throw new StangentException("not_found", $"Page '{page.Id}' isn't written yet.", ExitCodes.NotFound);
        var found = PageChecks.Statements(recorded).Where(s => s.Id == statement).Select(s => s.Statement).FirstOrDefault()
            ?? throw new StangentException("not_found", $"{page.Id} has no statement {statement}.", ExitCodes.NotFound);
        using var _ = PageRun.Lock(paths);
        var file = Load(repo.Root);
        var list = All(file);
        if (list.FirstOrDefault(c => (string)c["page"]! == page.Id && (string?)c["appliedTo"] == statement && (string)c["state"]! == "approved") is { } held)
            throw new StangentException("already_corrected", $"{statement} already has {(string)held["id"]!}. Withdraw it first: `stangent spec correct-reject {(string)held["id"]!}`.", ExitCodes.Failed);
        var id = $"C-{list.Count + 1:D4}";
        var correction = new JsonObject
        {
            ["id"] = id, ["page"] = page.Id, ["statement"] = statement, ["was"] = (string)found["text"]!, ["kind"] = text is null ? "remove" : "replace", ["why"] = why, ["by"] = by,
        };
        if (text is not null) correction["text"] = text;
        StateChanges.Apply(repo.Root, correction, StateKinds.PageCorrection, id, "proposed", StateActor.Code("stangent spec"), $"recorded from {by}", new JsonObject());
        file["corrections"]!.AsArray().Add(correction);
        Save(repo.Root, file);
        return correction;
    }

    public static JsonObject Approve(PageSet set, StangentRepo repo, string id, string by)
    {
        EnsureOn(repo);
        var paths = new SpecPaths(repo.Root);
        JsonObject correction;
        JsonObject record;
        PageDef page;
        using (PageRun.Lock(paths))
        {
            var file = Load(repo.Root);
            correction = Find(file, id);
            page = set.Find((string)correction["page"]!) ?? throw new StangentException("not_found", $"{id} is on {(string)correction["page"]!}, which the graph no longer calls for.", ExitCodes.NotFound);
            record = PageRun.LoadJson(paths.PageJson(page)) as JsonObject ?? throw new StangentException("not_found", $"Page '{page.Id}' isn't written.", ExitCodes.NotFound);
            StateChanges.Apply(repo.Root, correction, StateKinds.PageCorrection, id, "approved", StateActor.Person(by), "approved by a person");
            correction["approvedBy"] = by;
            correction["approvedAt"] = (string?)record["generated"]?["bundleHash"];
            Put(record, [correction]);
            Save(repo.Root, file);
        }
        // The page's input hash is untouched, so it stays current: no AI run follows.
        Quotes.WriteAtomic(paths.PageJson(page), CanonicalJson.Serialize(record));
        PageRun.WriteRendered(set, repo.Config, paths, page, record);
        var traceProblem = Staleness.ClearReportedWrong(repo.Root, page.Id, (string?)correction["appliedTo"] ?? (string)correction["statement"]!, $"{id} corrected the statement");
        var result = (JsonObject)correction.DeepClone();
        if (traceProblem is not null) result["traceProblem"] = traceProblem;
        return result;
    }

    /// <summary>Refuses a proposal, or withdraws an approved one: the page keeps its words until it's next written.</summary>
    public static JsonObject Reject(StangentRepo repo, string id, string by, string why)
    {
        EnsureOn(repo);
        using var _ = PageRun.Lock(new SpecPaths(repo.Root));
        var file = Load(repo.Root);
        var correction = Find(file, id);
        StateChanges.Apply(repo.Root, correction, StateKinds.PageCorrection, id, (string)correction["state"]! == "approved" ? "withdrawn" : "rejected", StateActor.Person(by), why);
        Save(repo.Root, file);
        return correction;
    }

    static JsonObject Find(JsonObject file, string id) =>
        All(file).FirstOrDefault(c => (string)c["id"]! == id) ?? throw new StangentException("not_found", $"No correction {id}. `stangent spec corrections` lists them.", ExitCodes.NotFound);

    /// <summary>The list, each saying whether the code moved under it since it was approved and whether it could be put back.</summary>
    public static JsonObject Report(PageSet? set, string root, string? pageKey)
    {
        var paths = new SpecPaths(root);
        var rows = new JsonArray();
        foreach (var c in All(Load(root)).Where(c => pageKey is null || (string)c["page"]! == pageKey || set?.Find(pageKey)?.Id == (string)c["page"]!))
        {
            var row = (JsonObject)c.DeepClone();
            if (set?.Find((string)c["page"]!) is { } page && (string)c["state"]! == "approved" && (string?)c["approvedAt"] is { } at
                && PageRun.LoadJson(paths.PageJson(page)) is JsonObject recorded && (string?)recorded["generated"]?["bundleHash"] != at)
                row["codeMoved"] = true;
            rows.Add(row);
        }
        return new JsonObject { ["corrections"] = rows };
    }

    /// <summary>Approved corrections for one page, or none when the switch is off.</summary>
    public static List<JsonObject> Approved(StangentConfig config, string root, string pageId) =>
        !StangentComponents.IsOn(config, Component) || !File.Exists(FileIn(root)) ? [] :
        [.. All(Load(root)).Where(c => (string)c["page"]! == pageId && (string)c["state"]! == "approved")];

    /// <summary>A copy of the recorded page with corrected statements back in the words the writer gave, so the ids match as they would have with no correction.</summary>
    public static JsonObject? BeforeCorrections(JsonObject? recorded, IReadOnlyList<JsonObject> approved)
    {
        if (recorded is null || approved.Count == 0) return recorded;
        var copy = (JsonObject)recorded.DeepClone();
        foreach (var c in approved.Where(c => (string)c["kind"]! == "replace"))
            if (PageChecks.Statements(copy).Where(s => s.Id == ((string?)c["appliedTo"] ?? (string)c["statement"]!)).Select(s => s.Statement).FirstOrDefault() is { } s)
                s["text"] = (string)c["was"]!;
        return copy;
    }

    /// <summary>Puts corrections on a page: by the statement's id, then by the words it was given for. Marks each with where it landed.</summary>
    public static void Put(JsonObject page, IReadOnlyList<JsonObject> approved)
    {
        foreach (var c in approved)
        {
            var was = Quotes.Normalize((string)c["was"]!);
            var id = (string?)c["appliedTo"] ?? (string)c["statement"]!;
            var all = PageChecks.Statements(page).ToList();
            var hit = all.Where(s => s.Id == id).Concat(all.Where(s => Quotes.Normalize((string?)s.Statement["text"] ?? "") == was)).Cast<(string Section, string Id, JsonObject Statement)?>().FirstOrDefault();
            if (hit is not { } found)
            {
                c["notApplied"] = true;
                continue;
            }
            c.Remove("notApplied");
            c["appliedTo"] = found.Id;
            if ((string)c["kind"]! == "replace") found.Statement["text"] = (string)c["text"]!;
            else page["sections"]![found.Section]!.AsArray().Remove(found.Statement);
        }
    }

    /// <summary>The page was written again: its approved corrections go back on, and the file says where they landed. A warning when one had nowhere to go.</summary>
    public static string? Reapply(StangentConfig config, string root, string pageId, JsonObject page)
    {
        if (Approved(config, root, pageId).Count == 0) return null;
        using var _ = PageRun.Lock(new SpecPaths(root));
        var file = Load(root);
        var mine = All(file).Where(c => (string)c["page"]! == pageId && (string)c["state"]! == "approved").ToList();
        Put(page, mine);
        Save(root, file);
        var lost = mine.Where(c => (bool?)c["notApplied"] == true).Select(c => (string)c["id"]!).ToList();
        return lost.Count == 0 ? null : $"{string.Join(", ", lost)}: the statement is gone from the page, so the correction wasn't put back. A person should look: `stangent spec corrections`.";
    }

    /// <summary>What the Markdown marks: corrected statements and removed ones. Empty when there are none, so a page with none reads as it always did.</summary>
    public static (IReadOnlyDictionary<string, string> Corrected, IReadOnlyList<JsonObject> Removed) Marks(string root, string pageId, StangentConfig? config)
    {
        var approved = config is null || !StangentComponents.IsOn(config, Component) || !File.Exists(FileIn(root)) ? [] : Approved(config, root, pageId);
        return (approved.Where(c => (string)c["kind"]! == "replace" && c["appliedTo"] is not null && (bool?)c["notApplied"] != true)
                .GroupBy(c => (string)c["appliedTo"]!).ToDictionary(g => g.Key, g => (string)g.Last()["id"]!, StringComparer.Ordinal),
            [.. approved.Where(c => (string)c["kind"]! == "remove" && (bool?)c["notApplied"] != true)]);
    }
}

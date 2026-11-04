using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.Evidence;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Trace;

/// <summary>What the links can point at: the imported FSD sections and the recorded reference pages.</summary>
public sealed class SpecIndex
{
    public required IReadOnlyDictionary<(string Doc, string Section), JsonObject> Sections { get; init; }
    public required IReadOnlyDictionary<string, JsonObject> Pages { get; init; }
    /// <summary>Where each recorded page's JSON lives, for what's kept beside it: its id ledger and line citations.</summary>
    public IReadOnlyDictionary<string, string> PageFiles { get; init; } = new Dictionary<string, string>();

    public static SpecIndex Load(string root)
    {
        var sections = new Dictionary<(string, string), JsonObject>();
        foreach (var doc in FsdImport.Documents(root))
            foreach (var s in doc["sections"]!.AsArray().OfType<JsonObject>())
                sections[((string)doc["id"]!, (string)s["id"]!)] = s;
        var pages = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var dir = Path.Combine(root, "specs", "reference");
        if (Directory.Exists(dir))
            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                if (Path.GetFileName(file) != "failed.json" && JsonNode.Parse(File.ReadAllText(file)) is JsonObject p && p["sections"] is not null)
                {
                    pages[(string)p["id"]!] = p;
                    files[(string)p["id"]!] = file;
                }
        return new SpecIndex { Sections = sections, Pages = pages, PageFiles = files };
    }

    public JsonObject? Statement(string page, string statement) =>
        Pages.TryGetValue(page, out var p) ? PageChecks.Statements(p).FirstOrDefault(s => s.Id == statement).Statement : null;

    /// <summary>Whether a page retired this statement id (2.3): it's gone for good, and nothing is re-pointed.</summary>
    public bool Retired(string root, string page, string statement) => PageFiles.TryGetValue(page, out var file) && !string.IsNullOrEmpty(statement)
        && File.Exists(StatementIds.LedgerFile(file)) && StatementIds.Ledger(root, file).Removed(statement[..1]).Contains(statement);
}

/// <summary>
/// The CI checks on <c>specs/trace.yaml</c> (D58): the schema and allowed values; every id still the hash of its link;
/// both quotes still in the current FSD section and page statement, and the statement's text still what the decision was made on
/// (else the decision drops back to undecided); every symbol a quoted statement cites in the graph; <c>decided_by</c> and
/// <c>severity_by</c> never typed in a PR; and each finding's id and severity, which it issues where they're missing, and its
/// evidence, which it derives again on every check and refuses if typed (D118).
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
        if (problems.Count > 0) return Report(trace, problems, [], [], [], [], new JsonObject());

        var index = SpecIndex.Load(root);
        var nodes = graph?.AllNodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var typedBefore = (baseTrace?["entries"]?.AsArray() ?? []).OfType<JsonObject>().ToDictionary(e => (string)e["id"]!, e => (string?)e["decided_by"]);
        var baseByLink = TraceFile.ByLink(baseTrace?["entries"]?.AsArray() ?? []);
        var baseEntries = (baseTrace?["entries"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        var baseByFinding = baseEntries.Where(e => e["finding"] is not null).GroupBy(e => (string)e["finding"]!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var refused = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var rowsMoved = AttachmentChecks.StampRows(root, trace);
        var stamped = TraceFindings.Stamp(root, trace, index, baseTrace is null ? null : baseByFinding, refused) | rowsMoved;
        stamped |= StampSectionIds(trace, index);
        var findingIds = new HashSet<string>(StringComparer.Ordinal);
        var stale = new List<string>();
        var changed = new List<JsonNode>();
        var changedIds = new List<string>();
        var retired = new List<JsonNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var requirements = new Lazy<HashSet<string>>(() => [.. Stangent.Spec.Requirements.RequirementFile.Docs(root).SelectMany(d => Stangent.Spec.Requirements.RequirementFile.Features(root, d))
            .SelectMany(f => f["requirements"]!.AsArray().Select(r => (string)r!["id"]!))]);
        foreach (var e in trace["entries"]!.AsArray().OfType<JsonObject>())
        {
            var id = (string)e["id"]!;
            if (!seen.Add(id)) problems.Add($"{id}: the id is used twice");
            var fsd = e["fsd"] as JsonObject;
            var page = e["page"] as JsonObject;
            var expected = TraceFile.EntryId((string)e["state"]!, (string?)fsd?["doc"], (string?)fsd?["section"], (string?)page?["id"], (string?)fsd?["quote"], (string?)page?["quote"], (string?)fsd?["requirement"]);
            if (id != expected) problems.Add($"{id}: the state, an end or a quote was edited by hand (only `decision` is edited; the id would be {expected})");
            if ((string?)fsd?["requirement"] is { } requirement && !requirements.Value.Contains(requirement))
                problems.Add($"{id}: requirement {requirement} doesn't exist any more");

            if (TraceFindings.IsFinding(e))
            {
                var finding = (string)e["finding"]!;
                if (!findingIds.Add(finding)) problems.Add($"{id}: finding {finding} is used twice");
                problems.AddRange(TraceFindings.Check(e, nodes, refused.GetValueOrDefault(finding)));
                // A finding's id is the same across a move, so the base's copy is found by it, or by its entry id when a person changed it.
                var entryBefore = baseEntries.FirstOrDefault(b => (string)b["id"]! == id);
                if ((string?)entryBefore?["finding"] is { } was && was != finding) problems.Add($"{id}: its finding id was {was} and is now {finding}; ids are issued by code");
                if ((string?)e["severity_by"] is { } gradedBy && baseTrace is not null && (string?)baseByFinding.GetValueOrDefault(finding)?["severity_by"] != gradedBy)
                    problems.Add($"{id}: severity_by was set in this change ({gradedBy}); it's filled from git after merge, never typed");
            }
            else if (TraceFile.FindingFields.Any(f => e[f] is not null))
                problems.Add($"{id}: an agree link isn't a finding, so it has no finding id, severity or evidence");

            // Nothing finds it now, so there's nothing to compare its quotes with: it's kept and listed (D122).
            if (TraceFindings.IsGone(e)) continue;

            // "The page is wrong" has to name a page, or there's nothing for its owner to regenerate (D119).
            if ((string?)e["decision"] == "page-wrong" && page is null) problems.Add($"{id}: page-wrong needs a page, and this entry has none");

            var quotesMatch = true;
            if (fsd is not null)
            {
                var sectionId = (string)fsd["section"]!;
                if (index.Sections.TryGetValue(((string)fsd["doc"]!, sectionId), out var section))
                {
                    // A quote from an attachment is checked against that file, with the switch on or off: it's what the link says it rests on (D123).
                    if (fsd["quote"] is { } q && !(fsd["attachment"] is not null ? AttachmentChecks.Check(root, fsd, (string)q!).Holds : Quotes.InText((string)q!, (string)section["text"]!))) quotesMatch = false;
                }
                // Its section went in a promotion, so the quote has nothing to be checked against: that isn't a stale quote (D117).
                else if (TraceFile.IsRemovedSection(sectionId)) problems.Add($"{id}: FSD section {fsd["doc"]} §{sectionId} was removed; its decision stays until a person deals with the entry");
                else { problems.Add($"{id}: FSD section {fsd["doc"]} §{sectionId} doesn't exist"); quotesMatch = false; }
            }
            if (page is not null)
            {
                if (!index.Pages.ContainsKey((string)page["id"]!)) { problems.Add($"{id}: page {page["id"]} has no recorded reference page"); quotesMatch = false; }
                else if (page["statement"] is { } st)
                {
                    var statement = index.Statement((string)page["id"]!, (string)st!);
                    if (statement is null && index.Retired(root, (string)page["id"]!, (string)st!))
                        retired.Add(new JsonObject { ["entry"] = id, ["page"] = (string)page["id"]!, ["statement"] = (string)st! });
                    if (statement is null || (page["quote"] is { } pq && !Quotes.InText((string)pq!, (string)statement["text"]!)))
                        quotesMatch = false;
                    else if (nodes is not null)
                        foreach (var cite in statement["cites"]!.AsArray().Select(c => (string)c!).Where(c => !nodes.Contains(c)))
                            problems.Add($"{id}: statement {page["id"]} {st} cites `{cite}`, which is no longer in the code graph");
                }
            }
            if (!quotesMatch)
            {
                stale.Add(id);
                if (fix) TraceFindings.DropDecision(root, e, StateActor.Code("stangent trace check"), "a quote no longer matches");
            }
            else if (e["decision"] is null)
            {
                if (e["decided_on"] is not null) { e.Remove("decided_on"); stamped = true; }
            }
            else if (DecidedOn(e, index) is { } textNow)
            {
                var entryWas = baseEntries.FirstOrDefault(b => (string)b["id"]! == id) ?? baseByLink.GetValueOrDefault(TraceFile.LinkKey(e));
                // A decision with its person still on it is the one the base holds: re-tying it to new words would keep it unchecked.
                if (baseTrace is not null && e["decided_by"] is not null && (string?)entryWas?["decided_on"] is { } onBefore && (string?)e["decided_on"] != onBefore)
                    problems.Add($"{id}: decided_on was changed in this change; code writes it, and it ties the decision to the statement's text");
                else if ((string?)e["decided_on"] is not { } decidedOn) { e["decided_on"] = textNow; stamped = true; }
                else if (decidedOn != textNow)
                {
                    changedIds.Add(id);
                    changed.Add(new JsonObject { ["entry"] = id, ["page"] = (string)page!["id"]!, ["statement"] = (string)page["statement"]!, ["was"] = (string?)e["decision"] });
                    if (fix) TraceFindings.DropDecision(root, e, StateActor.Code("stangent trace check"), "the statement it was decided on changed");
                }
            }

            // A confirmation rests on the same words a decision does, so it drops the same way, and the finding goes back to the BA rather than staying closed.
            if ((string?)e["review"] == "confirmed" && TraceFindings.ReviewHolds(e) && (!quotesMatch || DecidedOn(e, index) != (string?)e["review_note"]?["on"]))
            {
                if (fix)
                {
                    TraceFindings.SendBack(root, e, StateActor.Code("stangent trace check"), "a person's confirmation was dropped because the statement or quote it was made on changed");
                    stamped = true;
                }
                else problems.Add($"{id}: the statement or quote changed after a person confirmed this verdict, so the confirmation must drop (run `stangent trace check --fix`)");
            }

            if (baseTrace is not null && (string?)e["decided_by"] is { } by && (!typedBefore.TryGetValue(id, out var before) || before != by)
                // A promotion gave the entry a new id, because its section's number changed: the same link keeps who decided it (D117).
                && (string?)baseByLink.GetValueOrDefault(TraceFile.LinkKey(e))?["decided_by"] != by)
                problems.Add($"{id}: decided_by was set in this change ({by}); it's filled from git after merge, never typed");
        }
        var undecided = trace["entries"]!.AsArray().OfType<JsonObject>().Where(e => stale.Contains((string)e["id"]!) && e["decision"] is not null).Select(e => (string)e["id"]!).ToList();
        // A decision that dropped back took its person with it, and the evidence says who checked it.
        if (fix && (stale.Count > 0 || changedIds.Count > 0)) TraceFindings.Stamp(root, trace, index, baseTrace is null ? null : baseByFinding, refused);
        if (stamped || fix && (stale.Count > 0 || changedIds.Count > 0)) TraceFile.Save(root, trace);
        foreach (var id in undecided)
            problems.Add($"{id}: a quote no longer matches the FSD or the page, so its decision must drop back to undecided (run `stangent trace check --fix`)");
        var stillDecided = trace["entries"]!.AsArray().OfType<JsonObject>().Where(e => changedIds.Contains((string)e["id"]!) && e["decision"] is not null);
        foreach (var e in stillDecided)
            problems.Add($"{e["id"]}: statement {e["page"]!["id"]} {e["page"]!["statement"]} changed after this decision was made on it, so the decision must drop back to undecided (run `stangent trace check --fix`)");
        return Report(trace, problems, stale, undecided, retired, changed, TraceCoverage.All(root, trace));
    }

    /// <summary>
    /// Each link's section's permanent id, beside its number (D121), by code on every check, even without <c>--fix</c>, as a finding's id is. It follows
    /// the number and the document, so nothing typed survives, and no hash or entry id reads it. A section marked removed keeps the id its mark holds.
    /// One no imported document knows keeps what it has. Returns whether anything changed.
    /// </summary>
    internal static bool StampSectionIds(JsonObject trace, SpecIndex index)
    {
        var changed = false;
        foreach (var fsd in trace["entries"]!.AsArray().OfType<JsonObject>().Select(e => e["fsd"]).OfType<JsonObject>())
        {
            var section = (string)fsd["section"]!;
            var id = RemovedMark.Match(section) is { Success: true } mark ? mark.Groups[1].Value
                : (string?)index.Sections.GetValueOrDefault(((string)fsd["doc"]!, section))?["permanentId"];
            if (id is null || (string?)fsd["sectionId"] == id) continue;
            fsd["sectionId"] = id;
            changed = true;
        }
        return changed;
    }

    static readonly System.Text.RegularExpressions.Regex RemovedMark = new(@"^\(removed (S-[0-9]{4,})\)");

    static string? DecidedOn(JsonObject e, SpecIndex index) => TraceFindings.DecidedOn(e, index);

    /// <summary>Ties a decision made just now to the words it was made on, unless it's tied already.</summary>
    static bool TieDecision(JsonObject e, SpecIndex index)
    {
        if (e["decision"] is null || e["decided_on"] is not null || DecidedOn(e, index) is not { } text) return false;
        e["decided_on"] = text;
        return true;
    }

    static JsonObject Report(JsonObject trace, List<string> problems, List<string> stale, List<string> undecided, List<JsonNode> retired, List<JsonNode> changed, JsonObject coverage)
    {
        var entries = trace["entries"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
        return new JsonObject
        {
            ["ok"] = problems.Count == 0,
            ["counts"] = new JsonObject
            {
                ["entries"] = entries.Count,
                ["byState"] = new JsonObject(TraceFile.States.Select(s => KeyValuePair.Create(s, (JsonNode?)entries.Count(e => (string?)e["state"] == s)))),
                ["undecided"] = entries.Count(e => e["decision"] is null && (!TraceFindings.IsFinding(e) || TraceFindings.GoesTo(e) == "ba")),
                ["findings"] = entries.Count(e => TraceFindings.IsFinding(e) && !TraceFindings.IsGone(e)),
                ["bySeverity"] = new JsonObject(Severity.All.Select(s => KeyValuePair.Create(s, (JsonNode?)entries.Count(e => TraceFindings.IsFinding(e) && !TraceFindings.IsGone(e) && ((string?)e["severity"] ?? Severity.Ungraded) == s)))),
                ["staleQuotes"] = stale.Count,
                ["changedStatements"] = changed.Count,
                ["recheck"] = entries.Count(e => TraceFindings.Standing(e) == "recheck"),
                ["gone"] = entries.Count(TraceFindings.IsGone),
                ["uncoveredSections"] = coverage.Sum(d => d.Value!["uncovered"]!.AsArray().Count),
            },
            ["problems"] = new JsonArray([.. problems.Select(p => (JsonNode)p)]),
            ["staleQuotes"] = new JsonArray([.. stale.Select(s => (JsonNode)s)]),
            ["retiredStatements"] = new JsonArray([.. retired]),
            ["changedStatements"] = new JsonArray([.. changed]),
            ["coverage"] = coverage,
            ["findings"] = TraceFindings.Listing(TraceFile.Sorted(trace["entries"]?.AsArray() ?? [])),
            ["gone"] = TraceFindings.Listing(TraceFile.Sorted(trace["entries"]?.AsArray() ?? []), gone: true),
            ["queues"] = TraceFindings.Groups(TraceFile.Sorted(trace["entries"]?.AsArray() ?? [])),
        };
    }

    /// <summary>Bulk approval (D53): every confirmed "agree" link under a chapter gets <c>decision: agree</c>, in one edit.</summary>
    /// <param name="who">The person running it, for the history only; <c>decided_by</c> still comes from git after the merge (D58).</param>
    public static int ApproveChapter(string root, string doc, string chapter, string who = "developer")
    {
        var trace = TraceFile.Load(root);
        var index = SpecIndex.Load(root);
        var n = 0;
        foreach (var e in trace["entries"]!.AsArray().OfType<JsonObject>())
        {
            var section = (string?)e["fsd"]?["section"];
            if ((string?)e["state"] != "agree" || (string?)e["check"] != "confirmed" || e["decision"] is not null) continue;
            if ((string?)e["fsd"]?["doc"] != doc || section is null || !(section == chapter || section.StartsWith(chapter + ".", StringComparison.Ordinal) || section.StartsWith(chapter + " / ", StringComparison.Ordinal))) continue;
            StateChanges.Apply(root, e, StateKinds.TraceDecision, (string)e["id"]!, "agree", StateActor.Person(who), $"approved chapter {chapter}");
            TieDecision(e, index);
            n++;
        }
        if (n > 0) TraceFile.Save(root, trace);
        return n;
    }

    /// <summary>After a PR merges, CI names who decided (D58): every entry whose decision or severity the PR changed gets the
    /// approver or merger the code-host adapter reports. Never typed by a person. A severity's move goes on the module too, and a decision
    /// not yet tied to its statement's text is tied here, before a regeneration can change that text (D118).</summary>
    public static int Attribute(string root, JsonObject baseTrace, string by)
    {
        var trace = TraceFile.Load(root);
        var before = (baseTrace["entries"]?.AsArray() ?? []).OfType<JsonObject>().ToDictionary(e => (string)e["id"]!, e => (string?)e["decision"]);
        var byLink = TraceFile.ByLink(baseTrace["entries"]?.AsArray() ?? []);
        var graded = (baseTrace["entries"]?.AsArray() ?? []).OfType<JsonObject>().Where(e => e["finding"] is not null).GroupBy(e => (string)e["finding"]!).ToDictionary(g => g.Key, g => (string?)g.First()["severity"], StringComparer.Ordinal);
        var index = SpecIndex.Load(root);
        var tied = false;
        var n = 0;
        foreach (var e in trace["entries"]!.AsArray().OfType<JsonObject>())
        {
            tied |= TieDecision(e, index);
            if ((string?)e["finding"] is { } finding && (string?)e["severity"] is { } severity && severity != (graded.GetValueOrDefault(finding) ?? Severity.Ungraded))
            {
                StateChanges.Move(root, StateKinds.FindingSeverity, finding, graded.GetValueOrDefault(finding) ?? Severity.Ungraded, severity, StateActor.Person(by), "merged in a PR");
                e["severity_by"] = severity == Severity.Ungraded ? null : by;
                n++;
            }
            var decision = (string?)e["decision"];
            // A promotion gave the entry a new id, because its section's number changed: nobody decided anything (D117).
            var old = before.TryGetValue((string)e["id"]!, out var was) ? was : (string?)byLink.GetValueOrDefault(TraceFile.LinkKey(e))?["decision"];
            if (decision == old) continue;
            // The person's edit is already in the file: this is where it's seen, checked and put in the history.
            if ((string?)e["decided_by"] != by || decision is null)
                StateChanges.Move(root, StateKinds.TraceDecision, (string)e["id"]!, old, decision ?? "undecided", StateActor.Person(by), "merged in a PR");
            if (decision is null)
            {
                // A person took their decision away, so the verdict under it doesn't get to decide instead.
                TraceFindings.SendBack(root, e, StateActor.Person(by), "a person's decision was removed in a PR");
                tied = true;
                continue;
            }
            e["decided_by"] = by;
            TraceFindings.Settled(root, e, StateActor.Person(by));
            n++;
        }
        if (n > 0 || tied) TraceFile.Save(root, trace);
        return n;
    }
}

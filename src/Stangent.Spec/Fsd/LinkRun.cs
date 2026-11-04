using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>
/// One FSD section's AI steps (D53, D60, D62), as a small state machine like <see cref="PageRun"/>: candidates by code
/// (or, when code finds none, by the candidate finder from page titles only), the link judge with quotes, code checks
/// on every quote (one retry), a second fresh check of every "agree" link, then the entries go into
/// <c>specs/trace.yaml</c>. An unchanged input hash means no AI call (D18). Run records go to <c>specs/trace-runs.json</c>.
/// </summary>
public static class LinkRun
{
    public const string Finder = "stangent-candidate-finder";
    public const string Judge = "stangent-link-judge";
    public const string RequirementJudge = "stangent-requirement-judge";
    public const string Checker = "stangent-agree-checker";

    public static string Key(string doc, string section) => $"{doc}#{section}";
    public static string RunsFile(string root) => Path.Combine(root, "specs", "trace-runs.json");
    static string Work(string root, string doc, string section) =>
        Path.Combine(root, ".stangent", "fsd", doc, PageList.Slug(section, Key(doc, section)));

    static JsonObject? Parent(SpecIndex index, string doc, JsonObject section) =>
        (string?)section["parent"] is { } parent ? index.Sections.GetValueOrDefault((doc, parent)) : null;

    /// <summary>The section's requirements (D99), when they were built from its current text: links are then judged one
    /// requirement at a time, so a page covering part of a section can agree with part of it. Null: judge the section's
    /// sentences, as before.</summary>
    public static List<JsonObject>? Requirements(string root, string doc, JsonObject section) =>
        RequirementFile.Features(root, doc).FirstOrDefault(f => (string)f["section"]! == (string)section["id"]!) is { } feature
        && (string?)feature["textHash"] == CanonicalJson.Hash((string)section["text"]!)
            ? [.. feature["requirements"]!.AsArray().OfType<JsonObject>()] : null;

    static string FirstQuote(JsonObject requirement) => (string)requirement["quotes"]![0]!["text"]!;

    public static RunStep Prepare(PageSet set, StangentRepo repo, string doc, string sectionId)
    {
        var index = SpecIndex.Load(repo.Root);
        var section = index.Sections.GetValueOrDefault((doc, sectionId))
            ?? throw new StangentException("not_found", $"No section {sectionId} in FSD {doc}. Run `stangent fsd import` first.", ExitCodes.NotFound);
        if (((string)section["text"]!).Trim().Length == 0) return new RunStep("skipped", "none", Errors: ["the section has no text of its own"]);
        var runs = LoadRuns(repo.Root);
        var key = Key(doc, sectionId);
        var run = runs[key] as JsonObject;
        var requirements = Requirements(repo.Root, doc, section);
        // Every sentence is background: nothing here to link.
        if (requirements is { Count: 0 })
        {
            ApplyEntries(repo.Root, doc, sectionId, []);
            return new RunStep("skipped", "none", Errors: ["the section has no requirements"]);
        }

        var found = Candidates.ForSection(section, set, index.Pages, Parent(index, doc, section));
        var candidates = found.Where(c => index.Pages.ContainsKey(c.Page)).ToList();
        // A candidate with no recorded page (it failed, or was never generated) can't be judged: say so, never guess a gap.
        if (candidates.Count == 0 && found.Count > 0)
            return new RunStep("failed", "none", Errors: [$"no candidate page has been recorded yet: {string.Join(", ", found.Select(c => c.Page))}"]);
        var by = "code";
        if (candidates.Count == 0)
        {
            var finderBundle = FinderBundle(section, set, index);
            var finderHash = CanonicalJson.Hash(finderBundle);
            if ((string?)run?["finderInputs"] == finderHash)
            {
                candidates = [.. (run!["candidates"]?.AsArray() ?? []).Select(c => new Candidate((string)c!["page"]!, "ai", 0)).Where(c => index.Pages.ContainsKey(c.Page))];
                by = "ai";
            }
            else
            {
                AiPolicy.EnsureAllowed(repo.Config, "fsd prepare");
                var work = Fresh(repo.Root, doc, sectionId);
                File.WriteAllText(Path.Combine(work, "finder.md"), finderBundle);
                var finding = new JsonObject { ["doc"] = doc, ["section"] = sectionId, ["finderInputs"] = finderHash, ["retryUsed"] = false };
                Stage(repo.Root, finding, "find", "prepared");
                Save(work, finding);
                return Step(repo.Root, work, Finder, "finder.md", "finder.json", "Section and page list");
            }
        }
        return PrepareJudge(set, repo, index, section, doc, sectionId, candidates, by, run, runs);
    }

    static RunStep PrepareJudge(PageSet set, StangentRepo repo, SpecIndex index, JsonObject section, string doc, string sectionId,
        List<Candidate> candidates, string by, JsonObject? run, JsonObject runs, JsonObject? carried = null)
    {
        var key = Key(doc, sectionId);
        if (candidates.Count == 0)
        {
            // Nothing in the code matches this section, by name or by the finder: a gap, found by code.
            var quote = FirstSentence((string)section["text"]!);
            var unrecorded = set.Pages.Count(p => !index.Pages.ContainsKey(p.Id));
            var why = "no reference page matches this section (by name, field labels, or the candidate finder)"
                + (unrecorded > 0 ? $"; {unrecorded} page(s) have no recorded version, so it may be among them" : "");
            List<JsonObject> gaps = Requirements(repo.Root, doc, section) is { } reqs
                ? [.. reqs.Select(r => Entry("gap", doc, section, FirstQuote(r), null, null, null, "code", null, why, (string)r["id"]!))]
                : [Entry("gap", doc, section, quote, null, null, null, "code", null, why)];
            var existing = TraceFile.Load(repo.Root)["entries"]!.AsArray().OfType<JsonObject>()
                .Where(e => (string?)e["fsd"]?["doc"] == doc && (string?)e["fsd"]?["section"] == sectionId).Select(e => (string)e["id"]!).ToList();
            List<JsonObject> recorded = repo.Config.Fsd.Findings ? gaps : [];
            if (carried is null && existing.SequenceEqual(recorded.Select(e => (string)e["id"]!))) return new RunStep("current", "none");
            ApplyEntries(repo.Root, doc, sectionId, recorded);
            UpdateRun(repo.Root, key, r => { MergeInto(r, carried); r["inputs"] = null; r["candidates"] = new JsonArray(); r["candidatesBy"] = by; r["links"] = 1; });
            return new RunStep("recorded", "none");
        }
        var requirements = Requirements(repo.Root, doc, section);
        var judgeBundle = requirements is null ? JudgeBundle(section, doc, candidates.Select(c => c.Page), index) : RequirementJudgeBundle(section, doc, candidates.Select(c => c.Page), index, requirements);
        var inputs = JudgeInputs(judgeBundle, requirements is not null, repo.Config.Fsd.Findings);
        if ((string?)run?["inputs"] == inputs) return new RunStep("current", "none");
        AiPolicy.EnsureAllowed(repo.Config, "fsd prepare");
        var work = Fresh(repo.Root, doc, sectionId);
        File.WriteAllText(Path.Combine(work, "judge.md"), judgeBundle);
        var state = new JsonObject
        {
            ["doc"] = doc, ["section"] = sectionId, ["inputs"] = inputs, ["retryUsed"] = false,
            ["level"] = requirements is null ? "section" : "requirement",
            ["candidates"] = new JsonArray([.. candidates.Select(c => (JsonNode)new JsonObject { ["page"] = c.Page, ["why"] = c.Why })]),
            ["candidatesBy"] = by, ["usage"] = carried?.DeepClone() ?? new JsonObject(),
        };
        Stage(repo.Root, state, "judge", "prepared");
        Save(work, state);
        return Step(repo.Root, work, requirements is null ? Judge : RequirementJudge, "judge.md", "judge.json", "Section and candidate pages");
    }

    static string JudgeInputs(string judgeBundle, bool byRequirement, bool findings)
    {
        var inputs = CanonicalJson.Hash(judgeBundle + Bundles.Instructions(byRequirement ? "requirement-agree-checker.md" : "agree-checker.md") + SchemaRegistry.EmbeddedText("agree-checker-output.v1.json"));
        // Turning findings off (or on) changes what a section records, so it runs again; with them on the hash is unchanged.
        return findings ? inputs : CanonicalJson.Hash(inputs + "\nfindings off");
    }

    /// <summary>What a run's judge step would hash for this section and these candidate pages (D117: a section that only changed
    /// its number keeps its paid links). Null when a page has no record.</summary>
    internal static string? JudgeInputs(JsonObject section, string doc, IReadOnlyList<string> pages, SpecIndex index, List<JsonObject>? requirements, bool findings)
    {
        if (pages.Any(p => !index.Pages.ContainsKey(p))) return null;
        var bundle = requirements is null ? JudgeBundle(section, doc, pages, index) : RequirementJudgeBundle(section, doc, pages, index, requirements);
        return JudgeInputs(bundle, requirements is not null, findings);
    }

    public static RunStep Check(PageSet set, StangentRepo repo, CodeGraph graph, string doc, string sectionId, Usage? usage)
    {
        var work = Work(repo.Root, doc, sectionId);
        if (PageRun.LoadJson(Path.Combine(work, "state.json")) is not JsonObject state)
            throw new StangentException("not_prepared", $"No run for {doc} §{sectionId}. Run `stangent fsd prepare` first.", ExitCodes.Usage);
        var index = SpecIndex.Load(repo.Root);
        var section = index.Sections[(doc, sectionId)];
        var stage = (string)state["stage"]!;
        var role = stage switch { "find" => "finder", "judge" => "judge", "confirm" => "checker", _ => "none" };
        var usageAll = state["usage"] as JsonObject ?? new JsonObject();
        usageAll[role] = Usage.FromJson(usageAll[role]).Add(usage ?? Usage.None).ToJson();
        state["usage"] = usageAll;
        var errors = new List<string>();
        var nodes = graph.AllNodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var byRequirement = (string?)state["level"] == "requirement";
        var judge = byRequirement ? RequirementJudge : Judge;

        switch (stage)
        {
            case "find":
            {
                var output = PageChecks.Parse(Read(work, "finder.json"), errors);
                if (errors.Count == 0) errors.AddRange(SchemaRegistry.Embedded.Validate("candidate-finder-output.v1.json", output!.ToJsonString()).Select(e => $"Schema: {e}"));
                if (errors.Count == 0)
                    errors.AddRange(output!["candidates"]!.AsArray().Select(c => (string)c!["page"]!).Where(p => !index.Pages.ContainsKey(p))
                        .Select(p => $"`{p}` isn't a page in the list. Copy ids exactly."));
                if (errors.Count > 0) return Retry(repo.Root, work, state, errors, Finder, "finder.md", "finder.json") ?? Failed(repo.Root, work, state, errors);
                var picked = output!["candidates"]!.AsArray().Select(c => new Candidate((string)c!["page"]!, "ai", 0)).DistinctBy(c => c.Page).ToList();
                UpdateRun(repo.Root, Key(doc, sectionId), r =>
                {
                    r["finderInputs"] = (string)state["finderInputs"]!;
                    r["candidates"] = new JsonArray([.. picked.Select(c => (JsonNode)new JsonObject { ["page"] = c.Page, ["why"] = "ai" })]);
                    r["candidatesBy"] = "ai";
                });
                var runs = LoadRuns(repo.Root);
                // The finder ran because the page list changed. If it picked the same pages, the judge's inputs are the same: no second AI call (D18).
                var judged = PrepareJudge(set, repo, index, section, doc, sectionId, picked, "ai", runs[Key(doc, sectionId)] as JsonObject, runs, usageAll);
                if (judged.Status == "current") UpdateRun(repo.Root, Key(doc, sectionId), r => MergeInto(r, usageAll));
                return judged;
            }
            case "judge":
            {
                var candidates = state["candidates"]!.AsArray().Select(c => (string)c!["page"]!).ToHashSet(StringComparer.Ordinal);
                var output = PageChecks.Parse(Read(work, "judge.json"), errors);
                if (errors.Count == 0) errors.AddRange(SchemaRegistry.Embedded.Validate(byRequirement ? "requirement-judge-output.v1.json" : "link-judge-output.v1.json", output!.ToJsonString()).Select(e => $"Schema: {e}"));
                var links = new List<(JsonObject Link, List<string> Errors)>();
                var missing = new List<string>();
                if (errors.Count == 0)
                {
                    var requirements = byRequirement ? Requirements(repo.Root, doc, section) ?? [] : null;
                    links = [.. output!["links"]!.AsArray().OfType<JsonObject>().Select(l =>
                    {
                        var own = requirements is null ? [] : FromRequirement(l, requirements);
                        return (l, (List<string>)[.. own, .. LinkChecks.Check(l, section, candidates, index, nodes)]);
                    })];
                    if (requirements is not null)
                        missing = [.. requirements.Select(r => (string)r["id"]!).Where(id => !links.Any(l => (string?)l.Link["requirement"] == id)).Select(id => $"{id} has no link: give every requirement one, or a gap")];
                }
                state["unanswered"] = new JsonArray([.. missing.Select(m => (JsonNode)m.Split(' ')[0])]);
                var linkErrors = links.SelectMany((l, i) => l.Errors.Select(e => $"links[{i}]: {e}")).Concat(missing).ToList();
                if (errors.Count > 0 || linkErrors.Count > 0)
                {
                    if (Retry(repo.Root, work, state, [.. errors, .. linkErrors], judge, "judge.md", "judge.json") is { } retry) return retry;
                    if (errors.Count > 0) return Failed(repo.Root, work, state, errors);
                }
                var kept = links.Where(l => l.Errors.Count == 0).Select(l => l.Link).ToList();
                state["rejected"] = links.Count - kept.Count;
                state["links"] = new JsonArray([.. kept.Select(l => (JsonNode)l.DeepClone())]);
                var agrees = kept.Where(l => (string)l["state"]! == "agree").ToList();
                if (agrees.Count == 0) return Finish(repo.Root, work, state, doc, section, [], repo.Config.Fsd.Findings);
                Stage(repo.Root, state, "confirm", "the judge's output passed the code check");
                state["retryUsed"] = false; // the checker gets its own one retry
                Save(work, state);
                File.WriteAllText(Path.Combine(work, "checker.md"), CheckerBundle(section, doc, candidates, index, agrees, byRequirement ? Requirements(repo.Root, doc, section) : null));
                return Step(repo.Root, work, Checker, "checker.md", "checker.json", "Section, pages and the links to check");
            }
            case "confirm":
            {
                var candidates = state["candidates"]!.AsArray().Select(c => (string)c!["page"]!).ToHashSet(StringComparer.Ordinal);
                var output = PageChecks.Parse(Read(work, "checker.json"), errors);
                if (errors.Count == 0) errors.AddRange(SchemaRegistry.Embedded.Validate("agree-checker-output.v1.json", output!.ToJsonString()).Select(e => $"Schema: {e}"));
                if (errors.Count > 0 && Retry(repo.Root, work, state, errors, Checker, "checker.md", "checker.json") is { } retry) return retry;
                var checks = errors.Count > 0 ? [] : output!["checks"]!.AsArray().OfType<JsonObject>().ToList();
                return Finish(repo.Root, work, state, doc, section, checks, repo.Config.Fsd.Findings, candidates, index, nodes);
            }
            default:
                throw new StangentException("nothing_to_check", $"{doc} §{sectionId}'s run is {stage}. Run `stangent fsd prepare` to start again.", ExitCodes.Usage);
        }
    }

    /// <summary>Turns the checked links into trace entries. "agree" links take the checker's verdict: confirmed, or
    /// disputed and turned into the finding it raised (when its quotes pass), reviewed one by one (D62).</summary>
    static RunStep Finish(string root, string work, JsonObject state, string doc, JsonObject section, List<JsonObject> checks, bool findings,
        HashSet<string>? candidates = null, SpecIndex? index = null, IReadOnlySet<string>? nodes = null)
    {
        var entries = new List<JsonObject>();
        var k = 0;
        var disputed = 0;
        var unchecked_ = 0;
        foreach (var link in state["links"]!.AsArray().OfType<JsonObject>())
        {
            var s = (string)link["state"]!;
            string? check = null;
            var final = link;
            if (s == "agree")
            {
                var id = $"K{++k}";
                var verdict = checks.FirstOrDefault(c => (string)c["link"]! == id);
                if (verdict is null) { unchecked_++; final = (JsonObject)link.DeepClone(); final["note"] = $"{(string?)link["note"]} (the second check gave no answer: not confirmed, reviewed one by one)"; }
                else if ((string)verdict["verdict"]! == "confirm") check = "confirmed";
                else if (verdict["state"] is null) { check = "disputed"; disputed++; final = (JsonObject)link.DeepClone(); final["note"] = $"Disputed by the second check without saying what it is instead: {(string)verdict["note"]!}"; }
                else
                {
                    check = "disputed";
                    disputed++;
                    var raised = new JsonObject
                    {
                        ["state"] = verdict["state"]!.DeepClone(), ["fsdQuote"] = (verdict["fsdQuote"] ?? link["fsdQuote"])?.DeepClone(),
                        ["page"] = (verdict["page"] ?? link["page"])?.DeepClone(), ["statement"] = (verdict["statement"] ?? link["statement"])?.DeepClone(),
                        ["pageQuote"] = (verdict["pageQuote"] ?? link["pageQuote"])?.DeepClone(), ["note"] = $"Disputed by the second check: {(string)verdict["note"]!}",
                    };
                    // A requirement's link keeps its requirement and the quote code took from it.
                    if (link["requirement"] is not null) { raised["requirement"] = link["requirement"]!.DeepClone(); raised["fsdQuote"] = link["fsdQuote"]!.DeepClone(); }
                    if ((string)raised["state"]! == "gap") { raised.Remove("pageQuote"); raised.Remove("statement"); }
                    // A requirement's link can't become undocumented: that means the FSD doesn't say it, and this requirement quotes the FSD.
                    var forRequirement = link["requirement"] is not null && (string)raised["state"]! == "undocumented";
                    if (!forRequirement && LinkChecks.Check(raised, section, candidates!, index!, nodes).Count == 0) final = raised;
                    else final = (JsonObject)link.DeepClone(); // its evidence failed: stays "agree", disputed, never bulk-approved
                }
            }
            entries.Add(Entry((string)final["state"]!, doc, section, (string?)final["fsdQuote"], (string?)final["page"], (string?)final["statement"],
                (string?)final["pageQuote"], "judge", check, (string?)final["note"], (string?)link["requirement"]));
        }
        // With findings off (D57, D68) only links are recorded: a disputed "agree" that became a finding is left out too.
        if (!findings) entries = [.. entries.Where(e => (string)e["state"]! == "agree")];
        ApplyEntries(root, doc, (string)state["section"]!, entries);
        var key = Key(doc, (string)state["section"]!);
        UpdateRun(root, key, r =>
        {
            r["inputs"] = state["inputs"]!.DeepClone();
            r["candidates"] = state["candidates"]!.DeepClone();
            r["candidatesBy"] = state["candidatesBy"]!.DeepClone();
            MergeInto(r, state["usage"] as JsonObject);
            r["retries"] = (int?)state["retries"] ?? 0;
            r["rejected"] = (int?)state["rejected"] ?? 0;
            r["links"] = entries.Count;
            r["disputed"] = disputed;
            r["unchecked"] = unchecked_;
            // A requirement the judge left out even after its retry has no entry at all: say so, or it reads as nothing to see.
            if (state["unanswered"] is JsonArray { Count: > 0 } unanswered) r["unanswered"] = unanswered.DeepClone();
            else r.Remove("unanswered");
        });
        Stage(root, state, "done", "recorded");
        Save(work, state);
        return new RunStep("recorded", "none");
    }

    internal static RunStep? Retry(string root, string work, JsonObject state, List<string> errors, string agent, string bundle, string output)
    {
        if ((bool)state["retryUsed"]!) return null;
        state["retryUsed"] = true;
        state["retries"] = ((int?)state["retries"] ?? 0) + 1;
        Save(work, state);
        var sb = new StringBuilder("# The code check failed\n\nYour output wasn't accepted. Fix every problem below and write the whole output again ");
        sb.Append("(one more failure and the failing parts are rejected).\n\n");
        foreach (var e in errors.Take(40)) sb.Append($"- {e}\n");
        File.WriteAllText(Path.Combine(work, "feedback.md"), sb.ToString());
        // A subagent that wrote nothing (a timeout, a refusal) still gets its retry.
        if (File.Exists(Path.Combine(work, output))) File.Move(Path.Combine(work, output), Path.Combine(work, "previous-" + output), overwrite: true);
        else File.WriteAllText(Path.Combine(work, "previous-" + output), "(no output)");
        var step = Step(root, work, agent, bundle, output, "Input");
        return step with
        {
            Status = "retry", Errors = errors,
            Prompt = step.Prompt + $"\nYour previous attempt: {Rel(root, Path.Combine(work, "previous-" + output))}\nWhat to fix: {Rel(root, Path.Combine(work, "feedback.md"))}",
        };
    }

    static RunStep Failed(string root, string work, JsonObject state, List<string> errors)
    {
        Stage(root, state, "failed", "failed its checks");
        Save(work, state);
        UpdateRun(root, Key((string)state["doc"]!, (string)state["section"]!), r => { r["failed"] = string.Join("; ", errors.Take(3)); MergeInto(r, state["usage"] as JsonObject); });
        return new RunStep("failed", "none", Errors: errors);
    }

    internal static RunStep Step(string root, string work, string agent, string bundle, string output, string label) =>
        new(agent is Judge or RequirementJudge ? "judge" : agent == Finder ? "find" : "confirm", agent is Judge or RequirementJudge ? "judge" : agent == Finder ? "find" : "confirm", agent,
            $"{label}: {Rel(root, Path.Combine(work, bundle))}\nWrite your JSON to: {Rel(root, Path.Combine(work, output))}", Rel(root, Path.Combine(work, output)));

    public static JsonObject Entry(string state, string doc, JsonObject? section, string? fsdQuote, string? page, string? statement, string? pageQuote,
        string by, string? check, string? note, string? requirement = null)
    {
        JsonNode? fsd = section is null ? null : new JsonObject { ["doc"] = doc, ["section"] = (string)section["id"]!, ["title"] = (string)section["title"]! };
        if (fsd is JsonObject known && (string?)section!["permanentId"] is { } sectionId) known["sectionId"] = sectionId;
        if (fsd is JsonObject r && requirement is not null) r["requirement"] = requirement;
        if (fsd is JsonObject f && fsdQuote is not null) { f["quote"] = fsdQuote; f["quoteHash"] = TraceFile.QuoteHash(fsdQuote); }
        JsonNode? pg = page is null ? null : new JsonObject { ["id"] = page };
        if (pg is JsonObject p && statement is not null) p["statement"] = statement;
        if (pg is JsonObject p2 && pageQuote is not null) { p2["quote"] = pageQuote; p2["quoteHash"] = TraceFile.QuoteHash(pageQuote); }
        var e = new JsonObject
        {
            ["id"] = TraceFile.EntryId(state, section is null ? null : doc, (string?)section?["id"], page, fsdQuote, pageQuote, requirement),
            ["state"] = state, ["fsd"] = fsd, ["page"] = pg, ["by"] = by,
        };
        if (check is not null) e["check"] = check;
        if (note is not null) e["note"] = note;
        e["decision"] = null;
        e["decided_by"] = null;
        return e;
    }

    /// <summary>Replaces a section's entries; an entry that comes back with the same id keeps its decision.</summary>
    static readonly StateActor Who = StateActor.Code("stangent fsd");

    static void Stage(string root, JsonObject state, string to, string reason) =>
        StateChanges.Apply(root, state, StateKinds.LinkRun, Key((string)state["doc"]!, (string)state["section"]!), to, Who, reason);

    public static void ApplyEntries(string root, string doc, string? sectionId, IEnumerable<JsonObject> entries, Func<JsonObject, bool>? replaces = null)
    {
        using var _ = Lock(root);
        var trace = TraceFile.Load(root);
        var old = trace["entries"]!.AsArray().OfType<JsonObject>().ToList();
        replaces ??= e => (string?)e["fsd"]?["doc"] == doc && (string?)e["fsd"]?["section"] == sectionId;
        var keepDecisions = old.Where(replaces).ToDictionary(e => (string)e["id"]!, e => e);
        var list = old.Where(e => !replaces(e)).Select(e => (JsonNode)e.DeepClone()).ToList();
        foreach (var e in entries.DistinctBy(e => (string)e["id"]!))
        {
            var copy = (JsonObject)e.DeepClone();
            if (keepDecisions.TryGetValue((string)e["id"]!, out var was))
            {
                copy["decision"] = was["decision"]?.DeepClone();
                copy["decided_by"] = was["decided_by"]?.DeepClone();
                // What it was decided on stays too, or a re-judged link would tie an old decision to new words.
                if (was["decided_on"] is not null) copy["decided_on"] = was["decided_on"]!.DeepClone();
                // The same link is the same finding: its id, its grade and its evidence stay with it (D118).
                foreach (var carried in TraceFile.FindingFields.Where(f => was[f] is not null)) copy[carried] = was[carried]!.DeepClone();
            }
            list.Add(copy);
        }
        // A decided link that didn't come back took its decision with it: its id is the hash of its quotes (D58).
        var back = entries.Select(e => (string)e["id"]!).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, gone) in keepDecisions.Where(k => k.Value["decision"] is not null && !back.Contains(k.Key)))
            StateChanges.Move(root, StateKinds.TraceDecision, id, (string?)gone["decision"], "undecided", Who, "a new build didn't find this link again");
        trace["entries"] = new JsonArray([.. list]);
        TraceFile.Save(root, trace);
    }

    public static JsonObject LoadRuns(string root) => PageRun.LoadJson(RunsFile(root)) as JsonObject ?? new JsonObject();

    internal static void UpdateRun(string root, string key, Action<JsonObject> change)
    {
        using var _ = Lock(root);
        var runs = LoadRuns(root);
        var run = runs[key] as JsonObject ?? new JsonObject();
        runs[key] = null;
        var copy = (JsonObject)run.DeepClone();
        change(copy);
        runs[key] = copy;
        Quotes.WriteAtomic(RunsFile(root), CanonicalJson.Serialize(runs));
    }

    internal static void MergeInto(JsonObject run, JsonObject? usage)
    {
        if (usage is null) return;
        foreach (var (role, u) in usage) run[role] = u?.DeepClone();
    }

    internal static FileStream Lock(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".stangent", "fsd"));
        var path = Path.Combine(root, ".stangent", "fsd", "trace.lock");
        for (var i = 0; ; i++)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 600) { Thread.Sleep(50); }
        }
    }

    static string Fresh(string root, string doc, string section)
    {
        var work = Work(root, doc, section);
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        return work;
    }

    static void Save(string work, JsonObject state) => File.WriteAllText(Path.Combine(work, "state.json"), CanonicalJson.Serialize(state));
    static string Read(string work, string file) => File.Exists(Path.Combine(work, file)) ? File.ReadAllText(Path.Combine(work, file)) : "";
    internal static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>The first sentence of a section, as it appears in its text: the quote for a gap found by code.</summary>
    public static string FirstSentence(string text)
    {
        var line = text.Split('\n').Select(l => l.Trim()).First(l => l.Length > 0).TrimStart('-', ' ', '|');
        var stop = line.IndexOf(". ", StringComparison.Ordinal);
        return (stop > 0 ? line[..(stop + 1)] : line).Trim().TrimEnd('|').Trim();
    }

    // ---- bundles ----

    internal static string SectionBlock(JsonObject section, string doc) =>
        $"# The FSD section\n\n- document: `{doc}`\n- section: `{(string)section["id"]!}` {(string)section["title"]!}\n\nIts text, exactly:\n\n````text\n{(string)section["text"]!}\n````\n";

    static string FinderBundle(JsonObject section, PageSet set, SpecIndex index)
    {
        var sb = new StringBuilder("# Instructions\n\n").Append(Bundles.Instructions("candidate-finder.md"));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText("candidate-finder-output.v1.json").TrimEnd('\n')).Append("\n```\n\n");
        sb.Append(SectionBlock(section, "")).Append("\n# The pages\n\n| id | kind | title | from the code |\n|---|---|---|---|\n");
        foreach (var p in set.Pages.Where(p => index.Pages.ContainsKey(p.Id)))
            sb.Append($"| `{p.Id}` | {p.Kind} | {(string?)index.Pages[p.Id]["title"]} | {p.Title.Replace("|", "\\|")} |\n");
        return sb.ToString();
    }

    static string JudgeBundle(JsonObject section, string doc, IEnumerable<string> pages, SpecIndex index)
    {
        var sb = new StringBuilder("# Instructions\n\n").Append(Bundles.Instructions("link-judge.md"));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText("link-judge-output.v1.json").TrimEnd('\n')).Append("\n```\n\n");
        sb.Append(SectionBlock(section, doc)).Append(PagesBlock(pages, index));
        return sb.ToString();
    }

    static string RequirementJudgeBundle(JsonObject section, string doc, IEnumerable<string> pages, SpecIndex index, List<JsonObject> requirements)
    {
        var sb = new StringBuilder("# Instructions\n\n").Append(Bundles.Instructions("requirement-judge.md"));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText("requirement-judge-output.v1.json").TrimEnd('\n')).Append("\n```\n\n");
        sb.Append(SectionBlock(section, doc)).Append("\n# The requirements\n\n");
        foreach (var r in requirements) sb.Append(RequirementLine(r));
        sb.Append(PagesBlock(pages, index));
        return sb.ToString();
    }

    static string RequirementLine(JsonObject r) => $"- **{(string)r["id"]!}** {(string)r["statement"]!} (from: \"{FirstQuote(r)}\")\n";

    /// <summary>A requirement's link takes its FSD quote from the requirement, so the judge can't misquote it.</summary>
    static List<string> FromRequirement(JsonObject link, List<JsonObject> requirements)
    {
        var state = (string)link["state"]!;
        var id = (string?)link["requirement"];
        if (state == "undocumented") return id is null ? [] : ["an undocumented link has no requirement"];
        if (id is null) return [$"a {state} link needs its requirement"];
        var requirement = requirements.FirstOrDefault(r => (string)r["id"]! == id);
        if (requirement is null) return [$"{id} isn't one of this section's requirements"];
        link["fsdQuote"] = FirstQuote(requirement);
        return [];
    }

    static string CheckerBundle(JsonObject section, string doc, IEnumerable<string> pages, SpecIndex index, List<JsonObject> agrees, List<JsonObject>? requirements = null)
    {
        var sb = new StringBuilder("# Instructions\n\n").Append(Bundles.Instructions(requirements is null ? "agree-checker.md" : "requirement-agree-checker.md"));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText("agree-checker-output.v1.json").TrimEnd('\n')).Append("\n```\n\n");
        sb.Append(SectionBlock(section, doc)).Append(PagesBlock(pages, index)).Append("\n# The links to check\n\n");
        for (var i = 0; i < agrees.Count; i++)
        {
            var requirement = requirements?.FirstOrDefault(r => (string)r["id"]! == (string?)agrees[i]["requirement"]);
            var left = requirement is null ? $"FSD \"{(string?)agrees[i]["fsdQuote"]}\"" : $"{(string)requirement["id"]!} \"{(string)requirement["statement"]!}\" (from FSD \"{FirstQuote(requirement)}\")";
            sb.Append($"- **K{i + 1}**: {left} ↔ `{(string?)agrees[i]["page"]}` {(string?)agrees[i]["statement"]} \"{(string?)agrees[i]["pageQuote"]}\"\n");
        }
        return sb.ToString();
    }

    static string PagesBlock(IEnumerable<string> pages, SpecIndex index)
    {
        var sb = new StringBuilder("\n# The candidate pages\n\nGenerated from the code; every statement cites the code it comes from.\n");
        foreach (var id in pages.Order(StringComparer.Ordinal))
        {
            var p = index.Pages[id];
            sb.Append($"\n## `{id}`\n\n{(string)p["kind"]!}: {(string)p["title"]!} (from the code: {(string)p["codeTitle"]!})\n");
            foreach (var (section, sid, s) in PageChecks.Statements(p))
                sb.Append($"- **{sid}** ({section}) {(string)s["text"]!}\n");
        }
        return sb.ToString();
    }
}

/// <summary>The code checks on one link (D53 step 3): quotes word for word, both ends exist, cited symbols in the graph.</summary>
public static class LinkChecks
{
    public static List<string> Check(JsonObject link, JsonObject section, IReadOnlySet<string> candidates, SpecIndex index, IReadOnlySet<string>? nodes)
    {
        var errors = new List<string>();
        var state = (string)link["state"]!;
        var fsdQuote = (string?)link["fsdQuote"];
        var page = (string?)link["page"];
        var statement = (string?)link["statement"];
        var pageQuote = (string?)link["pageQuote"];
        if (state is "agree" or "conflict" or "gap" && fsdQuote is null) errors.Add($"a {state} link needs fsdQuote");
        if (state is "agree" or "conflict" or "undocumented" && (page is null || statement is null || pageQuote is null))
            errors.Add($"a {state} link needs page, statement and pageQuote");
        if (fsdQuote is not null && !Quotes.InText(fsdQuote, (string)section["text"]!))
            errors.Add($"fsdQuote \"{Short(fsdQuote)}\" isn't a sentence from the section's text, word for word (at least 4 words, or a whole table cell or list item)");
        if (page is not null && !candidates.Contains(page)) errors.Add($"page `{page}` isn't one of the candidate pages");
        else if (page is not null && statement is not null)
        {
            var s = index.Statement(page, statement);
            if (s is null) errors.Add($"page `{page}` has no statement {statement}");
            else
            {
                if (pageQuote is not null && !Quotes.InText(pageQuote, (string)s["text"]!))
                    errors.Add($"pageQuote \"{Short(pageQuote)}\" isn't from statement {statement} of `{page}` word for word (at least 4 words, or the whole statement)");
                if (nodes is not null)
                    foreach (var cite in s["cites"]!.AsArray().Select(c => (string)c!).Where(c => !nodes.Contains(c)))
                        errors.Add($"statement {statement} of `{page}` cites `{cite}`, which isn't in the code graph");
            }
        }
        return errors;
    }

    static string Short(string s) => s.Length > 80 ? s[..80] + "…" : s;
}

/// <summary>Findings code makes without AI (D53): screens no FSD section links to are undocumented.</summary>
public static class Undocumented
{
    /// <param name="scope">Screens the FSD claims to cover (routes or page ids); null: every screen.</param>
    /// <param name="findings">False (the default, D68): undocumented screens are findings, so none are listed and any
    /// listed before are removed.</param>
    public static int Apply(string root, PageSet set, IReadOnlyCollection<string>? scope, bool findings = true)
    {
        var trace = TraceFile.Load(root);
        var linked = trace["entries"]!.AsArray().OfType<JsonObject>()
            .Where(e => !((string?)e["by"] == "code" && (string?)e["state"] == "undocumented" && e["fsd"] is null))
            .Where(e => !TraceFile.IsRemovedSection((string?)e["fsd"]?["section"]))
            .Select(e => (string?)e["page"]?["id"]).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var index = SpecIndex.Load(root);
        var screens = set.Pages.Where(p => p.Kind == PageKind.Screen && index.Pages.ContainsKey(p.Id))
            .Where(p => scope is null || scope.Any(s => InScope(s, p.Id["page:".Length..]) || s == p.Id))
            .Where(p => !linked.Contains(p.Id)).ToList();
        List<JsonObject> entries = !findings ? [] : [.. screens.Select(p => LinkRun.Entry("undocumented", "", null, null, p.Id, null, null, "code", null,
            "no FSD section links to this screen"))];
        LinkRun.ApplyEntries(root, "", null, entries, e => (string?)e["by"] == "code" && (string?)e["state"] == "undocumented" && e["fsd"] is null);
        return entries.Count;
    }

    /// <summary>A scope entry is a route or a route prefix by whole segments: <c>/settings</c> covers
    /// <c>/settings/tags</c>; <c>/</c> covers only the root route.</summary>
    public static bool InScope(string entry, string route) =>
        route == entry || (entry != "/" && route.StartsWith(entry.TrimEnd('/') + "/", StringComparison.Ordinal));
}

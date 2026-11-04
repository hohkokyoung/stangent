using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Requirements;

/// <summary>
/// One FSD section's AI steps (D99, D60), as a small state machine like <see cref="Fsd.LinkRun"/>: the writer returns
/// items, code checks every quote and that every sentence is cited or background (one retry); a fresh checker says
/// whether each item says more than its quotes; disputed items go back to the writer once and are checked again; what's
/// still disputed is recorded as disputed, never hidden. An unchanged input hash means no AI call (D18).
/// </summary>
public static class RequirementRun
{
    public const string Writer = "stangent-requirement-writer";
    public const string Checker = "stangent-requirement-checker";

    public static string Key(string doc, string section) => $"{doc}#{section}";

    internal static readonly StateActor Who = StateActor.Code("stangent req");

    static void Stage(string root, JsonObject state, string to, string reason) =>
        StateChanges.Apply(root, state, StateKinds.ReqRun, Key((string)state["doc"]!, (string)state["section"]!), to, Who, reason);
    static string Work(string root, string doc, string section) =>
        Path.Combine(root, ".stangent", "req", doc, PageList.Slug(section, Key(doc, section)));

    public static RunStep Prepare(StangentRepo repo, string doc, string sectionId, bool retryFailed = false)
    {
        var index = SpecIndex.Load(repo.Root);
        var section = index.Sections.GetValueOrDefault((doc, sectionId))
            ?? throw new StangentException("not_found", $"No section {sectionId} in FSD {doc}. Run `stangent fsd import` first.", ExitCodes.NotFound);
        if (Sentences.Of((string)section["text"]!).Count == 0) return new RunStep("skipped", "none", Errors: ["the section has no text of its own"]);
        var bundle = WriterBundle(section, doc);
        var inputs = Inputs(bundle);
        var run = LoadRuns(repo.Root)[Key(doc, sectionId)] as JsonObject;
        if ((string?)run?["inputs"] == inputs)
        {
            if (run!["failed"] is { } failed)
            {
                if (!retryFailed) return new RunStep("failed", "none", Errors: [$"failed with these same inputs: {(string)failed!} (--retry-failed tries again)"]);
            }
            else if ((int?)run["unchecked"] is not > 0) return new RunStep("current", "none");
            else if (!retryFailed) return new RunStep("current", "none", Errors: [$"{(int)run["unchecked"]!} item(s) have no answer from the checker (--retry-failed tries again)"]);
        }
        AiPolicy.EnsureAllowed(repo.Config, "req prepare");
        var work = Fresh(repo.Root, doc, sectionId);
        File.WriteAllText(Path.Combine(work, "writer.md"), bundle);
        var fresh = new JsonObject { ["doc"] = doc, ["section"] = sectionId, ["inputs"] = inputs, ["textHash"] = CanonicalJson.Hash((string)section["text"]!), ["retryUsed"] = false, ["usage"] = new JsonObject() };
        Stage(repo.Root, fresh, "write", "prepared");
        Save(work, fresh);
        return Step(repo.Root, work, Writer, "writer.md", "writer.json", "FSD section");
    }

    static string Inputs(string bundle) => CanonicalJson.Hash(bundle + Bundles.Instructions("requirement-checker.md") + SchemaRegistry.EmbeddedText("requirement-checker-output.v1.json"));

    /// <summary>What <see cref="Prepare"/> hashes for this section (D117: a section that only changed its number keeps its paid items).</summary>
    internal static string InputsOf(JsonObject section, string doc) => Inputs(WriterBundle(section, doc));

    public static RunStep Check(StangentRepo repo, string doc, string sectionId, Usage? usage)
    {
        var work = Work(repo.Root, doc, sectionId);
        if (PageRun.LoadJson(Path.Combine(work, "state.json")) is not JsonObject state)
            throw new StangentException("not_prepared", $"No run for {doc} §{sectionId}. Run `stangent req prepare` first.", ExitCodes.Usage);
        var section = SpecIndex.Load(repo.Root).Sections.GetValueOrDefault((doc, sectionId))
            ?? throw new StangentException("not_found", $"No section {sectionId} in FSD {doc} any more. Run `stangent req prepare` for what's there now.", ExitCodes.NotFound);
        // Imported again while this ran: its quotes were checked against text that's gone.
        if ((string?)state["textHash"] != CanonicalJson.Hash((string)section["text"]!))
            throw new StangentException("section_changed", $"{doc} §{sectionId}'s text changed during this run. Run `stangent req prepare` to start again.", ExitCodes.Usage);
        var stage = (string)state["stage"]!;
        var role = stage is "write" or "fix" ? "writer" : "checker";
        var usageAll = state["usage"] as JsonObject ?? new JsonObject();
        usageAll[role] = Usage.FromJson(usageAll[role]).Add(usage ?? Usage.None).ToJson();
        state["usage"] = usageAll;

        switch (stage)
        {
            case "write":
            {
                var errors = new List<string>();
                var output = PageChecks.Parse(Read(work, "writer.json"), errors);
                if (errors.Count == 0 && output is null) errors.Add("The output is empty.");
                if (errors.Count == 0) errors.AddRange(RequirementChecks.Check(output!, section));
                if (errors.Count > 0)
                    return Retry(repo.Root, work, state, errors, Writer, "writer.md", "writer.json", "FSD section") ?? Failed(repo.Root, work, state, errors);
                state["output"] = output!.DeepClone();
                return ToChecker(repo.Root, work, state, section, doc, "confirm");
            }
            case "fix":
            {
                var errors = new List<string>();
                var output = PageChecks.Parse(Read(work, "writer.json"), errors);
                if (errors.Count == 0 && output is null) errors.Add("The output is empty.");
                if (errors.Count == 0) errors.AddRange(RequirementChecks.Check(output!, section));
                // The fix broke the code checks: keep what passed before, with the checker's disputes on it.
                if (errors.Count > 0) return Finish(repo.Root, work, state, section, doc, state["checks"]!.AsArray().OfType<JsonObject>().ToList());
                state["output"] = output!.DeepClone();
                return ToChecker(repo.Root, work, state, section, doc, "reconfirm");
            }
            case "confirm" or "reconfirm":
            {
                var errors = new List<string>();
                var output = PageChecks.Parse(Read(work, "checker.json"), errors);
                var items = RequirementChecks.Items((JsonObject)state["output"]!);
                if (errors.Count == 0 && output is null) errors.Add("The output is empty.");
                var itemQuotes = RequirementChecks.ItemQuotes((JsonObject)state["output"]!);
                if (errors.Count == 0) errors.AddRange(RequirementChecks.CheckVerdicts(output!, items, itemQuotes));
                var raw = output is not null && SchemaRegistry.Embedded.Validate("requirement-checker-output.v1.json", output.ToJsonString()).Count == 0
                    ? output["checks"]!.AsArray().OfType<JsonObject>().ToList() : [];
                if (errors.Count > 0)
                {
                    // A dispute it couldn't back up, then dropped when asked to: not a confirmation. Read loosely, since
                    // an answer that broke the schema can still hold a dispute.
                    var flagged = state["flagged"] as JsonArray ?? [];
                    foreach (var item in RequirementChecks.DisputedLoosely(output)) flagged.Add(item);
                    state["flagged"] = flagged.DeepClone();
                    if (Retry(repo.Root, work, state, errors, Checker, "checker.md", "checker.json", "Section and items") is { } retry) return retry;
                }
                var checks = RequirementChecks.FinalVerdicts(raw, items, itemQuotes, state["flagged"]?.AsArray().Select(f => (string)f!).ToHashSet(StringComparer.Ordinal) ?? []);
                var disputed = checks.Where(c => (string)c["verdict"]! != "ok").ToList();
                if (stage == "reconfirm" || disputed.Count == 0) return Finish(repo.Root, work, state, section, doc, checks);
                // Back to the writer once, with what the checker found.
                Stage(repo.Root, state, "fix", "the checker disputed items");
                state["retryUsed"] = true;
                state["checks"] = new JsonArray([.. checks.Select(c => (JsonNode)c.DeepClone())]);
                state["fixes"] = disputed.Count;
                Save(work, state);
                var sb = new StringBuilder("# The checker disputed these\n\nA fresh agent looked at your items. Fix each one: say only what the quotes say, ");
                sb.Append("raise a question for the BA instead, give a sentence that holds a rule an item of its own, or remove a quote the item doesn't use. Return the whole output again.\n\n");
                foreach (var c in disputed)
                    sb.Append((string)c["verdict"]! switch
                    {
                        "needs-item" => $"- `{(string)c["item"]!}` was marked background, but needs an item: {(string)c["note"]!}\n",
                        "padded" => $"- `{(string)c["item"]!}` cites \"{(string?)c["extra"]}\", which it says nothing about: {(string)c["note"]!}\n",
                        _ => $"- `{(string)c["item"]!}` goes too far with \"{(string?)c["extra"]}\": {(string)c["note"]!}\n",
                    });
                File.WriteAllText(Path.Combine(work, "feedback.md"), sb.ToString());
                File.Move(Path.Combine(work, "writer.json"), Path.Combine(work, "previous-writer.json"), overwrite: true);
                var step = Step(repo.Root, work, Writer, "writer.md", "writer.json", "FSD section");
                return step with { Prompt = step.Prompt + $"\nYour previous attempt: {Rel(repo.Root, Path.Combine(work, "previous-writer.json"))}\nWhat to fix: {Rel(repo.Root, Path.Combine(work, "feedback.md"))}" };
            }
            default:
                throw new StangentException("nothing_to_check", $"{doc} §{sectionId}'s run is {stage}. Run `stangent req prepare` to start again.", ExitCodes.Usage);
        }
    }

    static RunStep ToChecker(string root, string work, JsonObject state, JsonObject section, string doc, string stage)
    {
        Stage(root, state, stage, "the writer's output passed the code check");
        state["retryUsed"] = false; // the checker gets its own one retry
        state.Remove("flagged");
        // Never read an earlier round's verdicts: a checker that writes nothing leaves its items unchecked.
        File.Delete(Path.Combine(work, "checker.json"));
        Save(work, state);
        File.WriteAllText(Path.Combine(work, "checker.md"), CheckerBundle(section, doc, (JsonObject)state["output"]!));
        return Step(root, work, Checker, "checker.md", "checker.json", "Section and items");
    }

    static RunStep Finish(string root, string work, JsonObject state, JsonObject section, string doc, List<JsonObject> checks)
    {
        var output = (JsonObject)state["output"]!;
        var verdicts = checks.ToDictionary(c => (string)c["item"]!, c => c);
        using (RequirementFile.Lock(root))
        {
            RequirementRecord.Apply(root, doc, section, output, verdicts);
            var key = Key(doc, (string)section["id"]!);
            var items = RequirementChecks.Items(output);
            UpdateRun(root, key, r =>
            {
                r.Remove("failed");
                r["inputs"] = state["inputs"]!.DeepClone();
                MergeInto(r, state["usage"] as JsonObject);
                AddSpent(r, state["usage"] as JsonObject);
                r["retries"] = (int?)state["retries"] ?? 0;
                r["fixes"] = (int?)state["fixes"] ?? 0;
                r["requirements"] = output["requirements"]!.AsArray().Count;
                r["background"] = output["background"]!.AsArray().Count;
                r["questions"] = output["questions"]!.AsArray().Count;
                r["disputed"] = verdicts.Values.Count(v => (string)v["verdict"]! != "ok");
                r["unchecked"] = items.Keys.Count(k => !verdicts.ContainsKey(k));
            });
        }
        Stage(root, state, "done", "recorded");
        Save(work, state);
        var noAnswer = RequirementChecks.Items(output).Keys.Count(k => !verdicts.ContainsKey(k));
        return new RunStep("recorded", "none", Errors: noAnswer > 0 ? [$"{noAnswer} item(s) have no answer from the checker (--retry-failed tries again)"] : null);
    }

    static RunStep? Retry(string root, string work, JsonObject state, List<string> errors, string agent, string bundle, string output, string label)
    {
        if ((bool)state["retryUsed"]!) return null;
        state["retryUsed"] = true;
        state["retries"] = ((int?)state["retries"] ?? 0) + 1;
        Save(work, state);
        var then = agent == Writer ? "this section fails" : "the items in question stay unchecked";
        var sb = new StringBuilder($"# The code check failed\n\nYour output wasn't accepted. Fix every problem below and write the whole output again (one more failure and {then}).\n\n");
        foreach (var e in errors.Take(40)) sb.Append($"- {e}\n");
        File.WriteAllText(Path.Combine(work, "feedback.md"), sb.ToString());
        // A subagent that wrote nothing (a timeout, a refusal) still gets its retry.
        if (File.Exists(Path.Combine(work, output))) File.Move(Path.Combine(work, output), Path.Combine(work, "previous-" + output), overwrite: true);
        else File.WriteAllText(Path.Combine(work, "previous-" + output), "(no output)");
        var step = Step(root, work, agent, bundle, output, label);
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
        using (RequirementFile.Lock(root))
            UpdateRun(root, Key((string)state["doc"]!, (string)state["section"]!), r =>
            {
                r["inputs"] = state["inputs"]!.DeepClone();
                r["failed"] = string.Join("; ", errors.Take(3));
                r["retries"] = (int?)state["retries"] ?? 0;
                MergeInto(r, state["usage"] as JsonObject);
                AddSpent(r, state["usage"] as JsonObject);
            });
        return new RunStep("failed", "none", Errors: errors);
    }

    static RunStep Step(string root, string work, string agent, string bundle, string output, string label) =>
        new(agent == Writer ? "write" : "confirm", agent == Writer ? "write" : "confirm", agent,
            $"{label}: {Rel(root, Path.Combine(work, bundle))}\nWrite your JSON to: {Rel(root, Path.Combine(work, output))}", Rel(root, Path.Combine(work, output)));

    public static JsonObject LoadRuns(string root) => PageRun.LoadJson(RequirementFile.RunsFile(root)) as JsonObject ?? new JsonObject();

    static void UpdateRun(string root, string key, Action<JsonObject> change)
    {
        var runs = LoadRuns(root);
        var copy = (JsonObject)(runs[key] as JsonObject ?? new JsonObject()).DeepClone();
        change(copy);
        runs[key] = copy;
        Quotes.WriteAtomic(RequirementFile.RunsFile(root), CanonicalJson.Serialize(runs));
    }

    static void MergeInto(JsonObject run, JsonObject? usage)
    {
        if (usage is null) return;
        foreach (var (role, u) in usage) run[role] = u?.DeepClone();
    }

    /// <summary>What a section has cost across every attempt, failed ones too: the role fields hold the last run only.</summary>
    static void AddSpent(JsonObject run, JsonObject? usage) =>
        run["spentUsd"] = Math.Round(((double?)run["spentUsd"] ?? 0) + (usage?.Sum(u => Usage.FromJson(u.Value).CostUsd) ?? 0), 6);

    static string Fresh(string root, string doc, string section)
    {
        var work = Work(root, doc, section);
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        return work;
    }

    static void Save(string work, JsonObject state) => File.WriteAllText(Path.Combine(work, "state.json"), CanonicalJson.Serialize(state));
    static string Read(string work, string file) => File.Exists(Path.Combine(work, file)) ? File.ReadAllText(Path.Combine(work, file)) : "";
    static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    // ---- bundles ----

    static string SectionBlock(JsonObject section, string doc) =>
        $"# The FSD section\n\n- document: `{doc}`\n- section: `{(string)section["id"]!}` {(string)section["title"]!}\n\nIts text, exactly:\n\n````text\n{(string)section["text"]!}\n````\n";

    static string WriterBundle(JsonObject section, string doc)
    {
        var sb = new StringBuilder("# Instructions\n\n").Append(Bundles.Instructions("requirement-writer.md"));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText("requirement-writer-output.v1.json").TrimEnd('\n')).Append("\n```\n\n");
        sb.Append(SectionBlock(section, doc)).Append("\n# The sentences\n\nCode looks for each of these in your quotes or your background list:\n\n");
        foreach (var s in Sentences.Of((string)section["text"]!)) sb.Append($"- {s.Text}\n");
        return sb.ToString();
    }

    static string CheckerBundle(JsonObject section, string doc, JsonObject output)
    {
        var sb = new StringBuilder("# Instructions\n\n").Append(Bundles.Instructions("requirement-checker.md"));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText("requirement-checker-output.v1.json").TrimEnd('\n')).Append("\n```\n\n");
        sb.Append(SectionBlock(section, doc)).Append("\n# The items to check\n\n");
        foreach (var r in output["requirements"]!.AsArray().OfType<JsonObject>())
        {
            sb.Append($"- **{(string)r["key"]!}** ({(string)r["kind"]!}) {(string)r["title"]!}: {(string)r["statement"]!}\n  quotes: {QuoteList(r)}\n");
            foreach (var a in r["acceptance"]!.AsArray().OfType<JsonObject>())
                sb.Append($"  - acceptance: {(string)a["text"]!}\n    quotes: {QuoteList(a)}\n");
        }
        foreach (var d in output["definitions"]!.AsArray().OfType<JsonObject>())
            sb.Append($"- **{(string)d["key"]!}** ({(string)d["kind"]!}) {(string)d["name"]!}: {(string)d["text"]!}\n  quotes: {QuoteList(d)}\n");
        foreach (var j in output["journeys"]!.AsArray().OfType<JsonObject>())
        {
            sb.Append($"- **{(string)j["key"]!}** (journey) {(string)j["title"]!}: {Steps(j)}\n  quotes: {QuoteList(j)}\n");
            foreach (var s in j["scenarios"]!.AsArray().OfType<JsonObject>())
                sb.Append($"- **{(string)s["key"]!}** ({(string)s["kind"]!} path of {(string)j["key"]!}) {(string)s["title"]!}: {Steps(s)}\n  quotes: {QuoteList(s)}\n");
        }
        var background = output["background"]!.AsArray().Select(b => (string)b!).ToList();
        if (background.Count > 0)
        {
            sb.Append("\n# The sentences marked background\n\nThese got no item. Check them too.\n\n");
            for (var i = 0; i < background.Count; i++) sb.Append($"- **b{i + 1}** {background[i]}\n");
        }
        var questions = output["questions"]!.AsArray().OfType<JsonObject>().ToList();
        if (questions.Count > 0)
        {
            sb.Append("\n# Questions raised for the BA\n\nDon't check these. An item must not state as settled what one of these leaves open.\n\n");
            foreach (var q in questions) sb.Append($"- {(string)q["text"]!}\n");
        }
        return sb.ToString();
    }

    static string QuoteList(JsonObject o) => string.Join(" / ", o["quotes"]!.AsArray().Select(q => $"\"{(string)q!}\""));
    static string Steps(JsonObject o) => string.Join(" → ", o["steps"]!.AsArray().Select(s => (string)s!));
}

/// <summary>The code checks on one section's items (D99): schema, keys, every quote word for word, every sentence cited
/// or background.</summary>
public static class RequirementChecks
{
    public static List<string> Check(JsonNode node, JsonObject section)
    {
        var errors = SchemaRegistry.Embedded.Validate("requirement-writer-output.v1.json", node.ToJsonString()).Select(e => $"Schema: {e}").ToList();
        if (errors.Count > 0) return errors;
        var output = node.AsObject();
        var text = (string)section["text"]!;
        var reqs = output["requirements"]!.AsArray().OfType<JsonObject>().ToList();
        var defs = output["definitions"]!.AsArray().OfType<JsonObject>().ToList();
        var journeys = output["journeys"]!.AsArray().OfType<JsonObject>().ToList();
        var scenarios = journeys.SelectMany(j => j["scenarios"]!.AsArray().OfType<JsonObject>()).ToList();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in reqs.Concat(defs).Concat(journeys).Concat(scenarios).Select(o => (string)o["key"]!))
            if (!keys.Add(k)) errors.Add($"the key `{k}` is used twice");
        foreach (var r in reqs)
            foreach (var d in r["refers"]?.AsArray().Select(x => (string)x!) ?? [])
                if (!defs.Any(x => (string)x["key"]! == d)) errors.Add($"`{(string)r["key"]!}` refers to `{d}`, which isn't a definition here");
        foreach (var j in journeys.Concat(scenarios))
            foreach (var s in j["steps"]!.AsArray().Select(x => (string)x!))
                if (!reqs.Any(x => (string)x["key"]! == s)) errors.Add($"`{(string)j["key"]!}` has the step `{s}`, which isn't a requirement here");

        foreach (var same in reqs.GroupBy(r => Quotes.Normalize((string)r["statement"]!)).Where(g => g.Count() > 1))
            errors.Add($"{string.Join(" and ", same.Select(r => $"`{(string)r["key"]!}`"))} say the same thing: one fact in one place");

        // Only what the BA approves and agents read counts as citing: journeys and questions add no statements (D99).
        var citing = new List<string>();
        void Quoted(string where, JsonObject o, bool cites = true)
        {
            foreach (var q in o["quotes"]!.AsArray().Select(x => (string)x!))
            {
                if (cites) citing.Add(q);
                if (!Quotes.InText(q, text) && !Sentences.Of(text).Any(s => Sentences.IsExactly(q, s))) errors.Add($"{where}: the quote \"{Short(q)}\" isn't from the section's text, word for word (at least 4 words, or a whole sentence, table cell or list item)");
            }
        }
        foreach (var r in reqs)
        {
            Quoted($"`{(string)r["key"]!}`", r);
            foreach (var a in r["acceptance"]!.AsArray().OfType<JsonObject>()) Quoted($"`{(string)r["key"]!}`'s criterion", a);
        }
        foreach (var d in defs) Quoted($"`{(string)d["key"]!}`", d);
        foreach (var o in journeys.Concat(scenarios)) Quoted($"`{(string)o["key"]!}`", o, cites: false);
        foreach (var q in output["questions"]!.AsArray().OfType<JsonObject>()) Quoted("a question", q, cites: false);

        var sentences = Sentences.Of(text);
        var background = output["background"]!.AsArray().Select(x => (string)x!).ToList();
        foreach (var b in background)
            if (!sentences.Any(s => Sentences.IsExactly(b, s))) errors.Add($"background \"{Short(b)}\" isn't one of the sentences listed, copied exactly");
        foreach (var s in sentences)
            if (!Sentences.Cited(s, citing) && !background.Any(b => Sentences.IsExactly(b, s)))
                errors.Add($"this sentence isn't fully covered by the quotes of a requirement, criterion or definition that says what it says, and isn't in background: \"{Short(s.Text)}\" (quote it whole; never add a quote to an item it has nothing to do with)");
        return errors;
    }

    /// <summary>Every item the checker answers for, by key, with the words of its own it can dispute.</summary>
    public static Dictionary<string, string> Items(JsonObject output)
    {
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in output["requirements"]!.AsArray().OfType<JsonObject>())
            items[(string)r["key"]!] = string.Join(' ', [(string)r["kind"]!, (string)r["title"]!, (string)r["statement"]!, .. r["acceptance"]!.AsArray().Select(a => (string)a!["text"]!)]);
        foreach (var d in output["definitions"]!.AsArray().OfType<JsonObject>())
            items[(string)d["key"]!] = $"{(string)d["kind"]!} {(string)d["name"]!} {(string)d["text"]!}";
        foreach (var j in output["journeys"]!.AsArray().OfType<JsonObject>())
        {
            items[(string)j["key"]!] = $"{(string)j["title"]!} {string.Join(" → ", j["steps"]!.AsArray().Select(x => (string)x!))}";
            foreach (var s in j["scenarios"]!.AsArray().OfType<JsonObject>())
                items[(string)s["key"]!] = $"{(string)s["kind"]!} path of {(string)j["key"]!} {(string)s["title"]!} {string.Join(" → ", s["steps"]!.AsArray().Select(x => (string)x!))}";
        }
        var i = 0;
        foreach (var b in output["background"]!.AsArray()) items[$"b{++i}"] = (string)b!;
        return items;
    }

    /// <summary>Each item's quotes, as a dispute of "padded" must name one of them.</summary>
    public static Dictionary<string, string[]> ItemQuotes(JsonObject output)
    {
        var quotes = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(JsonObject o) => quotes[(string)o["key"]!] = [.. o["quotes"]!.AsArray().Select(q => Quotes.Normalize((string)q!))];
        foreach (var o in output["requirements"]!.AsArray().OfType<JsonObject>().Concat(output["definitions"]!.AsArray().OfType<JsonObject>())) Add(o);
        foreach (var j in output["journeys"]!.AsArray().OfType<JsonObject>())
        {
            Add(j);
            foreach (var s in j["scenarios"]!.AsArray().OfType<JsonObject>()) Add(s);
        }
        return quotes;
    }

    /// <summary>The items an answer disputes, read without trusting its shape.</summary>
    public static IEnumerable<string> DisputedLoosely(JsonNode? output)
    {
        if (output is not JsonObject o || o["checks"] is not JsonArray checks) yield break;
        foreach (var c in checks.OfType<JsonObject>())
            if (c["item"] is JsonValue item && item.TryGetValue<string>(out var id) && !(c["verdict"] is JsonValue v && v.TryGetValue<string>(out var verdict) && verdict == "ok"))
                yield return id;
    }

    public static List<string> CheckVerdicts(JsonNode output, Dictionary<string, string> items, Dictionary<string, string[]> quotes)
    {
        var errors = SchemaRegistry.Embedded.Validate("requirement-checker-output.v1.json", output.ToJsonString()).Select(e => $"Schema: {e}").ToList();
        if (errors.Count > 0) return errors;
        var checks = output["checks"]!.AsArray().OfType<JsonObject>().ToList();
        foreach (var c in checks)
        {
            var item = (string)c["item"]!;
            if (!items.ContainsKey(item)) errors.Add($"`{item}` isn't an item here");
            else if (!UsableVerdict(c, items, quotes))
                errors.Add(item.StartsWith('b') != ((string)c["verdict"]! == "needs-item")
                    ? $"`{item}`: `needs-item` is for background sentences (b1, b2…), `says-more` and `padded` for items"
                    : (string)c["verdict"]! == "padded"
                        ? $"`{item}`: `extra` must copy the quote the item doesn't use, exactly"
                        : $"`{item}`: `extra` must copy the item's own words that go too far, exactly");
        }
        foreach (var twice in checks.GroupBy(c => (string)c["item"]!).Where(g => g.Count() > 1)) errors.Add($"`{twice.Key}` is answered twice: answer every item once");
        foreach (var k in items.Keys.Where(k => !checks.Any(c => (string)c["item"]! == k))) errors.Add($"`{k}` has no answer");
        return errors;
    }

    /// <summary>A dispute must point at the item's own words (D18); one that doesn't leaves the item unchecked.</summary>
    public static bool UsableVerdict(JsonObject check, Dictionary<string, string> items, Dictionary<string, string[]> quotes)
    {
        var item = (string)check["item"]!;
        if (!items.TryGetValue(item, out var text)) return false;
        var verdict = (string)check["verdict"]!;
        if (verdict == "ok") return true;
        if (verdict == "needs-item") return item.StartsWith('b');
        if (item.StartsWith('b') || Quotes.Normalize((string?)check["extra"] ?? "") is not { Length: > 0 } extra) return false;
        return verdict == "padded" ? quotes.GetValueOrDefault(item)?.Contains(extra, StringComparer.Ordinal) == true : Quotes.Normalize(text).Contains(extra, StringComparison.Ordinal);
    }

    /// <summary>One verdict per item at most. A dispute outranks an "ok" for the same item; a dispute that couldn't be
    /// backed up, now or on the try before, leaves the item unchecked, never confirmed.</summary>
    public static List<JsonObject> FinalVerdicts(List<JsonObject> raw, Dictionary<string, string> items, Dictionary<string, string[]> quotes, IReadOnlySet<string> flagged)
    {
        var final = new List<JsonObject>();
        foreach (var g in raw.Where(c => items.ContainsKey((string)c["item"]!)).GroupBy(c => (string)c["item"]!))
        {
            var disputes = g.Where(c => (string)c["verdict"]! != "ok").ToList();
            if (disputes.Count > 0)
            {
                if (disputes.FirstOrDefault(c => UsableVerdict(c, items, quotes)) is { } usable) final.Add(usable);
            }
            else if (!flagged.Contains(g.Key)) final.Add(g.First());
        }
        return final;
    }

    static string Short(string s) => s.Length > 80 ? s[..80] + "…" : s;
}

/// <summary>Turns a section's checked output into its feature file (D99): an item whose quotes are unchanged keeps its
/// id, and its approval only if nothing about it changed; ids whose quotes are gone are retired, never reused.</summary>
public static class RequirementRecord
{
    public static void Apply(string root, string doc, JsonObject section, JsonObject output, Dictionary<string, JsonObject> verdicts)
    {
        var sectionId = (string)section["id"]!;
        var feature = RequirementFile.Ensure(root, doc, section, SpecIndex.Load(root));
        JsonObject Quote(string text) => new() { ["doc"] = doc, ["section"] = sectionId, ["text"] = Quotes.Normalize(text), ["hash"] = TraceFile.QuoteHash(text) };
        JsonArray QuotesOf(JsonObject o) => new([.. o["quotes"]!.AsArray().Select(q => (JsonNode)Quote((string)q!))]);
        void Checked(JsonObject item, string key)
        {
            var v = verdicts.GetValueOrDefault(key);
            item["check"] = v is null ? "unchecked" : (string)v["verdict"]! == "ok" ? "confirmed" : "disputed";
            item["note"] = v is null ? "the checker gave no usable answer"
                : (string)v["verdict"]! == "ok" ? null
                : (string)v["verdict"]! == "needs-item" ? $"Marked background, but the checker says it needs an item: {(string)v["note"]!}"
                : (string)v["verdict"]! == "padded" ? $"Cites a quote it says nothing about (\"{(string?)v["extra"]}\"): {(string)v["note"]!}"
                : $"Goes further than its quotes (\"{(string?)v["extra"]}\"): {(string)v["note"]!}";
        }

        bool IsOk(string key) => verdicts.TryGetValue(key, out var v) && (string)v["verdict"]! == "ok";

        var retired = feature["retired"]!.AsArray().Select(r => r!.DeepClone()).ToList();
        void Retire(Matcher m, string titleKey)
        {
            foreach (var old in m.Unclaimed()) retired.Add(new JsonObject { ["id"] = (string)old["id"]!, ["title"] = (string)old[titleKey]! });
        }

        // Definitions first: requirements refer to them by id.
        var oldDefs = new Matcher(feature["definitions"]!.AsArray().OfType<JsonObject>(), d => (string)d["text"]!, output["definitions"]!, d => (string)d!["text"]!);
        var defIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var defs = new JsonArray();
        foreach (var d in output["definitions"]!.AsArray().OfType<JsonObject>())
        {
            var was = oldDefs.For(d);
            var id = (string?)was?["id"] ?? RequirementFile.NextId(root, "D");
            defIds[(string)d["key"]!] = id;
            var item = new JsonObject { ["id"] = id, ["kind"] = (string)d["kind"]!, ["name"] = (string)d["name"]!, ["text"] = (string)d["text"]!, ["quotes"] = QuotesOf(d) };
            Checked(item, (string)d["key"]!);
            defs.Add((JsonNode)item);
        }

        var oldReqs = new Matcher(feature["requirements"]!.AsArray().OfType<JsonObject>(), r => (string)r["statement"]!, output["requirements"]!, r => (string)r!["statement"]!);
        var reqIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var reqs = new JsonArray();
        foreach (var r in output["requirements"]!.AsArray().OfType<JsonObject>())
        {
            var was = oldReqs.For(r);
            var id = (string?)was?["id"] ?? RequirementFile.NextId(root, "R");
            reqIds[(string)r["key"]!] = id;
            var acceptance = new JsonArray([.. r["acceptance"]!.AsArray().OfType<JsonObject>().Select(a => (JsonNode)new JsonObject { ["text"] = (string)a["text"]!, ["quotes"] = QuotesOf(a) })]);
            var quotes = QuotesOf(r);
            var refers = new JsonArray([.. (r["refers"]?.AsArray().Select(x => defIds[(string)x!]) ?? []).Select(x => (JsonNode)x)]);
            var usedWords = new JsonArray([.. (r["refers"]?.AsArray().Select(x => (string)x!) ?? [])
                .Select(k => (JsonNode)(string)output["definitions"]!.AsArray().First(d => (string)d!["key"]! == k)!["text"]!)]);
            var hash = ItemHash((string)r["kind"]!, (string)r["title"]!, (string)r["statement"]!, quotes, acceptance, usedWords);
            // Editing a requirement, its quoted text or a definition it uses drops it back to proposed (D11). So does any
            // answer from the checker but "ok": an approved item must have been checked as it stands.
            var keeps = (string?)was?["hash"] == hash && IsOk((string)r["key"]!);
            var item = new JsonObject
            {
                ["id"] = id, ["title"] = (string)r["title"]!, ["kind"] = (string)r["kind"]!, ["statement"] = (string)r["statement"]!, ["quotes"] = quotes, ["acceptance"] = acceptance,
                ["refers"] = refers,
            };
            StateChanges.Apply(root, item, StateKinds.Requirement, id, keeps ? (string?)was?["status"] ?? "proposed" : "proposed", RequirementRun.Who,
                was is null ? "first written" : "changed, or the checker didn't answer ok", previous: was);
            Checked(item, (string)r["key"]!);
            item["hash"] = hash;
            if (keeps && (string?)was?["decided_by"] is { } by) item["decided_by"] = by;
            reqs.Add((JsonNode)item);
        }

        var newScenarios = new JsonArray([.. output["journeys"]!.AsArray().SelectMany(j => j!["scenarios"]!.AsArray()).Select(x => x!.DeepClone())]);
        var oldJourneys = new Matcher(feature["journeys"]!.AsArray().OfType<JsonObject>(), j => (string)j["title"]!, output["journeys"]!, j => (string)j!["title"]!);
        var oldScenarios = new Matcher(feature["journeys"]!.AsArray().OfType<JsonObject>().SelectMany(j => j["scenarios"]!.AsArray().OfType<JsonObject>()), s => (string)s["title"]!, newScenarios, s => (string)s!["title"]!);
        JsonArray StepIds(JsonObject o) => new([.. o["steps"]!.AsArray().Select(s => (JsonNode)reqIds[(string)s!])]);
        var journeys = new JsonArray();
        foreach (var j in output["journeys"]!.AsArray().OfType<JsonObject>())
        {
            var id = (string?)oldJourneys.For(j)?["id"] ?? RequirementFile.NextId(root, "J");
            var scenarios = new JsonArray();
            foreach (var s in j["scenarios"]!.AsArray().OfType<JsonObject>())
            {
                var sid = (string?)oldScenarios.For(s)?["id"] ?? RequirementFile.NextId(root, "S");
                var sc = new JsonObject { ["id"] = sid, ["kind"] = (string)s["kind"]!, ["title"] = (string)s["title"]!, ["steps"] = StepIds(s), ["quotes"] = QuotesOf(s) };
                Checked(sc, (string)s["key"]!);
                scenarios.Add((JsonNode)sc);
            }
            var item = new JsonObject { ["id"] = id, ["title"] = (string)j["title"]!, ["steps"] = StepIds(j), ["quotes"] = QuotesOf(j) };
            Checked(item, (string)j["key"]!);
            item["scenarios"] = scenarios;
            journeys.Add((JsonNode)item);
        }

        Retire(oldDefs, "name");
        foreach (var gone in oldReqs.Unclaimed())
            StateChanges.Move(root, StateKinds.Requirement, (string)gone["id"]!, (string?)gone["status"], "retired", RequirementRun.Who, "its quotes are gone");
        Retire(oldReqs, "title");
        Retire(oldJourneys, "title");
        Retire(oldScenarios, "title");

        var updated = new JsonObject
        {
            ["version"] = 1, ["id"] = (string)feature["id"]!, ["doc"] = doc, ["section"] = sectionId, ["title"] = (string)section["title"]!,
            ["parent"] = (string?)feature["parent"], ["children"] = feature["children"]!.DeepClone(), ["textHash"] = CanonicalJson.Hash((string)section["text"]!),
            ["requirements"] = reqs, ["definitions"] = defs, ["journeys"] = journeys,
            ["background"] = new JsonArray([.. output["background"]!.AsArray().Select((b, i) =>
            {
                var item = new JsonObject { ["text"] = Quotes.Normalize((string)b!) };
                Checked(item, $"b{i + 1}");
                return (JsonNode)item;
            })]),
            ["retired"] = new JsonArray([.. retired]),
        };
        RequirementFile.Save(root, updated);

        // A question that comes back keeps its id and any answer; one that doesn't is closed, never deleted.
        var questions = RequirementFile.Questions(root, doc);
        var kept = questions.Where(q => (string)q["section"]! != sectionId).ToList();
        var earlier = questions.Where(q => (string)q["section"]! == sectionId).ToList();
        foreach (var q in output["questions"]!.AsArray().OfType<JsonObject>())
        {
            // The same words on the same text only: a BA's answer must never move to another question.
            var was = earlier.FirstOrDefault(o => Quotes.Normalize((string)o["text"]!) == Quotes.Normalize((string)q["text"]!)
                && o["quotes"]!.AsArray().Select(x => (string)x!["text"]!).Order(StringComparer.Ordinal).SequenceEqual(q["quotes"]!.AsArray().Select(x => Quotes.Normalize((string)x!)).Order(StringComparer.Ordinal)));
            if (was is not null) earlier.Remove(was);
            var id = (string?)was?["id"] ?? RequirementFile.NextId(root, "Q");
            var question = new JsonObject { ["id"] = id, ["section"] = sectionId, ["text"] = (string)q["text"]!, ["quotes"] = QuotesOf(q) };
            StateChanges.Apply(root, question, StateKinds.Question, id, (string?)was?["status"] is null or "dropped" ? "open" : (string)was!["status"]!, RequirementRun.Who,
                was is null ? "first raised" : "raised again", previous: was);
            question["answer"] = was?["answer"]?.DeepClone();
            kept.Add(question);
        }
        foreach (var gone in earlier)
        {
            var copy = (JsonObject)gone.DeepClone();
            StateChanges.Apply(root, copy, StateKinds.Question, (string)copy["id"]!, "dropped", RequirementRun.Who, "not raised any more");
            kept.Add(copy);
        }
        RequirementFile.SaveQuestions(root, doc, kept.OrderBy(q => (string)q["id"]!, StringComparer.Ordinal));
        RequirementFile.WriteViews(root, doc);
    }

    public static string ItemHash(string kind, string title, string statement, JsonArray quotes, JsonArray acceptance, JsonArray refers) => CanonicalJson.Hash(string.Join("\u0001",
        [kind, Quotes.Normalize(title), Quotes.Normalize(statement), .. quotes.Select(q => (string)q!["hash"]!),
         .. acceptance.SelectMany(a => new[] { Quotes.Normalize((string)a!["text"]!) }.Concat(a!["quotes"]!.AsArray().Select(q => (string)q!["hash"]!))),
         .. refers.Select(r => (string)r!)]));

    /// <summary>New items against old ones, decided up front. The same quotes and the same words keep an id. The same
    /// quotes with other words keep it only when there's one old and one new item on those quotes: with several, a guess
    /// could swap two ids and carry links and approvals to the wrong rule, so those get new ids. Telling reworded items
    /// apart properly is reconcile's job (D99).</summary>
    sealed class Matcher
    {
        readonly List<JsonObject> _old;
        readonly HashSet<JsonObject> _claimed = [];
        readonly Dictionary<JsonNode, JsonObject> _for = [];

        static string Key(IEnumerable<string> quotes) => string.Join("\u0001", quotes.Select(Quotes.Normalize).Order(StringComparer.Ordinal));

        public Matcher(IEnumerable<JsonObject> old, Func<JsonObject, string> words, JsonNode newItems, Func<JsonNode, string> newWords)
        {
            _old = [.. old];
            var fresh = newItems.AsArray().Select(n => n!).ToList();
            string OldKey(JsonObject o) => Key(o["quotes"]!.AsArray().Select(q => (string)q!["text"]!));
            string NewKey(JsonNode n) => Key(n["quotes"]!.AsArray().Select(q => (string)q!));
            foreach (var n in fresh.ToList())
                if (_old.FirstOrDefault(o => !_claimed.Contains(o) && OldKey(o) == NewKey(n) && Quotes.Normalize(words(o)) == Quotes.Normalize(newWords(n))) is { } same)
                {
                    _claimed.Add(same);
                    _for[n] = same;
                    fresh.Remove(n);
                }
            foreach (var group in fresh.GroupBy(NewKey))
            {
                var free = _old.Where(o => !_claimed.Contains(o) && OldKey(o) == group.Key).ToList();
                if (group.Count() != 1 || free.Count != 1) continue;
                _claimed.Add(free[0]);
                _for[group.First()] = free[0];
            }
        }

        public JsonObject? For(JsonNode item) => _for.GetValueOrDefault(item) ?? _for.FirstOrDefault(p => JsonNode.DeepEquals(p.Key, item)).Value;

        public IEnumerable<JsonObject> Unclaimed() => _old.Where(o => !_claimed.Contains(o));
    }
}

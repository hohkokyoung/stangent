using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;
using Stangent.Spec.Requirements;

namespace Stangent.Tickets;

/// <summary>
/// <c>stangent ticket match</c> (D99), after the file finder: which approved requirement says what should happen here, if any. Code limits the
/// candidates to requirements linked through page statements to the pages at the ticket's starting points; requirements no
/// page statement covers come second, by title only, and read "no code found for this rule". An AI picks one or none,
/// quoting both sides, and code checks the quotes. A fresh AI, without the pick's reasons, says whether that requirement
/// decides this situation; if not, there's no match. It only ever reads "suggested": a person confirms.
/// </summary>
public static class TicketMatch
{
    public const string Schema = "ticket-match.v1.json";
    public const string Matcher = "stangent-requirement-matcher";
    public const string Checker = "stangent-match-checker";
    public static readonly StepSpec Step = new("match", Matcher, "requirement-matcher-output.v1.json", "requirement-matcher.md");
    const string CheckerSchema = "match-checker-output.v1.json";

    static string Result(TicketPaths paths) => Path.Combine(paths.Dir, "match.json");
    public static JsonObject? Load(string root, string key) =>
        PageRun.LoadJson(Result(new TicketPaths(root, key))) as JsonObject;

    sealed record Candidate(JsonObject Requirement, string Doc, string Section, string SectionTitle, bool Linked, bool AtTheCode);

    /// <summary>Usable requirements for this ticket, the linked ones first. Only approved ones judge anything (D99); a benchmark's
    /// setting treats proposed ones as approved, and says so. One the checker disputed or didn't answer for never counts.</summary>
    static List<Candidate> Candidates(StangentRepo repo, TicketContext ctx, JsonObject points)
    {
        var skip = repo.Config.Fsd.SkipApproval;
        var list = points["points"]!.AsArray().Select(p => StartingPoint.FromJson(p!)).ToList();
        var files = list.Where(p => p.Kind == StartingPoint.File).Select(p => p.Id).ToList();
        // Every page at the starting points, not the bundle's: its size cap drops pages, and a rule shouldn't vanish with them.
        var pages = TicketBundle.Explaining(ctx, files).Keys.Concat(list.Where(p => p.Kind == StartingPoint.Page).Select(p => p.Id)).ToHashSet(StringComparer.Ordinal);
        var at = files.SelectMany(ctx.Analyzer.InFile).Select(n => n.Id).Concat(list.Where(p => p.Kind == StartingPoint.Symbol).Select(p => p.Id)).ToHashSet(StringComparer.Ordinal);
        var links = ctx.Trace.Where(e => (string?)e["fsd"]?["requirement"] is not null && IsLink(e))
            .GroupBy(e => (string)e["fsd"]!["requirement"]!).ToDictionary(g => g.Key, g => g.ToList());
        var found = new List<Candidate>();
        foreach (var doc in RequirementFile.Docs(repo.Root))
            foreach (var f in RequirementFile.Features(repo.Root, doc))
            {
                // Built from text that has since changed: its sentences may not be in the FSD any more.
                if (!ctx.Index.Sections.TryGetValue((doc, (string)f["section"]!), out var section) || (string?)f["textHash"] != CanonicalJson.Hash((string)section["text"]!)) continue;
                foreach (var r in f["requirements"]!.AsArray().OfType<JsonObject>())
                {
                    if ((string)r["check"]! != "confirmed" || !((string)r["status"]! == "approved" || skip)) continue;
                    var id = (string)r["id"]!;
                    var mine = links.GetValueOrDefault(id);
                    // Linked somewhere else: its code isn't at this ticket's starting points, so it doesn't cover this.
                    if (mine is not null && !mine.Any(e => ((string?)e["page"]?["id"] is { } p && pages.Contains(p)) || Cites(ctx, e, at))) continue;
                    var direct = mine?.Any(e => Cites(ctx, e, at)) == true;
                    found.Add(new Candidate(r, doc, (string)f["section"]!, (string)f["title"]!, mine is not null, direct));
                }
            }
        return [.. found.OrderByDescending(c => c.Linked).ThenByDescending(c => c.AtTheCode).ThenBy(c => (string)c.Requirement["id"]!, StringComparer.Ordinal)];
    }

    static bool Cites(TicketContext ctx, JsonObject link, HashSet<string> atFiles)
    {
        if ((string?)link["page"]?["id"] is not { } page || (string?)link["page"]?["statement"] is not { } statement) return false;
        return ctx.Index.Statement(page, statement)?["cites"]?.AsArray().Any(c => atFiles.Contains((string)c!)) == true;
    }

    static string TicketText(JsonObject pulled)
    {
        var sb = new StringBuilder();
        sb.Append($"- key: {(string)pulled["key"]!}\n- type: {(string?)pulled["ticket"]!["type"]}\n- summary: {(string?)pulled["ticket"]!["summary"]}\n\n");
        sb.Append("Description:\n\n````text\n").Append((string?)pulled["description"]).Append("\n````\n");
        foreach (var c in pulled["comments"]!.AsArray().OfType<JsonObject>()) sb.Append($"\nComment:\n\n````text\n{(string?)c["text"]}\n````\n");
        return sb.ToString();
    }

    /// <summary>What a ticket quote can come from: its summary, description and comments.</summary>
    static string QuotableText(JsonObject pulled) => string.Join('\n',
        [(string?)pulled["ticket"]!["summary"] ?? "", (string?)pulled["description"] ?? "", .. pulled["comments"]!.AsArray().Select(c => (string?)c!["text"] ?? "")]);

    public static RunStep Prepare(StangentRepo repo, TicketContext ctx, string key)
    {
        var paths = new TicketPaths(repo.Root, key);
        var pulled = TicketPull.LoadOrFail(repo.Root, key);
        var points = TicketPoints.LoadOrFail(repo.Root, key);
        var finder = new JsonObject { ["status"] = (string?)points["ai"]?["status"], ["points"] = points["points"]!.AsArray().Count };
        var candidates = Candidates(repo, ctx, points);
        var input = MatcherInput(pulled, candidates);
        // Everything the fresh check may read about a candidate, not only what the matcher reads.
        var hash = CanonicalJson.Hash(input + AiStep.Instructions("match-checker.md") + SchemaRegistry.EmbeddedText(CheckerSchema)
            + string.Join('\n', candidates.Select(c => (string)c.Requirement["hash"]!)));
        var earlier = Load(repo.Root, key);
        if ((string?)earlier?["inputs"] == hash && (string?)earlier?["outcome"] != "failed") return new RunStep("current", "none");
        var counts = new JsonObject { ["linked"] = candidates.Count(c => c.Linked), ["noCode"] = candidates.Count(c => !c.Linked) };
        // With no search for its code, a "none" would only mean nobody looked.
        if ((string?)finder["status"] is "failed" or "needed")
        {
            var why = $"the file finder's status is {(string)finder["status"]!}, so nothing says where this ticket's code is";
            Save(paths, Record(repo, key, hash, "failed", counts, new JsonArray(), finder, Usage.None, Usage.None, why));
            return new RunStep("failed", "none", Errors: [why]);
        }
        if (candidates.Count == 0)
        {
            Save(paths, Record(repo, key, hash, "none", counts, new JsonArray(), finder, Usage.None, Usage.None, why: "no approved requirement to match"));
            return new RunStep("recorded", "none");
        }
        AiPolicy.EnsureAllowed(repo.Config, "ticket match");
        return AiStep.Start(paths, Step, input, hash, new JsonObject
        {
            ["stage"] = "pick", ["candidates"] = new JsonArray([.. candidates.Select(c => (JsonNode)(string)c.Requirement["id"]!)]), ["counts"] = counts, ["finder"] = finder,
        });
    }

    public static RunStep Check(StangentRepo repo, TicketContext ctx, string key, Usage? usage)
    {
        var paths = new TicketPaths(repo.Root, key);
        var state = AiStep.State(paths, Step);
        var pulled = TicketPull.LoadOrFail(repo.Root, key);
        return (string)state["stage"]! switch
        {
            "pick" => CheckPick(repo, paths, pulled, state, usage),
            "confirm" => CheckConfirm(repo, paths, pulled, state, usage),
            var other => throw new StangentException("nothing_to_check", $"{key}'s match is {other}. Run `stangent ticket match {key}` to start again.", ExitCodes.Usage),
        };
    }

    static RunStep CheckPick(StangentRepo repo, TicketPaths paths, JsonObject pulled, JsonObject state, Usage? usage)
    {
        var (output, errors) = AiStep.Read(paths, Step, state, usage);
        var ids = state["candidates"]!.AsArray().Select(c => (string)c!).ToHashSet(StringComparer.Ordinal);
        JsonObject? requirement = null;
        if (output is not null && (string?)output["pick"] is { } pick)
        {
            requirement = ids.Contains(pick) ? (JsonObject?)RequirementFile.Show(repo.Root, pick)?["item"] : null;
            if (requirement is null) errors.Add($"`{pick}` isn't one of the candidates. Copy an id exactly, or pick null.");
            else errors.AddRange(QuoteErrors(output, pulled, requirement));
        }
        if (errors.Count > 0)
            return AiStep.Retry(paths, Step, state, errors) ?? Failed(repo, paths, state, errors);
        if (requirement is null)
        {
            Save(paths, Record(repo, paths.Key, (string)state["inputs"]!, "none", state["counts"]!, state["candidates"]!, state["finder"]!, Usage.FromJson(state["usage"]), Usage.None, why: (string?)output!["why"]));
            MarkDone(paths, state);
            return new RunStep("recorded", "none");
        }
        // The fresh check, told nothing of why it was picked.
        state["stage"] = "confirm";
        state["retryUsed"] = false;
        state["pick"] = (string)requirement["id"]!;
        state["pickOutput"] = output!.DeepClone();
        state["matcherUsage"] = state["usage"]!.DeepClone();
        state["usage"] = Usage.None.ToJson();
        var work = AiStep.Work(paths, Step);
        File.WriteAllText(Path.Combine(work, "check-input.md"), CheckerInput(pulled, requirement));
        File.Delete(Path.Combine(work, Step.OutputFile));
        AiStep.SaveState(paths, Step, state);
        return ToChecker(paths, "prepared");
    }

    static RunStep ToChecker(TicketPaths paths, string status)
    {
        var work = AiStep.Work(paths, Step);
        return new RunStep(status, "confirm", Checker,
            $"Input: {paths.Relative(Path.Combine(work, "check-input.md"))}\nWrite your JSON to: {paths.Relative(Path.Combine(work, Step.OutputFile))}",
            paths.Relative(Path.Combine(work, Step.OutputFile)));
    }

    static RunStep CheckConfirm(StangentRepo repo, TicketPaths paths, JsonObject pulled, JsonObject state, Usage? usage)
    {
        var checkerStep = Step with { Schema = CheckerSchema, Agent = Checker };
        var (output, errors) = AiStep.Read(paths, checkerStep, state, usage);
        var id = (string)state["pick"]!;
        var requirement = (JsonObject?)RequirementFile.Show(repo.Root, id)?["item"]
            ?? throw new StangentException("not_found", $"{id} doesn't exist any more. Run `stangent ticket match {paths.Key}` to start again.", ExitCodes.NotFound);
        if (output is not null && (bool)output["decides"]!)
        {
            errors.AddRange(QuoteErrors(output, pulled, requirement));
            if (output["reporter"] is null) errors.Add("`reporter` is needed when the requirement decides it.");
        }
        if (errors.Count > 0)
        {
            if ((bool?)state["retryUsed"] != true)
            {
                var retry = AiStep.Retry(paths, checkerStep, state, errors)!;
                var plain = ToChecker(paths, "retry");
                return retry with { Agent = Checker, Action = "confirm", Prompt = plain.Prompt + retry.Prompt![retry.Prompt!.IndexOf("\nYour previous attempt", StringComparison.Ordinal)..] };
            }
            return Failed(repo, paths, state, errors);
        }
        var matcher = Usage.FromJson(state["matcherUsage"]);
        var checker = Usage.FromJson(state["usage"]);
        var pick = state["pickOutput"]!.AsObject();
        JsonObject record;
        if (!(bool)output!["decides"]!)
        {
            record = Record(repo, paths.Key, (string)state["inputs"]!, "none", state["counts"]!, state["candidates"]!, state["finder"]!, matcher, checker, why: $"{(string)pick["why"]!} The fresh check disagreed: {(string)output["why"]!}");
            record["refused"] = new JsonObject { ["pick"] = id, ["why"] = (string)output["why"]! };
        }
        else
        {
            record = Record(repo, paths.Key, (string)state["inputs"]!, "suggested", state["counts"]!, state["candidates"]!, state["finder"]!, matcher, checker, why: (string)pick["why"]!);
            var codeFound = Linked(repo.Root, id);
            record["requirement"] = new JsonObject
            {
                ["id"] = id, ["title"] = (string)requirement["title"]!, ["statement"] = (string)requirement["statement"]!,
                ["quotes"] = requirement["quotes"]!.DeepClone(), ["status"] = (string)requirement["status"]!, ["codeFound"] = codeFound,
            };
            record["reading"] = codeFound ? $"suggested: {id}" : $"suggested: {id} (no code found for this rule)";
            record["reporter"] = (string)output["reporter"]!;
            record["quotes"] = new JsonObject { ["ticket"] = (string)output["ticketQuote"]!, ["requirement"] = (string)output["requirementQuote"]! };
        }
        Save(paths, record);
        MarkDone(paths, state);
        return new RunStep("recorded", "none");
    }

    static bool Linked(string root, string id) => RequirementFile.Links(root, id).Any(IsLink);

    /// <summary>A link that shows the code: a conflict, or an agree the second check didn't dispute.</summary>
    static bool IsLink(JsonObject entry) => (string)entry["state"]! == "conflict" || ((string)entry["state"]! == "agree" && (string?)entry["check"] == "confirmed");

    static List<string> QuoteErrors(JsonObject output, JsonObject pulled, JsonObject requirement)
    {
        var errors = new List<string>();
        if ((string?)output["ticketQuote"] is not { Length: > 0 } t) errors.Add("`ticketQuote` is needed.");
        else if (!Quotes.InText(t, QuotableText(pulled))) errors.Add($"The ticket quote \"{t}\" isn't from the ticket, word for word (at least 4 words, or a whole line).");
        var quotes = requirement["quotes"]!.AsArray().Select(q => (string)q!["text"]!).ToList();
        if ((string?)output["requirementQuote"] is not { Length: > 0 } r) errors.Add("`requirementQuote` is needed.");
        else if (!quotes.Any(q => Quotes.InText(r, q))) errors.Add($"The requirement quote \"{r}\" isn't from the requirement's FSD quotes, word for word (at least 4 words, or the whole quote).");
        return errors;
    }

    static RunStep Failed(StangentRepo repo, TicketPaths paths, JsonObject state, IReadOnlyList<string> errors)
    {
        var matcher = state["matcherUsage"] is { } m ? Usage.FromJson(m) : Usage.FromJson(state["usage"]);
        var checker = state["matcherUsage"] is null ? Usage.None : Usage.FromJson(state["usage"]);
        Save(paths, Record(repo, paths.Key, (string)state["inputs"]!, "failed", state["counts"]!, state["candidates"]!, state["finder"]!, matcher, checker, why: string.Join("; ", errors.Take(3))));
        MarkDone(paths, state);
        return new RunStep("failed", "none", Errors: errors);
    }

    static void MarkDone(TicketPaths paths, JsonObject state)
    {
        state["stage"] = "done";
        AiStep.SaveState(paths, Step, state);
    }

    static JsonObject Record(StangentRepo repo, string key, string inputs, string outcome, JsonNode counts, JsonNode candidates, JsonNode finder, Usage matcher, Usage checker, string? why) => new()
    {
        ["schemaVersion"] = 1, ["key"] = key, ["inputs"] = inputs, ["outcome"] = outcome,
        // Benchmark runs only (D99): every result made with it says so.
        ["approvalSkipped"] = repo.Config.Fsd.SkipApproval,
        ["finder"] = finder.DeepClone(),
        ["candidates"] = counts.DeepClone(),
        ["considered"] = candidates is JsonArray a ? a.DeepClone() : new JsonArray(),
        ["confirmed"] = null,
        ["why"] = why,
        ["usage"] = new JsonObject { ["matcher"] = matcher.ToJson(), ["checker"] = checker.ToJson() },
    };

    static void Save(TicketPaths paths, JsonObject record)
    {
        var errors = SchemaRegistry.Embedded.Validate(Schema, record.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_match", $"{paths.Key}'s match broke ticket-match v1 (a stangent bug).", ExitCodes.Failed, errors);
        Quotes.WriteAtomic(Result(paths), CanonicalJson.Serialize(record));
    }

    // ---- inputs ----

    static string MatcherInput(JsonObject pulled, List<Candidate> candidates)
    {
        var sb = AiStep.Header(Step);
        sb.Append("# The ticket\n\n").Append(TicketText(pulled)).Append("\n# Requirements linked to the code at this ticket's starting points\n\n");
        foreach (var c in candidates.Where(c => c.Linked))
        {
            var r = c.Requirement;
            sb.Append($"- **{(string)r["id"]!}** {(string)r["title"]!}: {(string)r["statement"]!}\n");
            foreach (var q in r["quotes"]!.AsArray()) sb.Append($"  - FSD: \"{(string)q!["text"]!}\"\n");
        }
        sb.Append("\n# Requirements no code was found for\n\nBy title and FSD section only. These say a feature may not be built.\n\n");
        foreach (var c in candidates.Where(c => !c.Linked))
        {
            var r = c.Requirement;
            sb.Append($"- **{(string)r["id"]!}** {(string)r["title"]!} (§{c.Section} {c.SectionTitle}): \"{(string)r["quotes"]![0]!["text"]!}\"\n");
        }
        return sb.ToString();
    }

    static string CheckerInput(JsonObject pulled, JsonObject requirement)
    {
        var sb = new StringBuilder("# Instructions\n\n").Append(AiStep.Instructions("match-checker.md"));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText(CheckerSchema).TrimEnd('\n')).Append("\n```\n\n");
        sb.Append("# The ticket\n\n").Append(TicketText(pulled)).Append("\n# The requirement\n\n");
        sb.Append($"{(string)requirement["title"]!}: {(string)requirement["statement"]!}\n\nFrom the agreed specification, word for word:\n\n");
        foreach (var q in requirement["quotes"]!.AsArray()) sb.Append($"- \"{(string)q!["text"]!}\"\n");
        foreach (var a in requirement["acceptance"]!.AsArray()) sb.Append($"\nHow to tell it's met: {(string)a!["text"]!}\n");
        return sb.ToString();
    }
}

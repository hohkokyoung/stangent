using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.Impact;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;
using Stangent.Tickets.Proof;

namespace Stangent.Tickets;

/// <summary>
/// Draft comments (plan step 6, D74): <c>questions</c> before work starts (missing bug details, clashes with the FSD or a
/// page, a bug that wasn't reproduced) and a <c>resolution</c> from the merged PR. Code gathers the facts; the AI words
/// them with every claim marked and linked; code checks each link (quotes word for word, pages, FSD sections, the PR's
/// files, the proof) and, for the <c>client</c> tone, that no code names or file paths appear. A person posts it with
/// <c>stangent ticket post</c>. Recorded as <c>ticket-update</c> v1.
/// </summary>
public static partial class TicketDrafts
{
    public const string Schema = "ticket-update.v1.json";
    public const int MaxPages = 10;

    public static string Dir(TicketPaths paths) => Path.Combine(paths.Dir, "drafts");
    public static string JsonPath(TicketPaths paths, string kind, string tone) => Path.Combine(Dir(paths), $"{kind}-{tone}.json");

    static StepSpec Step(string kind) => kind switch
    {
        "questions" => StepSpec.Questions,
        "resolution" => StepSpec.Resolution,
        _ => throw new StangentException("usage", $"--kind must be questions or resolution, not '{kind}'.", ExitCodes.Usage),
    };

    // ---- prepare ----

    public static RunStep Prepare(StangentRepo repo, TicketContext ctx, string key, string kind, string? tone, string? prRange, string? prUrl)
    {
        var paths = new TicketPaths(repo.Root, key);
        var step = Step(kind);
        tone ??= repo.Config.Tickets.Tone;
        if (tone is not ("internal" or "client")) throw new StangentException("usage", $"--tone must be internal or client, not '{tone}'.", ExitCodes.Usage);
        var pulled = TicketPull.LoadOrFail(repo.Root, key);
        var proof = BugProof.Load(paths);
        var facts = kind == "questions" ? Questions(repo, ctx, key, pulled) : Resolution(repo, ctx, prRange
            ?? throw new StangentException("usage", "A resolution draft needs --pr <base..head>: the merged PR's range.", ExitCodes.Usage));
        facts["proof"] = proof?["outcome"]?.DeepClone();
        facts["pr"] = prRange is null ? null : new JsonObject { ["range"] = prRange, ["url"] = prUrl };

        var input = Input(step, kind, tone, pulled, facts, proof, ctx);
        var hash = CanonicalJson.Hash(input);
        if (PageRun.LoadJson(JsonPath(paths, kind, tone)) is JsonObject existing && (string?)existing["inputs"] == hash) return new RunStep("current", "none");
        AiPolicy.EnsureAllowed(repo.Config, $"ticket draft --kind {kind}");
        facts["kind"] = kind;
        facts["tone"] = tone;
        return AiStep.Start(paths, step, input, hash, facts);
    }

    /// <summary>What a questions draft may link to: the bundle's included pages and FSD sections, the missing bug fields,
    /// the proof.</summary>
    static JsonObject Questions(StangentRepo repo, TicketContext ctx, string key, JsonObject pulled)
    {
        var bundle = TicketBundle.LoadOrFail(repo.Root, key);
        return new JsonObject
        {
            ["missing"] = Missing(pulled, bundle),
            ["pages"] = new JsonArray([.. bundle["pages"]!.AsArray().Where(p => (string?)p!["state"] == "included").Select(p => (JsonNode)(string)p!["id"]!)]),
            ["sections"] = new JsonArray([.. bundle["sections"]!.AsArray().Select(s => (JsonNode)new JsonObject { ["doc"] = (string)s!["doc"]!, ["section"] = (string)s["id"]! })]),
            ["files"] = new JsonArray([.. bundle["pointers"]!.AsArray().Select(p => (string?)p!["file"]).OfType<string>().Distinct().Order(StringComparer.Ordinal).Select(f => (JsonNode)f)]),
        };
    }

    /// <summary>The missing bug fields, plus <c>where</c> when the bundle has no starting point (D87): the reporter is
    /// asked where it happens rather than the work starting from a guess.</summary>
    public const string Where = "where";

    static JsonArray Missing(JsonObject pulled, JsonObject bundle)
    {
        var missing = pulled["bug"]?["missing"]?.AsArray().Select(m => (string)m!).ToList() ?? [];
        if (bundle["points"]!.AsArray().Count == 0 && !missing.Contains(Where)) missing.Add(Where);
        return new JsonArray([.. missing.Select(m => (JsonNode)m)]);
    }

    /// <summary>What a resolution may link to: the PR's changed files, the reference pages whose statements cite what the
    /// change reaches within 2 lines, their FSD sections, the proof.</summary>
    static JsonObject Resolution(StangentRepo repo, TicketContext ctx, string range)
    {
        var change = new ChangeSpec("diff", range);
        var files = DiffMapper.ChangedLines(repo.Root, range).Select(h => h.File).Distinct().Order(StringComparer.Ordinal).ToList();
        var starts = ctx.Analyzer.StartNodes(change, repo.Root);
        var reached = starts.Select(s => s.Id).Concat(ctx.Analyzer.AffectedWithDepth(starts).Where(a => a.Depth <= TicketBundle.ImpactDepth).Select(a => a.Node.Id))
            .ToHashSet(StringComparer.Ordinal);
        var pages = ctx.Index.Pages.Where(p => PageChecks.Statements(p.Value).Any(s => s.Statement["cites"]!.AsArray().Any(c => reached.Contains((string)c!))))
            .Select(p => p.Key).Order(StringComparer.Ordinal).Take(MaxPages).ToList();
        var pageSet = pages.ToHashSet(StringComparer.Ordinal);
        var findings = repo.Config.Fsd.Findings;
        var sections = ctx.Trace.Where(e => e["fsd"] is JsonObject && (string?)e["page"]?["id"] is { } p && pageSet.Contains(p) && (findings || (string?)e["state"] == "agree"))
            .Select(e => ((string)e["fsd"]!["doc"]!, (string)e["fsd"]!["section"]!)).Distinct().OrderBy(s => s.Item1, StringComparer.Ordinal).ThenBy(s => TraceFile.SectionKey(s.Item2), StringComparer.Ordinal);
        JsonObject? impact = starts.Count == 0 ? null : ctx.Analyzer.Analyze(change, repo.Root, null, TicketBundle.ImpactDepth, (r, x) => FsdImpact.Sections(repo.Root, r, x));
        return new JsonObject
        {
            ["missing"] = new JsonArray(),
            ["pages"] = new JsonArray([.. pages.Select(p => (JsonNode)p)]),
            ["sections"] = new JsonArray([.. sections.Select(s => (JsonNode)new JsonObject { ["doc"] = s.Item1, ["section"] = s.Item2 })]),
            ["files"] = new JsonArray([.. files.Select(f => (JsonNode)f)]),
            ["impact"] = impact is null ? null : ImpactAnalyzer.Markdown(impact),
        };
    }

    static string Input(StepSpec step, string kind, string tone, JsonObject pulled, JsonObject facts, JsonObject? proof, TicketContext ctx)
    {
        var sb = AiStep.Header(step);
        sb.Append($"# The draft\n\n- kind: {kind}\n- tone: **{tone}**\n\n# The ticket\n\n");
        foreach (var (where, text) in TicketText.From(pulled).Prose) sb.Append($"## {where}\n\n````text\n{text}\n````\n\n");
        if (pulled["bug"] is JsonObject bug)
        {
            sb.Append("# The bug's details\n\n");
            foreach (var part in TicketPull.BugParts) sb.Append($"- **{part}:** {((string?)bug[part] is { } v ? v.Replace("\n", " ") : "_missing_")}\n");
            sb.Append('\n');
        }
        var missing = facts["missing"]!.AsArray();
        if (missing.Count > 0) sb.Append($"# Missing\n\n{string.Join(", ", missing.Select(m => (string)m!))}\n\n");
        if (facts["pr"] is JsonObject pr) sb.Append($"# The PR\n\n- range: `{pr["range"]}`{((string?)pr["url"] is { } url ? $"\n- link: {url}" : "")}\n\n");
        var files = facts["files"]!.AsArray();
        if (kind == "resolution") sb.Append("# Files the PR changed\n\n").Append(string.Concat(files.Select(f => $"- `{f}`\n"))).Append('\n');
        if ((string?)facts["impact"] is { } impact) sb.Append("# What the change affects\n\n").Append(impact).Append('\n');

        sb.Append("# Reference pages\n\n");
        foreach (var id in facts["pages"]!.AsArray().Select(p => (string)p!))
        {
            var page = ctx.Index.Pages[id];
            sb.Append($"## `{id}`: {(string)page["title"]!}\n\n");
            foreach (var (_, sid, s) in PageChecks.Statements(page)) sb.Append($"- **{sid}** {(string)s["text"]!}\n");
            sb.Append('\n');
        }
        sb.Append("# FSD sections\n\n");
        foreach (var s in facts["sections"]!.AsArray())
        {
            var section = ctx.Index.Sections[((string)s!["doc"]!, (string)s["section"]!)];
            sb.Append($"## {s["doc"]} §{s["section"]} {(string)section["title"]!}\n\n````text\n{(string)section["text"]!}\n````\n\n");
        }
        if (proof?["attempts"]?.AsArray().LastOrDefault() is JsonObject a)
            sb.Append($"# The bug proof\n\n- outcome: **{proof["outcome"]}**{((string?)a["reason"] is { } r ? $"\n- why: {r}" : "")}{((string?)a["testName"] is { } tn ? $"\n- test: `{tn}` in `{a["testFile"]}`" : "")}{((string?)a["hint"] is { } h ? $"\n- hint tried: {h}" : "")}\n");
        return sb.ToString();
    }

    // ---- check ----

    public static RunStep Check(StangentRepo repo, TicketContext ctx, string key, string kind, Usage? usage)
    {
        var paths = new TicketPaths(repo.Root, key);
        var step = Step(kind);
        var state = AiStep.State(paths, step);
        var (output, errors) = AiStep.Read(paths, step, state, usage);
        if (output is not null) errors.AddRange(Errors(output, state, TicketPull.LoadOrFail(repo.Root, key), ctx));
        if (errors.Count > 0)
        {
            if (AiStep.Retry(paths, step, state, errors) is { } retry) return retry;
            state["stage"] = "failed";
            AiStep.SaveState(paths, step, state);
            return new RunStep("failed", "none", Errors: errors);
        }
        var tone = (string)state["tone"]!;
        var run = state["usage"]!.DeepClone().AsObject();
        run["retries"] = (int?)state["retries"] ?? 0;
        var record = new JsonObject
        {
            ["schemaVersion"] = 1, ["key"] = paths.Key, ["kind"] = kind, ["tone"] = tone,
            ["text"] = Markers().Replace((string)output!["text"]!, "").Trim(),
            ["claims"] = output["claims"]!.DeepClone(), ["pr"] = state["pr"]?.DeepClone(),
            ["inputs"] = (string)state["inputs"]!, ["run"] = run, ["posted"] = null,
        };
        Save(paths, record, (string)output["text"]!);
        state["stage"] = "done";
        AiStep.SaveState(paths, step, state);
        return new RunStep("recorded", "none");
    }

    [GeneratedRegex(@"[ \t]?\[C\d+\]")]
    private static partial Regex Markers();

    /// <summary>Every check on a draft (D74, D18's PR-summary row): markers match claims, each link is real, quotes are
    /// word for word, a questions draft asks for every missing field and mentions a failed proof, and a client draft
    /// holds no code names or paths.</summary>
    public static List<string> Errors(JsonObject output, JsonObject facts, JsonObject pulled, TicketContext ctx)
    {
        var errors = new List<string>();
        var text = (string)output["text"]!;
        var claims = output["claims"]!.AsArray().OfType<JsonObject>().ToList();
        var ids = claims.Select(c => (string)c["id"]!).ToList();
        foreach (var dup in ids.GroupBy(i => i).Where(g => g.Count() > 1)) errors.Add($"claim {dup.Key} is used twice.");
        foreach (var id in ids.Where(i => !text.Contains($"[{i}]", StringComparison.Ordinal))) errors.Add($"claim {id} isn't marked in the text: put [{id}] where it's made.");
        foreach (Match m in Regex.Matches(text, @"\[(C\d+)\]"))
            if (!ids.Contains(m.Groups[1].Value)) errors.Add($"the text marks [{m.Groups[1].Value}] but there's no such claim.");
        // Every sentence that states something carries a claim (D74): questions, a short greeting, and framing lines (a short
        // heading or lead-in, D84) are exempt.
        foreach (var sentence in Sentences(text).Where(x => !x.EndsWith('?') && !Markers().IsMatch(x) && x.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 4))
            errors.Add($"\"{Short(sentence)}\" states something without a claim: mark it with the claim that supports it, or leave it out.");

        var quotable = TicketText.From(pulled).Quotable + "\n\n" + string.Join("\n\n", TicketPull.BugParts.Select(p => (string?)pulled["bug"]?[p]).OfType<string>());
        var pages = facts["pages"]!.AsArray().Select(p => (string)p!).ToHashSet(StringComparer.Ordinal);
        var sections = facts["sections"]!.AsArray().Select(s => ((string)s!["doc"]!, (string)s["section"]!)).ToHashSet();
        var files = facts["files"]!.AsArray().Select(f => (string)f!).ToHashSet(StringComparer.Ordinal);
        var missing = facts["missing"]!.AsArray().Select(m => (string)m!).ToHashSet(StringComparer.Ordinal);
        var proof = (string?)facts["proof"];
        foreach (var c in claims)
            foreach (var (link, i) in c["links"]!.AsArray().OfType<JsonObject>().Select((l, i) => (l, i)))
            {
                var at = $"{c["id"]} link {i + 1}";
                var quote = (string?)link["quote"];
                switch ((string)link["kind"]!)
                {
                    case "ticket":
                        if (quote is null || !Quotes.InText(quote, quotable)) errors.Add($"{at}: \"{Short(quote)}\" isn't in the ticket word for word (at least 4 words, or a whole line).");
                        break;
                    case "page":
                        var page = (string?)link["page"] ?? "";
                        var statement = ctx.Index.Statement(page, (string?)link["statement"] ?? "");
                        if (!pages.Contains(page)) errors.Add($"{at}: page `{page}` isn't one of the pages in the input.");
                        else if (statement is null) errors.Add($"{at}: page `{page}` has no statement {link["statement"]}.");
                        else if (quote is null || !Quotes.InText(quote, (string)statement["text"]!)) errors.Add($"{at}: \"{Short(quote)}\" isn't statement {link["statement"]} of `{page}` word for word.");
                        break;
                    case "fsd":
                        var key = ((string?)link["doc"] ?? "", (string?)link["section"] ?? "");
                        if (!sections.Contains(key)) errors.Add($"{at}: FSD {key.Item1} §{key.Item2} isn't one of the sections in the input.");
                        else if (quote is null || !Quotes.InText(quote, (string)ctx.Index.Sections[key]["text"]!)) errors.Add($"{at}: \"{Short(quote)}\" isn't in FSD §{key.Item2} word for word.");
                        break;
                    case "file":
                        if (!files.Contains((string?)link["file"] ?? "")) errors.Add($"{at}: `{link["file"]}` isn't one of the files in the input.");
                        break;
                    case "pr":
                        if (facts["pr"] is null) errors.Add($"{at}: there's no PR to link to.");
                        break;
                    case "proof":
                        if (proof is null) errors.Add($"{at}: there's no bug proof to link to.");
                        else if ((string?)link["outcome"] != proof) errors.Add($"{at}: the proof's outcome is {proof}, not {(string?)link["outcome"] ?? "(none given)"}: say what it was.");
                        break;
                    case "missing":
                        if (!missing.Contains((string?)link["field"] ?? "")) errors.Add($"{at}: `{link["field"]}` isn't a missing bug field.");
                        break;
                }
            }

        var linked = claims.SelectMany(c => c["links"]!.AsArray()).OfType<JsonObject>().ToList();
        if ((string)facts["kind"]! == "questions")
        {
            foreach (var m in missing.Where(m => !linked.Any(l => (string)l["kind"]! == "missing" && (string?)l["field"] == m)).Order(StringComparer.Ordinal))
                errors.Add($"{(m == Where ? "where it happens" : $"the bug's {m}")} is missing: ask for it in a claim linked {{\"kind\":\"missing\",\"field\":\"{m}\"}}.");
            if (proof is "not-reproduced" or "not-reproduced-flaky" && !linked.Any(l => (string)l["kind"]! == "proof"))
                errors.Add("the bug wasn't reproduced: say what was tried and ask for what's needed, in a claim linked {\"kind\":\"proof\"}.");
        }
        if (AboutItsInput().Match(text) is { Success: true } about)
            errors.Add($"the draft talks about \"{about.Value}\": it's read by people who never saw what you were given; say it without that.");
        if ((string)facts["tone"]! == "client") errors.AddRange(ClientProblems(Markers().Replace(text, ""), ctx));
        return errors;
    }

    /// <summary>The text's sentences: by line, then at a full stop, exclamation or question mark; list marks dropped.
    /// Lines that only frame the text are left out (<see cref="IsFraming"/>, D84).</summary>
    static IEnumerable<string> Sentences(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => !IsFraming(l))
            .Select(l => Regex.Replace(l, @"^([-*]|\d+[.)])\s*", ""))
            .SelectMany(l => Regex.Split(Url().Replace(l, "LINK"), @"(?<=[.!?](?:\s*\[C\d+\])*)\s+(?=[A-Z0-9])"))
            .Select(x => x.Trim()).Where(x => x.Length > 0 && x != "---");

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Url();

    /// <summary>A line that only frames the text (D84): one phrase of eight words or fewer, with no sentence end inside, that
    /// is a heading (<c># …</c>), a whole line in bold, or a lead-in ending with a colon.</summary>
    static bool IsFraming(string line)
    {
        if (Markers().IsMatch(line)) return false;
        string inner;
        if (Regex.Match(line, @"^#{1,6}\s+(.+)$") is { Success: true } h) inner = h.Groups[1].Value;
        else if (Regex.Match(line, @"^\*\*([^*]+)\*\*:?$") is { Success: true } b) inner = b.Groups[1].Value;
        else if (line.EndsWith(':')) inner = line[..^1];
        else return false;
        inner = inner.Trim().TrimEnd(':');
        var words = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return words is > 0 and <= MaxFramingWords && !Regex.IsMatch(inner, @"[.!?;]");
    }

    const int MaxFramingWords = 8;

    /// <summary>Words that show a draft talks about its own prompt (D84): such a comment can't be posted.</summary>
    /// "the input" alone isn't enough: a ticket may be about an input field.
    [GeneratedRegex(@"\b(the input (I|you|holds|has|gives|contains|provided|doesn't|does not)\b|(in|from) the input(?! (box|field|area|element))\b|my input\b|these instructions|my instructions|I was given|I've been given|I have been given|the prompt\b)", RegexOptions.IgnoreCase)]
    private static partial Regex AboutItsInput();

    [GeneratedRegex(@"(?<![\w])(?:[\w.-]+/)+[\w.-]+|\b[\w-]+\.(?:cs|ts|tsx|js|jsx|json|csproj|sln|sql|py|java|go|rb|cshtml|html|ya?ml|xml)\b")]
    private static partial Regex PathLike();

    /// <summary>What a client mustn't see (D74): backticks, file paths, page or graph ids, and the code's own names
    /// (types and methods with two or more capitalised words, or <c>Type.Member</c>).</summary>
    public static List<string> ClientProblems(string text, TicketContext ctx)
    {
        var problems = new List<string>();
        if (text.Contains('`')) problems.Add("client tone: no code formatting (backticks).");
        var plain = Url().Replace(text, " ");
        foreach (Match m in PathLike().Matches(plain)) problems.Add($"client tone: \"{m.Value}\" looks like a file path.");
        foreach (var id in new[] { "page:", "endpoint:", "|M:", "ts|" }.Where(i => plain.Contains(i, StringComparison.Ordinal)))
            problems.Add($"client tone: \"{id}\" is part of an id; describe what the user sees instead.");
        var names = ctx.Graph.AllNodes.Where(n => n.Kind is NodeKind.Type or NodeKind.Method or NodeKind.Component or NodeKind.Property && !n.IsTest)
            .Select(n => n.Name).Where(n => n.Contains('.') || Regex.IsMatch(n, "^[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)+$")).ToHashSet(StringComparer.Ordinal);
        foreach (var name in names.Order(StringComparer.Ordinal))
            if (Regex.IsMatch(plain, $@"(?<![\w.]){Regex.Escape(name)}(?![\w])")) problems.Add($"client tone: \"{name}\" is a name from the code.");
        return [.. problems.Distinct().Take(20)];
    }

    static void Save(TicketPaths paths, JsonObject record, string? markedText = null)
    {
        var errors = SchemaRegistry.Embedded.Validate(Schema, record.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_draft", "The draft broke its contract (a stangent bug).", ExitCodes.Failed, errors);
        var kind = (string)record["kind"]!;
        var tone = (string)record["tone"]!;
        Quotes.WriteAtomic(JsonPath(paths, kind, tone), CanonicalJson.Serialize(record));
        Quotes.WriteAtomic(Path.ChangeExtension(JsonPath(paths, kind, tone), ".md"), Markdown(record, markedText));
    }

    /// <summary>The draft for the person who posts it: the text as it will be posted, then each claim's evidence.</summary>
    public static string Markdown(JsonObject record, string? markedText = null)
    {
        var sb = new StringBuilder($"<!-- {record["key"]} · {record["kind"]} · {record["tone"]} · {(record["posted"] is null ? "draft: post with `stangent ticket post`" : $"posted {record["posted"]!["at"]} by {record["posted"]!["by"]}")} -->\n\n");
        sb.Append((string)record["text"]!).Append("\n\n---\n\n**Checked claims**");
        if (markedText is not null) sb.Append(" (as marked in the draft)");
        sb.Append("\n\n");
        foreach (var c in record["claims"]!.AsArray())
            sb.Append($"- {c!["id"]}: ").Append(string.Join("; ", c["links"]!.AsArray().Select(l => l!["kind"] + ((string?)l["quote"] is { } q ? $" \"{q}\"" : "")
                + ((string?)l["page"] ?? (string?)l["file"] ?? (string?)l["field"] ?? ((string?)l["section"] is { } s ? $" {l["doc"]} §{s}" : "")) switch { "" => "", var x => $" ({x})" }))).Append('\n');
        return sb.ToString();
    }

    // ---- post ----

    /// <summary>
    /// Posts a checked draft under the signed-in person's own login (D74) and records who posted it and when. Nothing
    /// else is ever written to the ticket. A draft already posted isn't posted again unless <paramref name="again"/>.
    /// </summary>
    public static async Task<JsonObject> PostAsync(StangentRepo repo, IIssueTracker tracker, string key, string draft, string who, bool again, TimeProvider time, CancellationToken ct = default)
    {
        var paths = new TicketPaths(repo.Root, key);
        var file = draft.EndsWith(".json", StringComparison.Ordinal) ? Path.GetFullPath(Path.Combine(repo.Root, draft))
            : Path.Combine(Dir(paths), (draft.Contains('-') ? draft : $"{draft}-{repo.Config.Tickets.Tone}") + ".json");
        var record = PageRun.LoadJson(file) as JsonObject
            ?? throw new StangentException("no_draft", $"No draft at {paths.Relative(file)}. Run `stangent ticket draft {key} --kind …` first.", ExitCodes.NotFound);
        if ((string?)record["key"] != paths.Key) throw new StangentException("wrong_ticket", $"That draft is for {record["key"]}, not {paths.Key}.", ExitCodes.Usage);
        if (record["posted"] is not null && !again)
            throw new StangentException("already_posted", $"This draft was posted by {record["posted"]!["by"]} at {record["posted"]!["at"]} (comment {record["posted"]!["commentId"]}). Use --again to post it twice.", ExitCodes.Usage);
        var posted = await tracker.PostCommentAsync(paths.Key, (string)record["text"]!, ct);
        record["posted"] = new JsonObject { ["by"] = who, ["at"] = time.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture), ["commentId"] = posted.CommentId };
        Save(paths, record);
        return record;
    }

    static string Short(string? s) => s is null ? "(no quote)" : s.Length > 80 ? s[..80] + "…" : s;
}

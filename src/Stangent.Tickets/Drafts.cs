using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Core.State;
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
        var (facts, input) = Inputs(repo, ctx, key, kind, tone, prRange, prUrl);
        var hash = CanonicalJson.Hash(input);
        if (PageRun.LoadJson(JsonPath(paths, kind, tone)) is JsonObject existing && (string?)existing["inputs"] == hash) return new RunStep("current", "none");
        AiPolicy.EnsureAllowed(repo.Config, $"ticket draft --kind {kind}");
        facts["kind"] = kind;
        facts["tone"] = tone;
        return AiStep.Start(paths, step, input, hash, facts);
    }

    /// <summary>What a draft is made from: the facts it may link to, and the input the writer reads, whose hash says
    /// whether a recorded draft is still current.</summary>
    static (JsonObject Facts, string Input) Inputs(StangentRepo repo, TicketContext ctx, string key, string kind, string tone, string? prRange, string? prUrl)
    {
        var paths = new TicketPaths(repo.Root, key);
        var pulled = TicketPull.LoadOrFail(repo.Root, key);
        var proof = BugProof.Load(paths);
        var facts = kind == "questions" ? Questions(repo, ctx, key, pulled) : Resolution(repo, ctx, prRange
            ?? throw new StangentException("usage", "A resolution draft needs --pr <base..head>: the merged PR's range.", ExitCodes.Usage));
        facts["proof"] = proof?["outcome"]?.DeepClone();
        // D106: an attempt that only ran out says nothing about the bug, so it isn't the reporter's to answer.
        if (proof?["attempts"]?.AsArray().LastOrDefault() is JsonObject last && BugProof.NoProof(last) == "ran-out") facts["proofRanOut"] = true;
        facts["pr"] = prRange is null ? null : new JsonObject { ["range"] = prRange, ["url"] = prUrl };
        return (facts, Input(Step(kind), kind, tone, pulled, facts, proof, ctx));
    }

    /// <summary>What a questions draft may link to: the bundle's included pages and FSD sections, the missing bug fields,
    /// the proof.</summary>
    static JsonObject Questions(StangentRepo repo, TicketContext ctx, string key, JsonObject pulled)
    {
        var bundle = TicketBundle.LoadOrFail(repo.Root, key);
        return new JsonObject
        {
            ["missing"] = Missing(pulled, bundle),
            ["outside"] = bundle["outside"]?.DeepClone() ?? new JsonArray(),
            ["pages"] = new JsonArray([.. bundle["pages"]!.AsArray().Where(p => (string?)p!["state"] == "included").Select(p => (JsonNode)(string)p!["id"]!)]),
            ["sections"] = new JsonArray([.. bundle["sections"]!.AsArray().Select(s => (JsonNode)new JsonObject { ["doc"] = (string)s!["doc"]!, ["section"] = (string)s["id"]! })]),
            ["files"] = new JsonArray([.. bundle["pointers"]!.AsArray().Select(p => (string?)p!["file"]).OfType<string>().Distinct().Order(StringComparer.Ordinal).Select(f => (JsonNode)f)]),
        };
    }

    /// <summary>The missing bug fields, plus <c>where</c> when the bundle has no starting point (D87) and no part outside
    /// the code (D83): the reporter is asked where it happens rather than the work starting from a guess.</summary>
    public const string Where = "where";

    static JsonArray Missing(JsonObject pulled, JsonObject bundle)
    {
        var missing = pulled["bug"]?["missing"]?.AsArray().Select(m => (string)m!).ToList() ?? [];
        if (bundle["points"]!.AsArray().Count == 0 && OutsidePart.Of(bundle).Count == 0 && !missing.Contains(Where)) missing.Add(Where);
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
        var sections = ctx.Trace.Where(e => e["fsd"] is JsonObject && (string?)e["page"]?["id"] is { } p && pageSet.Contains(p) && (findings || (string?)e["state"] == "agree") && !TraceFindings.Closed(e))
            .Select(e => ((string)e["fsd"]!["doc"]!, (string)e["fsd"]!["section"]!)).Where(ctx.Index.Sections.ContainsKey).Distinct().OrderBy(s => s.Item1, StringComparer.Ordinal).ThenBy(s => TraceFile.SectionKey(s.Item2), StringComparer.Ordinal);
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
        if (facts["outside"] is JsonArray { Count: > 0 } outside)
        {
            sb.Append("# Outside the code\n\n");
            foreach (var o in outside.Select(o => OutsidePart.FromJson(o!)))
                sb.Append(o.By == "ai" ? $"- **{o.Area}**, the ticket says: {o.Quote.Replace("\n", " ")}\n" : $"- **{o.Area}**, a developer ({o.By}) notes: {o.Quote.Replace("\n", " ")}\n");
            sb.Append('\n');
        }
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
        {
            // D96: before work starts, a reproduced failure is a difference for a person to judge; a resolution comes after the fix.
            var reproduced = (string?)proof["outcome"] == "reproduced";
            var questions = kind == "questions";
            var expected = reproduced && questions && (string?)a["expectedQuote"] is { } eq ? $"\n- the reporter expected: {eq}" : "";
            var call = !reproduced ? ""
                : proof["confirmedBy"] is not null ? $"\n- confirmed by {proof["confirmedBy"]} as the reported problem{(questions ? " and a bug" : "")}"
                : questions ? "\n- no developer has confirmed this is a bug: the code differs from what the reporter expected, which may be how it's meant to work, and the failure may not be the reported one (D95, D96)"
                : "\n- not confirmed by a developer yet: the failure may not be the reported one (D95)";
            sb.Append($"# The bug proof\n\n- outcome: **{proof["outcome"]}**{((string?)a["reason"] is { } r ? $"\n- why: {r}" : "")}{((string?)a["testName"] is { } tn ? $"\n- test: `{tn}` in `{a["testFile"]}`" : "")}{expected}{(reproduced && (string?)a["actualQuote"] is { } aq && (string?)a["shows"] is { } sh ? $"\n- the ticket says: {aq}\n- the test got: {sh}" : "")}{call}{((string?)a["hint"] is { } h ? $"\n- hint tried: {h}" : "")}{((bool?)facts["proofRanOut"] == true ? "\n- this attempt only ran out of its limit, which says nothing about the bug: don't mention it, and don't ask for anything because of it" : "")}\n");
        }
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
            AiStep.Finish(paths, step, state, "failed", "failed its checks");
            return new RunStep("failed", "none", Errors: errors);
        }
        var tone = (string)state["tone"]!;
        var run = state["usage"]!.DeepClone().AsObject();
        run["retries"] = (int?)state["retries"] ?? 0;
        // D97, D98: a part that states something with no claim is left out, not a failed draft; every kept part was checked.
        var parts = Parts(output!);
        var dropped = parts.Where(p => !Keeps(p)).Select(p => p.Text).ToList();
        var notChecked = parts.Where(p => Keeps(p) && p.Claims.Count == 0 && p.Text != "---").Select(p => p.Text).ToList();
        var record = new JsonObject
        {
            ["schemaVersion"] = 1, ["key"] = paths.Key, ["kind"] = kind, ["tone"] = tone,
            ["text"] = Join(parts, Keeps),
            ["claims"] = output!["claims"]!.DeepClone(), ["pr"] = state["pr"]?.DeepClone(),
            ["inputs"] = (string)state["inputs"]!, ["run"] = run, ["posted"] = null,
        };
        if (dropped.Count > 0) record["dropped"] = new JsonArray([.. dropped.Select(d => (JsonNode)d)]);
        if (notChecked.Count > 0) record["unchecked"] = new JsonArray([.. notChecked.Select(d => (JsonNode)d)]);
        // Made again: the new draft hasn't been posted, whatever the last one was.
        if (PageRun.LoadJson(JsonPath(paths, kind, tone)) is JsonObject earlier && earlier["posted"] is not null)
            StateChanges.Move(repo.Root, StateKinds.DraftPosted, DraftId(paths, record), "posted", "unposted", StateActor.Code("stangent ticket"), "the draft was made again");
        Save(paths, record);
        AiStep.Finish(paths, step, state, "done", "recorded");
        return new RunStep("recorded", "none");
    }

    [GeneratedRegex(@"[ \t]?\[C\d+\]")]
    private static partial Regex Markers();

    /// <summary>Every check on a draft (D74, D18's PR-summary row, D98): each part is one sentence or line, every claim is
    /// on a part, each link is real, quotes are word for word, a questions draft asks for every missing field and mentions
    /// a failed proof, and a client draft holds no code names or paths. A part with no claim isn't an error (D97): Check
    /// leaves it out. The wording checks read every part, the ones left out too.</summary>
    public static List<string> Errors(JsonObject output, JsonObject facts, JsonObject pulled, TicketContext ctx)
    {
        var errors = new List<string>();
        var parts = Parts(output);
        var text = Join(parts, _ => true);
        var claims = output["claims"]!.AsArray().OfType<JsonObject>().ToList();
        var ids = claims.Select(c => (string)c["id"]!).ToList();
        foreach (var dup in ids.GroupBy(i => i).Where(g => g.Count() > 1)) errors.Add($"claim {dup.Key} is used twice.");
        foreach (var id in ids.Where(i => !parts.Any(p => p.Claims.Contains(i)))) errors.Add($"claim {id} isn't on any part: list it in the claims of the sentence it supports.");
        foreach (var (p, n) in parts.Select((p, i) => (p, i + 1)))
        {
            foreach (var id in p.Claims.Where(c => !ids.Contains(c)).Distinct()) errors.Add($"part {n} lists {id} but there's no such claim.");
            if (p.Text.Length == 0) errors.Add($"part {n} is empty.");
            else if (p.Text.Contains('\n')) errors.Add($"part {n} spans lines: give each line its own part, the next one with \"start\": \"line\".");
            else if (HoldsTwoSentences(p.Text))
                errors.Add($"part {n}, \"{Short(p.Text)}\", holds more than one sentence: give each sentence its own part.");
        }
        // A claim backs one sentence (D98): one link can't stand behind a whole comment.
        foreach (var id in ids.Distinct())
            if (parts.Select((p, i) => (p, i + 1)).Where(x => x.p.Claims.Contains(id)).Select(x => x.Item2).ToList() is { Count: > 1 } on)
                errors.Add($"claim {id} is on parts {string.Join(" and ", on)}: a claim backs one sentence; give each other sentence its own claim.");

        var quotable = TicketText.From(pulled).Quotable + "\n\n" + string.Join("\n\n", TicketPull.BugParts.Select(p => (string?)pulled["bug"]?[p]).OfType<string>());
        var pages = facts["pages"]!.AsArray().Select(p => (string)p!).ToHashSet(StringComparer.Ordinal);
        var sections = facts["sections"]!.AsArray().Select(s => ((string)s!["doc"]!, (string)s["section"]!)).ToHashSet();
        var files = facts["files"]!.AsArray().Select(f => (string)f!).ToHashSet(StringComparer.Ordinal);
        var missing = facts["missing"]!.AsArray().Select(m => (string)m!).ToHashSet(StringComparer.Ordinal);
        var proof = (string?)facts["proof"];
        var ranOut = (bool?)facts["proofRanOut"] == true;
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
                        else if (ranOut) errors.Add($"{at}: the attempt only ran out of its limit, which says nothing about the bug: leave it out.");
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
            if (proof is "not-reproduced" or "not-reproduced-flaky" && !ranOut && !linked.Any(l => (string)l["kind"]! == "proof"))
                errors.Add("the bug wasn't reproduced: say what was tried and ask for what's needed, in a claim linked {\"kind\":\"proof\"}.");
        }
        if (AboutItsInput().Match(text) is { Success: true } about)
            errors.Add($"the draft talks about \"{about.Value}\": it's read by people who never saw what you were given; say it without that.");
        if ((string)facts["tone"]! == "client") errors.AddRange(ClientProblems(Markers().Replace(text, ""), ctx));
        return errors;
    }

    /// <summary>One piece of the writer's draft (D98): one sentence or one framing line, the claims that back it, and
    /// whether it starts a new <c>line</c> or <c>paragraph</c> (otherwise it follows the one before on the same line).
    /// Markers like <c>[C1]</c> in the text are dropped: the claims are listed on the part.</summary>
    public sealed record Part(string Text, IReadOnlyList<string> Claims, string? Start);

    /// <summary>The writer's parts. Leading spaces stay only before a list mark (a nested item); markers are dropped.</summary>
    public static List<Part> Parts(JsonObject output) =>
        [.. output["parts"]!.AsArray().OfType<JsonObject>().Select(p =>
        {
            var text = Markers().Replace(((string)p["text"]!).Replace("\r\n", "\n"), "").TrimEnd();
            if (!Regex.IsMatch(text, @"^[ \t]+([-*+]|\d+[.)])\s")) text = text.TrimStart();
            return new Part(text, [.. (p["claims"]?.AsArray() ?? []).Select(c => (string)c!)], (string?)p["start"]);
        })];

    /// <summary>
    /// The comment from the parts <paramref name="keep"/> keeps. A left-out part's line or paragraph break passes to the
    /// next kept part. Markdown blocks get the break they need whatever <c>start</c> says: a heading, a list item or a
    /// quote starts a new line, a rule (<c>---</c>) a new paragraph, and what follows a heading or rule a new line.
    /// Top-level numbered items are numbered again, so a dropped item leaves no gap.
    /// </summary>
    public static string Join(IReadOnlyList<Part> parts, Func<Part, bool> keep)
    {
        var sb = new StringBuilder();
        var pending = 0;      // the strongest break since the last kept part: 0 same line, 1 new line, 2 new paragraph
        var number = 0;       // the last number given in this run of numbered items; 0 outside one
        var afterBlock = false;   // the last kept part was a heading or a rule: what follows starts a new line
        var inBlock = false;      // the last kept part was a list item or a quote: a new line that isn't one starts a paragraph
        foreach (var p in parts)
        {
            pending = Math.Max(pending, p.Start switch { "paragraph" => 2, "line" => 1, _ => 0 });
            if (!keep(p) || p.Text.Length == 0) continue;   // an empty part is an error (Errors); it never reaches Join's text
            var text = p.Text;
            var rule = text == "---";
            var item = Regex.IsMatch(text, @"^(>|\s*([-*+]|\d+[.)])\s)");
            pending = Math.Max(pending, rule ? 2 : afterBlock || item || Heading().IsMatch(text) ? 1 : 0);
            if (inBlock && pending == 1 && !item) pending = 2;
            if (Numbered().Match(text) is { Success: true } m) text = $"{++number}{m.Groups[2].Value}{text[m.Length..]}";
            else if (pending > 0 && !char.IsWhiteSpace(text[0])) number = 0;   // a new line that isn't an item ends the run
            if (sb.Length > 0) sb.Append(pending switch { 2 => "\n\n", 1 => "\n", _ => " " });
            sb.Append(text);
            afterBlock = rule || Heading().IsMatch(text);
            if (pending > 0 || sb.Length == 0) inBlock = item;
            pending = 0;
        }
        return sb.ToString().Trim();
    }

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Url();

    [GeneratedRegex(@"^#{1,6}\s")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\d+)([.)]\s)")]
    private static partial Regex Numbered();

    /// <summary>A list mark at the start of a part: "- ", "* ", "1. ", "2) ". A space must follow, so "**bold**" isn't one.</summary>
    [GeneratedRegex(@"^\s*([-*+]|\d+[.)])\s+")]
    private static partial Regex ListMark();

    /// <summary>
    /// Whether a part holds more than one sentence (D98): a full stop, "!", "?" or ";" with more text after it. Text inside
    /// quotes or backticks, an ellipsis and dotted lower-case abbreviations ("e.g.", "5 p.m.") don't count; a sentence
    /// end at the close of a quote does, unless lower-case text goes on (<c>"Save failed." The cache…</c> is two). A capital's full stop ("plan A. The…") counts:
    /// a retry is cheaper than a statement riding on its neighbour's claim.
    /// </summary>
    static bool HoldsTwoSentences(string text)
    {
        var t = ListMark().Replace(text, "");
        // A quote that closes on a sentence end ends the sentence, unless lower-case text goes on ("Click Save." but…).
        t = Regex.Replace(t, @"(`[^`]*`|""([^""]*)""|“([^”]*)”)(?=(\s+\p{Ll})?)",
            m => "§" + (m.Groups[4].Success ? "" : Regex.Match(m.Groups[2].Value + m.Groups[3].Value, @"[.!?]$").Value));
        t = Regex.Replace(t, @"\.{3}|…", "~");
        t = Regex.Replace(t, @"(?<!\p{L})\p{Ll}\.(\p{Ll}\.)+(?!\s+\p{Lu})", "~");   // "e.g.", "i.e.", "p.m."; "set x to y." still ends
        return Regex.IsMatch(t, @"[.!?;][*_""'”’)\]]*\s+\S");
    }

    /// <summary>A line that only frames the text (D84): one phrase of eight words or fewer, with no sentence end inside, that
    /// is a heading (<c># …</c>), a whole line in bold, or a lead-in ending with a colon; not "Label: finding" (D92).</summary>
    static bool IsFraming(string line)
    {
        if (Markers().IsMatch(line)) return false;
        string inner;
        if (Regex.Match(line, @"^#{1,6}\s+(.+)$") is { Success: true } h) inner = h.Groups[1].Value;
        else if (Regex.Match(line, @"^\*\*([^*]+)\*\*:?$") is { Success: true } b) inner = b.Groups[1].Value;
        else if (line.EndsWith(':')) inner = line[..^1];
        else return false;
        inner = inner.Trim().TrimEnd(':');
        // "Fix: the export now includes archived rows" names a finding, not a topic (D92): a statement, bold or not.
        if (Regex.IsMatch(inner, @":\s*\S")) return false;
        var words = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return words is > 0 and <= MaxFramingWords && !Regex.IsMatch(inner, @"[.!?;]");
    }

    const int MaxFramingWords = 8;

    /// <summary>
    /// Whether a part stays in the draft (D97, D98): one with a claim always does. One without stays only when it states
    /// nothing: a framing line or a rule (<c>---</c>), a greeting, thanks or sign-off that is nothing else, or a pure ask
    /// (<see cref="IsPureAsk"/>), with or without a greeting before it ("Hi Sam, could you send the logs?").
    /// </summary>
    static bool Keeps(Part p)
    {
        if (p.Claims.Count > 0) return true;
        if (p.Text == "---" || IsFraming(p.Text)) return true;
        var body = ListMark().Replace(p.Text, "").Trim().Trim('*', '_', '"', '\'', '(', ')').Trim();
        if (IsGreeting(body)) return true;
        return IsPureAsk(GreetingLead().Replace(body, ""));
    }

    /// <summary>A greeting, a thanks or a sign-off and nothing else (D98): "Hi Sam,", "Thanks for the report.",
    /// "Hi team, thanks for flagging this.", "Kind regards,". Not "Hi team, the export drops rows." or "Thanks to the
    /// cache, orders double.": those state something.</summary>
    static bool IsGreeting(string sentence) => sentence.Length > 0 && Greeting().IsMatch(sentence);

    const string Name = @"((?-i:\p{Lu}[\p{L}'’-]*)|team|all|everyone|there|folks)";
    const string Hello = @"(hi|hello|hey|dear|good (morning|afternoon|evening))(\s+" + Name + "){0,3}";

    [GeneratedRegex(@"^(" + Hello + @"\s*[,!.]?\s*)?((thanks|thank you|many thanks)(\s+(so much|again|all|everyone|team|in advance))?(\s+for(\s+[^\s,.!?;:]+){1,4})?|kind regards|best regards|regards|best wishes|best|cheers|all the best)?(\s*,\s*" + Name + @"(\s+" + Name + @")?)?\s*[,.!]?$", RegexOptions.IgnoreCase)]
    private static partial Regex Greeting();

    /// <summary>A greeting before what follows it: "Hi Sam, " in "Hi Sam, could you send the logs?".</summary>
    [GeneratedRegex(@"^" + Hello + @"\s*[,!]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex GreetingLead();

    /// <summary>
    /// A question or request that only asks (D92, D98): it starts by asking ("Which version…?", "Could you…",
    /// "Please send…") and carries no fact of its own: no reason ("because", "since", "as", "so that"), no clause after a
    /// colon, semicolon or ", which", and no asking to confirm something ("Can you confirm the scheduler runs twice?").
    /// "The cache corrupts every order, right?" and "The ticket says 40: which is right?" state something first; a
    /// question that rests on a fact needs the claim.
    /// </summary>
    static bool IsPureAsk(string sentence)
    {
        var question = sentence.EndsWith('?') && Regex.IsMatch(sentence,
            @"^(what|which|where|when|who|whom|whose|why|how|could|can|would|will|do|does|did|is|are|was|were|have|has|had|should|shall|may|might|any|anything)\b", RegexOptions.IgnoreCase);
        sentence = Regex.Replace(sentence, @"^please,\s*(?=(could|can|would|will) you\b)", "", RegexOptions.IgnoreCase);
        var request = Regex.IsMatch(sentence, @"^(please|could you|can you|would you|will you|let (us|me) know((\s+(if|whether|when|how|what|which|once))\b|[.!]?$))", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(sentence, @"^please\s*[,—–-]|^please (note|be (aware|advised|informed)|keep in mind|remember|know|understand|rest assured)\b", RegexOptions.IgnoreCase);
        // A link or a time isn't a clause: its colon doesn't count.
        return (question || request) && !AsksWithAFact().IsMatch(Regex.Replace(Regex.Replace(sentence, @"https?://\S+?(?=[.,:;!?)]*(\s|$))", "LINK"), @"\d:\d", "0"));
    }

    /// <summary>What turns an ask into a statement (D98), the words the writer instructions name: a reason ("because",
    /// "since", "given", "although", "though", "due to", "now that", "so that/we/I/you/it", ", as" or "as it/we/they…"), a
    /// colon, semicolon, dash, bracket or ellipsis, a clause (", which", ", who", ", that", ", where", "which is…"),
    /// "confirm", "verify" or "agree", "is it true that", and "did you know" / "are you aware" / "have you noticed".
    /// "Which version…", "tell us which is…", "as an attachment" and "as soon as it…" are asks.</summary>
    [GeneratedRegex(@"[;:—–…()\[\]]|\s-\s|\.\.\.|,\s*(which|who|whom|that|where|as)\b|.(?<!\b(tell (us|me)|show (us|me)|know|ask|check|see|wonder)\s)\b(which|who)\s+(is|are|was|were|has|have|had|does|do|did|means|causes?|fixes|breaks?|shows?)\b|\b(because|since|given|although|though|due to|now that)\b|(?<!\b(soon|well|long|much)\s)\bas\s+(it|we|they|there|this|that|these|those|I)\b|\bso\s+(that|we|I|you|it)\b|\b(confirm|verify|agree)\b|\b(is it|it's|it is|isn't it)\s+(true|correct|right|the case)\s+that\b|\b(did you know|were you aware|are you aware|have you noticed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsksWithAFact();


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

    static string DraftId(TicketPaths paths, JsonObject record) => $"{paths.Key}/{record["kind"]}-{record["tone"]}";

    static void Save(TicketPaths paths, JsonObject record)
    {
        var errors = SchemaRegistry.Embedded.Validate(Schema, record.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_draft", "The draft broke its contract (a stangent bug).", ExitCodes.Failed, errors);
        var kind = (string)record["kind"]!;
        var tone = (string)record["tone"]!;
        Quotes.WriteAtomic(JsonPath(paths, kind, tone), CanonicalJson.Serialize(record));
        Quotes.WriteAtomic(Path.ChangeExtension(JsonPath(paths, kind, tone), ".md"), Markdown(record));
    }

    /// <summary>The draft for the person who posts it: the text as it will be posted, then each claim's evidence.</summary>
    public static string Markdown(JsonObject record)
    {
        var sb = new StringBuilder($"<!-- {record["key"]} · {record["kind"]} · {record["tone"]} · {(record["posted"] is null ? "draft: post with `stangent ticket post`" : $"posted {record["posted"]!["at"]} by {record["posted"]!["by"]}")} -->\n\n");
        sb.Append((string)record["text"]!).Append("\n\n---\n\n**Checked claims**");
        sb.Append("\n\n");
        foreach (var c in record["claims"]!.AsArray())
            sb.Append($"- {c!["id"]}: ").Append(string.Join("; ", c["links"]!.AsArray().Select(l => l!["kind"] + ((string?)l["quote"] is { } q ? $" \"{q}\"" : "")
                + ((string?)l["page"] ?? (string?)l["file"] ?? (string?)l["field"] ?? ((string?)l["section"] is { } s ? $" {l["doc"]} §{s}" : "")) switch { "" => "", var x => $" ({x})" }))).Append('\n');
        List(sb, record["unchecked"], "Kept with no claim (questions, requests, greetings, headings: not checked, read them)");
        List(sb, record["dropped"], "Left out (no claim backed them, D97)");
        return sb.ToString();
    }

    static void List(StringBuilder sb, JsonNode? lines, string title)
    {
        if (lines is not JsonArray a || a.Count == 0) return;
        sb.Append($"\n**{title}**\n\n");
        foreach (var l in a) sb.Append($"- {(string)l!}\n");
    }

    // ---- post ----

    /// <summary>
    /// Posts a checked draft under the signed-in person's own login (D74) and records who posted it and when. Nothing
    /// else is ever written to the ticket. A draft already posted isn't posted again unless <paramref name="again"/>, and a
    /// draft whose inputs have changed since it was checked isn't posted unless <paramref name="stale"/> (D108).
    /// </summary>
    public static async Task<JsonObject> PostAsync(StangentRepo repo, IIssueTracker tracker, string key, string draft, string who, bool again, TimeProvider time, CancellationToken ct = default, bool stale = false)
    {
        var paths = new TicketPaths(repo.Root, key);
        var file = draft.EndsWith(".json", StringComparison.Ordinal) ? Path.GetFullPath(Path.Combine(repo.Root, draft))
            : Path.Combine(Dir(paths), (draft.Contains('-') ? draft : $"{draft}-{repo.Config.Tickets.Tone}") + ".json");
        var record = PageRun.LoadJson(file) as JsonObject
            ?? throw new StangentException("no_draft", $"No draft at {paths.Relative(file)}. Run `stangent ticket draft {key} --kind …` first.", ExitCodes.NotFound);
        if ((string?)record["key"] != paths.Key) throw new StangentException("wrong_ticket", $"That draft is for {record["key"]}, not {paths.Key}.", ExitCodes.Usage);
        if (record["posted"] is not null && !again)
            throw new StangentException("already_posted", $"This draft was posted by {record["posted"]!["by"]} at {record["posted"]!["at"]} (comment {record["posted"]!["commentId"]}). Use --again to post it twice.", ExitCodes.Usage);
        if (!stale && NotCurrent(repo, paths, record) is { } why)
        {
            var pr = (string?)record["pr"]?["range"] is { } range ? $" --pr {range}{((string?)record["pr"]?["url"] is { } url ? $" --pr-url {url}" : "")}" : "";
            // The range as given may be relative: repeated as it is, it would now mean other commits.
            var fixedRange = (string?)record["pr"]?["range"] is not { } given || Regex.IsMatch(given, "^[0-9a-f]{7,40}\\.\\.[0-9a-f]{7,40}$");
            throw new StangentException("draft_stale", $"{why} Make it again with `stangent ticket draft {paths.Key} --kind {record["kind"]} --tone {record["tone"]}{pr}`{(fixedRange ? "" : ", with the PR's own commits in place of that range")}, or use --stale to post it as it is.", ExitCodes.Usage);
        }
        var posted = await tracker.PostCommentAsync(paths.Key, (string)record["text"]!, ct);
        StateChanges.Move(repo.Root, StateKinds.DraftPosted, DraftId(paths, record), record["posted"] is null ? null : "posted", "posted", StateActor.Person(who), "posted to the ticket");
        record["posted"] = new JsonObject { ["by"] = who, ["at"] = time.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture), ["commentId"] = posted.CommentId };
        Save(paths, record);
        return record;
    }

    /// <summary>Why a recorded draft can't be called current (D108): what it was made from has changed (the ticket, its
    /// bundle, the proof, the PR's range, the writer's instructions), or that can't be worked out any more.</summary>
    static string? NotCurrent(StangentRepo repo, TicketPaths paths, JsonObject record)
    {
        try
        {
            var (_, input) = Inputs(repo, TicketContext.Load(repo), paths.Key, (string)record["kind"]!, (string)record["tone"]!, (string?)record["pr"]?["range"], (string?)record["pr"]?["url"]);
            return CanonicalJson.Hash(input) == (string?)record["inputs"] ? null : "What this draft was made from has changed since it was checked (the ticket, its bundle, the proof, the PR's range, which a later commit can move, or the writer's instructions).";
        }
        // Anything that stops the comparison, a damaged file too, means it can't be called current; refusing is the safe side.
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return $"Can't tell whether this draft is still current: {e.Message}.";
        }
    }

    static string Short(string? s) => s is null ? "(no quote)" : s.Length > 80 ? s[..80] + "…" : s;
}

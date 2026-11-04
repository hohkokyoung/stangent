using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Graph.Impact;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>One symbol's code as a bundle shows it. A quote has to be in here, at these lines.</summary>
public sealed record DisproveCode(string Symbol, string Path, int Start, int End, IReadOnlyList<string> Lines);

/// <summary>What the disprover reads, and what code checks its answer against.</summary>
public sealed record DisproveBundle(string Text, string Inputs, IReadOnlyList<DisproveCode> Code, IReadOnlyList<string> Missing);

/// <summary>
/// A second, fresh agent tries to refute each finding of a section from the code (2.5, D119, reversing D62's "rejected"). It follows the
/// other steps: <c>prepare</c>, one turn, no tools (D66), structured output, <c>check</c> by code (D18). One call per finding. An unchanged
/// bundle means no call. It never sees who found the finding or why they thought so (D61): the entry's <c>by</c>, <c>check</c> and
/// <c>note</c> stay out of the bundle. It reads nothing and changes nothing another step hashes.
/// </summary>
public static class DisproveRun
{
    public const string Disprover = "stangent-finding-disprover";
    public const string OutputSchema = "finding-disprover-output.v1.json";

    /// <summary>The size rule: at most this many characters of code in one bundle. Whole symbols only, in the order of
    /// <see cref="Choose"/>. What doesn't fit is named, so the agent knows what it can't see.</summary>
    public const int MaxCodeChars = 60_000;

    /// <summary>At most this many symbols or entry points named in a list. The rest are counted.</summary>
    const int MaxNamed = 30;

    static readonly StateActor Who = StateActor.Code("stangent fsd disprove");

    static string Work(string root, string doc, string section) =>
        Path.Combine(root, ".stangent", "fsd-disprove", doc, PageList.Slug(section, LinkRun.Key(doc, section)));

    /// <summary>Starts a section's run: the first finding that needs an answer, or nothing. Off unless <c>fsd.disprove</c> is on.</summary>
    public static RunStep Prepare(PageSet set, StangentRepo repo, string doc, string sectionId) =>
        !repo.Config.Fsd.Disprove ? new RunStep("off", "none")
        : !repo.Config.Fsd.Findings ? new RunStep("off", "none", Errors: [StangentComponents.MissingNeed(repo.Config, "disprove")!])
        : Next(set, repo, doc, sectionId, false);

    public static RunStep Check(PageSet set, StangentRepo repo, string doc, string sectionId, Usage? usage)
    {
        var work = Work(repo.Root, doc, sectionId);
        if (PageRun.LoadJson(Path.Combine(work, "state.json")) is not JsonObject state)
            throw new StangentException("not_prepared", $"No disprove run for {doc} §{sectionId}. Run `stangent fsd disprove prepare` first.", ExitCodes.Usage);
        if ((string?)state["stage"] != "disprove")
            throw new StangentException("nothing_to_check", $"{doc} §{sectionId}'s disprove run is {(string?)state["stage"]}. Run `stangent fsd disprove prepare` to start again.", ExitCodes.Usage);
        if (usage is not null)
            LinkRun.UpdateRun(repo.Root, LinkRun.Key(doc, sectionId), r => r["disprover"] = Usage.FromJson(r["disprover"]).Add(usage).ToJson());
        var model = usage?.Model ?? (string?)state["agent"];
        if (model is not null) state["agent"] = model;

        var errors = new List<string>();
        var text = File.Exists(Path.Combine(work, "output.json")) ? File.ReadAllText(Path.Combine(work, "output.json")) : "";
        var parsed = PageChecks.Parse(text, errors);
        var output = parsed as JsonObject;
        if (errors.Count == 0 && output is null) errors.Add($"The answer has to be one JSON object, and this is {parsed?.GetValueKind().ToString().ToLowerInvariant() ?? "null"}.");
        if (errors.Count == 0) errors.AddRange(SchemaRegistry.Embedded.Validate(OutputSchema, output!.ToJsonString()).Select(e => $"Schema: {e}"));
        var code = (JsonNode.Parse(File.ReadAllText(Path.Combine(work, "code.json"))) as JsonArray ?? []).OfType<JsonObject>()
            .Select(c => new DisproveCode((string)c["symbol"]!, (string)c["path"]!, (int)c["start"]!, (int)c["end"]!, [.. c["lines"]!.AsArray().Select(l => (string)l!)])).ToList();
        var symbol = errors.Count == 0 ? CheckQuote(output!, code, errors) : null;
        if (errors.Count > 0 && LinkRun.Retry(repo.Root, work, state, errors, Disprover, "bundle.md", "output.json") is { } retry) return retry;

        var entry = (string)state["entry"]!;
        var inputs = (string)state["inputs"]!;
        if (errors.Count > 0)
        {
            // Kept only when it's a verdict at all: anything else would break the file's own schema, and the first error already says what came back.
            var claimed = output?["verdict"] is JsonValue said && said.TryGetValue<string>(out var word) && StateKinds.Verdicts.Contains(word) ? word : null;
            Write(repo, entry, "cant-tell", Disproof(inputs, $"The answer failed the code check twice: {errors[0]}", model, claimed: claimed), "its answer failed the code check twice");
        }
        else
        {
            var verdict = (string)output!["verdict"]!;
            var disproof = Disproof(inputs, (string)output["why"]!, model, suggested: (string?)output["suggestedSeverity"]);
            if (verdict != "cant-tell")
            {
                disproof["path"] = (string)output["path"]!;
                disproof["start"] = (int)output["start"]!;
                disproof["end"] = (int)output["end"]!;
                disproof["quote"] = (string)output["quote"]!;
                disproof["symbol"] = symbol;
                disproof["commit"] = (string)state["commit"]!;
            }
            Write(repo, entry, verdict, disproof, "the code check passed");
        }
        StateChanges.Apply(repo.Root, state, StateKinds.DisproveRun, LinkRun.Key(doc, sectionId), "done", Who, "recorded");
        Save(work, state);
        return Next(set, repo, doc, sectionId, true);
    }

    /// <summary>The code check (D18): the verdict has the quote it needs, and it's word for word in the bundle's code at its path and lines. Returns the symbol it's in.</summary>
    static string? CheckQuote(JsonObject output, List<DisproveCode> code, List<string> errors)
    {
        if ((string)output["verdict"]! == "cant-tell") return null;
        if ((string?)output["path"] is not { } path || output["start"] is null || output["end"] is null || (string?)output["quote"] is not { } quote)
        {
            errors.Add($"a {(string)output["verdict"]!} verdict needs path, start, end and quote");
            return null;
        }
        if (output["start"] is not JsonValue from || !from.TryGetValue<int>(out var start) || output["end"] is not JsonValue to || !to.TryGetValue<int>(out var end))
        {
            errors.Add("start and end have to be line numbers shown in the code you were given");
            return null;
        }
        var at = code.FirstOrDefault(c => c.Path == path && c.Start <= start && end <= c.End && start <= end);
        if (at is null)
        {
            errors.Add($"`{path}` lines {start}-{end} aren't shown in the code you were given. Use a path and lines exactly as they're shown.");
            return null;
        }
        var lines = at.Lines.Skip(start - at.Start).Take(end - start + 1).Select(Quotes.Normalize).ToList();
        if (Quotes.InSource(quote, lines)) return at.Symbol;
        errors.Add($"the quote \"{(quote.Length > 80 ? quote[..80] + "…" : quote)}\" isn't word for word at `{path}` lines {start}-{end}, without the line numbers (at least {Quotes.MinSourceChars} characters, and two words or a whole line)");
        return null;
    }

    static JsonObject Disproof(string inputs, string why, string? agent, string? suggested = null, string? claimed = null)
    {
        var o = new JsonObject { ["inputs"] = inputs, ["why"] = why };
        if (agent is not null) o["agent"] = agent;
        if (suggested is not null) o["suggestedSeverity"] = suggested;
        if (claimed is not null) o["claimed"] = claimed;
        return o;
    }

    /// <summary>The finding still waiting for an answer, or the end of the run. A verdict on what's since changed drops back before anything else happens, so a closed finding reopens even if the call never comes.</summary>
    static RunStep Next(PageSet set, StangentRepo repo, string doc, string sectionId, bool worked)
    {
        if (ClearCommit(set.Graph) is { } unclear) return new RunStep("failed", "none", Errors: [unclear]);
        var index = SpecIndex.Load(repo.Root);
        AttachmentChecks.Load(index, repo);
        var section = index.Sections.GetValueOrDefault((doc, sectionId))
            ?? throw new StangentException("not_found", $"No section {sectionId} in FSD {doc}. Run `stangent fsd import` first.", ExitCodes.NotFound);
        var analyzer = new Lazy<ImpactAnalyzer>(() => new ImpactAnalyzer(set.Graph));
        foreach (var finding in Pending(TraceFile.Load(repo.Root), doc, sectionId).ToList())
        {
            var bundle = Build(set, index, analyzer, doc, section, finding);
            if ((string?)finding["disproof"]?["inputs"] == bundle.Inputs) continue;
            var entry = (string)finding["id"]!;
            // No code to read means nothing for an agent to do: a call would only cost money.
            if (bundle.Code.Count == 0)
            {
                Write(repo, entry, "cant-tell", Disproof(bundle.Inputs, "Code found no code to check this against" + (bundle.Missing.Count > 0 ? $": {bundle.Missing[0]}" : "."), null), "no code to check it against");
                worked = true;
                continue;
            }
            if (finding["verdict"] is not null) Drop(repo.Root, entry);
            AiPolicy.EnsureAllowed(repo.Config, "fsd disprove prepare");
            var work = Work(repo.Root, doc, sectionId);
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(work, "bundle.md"), bundle.Text);
            File.WriteAllText(Path.Combine(work, "code.json"), CanonicalJson.Serialize(new JsonArray([.. bundle.Code.Select(c => (JsonNode)new JsonObject
            {
                ["symbol"] = c.Symbol, ["path"] = c.Path, ["start"] = c.Start, ["end"] = c.End, ["lines"] = new JsonArray([.. c.Lines.Select(l => (JsonNode)l)]),
            })])));
            var state = new JsonObject
            {
                ["doc"] = doc, ["section"] = sectionId, ["entry"] = entry, ["inputs"] = bundle.Inputs, ["commit"] = SymbolLines.CommitOf(set.Graph), ["retryUsed"] = false, ["agent"] = Disprover,
            };
            StateChanges.Apply(repo.Root, state, StateKinds.DisproveRun, LinkRun.Key(doc, sectionId), "disprove", Who, "prepared");
            Save(work, state);
            return LinkRun.Step(repo.Root, work, Disprover, "bundle.md", "output.json", "Finding and code");
        }
        return new RunStep(worked ? "recorded" : "current", "none");
    }

    /// <summary>Why lines from this graph can't be cited at all, or null. Then no verdict could carry a quote, and it isn't the finding's fault.</summary>
    static string? ClearCommit(CodeGraph graph) =>
        SymbolLines.CommitOf(graph) is null ? "the graph wasn't built at a git commit, so no code line can be cited; build it from a commit"
        : graph.DirtyFiles is null || graph.NotInCommit is null ? "the graph doesn't say whether its tree was clean, so no code line can be cited; rebuild it"
        : null;

    /// <summary>A section's findings nobody has decided. A person's decision stands, so it isn't tested again.</summary>
    static IEnumerable<JsonObject> Pending(JsonObject trace, string doc, string sectionId) => TraceFile.Sorted(trace["entries"]!.AsArray())
        .Where(e => TraceFindings.IsFinding(e) && !TraceFindings.IsGone(e) && e["decision"] is null && (string?)e["fsd"]?["doc"] == doc && (string?)e["fsd"]?["section"] == sectionId);

    /// <summary>Writes a verdict through the state module, and the finding's evidence with it (D118's Stamp derives it from what's here).</summary>
    static void Write(StangentRepo repo, string entry, string verdict, JsonObject disproof, string reason)
    {
        using var _ = LinkRun.Lock(repo.Root);
        var trace = TraceFile.Load(repo.Root);
        var e = trace["entries"]!.AsArray().OfType<JsonObject>().FirstOrDefault(x => (string)x["id"]! == entry);
        if (e is null) return; // the links were judged again meanwhile: this finding is gone, and so is what the answer was about.
        TraceFindings.ClearReview(repo.Root, e, Who, "there's a new verdict");
        StateChanges.Apply(repo.Root, e, StateKinds.FindingVerdict, entry, verdict, Who, reason);
        e["disproof"] = disproof;
        e["disproof_hash"] = TraceFile.DisproofHash(verdict, disproof);
        TraceFindings.Stamp(repo.Root, trace, SpecIndex.Load(repo.Root), null, []);
        TraceFile.Save(repo.Root, trace);
    }

    static void Drop(string root, string entry)
    {
        using var _ = LinkRun.Lock(root);
        var trace = TraceFile.Load(root);
        var e = trace["entries"]!.AsArray().OfType<JsonObject>().First(x => (string)x["id"]! == entry);
        TraceFindings.ClearReview(root, e, Who, "what its verdict was judged on changed");
        StateChanges.Apply(root, e, StateKinds.FindingVerdict, entry, "unverified", Who, "what it was judged on changed");
        TraceFile.Save(root, trace);
    }

    static void Save(string work, JsonObject state) => File.WriteAllText(Path.Combine(work, "state.json"), CanonicalJson.Serialize(state));

    // ---- the bundle ----

    /// <summary>
    /// By code. The finding as a plain claim, never its finder's words; the FSD's text; the page's statements; the code; and the entry points
    /// that reach it, from the impact walk. The agent has no tools, so what isn't here it can't see, and this says what's missing.
    /// <c>Inputs</c> hashes what a verdict rests on, not the whole text: the commit, the section's number and where a symbol starts are shown
    /// but left out, so a new commit, a moved section or code pushed down a few lines keeps a verdict, and changed words or code don't.
    /// </summary>
    public static DisproveBundle Build(PageSet set, SpecIndex index, Lazy<ImpactAnalyzer> analyzer, string doc, JsonObject section, JsonObject finding)
    {
        var state = (string)finding["state"]!;
        var fsdQuote = (string?)finding["fsd"]?["quote"];
        var pageId = (string?)finding["page"]?["id"];
        var statementId = (string?)finding["page"]?["statement"];
        var pageQuote = (string?)finding["page"]?["quote"];
        var page = pageId is null ? null : index.Pages.GetValueOrDefault(pageId);

        var sb = new StringBuilder();
        var rests = new StringBuilder();
        void Both(string text) { sb.Append(text); rests.Append(text); }

        Both("# Instructions\n\n" + Bundles.Instructions("finding-disprover.md"));
        Both("\n# Output schema\n\n```json\n" + SchemaRegistry.EmbeddedText(OutputSchema).TrimEnd('\n') + "\n```\n\n");
        sb.Append(LinkRun.SectionBlock(section, doc));
        rests.Append($"# The FSD section\n\n- document: `{doc}`\n- title: {(string)section["title"]!}\n\n{(string)section["text"]!}\n");
        Both(AttachmentChecks.Block(section));
        Both("\n# The claim\n\n");
        Both(state switch
        {
            "conflict" => $"The application does something different from this FSD sentence: \"{fsdQuote}\". The page says: \"{pageQuote}\".\n",
            "gap" => $"The application doesn't do what this FSD sentence says: \"{fsdQuote}\".\n",
            _ => $"The application does this, and the FSD doesn't say it: \"{pageQuote}\".\n",
        });

        Both("\n# The reference page\n\n");
        if (page is null) Both(pageId is null ? "No page is linked to this claim.\n" : $"`{pageId}` has no recorded version.\n");
        else
        {
            Both($"`{pageId}`: {(string)page["title"]!}. Generated from the code; it may have left things out.\n\n");
            foreach (var (_, id, s) in PageChecks.Statements(page))
                Both($"- **{id}**{(id == statementId ? " (the claim's)" : "")} {(string)s["text"]!}\n");
        }

        var (code, missing, missingHashed) = Choose(set, page, pageId, statementId, $"{fsdQuote} {pageQuote}");
        sb.Append($"\n# The code\n\nAt commit `{SymbolLines.CommitOf(set.Graph)}`. ");
        rests.Append("\n# The code\n\n");
        Both($"Whole symbols only, up to {MaxCodeChars} characters: the claim's own cited symbols first, then ");
        Both("what the page's other statements cite, then the rest of the page's slice and the slices of the endpoints it reaches, each group by how many of the claim's words it holds.\n");
        foreach (var c in code)
        {
            sb.Append($"\n## `{c.Path}` lines {c.Start}-{c.End}: `{c.Symbol}`\n\n````text\n");
            rests.Append($"\n## `{c.Path}`: `{c.Symbol}`\n\n````text\n");
            for (var i = 0; i < c.Lines.Count; i++)
            {
                sb.Append($"{c.Start + i}: {c.Lines[i]}\n");
                rests.Append($"{c.Lines[i]}\n");
            }
            Both("````\n");
        }
        if (code.Count == 0) Both("\nNone.\n");
        Both("\n# Couldn't be shown\n\n");
        if (missing.Count == 0) Both("Nothing: every symbol looked at is shown above.\n");
        foreach (var m in missing.Take(MaxNamed)) sb.Append($"- {m}\n");
        foreach (var m in missingHashed.Take(MaxNamed)) rests.Append($"- {m}\n");
        if (missing.Count > MaxNamed) Both($"- and {missing.Count - MaxNamed} more\n");

        Both("\n# The entry points that reach it\n\n");
        var cited = statementId is not null && pageId is not null && index.Statement(pageId, statementId) is { } statement
            ? statement["cites"]!.AsArray().Select(c => (string)c!).ToList() : [];
        var entryPoints = new StringBuilder();
        AppendEntryPoints(entryPoints, set, analyzer, pageId, cited);
        Both(entryPoints.ToString());

        return new DisproveBundle(sb.ToString(), CanonicalJson.Hash(rests.ToString()), code, missing);
    }

    /// <summary>The code to show, under the size rule. The claim's own cited symbols come first, because that's what it rests on; and a gap has none,
    /// so the rest of the slice is where a page's omission shows up.</summary>
    static (List<DisproveCode> Code, List<string> Missing, List<string> MissingHashed) Choose(PageSet set, JsonObject? page, string? pageId, string? statementId, string claim)
    {
        var words = Regex.Matches(claim.ToLowerInvariant(), "[a-z0-9]{3,}").Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        var lookup = SymbolLines.Lookup(set.Graph);
        var statements = page is null ? [] : PageChecks.Statements(page).ToList();
        var own = statements.Where(s => s.Id == statementId).SelectMany(s => s.Statement["cites"]!.AsArray().Select(c => (string)c!)).Distinct(StringComparer.Ordinal).ToList();
        var others = statements.SelectMany(s => s.Statement["cites"]!.AsArray().Select(c => (string)c!)).Distinct(StringComparer.Ordinal).Except(own).ToList();
        // A screen's slice stops at the API (D63), and the rule a claim is about is often behind it, so the pages of the endpoints it reaches count too.
        var slice = new List<string>();
        if (pageId is not null && set.Slices.TryGetValue(pageId, out var found))
        {
            static IEnumerable<string> Bodies(Slice s) => s.Nodes.Where(n => n.Role is "walk" or "handler" && n.Node.Kind is NodeKind.Method or NodeKind.Component or NodeKind.Template).Select(n => n.Node.Id);
            var stops = found.Nodes.Where(n => n.Role == "stop").Select(n => n.Node.Id).ToHashSet(StringComparer.Ordinal);
            slice = [.. Bodies(found).Concat(set.Pages.Where(p => stops.Contains(p.Entry) && set.Slices.ContainsKey(p.Id)).SelectMany(p => Bodies(set.Slices[p.Id]))).Distinct(StringComparer.Ordinal).Except(own).Except(others)];
        }

        var code = new List<DisproveCode>();
        var missing = new List<string>();
        var missingHashed = new List<string>();
        void Miss(string shown, string? hashed = null) { missing.Add(shown); missingHashed.Add(hashed ?? shown); }
        var used = 0;
        foreach (var (group, named) in new[] { (own, true), (others, true), (slice, false) })
        {
            var candidates = new List<(string Id, SymbolLines? At, string[]? Lines, int Score)>();
            foreach (var id in group)
            {
                var at = lookup(id);
                var lines = at?.File is not null && at.LinesMatchCommit && at.StartLine is not null && at.EndLine is not null ? set.Source.Lines(at.File) : null;
                if (lines is null || at!.StartLine > lines.Length)
                {
                    if (named) Miss($"`{id}`: {(at is null ? "it isn't in the code graph any more" : at.Note ?? "its file can't be read")}");
                    continue;
                }
                var body = lines.Skip(at.StartLine!.Value - 1).Take(at.EndLine!.Value - at.StartLine.Value + 1).ToArray();
                var said = (id + " " + string.Join(' ', body)).ToLowerInvariant();
                candidates.Add((id, at, body, words.Count(said.Contains)));
            }
            foreach (var (id, at, body, _) in candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Lines!.Length).ThenBy(c => c.Id, StringComparer.Ordinal))
            {
                int start = at!.StartLine!.Value, end = start + body!.Length - 1;
                // Inside a symbol already shown, as a member is inside its type: its lines are there.
                if (code.Any(c => c.Path == at.File && c.Start <= start && end <= c.End)) continue;
                var size = body.Sum(l => l.Length + 1);
                if (used + size > MaxCodeChars)
                {
                    Miss($"`{id}` (`{at.File}` lines {start}-{end}): over the bundle's size limit", $"`{id}` (`{at.File}`): over the bundle's size limit");
                    continue;
                }
                used += size;
                code.Add(new DisproveCode(id, at.File!, start, end, body));
                if (at.Note is { } note) Miss($"`{id}`: {note}");
            }
        }
        return (code, missing, missingHashed);
    }

    /// <summary>The screens and endpoints the impact walk gets to, from what the claim cites, or else from the page's own entry point. Or that it gets to none.</summary>
    static void AppendEntryPoints(StringBuilder sb, PageSet set, Lazy<ImpactAnalyzer> analyzer, string? pageId, List<string> cited)
    {
        var entries = set.Pages.Where(p => p.Kind != PageKind.Component).ToDictionary(p => p.Entry, p => p.Id, StringComparer.Ordinal);
        var starts = cited.Select(analyzer.Value.Find).OfType<Node>().ToList();
        if (starts.Count == 0 && pageId is not null && set.Find(pageId) is { } def && analyzer.Value.Find(def.Entry) is { } entry) starts = [entry];
        if (starts.Count == 0)
        {
            sb.Append("The walk had nowhere to start: this claim cites no code.\n");
            return;
        }
        var reached = starts.Select(s => (s.Id, Depth: 0)).Concat(analyzer.Value.AffectedWithDepth(starts).Select(a => (a.Node.Id, a.Depth)))
            .Where(r => entries.ContainsKey(r.Id)).OrderBy(r => r.Depth).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        if (reached.Count == 0)
        {
            sb.Append("None found. The graph can miss a path made by reflection or configuration, so this alone doesn't prove the code is never run.\n");
            return;
        }
        foreach (var (id, depth) in reached.Take(MaxNamed)) sb.Append($"- `{id}` ({depth} line(s) away)\n");
        if (reached.Count > MaxNamed) sb.Append($"- and {reached.Count - MaxNamed} more\n");
    }

    /// <summary>What a run on this document would send, from the bundles alone: no AI, nothing written. For saying the cost before paying it.</summary>
    public static JsonObject Estimate(PageSet set, StangentRepo repo, string doc, string? chapter = null)
    {
        if (ClearCommit(set.Graph) is { } unclear) throw new StangentException("graph_not_clear", unclear, ExitCodes.Failed);
        var index = SpecIndex.Load(repo.Root);
        AttachmentChecks.Load(index, repo);
        var analyzer = new Lazy<ImpactAnalyzer>(() => new ImpactAnalyzer(set.Graph));
        var trace = TraceFile.Load(repo.Root);
        // One chapter on its own, to see what it costs before the whole document (E.3).
        var only = chapter is null ? null : FsdOutline.Chapter(FsdOutline.Load(repo.Root, doc), chapter).Select(s => (string)s["id"]!).ToHashSet(StringComparer.Ordinal);
        var findings = trace["entries"]!.AsArray().OfType<JsonObject>().Where(TraceFindings.IsFinding)
            .Where(f => only is null || (string?)f["fsd"]?["doc"] == doc && only.Contains((string?)f["fsd"]?["section"] ?? "")).ToList();
        var calls = new List<(string Section, DisproveBundle Bundle)>();
        var noCode = 0;
        foreach (var ((d, sectionId), section) in index.Sections.Where(s => s.Key.Doc == doc && (only is null || only.Contains(s.Key.Section))).OrderBy(s => TraceFile.SectionKey(s.Key.Section), StringComparer.Ordinal))
            foreach (var finding in Pending(trace, doc, sectionId))
            {
                var bundle = Build(set, index, analyzer, d, section, finding);
                if (bundle.Code.Count == 0) noCode++;
                else calls.Add((sectionId, bundle));
            }
        var chars = calls.Select(c => c.Bundle.Text.Length).Order().ToList();
        return new JsonObject
        {
            ["doc"] = doc,
            ["chapter"] = chapter,
            ["findings"] = findings.Count,
            ["notTested"] = findings.Count(f => f["fsd"] is null),
            ["decided"] = findings.Count(f => f["fsd"] is not null && f["decision"] is not null),
            ["noCodeSoNoCall"] = noCode,
            ["calls"] = calls.Count,
            ["bundleChars"] = new JsonObject
            {
                ["total"] = chars.Sum(c => (long)c), ["median"] = chars.Count == 0 ? 0 : chars[chars.Count / 2], ["largest"] = chars.Count == 0 ? 0 : chars[^1],
            },
            ["atTheCodeLimit"] = calls.Count(c => c.Bundle.Missing.Any(m => m.EndsWith("size limit", StringComparison.Ordinal))),
            ["withSomethingNotShown"] = calls.Count(c => c.Bundle.Missing.Count > 0),
        };
    }
}

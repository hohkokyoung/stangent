using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Spec.Pages;

/// <summary>
/// Where a page's files live. Committed (D52): <c>specs/reference/&lt;path&gt;.json</c> and <c>.md</c>, and
/// <c>specs/reference/failed.json</c>. Not committed: the run's bundle, AI outputs and state under <c>.stangent/spec/</c>.
/// </summary>
public sealed class SpecPaths(string root)
{
    public string Root => root;
    public string ReferenceDir => Path.Combine(root, "specs", "reference");
    public string FailedFile => Path.Combine(ReferenceDir, "failed.json");
    public string PageJson(PageDef p) => Path.Combine(ReferenceDir, p.Path + ".json");
    public string PageMarkdown(PageDef p) => Path.Combine(ReferenceDir, p.Path + ".md");
    public string Work(PageDef p) => Path.Combine(root, ".stangent", "spec", p.Path);
    public string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}

/// <summary>The next thing a driver (the plugin command, CI, the benchmark) does for a page.</summary>
public sealed record RunStep(string Status, string Action, string? Agent = null, string? Prompt = null, string? Output = null,
    IReadOnlyList<string>? Errors = null, Usage? Usage = null)
{
    public JsonObject ToJson(string page)
    {
        var o = new JsonObject { ["page"] = page, ["status"] = Status };
        var next = new JsonObject { ["action"] = Action };
        if (Agent is not null) next["agent"] = Agent;
        if (Prompt is not null) next["prompt"] = Prompt;
        if (Output is not null) next["output"] = Output;
        if (Agent is not null && SchemaOf(Agent) is { } schema) next["schema"] = schema;
        o["next"] = next;
        if (Errors is { Count: > 0 }) o["errors"] = new JsonArray([.. Errors.Select(e => (JsonNode)e)]);
        return o;
    }

    /// <summary>The output schema a step's result must match, so a single-turn driver (D64) can ask for structured output.</summary>
    public static string? SchemaOf(string agent) => agent switch
    {
        PageRun.Writer => Bundles.WriterSchema,
        PageRun.Verifier => Bundles.VerifierSchema,
        Stangent.Spec.Fsd.LinkRun.Finder => "candidate-finder-output.v1.json",
        Stangent.Spec.Fsd.LinkRun.Judge => "link-judge-output.v1.json",
        Stangent.Spec.Fsd.LinkRun.Checker => "agree-checker-output.v1.json",
        _ => null,
    };
}

/// <summary>
/// One page's AI step, as a small state machine (D60, D62): <c>prepare</c> writes the bundle, the writer writes the page,
/// <c>check</c> runs the code checks (one retry), then the fresh verifier's findings go back to the writer once, and the
/// page is recorded. All the logic is here, in code; a driver only runs the subagent each step names.
/// </summary>
public static class PageRun
{
    public const string Writer = "stangent-page-writer";
    public const string Verifier = "stangent-page-verifier";

    public static RunStep Prepare(PageSet set, PageDef page, StangentRepo repo, bool retryFailed = false)
    {
        var paths = new SpecPaths(repo.Root);
        var previous = LoadJson(paths.PageJson(page)) as JsonObject;
        var bundle = Bundles.Build(set, page, previous);
        if ((string?)previous?["generated"]?["bundleHash"] == bundle.InputHash) return new RunStep("current", "none");
        var failed = Failed(paths);
        if (!retryFailed && failed.FirstOrDefault(f => (string?)f!["id"] == page.Id) is { } known && (string?)known["bundleHash"] == bundle.InputHash)
            return new RunStep("failed", "none", Errors: [(string)known["reason"]!]);
        if (bundle.Chars > set.Settings.MaxBundleChars)
        {
            var reason = $"slice over the size cap: {bundle.Chars} characters, cap {set.Settings.MaxBundleChars}";
            MarkFailed(paths, page, bundle.InputHash, reason, []);
            return new RunStep("failed", "none", Errors: [reason]);
        }
        AiPolicy.EnsureAllowed(repo.Config, "spec prepare");

        var work = paths.Work(page);
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "bundle.md"), bundle.Text);
        var state = new JsonObject
        {
            ["page"] = page.Id, ["inputHash"] = bundle.InputHash, ["stage"] = "write", ["attempts"] = 0,
            ["checkRetryUsed"] = false, ["fixUsed"] = false, ["checkRetries"] = 0,
            ["writer"] = Usage.None.ToJson(), ["verifier"] = Usage.None.ToJson(),
        };
        Save(work, state);
        return WriteStep(paths, page, state, null);
    }

    public static RunStep Check(PageSet set, PageDef page, StangentRepo repo, Usage? usage)
    {
        var paths = new SpecPaths(repo.Root);
        var work = paths.Work(page);
        if (LoadJson(Path.Combine(work, "state.json")) is not JsonObject state)
            throw new StangentException("not_prepared", $"No run for {page.Id}. Run `stangent spec prepare` first.", ExitCodes.Usage);
        var previous = LoadJson(paths.PageJson(page)) as JsonObject;
        var bundle = Bundles.Build(set, page, previous);
        if (bundle.InputHash != (string)state["inputHash"]!)
            throw new StangentException("inputs_changed", $"{page.Id}: the code or settings changed since prepare. Run `stangent spec prepare` again.");
        var slice = set.Slices[page.Id];
        var stage = (string)state["stage"]!;

        switch (stage)
        {
            case "write" or "fix":
            {
                state["writer"] = Usage.FromJson(state["writer"]).Add(usage ?? Usage.None).ToJson();
                var attempt = (int)state["attempts"]!;
                var errors = new List<string>();
                var output = PageChecks.Parse(ReadOrEmpty(Path.Combine(work, $"writer-{attempt}.json")), errors);
                var findings = stage == "fix" ? FindingsFrom(state) : null;
                if (errors.Count == 0) errors = PageChecks.CheckWriter(output, slice, set.Builder, bundle, findings);
                if (errors.Count > 0)
                {
                    if ((bool)state["checkRetryUsed"]!)
                        return Fail(paths, page, state, work, "failed the code check twice", errors);
                    state["checkRetryUsed"] = true;
                    state["checkRetries"] = 1;
                    Save(work, state);
                    return WriteStep(paths, page, state, Feedback(errors, findings)) with { Status = "retry", Errors = errors };
                }
                state["staged"] = output!.DeepClone();
                if (stage == "fix") return Record(set, paths, page, state, work, bundle);
                state["stage"] = "verify";
                Save(work, state);
                File.WriteAllText(Path.Combine(work, "verify.md"), VerifyPage(page, (JsonObject)output));
                return new RunStep("verify", "verify", Verifier,
                    $"Bundle: {paths.Relative(Path.Combine(work, "bundle.md"))}\nPage to check: {paths.Relative(Path.Combine(work, "verify.md"))}\n" +
                    $"Write your findings as JSON to: {paths.Relative(Path.Combine(work, "verifier.json"))}",
                    paths.Relative(Path.Combine(work, "verifier.json")));
            }
            case "verify":
            {
                state["verifier"] = Usage.FromJson(state["verifier"]).Add(usage ?? Usage.None).ToJson();
                var errors = new List<string>();
                var output = PageChecks.Parse(ReadOrEmpty(Path.Combine(work, "verifier.json")), errors);
                var staged = (JsonObject)state["staged"]!;
                var (schemaErrors, kept, dropped) = errors.Count > 0 ? (errors, [], 0) : PageChecks.CheckVerifier(output, staged, bundle);
                if (schemaErrors.Count > 0)
                {
                    // The verifier gets one retry too (D60), with the specific errors.
                    if ((bool?)state["verifierRetryUsed"] == true)
                        return Fail(paths, page, state, work, "the verifier's output was missing or broke its schema twice", schemaErrors);
                    state["verifierRetryUsed"] = true;
                    Save(work, state);
                    var verifierOut = Path.Combine(work, "verifier.json");
                    if (File.Exists(verifierOut)) File.Move(verifierOut, Path.Combine(work, "verifier-1.json"), overwrite: true);
                    File.WriteAllText(Path.Combine(work, "verifier-feedback.md"), Feedback(schemaErrors, null));
                    return new RunStep("retry", "verify", Verifier,
                        $"Bundle: {paths.Relative(Path.Combine(work, "bundle.md"))}\nPage to check: {paths.Relative(Path.Combine(work, "verify.md"))}\n" +
                        $"Write your findings as JSON to: {paths.Relative(verifierOut)}\nWhat to fix: {paths.Relative(Path.Combine(work, "verifier-feedback.md"))}",
                        paths.Relative(verifierOut), schemaErrors);
                }
                state["dropped"] = dropped;
                state["findings"] = new JsonArray([.. kept.Select(f => (JsonNode)FindingJson(f))]);
                if (kept.Count == 0) return Record(set, paths, page, state, work, bundle);
                state["stage"] = "fix";
                state["fixUsed"] = true;
                Save(work, state);
                return WriteStep(paths, page, state, Feedback([], kept)) with { Status = "fix" };
            }
            default:
                throw new StangentException("nothing_to_check", $"{page.Id}'s run is {stage}. Run `stangent spec prepare` to start again.", ExitCodes.Usage);
        }
    }

    static RunStep WriteStep(SpecPaths paths, PageDef page, JsonObject state, string? feedback)
    {
        var work = paths.Work(page);
        var attempt = (int)state["attempts"]! + 1;
        state["attempts"] = attempt;
        Save(work, state);
        var output = paths.Relative(Path.Combine(work, $"writer-{attempt}.json"));
        var prompt = new StringBuilder($"Bundle: {paths.Relative(Path.Combine(work, "bundle.md"))}\nWrite the page as JSON to: {output}");
        if (feedback is not null)
        {
            var file = Path.Combine(work, $"feedback-{attempt}.md");
            File.WriteAllText(file, feedback);
            prompt.Append($"\nYour previous attempt: {paths.Relative(Path.Combine(work, $"writer-{attempt - 1}.json"))}");
            prompt.Append($"\nWhat to fix: {paths.Relative(file)}");
        }
        return new RunStep("write", "write", Writer, prompt.ToString(), output);
    }

    static string Feedback(IReadOnlyList<string> errors, IReadOnlyList<Finding>? findings)
    {
        var sb = new StringBuilder();
        if (errors.Count > 0)
        {
            sb.Append("# The code check failed\n\nYour page wasn't accepted. Fix every problem below and write the whole page again ");
            sb.Append("(one more failure and the page is marked failed).\n\n");
            foreach (var e in errors.Take(40)) sb.Append($"- {e}\n");
            if (errors.Count > 40) sb.Append($"- and {errors.Count - 40} more like these\n");
        }
        if (findings is { Count: > 0 })
        {
            sb.Append(errors.Count > 0 ? "\n" : "");
            sb.Append("# A separate reviewer's findings\n\nAnother agent checked your page against the slice's source and flagged the statements below. ");
            sb.Append("For each finding, either fix the statement (change or remove it) or reject the finding with a reason. ");
            sb.Append("Rejecting an `unsupported` finding needs `quote`: the line from the slice's source that supports the statement. ");
            sb.Append("Write the whole page again, with `resolutions`: one entry per finding.\n\n");
            foreach (var f in findings)
            {
                sb.Append($"- **{f.Id}** ({f.Kind}) on {f.Statement}: \"{f.StatementText}\"\n  - {f.Explanation}\n");
                if (f.Quote is not null) sb.Append($"  - contradicting line: `{f.Quote}`\n");
            }
        }
        return sb.ToString();
    }

    static string VerifyPage(PageDef page, JsonObject output)
    {
        var sb = new StringBuilder();
        sb.Append("# Instructions\n\n").Append(Bundles.Instructions("page-verifier.md"));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText(Bundles.VerifierSchema).TrimEnd('\n')).Append("\n```\n");
        sb.Append($"\n# The page\n\n- id: `{page.Id}`\n- title: {(string?)output["title"]}\n");
        foreach (var section in PageChecks.Sections)
        {
            sb.Append($"\n## {section}\n\n");
            var list = PageChecks.Statements(output).Where(s => s.Section == section).ToList();
            if (list.Count == 0) sb.Append("(none)\n");
            foreach (var (_, id, s) in list)
                sb.Append($"- **{id}** {(string)s["text"]!} (cites {string.Join(", ", (s["cites"] as JsonArray ?? []).Select(c => $"`{c}`"))})\n");
        }
        return sb.ToString();
    }

    static RunStep Record(PageSet set, SpecPaths paths, PageDef page, JsonObject state, string work, Bundle bundle)
    {
        var staged = (JsonObject)state["staged"]!;
        var findings = FindingsFrom(state);
        var resolutions = (staged["resolutions"] as JsonArray ?? []).OfType<JsonObject>().ToDictionary(r => (string)r["finding"]!, r => r);
        var sections = new JsonObject();
        foreach (var section in PageChecks.Sections)
            sections[section] = new JsonArray([.. PageChecks.Statements(staged).Where(s => s.Section == section).Select(s =>
            {
                var st = new JsonObject { ["id"] = s.Id, ["text"] = s.Statement["text"]!.DeepClone(), ["cites"] = s.Statement["cites"]!.DeepClone() };
                if (s.Statement["endpoints"] is JsonArray { Count: > 0 } eps) st["endpoints"] = eps.DeepClone();
                return (JsonNode)st;
            })]);
        var slice = set.Slices[page.Id];
        var record = new JsonObject
        {
            ["schemaVersion"] = 1, ["id"] = page.Id, ["kind"] = page.Kind, ["entry"] = page.Entry, ["codeTitle"] = page.Title,
            ["title"] = staged["title"]!.DeepClone(), ["group"] = page.Group, ["sections"] = sections,
            ["unknowns"] = new JsonArray([.. slice.Unknowns.Select(u => (JsonNode)new JsonObject { ["file"] = u.File, ["line"] = u.Line, ["reason"] = u.Reason, ["in"] = u.Owner })]),
            ["verification"] = new JsonObject
            {
                ["dropped"] = (int?)state["dropped"] ?? 0,
                ["findings"] = new JsonArray([.. findings.Select(f =>
                {
                    var r = resolutions[f.Id];
                    var o = FindingJson(f);
                    o["outcome"] = r["outcome"]!.DeepClone();
                    o["reason"] = r["reason"]!.DeepClone();
                    if (r["quote"] is { } q && (string)r["outcome"]! == "rejected") o["supportQuote"] = q.DeepClone();
                    return (JsonNode)o;
                })]),
            },
            ["generated"] = new JsonObject
            {
                ["commit"] = set.Commit, ["bundleHash"] = bundle.InputHash,
                ["slice"] = new JsonObject { ["depth"] = slice.Depth, ["points"] = slice.Nodes.Count, ["chars"] = bundle.Chars, ["cutOff"] = slice.CutOff },
                ["writer"] = state["writer"]!.DeepClone(), ["verifier"] = state["verifier"]!.DeepClone(),
                ["checkRetries"] = (int)state["checkRetries"]!,
            },
        };
        var errors = SchemaRegistry.Embedded.Validate("reference-page.v1.json", record.ToJsonString());
        if (errors.Count > 0)
            throw new StangentException("invalid_page", "The recorded page broke reference-page v1 (a stangent bug).", ExitCodes.Failed, errors);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PageJson(page))!);
        Quotes.WriteAtomic(paths.PageJson(page), CanonicalJson.Serialize(record));
        Quotes.WriteAtomic(paths.PageMarkdown(page), PageRender.Markdown(record, id => set.Find(id) is { } p && p.Id == id ? p.Path : null));
        ClearFailed(paths, page);
        state["stage"] = "done";
        Save(work, state);
        return new RunStep("recorded", "none");
    }

    static RunStep Fail(SpecPaths paths, PageDef page, JsonObject state, string work, string reason, IReadOnlyList<string> errors)
    {
        MarkFailed(paths, page, (string)state["inputHash"]!, reason, errors);
        state["stage"] = "failed";
        Save(work, state);
        return new RunStep("failed", "none", Errors: errors);
    }

    static JsonObject FindingJson(Finding f)
    {
        var o = new JsonObject { ["id"] = f.Id, ["statement"] = f.Statement, ["statementText"] = f.StatementText, ["kind"] = f.Kind, ["explanation"] = f.Explanation };
        if (f.Quote is not null) o["quote"] = f.Quote;
        return o;
    }

    static List<Finding> FindingsFrom(JsonObject state) => [.. (state["findings"] as JsonArray ?? []).OfType<JsonObject>().Select(f =>
        new Finding((string)f["id"]!, (string)f["statement"]!, (string)f["statementText"]!, (string)f["kind"]!, (string?)f["quote"], (string)f["explanation"]!))];

    public static JsonArray Failed(SpecPaths paths) => LoadJson(paths.FailedFile) as JsonArray ?? [];

    static void MarkFailed(SpecPaths paths, PageDef page, string hash, string reason, IReadOnlyList<string> errors)
    {
        using var _ = Lock(paths);
        var list = Failed(paths).OfType<JsonObject>().Where(f => (string?)f["id"] != page.Id).Select(f => f.DeepClone()).ToList();
        list.Add(new JsonObject
        {
            ["id"] = page.Id, ["path"] = page.Path, ["bundleHash"] = hash, ["reason"] = reason,
            ["errors"] = new JsonArray([.. errors.Take(10).Select(e => (JsonNode)e)]),
        });
        WriteFailed(paths, list);
    }

    static void ClearFailed(SpecPaths paths, PageDef page)
    {
        using var _ = Lock(paths);
        var list = Failed(paths).OfType<JsonObject>().ToList();
        if (list.All(f => (string?)f["id"] != page.Id)) return;
        WriteFailed(paths, [.. list.Where(f => (string?)f["id"] != page.Id).Select(f => f.DeepClone())]);
    }

    static void WriteFailed(SpecPaths paths, List<JsonNode> list)
    {
        Directory.CreateDirectory(paths.ReferenceDir);
        Quotes.WriteAtomic(paths.FailedFile, CanonicalJson.Serialize(new JsonArray([.. list.OrderBy(f => (string)f["id"]!, StringComparer.Ordinal)])));
    }

    /// <summary>Drivers run pages side by side; failed.json is the one file they share.</summary>
    static FileStream Lock(SpecPaths paths)
    {
        Directory.CreateDirectory(Path.Combine(paths.Root, ".stangent", "spec"));
        var path = Path.Combine(paths.Root, ".stangent", "spec", "failed.lock");
        for (var i = 0; ; i++)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 600) { Thread.Sleep(50); }
        }
    }

    static void Save(string work, JsonObject state) => File.WriteAllText(Path.Combine(work, "state.json"), CanonicalJson.Serialize(state));

    static string ReadOrEmpty(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    public static JsonNode? LoadJson(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonNode.Parse(File.ReadAllText(path)); }
        catch (System.Text.Json.JsonException e) { throw new StangentException("invalid_json", $"{path}: {e.Message}"); }
    }
}

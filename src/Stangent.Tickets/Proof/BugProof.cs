using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Pages;

namespace Stangent.Tickets.Proof;

/// <summary>
/// The bug proof (plan step 5, D75, D76). <c>stangent ticket prove</c> writes the reproducer's input: the bug, the
/// bundle, where tests may go and the only commands it may run. <c>stangent ticket check --step prove</c> then checks by
/// code: the test is in an allowed place, the named assertion is on its line, the quoted expected result is in the
/// ticket word for word, it compiles, and it fails on that assertion (a Playwright test the same way on 3 runs).
/// Every attempt is recorded in <c>bug-proof.json</c> (contract bug-proof v1) with its hint and outcome.
/// </summary>
public static partial class BugProof
{
    public const string Schema = "bug-proof.v1.json";
    public const int PlaywrightRuns = 3;
    /// <summary>A run where the test runner itself failed (D90): our setup, not the reproducer's test.</summary>
    public const string RunnerError = "runner-error";
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(15);

    public static string File(TicketPaths paths) => Path.Combine(paths.Dir, "bug-proof.json");
    public static JsonObject? Load(TicketPaths paths) => PageRun.LoadJson(File(paths)) as JsonObject;

    // ---- prepare ----

    public static RunStep Prepare(StangentRepo repo, TicketContext ctx, string key, string? hint)
    {
        var paths = new TicketPaths(repo.Root, key);
        var pulled = TicketPull.LoadOrFail(repo.Root, key);
        if (!(bool)pulled["isBug"]!)
            throw new StangentException("not_a_bug", $"{key} is a {(string)pulled["ticket"]!["type"]!}, not one of tickets.bugTypes; only bugs are proven (D75).", ExitCodes.Usage);
        var bundle = TicketBundle.LoadOrFail(repo.Root, key);
        var proof = Load(paths);

        // D83: every part is outside the code and nothing points into it: no test in the repo can reach it, so no attempt.
        if (OutsidePart.OnlyOutside(bundle))
        {
            var reason = "every part of the ticket is outside the code (" + string.Join(", ", OutsidePart.Of(bundle).Select(o => o.Area).Distinct())
                + ") and nothing points into it (D83); remove a wrong part with `stangent ticket points " + key + " --remove-outside <area>` to try the proof";
            var outsideInputs = CanonicalJson.Hash(reason + CanonicalJson.Serialize(bundle["outside"]!) + (hint ?? ""));
            if (Latest(proof) is { } last && (string?)last["inputs"] == outsideInputs) return new RunStep("current", "none");
            Record(repo, paths, new JsonObject
            {
                ["outcome"] = "not-provable", ["outside"] = true, ["reason"] = reason, ["hint"] = hint, ["commit"] = Commit(repo.Root, null), ["inputs"] = outsideInputs,
            });
            return new RunStep("recorded", "none", Errors: [reason]);
        }

        var targets = Targets(repo, ctx, bundle);

        if (targets.Count == 0)
        {
            const string reason = "no unit-test project covers the code at the starting points, and tickets.app isn't set for Playwright (D76)";
            var codeInputs = CanonicalJson.Hash(reason + (hint ?? ""));
            if (Latest(proof) is { } last && (string?)last["inputs"] == codeInputs) return new RunStep("current", "none");
            Record(repo, paths, new JsonObject
            {
                ["outcome"] = "not-provable", ["reason"] = reason, ["hint"] = hint, ["commit"] = Commit(repo.Root, null), ["inputs"] = codeInputs,
            });
            return new RunStep("recorded", "none", Errors: [reason]);
        }

        // Playwright: the repo's specs that open the ticket's screens, and the script the reproducer runs its own with (D86).
        var related = targets.Any(t => t.Kind == TestTarget.Playwright) ? AppSession.RelatedSpecs(repo, bundle) : [];
        if (targets.Any(t => t.Kind == TestTarget.Playwright)) { AppSession.WriteScript(repo, paths, related); AppSession.WriteConfig(repo); }
        var input = Input(repo, pulled, bundle, targets, related, hint, proof);
        // Earlier attempts are shown to the reproducer but aren't an input change: an unchanged ticket, bundle, targets
        // and hint make no new AI call (D18). A new hint does.
        var hash = CanonicalJson.Hash(Input(repo, pulled, bundle, targets, related, hint, null));
        if (Latest(proof) is { } latest && (string?)latest["inputs"] == hash && (string?)latest["outcome"] != "failed") return new RunStep("current", "none");
        AiPolicy.EnsureAllowed(repo.Config, "ticket prove");
        var n = (proof?["attempts"]?.AsArray().Count ?? 0) + 1;
        return AiStep.Start(paths, StepSpec.Prove, input, hash, new JsonObject
        {
            ["attempt"] = n, ["hint"] = hint, ["targets"] = new JsonArray([.. targets.Select(t => (JsonNode)t.ToJson())]),
            ["related"] = new JsonArray([.. related.Select(r => (JsonNode)r)]),
            ["outside"] = new JsonArray([.. OutsidePart.Of(bundle).Select(o => o.Area).Distinct().Select(a => (JsonNode)a)]),
        });
    }

    /// <summary>The limits the driver runs the reproducer under (D60, D81): what it may edit and run, its turns and cost.</summary>
    public static JsonObject DriverLimits(StangentRepo repo, string key)
    {
        var paths = new TicketPaths(repo.Root, key);
        var state = AiStep.State(paths, StepSpec.Prove);
        var targets = state["targets"]!.AsArray().Select(t => TestTarget.FromJson(t!)).ToList();
        // Only its result file: state.json (targets, related specs, usage) is stangent's and drives the next run (D86).
        // Paths absolute (D89): a `cd` in the agent's shell mustn't change what a rule allows.
        var tools = new List<string> { "Read", "Glob", "Grep", $"Edit({ClaudeRules.Absolute(OutputPath(paths))})" };
        foreach (var t in targets)
        {
            tools.AddRange(t.EditRules(repo.Root));
            tools.AddRange(Commands(paths, t).Select(c => $"Bash({c}:*)"));
            if (t.Runner is "vitest" or "jest") tools.Add($"Bash(cd {t.Dir})");
        }
        // Playwright (D86): the app is served for the attempt; with no related spec, a browser to look at the screens.
        JsonObject? app = null;
        if (targets.Any(t => t.Kind == TestTarget.Playwright) && repo.Config.Tickets.App is { Url: { } url })
        {
            var browser = (state["related"]?.AsArray().Count ?? 0) == 0;
            if (browser) tools.AddRange(BrowserTools.Select(b => "mcp__playwright__" + b));
            var pw = repo.Config.Tickets.App.PlaywrightDir is { Length: > 0 } d ? Path.Combine(repo.Root, d) : repo.Root;
            app = new JsonObject { ["serve"] = true, ["url"] = url, ["browser"] = browser, ["playwrightDir"] = pw };
        }
        // What's left of the attempt's cap: a retry doesn't get a fresh one (D81).
        var budget = repo.Config.Tickets.ProveBudget;
        var spent = Usage.FromJson(state["usage"]).CostUsd;
        var turns = (int?)state["turns"] ?? 0;
        return new JsonObject
        {
            ["allowedTools"] = new JsonArray([.. tools.Distinct().Select(x => (JsonNode)x)]),
            ["permissionMode"] = "dontAsk", ["maxTurns"] = Math.Max(1, budget.Turns - turns),
            ["maxBudgetUsd"] = Math.Max(0.01, Math.Round(budget.Usd - spent, 2)), ["multiTurn"] = true, ["app"] = app,
        };
    }

    static string OutputPath(TicketPaths paths) => Path.GetFullPath(Path.Combine(AiStep.Work(paths, StepSpec.Prove), "output.json"));

    /// <summary>The commands a target lets the reproducer run, as the start of a command line: its build, its test run,
    /// and for Playwright the script stangent wrote (by its repo-relative and its absolute path).</summary>
    static IEnumerable<string> Commands(TicketPaths paths, TestTarget t)
    {
        if (t.BuildCommand is { } build) yield return build;
        switch (t.Runner)
        {
            case "dotnet":
                // Both platforms' forms: VSTest takes the project first, Microsoft.Testing.Platform takes --project.
                yield return $"dotnet test '{t.Project}'";
                yield return $"dotnet test --project '{t.Project}'";
                break;
            case "vitest": yield return "npx vitest run"; break;
            case "jest": yield return "npx jest"; break;
            default:
                yield return paths.Relative(AppSession.ScriptPath(paths));
                yield return Path.GetFullPath(AppSession.ScriptPath(paths));
                break;
        }
    }

    /// <summary>The Playwright MCP tools the reproducer may use to look at the app (D86): reading and clicking, no files,
    /// no code evaluation, no other tabs.</summary>
    public static readonly string[] BrowserTools =
        ["browser_navigate", "browser_snapshot", "browser_click", "browser_type", "browser_select_option", "browser_press_key", "browser_hover", "browser_wait_for"];

    /// <summary>
    /// The test targets for the code at the starting points (D89), whatever kind of point: the bundle's pointers (but
    /// impact's), a point that is code, and a page point's entry (a handler page is its handler, an endpoint page its
    /// endpoint). When no unit test covers that code, the code the point pages run is tried next: what their slices walk
    /// (an endpoint's action and the services behind it). That code only widens the search, never outranks the points'
    /// own projects. Without it, a backend point picked as a page offered only Playwright, which can't reach server code.
    /// </summary>
    static List<TestTarget> Targets(StangentRepo repo, TicketContext ctx, JsonObject bundle)
    {
        var all = (bundle["points"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var points = all.Where(p => (string)p["kind"]! != StartingPoint.File).Select(p => (string)p["id"]!).ToList();
        var at = bundle["pointers"]!.AsArray().Where(p => (string?)p!["from"] != "impact").Select(p => (string)p!["id"]!).ToList();
        foreach (var p in points) at.Add(ctx.Analyzer.Find(p) is not null ? p : ctx.Set.Find(p)?.Entry ?? p);
        // A file point (D101): the code declared in that file.
        foreach (var f in all.Where(p => (string)p["kind"]! == StartingPoint.File)) at.AddRange(ctx.Analyzer.InFile((string)f["id"]!).Select(n => n.Id));
        var targets = Find(at.Select(ctx.Analyzer.Find).OfType<Node>());
        if (targets.Any(t => t.Kind == TestTarget.Unit)) return targets;
        var behind = points.SelectMany(p => ctx.Set.Slices.TryGetValue(p, out var s) ? s.Nodes.Where(n => n.Role == "walk").Select(n => n.Node) : []);
        return Find(at.Select(ctx.Analyzer.Find).OfType<Node>().Concat(behind));

        List<TestTarget> Find(IEnumerable<Node> code)
        {
            var nodes = code.DistinctBy(n => n.Id).ToList();
            var projects = nodes.Select(s => s.Project).OfType<string>().Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(p => p["project:".Length..]).Distinct(StringComparer.Ordinal).ToList();
            var files = nodes.Select(s => s.File).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            return TestTargets.Find(repo, projects, files);
        }
    }

    static string Input(StangentRepo repo, JsonObject pulled, JsonObject bundle, List<TestTarget> targets, List<string> related, string? hint, JsonObject? proof)
    {
        var paths = new TicketPaths(repo.Root, (string)pulled["key"]!);
        var sb = AiStep.Header(StepSpec.Prove);
        var bug = pulled["bug"]!;
        sb.Append("# The bug\n\n");
        foreach (var part in TicketPull.BugParts)
            sb.Append($"- **{part}:** {((string?)bug[part] is { } v ? v.Replace("\n", "\n  ") : "_not given_")}\n");
        sb.Append("\n# The ticket\n\n");
        foreach (var (where, text) in TicketText.From(pulled).Prose) sb.Append($"## {where}\n\n````text\n{text}\n````\n\n");
        sb.Append("# Where the work starts (the bundle)\n\n").Append(TicketBundle.Markdown(bundle)).Append('\n');

        var mtp = UsesMtp(repo.Root);
        var report = paths.Relative(Path.Combine(AiStep.Work(paths, StepSpec.Prove), "your-run"));
        sb.Append("# Test targets\n\nWrite the test in one of these, in the folder given, as a file matching its patterns.\n\n");
        sb.Append("| target | kind | write in | files | build | run your test |\n|---|---|---|---|---|---|\n");
        foreach (var t in targets)
        {
            var run = t.Kind == TestTarget.Playwright ? $"`{paths.Relative(AppSession.ScriptPath(paths))} <spec, relative to the repo>` (the app is running)"
                : $"`{(t.Runner is "vitest" or "jest" ? $"cd {t.Dir} && " : "")}{t.TestCommand("<file>", "<test name>", report + (t.Runner == "dotnet" ? ".trx" : ".json"), !mtp)}`";
            sb.Append($"| `{t.Id}` | {t.Kind} | `{t.Dir}/` | {string.Join(", ", t.Patterns)} | {(t.BuildCommand is { } b ? $"`{b}`" : "none")} | {run} |\n");
        }
        if (targets.Any(t => t.Kind == TestTarget.Playwright) && repo.Config.Tickets.App is { } app)
        {
            sb.Append($"\n# The app while you work\n\nstangent started the app at `{app.Url}` before you began (seeded by its start step); it stays up until you finish.\n\n");
            if (related.Count > 0)
            {
                sb.Append("**Related Playwright tests** open this ticket's screens. Read them and copy the closest: the way to the screen is already right.\n\n");
                foreach (var r in related) sb.Append($"- `{r}`\n");
                sb.Append($"\nThey were run against the app before you started; the results are in `{paths.Relative(AppSession.RelatedRunPath(paths))}`.\n");
            }
            else
                sb.Append("No Playwright test opens these screens yet. You have a headless browser on the app (the `mcp__playwright__browser_*` tools): **look at the screen first** (navigate, snapshot, click your way to it), so your test clicks and reads what is really there.\n");
            sb.Append("\n**Test first:** run your spec with the command in the table and hand it in only once it **fails on your assertion**. stangent then runs it itself, three times.\n");
        }
        var budget = repo.Config.Tickets.ProveBudget;
        sb.Append($"\n# Your limits\n\n- Write and edit files only in the test targets' folders.\n- Run only the build and test commands above.\n");
        sb.Append($"- At most {budget.Turns} turns and ${budget.Usd:0.##}; stopping at the cap counts as not reproduced.\n");
        if (hint is not null) sb.Append($"\n# A hint from the developer\n\n{hint}\n");
        var earlier = proof?["attempts"]?.AsArray() ?? [];
        if (earlier.Count > 0)
        {
            sb.Append("\n# Earlier attempts\n\n");
            foreach (var a in earlier)
                sb.Append($"- attempt {a!["n"]}: {a["outcome"]}{((string?)a["testName"] is { } tn ? $", test `{tn}` in `{a["testFile"]}`" : "")}{((string?)a["reason"] is { } r ? $": {r}" : "")}{((string?)a["hint"] is { } h ? $" (hint: {h})" : "")}\n");
        }
        return sb.ToString();
    }

    // ---- check ----

    public static async Task<RunStep> CheckAsync(StangentRepo repo, string key, JsonNode? claudeResult, ICommandRunner runner, CancellationToken ct = default)
    {
        var paths = new TicketPaths(repo.Root, key);
        var step = StepSpec.Prove;
        var state = AiStep.State(paths, step);
        // The cap is per attempt (D81), first call and retry together, so the reproducer's own result is required.
        if (claudeResult is not JsonObject result)
            throw new StangentException("usage", "check --step prove needs --usage <Claude Code's JSON result, with the driver's rounds>: the attempt's turn and cost cap is enforced from it (D81, D104).", ExitCodes.Usage);
        var usage = Usage.FromClaudeResult(result);
        // D104: turns are what `--max-turns` limits, the model's rounds, which the driver counts. `num_turns` also counts
        // parallel tool calls, so a run well inside its limit could read as over it.
        state["turns"] = ((int?)state["turns"] ?? 0) + (result["rounds"] is JsonValue rounds && rounds.TryGetValue<int>(out var n) ? n : 0);
        var targets = state["targets"]!.AsArray().Select(t => TestTarget.FromJson(t!)).ToList();
        var attempt = new JsonObject { ["n"] = (int)state["attempt"]!, ["hint"] = state["hint"]?.DeepClone(), ["inputs"] = state["inputs"]!.DeepClone() };

        // D89: Claude Code refused something stangent's own rules allow. Unless the test still reproduces the bug (code
        // checked that), the attempt says nothing about the reproducer: it's recorded as a setup failure, with no retry,
        // and the batch stops so the next attempts don't hit the same wall.
        var blocked = Blocked(repo, paths, targets, result);
        RunStep SetupBlocked(string? why = null)
        {
            var reason = why ?? $"stangent's own permission rules blocked what they allow ({string.Join("; ", blocked.Take(3))}): a setup bug, not the reproducer's (D89)";
            attempt["setupBlocked"] = true;
            attempt["advice"] = SetupAdvice.For(reason, repo.Config.Tickets.App?.PlaywrightDir);
            return Finish(repo, paths, state, attempt, "failed", reason, null) with { Status = "blocked" };
        }

        // D81: an attempt that stops at its cap is "not reproduced".
        if (CapHit(repo, (string?)result["subtype"], Usage.FromJson(state["usage"]).Add(usage).CostUsd, (int)state["turns"]!) is { } cap)
        {
            state["usage"] = Usage.FromJson(state["usage"]).Add(usage).ToJson();
            return blocked.Count > 0 ? SetupBlocked() : Finish(repo, paths, state, attempt, "not-reproduced", cap, null);
        }

        var (output, errors) = AiStep.Read(paths, step, state, usage);
        if (blocked.Count > 0 && output is null) return SetupBlocked();
        // Code found test targets, so "not provable" is the reproducer's judgment, not a fact: it gets the one retry, and
        // if it says so again, it's recorded labelled as its judgment.
        if (output is not null && (string)output["outcome"]! == "not-provable")
        {
            if (blocked.Count > 0) return SetupBlocked();
            var reason = (string?)output["reason"] ?? "no reason given";
            // D83: part of the ticket is outside the code (in the bundle this attempt was prepared with), so "not
            // provable" may be right: accepted without the push-back.
            var outside = state["outside"]?.AsArray().Select(a => (string)a!).ToList() ?? [];
            if (outside.Count > 0)
            {
                attempt["outside"] = true;
                return Finish(repo, paths, state, attempt, "not-provable",
                    $"the reproducer's judgment (part of the ticket is outside the code: {string.Join(", ", outside)}): {reason}", null);
            }
            if (AiStep.Retry(paths, step, state, [$"Code found test targets ({string.Join(", ", targets.Select(t => t.Id))}). Write a test in one of them; answer not-provable again only if none can reach the behaviour, saying why. You said: {reason}"]) is { } again)
                return again;
            return Finish(repo, paths, state, attempt, "not-provable", $"the reproducer's judgment (code found test targets): {reason}", null);
        }

        TestTarget? target = null;
        string? testFile = null;
        (int From, int To) span = (0, 0);
        if (output is not null)
        {
            foreach (var field in new[] { "target", "testFile", "testName", "assertion", "expectedQuote", "actualQuote" })
                if (output[field] is null) errors.Add($"A written test needs `{field}`.");
            if (errors.Count == 0)
            {
                target = targets.FirstOrDefault(t => t.Id == (string)output["target"]!);
                testFile = TestTarget.Normalize((string)output["testFile"]!);
                if (target is null) errors.Add($"target `{output["target"]}` isn't one of the test targets in the input.");
                else if (!target.Allows(testFile)) errors.Add($"`{testFile}` isn't a test file stangent lets you write: it must be under `{(target.AtRoot ? "the repo" : target.Dir + "/")}`, without `..`, and match {string.Join(", ", target.Patterns)}.");
                else if (!System.IO.File.Exists(Path.Combine(repo.Root, testFile))) errors.Add($"`{testFile}` doesn't exist.");
                else
                {
                    var lines = System.IO.File.ReadAllLines(Path.Combine(repo.Root, testFile));
                    var line = (int)output["assertion"]!["line"]!;
                    var text = (string)output["assertion"]!["text"]!;
                    if (line > lines.Length || !Quotes.Normalize(lines[line - 1]).Contains(Quotes.Normalize(text), StringComparison.Ordinal))
                        errors.Add($"line {line} of `{testFile}` isn't the assertion \"{Short(text)}\": copy the line number and its code exactly.");
                    else span = Span(lines, line);
                }
                var quotable = Quotable(TicketPull.LoadOrFail(repo.Root, key));
                if (!Quotes.InText((string)output["expectedQuote"]!, quotable))
                    errors.Add($"expectedQuote \"{Short((string)output["expectedQuote"]!)}\" isn't in the ticket word for word (at least 4 words, or a whole line).");
                if (!Quotes.InText((string)output["actualQuote"]!, quotable))
                    errors.Add($"actualQuote \"{Short((string)output["actualQuote"]!)}\" isn't in the ticket word for word (at least 4 words, or a whole line).");
            }
        }

        var runs = new JsonArray();
        string? outcome = null;
        if (errors.Count == 0)
        {
            attempt["kind"] = target!.Kind;
            attempt["target"] = target.Id;
            attempt["targetSpec"] = target.ToJson();
            attempt["testFile"] = testFile;
            attempt["testName"] = (string)output!["testName"]!;
            attempt["assertion"] = output["assertion"]!.DeepClone();
            attempt["expectedQuote"] = (string)output["expectedQuote"]!;
            attempt["actualQuote"] = (string)output["actualQuote"]!;
            List<JsonObject> results;
            try { results = await RunTest(repo, paths, target, testFile!, (string)output["testName"]!, span, runner, target.Kind == TestTarget.Playwright ? PlaywrightRuns : 1, ct); }
            catch (StangentException e) when (e.Code.StartsWith("app_", StringComparison.Ordinal))
            {
                // The app didn't build, start or seed: not the test's fault, and not something a retry fixes (D91: a
                // setup failure, with advice, that stops a batch).
                return SetupBlocked(e.Message);
            }
            foreach (var r in results) runs.Add(Stored(r));
            // D90: the test runner itself failed (no .NET runtime for the target, an adapter that finds nothing): the
            // attempt measured the machine, not the reproducer. No retry; the batch stops.
            if (results.FirstOrDefault(r => (string)r["result"]! == RunnerError) is { } broken)
            {
                attempt["runs"] = runs;
                return SetupBlocked($"{broken["message"]} (D90): a setup problem, not the reproducer's");
            }
            var kinds = results.Select(r => (string)r["result"]!).ToList();
            if (kinds.All(k => k == "failed-on-assertion") && results.Select(r => (int?)r["line"]).Distinct().Count() == 1) outcome = "reproduced";
            else if (kinds.All(k => k == "passed")) outcome = "not-reproduced";
            else if (target.Kind == TestTarget.Playwright && kinds.All(k => k is "passed" or "failed-on-assertion")) outcome = "not-reproduced-flaky";
            else errors.AddRange(results.Where(r => (string)r["result"]! is not ("passed" or "failed-on-assertion"))
                .Select(r => $"The test run gave {r["result"]}: {Short((string?)r["message"] ?? "", 600)}").Distinct());
            // D95: a reproduced failure names the words of its message that show the ticket's actual result, so the
            // developer can check the match side by side. Code checks only that those words are in what the test printed.
            if (outcome == "reproduced")
            {
                var source = System.IO.File.ReadAllText(Path.Combine(repo.Root, testFile!));
                if (ShowsProblem((string?)output!["shows"], results, source) is { } problem)
                {
                    errors.Add(problem);
                    outcome = null;
                }
                else attempt["shows"] = (string)output["shows"]!;   // stored only once checked: what the side-by-side shows
            }
        }

        if (outcome != "reproduced" && blocked.Count > 0)
        {
            attempt["runs"] = runs;
            return SetupBlocked();
        }
        if (outcome is null)
        {
            if (AiStep.Retry(paths, step, state, errors) is { } retry) return retry;
            attempt["runs"] = runs;
            return Finish(repo, paths, state, attempt, "failed", string.Join("; ", errors.Take(3)), null);
        }
        attempt["runs"] = runs;
        JsonArray? baseline = null;
        if (outcome == "reproduced" && target!.Kind == TestTarget.Unit)
            baseline = new JsonArray([.. (await FailingTests(repo, paths, target, runner, ct)).Where(n => !SameTest(n, (string)output!["testName"]!)).Select(n => (JsonNode)n)]);
        return Finish(repo, paths, state, attempt, outcome, outcome switch
        {
            "not-reproduced" => "the test passes: the code already does what the ticket expects, or the test doesn't reach the reported behaviour",
            "not-reproduced-flaky" => "the test failed on some runs and passed on others (D76)",
            _ => null,
        }, baseline, testFile);
    }

    /// <summary>
    /// Why <paramref name="shows"/> doesn't stand for the failure (D95): missing, not in every run's whole message, or
    /// words the test itself holds (its expected value, its own "because …" text), which say nothing about what the code
    /// did. Colour codes, spacing, case and curly quotes don't count.
    /// </summary>
    public static string? ShowsProblem(string? shows, IReadOnlyList<JsonObject> results, string testSource = "")
    {
        string Message(JsonObject r) => (string?)r["full"] ?? (string?)r["message"] ?? "";
        var message = results.Count == 0 ? "" : Message(results[0]);
        if (string.IsNullOrWhiteSpace(shows))
            return $"Say which words of the failure message show the ticket's actual result, in `shows`. The message was: {Short(Plain(message), 1500)}";
        var want = Plain(shows);
        if (!results.All(r => Plain(Message(r)).Contains(want, StringComparison.OrdinalIgnoreCase)))
            return $"shows \"{Short(shows)}\" isn't in the failure message: copy the words from it. The message was: {Short(Plain(message), 1500)}";
        if (Plain(testSource).Contains(want, StringComparison.OrdinalIgnoreCase))
            return $"shows \"{Short(shows)}\" is written in your test (its expected value or its own message): give the words that show what the code did instead, e.g. what it found or returned. The message was: {Short(Plain(message), 1500)}";
        return null;
    }

    static string Plain(string s) => Quotes.Normalize(Regex.Replace(s, @"\x1b\[[0-9;]*m|\[[0-9;]+m", ""))
        .Replace('“', '"').Replace('”', '"').Replace('‘', '\'').Replace('’', '\'');

    static RunStep Finish(StangentRepo repo, TicketPaths paths, JsonObject state, JsonObject attempt, string outcome, string? reason, JsonArray? baseline, string? testFile = null)
    {
        attempt["outcome"] = outcome;
        if (reason is not null) attempt["reason"] = reason;
        if (baseline is not null) attempt["baseline"] = baseline;
        attempt["commit"] = Commit(repo.Root, testFile);
        attempt["usage"] = state["usage"]!.DeepClone();
        attempt["retries"] = (int?)state["retries"] ?? 0;
        Record(repo, paths, attempt);
        state["stage"] = "done";
        AiStep.SaveState(paths, StepSpec.Prove, state);
        return outcome == "failed" ? new RunStep("failed", "none", Errors: [reason ?? "failed"]) : new RunStep("recorded", "none");
    }

    /// <summary>Appends an attempt; it becomes the current outcome, and any earlier confirmation or verification is cleared.</summary>
    static void Record(StangentRepo repo, TicketPaths paths, JsonObject attempt)
    {
        var proof = Load(paths) ?? new JsonObject { ["schemaVersion"] = 1, ["key"] = paths.Key, ["attempts"] = new JsonArray() };
        var attempts = proof["attempts"]!.AsArray();
        attempt["n"] = attempts.Count + 1;
        attempts.Add(attempt);
        proof["outcome"] = (string)attempt["outcome"]!;
        proof["confirmedBy"] = null;
        proof["verification"] = null;
        Save(paths, proof);
    }

    static void Save(TicketPaths paths, JsonObject proof)
    {
        var errors = SchemaRegistry.Embedded.Validate(Schema, proof.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_proof", "The proof record broke its contract (a stangent bug).", ExitCodes.Failed, errors);
        Quotes.WriteAtomic(File(paths), CanonicalJson.Serialize(proof));
    }

    static JsonObject? Latest(JsonObject? proof) => proof?["attempts"]?.AsArray().LastOrDefault() as JsonObject;

    // ---- confirm and verify ----

    /// <summary>The developer confirms the failure is the reported problem and a bug, not the code working as meant (D75, D96).</summary>
    public static JsonObject Confirm(StangentRepo repo, string key, string who)
    {
        var paths = new TicketPaths(repo.Root, key);
        var proof = Load(paths) ?? throw new StangentException("no_proof", $"{key} has no proof yet. Run `stangent ticket prove {key}`.", ExitCodes.NotFound);
        if ((string)proof["outcome"]! != "reproduced")
            throw new StangentException("not_reproduced", $"{key}'s proof is {(string)proof["outcome"]!}; only a reproduced failure is confirmed.", ExitCodes.Usage);
        proof["confirmedBy"] = who;
        Save(paths, proof);
        return proof;
    }

    /// <summary>On the fix branch (D18's test row): the reproduction test passes, and no other test in its target fails
    /// that wasn't already failing when the bug was reproduced.</summary>
    public static async Task<JsonObject> VerifyAsync(StangentRepo repo, string key, ICommandRunner runner, CancellationToken ct = default)
    {
        var paths = new TicketPaths(repo.Root, key);
        var proof = Load(paths) ?? throw new StangentException("no_proof", $"{key} has no proof yet.", ExitCodes.NotFound);
        var latest = Latest(proof)!;
        if ((string)proof["outcome"]! != "reproduced")
            throw new StangentException("not_reproduced", $"{key}'s proof is {(string)proof["outcome"]!}: there's no failing test to verify the fix with.", ExitCodes.Usage);
        var target = latest["targetSpec"] is JsonObject spec ? TestTarget.FromJson(spec)
            : throw new StangentException("no_proof", $"{key}'s reproduced attempt has no recorded test target; prove it again.", ExitCodes.NotFound);
        var testFile = (string)latest["testFile"]!;
        var lines = System.IO.File.Exists(Path.Combine(repo.Root, testFile)) ? System.IO.File.ReadAllLines(Path.Combine(repo.Root, testFile)) : [];
        var line = (int)latest["assertion"]!["line"]!;
        var runs = await RunTest(repo, paths, target, testFile, (string)latest["testName"]!, lines.Length >= line ? Span(lines, line) : (line, line), runner,
            target.Kind == TestTarget.Playwright ? PlaywrightRuns : 1, ct);
        var passed = runs.All(r => (string)r["result"]! == "passed");
        var baseline = (latest["baseline"]?.AsArray() ?? []).Select(b => (string)b!).ToHashSet(StringComparer.Ordinal);
        var failing = target.Kind == TestTarget.Unit ? await FailingTests(repo, paths, target, runner, ct) : [];
        var fresh = failing.Where(f => !baseline.Contains(f)).Order(StringComparer.Ordinal).ToList();
        proof["verification"] = new JsonObject
        {
            ["commit"] = Commit(repo.Root, null), ["passed"] = passed && fresh.Count == 0,
            ["newFailures"] = new JsonArray([.. fresh.Select(f => (JsonNode)f)]), ["runs"] = new JsonArray([.. runs.Select(r => (JsonNode)Stored(r))]),
        };
        Save(paths, proof);
        return proof;
    }

    // ---- running ----

    static async Task<List<JsonObject>> RunTest(StangentRepo repo, TicketPaths paths, TestTarget target, string testFile, string testName, (int From, int To) span,
        ICommandRunner runner, int times, CancellationToken ct)
    {
        var dir = Path.Combine(AiStep.Work(paths, StepSpec.Prove), "runs");
        Directory.CreateDirectory(dir);
        if (target.BuildCommand is { } build)
        {
            var b = await runner.RunAsync(build, repo.Root, CommandTimeout, null, ct);
            if (b.TimedOut) return [Run("timeout", "the build timed out", null, null, b.Ms)];
            if (b.ExitCode != 0) return [Run("compile-error", Errors(b.Output), null, null, b.Ms)];
        }
        if (target.Kind == TestTarget.Playwright) AppSession.WriteConfig(repo);   // on a fix branch too
        await using var app = target.Kind == TestTarget.Playwright ? await StartApp(repo, runner, ct) : null;
        var results = new List<JsonObject>();
        for (var i = 1; i <= times; i++)
        {
            var report = Path.Combine(dir, $"run-{i}" + (target.Runner == "dotnet" ? ".trx" : ".json"));
            if (System.IO.File.Exists(report)) System.IO.File.Delete(report);
            var fileArg = target.Runner is "vitest" or "jest" or "playwright" ? Path.GetRelativePath(target.WorkingDir(repo.Root), Path.Combine(repo.Root, testFile)).Replace('\\', '/') : testFile;
            var env = target.Kind == TestTarget.Playwright
                ? new Dictionary<string, string> { ["PLAYWRIGHT_JSON_OUTPUT_NAME"] = report, ["BASE_URL"] = repo.Config.Tickets.App?.Url ?? "" } : null;
            var r = await runner.RunAsync(target.TestCommand(target.Runner == "dotnet" ? null : fileArg, testName, report, !UsesMtp(repo.Root)), target.WorkingDir(repo.Root), CommandTimeout, env, ct);
            results.Add(Classify(target, report, r, testFile, testName, span));
        }
        // D90: the runner is broken only if it can't list the project's tests at all (an adapter that won't load, a
        // missing runtime). Then "no test named X ran" isn't the reproducer's mistake; and a run with no result but an
        // error is a runner failure only then, else the test crashed the host (its own doing, it gets the retry).
        // A run with no report and a non-zero exit (MTP with no runtime writes none) is checked the same way.
        if (results.Any(x => (string)x["result"]! is "not-found" or RunnerError or "error") && target.ListCommand(!UsesMtp(repo.Root)) is { } list)
        {
            var l = await runner.RunAsync(list, target.WorkingDir(repo.Root), CommandTimeout, null, ct);
            // VSTest prints its "available" header even when it finds nothing: a test listed means a name under it.
            var lists = ListedTest().IsMatch(l.Output);
            var broken = !lists && (l.Output.Contains("No test is available in", StringComparison.Ordinal) || l.Output.Contains("You must install or update .NET", StringComparison.Ordinal)
                || l.Output.Contains("Discovered 0 tests", StringComparison.Ordinal));
            for (var i = 0; i < results.Count; i++)
            {
                var kind = (string)results[i]["result"]!;
                if (broken && kind is "not-found" or "error")
                    results[i] = Run(RunnerError, $"the test runner finds no tests at all in {target.Project}: {Short(RunnerLines(l.Output), 800)}", null, null, l.Ms);
                else if (!broken && kind == RunnerError)
                    results[i] = Run("error", $"the test run stopped before reporting a result (the test may have crashed the test host): {(string?)results[i]["message"]}", null, null, (long?)results[i]["ms"] ?? 0);
            }
        }
        return results;
    }

    static JsonObject Classify(TestTarget target, string report, CommandResult r, string testFile, string testName, (int From, int To) span)
    {
        if (r.TimedOut) return Run("timeout", "the test run timed out", null, null, r.Ms);
        // The tools a frontend run needs aren't there (D94 review): the machine, not the test.
        if (target.Runner != "dotnet" && MissingTool(r) is { } missing) return Run(RunnerError, missing, null, null, r.Ms);
        if (!System.IO.File.Exists(report)) return Run(r.ExitCode == 0 ? "not-found" : "error", r.ExitCode == 0 ? "no report: the test name matched nothing" : Errors(r.Output), null, null, r.Ms);
        List<TestCaseResult> cases;
        try
        {
            if (target.Runner == "dotnet" && TestReports.TrxRunnerError(System.IO.File.ReadAllText(report)) is { } broken)
                // A test can crash the host itself (a stack overflow, Environment.Exit): that's the test's doing, and
                // RunTest confirms a runner failure by listing the project's tests before it's called one.
                return Run(RunnerError, $"the test runner failed before any test ran: {broken}", null, null, r.Ms);
            cases = Parse(target, System.IO.File.ReadAllText(report));
        }
        catch (Exception e) when (e is JsonException or System.Xml.XmlException) { return Run("error", $"unreadable report: {e.Message}", null, null, r.Ms); }
        cases = [.. cases.Where(c => c.Status != "skipped" && SameTest(c.Name, testName))];
        if (cases.Count == 0) return Run("not-found", $"no test named \"{testName}\" ran", null, null, r.Ms);
        var failed = cases.FirstOrDefault(c => c.Status is "failed" or "timeout");
        if (failed is null) return Run("passed", "", null, null, r.Ms);
        if (failed.Status == "timeout") return Run("timeout", failed.Message ?? "the test timed out", testFile, null, r.Ms);
        var line = TestReports.LineIn(failed, testFile);
        if (!TestReports.IsAssertion(failed)) return Run("failed-other", $"not an assertion failure: {failed.Message}", testFile, line, r.Ms);
        if (line is null || line < span.From || line > span.To)
            return Run("failed-other", $"failed on an assertion at line {line?.ToString() ?? "?"}, not the named one at line {span.From}: {failed.Message}", testFile, line, r.Ms);
        return Run("failed-on-assertion", failed.Message ?? "", testFile, line, r.Ms);
    }

    static List<TestCaseResult> Parse(TestTarget target, string report) => target.Runner switch
    {
        "dotnet" => TestReports.ParseTrx(report),
        "playwright" => TestReports.ParsePlaywright(report),
        _ => TestReports.ParseJest(report),
    };

    /// <summary>A report's name is the test's: the full name, or ends with its last part (NUnit and TRX vary).</summary>
    /// <summary>A report's name is the test's: the same full name, the same with a parameter list (<c>Name(1)</c>), the
    /// same method name alone (some TRX writers), or a Jest/Playwright full title ending in it. Never a prefix:
    /// <c>Rejects_over_1000</c> isn't <c>Rejects_over_100</c>.</summary>
    public static bool SameTest(string reported, string testName)
    {
        static string Bare(string n) => n.Contains('(') ? n[..n.IndexOf('(')] : n;
        var r = Bare(reported);
        var last = testName.Split('.')[^1];
        return r == testName || r == last || reported.EndsWith(" " + testName, StringComparison.Ordinal)
            || (r.EndsWith("." + last, StringComparison.Ordinal) && testName.EndsWith(r, StringComparison.Ordinal));
    }

    static async Task<List<string>> FailingTests(StangentRepo repo, TicketPaths paths, TestTarget target, ICommandRunner runner, CancellationToken ct)
    {
        var report = Path.Combine(AiStep.Work(paths, StepSpec.Prove), "runs", "all" + (target.Runner == "dotnet" ? ".trx" : ".json"));
        if (System.IO.File.Exists(report)) System.IO.File.Delete(report);
        await runner.RunAsync(target.TestCommand(null, null, report, !UsesMtp(repo.Root)), target.WorkingDir(repo.Root), CommandTimeout, null, ct);
        if (!System.IO.File.Exists(report)) return [];
        return [.. Parse(target, System.IO.File.ReadAllText(report)).Where(c => c.Status is "failed" or "timeout").Select(c => c.Name).Distinct().Order(StringComparer.Ordinal)];
    }

    internal static async Task<IAsyncDisposable?> StartApp(StangentRepo repo, ICommandRunner runner, CancellationToken ct)
    {
        var app = repo.Config.Tickets.App!;
        if (app.Build is { } build && (await runner.RunAsync(build, repo.Root, CommandTimeout, null, ct)) is { ExitCode: not 0 } b)
            throw new StangentException("app_build_failed", $"tickets.app.build failed: {Errors(b.Output)}");
        var started = runner.Start(app.Start!, repo.Root);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var deadline = DateTime.UtcNow.AddSeconds(app.ReadySeconds);
            while (true)
            {
                try { using var r = await http.GetAsync(app.Url, ct); if ((int)r.StatusCode < 500) break; }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
                // D89: the start command failed without the app answering (its seed failed, the port was taken): say so
                // now, with its own error, rather than wait out the ready time. One that exits 0 may have started the
                // app in the background (`docker compose up -d`): it's waited for as before, though stangent then can't
                // stop it (the start command should stay in the foreground).
                if (started.HasExited && started.ExitCode != 0)
                    throw new StangentException("app_not_ready", $"tickets.app.start exited (code {started.ExitCode?.ToString() ?? "?"}) before the app answered at {app.Url}: {StartError(started.Output)}");
                if (DateTime.UtcNow > deadline)
                    throw new StangentException("app_not_ready", $"The app didn't answer at {app.Url} within {app.ReadySeconds} s.{(started.Output.Length > 0 ? " The start command's last output: " + StartError(started.Output) : "")}");
                await Task.Delay(1000, ct);
            }
            if (app.Seed is { } seed && (await runner.RunAsync(seed, repo.Root, CommandTimeout, null, ct)) is { ExitCode: not 0 } s)
                throw new StangentException("app_seed_failed", $"tickets.app.seed failed: {Errors(s.Output)}");
            return started;
        }
        catch
        {
            // Not ready, failed, or cancelled while starting: the app never outlives this call (D86).
            await started.DisposeAsync();
            throw;
        }
    }

    /// <summary>Node or a Playwright browser missing, from a frontend run's output and exit code (127: not found).</summary>
    static string? MissingTool(CommandResult r)
    {
        var line = r.Output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l =>
            Regex.IsMatch(l, @"npx: (command )?not found|command not found: npx|node: (command )?not found|env: node|Executable doesn't exist|npx playwright install", RegexOptions.IgnoreCase));
        if (line is not null) return $"the test runner couldn't start: {Short(line, 300)}";
        return r.ExitCode == 127 ? $"the test runner couldn't start (exit 127, a command not found): {Short(r.Output.Trim(), 300)}" : null;
    }

    /// <summary>The lines of a runner's output that say why it couldn't run, the runtime's name and version included.</summary>
    static string RunnerLines(string output)
    {
        var lines = output.Split('\n').Select(l => l.Trim())
            .Where(l => Regex.IsMatch(l, @"error|fail|You must install|Framework:|No test is available|Discovered 0 tests", RegexOptions.IgnoreCase)).Distinct().Take(8).ToList();
        return lines.Count > 0 ? string.Join(" | ", lines) : Short(output.Trim(), 600);
    }

    /// <summary>A start command's output, short: its error lines if any, else its last lines.</summary>
    static string StartError(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var errors = lines.Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains("fail", StringComparison.OrdinalIgnoreCase)).Distinct().Take(5).ToList();
        var text = string.Join(" | ", errors.Count > 0 ? errors : lines.TakeLast(5));
        return text.Length == 0 ? "it printed nothing" : Short(text, 800);
    }

    /// <summary>A test name under VSTest's list header (indented 4; a stack frame is indented 3).</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"(?m)^The following Tests are available:\r?\n {4}\S")]
    private static partial System.Text.RegularExpressions.Regex ListedTest();

    /// <summary>One run's result. A message is stored cut to 2,000 characters; the whole of it stays in memory as
    /// <c>full</c> for the D95 check and is dropped before saving (<see cref="Stored"/>).</summary>
    static JsonObject Run(string result, string message, string? file, int? line, long ms)
    {
        var run = new JsonObject { ["result"] = result, ["message"] = Short(message, 2000), ["file"] = file, ["line"] = line, ["ms"] = ms };
        if (message.Length > 2000) run["full"] = message;
        return run;
    }

    static JsonObject Stored(JsonObject run)
    {
        var copy = run.DeepClone().AsObject();
        copy.Remove("full");
        return copy;
    }

    /// <summary>The lines an assertion statement spans from its first line: to the line ending in <c>;</c>, at most 8.</summary>
    public static (int From, int To) Span(string[] lines, int line)
    {
        var to = line;
        while (to < lines.Length && to - line < 8 && !lines[to - 1].TrimEnd().EndsWith(';') && lines[to].Trim().Length > 0
               && !lines[to].TrimStart().StartsWith("expect(", StringComparison.Ordinal) && !lines[to].TrimStart().StartsWith("Assert", StringComparison.Ordinal)
               && !lines[to].TrimStart().StartsWith('}'))
            to++;
        return (line, to);
    }

    static string Errors(string output)
    {
        var errors = output.Split('\n').Where(l => l.Contains(" error ", StringComparison.Ordinal) || l.Contains("error:", StringComparison.OrdinalIgnoreCase)).Distinct().Take(10).ToList();
        return errors.Count > 0 ? string.Join("\n", errors) : output.Length > 1500 ? output[^1500..] : output;
    }

    static string Quotable(JsonObject pulled)
    {
        var bug = pulled["bug"] as JsonObject;
        var parts = TicketPull.BugParts.Select(p => (string?)bug?[p]).OfType<string>();
        return TicketText.From(pulled).Quotable + "\n\n" + string.Join("\n\n", parts);
    }

    /// <summary>
    /// What Claude Code refused (its result's <c>permission_denials</c>) that stangent's rules allow: a write to the result
    /// file or to a file a target allows, or a command line made only of allowed commands and <c>cd</c>s (D89).
    /// Refusals of anything else are the rules working: the reproducer reached outside what it may do.
    /// </summary>
    public static List<string> Blocked(StangentRepo repo, TicketPaths paths, IReadOnlyList<TestTarget> targets, JsonObject claudeResult)
    {
        var found = new List<string>();
        var output = OutputPath(paths);
        var commands = targets.SelectMany(t => Commands(paths, t)).Distinct().ToList();
        foreach (var d in (claudeResult["permission_denials"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var tool = (string?)d["tool_name"] ?? "";
            var input = d["tool_input"] as JsonObject;
            if (tool is "Edit" or "Write" or "MultiEdit" or "NotebookEdit" && ((string?)input?["file_path"] ?? (string?)input?["notebook_path"]) is { Length: > 0 } file)
            {
                var full = Path.GetFullPath(Path.Combine(repo.Root, file));
                var rel = Path.GetRelativePath(repo.Root, full).Replace('\\', '/');
                if (full == output || (!rel.StartsWith("../", StringComparison.Ordinal) && rel != ".." && targets.Any(t => t.Allows(rel))))
                    found.Add($"{tool} {rel}");
            }
            // A command line counts only when all of it is allowed: allowed commands and `cd`s, nothing piped or
            // redirected. `dotnet test … && rm -rf x` refused for the `rm` is the rules working.
            else if (tool == "Bash" && (string?)input?["command"] is { } command && AllAllowed(repo.Root, command, commands))
                found.Add($"Bash {Short(command, 80)}");
        }
        return [.. found.Distinct()];
    }

    /// <summary>Every part is an allowed command or a <c>cd</c> that stays in the repo; nothing piped, redirected,
    /// substituted or sent to the background.</summary>
    static bool AllAllowed(string root, string command, List<string> allowed)
    {
        if (command.IndexOfAny(['|', '>', '<', '`', '$']) >= 0 || command.Replace("&&", "").Contains('&')) return false;
        var parts = command.Split(["&&", ";", "\n"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var here = Path.GetFullPath(root);
        var inside = here.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var any = false;
        foreach (var part in parts)
        {
            if (part.StartsWith("cd ", StringComparison.Ordinal))
            {
                here = Path.GetFullPath(Path.Combine(here, part[3..].Trim().Trim('\'', '"')));
                if (here != Path.GetFullPath(root) && !here.StartsWith(inside, StringComparison.Ordinal)) return false;
            }
            else if (allowed.Any(c => part.StartsWith(c, StringComparison.Ordinal))) any = true;
            else return false;
        }
        return any;
    }

    /// <summary>A capped attempt (D81): Claude Code stopped at its turn or budget limit, or the attempt's calls together
    /// went past the turns or dollars allowed.</summary>
    static string? CapHit(StangentRepo repo, string? subtype, double spentUsd, int turns)
    {
        var budget = repo.Config.Tickets.ProveBudget;
        subtype ??= "";
        if (subtype.Contains("max_turns", StringComparison.Ordinal) && turns == 0) return "hit the cap: Claude Code stopped at its turn limit";   // a result with no rounds (D104)
        if (subtype.Contains("max_turns", StringComparison.Ordinal) || turns > budget.Turns) return $"hit the cap: {turns} of {budget.Turns} turns";
        if (subtype.Contains("budget", StringComparison.Ordinal) || spentUsd > budget.Usd) return $"hit the cap: ${spentUsd:0.00} of ${budget.Usd:0.00}";
        return null;
    }

    /// <summary>The commit, with <c>-dirty</c> when the working tree has tracked changes beyond the test file.</summary>
    static string Commit(string root, string? testFile)
    {
        string Git(string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", args) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
            try
            {
                using var p = System.Diagnostics.Process.Start(psi)!;
                var text = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 ? text : "";
            }
            catch (System.ComponentModel.Win32Exception) { return ""; }
        }
        var head = Git("rev-parse HEAD").Trim();
        if (head.Length == 0) return "unknown";
        var dirty = Git("status --porcelain --untracked-files=no").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l[3..].Trim()).Any(f => f != testFile && !f.StartsWith(".stangent/", StringComparison.Ordinal));
        return dirty ? head + "-dirty" : head;
    }

    static bool UsesMtp(string root)
    {
        var global = Path.Combine(root, "global.json");
        return System.IO.File.Exists(global) && System.IO.File.ReadAllText(global).Contains("Microsoft.Testing.Platform", StringComparison.Ordinal);
    }

    static string Short(string s, int n = 120) => s.Length > n ? s[..n] + "…" : s;
}

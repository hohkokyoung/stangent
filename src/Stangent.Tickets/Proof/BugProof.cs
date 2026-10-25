using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
public static class BugProof
{
    public const string Schema = "bug-proof.v1.json";
    public const int PlaywrightRuns = 3;
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
        var targets = Targets(repo, ctx, bundle);
        var proof = Load(paths);

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

        var input = Input(repo, pulled, bundle, targets, hint, proof);
        // Earlier attempts are shown to the reproducer but aren't an input change: an unchanged ticket, bundle, targets
        // and hint make no new AI call (D18). A new hint does.
        var hash = CanonicalJson.Hash(Input(repo, pulled, bundle, targets, hint, null));
        if (Latest(proof) is { } latest && (string?)latest["inputs"] == hash && (string?)latest["outcome"] != "failed") return new RunStep("current", "none");
        AiPolicy.EnsureAllowed(repo.Config, "ticket prove");
        var n = (proof?["attempts"]?.AsArray().Count ?? 0) + 1;
        return AiStep.Start(paths, StepSpec.Prove, input, hash, new JsonObject
        {
            ["attempt"] = n, ["hint"] = hint, ["targets"] = new JsonArray([.. targets.Select(t => (JsonNode)t.ToJson())]),
        });
    }

    /// <summary>The limits the driver runs the reproducer under (D60, D81): what it may edit and run, its turns and cost.</summary>
    public static JsonObject DriverLimits(StangentRepo repo, string key)
    {
        var paths = new TicketPaths(repo.Root, key);
        var state = AiStep.State(paths, StepSpec.Prove);
        var targets = state["targets"]!.AsArray().Select(t => TestTarget.FromJson(t!)).ToList();
        var tools = new List<string> { "Read", "Glob", "Grep", $"Edit({paths.Relative(AiStep.Work(paths, StepSpec.Prove))}/**)" };
        foreach (var t in targets)
        {
            tools.AddRange(t.EditRules());
            if (t.BuildCommand is { } build) tools.Add($"Bash({build}:*)");
            tools.Add(t.Runner switch
            {
                "dotnet" => $"Bash(dotnet test '{t.Project}':*)",
                "vitest" => "Bash(npx vitest run:*)",
                "jest" => "Bash(npx jest:*)",
                _ => "Bash(npx playwright test --list:*)",
            });
            if (t.Runner is "vitest" or "jest") tools.Add($"Bash(cd {t.Dir})");
        }
        // What's left of the attempt's cap: a retry doesn't get a fresh one (D81).
        var budget = repo.Config.Tickets.ProveBudget;
        var spent = Usage.FromJson(state["usage"]).CostUsd;
        var turns = (int?)state["turns"] ?? 0;
        return new JsonObject
        {
            ["allowedTools"] = new JsonArray([.. tools.Distinct().Select(x => (JsonNode)x)]),
            ["permissionMode"] = "dontAsk", ["maxTurns"] = Math.Max(1, budget.Turns - turns),
            ["maxBudgetUsd"] = Math.Max(0.01, Math.Round(budget.Usd - spent, 2)), ["multiTurn"] = true,
        };
    }

    static List<TestTarget> Targets(StangentRepo repo, TicketContext ctx, JsonObject bundle)
    {
        var symbols = bundle["pointers"]!.AsArray().Where(p => (string?)p!["from"] != "impact").Select(p => ctx.Analyzer.Find((string)p!["id"]!)).OfType<Node>().ToList();
        var projects = symbols.Select(s => s.Project).OfType<string>().Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(p => p["project:".Length..]).Distinct(StringComparer.Ordinal).ToList();
        var files = symbols.Select(s => s.File).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        return TestTargets.Find(repo, projects, files);
    }

    static string Input(StangentRepo repo, JsonObject pulled, JsonObject bundle, List<TestTarget> targets, string? hint, JsonObject? proof)
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
            var run = t.Kind == TestTarget.Playwright ? "stangent runs it (it starts the app)"
                : $"`{(t.Runner is "vitest" or "jest" ? $"cd {t.Dir} && " : "")}{t.TestCommand("<file>", "<test name>", report + (t.Runner == "dotnet" ? ".trx" : ".json"), !mtp)}`";
            sb.Append($"| `{t.Id}` | {t.Kind} | `{t.Dir}/` | {string.Join(", ", t.Patterns)} | {(t.BuildCommand is { } b ? $"`{b}`" : "none")} | {run} |\n");
        }
        if (repo.Config.Tickets.App is { } app)
            sb.Append($"\nThe app for Playwright runs at `{app.Url}`, started and seeded by stangent with `{app.Seed ?? "(no seed step)"}`.\n");
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
            throw new StangentException("usage", "check --step prove needs --usage <Claude Code's JSON result>: the attempt's turn and cost cap is enforced from it (D81).", ExitCodes.Usage);
        var usage = Usage.FromClaudeResult(result);
        state["turns"] = ((int?)state["turns"] ?? 0) + (result["num_turns"] is JsonValue nt && nt.TryGetValue<int>(out var n) ? n : 0);
        var targets = state["targets"]!.AsArray().Select(t => TestTarget.FromJson(t!)).ToList();
        var attempt = new JsonObject { ["n"] = (int)state["attempt"]!, ["hint"] = state["hint"]?.DeepClone(), ["inputs"] = state["inputs"]!.DeepClone() };

        // D81: an attempt that stops at its cap is "not reproduced".
        if (CapHit(repo, (string?)result["subtype"], Usage.FromJson(state["usage"]).Add(usage).CostUsd, (int)state["turns"]!) is { } cap)
        {
            state["usage"] = Usage.FromJson(state["usage"]).Add(usage).ToJson();
            return Finish(repo, paths, state, attempt, "not-reproduced", cap, null);
        }

        var (output, errors) = AiStep.Read(paths, step, state, usage);
        // Code found test targets, so "not provable" is the reproducer's judgment, not a fact: it gets the one retry, and
        // if it says so again, it's recorded labelled as its judgment.
        if (output is not null && (string)output["outcome"]! == "not-provable")
        {
            var reason = (string?)output["reason"] ?? "no reason given";
            if (AiStep.Retry(paths, step, state, [$"Code found test targets ({string.Join(", ", targets.Select(t => t.Id))}). Write a test in one of them; answer not-provable again only if none can reach the behaviour, saying why. You said: {reason}"]) is { } again)
                return again;
            return Finish(repo, paths, state, attempt, "not-provable", $"the reproducer's judgment (code found test targets): {reason}", null);
        }

        TestTarget? target = null;
        string? testFile = null;
        (int From, int To) span = (0, 0);
        if (output is not null)
        {
            foreach (var field in new[] { "target", "testFile", "testName", "assertion", "expectedQuote" })
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
            List<JsonObject> results;
            try { results = await RunTest(repo, paths, target, testFile!, (string)output["testName"]!, span, runner, target.Kind == TestTarget.Playwright ? PlaywrightRuns : 1, ct); }
            catch (StangentException e) when (e.Code.StartsWith("app_", StringComparison.Ordinal))
            {
                // The app didn't build, start or seed: not the test's fault, and not something a retry fixes.
                return Finish(repo, paths, state, attempt, "failed", e.Message, null);
            }
            foreach (var r in results) runs.Add(r.DeepClone());
            var kinds = results.Select(r => (string)r["result"]!).ToList();
            if (kinds.All(k => k == "failed-on-assertion") && results.Select(r => (int?)r["line"]).Distinct().Count() == 1) outcome = "reproduced";
            else if (kinds.All(k => k == "passed")) outcome = "not-reproduced";
            else if (target.Kind == TestTarget.Playwright && kinds.All(k => k is "passed" or "failed-on-assertion")) outcome = "not-reproduced-flaky";
            else errors.AddRange(results.Where(r => (string)r["result"]! is not ("passed" or "failed-on-assertion"))
                .Select(r => $"The test run gave {r["result"]}: {Short((string?)r["message"] ?? "", 600)}").Distinct());
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

    /// <summary>The developer confirms the failure matches the reported actual result (D75).</summary>
    public static JsonObject Confirm(StangentRepo repo, string key, string who)
    {
        var paths = new TicketPaths(repo.Root, key);
        var proof = Load(paths) ?? throw new StangentException("no_proof", $"{key} has no proof yet. Run `stangent ticket prove {key}`.", ExitCodes.NotFound);
        if ((string)proof["outcome"]! != "reproduced")
            throw new StangentException("not_reproduced", $"{key}'s proof is {(string)proof["outcome"]!}; only a reproduced bug is confirmed.", ExitCodes.Usage);
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
            ["newFailures"] = new JsonArray([.. fresh.Select(f => (JsonNode)f)]), ["runs"] = new JsonArray([.. runs.Select(r => r.DeepClone())]),
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
        await using var app = target.Kind == TestTarget.Playwright ? await StartApp(repo, runner, ct) : null;
        var results = new List<JsonObject>();
        for (var i = 1; i <= times; i++)
        {
            var report = Path.Combine(dir, $"run-{i}" + (target.Runner == "dotnet" ? ".trx" : ".json"));
            if (System.IO.File.Exists(report)) System.IO.File.Delete(report);
            var fileArg = target.Runner is "vitest" or "jest" ? Path.GetRelativePath(Path.Combine(repo.Root, target.Dir), Path.Combine(repo.Root, testFile)).Replace('\\', '/') : testFile;
            var env = target.Kind == TestTarget.Playwright
                ? new Dictionary<string, string> { ["PLAYWRIGHT_JSON_OUTPUT_NAME"] = report, ["BASE_URL"] = repo.Config.Tickets.App?.Url ?? "" } : null;
            var r = await runner.RunAsync(target.TestCommand(target.Runner == "dotnet" ? null : fileArg, testName, report, !UsesMtp(repo.Root)), target.WorkingDir(repo.Root), CommandTimeout, env, ct);
            results.Add(Classify(target, report, r, testFile, testName, span));
        }
        return results;
    }

    static JsonObject Classify(TestTarget target, string report, CommandResult r, string testFile, string testName, (int From, int To) span)
    {
        if (r.TimedOut) return Run("timeout", "the test run timed out", null, null, r.Ms);
        if (!System.IO.File.Exists(report)) return Run(r.ExitCode == 0 ? "not-found" : "error", r.ExitCode == 0 ? "no report: the test name matched nothing" : Errors(r.Output), null, null, r.Ms);
        List<TestCaseResult> cases;
        try { cases = Parse(target, System.IO.File.ReadAllText(report)); }
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

    static async Task<IAsyncDisposable?> StartApp(StangentRepo repo, ICommandRunner runner, CancellationToken ct)
    {
        var app = repo.Config.Tickets.App!;
        if (app.Build is { } build && (await runner.RunAsync(build, repo.Root, CommandTimeout, null, ct)) is { ExitCode: not 0 } b)
            throw new StangentException("app_build_failed", $"tickets.app.build failed: {Errors(b.Output)}");
        var started = runner.Start(app.Start!, repo.Root);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(app.ReadySeconds);
        while (true)
        {
            try { using var r = await http.GetAsync(app.Url, ct); if ((int)r.StatusCode < 500) break; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
            if (DateTime.UtcNow > deadline) { await started.DisposeAsync(); throw new StangentException("app_not_ready", $"The app didn't answer at {app.Url} within {app.ReadySeconds} s."); }
            await Task.Delay(1000, ct);
        }
        if (app.Seed is { } seed && (await runner.RunAsync(seed, repo.Root, CommandTimeout, null, ct)) is { ExitCode: not 0 } s)
        {
            await started.DisposeAsync();
            throw new StangentException("app_seed_failed", $"tickets.app.seed failed: {Errors(s.Output)}");
        }
        return started;
    }

    static JsonObject Run(string result, string message, string? file, int? line, long ms) => new()
    {
        ["result"] = result, ["message"] = Short(message, 2000), ["file"] = file, ["line"] = line, ["ms"] = ms,
    };

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

    /// <summary>A capped attempt (D81): Claude Code stopped at its turn or budget limit, or the attempt's calls together
    /// went past the turns or dollars allowed.</summary>
    static string? CapHit(StangentRepo repo, string? subtype, double spentUsd, int turns)
    {
        var budget = repo.Config.Tickets.ProveBudget;
        subtype ??= "";
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

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.State;
using Stangent.Graph;
using Stangent.Graph.Impact;
using Stangent.Spec.Pages;
using Stangent.Tickets.Proof;

namespace Stangent.Tickets.Fix;

/// <summary>
/// The minimal fix loop (Phase 4, D343–D350): one fix agent per ticket, every check run by code.
/// <c>fix start</c> makes the branch and worktree with the bug proof's test committed first; <c>fix prepare</c> writes the
/// fixer's input and limits; <c>fix check</c> runs the five checks in order (plan, files, build, tests, test reach) and
/// gives one retry; <c>fix report</c> writes <c>report.md</c> and <c>agent-run.json</c>. Nothing here pushes (D347).
/// </summary>
public static class FixLoop
{
    public const string Agent = "stangent-fixer";
    public const string OutputSchema = "fixer-output.v1.json";
    public const string PlanSchema = "task-plan.v1.json";
    public const string RunSchema = "agent-run.v1.json";
    public static readonly string[] Contexts = ["ticket", "bundle"];
    public static readonly string[] CheckNames = ["plan", "files", "build", "tests", "test-reach"];
    /// <summary>How much of a check's output goes back to the fixer on its retry.</summary>
    const int RetryChars = 4000;

    /// <summary>Rebuilds the code graph in the worktree (D350 step 4); null or a throw means it couldn't be.</summary>
    public delegate Task<CodeGraph?> GraphRebuild(StangentRepo worktree, CancellationToken ct);

    public static JsonObject? Load(FixPaths paths) => PageRun.LoadJson(paths.State) as JsonObject;

    static JsonObject LoadOrFail(FixPaths paths) => Load(paths)
        ?? throw new StangentException("no_fix", $"{paths.Key} has no fix run. Start one with `stangent fix start {paths.Key}`.", ExitCodes.NotFound);

    static void Save(FixPaths paths, JsonObject state)
    {
        Directory.CreateDirectory(paths.Dir);
        File.WriteAllText(paths.State, CanonicalJson.Serialize(state));
    }

    static void EnsureOn(StangentRepo repo)
    {
        if (!StangentComponents.IsOn(repo.Config, "fix"))
            throw new StangentException("fix_off", "The fix loop is off (components.fix: false), so nothing was tried.", ExitCodes.Usage);
    }

    // ---- start ----

    /// <summary>
    /// The ticket's branch <c>stangent/fix-&lt;KEY&gt;</c> in a worktree at <c>.stangent/fix/&lt;KEY&gt;/worktree</c>, from
    /// <paramref name="baseRef"/> (HEAD when null), with the bug proof's test committed first (D346, D350). Refuses a
    /// ticket with no reproduced proof (it's recorded, so <c>fix report</c> says "no failing test"), a base with changes
    /// beyond the proof's test, and a run that already exists unless <paramref name="replace"/>.
    /// </summary>
    public static JsonObject Start(StangentRepo repo, string key, string? baseRef, bool replace)
    {
        EnsureOn(repo);
        var paths = new FixPaths(repo.Root, key);
        var tpaths = new TicketPaths(repo.Root, key);
        var proof = BugProof.Load(tpaths);
        var reproduced = proof?["attempts"]?.AsArray().OfType<JsonObject>().LastOrDefault(a => (string?)a["outcome"] == "reproduced");
        if (proof is null || (string?)proof["outcome"] != "reproduced" || reproduced is null)
        {
            var why = proof is null
                ? $"{key} has no bug proof: run `stangent ticket prove {key}` first; the loop starts from the proof's failing test (D346, D350)"
                : $"{key}'s bug proof is {(string?)proof["outcome"]}: there is no failing test to start from, so the ticket goes to the developer (D346)";
            if (!Directory.Exists(paths.Worktree))
            {
                Move(repo, paths, (string?)Load(paths)?["stop"]?["reason"], "no-failing-test", why);
                Save(paths, new JsonObject
                {
                    ["key"] = paths.Key, ["branch"] = paths.Branch, ["base"] = baseRef ?? "HEAD", ["baseCommit"] = "",
                    ["context"] = null, ["proofCostUsd"] = ProofCost(proof), ["capUsd"] = repo.Config.Fix.TicketUsd,
                    ["calls"] = new JsonArray(), ["checks"] = new JsonArray(), ["commits"] = new JsonArray(),
                    ["stop"] = new JsonObject { ["reason"] = "no-failing-test", ["detail"] = why },
                });
            }
            throw new StangentException("no_failing_test", why + ".", ExitCodes.Usage);
        }
        if (!Git.Run(repo.Root, "rev-parse", "--is-inside-work-tree").Ok)
            throw new StangentException("not_git", $"{repo.Root} isn't in a git repo: the fix loop works on its own branch (D347).", ExitCodes.Usage);
        var prefix = Git.Must(repo.Root, "rev-parse", "--show-prefix").Trim();
        var testFile = (string)reproduced["testFile"]!;
        var baseCommit = Git.Run(repo.Root, "rev-parse", "--verify", "--quiet", (baseRef ?? "HEAD") + "^{commit}") is { Ok: true } b ? b.Out.Trim()
            : throw new StangentException("bad_base", $"'{baseRef ?? "HEAD"}' isn't a commit here.", ExitCodes.Usage);

        // A dirty base: a tracked file changed, but the proof's test (the reproducer wrote it here), stangent's own folder
        // and the local files. Untracked files never reach the worktree, so they aren't dirt (an untracked stangent.yaml,
        // page output): they're named in a warning instead (D353).
        var local = repo.Config.Fix.LocalFiles.Select(p => TestTarget.Normalize(p).TrimEnd('/')).Where(p => p.Length > 0).ToHashSet(StringComparer.Ordinal);
        var untracked = Git.Must(repo.Root, "ls-files", "--others", "--exclude-standard", "--full-name", "-z")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(p => Strip(prefix, p)).ToHashSet(StringComparer.Ordinal);
        bool Ours(string p) => p == testFile || IsStangent(p) || Under(p, local);
        var dirty = Git.Changed(repo.Root).Select(p => Strip(prefix, p)).Where(p => !Ours(p) && !untracked.Contains(p)).Order(StringComparer.Ordinal).ToList();
        var leftOut = untracked.Where(p => !Ours(p) && p != StangentRepo.ConfigFile).Order(StringComparer.Ordinal).ToList();
        if (dirty.Count > 0)
            throw new StangentException("dirty_base", $"The checkout has changes beyond the proof's test ({string.Join(", ", dirty.Take(5))}{(dirty.Count > 5 ? ", …" : "")}): commit or put them aside first, so the fix starts from a known commit.", ExitCodes.Usage, dirty);

        var previous = (string?)Load(paths)?["stop"]?["reason"];
        var branchExists = Git.Run(repo.Root, "rev-parse", "--verify", "--quiet", "refs/heads/" + paths.Branch).Ok;
        if (Directory.Exists(paths.Worktree) || branchExists || File.Exists(paths.State))
        {
            if (!replace)
                throw new StangentException("fix_exists", $"{key} already has a fix run (branch {paths.Branch}, {paths.Relative(paths.Dir)}). Pass --replace to start again: it removes that worktree and branch.", ExitCodes.Usage);
            if (Directory.Exists(paths.Worktree)) Git.Run(repo.Root, "worktree", "remove", "--force", paths.Worktree);
            Git.Run(repo.Root, "worktree", "prune");
            if (branchExists) Git.Must(repo.Root, "branch", "-D", paths.Branch);
            if (Directory.Exists(paths.Dir)) Directory.Delete(paths.Dir, recursive: true);
        }
        Directory.CreateDirectory(paths.Dir);
        Git.Must(repo.Root, "worktree", "add", "-q", "-b", paths.Branch, paths.Worktree, baseCommit);
        var wtRoot = WorktreeRoot(paths, prefix);

        // The proof's test, committed first (D346, D350): from the checkout, where the reproducer wrote it.
        var source = Path.Combine(repo.Root, testFile);
        var target = Path.Combine(wtRoot, testFile);
        string? testCommit = null;
        if (File.Exists(source))
        {
            var text = File.ReadAllBytes(source);
            if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(text))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, text);
                Git.Must(paths.Worktree, "add", "-f", "--", prefix + testFile);   // -f: under an ignored folder too (e2e/, D354)
                testCommit = Git.Commit(paths.Worktree, $"test: reproduce {paths.Key}\n\nThe bug proof's failing test (stangent ticket prove), committed before any fix (D346).");
            }
        }
        else if (!File.Exists(target))
        {
            Git.Run(repo.Root, "worktree", "remove", "--force", paths.Worktree);
            Git.Run(repo.Root, "branch", "-D", paths.Branch);
            throw new StangentException("no_test_file", $"The proof's test {testFile} is neither in the checkout nor at {baseCommit[..7]}.", ExitCodes.NotFound);
        }

        // What stangent needs in the worktree, git-ignored or left out of every check: its settings (when the repo doesn't
        // track them), the proof (for --verify, with the repo's path in its commands moved to the worktree) and the graph
        // (so the rebuild is incremental).
        var copied = new JsonArray();
        var yaml = Path.Combine(wtRoot, StangentRepo.ConfigFile);
        if (!File.Exists(yaml)) { File.Copy(Path.Combine(repo.Root, StangentRepo.ConfigFile), yaml); copied.Add(StangentRepo.ConfigFile); }
        // A local file or folder as the checkout has it (D351, D356): an SDK pin over the committed global.json, the app's
        // build scripts.
        foreach (var f in local.Order(StringComparer.Ordinal))
        {
            if (f == testFile || IsStangent(f) || f.Split('/').Contains("..")) continue;
            var from = Path.Combine(repo.Root, f);
            if (Directory.Exists(from)) CopyFolder(from, Path.Combine(wtRoot, f));
            else if (File.Exists(from))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(wtRoot, f))!);
                File.Copy(from, Path.Combine(wtRoot, f), overwrite: true);
            }
            else continue;
            if (!copied.Any(c => (string?)c == f)) copied.Add(f);
        }
        var wtTickets = new TicketPaths(wtRoot, key);
        Directory.CreateDirectory(wtTickets.Dir);
        var moved = proof.DeepClone().AsObject();
        foreach (var a in moved["attempts"]!.AsArray().OfType<JsonObject>())
            if (a["targetSpec"] is JsonObject spec && (string?)spec["args"] is { Length: > 0 } args)
                spec["args"] = args.Replace(repo.Root.TrimEnd('/'), wtRoot.TrimEnd('/'), StringComparison.Ordinal);
        moved.Remove("verification");
        File.WriteAllText(BugProof.File(wtTickets), CanonicalJson.Serialize(moved));
        var graph = GraphStore.PathFor(repo);
        if (File.Exists(graph)) File.Copy(graph, Path.Combine(wtRoot, ".stangent", "graph.sqlite"), overwrite: true);

        var warnings = new JsonArray();
        var proofCommit = ((string?)reproduced["commit"] ?? "").Replace("-dirty", "");
        if (proofCommit.Length > 0 && !baseCommit.StartsWith(proofCommit, StringComparison.Ordinal) && !proofCommit.StartsWith(baseCommit, StringComparison.Ordinal))
            warnings.Add($"the proof was made at {proofCommit[..Math.Min(7, proofCommit.Length)]}, the base is {baseCommit[..7]}: the test may fail differently here");
        if (!Git.Run(repo.Root, "check-ignore", "-q", ".stangent/fix/x").Ok)
            warnings.Add("`.stangent/fix/` isn't git-ignored here: add it to .gitignore, or the worktree shows as untracked");
        if (leftOut.Count > 0)
            warnings.Add($"{leftOut.Count} untracked file(s) left out of the worktree ({string.Join(", ", leftOut.Take(5))}{(leftOut.Count > 5 ? ", …" : "")}): commit one the fix needs, or list it in fix.localFiles");

        var state = new JsonObject
        {
            ["key"] = paths.Key, ["branch"] = paths.Branch, ["base"] = baseRef ?? "HEAD", ["baseCommit"] = baseCommit,
            ["worktree"] = paths.Relative(paths.Worktree), ["prefix"] = prefix,
            ["testFile"] = testFile, ["testName"] = (string)reproduced["testName"]!,
            ["failedBefore"] = (string?)reproduced["runs"]?[0]?["message"] ?? "",
            ["proofCommit"] = proofCommit, ["proofCostUsd"] = ProofCost(proof),
            ["testCommit"] = testCommit ?? baseCommit, ["copied"] = copied,
            ["capUsd"] = repo.Config.Fix.TicketUsd, ["callUsd"] = repo.Config.Fix.CallBudget.Usd, ["callTurns"] = repo.Config.Fix.CallBudget.Turns,
            ["context"] = null, ["round"] = 0, ["calls"] = new JsonArray(), ["checks"] = new JsonArray(),
            ["commits"] = testCommit is null ? new JsonArray() : new JsonArray(testCommit),
            ["stop"] = new JsonObject { ["reason"] = "in-progress", ["detail"] = "started" },
        };
        Move(repo, paths, previous, "in-progress", replace && previous is not null ? "started again (--replace)" : "started");
        Save(paths, state);
        return new JsonObject
        {
            ["key"] = paths.Key, ["branch"] = paths.Branch, ["worktree"] = paths.Relative(paths.Worktree), ["baseCommit"] = baseCommit,
            ["testFile"] = testFile, ["testCommit"] = testCommit, ["proofCostUsd"] = ProofCost(proof), ["warnings"] = warnings,
            ["next"] = new JsonObject { ["action"] = "prepare", ["command"] = $"stangent fix prepare {paths.Key} --context bundle" },
        };
    }

    /// <summary>The run's lifecycle through the state module (D114): an ended run starts again only through in-progress.</summary>
    static void Move(StangentRepo repo, FixPaths paths, string? from, string to, string reason)
    {
        if (from == to && to != "in-progress") return;
        if (from is not null and not "in-progress" && to != "in-progress")
        {
            StateChanges.Move(repo.Root, StateKinds.FixRun, paths.Key, from, "in-progress", StateActor.Code("fix start"), reason);
            from = "in-progress";
        }
        StateChanges.Move(repo.Root, StateKinds.FixRun, paths.Key, from, to, StateActor.Code(to == "in-progress" ? "fix start" : "fix check"), reason);
    }

    static double ProofCost(JsonObject? proof) =>
        Math.Round((proof?["attempts"]?.AsArray() ?? []).OfType<JsonObject>().Sum(a => Usage.FromJson(a["usage"]).CostUsd), 6);

    static string WorktreeRoot(FixPaths paths, string prefix) => prefix.Length == 0 ? paths.Worktree : Path.Combine(paths.Worktree, prefix.TrimEnd('/'));

    static string WorktreeRoot(FixPaths paths, JsonObject state) => WorktreeRoot(paths, (string?)state["prefix"] ?? "");

    static string Strip(string prefix, string gitPath) => prefix.Length == 0 ? gitPath : gitPath.StartsWith(prefix, StringComparison.Ordinal) ? gitPath[prefix.Length..] : "../" + gitPath;

    /// <summary>The path is one of these, or inside one of these folders.</summary>
    static bool Under(string path, IEnumerable<string> roots) => roots.Any(r => path == r || path.StartsWith(r + "/", StringComparison.Ordinal));

    static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    static bool IsStangent(string path) => path == ".stangent" || path.StartsWith(".stangent/", StringComparison.Ordinal);

    // ---- prepare ----

    /// <summary>The fixer's input (D345's two sides: <c>ticket</c> or <c>bundle</c>) and the limits the driver runs it under.</summary>
    public static JsonObject Prepare(StangentRepo repo, string key, string context)
    {
        EnsureOn(repo);
        if (!Contexts.Contains(context))
            throw new StangentException("usage", $"--context is ticket or bundle, not '{context}'.", ExitCodes.Usage);
        var paths = new FixPaths(repo.Root, key);
        var state = LoadOrFail(paths);
        if ((string?)state["stop"]?["reason"] != "in-progress") return Step(repo, paths, state, (string)state["stop"]!["reason"]!);
        if (state["calls"]!.AsArray().Count > 0)
            throw new StangentException("already_run", $"{key}'s fixer has run already. Start again with `stangent fix start {key} --replace`.", ExitCodes.Usage);
        if (context == "bundle") TicketBundle.LoadOrFail(repo.Root, key);
        AiPolicy.EnsureAllowed(repo.Config, "fix prepare");
        state["context"] = context;
        state["round"] = 1;
        if (Left(state) < 0.01) return Stop(repo, paths, state, "cap-hit", $"the ticket's ${(double)state["capUsd"]!:0.##} is spent (the proof took ${(double)state["proofCostUsd"]!:0.##})");
        WriteInput(repo, paths, state, null);
        Save(paths, state);
        return Step(repo, paths, state, "prepared");
    }

    static double Spent(JsonObject state) =>
        (double)state["proofCostUsd"]! + state["calls"]!.AsArray().Sum(c => (double)c!["costUsd"]!);

    static double Left(JsonObject state) => Math.Round((double)state["capUsd"]! - Spent(state), 4);

    static void WriteInput(StangentRepo repo, FixPaths paths, JsonObject state, string? retry)
    {
        var key = paths.Key;
        var wtRoot = WorktreeRoot(paths, state);
        var pulled = TicketPull.LoadOrFail(repo.Root, key);
        var sb = new StringBuilder("# Instructions\n\n").Append(AiStep.Instructions("fixer.md"));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText(OutputSchema).TrimEnd('\n')).Append("\n```\n\n");
        sb.Append($"# The ticket: {key}\n\n");
        if (pulled["bug"] is JsonObject bug)
            foreach (var part in TicketPull.BugParts)
                sb.Append($"- **{part}:** {((string?)bug[part] is { } v ? v.Replace("\n", "\n  ") : "_not given_")}\n");
        sb.Append('\n');
        foreach (var (where, text) in TicketText.From(pulled).Prose) sb.Append($"## {where}\n\n````text\n{text}\n````\n\n");
        if ((string?)state["context"] == "bundle")
            sb.Append("# The ticket bundle\n\n").Append(TicketBundle.Markdown(TicketBundle.LoadOrFail(repo.Root, key))).Append('\n');

        var testFile = (string)state["testFile"]!;
        var testPath = Path.Combine(wtRoot, testFile);
        sb.Append($"# The failing test\n\n`{state["testName"]}` in `{testFile}`, committed on this branch before you started. **Don't edit it.**\n\n");
        if (File.Exists(testPath)) sb.Append($"```{Fence(testFile)}\n{File.ReadAllText(testPath).TrimEnd('\n')}\n```\n\n");
        sb.Append($"How it failed on the unchanged code:\n\n````text\n{state["failedBefore"]}\n````\n\n");

        var target = Target(paths, state);
        sb.Append("# Where you work\n\nYour current folder is the worktree: edit the repo's source there and nowhere else.\n\n");
        sb.Append("| what | command |\n|---|---|\n");
        if (target?.BuildCommand is { } build) sb.Append($"| build | `{build}` |\n");
        foreach (var c in TestCommands(target)) sb.Append($"| run tests | `{c} …` |\n");
        sb.Append($"\n# Your limits\n\n- At most {CallTurns(state)} turns and ${CallBudget(state):0.##} for this call; stopping at the cap stops the ticket (D348).\n");
        sb.Append("- Edit only files in your current folder; never the failing test, `.stangent/`, `.git/` or `stangent.yaml`.\n");
        sb.Append("- Run only the commands above. Don't commit, push or open a pull request.\n");
        if (retry is not null) sb.Append("\n# Your previous attempt (retry)\n\nYour edits are still in the worktree. The checks that failed:\n\n").Append(retry).Append('\n');
        Directory.CreateDirectory(paths.Work);
        File.WriteAllText(paths.Input, sb.ToString());
    }

    static string Fence(string file) => Path.GetExtension(file) switch { ".cs" => "csharp", ".ts" => "ts", ".tsx" => "tsx", ".js" => "js", ".py" => "python", _ => "" };

    static int CallTurns(JsonObject state) => (int)state["callTurns"]!;

    /// <summary>A call's cap: its own $1, never more than what's left of the ticket's $3 (D348).</summary>
    static double CallBudget(JsonObject state) => Math.Round(Math.Max(0.01, Math.Min((double)state["callUsd"]!, Left(state))), 4);

    /// <summary>The proof's test target as the worktree's copy of the proof has it (the repo's path moved there).</summary>
    static TestTarget? Target(FixPaths paths, JsonObject state)
    {
        var proof = BugProof.Load(new TicketPaths(WorktreeRoot(paths, state), paths.Key));
        var latest = proof?["attempts"]?.AsArray().OfType<JsonObject>().LastOrDefault(a => (string?)a["outcome"] == "reproduced");
        return latest?["targetSpec"] is JsonObject spec ? TestTarget.FromJson(spec) : null;
    }

    static IEnumerable<string> TestCommands(TestTarget? t)
    {
        if (t is null) yield break;
        switch (t.Runner)
        {
            case "dotnet":
                yield return $"dotnet test {Quote(t.Project!)}";
                yield return $"dotnet test --project {Quote(t.Project!)}";
                break;
            case "vitest": yield return "npx vitest run"; break;
            case "jest": yield return "npx jest"; break;
        }
    }

    static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>The fixer's limits (D60, D89, D348): read and edit inside the worktree only, never stangent's folder, git's,
    /// the proof's test or a copied stangent.yaml; only the target's build and test commands; its turn and dollar caps.
    /// Paths absolute (D89). Its plan comes back as structured output, so it never reads or writes stangent's files.</summary>
    public static JsonObject Limits(FixPaths paths, JsonObject state)
    {
        var wt = ClaudeRules.Absolute(WorktreeRoot(paths, state));
        var gitRoot = ClaudeRules.Absolute(paths.Worktree);
        var allowed = new List<string> { $"Read({wt}/**)", $"Edit({wt}/**)" };
        var target = Target(paths, state);
        if (target?.BuildCommand is { } build) allowed.Add($"Bash({build}:*)");
        allowed.AddRange(TestCommands(target).Select(c => $"Bash({c}:*)"));
        if (target?.Runner is "vitest" or "jest") allowed.Add($"Bash(cd {target.Dir})");
        var denied = new List<string>
        {
            $"Read({wt}/.stangent/**)", $"Edit({wt}/.stangent/**)", $"Read({gitRoot}/.git/**)", $"Edit({gitRoot}/.git/**)", $"Edit({gitRoot}/.git)",
            $"Edit({wt}/{(string)state["testFile"]!})",
        };
        denied.Add($"Edit({wt}/{StangentRepo.ConfigFile})");
        foreach (var c in state["copied"]!.AsArray().Select(c => (string)c!).Where(c => c != StangentRepo.ConfigFile))
        {
            denied.Add($"Edit({wt}/{c})");
            if (Directory.Exists(Path.Combine(WorktreeRoot(paths, state), c))) denied.Add($"Edit({wt}/{c}/**)");
        }
        return new JsonObject
        {
            ["cwd"] = WorktreeRoot(paths, state),
            ["tools"] = "Read,Grep,Glob,Edit,Write,Bash",
            ["allowedTools"] = new JsonArray([.. allowed.Distinct().Select(x => (JsonNode)x)]),
            ["disallowedTools"] = new JsonArray([.. denied.Select(x => (JsonNode)x)]),
            ["permissionMode"] = "dontAsk", ["maxTurns"] = CallTurns(state), ["maxBudgetUsd"] = CallBudget(state), ["multiTurn"] = true,
        };
    }

    /// <summary>What the driver reads: run the fixer (<c>run</c>), or nothing more (<c>none</c>) and why.</summary>
    static JsonObject Step(StangentRepo repo, FixPaths paths, JsonObject state, string status)
    {
        var running = (string?)state["stop"]?["reason"] == "in-progress";
        var next = running
            ? new JsonObject
            {
                ["action"] = "run", ["agent"] = Agent, ["schema"] = OutputSchema,
                ["input"] = paths.Relative(paths.Input), ["output"] = paths.Relative(paths.Output),
                ["limits"] = Limits(paths, state),
            }
            : new JsonObject { ["action"] = "none", ["command"] = $"stangent fix report {paths.Key}" };
        return new JsonObject
        {
            ["key"] = paths.Key, ["status"] = status, ["round"] = (int?)state["round"] ?? 0, ["branch"] = paths.Branch,
            ["stop"] = state["stop"]!.DeepClone(), ["spentUsd"] = Math.Round(Spent(state), 4), ["capUsd"] = state["capUsd"]!.DeepClone(), ["next"] = next,
        };
    }

    static JsonObject Stop(StangentRepo repo, FixPaths paths, JsonObject state, string reason, string detail)
    {
        // What the fixer edited stays on the branch (D348), committed so the branch holds it.
        if (Directory.Exists(paths.Worktree) && Edited(paths, state).Count > 0)
            CommitEdits(paths, state, reason == "fixed" ? $"fix: {paths.Key}" : $"wip: {paths.Key} (stopped: {reason})", detail);
        Move(repo, paths, (string?)state["stop"]?["reason"], reason, detail);
        state["stop"] = new JsonObject { ["reason"] = reason, ["detail"] = detail };
        Save(paths, state);
        return Step(repo, paths, state, reason);
    }

    static void CommitEdits(FixPaths paths, JsonObject state, string subject, string detail)
    {
        var prefix = (string?)state["prefix"] ?? "";
        // An excluded path that git already ignores is left out: git refuses an ignored path named in a pathspec, and
        // `add -A` skips it anyway (a repo that git-ignores stangent.yaml, D355).
        var specs = new List<string> { "add", "-A", "--", prefix.Length == 0 ? "." : prefix };
        specs.AddRange(new[] { ".stangent" }.Concat(state["copied"]!.AsArray().Select(c => (string)c!))
            .Where(p => !Git.Run(paths.Worktree, "check-ignore", "-q", "--no-index", prefix + p).Ok)
            .Select(p => $":(exclude){prefix}{p}"));
        Git.Must(paths.Worktree, [.. specs]);
        if (Git.Run(paths.Worktree, "diff", "--cached", "--quiet").Ok) return;
        var sha = Git.Commit(paths.Worktree, $"{subject}\n\n{detail}\n\nEdited by stangent-fixer and checked by stangent fix check (D343). Not pushed (D347).");
        state["commits"]!.AsArray().Add(sha);
    }

    /// <summary>Files edited on the branch since the proof's test was committed, relative to the stangent root: the test,
    /// stangent's folder and copied settings left out.</summary>
    static List<string> Edited(FixPaths paths, JsonObject state)
    {
        var prefix = (string?)state["prefix"] ?? "";
        var copied = state["copied"]!.AsArray().Select(c => (string)c!).ToHashSet(StringComparer.Ordinal);
        var committed = Git.Must(paths.Worktree, "diff", "--name-only", (string)state["testCommit"]!, "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return [.. Git.Changed(paths.Worktree).Concat(committed).Select(p => Strip(prefix, p))
            .Where(p => !IsStangent(p) && !Under(p, copied)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    // ---- check ----

    sealed record CheckResult(string Name, string Result, string Summary, string Output)
    {
        public bool Failed => Result == "failed";
    }

    /// <summary>
    /// The fixer's call just ended: its cost and turns are recorded, then code runs the checks in order, stopping at the
    /// first that fails (D343, D346, D350): <b>plan</b> (points in the base graph, quotes word for word, tests real),
    /// <b>files</b> (each edit planned or in a planned file's reach), <b>build</b>, <b>tests</b> (the proof's
    /// <c>--verify</c>: only <c>passed</c> passes), <b>test reach</b> (the graph rebuilt in the worktree; the proof's test
    /// among a planned point's tests, else not checked). One retry with the failures; then the ticket stops.
    /// </summary>
    public static async Task<JsonObject> CheckAsync(StangentRepo repo, string key, JsonNode? claudeResult, ICommandRunner runner, GraphRebuild rebuild, CancellationToken ct = default)
    {
        EnsureOn(repo);
        var paths = new FixPaths(repo.Root, key);
        var state = LoadOrFail(paths);
        if ((string?)state["stop"]?["reason"] != "in-progress") return Step(repo, paths, state, (string)state["stop"]!["reason"]!);
        if ((int?)state["round"] is not (1 or 2))
            throw new StangentException("not_prepared", $"Run `stangent fix prepare {key}` first.", ExitCodes.Usage);
        // The caps are enforced from the call's own result (D81, D104), so it's required.
        if (claudeResult is not JsonObject result)
            throw new StangentException("usage", "fix check needs --usage <Claude Code's JSON result, with the driver's rounds>: the ticket's turn and dollar caps are enforced from it (D348).", ExitCodes.Usage);
        var round = (int)state["round"]!;
        var usage = Usage.FromClaudeResult(result);
        var subtype = (string?)result["subtype"];
        var turns = result["rounds"] is JsonValue r && r.TryGetValue<int>(out var n) ? n : (int?)result["num_turns"] ?? 0;
        state["calls"]!.AsArray().Add(new JsonObject
        {
            ["call"] = state["calls"]!.AsArray().Count + 1, ["turns"] = turns, ["costUsd"] = usage.CostUsd,
            ["inputTokens"] = usage.InputTokens + usage.CacheReadTokens + usage.CacheWriteTokens, ["outputTokens"] = usage.OutputTokens,
            ["ended"] = subtype, ["model"] = usage.Run is { Model: not RunSettings.UnknownValue } rs ? rs.Model : usage.Model,
            ["effort"] = usage.Run is { Effort: not RunSettings.UnknownValue } re ? re.Effort : null,
        });

        if (subtype is { } s && (s.Contains("max_turns", StringComparison.Ordinal) || s.Contains("budget", StringComparison.Ordinal)))
            return Stop(repo, paths, state, "cap-hit", $"the fixer's call {round} stopped at its cap ({s}: {turns} turns, ${usage.CostUsd:0.##})");
        if (Spent(state) > (double)state["capUsd"]! + 0.005)
            return Stop(repo, paths, state, "cap-hit", $"the ticket's ${(double)state["capUsd"]!:0.##} is spent (${Spent(state):0.##})");

        var output = File.Exists(paths.Output) ? File.ReadAllText(paths.Output) : "";
        JsonObject? plan = null;
        try { plan = output.Trim().Length > 0 ? JsonNode.Parse(output) as JsonObject : null; } catch (JsonException) { }
        if (plan is not null && plan["changes"] is JsonArray { Count: 0 } && (string?)plan["gaveUp"] is { Length: > 0 } gaveUp && Edited(paths, state).Count == 0)
        {
            WritePlan(repo, paths, state, plan, []);
            return Stop(repo, paths, state, "gave-up", gaveUp);
        }

        var checks = new List<CheckResult>();
        var reach = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var points = new List<(string File, string? Id)>();
        var c1 = CheckPlan(repo, paths, state, output, plan, reach, points);
        checks.Add(c1);
        if (!c1.Failed) WritePlan(repo, paths, state, plan!, points.Select(p => (p.File, p.Id, reach.GetValueOrDefault(p.File) ?? [])).ToList());
        if (!c1.Failed) checks.Add(CheckFiles(paths, state, plan!, reach));
        if (!checks[^1].Failed) checks.Add(await CheckBuild(paths, state, runner, ct));
        if (!checks[^1].Failed) checks.Add(await CheckTests(paths, state, runner, ct));
        if (!checks[^1].Failed) checks.Add(await CheckReach(paths, state, points, rebuild, ct));

        Directory.CreateDirectory(paths.Checks);
        for (var i = 0; i < checks.Count; i++)
        {
            var c = checks[i];
            var file = Path.Combine(paths.Checks, $"round-{round}-{i + 1}-{c.Name}.txt");
            File.WriteAllText(file, c.Output);
            state["checks"]!.AsArray().Add(new JsonObject { ["round"] = round, ["name"] = c.Name, ["result"] = c.Result, ["summary"] = c.Summary, ["output"] = paths.Relative(file) });
        }

        var failed = checks.FirstOrDefault(c => c.Failed);
        if (failed is null)
        {
            var reached = checks[^1];
            return Stop(repo, paths, state, "fixed", reached.Result == "passed"
                ? "every check passed" : $"every check passed; test reach not checked: {reached.Summary}");
        }
        if (round == 2)
            return Stop(repo, paths, state, "check-failed", $"the {failed.Name} check failed after the retry: {failed.Summary}");
        if (Left(state) < 0.01)
            return Stop(repo, paths, state, "cap-hit", $"the {failed.Name} check failed and nothing is left of the ticket's ${(double)state["capUsd"]!:0.##} for the retry");
        state["round"] = 2;
        WriteInput(repo, paths, state, $"- **{failed.Name}**: {failed.Summary}\n\n````text\n{Tail(failed.Output, RetryChars)}\n````\n\nYour plan was:\n\n```json\n{Tail(output, RetryChars)}\n```\n");
        if (File.Exists(paths.Output)) File.Delete(paths.Output);
        Save(paths, state);
        return Step(repo, paths, state, "retry");
    }

    static string Tail(string text, int chars) => text.Length <= chars ? text : "…" + text[^chars..];

    static void WritePlan(StangentRepo repo, FixPaths paths, JsonObject state, JsonObject plan, List<(string File, string? Id, List<string> Reach)> resolved)
    {
        var changes = new JsonArray();
        var list = plan["changes"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
        for (var i = 0; i < list.Count && i < resolved.Count; i++)
            changes.Add(new JsonObject
            {
                ["file"] = resolved[i].File, ["point"] = resolved[i].Id, ["why"] = list[i]["why"]!.DeepClone(),
                ["reach"] = new JsonArray([.. resolved[i].Reach.Select(x => (JsonNode)x)]),
            });
        var taskPlan = new JsonObject
        {
            ["schemaVersion"] = 1, ["key"] = paths.Key, ["baseCommit"] = (string)state["baseCommit"]!,
            ["test"] = new JsonObject { ["file"] = state["testFile"]!.DeepClone(), ["name"] = state["testName"]!.DeepClone(), ["failedBefore"] = state["failedBefore"]!.DeepClone() },
            ["changes"] = changes, ["gaveUp"] = (string?)plan["gaveUp"],
        };
        var errors = SchemaRegistry.Embedded.Validate(PlanSchema, taskPlan.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_plan", "The task plan broke its contract (a stangent bug).", ExitCodes.Failed, errors);
        File.WriteAllText(paths.Plan, CanonicalJson.Serialize(taskPlan));
        state["plan"] = paths.Relative(paths.Plan);
    }

    /// <summary>Check 1 (D344): the schema; every point in the graph at the base, in its file; every quote word for word
    /// (<see cref="Quotes.InText"/>, as <c>fsd review</c>); a test name a real test. Fills each planned file's reach
    /// from the base graph, as <c>graph impact --file</c> walks it.</summary>
    static CheckResult CheckPlan(StangentRepo repo, FixPaths paths, JsonObject state, string output, JsonObject? plan,
        Dictionary<string, List<string>> reach, List<(string File, string? Id)> points)
    {
        if (plan is null) return new("plan", "failed", "no plan came back (no JSON object as the structured output)", output);
        var schema = SchemaRegistry.Embedded.ValidateDeep(OutputSchema, plan.ToJsonString());
        if (schema.Count > 0) return new("plan", "failed", $"the plan doesn't match {OutputSchema}: {schema[0]}", string.Join('\n', schema));
        var graph = GraphStore.Load(GraphStore.PathFor(repo));
        if (graph is null) return new("plan", "failed", "no code graph at the base: run `stangent graph build`", "");
        var analyzer = new ImpactAnalyzer(graph);
        var errors = new List<string>();
        var notes = new List<string>();
        if (graph.Commit is { Length: > 0 } gc && !((string)state["baseCommit"]!).StartsWith(gc, StringComparison.Ordinal) && !gc.StartsWith((string)state["baseCommit"]!, StringComparison.Ordinal))
            notes.Add($"note: the graph was built at {gc[..Math.Min(7, gc.Length)]}, the base is {((string)state["baseCommit"]!)[..7]}");
        var pulled = TicketPull.LoadOrFail(repo.Root, paths.Key);
        var ticketText = TicketText.From(pulled).Quotable;
        var bundle = (string?)state["context"] == "bundle" ? TicketBundle.LoadOrFail(repo.Root, paths.Key) : null;
        var testFile = (string)state["testFile"]!;
        var testName = (string)state["testName"]!;
        var baseCommit = (string)state["baseCommit"]!;
        var prefix = (string?)state["prefix"] ?? "";
        var changes = plan["changes"]!.AsArray().OfType<JsonObject>().ToList();
        if (changes.Count == 0) errors.Add("the plan names no change (and no gaveUp)");
        for (var i = 0; i < changes.Count; i++)
        {
            var c = changes[i];
            var at = $"changes[{i}]";
            var file = TestTarget.Normalize((string)c["file"]!);
            string? id = null;
            if (file.StartsWith('/') || file.Split('/').Contains("..") || IsStangent(file) || file.StartsWith(".git/", StringComparison.Ordinal))
                errors.Add($"{at}: `{file}` isn't a path inside the repo's source");
            else if (file == testFile)
                errors.Add($"{at}: `{file}` is the proof's test, which the fix may not change");
            else if (Git.Run(repo.Root, "cat-file", "-e", $"{baseCommit}:{prefix}{file}").Ok)
            {
                var point = (string?)c["point"];
                if (point is null) errors.Add($"{at}: `{file}` exists at the base, so the change names its point (the method or type changed)");
                else
                {
                    var found = analyzer.Find(point) is { } exact ? [exact] : analyzer.Resolve(point);
                    var inFile = found.Where(f => f.File == file).ToList();
                    if (inFile.Count == 0)
                        errors.Add(found.Count == 0 ? $"{at}: no point `{point}` in the code graph at the base"
                            : $"{at}: `{point}` is in {string.Join(", ", found.Select(f => f.File ?? "?").Distinct().Take(3))}, not in `{file}`");
                    else id = inFile[0].Id;
                }
                var starts = analyzer.InFile(file);
                // Code only: a test that reaches the change isn't the fix's to edit (fresh check, D352).
                if (starts.Any(n => n.IsTest)) errors.Add($"{at}: `{file}` holds tests; the fix may not change existing tests");
                reach[file] = [.. analyzer.Affected(starts).Where(a => !a.Node.IsTest).Select(a => a.Node.File).OfType<string>().Where(f => f != file).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            }
            else reach.TryAdd(file, []);   // a new file: no point yet, and nothing reaches it
            points.Add((file, id));

            var why = (JsonObject)c["why"]!;
            var kind = (string)why["kind"]!;
            var extra = why.Select(p => p.Key).Where(k => k != "kind" && k != (kind == "test" ? "test" : "quote") && !(kind == "fsd" && k == "section")).ToList();
            if (extra.Count > 0) errors.Add($"{at}.why: one reason only; `{kind}` takes no {string.Join(", ", extra)}");
            switch (kind)
            {
                case "ticket":
                    if ((string?)why["quote"] is not { } q) errors.Add($"{at}.why: a ticket reason quotes the ticket");
                    else if (!Quotes.InText(q, ticketText)) errors.Add($"{at}.why: \"{Short(q)}\" isn't in the ticket word for word (or is under 4 words and not a whole line)");
                    break;
                case "fsd":
                    if (bundle is null) { errors.Add($"{at}.why: an FSD reason needs the bundle, and this run has --context ticket"); break; }
                    var section = (bundle["sections"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(s => (string?)s["id"] == (string?)why["section"]);
                    if (section is null) errors.Add($"{at}.why: no FSD section `{why["section"]}` in the bundle");
                    else if ((string?)why["quote"] is not { } fq || !Quotes.InText(fq, (string)section["text"]!))
                        errors.Add($"{at}.why: the quote isn't in section `{why["section"]}` word for word");
                    break;
                case "test":
                    var name = (string?)why["test"];
                    if (name is null || !RealTest(graph, name, testName)) errors.Add($"{at}.why: `{name}` isn't a test in the code graph or the proof's test");
                    break;
            }
        }
        var summary = errors.Count == 0 ? $"{changes.Count} change(s): every point and quote checks out" : $"{errors.Count} problem(s): {errors[0]}";
        return new("plan", errors.Count == 0 ? "passed" : "failed", summary, string.Join('\n', notes.Concat(errors.Count == 0 ? changes.Select((c, i) => $"{points[i].File} · {points[i].Id ?? "(new file)"}") : errors)));
    }

    static string Short(string s) => s.Length <= 80 ? s : s[..77] + "…";

    static bool RealTest(CodeGraph graph, string name, string proofTest) =>
        BugProof.SameTest(name, proofTest) || BugProof.SameTest(proofTest, name)
        || graph.AllNodes.Any(n => n.IsTest && n.Kind == NodeKind.Method && (MethodName(n.Id) == name || n.Name == name || BugProof.SameTest(name, MethodName(n.Id))));

    /// <summary>A method's graph id as a full name: <c>Shop.Tests|M:Shop.Tests.A.B(System.Int32)</c> → <c>Shop.Tests.A.B</c>.</summary>
    static string MethodName(string id)
    {
        var at = id.IndexOf("M:", StringComparison.Ordinal);
        var name = at < 0 ? id : id[(at + 2)..];
        return name.Contains('(') ? name[..name.IndexOf('(')] : name;
    }

    /// <summary>Check 2 (D344): every edited file is planned or in a planned file's reach; the others are listed and fail it.</summary>
    static CheckResult CheckFiles(FixPaths paths, JsonObject state, JsonObject plan, Dictionary<string, List<string>> reach)
    {
        var testFile = (string)state["testFile"]!;
        var edited = Edited(paths, state);
        state["edited"] = new JsonArray([.. edited.Where(f => f != testFile).Select(f => (JsonNode)f)]);
        if (edited.Contains(testFile)) return new("files", "failed", $"the proof's test `{testFile}` was edited; the fix may not change it", string.Join('\n', edited));
        var fixes = edited.ToList();
        if (fixes.Count == 0) return new("files", "failed", "nothing was edited", "");
        var allowed = reach.Keys.Concat(reach.Values.SelectMany(v => v)).ToHashSet(StringComparer.Ordinal);
        var outside = fixes.Where(f => !allowed.Contains(f)).ToList();
        state["outsideReach"] = new JsonArray([.. outside.Select(f => (JsonNode)f)]);
        var lines = fixes.Select(f => $"{(outside.Contains(f) ? "outside" : reach.ContainsKey(f) ? "planned" : "in reach")}  {f}");
        return outside.Count == 0
            ? new("files", "passed", $"{fixes.Count} file(s) edited, each planned or in a planned file's reach", string.Join('\n', lines))
            : new("files", "failed", $"{outside.Count} edited file(s) in neither the plan nor its reach: {string.Join(", ", outside.Take(5))}", string.Join('\n', lines));
    }

    /// <summary>Check 3: the proof target's build command, in the worktree.</summary>
    static async Task<CheckResult> CheckBuild(FixPaths paths, JsonObject state, ICommandRunner runner, CancellationToken ct)
    {
        var target = Target(paths, state);
        if (target?.BuildCommand is not { } build) return new("build", "skipped", "the proof's test target has no build command; the tests check builds what it runs", "");
        var r = await runner.RunAsync(build, WorktreeRoot(paths, state), BugProof.CommandTimeout, null, ct);
        if (r.TimedOut) return new("build", "failed", "the build timed out", Tail(r.Output, 20_000));
        return r.ExitCode == 0 ? new("build", "passed", $"`{build}` built", Tail(r.Output, 4000))
            : new("build", "failed", $"`{build}` failed (exit {r.ExitCode})", Tail(r.Output, 20_000));
    }

    /// <summary>Check 4 (D105, D350): the proof's <c>--verify</c> in the worktree; only <c>passed</c> passes.</summary>
    static async Task<CheckResult> CheckTests(FixPaths paths, JsonObject state, ICommandRunner runner, CancellationToken ct)
    {
        var wt = StangentRepo.Discover(WorktreeRoot(paths, state));
        JsonObject proof;
        try { proof = await BugProof.VerifyAsync(wt, paths.Key, runner, ct); }
        catch (StangentException e) { return new("tests", "failed", $"--verify couldn't run: {e.Message}", e.Message); }
        var v = proof["verification"]!.AsObject();
        var result = (string)v["result"]!;
        var output = CanonicalJson.Serialize(v);
        // D343: "the other tests still pass" only when a full run said so; unknown is never a pass (fresh check, D352).
        if (result == "passed" && (bool?)v["othersChecked"] != true)
            return new("tests", "failed", "the test passes, but the other tests in its project weren't checked (the full run timed out or gave no results)", output);
        return result == "passed" && (bool?)v["testChanged"] != true
            ? new("tests", "passed", (string)v["reason"]!, output)
            : new("tests", "failed", $"{result}: {(string)v["reason"]!}", output);
    }

    /// <summary>Check 5 (D346 step 3, D350): the graph rebuilt in the worktree; the proof's test must be among the tests
    /// that reach a planned point. A graph that can't be rebuilt, or a plan with only new files, is not checked, never passed.</summary>
    static async Task<CheckResult> CheckReach(FixPaths paths, JsonObject state, List<(string File, string? Id)> points, GraphRebuild rebuild, CancellationToken ct)
    {
        if (points.All(p => p.Id is null)) return new("test-reach", "not-checked", "the plan changes only new files: no point to reach", "");
        CodeGraph? graph;
        try { graph = await rebuild(StangentRepo.Discover(WorktreeRoot(paths, state)), ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return new("test-reach", "not-checked", $"the graph couldn't be rebuilt in the worktree: {Short(e.Message)}", e.ToString()); }
        if (graph is null) return new("test-reach", "not-checked", "the graph couldn't be rebuilt in the worktree", "");
        var analyzer = new ImpactAnalyzer(graph);
        var testFile = (string)state["testFile"]!;
        var testName = (string)state["testName"]!;
        var method = testName.Split('.')[^1];
        var lines = new List<string>();
        foreach (var (file, id) in points.Where(p => p.Id is not null))
        {
            var starts = analyzer.Find(id!) is { } n ? [n] : analyzer.InFile(file).ToList();
            var tests = analyzer.Affected(starts).Where(a => a.Node.IsTest).Select(a => a.Node).ToList();
            var hit = tests.FirstOrDefault(t => t.File == testFile && (t.Name.Split('.')[^1] == method || MethodName(t.Id).EndsWith("." + method, StringComparison.Ordinal)));
            lines.Add($"{id}: {tests.Count} test(s){(hit is null ? "" : $", the proof's test among them ({hit.Id})")}");
            if (hit is not null) return new("test-reach", "passed", $"the proof's test reaches the planned point {analyzer.Find(id!)?.Name ?? id}", string.Join('\n', lines));
        }
        return new("test-reach", "failed", "the proof's test reaches none of the planned points in the rebuilt graph: the plan may name the wrong place, or the test passes for another reason", string.Join('\n', lines));
    }

    // ---- report ----

    /// <summary><c>report.md</c> and <c>agent-run.json</c> (D349): why it stopped, the plan, each check, the last failed
    /// check's output, the branch. Returns the agent-run record.</summary>
    public static JsonObject Report(StangentRepo repo, string key)
    {
        var paths = new FixPaths(repo.Root, key);
        var state = LoadOrFail(paths);
        var calls = state["calls"]!.AsArray().OfType<JsonObject>().ToList();
        var steps = new JsonArray();
        // The proof's turns aren't kept on its record; its cost is, and counts toward the ticket's cap (D348).
        if ((double?)state["proofCostUsd"] is > 0)
            steps.Add(new JsonObject { ["name"] = "proof", ["turns"] = null, ["costUsd"] = (double)state["proofCostUsd"]! });
        foreach (var c in calls)
            steps.Add(new JsonObject
            {
                ["name"] = "fixer", ["call"] = (int)c["call"]!, ["turns"] = (int)c["turns"]!, ["costUsd"] = (double)c["costUsd"]!,
                ["inputTokens"] = (long)c["inputTokens"]!, ["outputTokens"] = (long)c["outputTokens"]!, ["ended"] = (string?)c["ended"],
            });
        var run = new JsonObject
        {
            ["schemaVersion"] = 1, ["key"] = paths.Key, ["branch"] = paths.Branch, ["base"] = (string?)state["base"] ?? "HEAD",
            ["baseCommit"] = (string)state["baseCommit"]!, ["worktree"] = (string?)state["worktree"], ["context"] = (string?)state["context"],
            ["model"] = calls.Select(c => (string?)c["model"]).LastOrDefault(m => m is not null),
            ["effort"] = calls.Select(c => (string?)c["effort"]).LastOrDefault(m => m is not null),
            ["steps"] = steps,
            ["checks"] = new JsonArray([.. state["checks"]!.AsArray().Select(c => c!.DeepClone())]),
            ["stop"] = state["stop"]!.DeepClone(),
            ["plan"] = (string?)state["plan"],
            ["outsideReach"] = (state["outsideReach"] as JsonArray)?.DeepClone() ?? new JsonArray(),
            ["edited"] = (state["edited"] as JsonArray)?.DeepClone() ?? new JsonArray(),
            ["commits"] = state["commits"]!.DeepClone(),
            ["costUsd"] = Math.Round(Spent(state), 6), ["capUsd"] = (double)state["capUsd"]!,
        };
        var errors = SchemaRegistry.Embedded.Validate(RunSchema, run.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_report", "The agent-run record broke its contract (a stangent bug).", ExitCodes.Failed, errors);
        File.WriteAllText(paths.AgentRun, CanonicalJson.Serialize(run));
        File.WriteAllText(paths.Report, Markdown(repo, paths, run));
        return run;
    }

    static string Markdown(StangentRepo repo, FixPaths paths, JsonObject run)
    {
        var stop = (string)run["stop"]!["reason"]!;
        var detail = (string)run["stop"]!["detail"]!;
        var sb = new StringBuilder();
        sb.Append(stop == "fixed" ? $"# {paths.Key} · fixed\n\n{detail}.\n\n" : $"# {paths.Key} · stopped: {StopWords(stop)}\n\n{detail}.\n\n");
        var edited = run["edited"]!.AsArray().Count;
        if ((string)run["baseCommit"]! is { Length: > 0 } baseCommit)
            sb.Append($"- **Branch:** `{run["branch"]}` from `{baseCommit[..7]}`{(edited > 0 || run["commits"]!.AsArray().Count > 0 ? "" : " (no edits)")}; never pushed, no pull request (D347).\n");
        if ((string?)run["worktree"] is { } wt) sb.Append($"- **Worktree:** `{wt}`\n");
        if ((string?)run["context"] is { } ctx) sb.Append($"- **Given:** {(ctx == "bundle" ? "the ticket, the code, the failing test and the ticket bundle" : "the ticket, the code and the failing test")} (`--context {ctx}`)\n");
        if ((string?)run["model"] is { } model) sb.Append($"- **Model:** {model}{((string?)run["effort"] is { } e ? $", {e} effort" : "")}\n");
        var proof = run["steps"]!.AsArray().OfType<JsonObject>().Where(s => (string)s["name"]! == "proof").Sum(s => (double)s["costUsd"]!);
        sb.Append($"- **Cost:** ${(double)run["costUsd"]!:0.00} of the ticket's ${(double)run["capUsd"]!:0.##} (the proof ${proof:0.00}); {run["steps"]!.AsArray().Count(s => (string)s!["name"]! == "fixer")} fixer call(s)\n\n");

        if (PageRun.LoadJson(paths.Plan) is JsonObject plan)
        {
            sb.Append("## Plan\n\n");
            var changes = plan["changes"]!.AsArray().OfType<JsonObject>().ToList();
            if (changes.Count == 0) sb.Append($"No edits: {(string?)plan["gaveUp"]}\n");
            foreach (var c in changes)
            {
                var why = c["why"]!;
                var reason = (string)why["kind"]! switch
                {
                    "ticket" => $"ticket \"{why["quote"]}\"",
                    "fsd" => $"FSD {why["section"]} \"{why["quote"]}\"",
                    _ => $"failing test `{why["test"]}`",
                };
                var reach = c["reach"]!.AsArray().Select(x => (string)x!).ToList();
                sb.Append($"- `{c["file"]}`{((string?)c["point"] is { } p ? $" · `{p}`" : " (new)")} · why: {reason} · reaches: {(reach.Count == 0 ? "nothing else" : string.Join(", ", reach.Take(8).Select(x => $"`{x}`")) + (reach.Count > 8 ? $" and {reach.Count - 8} more" : ""))}\n");
            }
            sb.Append('\n');
        }
        var checks = run["checks"]!.AsArray().OfType<JsonObject>().ToList();
        if (checks.Count > 0)
        {
            sb.Append("## Checks\n\n| round | check | result | what it found |\n|---|---|---|---|\n");
            foreach (var c in checks)
                sb.Append($"| {c["round"]} | {c["name"]} | {Mark((string)c["result"]!)} | {((string)c["summary"]!).Replace("|", "\\|").Replace("\n", " ")} |\n");
            sb.Append('\n');
            if (checks.LastOrDefault(c => (string)c["result"]! == "failed") is { } last && (string?)last["output"] is { } file
                && File.Exists(Path.Combine(repo.Root, file)))
                sb.Append($"## Last failed check: {last["name"]} (round {last["round"]})\n\n````text\n{Tail(File.ReadAllText(Path.Combine(repo.Root, file)).TrimEnd('\n'), 6000)}\n````\n\n");
        }
        var outside = run["outsideReach"]!.AsArray().Select(x => (string)x!).ToList();
        if (edited > 0)
        {
            sb.Append("## Edited files\n\n");
            foreach (var f in run["edited"]!.AsArray().Select(x => (string)x!))
                sb.Append($"- `{f}`{(outside.Contains(f) ? " · **outside the plan's reach**" : "")}\n");
            sb.Append('\n');
        }
        sb.Append(stop switch
        {
            "fixed" => $"Next: a person reads the diff (`git diff {((string)run["baseCommit"]!)[..Math.Min(7, ((string)run["baseCommit"]!).Length)]}..{run["branch"]}`), then pushes and opens the pull request; stangent doesn't (D18, D347).\n",
            "no-failing-test" => "Next: the ticket goes to the developer with no fix tried (D346).\n",
            "in-progress" => "Next: the run isn't finished; `stangent fix check` records the fixer's call.\n",
            _ => "Next: the ticket goes back to a person, with the branch as it is (D343).\n",
        });
        return sb.ToString();
    }

    static string StopWords(string reason) => reason switch
    {
        "no-failing-test" => "no failing test",
        "gave-up" => "the fixer found no fix it could make here",
        "cap-hit" => "cap hit",
        "check-failed" => "a check failed after the retry",
        "in-progress" => "not finished",
        _ => reason,
    };

    static string Mark(string result) => result switch { "passed" => "✅ passed", "failed" => "❌ failed", "not-checked" => "⚪ not checked", _ => "➖ skipped" };
}

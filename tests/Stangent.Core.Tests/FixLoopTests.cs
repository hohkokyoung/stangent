using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Graph;
using Stangent.Spec.Pages;
using Stangent.Tickets;
using Stangent.Tickets.Fix;
using Stangent.Tickets.Proof;
using Stangent.Tickets.Secrets;

namespace Stangent.Core.Tests;

/// <summary>
/// Phase 4's minimal fix loop (D343–D350) on <c>tests/fixtures/fix-shop</c>, a small .NET repo whose bug has a failing
/// test, in a real git repo with a real worktree. A fake runner stands in for <c>dotnet build</c> and <c>dotnet test</c>
/// with canned reports, the fixer's edits and plan are made by the test, and the rebuilt graph is canned: no LLM, no
/// test framework run, nothing paid.
/// </summary>
public class FixLoopTests
{
    const string Key = "SHOP-1";
    const string TestFile = "Shop.Tests/OrderLimitTests.cs";
    const string TestName = "Shop.Tests.OrderLimitTests.Orders_over_100_wait_for_approval";
    const string Place = "Shop.Core|M:Shop.Core.OrderService.Place(System.Decimal)";
    const string Service = "Shop.Core/OrderService.cs";
    const string Expected = "Orders over 100 wait for approval until someone approves them.";
    const string Description = """
        ### Current Behavior

        An order of 150 is placed without approval.

        ### Expected Behavior

        Orders over 100 wait for approval until someone approves them.

        ### Steps To Reproduce

        Call `OrderService.Place` with 150.
        """;

    static string Fixture => Path.Combine(RepoRoot.Find(), "tests", "fixtures", "fix-shop");

    /// <summary>The fixture in its own git repo, the ticket pulled and bundled, and its bug proof reproduced.</summary>
    sealed class Rig : IDisposable
    {
        public TempRepo Temp { get; }
        public string Root => Temp.Root;
        public StangentRepo Repo { get; private set; }
        public Runner Runner { get; } = new();
        public string Base { get; }
        public FixPaths Paths => new(Root, Key);
        public string Wt => Paths.Worktree;

        public Rig(string extra = "", bool trackConfig = true, bool reproduce = true)
        {
            Temp = new TempRepo($"version: 1\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n  aiPoints: false\n{extra}");
            Repo = Temp.Repo;
            foreach (var file in Directory.EnumerateFiles(Fixture, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(Fixture, file);
                if (rel.StartsWith("proof", StringComparison.Ordinal) || rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Root, rel))!);
                File.Copy(file, Path.Combine(Root, rel));
            }
            TicketFixtures.Make(Path.Combine(Root, "replay"), Key, "Bug", "Big orders skip approval", Description);
            Git("init", "-q");
            // Untracked and not ignored, as a repo that doesn't commit stangent.yaml has it (D353).
            if (trackConfig) Git("add", "-A");
            else Git("add", "-A", "--", ".", ":!stangent.yaml");
            Git("commit", "-q", "-m", "the shop");
            Base = Git("rev-parse", "HEAD").Trim();
            GraphStore.Save(Graph(Base, withProofTest: false), GraphStore.PathFor(Repo));

            var tracker = Trackers.Open(Repo, new MemorySecretStore(), TextWriter.Null, null);
            TicketPull.RunAsync(Repo, tracker, Key).GetAwaiter().GetResult();
            var ctx = TicketContext.Load(Repo);
            TicketPoints.Update(Repo, ctx, Key, [], [], "Pat Developer");
            TicketBundle.Build(Repo, ctx, Key);
            if (reproduce) Reproduce();
        }

        /// <summary>As <c>ticket prove</c> leaves it: the test in the checkout, uncommitted, and a reproduced proof.</summary>
        void Reproduce()
        {
            var step = BugProof.Prepare(Repo, TicketContext.Load(Repo), Key, null);
            Assert.Equal(StepSpec.BugReproducer, step.Agent);
            File.Copy(Path.Combine(Fixture, "proof", "OrderLimitTests.cs"), Path.Combine(Root, TestFile));
            Runner.Tests.Enqueue(Failure());
            Runner.All = TestReportTests.Trx((TestName, "Failed", "Assert.True() Failure\nExpected: True\nActual:   False", ""));
            File.WriteAllText(Path.Combine(Root, ".stangent", "tickets", Key, "prove", "output.json"), new JsonObject
            {
                ["outcome"] = "test-written", ["target"] = "dotnet:Shop.Tests/Shop.Tests.csproj", ["testFile"] = TestFile, ["testName"] = TestName,
                ["assertion"] = new JsonObject { ["line"] = 12, ["text"] = "Assert.True(order.NeedsApproval, \"an order of 150 was placed without approval\");" },
                ["expectedQuote"] = Expected, ["actualQuote"] = "An order of 150 is placed without approval.", ["shows"] = "Expected: True",
            }.ToJsonString());
            var claude = JsonNode.Parse("""{"type":"result","subtype":"success","num_turns":5,"rounds":5,"total_cost_usd":0.4}""");
            BugProof.CheckAsync(Repo, Key, claude, Runner).GetAwaiter().GetResult();
            Assert.Equal("reproduced", (string?)BugProof.Load(new TicketPaths(Root, Key))!["outcome"]);
            Runner.Commands.Clear();
            Runner.Cwds.Clear();
        }

        public static string Failure() => TestReportTests.Trx((TestName, "Failed", "Assert.True() Failure\nExpected: True\nActual:   False",
            $"   at Shop.Tests.OrderLimitTests.Orders_over_100_wait_for_approval() in /ci/{TestFile}:line 12"));

        public static string Pass() => TestReportTests.Trx((TestName, "Passed", null, null));

        public string Git(params string[] args)
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["GIT_AUTHOR_NAME"] = psi.Environment["GIT_COMMITTER_NAME"] = "Test";
            psi.Environment["GIT_AUTHOR_EMAIL"] = psi.Environment["GIT_COMMITTER_EMAIL"] = "test@example.invalid";
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)}: {error}");
            return output;
        }

        public string WtGit(params string[] args) => Git(["-C", Wt, .. args]);

        public JsonObject Start(string? baseRef = null, bool replace = false) => FixLoop.Start(Repo, Key, baseRef, replace);

        public JsonObject Prepare(string context = "bundle") => FixLoop.Prepare(Repo, Key, context);

        /// <summary>The fixer's edit: the comparison uses the limit itself.</summary>
        public void Fix() => Edit(Service, s => s.Replace("total > ApprovalLimit * 10", "total > ApprovalLimit"));

        public void Edit(string rel, Func<string, string> change)
        {
            var path = Path.Combine(Wt, rel);
            File.WriteAllText(path, change(File.Exists(path) ? File.ReadAllText(path) : ""));
        }

        public static JsonObject Plan(string file = Service, string? point = "OrderService.Place", JsonObject? why = null) => new()
        {
            ["changes"] = new JsonArray(Change(file, point, why)),
        };

        public static JsonObject Change(string file = Service, string? point = "OrderService.Place", JsonObject? why = null)
        {
            var c = new JsonObject { ["file"] = file, ["why"] = why ?? new JsonObject { ["kind"] = "ticket", ["quote"] = Expected } };
            if (point is not null) c["point"] = point;
            return c;
        }

        public static JsonNode Claude(string subtype = "success", int rounds = 12, double usd = 0.5) => new JsonObject
        {
            ["type"] = "result", ["subtype"] = subtype, ["num_turns"] = rounds, ["rounds"] = rounds, ["total_cost_usd"] = usd,
            ["stangent_run"] = new JsonObject { ["agent"] = "stangent-fixer", ["model"] = "claude-sonnet-5-5", ["effort"] = "medium", ["mode"] = "agentic", ["claudeVersion"] = "9.9.9" },
        };

        public Func<StangentRepo, CancellationToken, Task<CodeGraph?>> Rebuilt { get; set; } = (_, _) => Task.FromResult<CodeGraph?>(Graph("next", withProofTest: true));

        /// <summary>The fixer's call ended: its plan written where the driver writes it, then code's checks. A passing
        /// test run is queued unless the test says otherwise.</summary>
        public JsonObject Check(JsonObject? plan, JsonNode? claude = null, bool queuePass = true)
        {
            Directory.CreateDirectory(Paths.Work);
            if (plan is null) { if (File.Exists(Paths.Output)) File.Delete(Paths.Output); }
            else File.WriteAllText(Paths.Output, plan.ToJsonString());
            if (queuePass && Runner.Tests.Count == 0) { Runner.Tests.Enqueue(Pass()); Runner.All = TestReportTests.Trx((TestName, "Passed", null, null), ("Shop.Tests.ReceiptTests.A_paid_order_says_paid", "Passed", null, null)); }
            return FixLoop.CheckAsync(Repo, Key, claude ?? Claude(), Runner, (r, ct) => Rebuilt(r, ct), TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        }

        public JsonObject State => FixLoop.Load(Paths)!;

        public void Dispose()
        {
            if (Directory.Exists(Wt)) { try { Git("worktree", "remove", "--force", Wt); } catch (Xunit.Sdk.XunitException) { } }
            Temp.Dispose();
        }
    }

    /// <summary>The fixture's code as the graph sees it; with the proof's test once it's on the branch.</summary>
    static CodeGraph Graph(string commit, bool withProofTest)
    {
        var g = new CodeGraph { Commit = commit };
        var f = new ProjectFragment { ProjectId = "project:all", Fingerprint = "x" };
        const string core = "project:Shop.Core/Shop.Core.csproj", tests = "project:Shop.Tests/Shop.Tests.csproj";
        const string receipt = "Shop.Core|M:Shop.Core.Receipts.For(Shop.Core.Order)", receiptTest = "Shop.Tests|M:Shop.Tests.ReceiptTests.A_paid_order_says_paid";
        f.Nodes.AddRange([
            new Node(core, NodeKind.Project, "Shop.Core"), new Node(tests, NodeKind.Project, "Shop.Tests"),
            new Node("Shop.Core|T:Shop.Core.OrderService", NodeKind.Type, "OrderService", core, Service, 7, 16),
            new Node(Place, NodeKind.Method, "OrderService.Place", core, Service, 11, 16),
            new Node(receipt, NodeKind.Method, "Receipts.For", core, Service, 20, 20),
            new Node(receiptTest, NodeKind.Method, "ReceiptTests.A_paid_order_says_paid", tests, "Shop.Tests/ReceiptTests.cs", 9, 9, IsTest: true),
        ]);
        f.Edges.Add(new Edge(receiptTest, receipt, EdgeKind.Calls, Label.Certain));
        if (withProofTest)
        {
            const string proofTest = "Shop.Tests|M:Shop.Tests.OrderLimitTests.Orders_over_100_wait_for_approval";
            f.Nodes.Add(new Node(proofTest, NodeKind.Method, "OrderLimitTests.Orders_over_100_wait_for_approval", tests, TestFile, 10, 14, IsTest: true));
            f.Edges.Add(new Edge(proofTest, Place, EdgeKind.Calls, Label.Certain));
        }
        g.Fragments[f.ProjectId] = f;
        return g;
    }

    // ---- start ----

    [Fact]
    public void Start_makes_the_branch_and_worktree_and_commits_the_proofs_test_first()
    {
        using var rig = new Rig();
        var started = rig.Start();
        Assert.Equal("stangent/fix-SHOP-1", (string?)started["branch"]);
        Assert.True(File.Exists(Path.Combine(rig.Wt, TestFile)));
        Assert.Equal("test: reproduce SHOP-1", rig.WtGit("log", "-1", "--format=%s").Trim());
        Assert.Equal(rig.Base, rig.WtGit("rev-parse", "HEAD~1").Trim());
        Assert.Equal("stangent/fix-SHOP-1", rig.WtGit("branch", "--show-current").Trim());
        // The developer's checkout is untouched: still on its branch, the test still uncommitted there.
        Assert.Equal(rig.Base, rig.Git("rev-parse", "HEAD").Trim());
        Assert.Contains("?? " + TestFile, rig.Git("status", "--porcelain", "--untracked-files=all"));
        Assert.DoesNotContain(".stangent", rig.Git("status", "--porcelain"));   // .stangent/fix/ is git-ignored (init's lines)
        // The worktree's copy of the proof builds the worktree, not the checkout.
        Assert.True(File.Exists(BugProof.File(new TicketPaths(rig.Wt, Key))));
        Assert.Equal(0.4, (double)rig.State["proofCostUsd"]!);
        Assert.Equal("in-progress", (string?)rig.State["stop"]!["reason"]);
    }

    [Fact]
    public void Start_refuses_with_no_reproduced_proof_and_the_report_says_no_failing_test()
    {
        using var rig = new Rig(reproduce: false);
        var e = Assert.Throws<StangentException>(() => rig.Start());
        Assert.Equal("no_failing_test", e.Code);
        Assert.False(Directory.Exists(rig.Wt));
        var run = FixLoop.Report(rig.Repo, Key);
        Assert.Equal("no-failing-test", (string?)run["stop"]!["reason"]);
        Assert.Contains("stopped: no failing test", File.ReadAllText(rig.Paths.Report));
    }

    [Fact]
    public void Start_refuses_a_dirty_base_and_an_existing_run_unless_replaced()
    {
        using var rig = new Rig();
        File.AppendAllText(Path.Combine(rig.Root, Service), "// a developer's edit\n");
        var dirty = Assert.Throws<StangentException>(() => rig.Start());
        Assert.Equal("dirty_base", dirty.Code);
        Assert.Contains(Service, dirty.Message);
        rig.Git("checkout", "--", Service);

        rig.Start();
        Assert.Equal("fix_exists", Assert.Throws<StangentException>(() => rig.Start()).Code);
        rig.Edit(Service, s => s + "// left from the first run\n");
        rig.Start(replace: true);
        Assert.DoesNotContain("left from the first run", File.ReadAllText(Path.Combine(rig.Wt, Service)));
        Assert.Equal("test: reproduce SHOP-1", rig.WtGit("log", "-1", "--format=%s").Trim());
    }

    [Fact]
    public void Start_commits_a_proof_test_under_an_ignored_folder()
    {
        using var rig = new Rig();
        File.AppendAllText(Path.Combine(rig.Root, ".git", "info", "exclude"), "OrderLimitTests.cs\n");   // as Sonarr ignores e2e/ (D354)
        rig.Start();
        Assert.Equal("test: reproduce SHOP-1", rig.WtGit("log", "-1", "--format=%s").Trim());
        Assert.Contains(TestFile, rig.WtGit("show", "--name-only", "--format=", "HEAD"));
    }

    [Fact]
    public void Start_takes_a_base_and_copies_an_untracked_stangent_yaml_that_the_fixer_may_not_edit()
    {
        using var rig = new Rig(trackConfig: false);
        File.WriteAllText(Path.Combine(rig.Root, "NOTES.md"), "later\n");
        rig.Git("add", "NOTES.md");
        rig.Git("commit", "-q", "-m", "notes");
        File.WriteAllText(Path.Combine(rig.Root, "scratch.txt"), "untracked\n");
        var started = rig.Start(baseRef: rig.Base);
        Assert.False(File.Exists(Path.Combine(rig.Wt, "NOTES.md")));
        Assert.False(File.Exists(Path.Combine(rig.Wt, "scratch.txt")));
        var warnings = started["warnings"]!.AsArray().Select(w => (string)w!).ToList();
        Assert.Contains(warnings, w => w.Contains("scratch.txt") && !w.Contains("stangent.yaml"));
        Assert.True(File.Exists(Path.Combine(rig.Wt, "stangent.yaml")));
        Assert.Equal(["stangent.yaml"], rig.State["copied"]!.AsArray().Select(c => (string)c!));
        var limits = rig.Prepare()["next"]!["limits"]!;
        Assert.Contains($"Edit({ClaudeRules.Absolute(rig.Wt)}/stangent.yaml)", limits["disallowedTools"]!.AsArray().Select(t => (string)t!));
    }

    [Fact]
    public void A_local_file_such_as_an_sdk_pin_is_no_dirt_goes_into_the_worktree_and_out_of_every_check()
    {
        using var rig = new Rig("fix:\n  localFiles: [README.md]\n");
        File.WriteAllText(Path.Combine(rig.Root, "README.md"), "pinned for this machine\n");
        rig.Start();
        Assert.Equal("pinned for this machine\n", File.ReadAllText(Path.Combine(rig.Wt, "README.md")));
        rig.Prepare();
        rig.Fix();
        Assert.Equal("fixed", (string?)rig.Check(Rig.Plan())["status"]);
        Assert.Equal([Service], rig.WtGit("diff", "--name-only", "HEAD~1", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void A_local_folder_such_as_the_apps_build_scripts_is_copied_and_kept_out_of_every_check()
    {
        using var rig = new Rig("fix:\n  localFiles: [.app/]\n");
        Directory.CreateDirectory(Path.Combine(rig.Root, ".app", "bin"));
        File.WriteAllText(Path.Combine(rig.Root, ".app", "bin", "build.sh"), "echo build\n");   // untracked, as Sonarr's .stangent-app/ (D356)
        var started = rig.Start();
        Assert.Equal("echo build\n", File.ReadAllText(Path.Combine(rig.Wt, ".app", "bin", "build.sh")));
        Assert.DoesNotContain(started["warnings"]!.AsArray().Select(w => (string)w!), w => w.Contains(".app"));
        var limits = rig.Prepare()["next"]!["limits"]!;
        Assert.Contains($"Edit({ClaudeRules.Absolute(rig.Wt)}/.app/**)", limits["disallowedTools"]!.AsArray().Select(t => (string)t!));
        rig.Fix();
        Assert.Equal("fixed", (string?)rig.Check(Rig.Plan())["status"]);
        Assert.Equal([Service], rig.WtGit("diff", "--name-only", "HEAD~1", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // ---- prepare ----

    [Fact]
    public void Prepare_gives_the_bundle_or_only_the_ticket_and_limits_inside_the_worktree()
    {
        using var rig = new Rig();
        rig.Start();
        var step = rig.Prepare("ticket");
        Assert.Equal("run", (string?)step["next"]!["action"]);
        Assert.Equal(FixLoop.Agent, (string?)step["next"]!["agent"]);
        var input = File.ReadAllText(rig.Paths.Input);
        Assert.Contains("Orders over 100 wait for approval", input);
        Assert.Contains("public void Orders_over_100_wait_for_approval()", input);   // the test's source
        Assert.Contains("Expected: True", input);                                       // its failure
        Assert.Contains("dotnet build 'Shop.Tests/Shop.Tests.csproj'", input);
        Assert.DoesNotContain("# The ticket bundle", input);

        var limits = step["next"]!["limits"]!;
        var wt = ClaudeRules.Absolute(rig.Wt);
        var allowed = limits["allowedTools"]!.AsArray().Select(t => (string)t!).ToList();
        var denied = limits["disallowedTools"]!.AsArray().Select(t => (string)t!).ToList();
        Assert.Contains($"Edit({wt}/**)", allowed);
        Assert.Contains(allowed, a => a.StartsWith("Bash(dotnet build 'Shop.Tests/Shop.Tests.csproj'", StringComparison.Ordinal));
        Assert.DoesNotContain(allowed, a => a.Contains(ClaudeRules.Absolute(rig.Root) + "/**", StringComparison.Ordinal));   // never the checkout
        Assert.Contains($"Edit({wt}/{TestFile})", denied);
        Assert.Contains($"Read({wt}/.stangent/**)", denied);
        Assert.Contains($"Edit({wt}/.stangent/**)", denied);
        Assert.Equal(rig.Wt, (string?)limits["cwd"]);
        Assert.Equal(30, (int)limits["maxTurns"]!);
        Assert.Equal(1.0, (double)limits["maxBudgetUsd"]!);

        Assert.Equal("already_run", Assert.Throws<StangentException>(() => { rig.Check(Rig.Plan(), queuePass: false); rig.Prepare(); }).Code);
    }

    [Fact]
    public void With_the_bundle_the_input_has_it()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare("bundle");
        Assert.Contains("# The ticket bundle", File.ReadAllText(rig.Paths.Input));
    }

    [Fact]
    public void A_call_never_gets_more_than_whats_left_of_the_tickets_cap()
    {
        using var rig = new Rig("fix:\n  ticketUsd: 1.2\n");
        rig.Start();
        Assert.Equal(0.8, (double)rig.Prepare()["next"]!["limits"]!["maxBudgetUsd"]!);   // $1.20 less the proof's $0.40

        using var spent = new Rig("fix:\n  ticketUsd: 0.4\n");
        spent.Start();
        var step = spent.Prepare();
        Assert.Equal("none", (string?)step["next"]!["action"]);
        Assert.Equal("cap-hit", (string?)step["stop"]!["reason"]);
    }

    // ---- check ----

    [Fact]
    public void A_fix_is_committed_when_the_repo_git_ignores_stangent_yaml()
    {
        using var rig = new Rig(trackConfig: false);
        File.AppendAllText(Path.Combine(rig.Root, ".git", "info", "exclude"), "/stangent.yaml\n");   // D355
        rig.Start();
        rig.Prepare();
        rig.Fix();
        Assert.Equal("fixed", (string?)rig.Check(Rig.Plan())["status"]);
        Assert.Equal("fix: SHOP-1", rig.WtGit("log", "-1", "--format=%s").Trim());
        Assert.Equal([Service], rig.WtGit("diff", "--name-only", "HEAD~1", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void A_good_fix_passes_every_check_and_is_committed_on_the_branch()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Fix();
        var result = rig.Check(Rig.Plan());
        Assert.Equal("fixed", (string?)result["status"]);
        Assert.Equal("none", (string?)result["next"]!["action"]);
        var checks = rig.State["checks"]!.AsArray().Select(c => ((string)c!["name"]!, (string)c["result"]!)).ToList();
        Assert.Equal([("plan", "passed"), ("files", "passed"), ("build", "passed"), ("tests", "passed"), ("test-reach", "passed")], checks);
        Assert.Equal("fix: SHOP-1", rig.WtGit("log", "-1", "--format=%s").Trim());
        Assert.Equal([Service], rig.WtGit("diff", "--name-only", "HEAD~1", "HEAD").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        // Every command ran in the worktree, never the checkout.
        Assert.All(rig.Runner.Cwds, c => Assert.StartsWith(rig.Wt, c));
        Assert.Contains(rig.Runner.Commands, c => c.StartsWith("dotnet test", StringComparison.Ordinal) && c.Contains("--filter"));

        var plan = JsonNode.Parse(File.ReadAllText(rig.Paths.Plan))!;
        Assert.Equal(Place, (string?)plan["changes"]![0]!["point"]);
        Assert.DoesNotContain("Shop.Tests/ReceiptTests.cs", plan["changes"]![0]!["reach"]!.AsArray().Select(r => (string)r!));   // filled by code, tests left out
        Assert.Equal(rig.Base, (string?)plan["baseCommit"]);

        Assert.Equal([(null, "in-progress"), ("in-progress", "fixed")],
            Stangent.Core.State.StateChanges.History(rig.Root, Stangent.Core.State.StateKinds.FixRun, Key).Select(m => (m.From, m.To)));
        rig.Start(replace: true);   // a person starts it again
        Assert.Equal(("fixed", "in-progress"), Stangent.Core.State.StateChanges.History(rig.Root, Stangent.Core.State.StateKinds.FixRun, Key).Select(m => (m.From, m.To)).Last());
        rig.Prepare();
        rig.Fix();
        rig.Check(Rig.Plan());
        var run = FixLoop.Report(rig.Repo, Key);
        Assert.Equal("fixed", (string?)run["stop"]!["reason"]);
        Assert.Equal("claude-sonnet-5-5", (string?)run["model"]);
        Assert.Equal("medium", (string?)run["effort"]);
        Assert.Equal(0.9, (double)run["costUsd"]!, 6);   // the proof's 0.4 and the call's 0.5
        Assert.Equal(["proof", "fixer"], run["steps"]!.AsArray().Select(s => (string)s!["name"]!));
        var report = File.ReadAllText(rig.Paths.Report);
        Assert.Contains("# SHOP-1 · fixed", report);
        Assert.Contains("`Shop.Core/OrderService.cs` · `" + Place + "` · why: ticket \"" + Expected + "\"", report);
        Assert.Contains("never pushed", report);
        Assert.DoesNotContain("push", string.Join('\n', rig.Runner.Commands));
    }

    [Fact]
    public void A_made_up_quote_or_point_fails_the_plan_and_the_retry_is_told_why()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Fix();
        var plan = new JsonObject
        {
            ["changes"] = new JsonArray(
                Rig.Change(why: new JsonObject { ["kind"] = "ticket", ["quote"] = "Orders over 100 must be refused outright." }),
                Rig.Change(point: "OrderService.Cancel")),
        };
        var retry = rig.Check(plan, queuePass: false);
        Assert.Equal("retry", (string?)retry["status"]);
        Assert.Equal(2, (int)retry["round"]!);
        Assert.Equal("run", (string?)retry["next"]!["action"]);
        Assert.Equal(1.0, (double)retry["next"]!["limits"]!["maxBudgetUsd"]!);   // its own $1 again: $2.10 of the ticket's $3 is left
        var input = File.ReadAllText(rig.Paths.Input);
        Assert.Contains("# Your previous attempt (retry)", input);
        Assert.Contains("isn't in the ticket word for word", input);
        Assert.Contains("no point `OrderService.Cancel`", input);
        Assert.Empty(rig.Runner.Commands);   // nothing built or run after the plan failed

        rig.Check(plan, queuePass: false);
        Assert.Equal("check-failed", (string?)rig.State["stop"]!["reason"]);
        Assert.Equal("wip: SHOP-1 (stopped: check-failed)", rig.WtGit("log", "-1", "--format=%s").Trim());   // the edits stay on the branch
        FixLoop.Report(rig.Repo, Key);
        var report = File.ReadAllText(rig.Paths.Report);
        Assert.Contains("stopped: a check failed after the retry", report);
        Assert.Contains("## Last failed check: plan (round 2)", report);
    }

    [Fact]
    public void An_edit_outside_the_plans_reach_is_listed_and_fails_the_files_check()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Fix();
        rig.Edit("README.md", s => s + "\nFixed SHOP-1.\n");
        rig.Check(Rig.Plan(), queuePass: false);
        var files = rig.State["checks"]!.AsArray().Last()!;
        Assert.Equal(("files", "failed"), ((string)files["name"]!, (string)files["result"]!));
        Assert.Contains("README.md", (string)files["summary"]!);
        Assert.Equal(["README.md"], rig.State["outsideReach"]!.AsArray().Select(x => (string)x!));
    }

    [Fact]
    public void An_edited_proof_test_fails_the_files_check()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Edit(TestFile, s => s.Replace("Assert.True(order.NeedsApproval", "Assert.False(order.NeedsApproval"));
        rig.Check(Rig.Plan(), queuePass: false);
        var files = rig.State["checks"]!.AsArray().Last()!;
        Assert.Equal("failed", (string?)files["result"]);
        Assert.Contains("the proof's test", (string)files["summary"]!);
    }

    [Fact]
    public void A_test_that_still_fails_fails_the_tests_check()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Edit(Service, s => s.Replace("// The bug", "// Still the bug"));
        rig.Runner.Tests.Enqueue(Rig.Failure());
        rig.Runner.All = TestReportTests.Trx((TestName, "Failed", "Assert.True() Failure\nExpected: True\nActual:   False", ""));
        var result = rig.Check(Rig.Plan());
        Assert.Equal("retry", (string?)result["status"]);
        var tests = rig.State["checks"]!.AsArray().Last()!;
        Assert.Equal(("tests", "failed"), ((string)tests["name"]!, (string)tests["result"]!));
        Assert.StartsWith("same-failure", (string)tests["summary"]!);
    }

    [Fact]
    public void A_build_that_fails_stops_before_the_tests()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Fix();
        rig.Runner.BuildFails = "OrderService.cs(14,1): error CS1002: ; expected";
        rig.Check(Rig.Plan(), queuePass: false);
        var build = rig.State["checks"]!.AsArray().Last()!;
        Assert.Equal(("build", "failed"), ((string)build["name"]!, (string)build["result"]!));
        Assert.DoesNotContain(rig.Runner.Commands, c => c.StartsWith("dotnet test", StringComparison.Ordinal));
    }

    [Fact]
    public void A_graph_that_cant_be_rebuilt_leaves_test_reach_not_checked_never_passed()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Fix();
        rig.Rebuilt = (_, _) => throw new StangentException("dotnet_missing", "no dotnet here");
        var result = rig.Check(Rig.Plan());
        Assert.Equal("fixed", (string?)result["status"]);
        var reach = rig.State["checks"]!.AsArray().Last()!;
        Assert.Equal(("test-reach", "not-checked"), ((string)reach["name"]!, (string)reach["result"]!));
        Assert.Contains("test reach not checked", (string)rig.State["stop"]!["detail"]!);
    }

    [Fact]
    public void A_test_that_reaches_no_planned_point_fails_test_reach()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Fix();
        rig.Rebuilt = (_, _) => Task.FromResult<CodeGraph?>(Graph("next", withProofTest: false));
        rig.Check(Rig.Plan());
        var reach = rig.State["checks"]!.AsArray().Last()!;
        Assert.Equal(("test-reach", "failed"), ((string)reach["name"]!, (string)reach["result"]!));
    }

    [Fact]
    public void A_call_that_hits_its_cap_stops_the_ticket_and_keeps_its_edits()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Fix();
        var result = rig.Check(null, Rig.Claude("error_max_turns", 30, 0.9), queuePass: false);
        Assert.Equal("cap-hit", (string?)result["status"]);
        Assert.Equal("wip: SHOP-1 (stopped: cap-hit)", rig.WtGit("log", "-1", "--format=%s").Trim());
        Assert.Empty(rig.State["checks"]!.AsArray());
        FixLoop.Report(rig.Repo, Key);
        Assert.Contains("stopped: cap hit", File.ReadAllText(rig.Paths.Report));
    }

    [Fact]
    public void A_fixer_that_gives_up_with_no_edits_stops_with_its_reason()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Check(new JsonObject { ["changes"] = new JsonArray(), ["gaveUp"] = "The limit comes from a setting outside this repo." }, queuePass: false);
        Assert.Equal("gave-up", (string?)rig.State["stop"]!["reason"]);
        FixLoop.Report(rig.Repo, Key);
        var report = File.ReadAllText(rig.Paths.Report);
        Assert.Contains("No edits: The limit comes from a setting outside this repo.", report);
        Assert.Equal("test: reproduce SHOP-1", rig.WtGit("log", "-1", "--format=%s").Trim());
    }

    [Fact]
    public void An_fsd_reason_needs_the_bundle_and_a_test_reason_a_real_test()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare("ticket");
        rig.Fix();
        var plan = new JsonObject
        {
            ["changes"] = new JsonArray(
                Rig.Change(why: new JsonObject { ["kind"] = "fsd", ["section"] = "orders-1", ["quote"] = "Orders over 100 wait for approval." }),
                Rig.Change(why: new JsonObject { ["kind"] = "test", ["test"] = "Shop.Tests.Nope.Missing" }),
                Rig.Change(why: new JsonObject { ["kind"] = "test", ["test"] = TestName, ["quote"] = "two reasons" })),
        };
        rig.Check(plan, queuePass: false);
        var output = File.ReadAllText(Path.Combine(rig.Root, (string)rig.State["checks"]!.AsArray().Last()!["output"]!));
        Assert.Contains("an FSD reason needs the bundle", output);
        Assert.Contains("`Shop.Tests.Nope.Missing` isn't a test", output);
        Assert.Contains("one reason only", output);
        Assert.DoesNotContain("changes[2].why: `", output);   // the proof's test name itself is a real test
    }

    [Fact]
    public void An_existing_test_is_never_the_fixs_to_change()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Fix();
        rig.Edit("Shop.Tests/ReceiptTests.cs", s => s.Replace("paid 20", "paid 21"));
        // Not in the plan: outside reach, since a test reaching the change isn't reach.
        rig.Check(Rig.Plan(), queuePass: false);
        Assert.Equal(("files", "failed"), ((string)rig.State["checks"]!.AsArray().Last()!["name"]!, (string)rig.State["checks"]!.AsArray().Last()!["result"]!));
        // Planned: refused as a plan.
        var plan = new JsonObject { ["changes"] = new JsonArray(Rig.Change(), Rig.Change("Shop.Tests/ReceiptTests.cs", "ReceiptTests.A_paid_order_says_paid")) };
        rig.Check(plan, queuePass: false);
        var last = rig.State["checks"]!.AsArray().Last()!;
        Assert.Equal(("plan", "failed"), ((string)last["name"]!, (string)last["result"]!));
        Assert.Contains("holds tests", (string)last["summary"]!);
    }

    [Fact]
    public void Other_tests_that_werent_checked_fail_the_tests_check()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Fix();
        rig.Runner.Tests.Enqueue(Rig.Pass());
        rig.Runner.All = null;   // the full run left no report: nothing is known about the other tests
        rig.Check(Rig.Plan());
        var tests = rig.State["checks"]!.AsArray().Last()!;
        Assert.Equal(("tests", "failed"), ((string)tests["name"]!, (string)tests["result"]!));
        Assert.Contains("weren't checked", (string)tests["summary"]!);
    }

    [Fact]
    public void The_records_match_their_contracts()
    {
        using var rig = new Rig();
        rig.Start();
        rig.Prepare();
        rig.Fix();
        rig.Check(Rig.Plan());
        FixLoop.Report(rig.Repo, Key);
        Assert.Empty(SchemaRegistry.Embedded.Validate(FixLoop.PlanSchema, File.ReadAllText(rig.Paths.Plan)));
        Assert.Empty(SchemaRegistry.Embedded.Validate(FixLoop.RunSchema, File.ReadAllText(rig.Paths.AgentRun)));
        Assert.Empty(SchemaRegistry.Embedded.Validate(FixLoop.OutputSchema, Rig.Plan().ToJsonString()));
    }

    [Fact]
    public void The_fix_component_off_refuses_every_step()
    {
        using var rig = new Rig("components:\n  fix: false\n");
        Assert.Equal("fix_off", Assert.Throws<StangentException>(() => rig.Start()).Code);
    }

    /// <summary>Stands in for dotnet: canned reports written where the command asks, and where each command ran.</summary>
    sealed class Runner : ICommandRunner
    {
        public List<string> Commands { get; } = [];
        public List<string> Cwds { get; } = [];
        public Queue<string> Tests { get; } = new();
        public string? All { get; set; }
        public string? BuildFails { get; set; }

        public Task<CommandResult> RunAsync(string command, string cwd, TimeSpan timeout, IReadOnlyDictionary<string, string>? env = null, CancellationToken ct = default)
        {
            Commands.Add(command);
            Cwds.Add(cwd);
            if (command.Contains("--list-tests", StringComparison.Ordinal))
                return Task.FromResult(new CommandResult(0, "The following Tests are available:\n    Orders_over_100_wait_for_approval\n", false, 5));
            if (command.StartsWith("dotnet build", StringComparison.Ordinal))
                return Task.FromResult(BuildFails is null ? new CommandResult(0, "Build succeeded.", false, 5) : new CommandResult(1, BuildFails, false, 5));
            if (command.StartsWith("dotnet test", StringComparison.Ordinal))
            {
                var report = Regex.Match(command, "LogFileName=([^']+)").Groups[1].Value;
                var content = command.Contains("FullyQualifiedName~", StringComparison.Ordinal) ? Tests.Dequeue() : All;
                if (content is not null) File.WriteAllText(report, content);
                return Task.FromResult(new CommandResult(1, "", false, 10));
            }
            return Task.FromResult(new CommandResult(0, "", false, 1));
        }

        public IRunningProcess Start(string command, string cwd, IReadOnlyDictionary<string, string>? env = null) => throw new NotSupportedException();
    }
}

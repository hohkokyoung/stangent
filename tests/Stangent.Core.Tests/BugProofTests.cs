using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Spec.Pages;
using Stangent.Tickets;
using Stangent.Tickets.Proof;
using Stangent.Tickets.Secrets;

namespace Stangent.Core.Tests;

/// <summary>
/// Phase 3 step 5: the bug proof (D75, D76) on the shop app. A fake runner stands in for <c>dotnet test</c> and
/// Playwright with canned reports, and the reproducer's outputs are canned: no LLM, no network, no test framework.
/// </summary>
public class BugProofTests
{
    const string Key = "SHOP-1";
    const string TestFile = "Tests/OrderTests.cs";
    const string TestName = "Shop.Tests.OrderTests.Rejects_orders_over_100";
    const string Description = """
        ### Current Behavior

        An order of 150 is placed without approval.

        ### Expected Behavior

        Orders over 100 are rejected until someone approves them.

        ### Steps To Reproduce

        Call `OrderService.Place` with 150.
        """;

    static readonly string TestCode = """
        using Xunit;
        namespace Shop.Tests;
        public class OrderTests
        {
            [Fact]
            public void Rejects_orders_over_100()
            {
                Assert.Throws<ArgumentException>(() => new OrderService().Place(150));
            }
        }
        """;

    sealed class Proof : IDisposable
    {
        public SpecApp App { get; }
        public FakeRunner Runner { get; } = new();
        public string Root => App.Root;

        public Proof(string extra = "", bool testProject = true, string type = "Bug", string description = Description, string? packageJson = null)
        {
            App = new SpecApp($"version: 1\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n{extra}");
            App.Write("Core/Shop.Core.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            if (testProject)
                App.Write("Tests/Shop.Tests.csproj", """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <ItemGroup><PackageReference Include="xunit" /><ProjectReference Include="..\Core\Shop.Core.csproj" /></ItemGroup>
                    </Project>
                    """);
            if (packageJson is not null) App.Write("package.json", packageJson);
            TicketFixtures.Make(Path.Combine(Root, "replay"), Key, type, "Big orders skip approval", description);
            var tracker = Trackers.Open(App.Repo, new MemorySecretStore(), TextWriter.Null, null);
            TicketPull.RunAsync(App.Repo, tracker, Key).GetAwaiter().GetResult();
            var ctx = TicketContext.Load(App.Repo);
            TicketPoints.Update(App.Repo, ctx, Key, [], [], "Pat Developer");
            TicketBundle.Build(App.Repo, ctx, Key);
        }

        public RunStep Prepare(string? hint = null) => BugProof.Prepare(App.Repo, TicketContext.Load(App.Repo), Key, hint);

        public static JsonNode Ok(int turns = 3, double usd = 0.1) =>
            JsonNode.Parse($$"""{"type":"result","subtype":"success","num_turns":{{turns}},"total_cost_usd":{{usd.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""")!;

        public RunStep Check(string output, JsonNode? claude = null)
        {
            File.WriteAllText(Path.Combine(Root, ".stangent", "tickets", Key, "prove", "output.json"), output);
            return BugProof.CheckAsync(App.Repo, Key, claude ?? Ok(), Runner).GetAwaiter().GetResult();
        }

        public JsonObject Record => BugProof.Load(new TicketPaths(Root, Key))!;

        public void Dispose() => App.Dispose();
    }

    static string Written(int line = 8, string text = "Assert.Throws<ArgumentException>(() => new OrderService().Place(150));",
        string quote = "Orders over 100 are rejected until someone approves them.", string file = TestFile, string target = "dotnet:Tests/Shop.Tests.csproj") =>
        new JsonObject
        {
            ["outcome"] = "test-written", ["target"] = target, ["testFile"] = file, ["testName"] = TestName,
            ["assertion"] = new JsonObject { ["line"] = line, ["text"] = text }, ["expectedQuote"] = quote,
        }.ToJsonString();

    static string AssertFailure(int line = 8) => TestReportTests.Trx((TestName, "Failed", "Assert.Throws() Failure: No exception was thrown",
        $"   at Shop.Tests.OrderTests.Rejects_orders_over_100() in /home/ci/shop/{TestFile}:line {line}"));

    [Fact]
    public void The_input_names_the_test_project_that_covers_the_code_and_the_commands()
    {
        using var p = new Proof();
        var step = p.Prepare();
        Assert.Equal(StepSpec.BugReproducer, step.Agent);
        var input = File.ReadAllText(Path.Combine(p.Root, ".stangent", "tickets", Key, "prove", "input.md"));
        Assert.Contains("`dotnet:Tests/Shop.Tests.csproj`", input);
        Assert.Contains("dotnet build 'Tests/Shop.Tests.csproj'", input);
        Assert.Contains("Orders over 100 are rejected until someone approves them.", input);
        var limits = BugProof.DriverLimits(p.App.Repo, Key);
        var tools = limits["allowedTools"]!.AsArray().Select(t => (string)t!).ToList();
        Assert.Contains("Edit(Tests/**/*.cs)", tools);
        Assert.Contains("Bash(dotnet test 'Tests/Shop.Tests.csproj':*)", tools);
        Assert.DoesNotContain(tools, t => t.StartsWith("Edit(Core", StringComparison.Ordinal));
        Assert.Equal(15, (int)limits["maxTurns"]!);
        Assert.Equal(1.0, (double)limits["maxBudgetUsd"]!);
    }

    [Fact]
    public void A_test_that_fails_on_the_named_assertion_reproduces_the_bug_and_records_the_baseline()
    {
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        p.Runner.Tests.Enqueue(AssertFailure());
        p.Runner.All = TestReportTests.Trx((TestName, "Failed", "Assert.Throws() Failure", ""), ("Shop.Tests.Other.AlreadyBroken", "Failed", "Assert.True() Failure", ""));
        Assert.Equal("recorded", p.Check(Written()).Status);
        var proof = p.Record;
        Assert.Equal("reproduced", (string?)proof["outcome"]);
        var attempt = proof["attempts"]![0]!;
        Assert.Equal("failed-on-assertion", (string?)attempt["runs"]![0]!["result"]);
        Assert.Equal(["Shop.Tests.Other.AlreadyBroken"], attempt["baseline"]!.AsArray().Select(b => (string)b!));
        Assert.Contains(p.Runner.Commands, c => c.StartsWith("dotnet build", StringComparison.Ordinal));

        Assert.Equal("Pat Developer", (string?)BugProof.Confirm(p.App.Repo, Key, "Pat Developer")["confirmedBy"]);
        Assert.Equal("current", p.Prepare().Status); // nothing changed: no AI call
        Assert.Equal("prepared", p.Prepare("set the total to exactly 100.01").Status); // a hint is a new attempt
    }

    [Fact]
    public void A_passing_test_is_not_reproduced_without_a_retry()
    {
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        p.Runner.Tests.Enqueue(TestReportTests.Trx((TestName, "Passed", null, null)));
        Assert.Equal("recorded", p.Check(Written()).Status);
        Assert.Equal("not-reproduced", (string?)p.Record["outcome"]);
        Assert.Throws<StangentException>(() => BugProof.Confirm(p.App.Repo, Key, "Pat"));
    }

    [Fact]
    public void A_crash_in_setup_gets_one_retry_then_the_attempt_fails()
    {
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        Assert.Equal("usage", Assert.Throws<StangentException>(() =>
        {
            File.WriteAllText(Path.Combine(p.Root, ".stangent", "tickets", Key, "prove", "output.json"), Written());
            BugProof.CheckAsync(p.App.Repo, Key, null, p.Runner, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        }).Code);
        var crash = TestReportTests.Trx((TestName, "Failed", "System.NullReferenceException : Object reference not set to an instance of an object.",
            $"   at Shop.Core.OrderService..ctor() in /x/Core/OrderService.cs:line 3\n   at Shop.Tests.OrderTests.Rejects_orders_over_100() in /x/{TestFile}:line 8"));
        p.Runner.Tests.Enqueue(crash);
        var retry = p.Check(Written());
        Assert.Equal("retry", retry.Status);
        Assert.Contains(retry.Errors!, e => e.Contains("not an assertion failure"));
        p.Runner.Tests.Enqueue(crash);
        Assert.Equal("failed", p.Check(Written()).Status);
        Assert.Equal("failed", (string?)p.Record["outcome"]);
    }

    [Fact]
    public void Failing_on_another_assertion_or_not_compiling_is_rejected()
    {
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode + "\n// padding\n");
        p.Runner.Tests.Enqueue(AssertFailure(line: 11));
        Assert.Contains(p.Check(Written()).Errors!, e => e.Contains("not the named one at line 8"));

        using var q = new Proof();
        q.Prepare();
        q.App.Write(TestFile, TestCode);
        q.Runner.BuildFails = "Tests/OrderTests.cs(8,13): error CS0246: The type or namespace name 'OrderService' could not be found";
        var retry = q.Check(Written());
        Assert.Contains(retry.Errors!, e => e.Contains("compile-error") && e.Contains("CS0246"));
    }

    [Fact]
    public void A_made_up_expected_result_a_wrong_line_or_a_file_outside_the_tests_is_rejected_before_running()
    {
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        p.App.Write("Core/Sneaky.cs", "// not a test");
        var errors = p.Check(Written(quote: "Orders over 100 must always be refused by the shop")).Errors!;
        Assert.Contains(errors, e => e.Contains("isn't in the ticket word for word"));
        Assert.Empty(p.Runner.Commands);

        var wrongLine = p.Check(Written(line: 3)).Errors!;
        Assert.Equal("failed", p.Record["outcome"]?.GetValue<string>() ?? "failed");
        Assert.Contains(wrongLine, e => e.Contains("isn't the assertion"));

        using var q = new Proof();
        q.Prepare();
        q.App.Write("Core/Sneaky.cs", "// not a test");
        Assert.Contains(q.Check(Written(file: "Core/Sneaky.cs")).Errors!, e => e.Contains("isn't a test file stangent lets you write"));
    }

    [Fact]
    public void Hitting_the_cap_is_not_reproduced()
    {
        using var p = new Proof();
        p.Prepare();
        var claude = JsonNode.Parse("""{"subtype":"error_max_turns","num_turns":16,"total_cost_usd":0.62,"usage":{"input_tokens":1000,"output_tokens":100}}""");
        Assert.Equal("recorded", p.Check("", claude).Status);
        Assert.Equal("not-reproduced", (string?)p.Record["outcome"]);
        Assert.StartsWith("hit the cap", (string?)p.Record["attempts"]![0]!["reason"]);
        var over = JsonNode.Parse("""{"subtype":"success","num_turns":9,"total_cost_usd":1.40}""");
        p.Prepare("try harder");
        p.Check(Written(), over);
        Assert.StartsWith("hit the cap: $1.40", (string?)p.Record["attempts"]![1]!["reason"]);
    }

    [Fact]
    public void Not_provable_by_the_reproducer_or_by_code_when_no_test_project_covers_the_code()
    {
        using var p = new Proof();
        p.Prepare();
        const string giveUp = """{"outcome":"not-provable","reason":"the total is only shown on screen"}""";
        Assert.Equal("retry", p.Check(giveUp).Status); // code found a test project: it's a judgment, so one retry first
        Assert.Equal("recorded", p.Check(giveUp).Status);
        Assert.Equal("not-provable", (string?)p.Record["outcome"]);
        Assert.StartsWith("the reproducer's judgment", (string?)p.Record["attempts"]![0]!["reason"]);

        using var q = new Proof(testProject: false);
        Assert.Equal("recorded", q.Prepare().Status);
        Assert.Equal("not-provable", (string?)q.Record["outcome"]);
        Assert.Equal("current", q.Prepare().Status);

        using var r = new Proof(type: "Story");
        Assert.Equal("not_a_bug", Assert.Throws<StangentException>(() => r.Prepare()).Code);
    }

    [Fact]
    public async Task Verify_passes_on_the_fix_only_when_the_test_passes_and_nothing_new_fails()
    {
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        p.Runner.Tests.Enqueue(AssertFailure());
        p.Runner.All = TestReportTests.Trx(("Shop.Tests.Other.AlreadyBroken", "Failed", "x", ""));
        p.Check(Written());

        Assert.Equal("prepared", p.Prepare("a newer attempt replaces the prepared state").Status);
        p.Runner.Tests.Enqueue(TestReportTests.Trx((TestName, "Passed", null, null)));
        var ok = (await BugProof.VerifyAsync(p.App.Repo, Key, p.Runner, TestContext.Current.CancellationToken))["verification"]!;
        Assert.True((bool)ok["passed"]!);

        p.Runner.Tests.Enqueue(TestReportTests.Trx((TestName, "Passed", null, null)));
        p.Runner.All = TestReportTests.Trx(("Shop.Tests.Other.AlreadyBroken", "Failed", "x", ""), ("Shop.Tests.Billing.Charges", "Failed", "Assert.Equal() Failure", ""));
        var broke = (await BugProof.VerifyAsync(p.App.Repo, Key, p.Runner, TestContext.Current.CancellationToken))["verification"]!;
        Assert.False((bool)broke["passed"]!);
        Assert.Equal(["Shop.Tests.Billing.Charges"], broke["newFailures"]!.AsArray().Select(x => (string)x!));
    }

    [Fact]
    public void A_Playwright_test_must_fail_the_same_way_three_times()
    {
        using var server = new TinyServer();
        using var p = new Proof($"  app: {{ start: ./run.sh, url: \"{server.Url}\", seed: ./seed.sh }}\n");
        p.Prepare();
        const string spec = "e2e/stangent/orders.spec.ts";
        p.App.Write(spec, "import { test, expect } from '@playwright/test';\ntest('orders rejects big totals', async ({ page }) => {\n  await page.goto('/orders');\n  await expect(page.getByText('Needs approval')).toBeVisible();\n});");
        string Fail() => """{"suites":[{"title":"orders.spec.ts","specs":[{"title":"orders rejects big totals","tests":[{"results":[{"status":"failed","error":{"message":"Error: expect(locator).toBeVisible() failed","stack":"at /repo/e2e/stangent/orders.spec.ts:4:50"}}]}]}]}]}""";
        string Pass() => """{"suites":[{"title":"orders.spec.ts","specs":[{"title":"orders rejects big totals","tests":[{"results":[{"status":"passed"}]}]}]}]}""";
        var output = new JsonObject
        {
            ["outcome"] = "test-written", ["target"] = "playwright:e2e/stangent", ["testFile"] = spec, ["testName"] = "orders rejects big totals",
            ["assertion"] = new JsonObject { ["line"] = 4, ["text"] = "await expect(page.getByText('Needs approval')).toBeVisible();" },
            ["expectedQuote"] = "Orders over 100 are rejected until someone approves them.",
        }.ToJsonString();

        foreach (var r in new[] { Fail(), Pass(), Fail() }) p.Runner.Playwright.Enqueue(r);
        p.Check(output);
        Assert.Equal("not-reproduced-flaky", (string?)p.Record["outcome"]);
        Assert.Equal(1, p.Runner.Started);
        Assert.Contains(p.Runner.Commands, c => c == "./seed.sh");

        p.Prepare("seed two orders first");
        foreach (var r in new[] { Fail(), Fail(), Fail() }) p.Runner.Playwright.Enqueue(r);
        p.Check(output);
        Assert.Equal("reproduced", (string?)p.Record["outcome"]);
        Assert.Equal(3, p.Record["attempts"]![1]!["runs"]!.AsArray().Count);
    }

    [Fact]
    public void The_cap_covers_the_whole_attempt_and_a_retry_gets_only_what_is_left()
    {
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        var retry = p.Check(Written(quote: "a sentence the ticket never says at all"), Proof.Ok(turns: 10, usd: 0.6));
        Assert.Equal("retry", retry.Status);
        var left = BugProof.DriverLimits(p.App.Repo, Key);
        Assert.Equal(5, (int)left["maxTurns"]!);
        Assert.Equal(0.4, (double)left["maxBudgetUsd"]!, 2);
        Assert.Equal("recorded", p.Check(Written(), Proof.Ok(turns: 6, usd: 0.3)).Status);
        Assert.Equal("not-reproduced", (string?)p.Record["outcome"]);
        Assert.Equal("hit the cap: 16 of 15 turns", (string?)p.Record["attempts"]![0]!["reason"]);
        Assert.Empty(p.Runner.Commands); // capped before anything ran
    }

    [Fact]
    public void A_Vitest_package_at_the_repo_root_takes_a_test_anywhere_in_it()
    {
        using var p = new Proof(testProject: false, description: Description.Replace("Call `OrderService.Place` with 150.", "Press save in `OrdersPage.save` with 150."),
            packageJson: """{"devDependencies":{"vitest":"3.2.4"}}""");
        p.Prepare();
        var tools = BugProof.DriverLimits(p.App.Repo, Key)["allowedTools"]!.AsArray().Select(t => (string)t!).ToList();
        Assert.Contains("Edit(**/*.test.ts)", tools);
        const string file = "web/src/orders.test.ts";
        p.App.Write(file, "import { test, expect } from 'vitest';\ntest('rejects big orders', () => {\n  expect(place(150)).toBe('needs approval');\n});");
        p.Runner.Vitest.Enqueue("""{"testResults":[{"assertionResults":[{"fullName":"rejects big orders","status":"failed","failureMessages":["Error: expect(received).toBe(expected)\n    at /repo/web/src/orders.test.ts:3:21"]}]}]}""");
        p.Runner.Vitest.Enqueue("""{"testResults":[{"assertionResults":[{"fullName":"rejects big orders","status":"failed","failureMessages":["x"]}]}]}""");
        var output = Written(line: 3, text: "expect(place(150)).toBe('needs approval');", file: "./" + file, target: "vitest:.").Replace(TestName, "rejects big orders");
        Assert.Equal("recorded", p.Check(output).Status);
        Assert.Equal("reproduced", (string?)p.Record["outcome"]);
        Assert.Contains(p.Runner.Commands, c => c.Contains("-t 'rejects big orders$'", StringComparison.Ordinal));
    }

    [Fact]
    public void Test_names_are_matched_literally_and_exactly()
    {
        var t = new TestTarget("x", TestTarget.Unit, "vitest", ".", null, ["*.test.ts"], []);
        Assert.Contains(@"-t 'shows total \(with tax\)$'", t.TestCommand("a.test.ts", "shows total (with tax)", "r.json"));
        var dotnet = new TestTarget("d", TestTarget.Unit, "dotnet", "T", "T/T.csproj", ["*.cs"], []);
        Assert.Contains(@"FullyQualifiedName~N.C.Case\(1\)", dotnet.TestCommand(null, "N.C.Case(1)", "r.trx"));
        var pw = new TestTarget("p", TestTarget.Playwright, "playwright", "e2e", null, ["*.spec.ts"], []);
        Assert.Contains("--retries=0", pw.TestCommand("e2e/a.spec.ts", "a", "r.json"));
        Assert.True(BugProof.SameTest("Shop.Tests.OrderTests.Rejects_over_100", "Shop.Tests.OrderTests.Rejects_over_100"));
        Assert.True(BugProof.SameTest("Rejects_over_100(150)", "Shop.Tests.OrderTests.Rejects_over_100"));
        Assert.False(BugProof.SameTest("Shop.Tests.OrderTests.Rejects_over_1000", "Shop.Tests.OrderTests.Rejects_over_100"));
        Assert.False(BugProof.SameTest("Shop.Tests.Other.Rejects_over_100", "Shop.Tests.OrderTests.Rejects_over_100"));
        Assert.False(new TestTarget("x", TestTarget.Unit, "vitest", ".", null, ["*.test.ts"], []).Allows("../outside/a.test.ts"));
        Assert.False(new TestTarget("x", TestTarget.Unit, "vitest", ".", null, ["*.test.ts"], []).Allows("/etc/a.test.ts"));
    }

    /// <summary>Stands in for dotnet and npx: canned reports written where the command asks for them.</summary>
    sealed class FakeRunner : ICommandRunner
    {
        public List<string> Commands { get; } = [];
        public Queue<string> Tests { get; } = new();
        public Queue<string> Playwright { get; } = new();
        public Queue<string> Vitest { get; } = new();
        public string? All { get; set; }
        public string? BuildFails { get; set; }
        public int Started { get; private set; }

        public Task<CommandResult> RunAsync(string command, string cwd, TimeSpan timeout, IReadOnlyDictionary<string, string>? env = null, CancellationToken ct = default)
        {
            Commands.Add(command);
            if (command.StartsWith("dotnet build", StringComparison.Ordinal))
                return Task.FromResult(BuildFails is null ? new CommandResult(0, "Build succeeded.", false, 5) : new CommandResult(1, BuildFails, false, 5));
            if (command.StartsWith("dotnet test", StringComparison.Ordinal))
            {
                var report = Regex.Match(command, "LogFileName=([^']+)").Groups[1].Value;
                var content = command.Contains("--filter", StringComparison.Ordinal) ? Tests.Dequeue() : All;
                if (content is not null) File.WriteAllText(report, content);
                return Task.FromResult(new CommandResult(1, "", false, 10));
            }
            if (command.StartsWith("npx vitest", StringComparison.Ordinal))
            {
                File.WriteAllText(Regex.Match(command, "--outputFile='([^']+)'").Groups[1].Value, Vitest.Dequeue());
                return Task.FromResult(new CommandResult(1, "", false, 10));
            }
            if (command.StartsWith("npx playwright", StringComparison.Ordinal))
            {
                File.WriteAllText(env!["PLAYWRIGHT_JSON_OUTPUT_NAME"], Playwright.Dequeue());
                return Task.FromResult(new CommandResult(1, "", false, 10));
            }
            return Task.FromResult(new CommandResult(0, "", false, 1));
        }

        public IAsyncDisposable Start(string command, string cwd, IReadOnlyDictionary<string, string>? env = null)
        {
            Started++;
            Commands.Add("start: " + command);
            return new Nothing();
        }

        sealed class Nothing : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }

    /// <summary>An app that's "up": answers 200 on a loopback port.</summary>
    sealed class TinyServer : IDisposable
    {
        readonly HttpListener listener = new();
        public string Url { get; }

        public TinyServer()
        {
            var port = 41000 + Random.Shared.Next(2000);
            Url = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(Url);
            listener.Start();
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    try { var c = await listener.GetContextAsync(); c.Response.StatusCode = 200; c.Response.Close(); }
                    catch (HttpListenerException) { break; }
                    catch (ObjectDisposedException) { break; }
                }
            });
        }

        public void Dispose() => listener.Close();
    }
}

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
                      <ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" /><PackageReference Include="xunit" /><ProjectReference Include="..\Core\Shop.Core.csproj" /></ItemGroup>
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
        string quote = "Orders over 100 are rejected until someone approves them.", string file = TestFile, string target = "dotnet:Tests/Shop.Tests.csproj",
        string actual = "An order of 150 is placed without approval.", string? shows = "No exception was thrown")
    {
        var o = new JsonObject
        {
            ["outcome"] = "test-written", ["target"] = target, ["testFile"] = file, ["testName"] = TestName,
            ["assertion"] = new JsonObject { ["line"] = line, ["text"] = text }, ["expectedQuote"] = quote, ["actualQuote"] = actual,
        };
        if (shows is not null) o["shows"] = shows;
        return o.ToJsonString();
    }

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
        Assert.Contains($"Edit({ClaudeRules.Absolute(Path.Combine(p.Root, "Tests"))}/**/*.cs)", tools);
        Assert.Contains("Bash(dotnet test 'Tests/Shop.Tests.csproj':*)", tools);
        Assert.Contains("Bash(dotnet test --project 'Tests/Shop.Tests.csproj':*)", tools);   // MTP's form (audit item 1)
        Assert.DoesNotContain(tools, t => t.StartsWith($"Edit({ClaudeRules.Absolute(Path.Combine(p.Root, "Core"))}", StringComparison.Ordinal));
        Assert.All(tools.Where(t => t.StartsWith("Edit(", StringComparison.Ordinal)), t => Assert.StartsWith("Edit(//", t));   // D89: absolute, whatever the shell's folder
        Assert.Equal(30, (int)limits["maxTurns"]!);
        Assert.Equal(1.0, (double)limits["maxBudgetUsd"]!);
    }

    [Fact]
    public void Test_packages_added_in_Directory_Build_props_count_and_a_helper_library_does_not()
    {
        // Sonarr's layout: the test runner comes from Directory.Build.props for projects named *.Tests, and a shared
        // helper project references NUnit itself but runs no tests.
        using var p = new Proof(testProject: false);
        p.App.Write("Directory.Build.props", """
            <Project>
              <ItemGroup Condition="$(MSBuildProjectName.EndsWith('.Tests'))"><PackageReference Include="Microsoft.NET.Test.Sdk" /></ItemGroup>
            </Project>
            """);
        p.App.Write("Common/Shop.Testing.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="NUnit" /><ProjectReference Include="..\Core\Shop.Core.csproj" /></ItemGroup>
            </Project>
            """);
        p.App.Write("Tests/Shop.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\Common\Shop.Testing.csproj" /><ProjectReference Include="..\Core\Shop.Core.csproj" /></ItemGroup>
            </Project>
            """);
        p.Prepare();
        var input = File.ReadAllText(Path.Combine(p.Root, ".stangent", "tickets", Key, "prove", "input.md"));
        Assert.Contains("`dotnet:Tests/Shop.Tests.csproj`", input);
        Assert.DoesNotContain("Shop.Testing.csproj", input);
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
        Assert.Equal("An order of 150 is placed without approval.", (string?)attempt["actualQuote"]);   // D95: side by side
        Assert.Equal("No exception was thrown", (string?)attempt["shows"]);
        Assert.Contains(p.Runner.Commands, c => c.StartsWith("dotnet build", StringComparison.Ordinal));

        Assert.Equal("Pat Developer", (string?)BugProof.Confirm(p.App.Repo, Key, "Pat Developer")["confirmedBy"]);
        Assert.Equal("current", p.Prepare().Status); // nothing changed: no AI call
        Assert.Equal("prepared", p.Prepare("set the total to exactly 100.01").Status); // a hint is a new attempt
    }

    [Fact]
    public void A_reproduction_names_the_tickets_actual_result_and_the_words_of_its_failure_that_show_it()
    {
        // D95: the quote must be in the ticket, and `shows` in the message the test really printed; each gets the retry.
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        p.Runner.Tests.Enqueue(AssertFailure());
        var made = p.Check(Written(actual: "An order of 150 goes straight through to shipping."));
        Assert.Equal("retry", made.Status);
        Assert.Contains(made.Errors!, e => e.StartsWith("actualQuote") && e.Contains("isn't in the ticket word for word"));

        p.Runner.Tests.Enqueue(AssertFailure());
        p.App.Write(TestFile, TestCode);
        var wrong = p.Check(Written(shows: "but found 150"));
        Assert.Equal("failed", wrong.Status);   // the retry was used: a second miss is recorded as failed
        Assert.Contains(wrong.Errors!, e => e.Contains("shows \"but found 150\" isn't in the failure message") && e.Contains("No exception was thrown"));
    }

    [Fact]
    public void A_reproduction_without_shows_is_asked_for_it_with_the_message()
    {
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        p.Runner.Tests.Enqueue(AssertFailure());
        var step = p.Check(Written(shows: null));
        Assert.Equal("retry", step.Status);
        Assert.Contains(step.Errors!, e => e.StartsWith("Say which words of the failure message") && e.Contains("No exception was thrown"));
    }

    [Fact]
    public void Shows_is_checked_against_every_run_the_whole_message_and_not_the_tests_own_words()
    {
        JsonObject R(string m) => new() { ["message"] = m };
        // A Playwright proof runs 3 times: every run must show it.
        Assert.NotNull(BugProof.ShowsProblem("Received: \"0\"", [R("Received: \"0\""), R("Received: \"0\""), R("Received: \"5\"")]));
        // A message cut for storage is still checked whole.
        var longMessage = new string('x', 2100) + " to contain \"English\"";
        Assert.Null(BugProof.ShowsProblem("to contain \"English\"", [new JsonObject { ["message"] = longMessage[..2000], ["full"] = longMessage }]));
        // Words the test itself holds (its expected value, its "because" text) say nothing about what the code did.
        const string source = "result.Should().Be(2, \"because Jellyfin needs one episode per file\");";
        Assert.Contains("is written in your test",
            BugProof.ShowsProblem("because Jellyfin needs one episode per file", [R("Expected value to be 2 because Jellyfin needs one episode per file, but found 4.")], source));
        Assert.Null(BugProof.ShowsProblem("but found 4", [R("Expected value to be 2 because Jellyfin needs one episode per file, but found 4.")], source));
    }

    [Fact]
    public void Shows_is_kept_only_on_a_reproduced_attempt()
    {
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        p.Runner.Tests.Enqueue(TestReportTests.Trx((TestName, "Passed", "", "")));
        Assert.Equal("recorded", p.Check(Written()).Status);
        var attempt = p.Record["attempts"]![0]!;
        Assert.Equal("not-reproduced", (string?)p.Record["outcome"]);
        Assert.Null(attempt["shows"]);   // never checked against a failure, so never shown as "the test got"
        Assert.Equal("An order of 150 is placed without approval.", (string?)attempt["actualQuote"]);
    }

    [Theory]
    [InlineData("found 11", "Expected value to be less than or equal to 1, but found 11 (difference of 10).", true)]
    [InlineData("FOUND  11", "Expected value to be less than or equal to 1, but found 11.", true)]                // case, spacing
    [InlineData("toBeVisible() failed", "Error: \u001b[2mexpect(\u001b[22mlocator).toBeVisible() failed", true)]
    [InlineData("expect(locator).toBeVisible() failed", "Error: \u001b[2mexpect(\u001b[22mlocator).toBeVisible() failed", true)]
    [InlineData("but found 12", "Expected value to be 1, but found 11.", false)]
    public void Shows_must_be_in_the_failure_message(string shows, string message, bool ok)
    {
        var problem = BugProof.ShowsProblem(shows, [new JsonObject { ["message"] = message }]);
        Assert.Equal(ok, problem is null);
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

    /// <summary>Re-picks the points: every code point removed, outside parts added or removed; then the bundle again.</summary>
    static void Outside(Proof p, string[]? add = null, string[]? remove = null, bool dropCode = true)
    {
        var ctx = TicketContext.Load(p.App.Repo);
        var ids = dropCode ? TicketPoints.LoadOrFail(p.Root, Key)["points"]!.AsArray().Select(x => (string)x!["id"]!).ToArray() : [];
        TicketPoints.Update(p.App.Repo, ctx, Key, [], ids, "Pat Developer", add ?? [], remove ?? []);
        TicketBundle.Build(p.App.Repo, ctx, Key);
    }

    [Fact]
    public void With_every_part_outside_the_code_proof_is_skipped_until_a_developer_removes_the_tag()
    {
        using var p = new Proof();
        var code = TicketPoints.LoadOrFail(p.Root, Key)["points"]!.AsArray().Select(x => (string)x!["id"]!).ToArray();
        Outside(p, add: ["network: the proxy in front of it times out"]);
        var skipped = p.Prepare();
        Assert.Equal("recorded", skipped.Status);
        Assert.Equal("none", skipped.Action);                          // no AI call (D83)
        Assert.Equal("not-provable", (string?)p.Record["outcome"]);
        Assert.True((bool?)p.Record["attempts"]![0]!["outside"]);
        Assert.Contains("outside the code (network)", (string?)p.Record["attempts"]![0]!["reason"]);
        Assert.Equal("current", p.Prepare().Status);

        // The developer says it's in the code after all: the tag goes, the method comes back, and the reproducer gets its attempt.
        var ctx = TicketContext.Load(p.App.Repo);
        TicketPoints.Update(p.App.Repo, ctx, Key, code, [], "Pat Developer", removeOutside: ["network"]);
        TicketBundle.Build(p.App.Repo, ctx, Key);
        Assert.Equal(StepSpec.Prove.Agent, p.Prepare().Agent);
    }

    [Fact]
    public void With_a_code_part_and_an_outside_part_the_reproducer_tries_and_not_provable_is_accepted_at_once()
    {
        using var p = new Proof();
        Outside(p, add: ["deployment: only on the server behind the load balancer"], dropCode: false);
        Assert.Equal(StepSpec.Prove.Agent, p.Prepare().Agent);         // a code point is there: proof runs
        Assert.Contains("## Outside the code", File.ReadAllText(Path.Combine(p.Root, ".stangent", "tickets", Key, "prove", "input.md")));

        Assert.Equal("recorded", p.Check("""{"outcome":"not-provable","reason":"it only happens behind the load balancer"}""").Status); // no push-back
        Assert.Equal("not-provable", (string?)p.Record["outcome"]);
        Assert.True((bool?)p.Record["attempts"]![0]!["outside"]);
        Assert.Contains("outside the code: deployment", (string?)p.Record["attempts"]![0]!["reason"]);
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

    [Theory]
    [InlineData("/orders", "/orders", true)]
    [InlineData("/orders", "/orders/?tab=1", true)]
    [InlineData("/orders", "http://127.0.0.1:8989/orders", true)]
    [InlineData("/series/:titleSlug", "/series/the-test-show", true)]
    [InlineData("/orders", "/orders/5", false)]
    [InlineData("/settings/profiles", "/settings/general", false)]
    public void A_spec_opens_a_screen_when_its_goto_path_matches_the_route(string route, string path, bool matches) =>
        Assert.Equal(matches, AppSession.RouteMatches(route, path));

    const string AppSetting = "  app: { start: ./run.sh, url: \"http://127.0.0.1:8989/\", playwrightDir: e2e }\n";

    [Fact]
    public void With_no_related_spec_the_reproducer_gets_the_running_app_its_own_runs_and_a_browser()
    {
        using var p = new Proof(AppSetting);
        p.Prepare();
        var input = File.ReadAllText(Path.Combine(p.Root, ".stangent", "tickets", Key, "prove", "input.md"));
        Assert.Contains("# The app while you work", input);
        Assert.Contains("No Playwright test opens these screens yet", input);
        Assert.Contains($".stangent/tickets/{Key}/tools/playwright.sh <spec", input);

        var limits = BugProof.DriverLimits(p.App.Repo, Key);
        var tools = limits["allowedTools"]!.AsArray().Select(t => (string)t!).ToList();
        Assert.Contains($"Bash(.stangent/tickets/{Key}/tools/playwright.sh:*)", tools);
        Assert.Contains("mcp__playwright__browser_snapshot", tools);
        Assert.DoesNotContain(tools, t => t.Contains("browser_evaluate") || t.Contains("browser_file_upload"));
        Assert.True((bool)limits["app"]!["browser"]!);

        // The script runs only the reproducer's own specs or a related one, never another file.
        var script = File.ReadAllText(Path.Combine(p.Root, ".stangent", "tickets", Key, "tools", "playwright.sh"));
        Assert.Contains("'e2e/stangent'/*.spec.ts", script);
        Assert.Contains("only a spec in e2e/stangent/", script);
    }

    [Fact]
    public void Playwright_runs_with_a_config_beside_the_repos_own_that_points_at_the_served_app()
    {
        // Generality audit items 2 and 3: the repo's config may fix its baseURL, test folder and file pattern.
        using var p = new Proof(AppSetting);
        p.App.Write("e2e/playwright.config.ts", "export default { testDir: './tests', testMatch: '**/*.e2e.ts', webServer: { command: 'npm start' } };");
        p.Prepare();
        var config = File.ReadAllText(Path.Combine(p.Root, "e2e", AppSession.ConfigFile));
        Assert.Contains("import * as own from \"./playwright.config.ts\"", config);
        Assert.Contains("baseURL: process.env.BASE_URL", config);
        Assert.Contains("webServer: undefined", config);
        Assert.Contains(System.Text.Json.JsonSerializer.Serialize(Path.Combine(p.Root, "e2e", "stangent")), config);
        Assert.Contains("name,", config);                                                          // one project of stangent's own
        Assert.Contains("dependencies: like.dependencies", config);                               // after the repo's login setup
        Assert.StartsWith("// @ts-nocheck", config);
        var run = $"--config {AppSession.ConfigFile} --project={AppSession.Project}";
        Assert.Contains(run, File.ReadAllText(Path.Combine(p.Root, ".stangent", "tickets", Key, "tools", "playwright.sh")));
        Assert.Contains(run, new TestTarget("p", TestTarget.Playwright, "playwright", "e2e/stangent", "e2e", ["*.spec.ts"], []).TestCommand("stangent/a.spec.ts", "a", "r.json"));
    }

    [Fact]
    public void A_related_spec_is_offered_to_copy_and_then_no_browser_is_needed()
    {
        using var p = new Proof(AppSetting);
        p.App.Write("e2e/tests/orders.spec.ts", "test('orders', async ({ page }) => { await page.goto('/orders'); });");
        p.App.Write("e2e/tests/cart.spec.ts", "test('cart', async ({ page }) => { await page.goto('/cart'); });");
        p.App.Write("e2e/stangent/old-attempt.spec.ts", "test('x', async ({ page }) => { await page.goto('/orders'); });");   // the reproducer's own folder: not related
        var ctx = TicketContext.Load(p.App.Repo);
        TicketPoints.Update(p.App.Repo, ctx, Key, ["page:/orders"], [], "Pat Developer");
        TicketBundle.Build(p.App.Repo, ctx, Key);
        p.Prepare();

        var input = File.ReadAllText(Path.Combine(p.Root, ".stangent", "tickets", Key, "prove", "input.md"));
        Assert.Contains("- `e2e/tests/orders.spec.ts`", input);
        Assert.DoesNotContain("cart.spec.ts", input);
        Assert.DoesNotContain("old-attempt.spec.ts", input);
        var limits = BugProof.DriverLimits(p.App.Repo, Key);
        Assert.False((bool)limits["app"]!["browser"]!);
        Assert.DoesNotContain(limits["allowedTools"]!.AsArray(), t => ((string)t!).StartsWith("mcp__", StringComparison.Ordinal));
        Assert.Contains("'e2e/tests/orders.spec.ts') ;;", File.ReadAllText(Path.Combine(p.Root, ".stangent", "tickets", Key, "tools", "playwright.sh")));
    }

    [Fact]
    public async Task Serve_keeps_the_app_up_runs_the_related_specs_once_and_stops_when_told()
    {
        using var server = new TinyServer();
        using var p = new Proof($"  app: {{ start: ./run.sh, url: \"{server.Url}\", playwrightDir: e2e }}\n");
        p.App.Write("e2e/tests/orders.spec.ts", "test('orders', async ({ page }) => { await page.goto('/orders'); });");
        var ctx = TicketContext.Load(p.App.Repo);
        TicketPoints.Update(p.App.Repo, ctx, Key, ["page:/orders"], [], "Pat Developer");
        TicketBundle.Build(p.App.Repo, ctx, Key);
        p.Prepare();

        using var stop = new CancellationTokenSource();
        var serving = AppSession.ServeAsync(p.App.Repo, Key, p.Runner, stop.Token);
        var ready = Path.Combine(p.Root, ".stangent", "tickets", Key, "tools", "app-ready.json");
        for (var i = 0; i < 100 && !File.Exists(ready); i++) await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(ready));
        Assert.Equal(1, p.Runner.Started);
        Assert.Contains(p.Runner.Commands, c => c.Contains("tests/orders.spec.ts", StringComparison.Ordinal));
        Assert.Contains("passed", File.ReadAllText(Path.Combine(p.Root, ".stangent", "tickets", Key, "prove", "related-run.md")));

        stop.Cancel();
        var result = await serving;
        Assert.True((bool)result["served"]!);
        Assert.False(File.Exists(ready));
    }

    [Fact]
    public async Task Serve_cancelled_while_the_app_starts_stops_the_app()
    {
        // Nothing answers at this URL, so serve is still waiting for the app when it's told to stop.
        using var p = new Proof("  app: { start: ./run.sh, url: \"http://127.0.0.1:1/\", readySeconds: 60 }\n");
        p.Prepare();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AppSession.ServeAsync(p.App.Repo, Key, p.Runner, stop.Token));
        Assert.Equal(1, p.Runner.Started);
        Assert.Equal(1, p.Runner.Stopped);
    }

    [Fact]
    public void The_reproducer_may_write_only_its_result_not_the_attempts_state()
    {
        using var p = new Proof(AppSetting);
        p.Prepare();
        var tools = BugProof.DriverLimits(p.App.Repo, Key)["allowedTools"]!.AsArray().Select(t => (string)t!).ToList();
        Assert.Contains($"Edit({ClaudeRules.Absolute(Path.Combine(p.Root, ".stangent", "tickets", Key, "prove", "output.json"))})", tools);
        Assert.DoesNotContain(tools, t => t.Contains("prove/**", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("e2e/stangent/|.*.spec.ts")]       // a pattern that would match every spec
    [InlineData("/etc/x.spec.ts")]                  // another folder
    [InlineData("e2e/stangent/../../x.spec.ts")]
    [InlineData("e2e/tests/cart.spec.ts")]          // a spec that isn't related
    [InlineData("e2e/stangent/a b.spec.ts")]
    public void The_run_script_refuses_anything_but_its_own_or_a_related_spec(string arg)
    {
        if (OperatingSystem.IsWindows()) return;
        using var p = new Proof(AppSetting);
        p.Prepare();
        var psi = new System.Diagnostics.ProcessStartInfo("bash") { RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = p.Root };
        psi.ArgumentList.Add(Path.Combine(p.Root, ".stangent", "tickets", Key, "tools", "playwright.sh"));
        psi.ArgumentList.Add(arg);
        using var run = System.Diagnostics.Process.Start(psi)!;
        run.WaitForExit(10_000);
        Assert.Equal(2, run.ExitCode);
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
            ["actualQuote"] = "An order of 150 is placed without approval.", ["shows"] = "toBeVisible() failed",
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
        var retry = p.Check(Written(quote: "a sentence the ticket never says at all"), Proof.Ok(turns: 20, usd: 0.6));
        Assert.Equal("retry", retry.Status);
        var left = BugProof.DriverLimits(p.App.Repo, Key);
        Assert.Equal(10, (int)left["maxTurns"]!);
        Assert.Equal(0.4, (double)left["maxBudgetUsd"]!, 2);
        Assert.Equal("recorded", p.Check(Written(), Proof.Ok(turns: 11, usd: 0.3)).Status);
        Assert.Equal("not-reproduced", (string?)p.Record["outcome"]);
        Assert.Equal("hit the cap: 31 of 30 turns", (string?)p.Record["attempts"]![0]!["reason"]);
        Assert.Empty(p.Runner.Commands); // capped before anything ran
    }

    static JsonObject Refused(string tool, string field, string value) =>
        new() { ["tool_name"] = tool, ["tool_use_id"] = "toolu_1", ["tool_input"] = new JsonObject { [field] = value } };

    static RunStep CheckRefused(Proof p, string? output, params JsonObject[] refused)
    {
        var file = Path.Combine(p.Root, ".stangent", "tickets", Key, "prove", "output.json");
        if (output is null) File.Delete(file); else File.WriteAllText(file, output);
        var claude = Proof.Ok(turns: 12);
        claude["permission_denials"] = new JsonArray([.. refused]);
        return BugProof.CheckAsync(p.App.Repo, Key, claude, p.Runner).GetAwaiter().GetResult();
    }

    [Fact]
    public void When_Claude_Code_refuses_what_stangents_rules_allow_the_attempt_is_a_setup_failure_and_tried_again_later()
    {
        // D89: the reproducer ran `cd Tests`, and Claude Code then refused its write to a file the target allows and the
        // run of an allowed command; it ended with no result. That says nothing about the reproducer.
        using var p = new Proof();
        p.Prepare();
        var step = CheckRefused(p, null,
            Refused("Write", "file_path", Path.Combine(p.Root, TestFile)),
            Refused("Bash", "command", $"cd {p.Root} && dotnet test 'Tests/Shop.Tests.csproj' --filter x"),
            Refused("Bash", "command", "curl https://example.com"));                       // outside the rules: not the setup's fault
        Assert.Equal("blocked", step.Status);
        var attempt = p.Record["attempts"]![0]!;
        Assert.Equal("failed", (string?)attempt["outcome"]);
        Assert.True((bool)attempt["setupBlocked"]!);
        Assert.Contains($"Write {TestFile}", (string?)attempt["reason"]);
        Assert.Contains("Bash cd ", (string?)attempt["reason"]);
        Assert.DoesNotContain("curl", (string?)attempt["reason"]);
        Assert.Empty(p.Runner.Commands);                                                       // no retry, nothing run
        Assert.Equal(StepSpec.BugReproducer, p.Prepare().Agent);                               // the next run tries again
    }

    [Fact]
    public void Refusals_of_what_the_rules_do_not_allow_are_the_rules_working()
    {
        using var p = new Proof();
        p.Prepare();
        var step = CheckRefused(p, null,
            Refused("Edit", "file_path", Path.Combine(p.Root, "Core", "OrderService.cs")),     // the code under test
            Refused("Write", "file_path", Path.Combine(p.Root, "..", "elsewhere", "OrderTests.cs")),
            Refused("Bash", "command", "rm -rf Tests"));
        Assert.Equal("retry", step.Status);                                                    // the usual push-back
        Assert.Empty(BugProof.Blocked(p.App.Repo, new TicketPaths(p.Root, Key), [], new JsonObject()));
    }

    [Fact]
    public void A_test_that_reproduces_the_bug_counts_even_when_something_allowed_was_refused_on_the_way()
    {
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        p.Runner.Tests.Enqueue(AssertFailure());
        var step = CheckRefused(p, Written(), Refused("Edit", "file_path", Path.Combine(p.Root, TestFile)));
        Assert.Equal("recorded", step.Status);
        Assert.Equal("reproduced", (string?)p.Record["outcome"]);
        Assert.Null(p.Record["attempts"]![0]!["setupBlocked"]);
    }

    [Fact]
    public async Task A_start_command_that_exits_is_reported_at_once_with_its_own_error()
    {
        // D89: Sonarr's seed failed on old commits, its start script exited, and serve waited 300 s to say "didn't answer".
        using var p = new Proof("  app: { start: ./run.sh, url: \"http://127.0.0.1:1/\", readySeconds: 120 }\n");
        p.Prepare();
        p.Runner.StartExits = "Runtime error near line 4: UNIQUE constraint failed: Series.TvRageId (19)\n";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<StangentException>(() => AppSession.ServeAsync(p.App.Repo, Key, p.Runner, CancellationToken.None));
        Assert.Equal("app_not_ready", e.Code);
        Assert.Contains("tickets.app.start exited (code 1)", e.Message);
        Assert.Contains("UNIQUE constraint failed", e.Message);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed}");
        Assert.Equal(1, p.Runner.Stopped);
    }

    [Fact]
    public void A_backend_point_picked_as_a_page_offers_the_unit_tests_that_reach_the_code_behind_it()
    {
        // D89: the only point is the POST /api/orders endpoint's page, not a symbol. Its slice runs through the action
        // into Shop.Core, which Tests/Shop.Tests.csproj covers, so the unit target is offered, not just "no project covers".
        using var p = new Proof(description: Description.Replace("Call `OrderService.Place` with 150.", "Place an order of 150."));
        var ctx = TicketContext.Load(p.App.Repo);
        var existing = TicketPoints.Update(p.App.Repo, ctx, Key, [], [], "Pat Developer")["points"]!.AsArray().Select(x => (string)x!["id"]!).ToList();
        TicketPoints.Update(p.App.Repo, ctx, Key, [SpecApp.Post], existing, "Pat Developer");
        var bundle = TicketBundle.Build(p.App.Repo, ctx, Key);
        Assert.Equal([SpecApp.Post], bundle["points"]!.AsArray().Select(x => (string)x!["id"]!).ToList());
        Assert.Equal(StepSpec.BugReproducer, p.Prepare().Agent);
        var target = AiStep.State(new TicketPaths(p.Root, Key), StepSpec.Prove)["targets"]!.AsArray().Select(t => TestTarget.FromJson(t!)).Single();
        Assert.Equal("dotnet:Tests/Shop.Tests.csproj", target.Id);
        Assert.Contains("Core/Shop.Core.csproj", target.Covers);
    }

    [Theory]
    [InlineData("dotnet test 'Tests/Shop.Tests.csproj' && rm -rf Core")]       // refused for the rm
    [InlineData("dotnet test 'Tests/Shop.Tests.csproj'; curl https://example.com")]
    [InlineData("dotnet test 'Tests/Shop.Tests.csproj' > /etc/x")]
    [InlineData("dotnet test 'Tests/Shop.Tests.csproj' 2>&1 | tail -20")]
    [InlineData("cd Tests")]
    [InlineData("dotnet test 'Tests/Shop.Tests.csproj' & rm -rf Core")]        // one & sends it to the background
    [InlineData("cd /etc && dotnet test 'Tests/Shop.Tests.csproj'")]            // a cd out of the repo
    [InlineData("cd .. && dotnet test 'Tests/Shop.Tests.csproj'")]
    public void A_command_line_counts_as_blocked_only_when_all_of_it_is_allowed(string command)
    {
        using var p = new Proof();
        p.Prepare();
        var paths = new TicketPaths(p.Root, Key);
        var targets = AiStep.State(paths, StepSpec.Prove)["targets"]!.AsArray().Select(t => TestTarget.FromJson(t!)).ToList();
        JsonObject Result(string c) => new() { ["permission_denials"] = new JsonArray(Refused("Bash", "command", c)) };
        Assert.Empty(BugProof.Blocked(p.App.Repo, paths, targets, Result(command)));
        Assert.Single(BugProof.Blocked(p.App.Repo, paths, targets, Result($"cd {p.Root} && dotnet test 'Tests/Shop.Tests.csproj' --filter x")));
        Assert.Single(BugProof.Blocked(p.App.Repo, paths, targets, Result("cd Tests && cd .. && dotnet test 'Tests/Shop.Tests.csproj' --filter x")));
    }

    [Fact]
    public async Task A_blocked_attempt_is_a_setup_failure_whether_it_hit_the_cap_said_not_provable_or_wrote_a_test_that_passes()
    {
        var write = Refused("Write", "file_path", "Tests/OrderTests.cs");                    // a relative path, from the repo
        using (var p = new Proof())
        {
            p.Prepare();
            var claude = Proof.Ok(turns: 31);
            claude["permission_denials"] = new JsonArray(write.DeepClone());
            File.Delete(Path.Combine(p.Root, ".stangent", "tickets", Key, "prove", "output.json"));
            Assert.Equal("blocked", (await BugProof.CheckAsync(p.App.Repo, Key, claude, p.Runner, TestContext.Current.CancellationToken)).Status);
            Assert.True((bool)p.Record["attempts"]![0]!["setupBlocked"]!);
        }
        using (var p = new Proof())
        {
            p.Prepare();
            Assert.Equal("blocked", CheckRefused(p, """{"outcome":"not-provable","reason":"only a browser test is offered"}""", (JsonObject)write.DeepClone()).Status);
            Assert.Equal("failed", (string?)p.Record["outcome"]);
        }
        using (var p = new Proof())
        {
            p.Prepare();
            p.App.Write(TestFile, TestCode);
            p.Runner.Tests.Enqueue(TestReportTests.Trx((TestName, "Passed", null, null)));
            Assert.Equal("blocked", CheckRefused(p, Written(), (JsonObject)write.DeepClone()).Status);
            Assert.True((bool)p.Record["attempts"]![0]!["setupBlocked"]!);
            Assert.NotEmpty(p.Record["attempts"]![0]!["runs"]!.AsArray());                     // what ran is kept
        }
    }

    [Fact]
    public async Task A_start_command_that_exits_0_is_waited_for_as_before()
    {
        // It may have started the app in the background (docker compose up -d): not a failure by itself.
        using var p = new Proof("  app: { start: ./run.sh, url: \"http://127.0.0.1:1/\", readySeconds: 2 }\n");
        p.Prepare();
        p.Runner.StartExits = "started in the background\n";
        p.Runner.StartExitCode = 0;
        var e = await Assert.ThrowsAsync<StangentException>(() => AppSession.ServeAsync(p.App.Repo, Key, p.Runner, CancellationToken.None));
        Assert.Contains("didn't answer at http://127.0.0.1:1/ within 2 s", e.Message);
        Assert.Contains("started in the background", e.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]    // without the split the screen's slice walks through the endpoint into Shop.Api
    public void Code_behind_a_page_widens_the_search_only_when_the_points_own_code_has_no_unit_test(bool split)
    {
        // D89 review: with Shop.Core's OrderService.Place as a point and the /orders screen too, the screen's slice reaches
        // the Api endpoint. Counting that would score Api.Tests (Api and Core) above Tests (Core) by reach alone.
        using var p = new Proof(split ? "" : "spec: { splitAtEndpoints: false }\n");
        p.App.Write("Api/Shop.Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"..\\Core\\Shop.Core.csproj\" /></ItemGroup></Project>");
        p.App.Write("ApiTests/Shop.Api.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" /><PackageReference Include="xunit" /><ProjectReference Include="..\Api\Shop.Api.csproj" /><ProjectReference Include="..\Core\Shop.Core.csproj" /></ItemGroup>
            </Project>
            """);
        var ctx = TicketContext.Load(p.App.Repo);
        TicketPoints.Update(p.App.Repo, ctx, Key, ["page:/orders"], [], "Pat Developer");
        TicketBundle.Build(p.App.Repo, ctx, Key);
        p.Prepare();
        var targets = AiStep.State(new TicketPaths(p.Root, Key), StepSpec.Prove)["targets"]!.AsArray().Select(t => TestTarget.FromJson(t!)).ToList();
        Assert.All(targets.Where(t => t.Kind == TestTarget.Unit), t => Assert.Equal(["Core/Shop.Core.csproj"], t.Covers));
        Assert.Contains(targets, t => t.Id == "dotnet:Tests/Shop.Tests.csproj");
    }

    const string NoRuntimeTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="1" name="run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <ResultSummary outcome="Failed">
            <Counters total="0" executed="0" passed="0" failed="0" error="0" />
            <RunInfos>
              <RunInfo computerName="ci" outcome="Error" timestamp="2026-11-01T00:38:10">
                <Text>Testhost process for source(s) 'Tests.dll' exited with error: You must install or update .NET to run this application. Framework: 'Microsoft.NETCore.App', version '8.0.29'</Text>
              </RunInfo>
            </RunInfos>
          </ResultSummary>
        </TestRun>
        """;

    [Fact]
    public void A_test_runner_that_cannot_start_is_a_setup_failure_not_the_reproducers()
    {
        // D90: on an old commit the tests need .NET 8 and the machine has only 10. The TRX holds no test, only the host's
        // error; it used to read as "no test named … ran", the reproducer's fault.
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        p.Runner.Tests.Enqueue(NoRuntimeTrx);
        // Real VSTest output: the header comes first even when the host can't start (D90 recheck).
        p.Runner.ListOutput = "The following Tests are available:\nTestPlatformException: Testhost process for source(s) 'Tests.dll' exited with error: You must install or update .NET to run this application.";
        Assert.Equal("blocked", p.Check(Written()).Status);
        var attempt = p.Record["attempts"]![0]!;
        Assert.Equal("failed", (string?)attempt["outcome"]);
        Assert.True((bool)attempt["setupBlocked"]!);
        Assert.Contains("You must install or update .NET", (string?)attempt["reason"]);
        Assert.Equal(BugProof.RunnerError, (string?)attempt["runs"]![0]!["result"]);
        Assert.Contains("the .NET 8.0 runtime", (string?)attempt["advice"]);                    // D91
    }

    static string RunInfoTrx(string outcome, string text) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="1" name="run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <ResultSummary outcome="Failed"><Counters total="0" executed="0" />
            <RunInfos><RunInfo computerName="ci" outcome="{outcome}" timestamp="2026-11-01T00:38:10"><Text>{text}</Text></RunInfo></RunInfos>
          </ResultSummary>
        </TestRun>
        """;

    [Theory]
    // The test crashed the host itself (a stack overflow in the code under test): the runner lists tests fine, so it's
    // the test's doing and it gets the retry, not a stopped batch.
    [InlineData("Error", "The active test run was aborted. Reason: Test host process crashed : Stack overflow.", "The following Tests are available:\n    Rejects_orders_over_100\n")]
    // An ordinary wrong name: VSTest's RunInfo is a Warning.
    [InlineData("Warning", "No test matches the given testcase filter `FullyQualifiedName~X` in /r/Tests.dll", "The following Tests are available:\n    Rejects_orders_over_100\n")]
    // Two target frameworks, one runtime missing: the runner still lists tests from the other, so it isn't broken.
    [InlineData("Warning", "No test matches the given testcase filter", "The following Tests are available:\n    Rejects_orders_over_100\nTesthost exited with error: You must install or update .NET to run this application.")]
    public void A_run_with_no_result_is_the_runners_failure_only_when_the_runner_lists_no_tests(string outcome, string text, string list)
    {
        using var p = new Proof();
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        p.Runner.Tests.Enqueue(RunInfoTrx(outcome, text));
        p.Runner.ListOutput = list;
        Assert.Equal("retry", p.Check(Written()).Status);
    }

    [Fact]
    public void Under_MTP_a_run_with_no_report_is_checked_by_listing_too()
    {
        // MTP with no runtime for the target exits non-zero and writes no report; its list says "Discovered 0 tests".
        using var p = new Proof();
        p.App.Write("global.json", """{"test":{"runner":"Microsoft.Testing.Platform"}}""");
        p.Prepare();
        p.App.Write(TestFile, TestCode);
        p.Runner.Tests.Enqueue(null!);                                                         // no report written
        p.Runner.ListOutput = "You must install or update .NET to run this application.\nDiscovered 0 tests";
        Assert.Equal("blocked", p.Check(Written()).Status);
        Assert.Contains(p.Runner.Commands, c => c == "dotnet test --project 'Tests/Shop.Tests.csproj' --no-build --list-tests");
    }

    [Fact]
    public void The_list_command_lists_without_building_in_either_test_platform()
    {
        var t = new TestTarget("d", TestTarget.Unit, "dotnet", "T", "T/T.csproj", ["*.cs"], []) { Args = "-p:SolutionDir=/r/src/" };
        Assert.Equal("dotnet test 'T/T.csproj' -p:SolutionDir=/r/src/ --no-build --list-tests", t.ListCommand(vstest: true));
        Assert.Equal("dotnet test --project 'T/T.csproj' --no-build --list-tests", t.ListCommand(vstest: false));
        Assert.Null(new TestTarget("v", TestTarget.Unit, "vitest", ".", null, ["*.test.ts"], []).ListCommand());
    }

    [Fact]
    public void No_test_by_that_name_is_the_reproducers_mistake_only_if_the_runner_finds_tests_at_all()
    {
        // A runner whose adapter won't load (another commit's newer adapter left in a shared output folder) finds no
        // tests at all: that's the setup. One that lists tests but not this one: the reproducer named it wrong.
        const string Empty = """<?xml version="1.0" encoding="utf-8"?><TestRun id="1" name="run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="Completed"><Counters total="0" /></ResultSummary></TestRun>""";
        using (var p = new Proof())
        {
            p.Prepare();
            p.App.Write(TestFile, TestCode);
            p.Runner.Tests.Enqueue(Empty);
            p.Runner.ListOutput = "The following Tests are available:\nNo test is available in /r/_tests/net8.0/Shop.Tests.dll. Make sure that test discoverer & executors are registered";
            Assert.Equal("blocked", p.Check(Written()).Status);
            Assert.Contains("finds no tests at all", (string?)p.Record["attempts"]![0]!["reason"]);
            Assert.Contains(p.Runner.Commands, c => c.Contains("--list-tests", StringComparison.Ordinal));
        }
        using (var p = new Proof())
        {
            p.Prepare();
            p.App.Write(TestFile, TestCode);
            p.Runner.Tests.Enqueue(Empty);
            Assert.Equal("retry", p.Check(Written()).Status);                                  // the usual push-back
        }
    }

    [Fact]
    public void The_proofs_build_gets_the_graph_builds_properties_unless_the_settings_give_them()
    {
        // Audit item 4: a project that builds only with $(SolutionDir) set builds for the graph, so it should for the proof.
        using var p = new Proof();
        p.App.Write("Shop.sln", "Project(\"x\") = \"Shop.Core\", \"Core\\Shop.Core.csproj\"\nProject(\"x\") = \"Shop.Tests\", \"Tests\\Shop.Tests.csproj\"\n");
        p.Prepare();
        var target = AiStep.State(new TicketPaths(p.Root, Key), StepSpec.Prove)["targets"]!.AsArray().Select(t => TestTarget.FromJson(t!)).Single();
        Assert.Contains("-p:SolutionDir=" + p.Root.TrimEnd('/') + "/", target.Args);
        Assert.Contains("-p:NuGetAudit=false", target.Args);

        using var own = new Proof("  dotnetArgs: \"-p:SolutionDir={root}/src/ -p:NuGetAudit=true\"\n");
        own.Prepare();
        var mine = AiStep.State(new TicketPaths(own.Root, Key), StepSpec.Prove)["targets"]!.AsArray().Select(t => TestTarget.FromJson(t!)).Single();
        Assert.Equal("-p:EnableWindowsTargeting=true -p:SolutionDir=" + own.Root.TrimEnd('/') + "/src/ -p:NuGetAudit=true", mine.Args);   // the settings win
    }

    [Fact]
    public void A_test_project_reaching_the_code_through_a_long_chain_of_references_is_offered()
    {
        // Audit item 9: Tests → Web → Infrastructure → Application → Core. Two levels used to find nothing.
        using var p = new Proof(testProject: false);
        p.App.Write("App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"..\\Core\\Shop.Core.csproj\" /></ItemGroup></Project>");
        p.App.Write("Infra/Infra.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"..\\App\\App.csproj\" /></ItemGroup></Project>");
        p.App.Write("Web/Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"..\\Infra\\Infra.csproj\" /></ItemGroup></Project>");
        p.App.Write("Tests/Web.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" /><PackageReference Include="xunit" /><ProjectReference Include="..\Web\Web.csproj" /></ItemGroup>
            </Project>
            """);
        // (On macOS "Web/" lands in the fixture's existing "web/" folder: paths must match ignoring case.)
        Assert.Equal(StepSpec.BugReproducer, p.Prepare().Agent);
        var target = AiStep.State(new TicketPaths(p.Root, Key), StepSpec.Prove)["targets"]!.AsArray().Select(t => TestTarget.FromJson(t!)).Single();
        Assert.Equal("dotnet:Tests/Web.Tests.csproj", target.Id);
    }

    [Fact]
    public void Under_MTP_the_test_run_uses_the_build_that_just_ran()
    {
        var t = new TestTarget("d", TestTarget.Unit, "dotnet", "T", "T/T.csproj", ["*.cs"], []) { Args = "-p:NuGetAudit=false" };
        Assert.Contains("dotnet test --project 'T/T.csproj' --no-build", t.TestCommand(null, "N.C.T", "/r/one.trx", vstest: false));
    }

    [Fact]
    public void A_frontend_run_without_node_is_a_setup_failure_with_advice()
    {
        using var p = new Proof(testProject: false, description: Description.Replace("Call `OrderService.Place` with 150.", "Press save in `OrdersPage.save` with 150."),
            packageJson: """{"devDependencies":{"vitest":"3.2.4"}}""");
        p.Prepare();
        const string file = "web/src/orders.test.ts";
        p.App.Write(file, "import { test, expect } from 'vitest';\ntest('rejects big orders', () => {\n  expect(place(150)).toBe('needs approval');\n});");
        p.Runner.FrontendFails = "bash: npx: command not found";
        var output = Written(line: 3, text: "expect(place(150)).toBe('needs approval');", file: file, target: "vitest:.").Replace(TestName, "rejects big orders");
        Assert.Equal("blocked", p.Check(output).Status);
        Assert.Contains("Node.js isn't on the PATH", (string?)p.Record["attempts"]![0]!["advice"]);
    }

    [Fact]
    public void A_Vitest_package_at_the_repo_root_takes_a_test_anywhere_in_it()
    {
        using var p = new Proof(testProject: false, description: Description.Replace("Call `OrderService.Place` with 150.", "Press save in `OrdersPage.save` with 150."),
            packageJson: """{"devDependencies":{"vitest":"3.2.4"}}""");
        p.Prepare();
        var tools = BugProof.DriverLimits(p.App.Repo, Key)["allowedTools"]!.AsArray().Select(t => (string)t!).ToList();
        Assert.Contains($"Edit({ClaudeRules.Absolute(p.Root)}/**/*.test.ts)", tools);
        const string file = "web/src/orders.test.ts";
        p.App.Write(file, "import { test, expect } from 'vitest';\ntest('rejects big orders', () => {\n  expect(place(150)).toBe('needs approval');\n});");
        p.Runner.Vitest.Enqueue("""{"testResults":[{"assertionResults":[{"fullName":"rejects big orders","status":"failed","failureMessages":["Error: expect(received).toBe(expected)\n    at /repo/web/src/orders.test.ts:3:21"]}]}]}""");
        p.Runner.Vitest.Enqueue("""{"testResults":[{"assertionResults":[{"fullName":"rejects big orders","status":"failed","failureMessages":["x"]}]}]}""");
        var output = Written(line: 3, text: "expect(place(150)).toBe('needs approval');", file: "./" + file, target: "vitest:.", shows: "toBe(expected)").Replace(TestName, "rejects big orders");
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
        var sonarr = new TestTarget("d", TestTarget.Unit, "dotnet", "T", "T/T.csproj", ["*.cs"], []) { Args = "-p:SolutionDir=/r/src/", Filter = "TestCategory!=ManualTest" };
        Assert.Equal("dotnet build 'T/T.csproj' -p:SolutionDir=/r/src/", sonarr.BuildCommand);
        Assert.Contains("--filter 'TestCategory!=ManualTest'", sonarr.TestCommand(null, null, "all.trx"));      // a whole-project run
        Assert.DoesNotContain("ManualTest", sonarr.TestCommand(null, "N.C.T", "one.trx"));                    // one test by name
        Assert.Equal(sonarr, TestTarget.FromJson(sonarr.ToJson()) with { Patterns = sonarr.Patterns, Covers = sonarr.Covers });
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
        public int Stopped { get; set; }

        /// <summary>When set, the start command "exits" at once with this output (a seed that failed, D89).</summary>
        public string? StartExits { get; set; }
        /// <summary>When set, every npx run fails at once with this output and exit 127 (Node missing).</summary>
        public string? FrontendFails { get; set; }
        /// <summary>What `dotnet test --list-tests` prints.</summary>
        public string ListOutput { get; set; } = "The following Tests are available:\n    Rejects_orders_over_100\n";
        public int StartExitCode { get; set; } = 1;

        sealed class Stopper(FakeRunner runner, string? exited) : IRunningProcess
        {
            public bool HasExited => exited is not null;
            public int? ExitCode => exited is null ? null : runner.StartExitCode;
            public string Output => exited ?? "";
            public ValueTask DisposeAsync() { runner.Stopped++; return ValueTask.CompletedTask; }
        }

        public Task<CommandResult> RunAsync(string command, string cwd, TimeSpan timeout, IReadOnlyDictionary<string, string>? env = null, CancellationToken ct = default)
        {
            Commands.Add(command);
            if (command.Contains("--list-tests", StringComparison.Ordinal))
                return Task.FromResult(new CommandResult(0, ListOutput, false, 5));
            if (command.StartsWith("dotnet build", StringComparison.Ordinal))
                return Task.FromResult(BuildFails is null ? new CommandResult(0, "Build succeeded.", false, 5) : new CommandResult(1, BuildFails, false, 5));
            if (command.StartsWith("dotnet test", StringComparison.Ordinal))
            {
                var report = Regex.Match(command, "LogFileName=([^']+)").Groups[1].Value;
                var content = command.Contains("--filter", StringComparison.Ordinal) ? Tests.Dequeue() : All;
                if (content is not null) File.WriteAllText(report, content);
                return Task.FromResult(new CommandResult(1, "", false, 10));
            }
            if (FrontendFails is not null && command.StartsWith("npx ", StringComparison.Ordinal))
                return Task.FromResult(new CommandResult(127, FrontendFails, false, 1));
            if (command.StartsWith("npx vitest", StringComparison.Ordinal))
            {
                File.WriteAllText(Regex.Match(command, "--outputFile='([^']+)'").Groups[1].Value, Vitest.Dequeue());
                return Task.FromResult(new CommandResult(1, "", false, 10));
            }
            if (command.StartsWith("npx playwright", StringComparison.Ordinal) && env?.ContainsKey("PLAYWRIGHT_JSON_OUTPUT_NAME") != true)
                return Task.FromResult(new CommandResult(0, "  1 passed (0.9s)", false, 10));   // a related spec run by `ticket serve`
            if (command.StartsWith("npx playwright", StringComparison.Ordinal))
            {
                File.WriteAllText(env!["PLAYWRIGHT_JSON_OUTPUT_NAME"], Playwright.Dequeue());
                return Task.FromResult(new CommandResult(1, "", false, 10));
            }
            return Task.FromResult(new CommandResult(0, "", false, 1));
        }

        public IRunningProcess Start(string command, string cwd, IReadOnlyDictionary<string, string>? env = null)
        {
            Started++;
            Commands.Add("start: " + command);
            return new Stopper(this, StartExits);
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

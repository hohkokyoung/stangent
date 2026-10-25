using Stangent.Tickets.Proof;

namespace Stangent.Core.Tests;

/// <summary>Phase 3 step 5: reading test runners' reports by code, and telling an assertion failure from a crash (D75).</summary>
public class TestReportTests
{
    public static string Trx(params (string Name, string Outcome, string? Message, string? Stack)[] results) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="1" name="run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
        {string.Concat(results.Select(r => $"""
            <UnitTestResult testName="{r.Name}" outcome="{r.Outcome}">
              <Output>{(r.Message is null ? "" : $"<ErrorInfo><Message>{System.Security.SecurityElement.Escape(r.Message)}</Message><StackTrace>{System.Security.SecurityElement.Escape(r.Stack ?? "")}</StackTrace></ErrorInfo>")}</Output>
            </UnitTestResult>
        """))}
          </Results>
        </TestRun>
        """;

    [Fact]
    public void TRX_results_are_read_with_their_message_and_stack()
    {
        var cases = TestReports.ParseTrx(Trx(
            ("Shop.Tests.OrderTests.Rejects", "Failed", "Assert.Equal() Failure: Values differ\nExpected: 100\nActual:   50", "   at Shop.Tests.OrderTests.Rejects() in /src/Tests/OrderTests.cs:line 7"),
            ("Shop.Tests.OrderTests.Works", "Passed", null, null)));
        Assert.Equal(2, cases.Count);
        Assert.Equal("failed", cases[0].Status);
        Assert.True(TestReports.IsAssertion(cases[0]));
        Assert.Equal(7, TestReports.LineIn(cases[0], "Tests/OrderTests.cs"));
        Assert.Equal("passed", cases[1].Status);
    }

    [Theory]
    [InlineData("Assert.Equal() Failure: Values differ", "at Xunit.Assert.Equal", true)]
    [InlineData("  Expected: 5\n  But was:  3", "at NUnit.Framework.Assert.That(Object actual)", true)]
    [InlineData("Expected order.Total to be 100, but found 50.", "at FluentAssertions.Execution.XUnit2TestFramework.Throw", true)]
    [InlineData("System.NullReferenceException : Object reference not set to an instance of an object.", "at Shop.Tests.OrderTests.Setup()", false)]
    [InlineData("System.InvalidOperationException: Sequence contains no elements", "at System.Linq.Enumerable.First", false)]
    [InlineData("Error: expect(received).toBe(expected) // Object.is equality\n\nExpected: 100\nReceived: 50", "at Object.<anonymous> (src/order.test.ts:12:21)", true)]
    [InlineData("AssertionError: expected 50 to be 100", "at src/order.test.ts:12:21", true)]
    [InlineData("TypeError: Cannot read properties of undefined (reading 'total')", "at src/order.ts:4:10", false)]
    [InlineData("Error: page.goto: net::ERR_CONNECTION_REFUSED at http://localhost:8989/", "at e2e/series.spec.ts:5:14", false)]
    [InlineData("Error: expect(locator).toHaveText(expected) failed", "at e2e/series.spec.ts:9:31", true)]
    public void An_assertion_failure_is_told_apart_from_a_crash(string message, string stack, bool assertion) =>
        Assert.Equal(assertion, TestReports.IsAssertion(new TestCaseResult("t", "failed", message, stack)));

    [Fact]
    public void Jest_and_Vitest_reports_are_read()
    {
        var cases = TestReports.ParseJest("""
            {"testResults":[{"assertionResults":[
              {"fullName":"orders rejects big totals","status":"failed","failureMessages":["Error: expect(received).toBe(expected)\n    at Object.<anonymous> (/repo/web/src/order.test.ts:12:21)"]},
              {"fullName":"orders works","status":"passed","failureMessages":[]}]}]}
            """);
        Assert.Equal(["failed", "passed"], cases.Select(c => c.Status));
        Assert.Equal(12, TestReports.LineIn(cases[0], "web/src/order.test.ts"));
    }

    [Fact]
    public void Playwright_reports_are_read_with_timeouts_apart()
    {
        var cases = TestReports.ParsePlaywright("""
            {"suites":[{"title":"series.spec.ts","specs":[],"suites":[{"title":"series","specs":[{"title":"shows the new folder","tests":[{"results":[
              {"status":"failed","error":{"message":"Error: expect(locator).toHaveText(expected) failed","stack":"Error: ...\n    at /repo/e2e/stangent/series.spec.ts:9:31"}},
              {"status":"timedOut","error":{"message":"Test timeout of 30000ms exceeded."}}]}]}]}]}]}
            """);
        Assert.Equal(["failed", "timeout"], cases.Select(c => c.Status));
        Assert.Equal("series shows the new folder", cases[0].Name);
        Assert.Equal(9, TestReports.LineIn(cases[0], "e2e/stangent/series.spec.ts"));
    }

    [Fact]
    public void A_multi_line_assertion_spans_to_its_semicolon()
    {
        string[] lines = ["    [Fact]", "    public void T()", "    {", "        Assert.Equal(", "            100,", "            order.Total);", "    }"];
        Assert.Equal((4, 6), BugProof.Span(lines, 4));
        Assert.Equal((6, 6), BugProof.Span(lines, 6));
    }
}

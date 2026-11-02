using Stangent.Tickets.Proof;

namespace Stangent.Core.Tests;

/// <summary>D91: a setup failure carries a plain next step, chosen by code; stangent never installs anything itself.</summary>
public class SetupAdviceTests
{
    [Theory]
    [InlineData("the test runner failed before any test ran: Testhost exited with error: You must install or update .NET to run this application. Framework: 'Microsoft.NETCore.App', version '8.0.29' (arm64)",
        "the .NET 8.0 runtime")]
    [InlineData("You must install or update .NET. Framework: 'Microsoft.AspNetCore.App', version '6.0.1'", "ASP.NET Core 6.0 runtime")]
    [InlineData("the test runner finds no tests at all in T/T.csproj: No test is available in T.dll", "lists no tests")]
    [InlineData("tickets.app.start exited (code 1) before the app answered at http://127.0.0.1:8989/: UNIQUE constraint failed", "The app didn't start")]
    [InlineData("tickets.app.build failed: error CS0246", "The app didn't build")]
    [InlineData("bash: npx: command not found", "Node.js isn't on the PATH")]
    [InlineData("browserType.launch: Executable doesn't exist at /x/chrome", "npx playwright install chromium")]
    [InlineData("stangent's own permission rules blocked what they allow (Write T/A.cs): a setup bug", "A stangent bug")]
    [InlineData("something nobody planned for", "A setup problem, not the bug")]
    public void Each_known_setup_failure_gets_its_own_advice(string reason, string expected) =>
        Assert.Contains(expected, SetupAdvice.For(reason, "e2e"));

    [Fact]
    public void Advice_never_says_stangent_installed_anything() =>
        Assert.Contains("stangent doesn't install anything itself", SetupAdvice.For("You must install or update .NET. 'Microsoft.NETCore.App', version '8.0.1'"));
}

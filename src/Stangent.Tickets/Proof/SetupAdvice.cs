using System.Text.RegularExpressions;

namespace Stangent.Tickets.Proof;

/// <summary>
/// What a developer can do about a setup failure (D91), chosen by code from the error: the missing runtime and where
/// to get it, the app's start or build step, Node or Playwright, a runner that lists no tests, or stangent's own rules.
/// Text only: stangent never installs software or changes the machine itself.
/// </summary>
public static partial class SetupAdvice
{
    [GeneratedRegex(@"Microsoft\.(?<fw>NETCore|AspNetCore)\.App'?,? version '(?<v>\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MissingFramework();

    public static string For(string reason, string? playwrightDir = null)
    {
        if (reason.Contains("You must install or update .NET", StringComparison.Ordinal))
        {
            var m = MissingFramework().Match(reason);
            var v = m.Success ? m.Groups["v"].Value : null;
            var which = m.Success && m.Groups["fw"].Value.Equals("AspNetCore", StringComparison.OrdinalIgnoreCase) ? "ASP.NET Core" : ".NET";
            return v is null
                ? "This commit's tests need a .NET runtime that isn't installed. Install the one its test projects target (https://dotnet.microsoft.com/download), then prove again. stangent doesn't install anything itself."
                : $"This commit's tests need the {which} {v} runtime, which isn't installed. Install it from https://dotnet.microsoft.com/download/dotnet/{v} (or `dotnet-install.sh --channel {v} --runtime {(which == ".NET" ? "dotnet" : "aspnetcore")}`), then prove again. stangent doesn't install anything itself.";
        }
        if (reason.Contains("finds no tests at all", StringComparison.Ordinal) || reason.Contains("No test is available", StringComparison.Ordinal))
            return "The test project builds, but its runner lists no tests. Delete its build output (bin/obj, or a shared output folder the repo builds into) and rebuild; check its test adapter package matches its test framework. Then prove again.";
        if (Regex.IsMatch(reason, @"npx: (command )?not found|node: (command )?not found|env: node", RegexOptions.IgnoreCase))
            return "Node.js isn't on the PATH. Install Node.js, then prove again. stangent doesn't install anything itself.";
        if (reason.Contains("Executable doesn't exist", StringComparison.Ordinal) || reason.Contains("npx playwright install", StringComparison.Ordinal))
            return $"Playwright's browser isn't installed. Run `npx playwright install chromium` in `{playwrightDir ?? "the Playwright folder"}`, then prove again.";
        if (reason.Contains("tickets.app.build failed", StringComparison.Ordinal))
            return "The app didn't build. Run `tickets.app.build` yourself at this commit and fix what it reports, then prove again.";
        if (reason.Contains("tickets.app.start exited", StringComparison.Ordinal) || reason.Contains("didn't answer at", StringComparison.Ordinal))
            return "The app didn't start. Run `tickets.app.start` yourself and fix what it reports (its seed step, the port, a missing tool); it should stay in the foreground. Then prove again.";
        if (reason.Contains("tickets.app.seed failed", StringComparison.Ordinal))
            return "The app started, but its seed step failed. Run `tickets.app.seed` yourself and fix what it reports, then prove again.";
        if (reason.Contains("permission rules blocked", StringComparison.Ordinal))
            return "A stangent bug: its own permission rules blocked the reproducer. Prove again; if it happens again, report it with this ticket's bug-proof.json.";
        return "A setup problem, not the bug: fix what the reason names, then prove again.";
    }
}

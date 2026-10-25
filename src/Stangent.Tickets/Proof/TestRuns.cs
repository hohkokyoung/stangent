using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Stangent.Tickets.Proof;

public sealed record CommandResult(int ExitCode, string Output, bool TimedOut, long Ms);

/// <summary>Runs a build or test command. Tests use a fake; stangent uses the shell.</summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string command, string cwd, TimeSpan timeout, IReadOnlyDictionary<string, string>? env = null, CancellationToken ct = default);
    /// <summary>Starts a long-running process (the app for Playwright, D76); disposing it stops the whole process tree.</summary>
    IAsyncDisposable Start(string command, string cwd, IReadOnlyDictionary<string, string>? env = null);
}

/// <summary>Commands through <c>/bin/sh -c</c> (or <c>cmd /c</c>), output kept to its last 20,000 characters, the process tree killed on timeout.</summary>
public sealed class ShellRunner : ICommandRunner
{
    public const int KeepChars = 20_000;

    static ProcessStartInfo Info(string command, string cwd, IReadOnlyDictionary<string, string>? env)
    {
        var psi = OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd") : new ProcessStartInfo("/bin/sh");
        psi.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        psi.ArgumentList.Add(command);
        psi.WorkingDirectory = cwd;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        foreach (var (k, v) in env ?? new Dictionary<string, string>()) psi.Environment[k] = v;
        return psi;
    }

    public async Task<CommandResult> RunAsync(string command, string cwd, TimeSpan timeout, IReadOnlyDictionary<string, string>? env = null, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        using var p = Process.Start(Info(command, cwd, env))!;
        var output = new StringBuilder();
        void Keep(string? line)
        {
            if (line is null) return;
            lock (output)
            {
                output.Append(line).Append('\n');
                if (output.Length > KeepChars * 2) output.Remove(0, output.Length - KeepChars);
            }
        }
        p.OutputDataReceived += (_, e) => Keep(e.Data);
        p.ErrorDataReceived += (_, e) => Keep(e.Data);
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var timedOut = false;
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
        var text = output.ToString();
        return new CommandResult(timedOut ? -1 : p.ExitCode, text.Length > KeepChars ? text[^KeepChars..] : text, timedOut, watch.ElapsedMilliseconds);
    }

    public IAsyncDisposable Start(string command, string cwd, IReadOnlyDictionary<string, string>? env = null)
    {
        var p = Process.Start(Info(command, cwd, env))!;
        p.OutputDataReceived += (_, _) => { };
        p.ErrorDataReceived += (_, _) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return new Running(p);
    }

    sealed class Running(Process p) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            p.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>One test's result in a runner's report.</summary>
public sealed record TestCaseResult(string Name, string Status, string? Message, string? Stack);

/// <summary>Reads test reports by code: VSTest/MTP TRX for .NET, Jest and Vitest JSON, Playwright JSON.</summary>
public static partial class TestReports
{
    static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static List<TestCaseResult> ParseTrx(string xml)
    {
        var doc = XDocument.Parse(xml);
        return [.. doc.Descendants(Trx + "UnitTestResult").Select(r =>
        {
            var error = r.Element(Trx + "Output")?.Element(Trx + "ErrorInfo");
            var outcome = (string?)r.Attribute("outcome") ?? "";
            return new TestCaseResult((string?)r.Attribute("testName") ?? "", outcome switch
            {
                "Passed" => "passed", "Failed" or "Error" => "failed", "Timeout" => "timeout", _ => "skipped",
            }, error?.Element(Trx + "Message")?.Value, error?.Element(Trx + "StackTrace")?.Value);
        })];
    }

    /// <summary>Jest's <c>--json</c> and Vitest's JSON reporter share this shape.</summary>
    public static List<TestCaseResult> ParseJest(string json)
    {
        var root = JsonNode.Parse(json)!;
        return [.. (root["testResults"]?.AsArray() ?? []).SelectMany(f => f!["assertionResults"]?.AsArray() ?? []).Select(a =>
        {
            var messages = (a!["failureMessages"]?.AsArray() ?? []).Select(m => (string?)m).OfType<string>().ToList();
            var status = (string?)a["status"] switch { "passed" => "passed", "failed" => "failed", _ => "skipped" };
            return new TestCaseResult((string?)a["fullName"] ?? (string?)a["title"] ?? "", status, messages.FirstOrDefault(), messages.FirstOrDefault());
        })];
    }

    public static List<TestCaseResult> ParsePlaywright(string json)
    {
        var results = new List<TestCaseResult>();
        void Walk(JsonNode? suite, string prefix)
        {
            if (suite is null) return;
            var title = (string?)suite["title"];
            var name = string.IsNullOrEmpty(title) || title.EndsWith(".ts", StringComparison.Ordinal) || title.EndsWith(".js", StringComparison.Ordinal) ? prefix : (prefix.Length == 0 ? title : $"{prefix} {title}");
            foreach (var spec in suite["specs"]?.AsArray() ?? [])
                foreach (var test in spec!["tests"]?.AsArray() ?? [])
                    foreach (var r in test!["results"]?.AsArray() ?? [])
                    {
                        var status = (string?)r!["status"] switch { "passed" => "passed", "failed" => "failed", "timedOut" => "timeout", _ => "skipped" };
                        var error = r["error"];
                        results.Add(new TestCaseResult(name.Length == 0 ? (string)spec["title"]! : $"{name} {(string)spec["title"]!}", status,
                            (string?)error?["message"], (string?)error?["stack"] ?? (error?["location"] is { } loc ? $"at {(string?)loc["file"]}:{(int?)loc["line"]}:{(int?)loc["column"]}" : null)));
                    }
            foreach (var child in suite["suites"]?.AsArray() ?? []) Walk(child, name);
        }
        foreach (var s in JsonNode.Parse(json)!["suites"]?.AsArray() ?? []) Walk(s, "");
        return results;
    }

    // ---- what a failure was ----

    [GeneratedRegex(@"^\s*(?:[\w.]+\.)?(?<type>\w*(?:Exception|Error))\b")]
    private static partial Regex LeadingException();

    static readonly string[] AssertionMarks =
    [
        "Xunit.Sdk.", "Xunit.Assert", "Assert.", "NUnit.Framework.", "AssertionException", "AssertFailedException", "FluentAssertions",
        "Shouldly", "ShouldAssertException", "expect(", "AssertionError", "toBe", "toEqual", "toHave", "toMatch", "toContain", "toThrow",
    ];

    static readonly HashSet<string> AssertionTypes = ["AssertionError", "AssertionException", "AssertFailedException", "ShouldAssertException",
        "EqualException", "TrueException", "FalseException", "NullException", "NotNullException", "ContainsException", "XunitException", "JestAssertionError"];

    /// <summary>A failure on an assertion (D75), not a crash in setup or the code: an assertion library's mark in the
    /// message or stack, and the message doesn't start with another exception type.</summary>
    public static bool IsAssertion(TestCaseResult t)
    {
        var message = t.Message ?? "";
        var lead = LeadingException().Match(message);
        // Jest and Playwright report a failed expect as a plain "Error: expect(received)…"; TypeError and the like are crashes.
        var plainExpect = lead.Success && lead.Groups["type"].Value == "Error" && message.Contains("expect(", StringComparison.Ordinal);
        if (lead.Success && !plainExpect && !AssertionTypes.Contains(lead.Groups["type"].Value)) return false;
        var both = message + "\n" + (t.Stack ?? "");
        return AssertionMarks.Any(m => both.Contains(m, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"(?:in\s+(?<f>[^\n]+?):line\s+(?<l>\d+))|(?:\(?(?<f>[^\s()]+?):(?<l>\d+):\d+\)?)")]
    private static partial Regex Frame();

    /// <summary>The line in <paramref name="testFile"/> the failure points at: the first stack frame in that file.</summary>
    public static int? LineIn(TestCaseResult t, string testFile)
    {
        var name = Path.GetFileName(testFile);
        foreach (Match m in Frame().Matches(t.Stack ?? t.Message ?? ""))
        {
            var f = m.Groups["f"].Value.Replace('\\', '/');
            if (f.EndsWith("/" + testFile.Replace('\\', '/'), StringComparison.Ordinal) || Path.GetFileName(f) == name)
                return int.Parse(m.Groups["l"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }
}

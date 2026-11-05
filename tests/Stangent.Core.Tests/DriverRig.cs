using System.Diagnostics;

namespace Stangent.Core.Tests;

/// <summary>
/// Runs a bash driver with fake <c>claude</c> and <c>stangent</c> first on the path (D112). The environment is built from
/// nothing: the fake folder plus <c>/usr/bin:/bin</c>, a throwaway home, no API key. So no test can reach the real
/// <c>claude</c>, whatever the machine has. Every fake call lands in a log the test reads back.
/// </summary>
public sealed class DriverRig : IDisposable
{
    const string ClaudeBody = """
        #!/bin/bash
        printf 'claude %s\n' "$*" >>"$FAKE_LOG"
        printf 'claude-cwd %s\n' "$(pwd -P)" >>"$FAKE_LOG"
        cat >>"$FAKE_DIR/claude-stdin"
        if [[ " $* " == *" stream-json "* ]]; then echo '{"type":"assistant","message":{"id":"m1"}}'; fi
        cat "$FAKE_DIR/claude-answer"
        exit "$(cat "$FAKE_DIR/claude-exit" 2>/dev/null || echo 0)"
        """;

    public const string GoodAnswer = """{"type":"result","is_error":false,"structured_output":{"done":true}}""";

    public static string RepoRoot { get; } = FindRoot();

    public string Dir { get; } = Directory.CreateTempSubdirectory("stangent-driver").FullName;
    public string Work => Path.Combine(Dir, "work");
    public string Bin => Path.Combine(Dir, "bin");
    public Dictionary<string, string> Env { get; } = [];

    public DriverRig()
    {
        Directory.CreateDirectory(Bin);
        Directory.CreateDirectory(Work);
        Directory.CreateDirectory(Path.Combine(Dir, "home"));
        Directory.CreateDirectory(Path.Combine(Dir, "tmp"));
        WriteExecutable("claude", ClaudeBody.Replace("\r\n", "\n"));
        ClaudeAnswers(GoodAnswer);
        Stangent("");
    }

    public void Dispose() => Directory.Delete(Dir, true);

    /// <summary><see cref="Dir"/> as the shell's <c>pwd -P</c> sees it (macOS's temp folder is behind a link).</summary>
    public string RealPath(string path) => RunIn(path, WriteScript("pwd.sh", "pwd -P")).Out.Trim();

    public string WriteScript(string name, string text)
    {
        var path = Path.Combine(Dir, name);
        File.WriteAllText(path, text + "\n");
        return path;
    }

    /// <summary>The fake stangent: <paramref name="cases"/> is the body of a bash <c>case "$*" in</c>. Anything it doesn't
    /// name fails loudly, so a driver calling something unexpected shows up.</summary>
    public void Stangent(string cases) => WriteExecutable("stangent", $$$"""
        #!/bin/bash
        printf 'stangent %s\n' "$*" >>"$FAKE_LOG"
        case "$*" in
        {{{cases}}}
        "spec schema "*) echo '{"type":"object"}' ;;
        "spec assemble --in-place "*) ;;
        "spec assemble "*) cat "${@: -1}" ;;
        "run-check "*) echo '{"ok":true,"result":{"checked":true}}' ;;
        "cost estimate "*) echo '{"ok":true,"result":{"estimateUsd":null,"basis":"fake"}}' ;;
        *) echo "fake stangent: unexpected: $*" >&2; exit 99 ;;
        esac
        """.Replace("\r\n", "\n"));

    public void ClaudeAnswers(string answer, int exit = 0)
    {
        File.WriteAllText(Path.Combine(Dir, "claude-answer"), answer + "\n");
        File.WriteAllText(Path.Combine(Dir, "claude-exit"), exit.ToString());
    }

    public string ClaudeStdin => File.Exists(Path.Combine(Dir, "claude-stdin")) ? File.ReadAllText(Path.Combine(Dir, "claude-stdin")) : "";

    public IReadOnlyList<string> Calls() => File.Exists(Path.Combine(Dir, "log")) ? File.ReadAllLines(Path.Combine(Dir, "log")) : [];
    public IReadOnlyList<string> ClaudeCalls() => [.. Calls().Where(c => c.StartsWith("claude -p"))];
    public IReadOnlyList<string> StangentCalls() => [.. Calls().Where(c => c.StartsWith("stangent ") && !c.StartsWith("stangent cost ") && !c.StartsWith("stangent run-check ") && !c.StartsWith("stangent spec assemble "))];

    /// <summary>Writes a file under the work folder (or an absolute path).</summary>
    public string Put(string path, string text)
    {
        var full = Path.Combine(Work, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    /// <summary>Runs a driver by its path from the repo root, or an absolute path, in <see cref="Work"/> by default.</summary>
    public DriverResult Run(string script, params string[] args) => RunIn(Work, script, args);

    public DriverResult RunIn(string cwd, string script, params string[] args)
    {
        var psi = StartInfo("/bin/bash", cwd);
        psi.ArgumentList.Add(Path.IsPathRooted(script) ? script : Path.Combine(RepoRoot, script));
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{script} still running after two minutes");
        }
        // Not ReadToEnd's wait alone: a driver's leftover background job must not hang the test.
        Task.WaitAll([output, error], TimeSpan.FromSeconds(10));
        return new DriverResult(process.ExitCode, output.IsCompleted ? output.Result : "", error.IsCompleted ? error.Result : "");
    }

    /// <summary>Runs git in a folder with the rig's environment and returns its output.</summary>
    public string Git(string cwd, params string[] args)
    {
        var psi = StartInfo("/usr/bin/git", cwd);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        process.StandardInput.Close();
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)}: {error.Result}");
        return output;
    }

    /// <summary>A git repo in a new folder with one commit; returns its folder.</summary>
    public string NewRepo(string name, params (string Path, string Text)[] files)
    {
        var repo = Path.Combine(Dir, name);
        Directory.CreateDirectory(repo);
        Git(repo, "init", "-q");
        foreach (var (path, text) in files.Length > 0 ? files : [("README.md", "hello\n")])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo, path))!);
            File.WriteAllText(Path.Combine(repo, path), text);
        }
        Commit(repo);
        return repo;
    }

    public string Commit(string repo)
    {
        Git(repo, "add", "-A");
        Git(repo, "commit", "-q", "--allow-empty", "-m", "c");
        return Git(repo, "rev-parse", "HEAD");
    }

    ProcessStartInfo StartInfo(string file, string cwd)
    {
        var psi = new ProcessStartInfo(file)
        {
            WorkingDirectory = cwd, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        psi.Environment.Clear();
        psi.Environment["PATH"] = Bin + ":/usr/bin:/bin";
        psi.Environment["HOME"] = Path.Combine(Dir, "home");
        psi.Environment["TMPDIR"] = Path.Combine(Dir, "tmp");
        psi.Environment["FAKE_LOG"] = Path.Combine(Dir, "log");
        psi.Environment["FAKE_DIR"] = Dir;
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (var who in new[] { "AUTHOR", "COMMITTER" })
        {
            psi.Environment[$"GIT_{who}_NAME"] = "Test";
            psi.Environment[$"GIT_{who}_EMAIL"] = "test@example.invalid";
        }
        // A test about the hard stop sets or clears this itself (T.2).
        psi.Environment["STANGENT_MAX_COST"] = "none";
        foreach (var (key, value) in Env) psi.Environment[key] = value;
        return psi;
    }

    void WriteExecutable(string name, string text)
    {
        var path = Path.Combine(Bin, name);
        File.WriteAllText(path, text);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, (UnixFileMode)0b111_101_101);
    }

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Stangent.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Stangent.slnx not found above the test folder.");
    }
}

public sealed record DriverResult(int Exit, string Out, string Err);

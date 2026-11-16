using System.Diagnostics;

namespace Stangent.Tickets.Fix;

/// <summary>Where one ticket's fix loop keeps its files (D350): all under <c>.stangent/fix/&lt;KEY&gt;/</c>, git-ignored.</summary>
public sealed class FixPaths(string root, string key)
{
    public string Key { get; } = TicketKeys.Check(key);
    public string Root => root;
    public static string FixDir(string root) => Path.Combine(root, ".stangent", "fix");
    public string Dir => Path.Combine(FixDir(root), Key);
    public string Worktree => Path.Combine(Dir, "worktree");
    public string State => Path.Combine(Dir, "fix.json");
    public string Work => Path.Combine(Dir, "work");
    public string Input => Path.Combine(Work, "input.md");
    public string Output => Path.Combine(Work, "output.json");
    public string Checks => Path.Combine(Dir, "checks");
    public string Plan => Path.Combine(Dir, "task-plan.json");
    public string Report => Path.Combine(Dir, "report.md");
    public string AgentRun => Path.Combine(Dir, "agent-run.json");
    public string Branch => "stangent/fix-" + Key;
    public string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}

/// <summary>git, run with its arguments as a list (no shell), for the fix loop's branch and worktree.</summary>
static class Git
{
    public sealed record Result(int Exit, string Out, string Err)
    {
        public bool Ok => Exit == 0;
    }

    public static Result Run(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            return new Result(p.ExitCode, stdout.Result, stderr.Result);
        }
        catch (System.ComponentModel.Win32Exception e) { return new Result(127, "", e.Message); }
    }

    public static string Must(string cwd, params string[] args)
    {
        var r = Run(cwd, args);
        if (!r.Ok)
            throw new Stangent.Core.StangentException("git_failed", $"git {string.Join(' ', args)} failed: {r.Err.Trim()}", Stangent.Core.ExitCodes.Failed);
        return r.Out;
    }

    /// <summary>A commit with the repo's own identity, or stangent's when the repo has none (a fresh clone, CI).</summary>
    public static string Commit(string cwd, string message)
    {
        var named = Run(cwd, "config", "user.email").Out.Trim().Length > 0 && Run(cwd, "config", "user.name").Out.Trim().Length > 0;
        string[] args = named ? ["commit", "-q", "--no-verify", "-m", message] : ["-c", "user.name=stangent", "-c", "user.email=stangent@localhost", "commit", "-q", "--no-verify", "-m", message];
        Must(cwd, args);
        return Must(cwd, "rev-parse", "HEAD").Trim();
    }

    /// <summary>Paths from <c>git status --porcelain -z</c>, relative to the git root; a rename gives its new path.</summary>
    public static List<string> Changed(string cwd)
    {
        var parts = Must(cwd, "status", "--porcelain", "-z", "--untracked-files=all").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>();
        for (var i = 0; i < parts.Length; i++)
        {
            var entry = parts[i];
            if (entry.Length < 4) continue;
            list.Add(entry[3..]);
            if (entry[0] is 'R' or 'C') i++;   // the old path follows a rename or copy
        }
        return list;
    }
}

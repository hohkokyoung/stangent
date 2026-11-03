using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Stangent.Core.DataCheck;

/// <summary>
/// Keeps client data out of the repo (D56, D113): flags tracked documents, and lines holding a term from a terms file
/// kept outside the repo. Reads the current tree, not git history. Its output never holds a term or the text around one:
/// printing it would be its own leak. A path holding a term is hidden too, shown by its place in <c>git ls-files</c>.
/// </summary>
public static class ClientDataCheck
{
    public const string AllowFile = ".stangent-data-allow";
    public const string TermsVariable = "STANGENT_CLIENT_TERMS";
    const string HiddenPath = "(hidden: the path holds a term)";
    const int BinaryProbe = 8000;

    static readonly HashSet<string> DocumentTypes = new(StringComparer.OrdinalIgnoreCase)
        { ".docx", ".doc", ".pdf", ".xlsx", ".xls", ".pptx", ".ppt", ".msg", ".eml" };

    /// <summary>The check's report; <c>ok</c> is false on any finding.</summary>
    /// <param name="folder">Any folder in the repo.</param>
    /// <param name="termsOption">The path given as <c>--terms</c>, if any.</param>
    /// <param name="termsVariable">The value of <see cref="TermsVariable"/>, if any.</param>
    /// <param name="home">The user's home folder, for the default terms file.</param>
    public static JsonObject Run(string folder, string? termsOption, string? termsVariable, string home)
    {
        var root = Git(folder, "rev-parse", "--show-toplevel")?.TrimEnd('\n')
            ?? throw new StangentException("not_a_git_repo", "This check reads what git tracks, and this is not a git repo.", ExitCodes.Usage);
        var tracked = (Git(root, "ls-files", "-z") ?? "").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var terms = LoadTerms(root, termsOption, termsVariable, home);

        var findings = new List<JsonObject>();
        var hidden = new bool[tracked.Length];
        for (var i = 0; i < tracked.Length; i++)
            foreach (var (termLine, term) in terms)
                if (tracked[i].Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    hidden[i] = true;
                    findings.Add(new JsonObject { ["kind"] = "term-in-path", ["file"] = HiddenPath, ["fileIndex"] = i + 1, ["termLine"] = termLine });
                }
        JsonObject At(string kind, int i) => hidden[i]
            ? new JsonObject { ["kind"] = kind, ["file"] = HiddenPath, ["fileIndex"] = i + 1 }
            : new JsonObject { ["kind"] = kind, ["file"] = tracked[i] };

        var allow = LoadAllowList(root, tracked, findings, out var allowState);
        var allowedDocuments = 0;
        int scanned = 0, skippedBinary = 0, skippedOther = 0;
        for (var i = 0; i < tracked.Length; i++)
        {
            if (DocumentTypes.Contains(Path.GetExtension(tracked[i])))
            {
                if (allow.Contains(tracked[i])) allowedDocuments++;
                else findings.Add(At("document", i));
            }
            if (terms.Count == 0) continue;

            var path = Path.Combine(root, tracked[i]);
            var info = new FileInfo(path);
            // A deleted file, a submodule or a link: nothing of this repo's to read here.
            if (!info.Exists || info.LinkTarget is not null) { skippedOther++; continue; }
            try
            {
                if (LooksBinary(path)) { skippedBinary++; continue; }
                ScanLines(path, terms, (line, column, termLine) =>
                {
                    var finding = At("term", i);
                    finding["line"] = line;
                    finding["column"] = column;
                    finding["termLine"] = termLine;
                    findings.Add(finding);
                });
                scanned++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skippedOther++; }
        }

        return new JsonObject
        {
            ["ok"] = findings.Count == 0,
            ["termsChecked"] = terms.Count > 0,
            ["trackedFiles"] = tracked.Length,
            ["filesScanned"] = scanned,
            ["skippedBinary"] = skippedBinary,
            ["skippedOther"] = skippedOther,
            ["allowedDocuments"] = allowedDocuments,
            ["allowFile"] = allowState,
            ["findings"] = new JsonArray([.. findings.Select(f => (JsonNode)f)]),
        };
    }

    /// <summary>--terms, then the variable, then the default. A file someone named must exist: without it the run would
    /// pass having checked nothing. The default may be absent. A file with no terms checks nothing, like no file.</summary>
    static List<(int Line, string Term)> LoadTerms(string root, string? option, string? variable, string home)
    {
        var named = !string.IsNullOrEmpty(option) ? option : !string.IsNullOrEmpty(variable) ? variable : null;
        var path = Path.GetFullPath(named ?? Path.Combine(home, ".config", "stangent", "client-terms.txt"));
        if (!File.Exists(path))
            return named is null ? [] : throw new StangentException("terms_file_missing", "The terms file named doesn't exist.", ExitCodes.NotFound);
        if (InSameRepo(root, path))
            throw new StangentException("terms_file_in_repo", "The terms file is inside this repo. It names the clients, so keep it outside.", ExitCodes.Usage);

        var terms = new List<(int, string)>();
        var number = 0;
        foreach (var raw in File.ReadLines(path))
        {
            number++;
            var term = raw.Trim();
            if (term.Length > 0 && !term.StartsWith('#')) terms.Add((number, term));
        }
        return terms;
    }

    /// <summary>Asks git, which sees through links; another worktree of this repo counts as this repo.</summary>
    static bool InSameRepo(string root, string file)
    {
        var real = File.ResolveLinkTarget(file, returnFinalTarget: true)?.FullName ?? file;
        var theirs = Git(Path.GetDirectoryName(real)!, "rev-parse", "--path-format=absolute", "--git-common-dir");
        return theirs is not null && theirs == Git(root, "rev-parse", "--path-format=absolute", "--git-common-dir");
    }

    /// <summary>Lines of <c>&lt;path&gt; # &lt;reason&gt;</c>, exact paths only. Honoured only when committed or staged:
    /// otherwise it would pass here and fail in CI. A bad or stale entry is a finding, shown by its line alone.</summary>
    static HashSet<string> LoadAllowList(string root, string[] tracked, List<JsonObject> findings, out string state)
    {
        var allow = new HashSet<string>(StringComparer.Ordinal);
        var path = Path.Combine(root, AllowFile);
        if (!File.Exists(path)) { state = "absent"; return allow; }
        if (!tracked.Contains(AllowFile)) { state = "ignored: not tracked"; return allow; }
        state = "used";

        var number = 0;
        foreach (var raw in File.ReadLines(path))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var cut = line.IndexOf(" # ", StringComparison.Ordinal);
            var entry = cut < 0 ? line : line[..cut].Trim();
            var problem = cut < 0 || line[(cut + 3)..].Trim().Length == 0 ? "no reason given"
                : !tracked.Contains(entry) ? "not a tracked file"
                : null;
            if (problem is null) allow.Add(entry);
            else findings.Add(new JsonObject { ["kind"] = "allow-entry", ["file"] = AllowFile, ["line"] = number, ["problem"] = problem });
        }
        return allow;
    }

    /// <summary>As git decides: a NUL byte near the start.</summary>
    static bool LooksBinary(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[BinaryProbe];
        var read = stream.Read(head, 0, head.Length);
        return Array.IndexOf(head, (byte)0, 0, read) >= 0;
    }

    static void ScanLines(string path, List<(int Line, string Term)> terms, Action<int, int, int> found)
    {
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            foreach (var (termLine, term) in terms)
            {
                var at = line.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) found(number, at + 1, termLine);
            }
        }
    }

    static string? Git(string folder, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(psi)!;
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or DirectoryNotFoundException)
        {
            return null;
        }
    }
}

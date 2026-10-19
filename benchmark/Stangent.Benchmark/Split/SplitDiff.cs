using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Stangent.Benchmark.Split;

/// <summary>git's numbers for one zero-context hunk: <c>@@ -OldStart,OldCount +NewStart,NewCount @@</c>.</summary>
sealed record Hunk(int OldStart, int OldCount, int NewStart, int NewCount)
{
    /// <summary>0-based index of the first old line, or where new lines go in when nothing is removed.</summary>
    public int OldFrom => OldCount == 0 ? OldStart : OldStart - 1;
    public int NewFrom => NewCount == 0 ? NewStart : NewStart - 1;
}

/// <summary>
/// One file's change between the parent and the change (renames shown as a delete and an add). Lines are
/// the raw bytes of each line, newline included, so applying part of a change keeps BOMs and CRLFs.
/// </summary>
sealed record FileChange(string Path, char Status, bool Binary, IReadOnlyList<Hunk> Hunks, byte[][] OldLines, byte[][] NewLines)
{
    public static byte[][] SplitLines(byte[] bytes)
    {
        var lines = new List<byte[]>();
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] == (byte)'\n') { lines.Add(bytes[start..(i + 1)]); start = i + 1; }
        if (start < bytes.Length) lines.Add(bytes[start..]);
        return [.. lines];
    }

    public static string Text(byte[] line) => Encoding.UTF8.GetString(line).TrimEnd('\n', '\r');
}

/// <summary>Reads a commit's change against its first parent with git, as bytes.</summary>
static partial class SplitDiff
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", RegexOptions.Multiline)]
    private static partial Regex HunkHeader();

    /// <summary>Every changed file, newest side's path order; <paramref name="parent"/> is the first parent.</summary>
    public static List<FileChange> Read(string root, string parent, string change)
    {
        var files = new List<FileChange>();
        var status = Git(root, ["diff", "--name-status", "--no-renames", "-z", parent, change]);
        var parts = Encoding.UTF8.GetString(status).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var kind = parts[i][0];
            var path = parts[i + 1];
            var diff = Encoding.UTF8.GetString(Git(root, ["diff", "-U0", "--no-renames", "--no-color", "--no-ext-diff", parent, change, "--", path]));
            var binary = diff.Contains("\nBinary files ", StringComparison.Ordinal) || diff.StartsWith("Binary files ", StringComparison.Ordinal);
            var hunks = binary ? [] : HunkHeader().Matches(diff).Select(m => new Hunk(
                int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1,
                int.Parse(m.Groups[3].Value), m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 1)).ToList();
            var oldLines = kind == 'A' || binary ? [] : FileChange.SplitLines(Git(root, ["show", $"{parent}:{path}"]));
            var newLines = kind == 'D' || binary ? [] : FileChange.SplitLines(Git(root, ["show", $"{change}:{path}"]));
            files.Add(new FileChange(path, kind is 'A' or 'D' ? kind : 'M', binary, hunks, oldLines, newLines));
        }
        return [.. files.OrderBy(f => f.Path, StringComparer.Ordinal)];
    }

    /// <summary>Files changed, counting a rename once (the size filter's "≤ 15 files").</summary>
    public static int FileCount(string root, string parent, string change) =>
        Encoding.UTF8.GetString(Git(root, ["diff", "--name-only", "-M", "-z", parent, change])).Split('\0', StringSplitOptions.RemoveEmptyEntries).Length;

    public static byte[] Git(string root, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        using var buffer = new MemoryStream();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.BaseStream.CopyTo(buffer);
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', psi.ArgumentList)}: {stderr.Result.Trim()}");
        return buffer.ToArray();
    }
}

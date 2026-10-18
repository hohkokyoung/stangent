using System.Text.RegularExpressions;
using Stangent.Core;

namespace Stangent.Graph.Impact;

public sealed record ChangedLines(string File, int From, int To);

/// <summary>Turns <c>git diff</c> hunks into changed line ranges (new side of the diff).</summary>
public static partial class DiffMapper
{
    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@")]
    private static partial Regex Hunk();

    /// <summary><paramref name="range"/> is <c>base..head</c>, or a single ref meaning "that ref vs the working tree".</summary>
    public static List<ChangedLines> ChangedLines(string root, string range)
    {
        if (range.StartsWith('-') || range.Contains(' '))
            throw new StangentException("usage", $"'{range}' is not a git revision range.", ExitCodes.Usage);
        var output = Git.Run(root, $"diff --no-color --no-ext-diff -U0 {range}")
            ?? throw new StangentException("git_failed", $"git diff {range} failed in {root}.", ExitCodes.Failed);
        return Parse(output);
    }

    public static List<ChangedLines> Parse(string diff)
    {
        var result = new List<ChangedLines>();
        string? file = null;
        foreach (var line in diff.Split('\n'))
        {
            if (line.StartsWith("+++ ", StringComparison.Ordinal))
                file = line == "+++ /dev/null" ? null : line[4..].StartsWith("b/", StringComparison.Ordinal) ? line[6..] : line[4..];
            else if (file is not null && Hunk().Match(line) is { Success: true } m)
            {
                var start = int.Parse(m.Groups[1].Value);
                var count = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
                // A pure deletion (count 0) is anchored at the line it was removed after.
                result.Add(new ChangedLines(file, Math.Max(1, start), Math.Max(1, start + Math.Max(count, 1) - 1)));
            }
        }
        return result;
    }
}

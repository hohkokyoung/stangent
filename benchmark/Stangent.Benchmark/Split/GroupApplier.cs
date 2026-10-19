using System.Text.Json.Nodes;

namespace Stangent.Benchmark.Split;

/// <summary>
/// Applies one group alone onto the parent's working tree (D46): its pieces, the outside edits that go with
/// them and the shared files, nothing else; then puts the tree back. A state file in <c>.stangent/</c> lets
/// the next run put back a tree left half-applied by an interrupted one.
/// </summary>
sealed class GroupApplier(string root, string parent, string change)
{
    public const string StateFile = ".stangent/split-state.json";

    /// <summary>What each file's content becomes with only these pieces and whole files applied; null = deleted.</summary>
    public static Dictionary<string, byte[]?> Contents(IReadOnlyList<FileChange> files, IReadOnlyList<Piece> pieces, SplitGroup group, Func<FileChange, byte[]> newBytes)
    {
        var result = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            var all = pieces.Where(p => p.File == f.Path).OrderBy(p => p.Order).ToList();
            var chosen = all.Where(p => group.Pieces.Contains(p.Id)).ToList();
            if (group.WholeFiles.Contains(f.Path) || (chosen.Count == all.Count && all.Count > 0))
                result[f.Path] = f.Status == 'D' ? null : newBytes(f);
            else if (chosen.Count > 0)
                result[f.Path] = Assemble(f, chosen);
        }
        return result;
    }

    /// <summary>The parent's lines with the chosen pieces' replacements, in order.</summary>
    public static byte[] Assemble(FileChange f, IReadOnlyList<Piece> chosen)
    {
        using var output = new MemoryStream();
        var cursor = 0;
        foreach (var p in chosen)
        {
            if (p.OldFrom < cursor) throw new InvalidOperationException($"{f.Path}: pieces out of order");
            for (; cursor < p.OldFrom; cursor++) output.Write(f.OldLines[cursor]);
            for (var i = p.NewFrom; i < p.NewFrom + p.NewLen; i++) output.Write(f.NewLines[i]);
            cursor = p.OldFrom + p.OldLen;
        }
        for (; cursor < f.OldLines.Length; cursor++) output.Write(f.OldLines[cursor]);
        return output.ToArray();
    }

    /// <summary>Writes the group onto the tree, which must be at the parent. Returns the files it created.</summary>
    public List<string> Apply(IReadOnlyList<FileChange> files, IReadOnlyList<Piece> pieces, SplitGroup group)
    {
        var contents = Contents(files, pieces, group, NewBytes);
        var created = contents.Where(kv => kv.Value is not null && files.First(f => f.Path == kv.Key).Status == 'A').Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
        WriteState(created);
        foreach (var (path, bytes) in contents.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var full = Path.Combine(root, path);
            if (bytes is null) { File.Delete(full); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        return created;
    }

    byte[] NewBytes(FileChange f) => f.Binary ? SplitDiff.Git(root, ["show", $"{change}:{f.Path}"]) : [.. f.NewLines.SelectMany(l => l)];

    /// <summary>Puts the tree back at the parent: tracked files from git, created files removed.</summary>
    public void Restore(IEnumerable<string> created)
    {
        foreach (var path in created)
        {
            var full = Path.Combine(root, path);
            if (File.Exists(full)) File.Delete(full);
        }
        SplitDiff.Git(root, ["checkout", "-q", "-f", parent]);
        File.Delete(Path.Combine(root, StateFile));
    }

    void WriteState(IReadOnlyList<string> created)
    {
        var state = new JsonObject { ["parent"] = parent, ["created"] = new JsonArray([.. created.Select(c => (JsonNode)c)]) };
        Directory.CreateDirectory(Path.Combine(root, ".stangent"));
        File.WriteAllText(Path.Combine(root, StateFile), state.ToJsonString());
    }

    /// <summary>After an interrupted run: remove the files a half-applied group created. Returns whether there was one.</summary>
    public static bool RecoverInterrupted(string root)
    {
        var path = Path.Combine(root, StateFile);
        if (!File.Exists(path)) return false;
        var state = JsonNode.Parse(File.ReadAllText(path))!;
        foreach (var c in state["created"]!.AsArray())
            if (File.Exists(Path.Combine(root, (string)c!))) File.Delete(Path.Combine(root, (string)c!));
        File.Delete(path);
        return true;
    }
}

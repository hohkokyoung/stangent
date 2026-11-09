using System.Text.Json;

namespace Stangent.Flows;

/// <summary>
/// A click flow's name from its button's text key, through the app's English translation file (D176 item 2): every <c>en.json</c>,
/// <c>en-US.json</c> or <c>en_US.json</c> in the repo (outside build output and packages), flat or nested, read in path order;
/// the first file holding the key wins.
/// </summary>
internal sealed class Translations
{
    static readonly HashSet<string> Names = new(["en.json", "en-us.json", "en_us.json"], StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> Skip = new(["node_modules", ".git", "bin", "obj", "dist", ".stangent", "specs", "artifacts"], StringComparer.OrdinalIgnoreCase);
    readonly string root;
    List<Dictionary<string, string>>? files;

    public Translations(string root) => this.root = root;

    /// <summary>The words for the first of these <c>|</c>-separated keys any file holds; null when none does.</summary>
    public string? Text(string keys)
    {
        if (keys == "") return null;
        files ??= Load();
        foreach (var key in keys.Split('|'))
            foreach (var f in files)
                if (f.TryGetValue(key, out var text)) return text;
        return null;
    }

    List<Dictionary<string, string>> Load()
    {
        var found = new List<string>();
        void Walk(string dir)
        {
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir).Order(StringComparer.Ordinal).ToList(); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            foreach (var e in entries)
            {
                var name = Path.GetFileName(e);
                if (Directory.Exists(e)) { if (!Skip.Contains(name)) Walk(e); }
                else if (Names.Contains(name)) found.Add(e);
            }
        }
        Walk(root);
        var result = new List<Dictionary<string, string>>();
        foreach (var path in found.OrderBy(p => Path.GetRelativePath(root, p).Replace('\\', '/'), StringComparer.Ordinal))
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                Flatten(doc.RootElement, "", map);
            }
            catch (JsonException) { continue; }
            result.Add(map);
        }
        return result;
    }

    static void Flatten(JsonElement e, string prefix, Dictionary<string, string> map)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject()) Flatten(p.Value, prefix == "" ? p.Name : $"{prefix}.{p.Name}", map);
        else if (e.ValueKind == JsonValueKind.String) map.TryAdd(prefix, e.GetString()!);
    }
}

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>
/// Names matched across the two sides by code (D53): case and word order normalised, plurals and "-ing" folded. What is left of the
/// old link route's candidate finder (cut by D342): ticket starting points read pages' names with it (D70).
/// </summary>
public static partial class NameWords
{
    static readonly HashSet<string> Stop =
    [
        "a", "an", "the", "of", "for", "to", "and", "or", "in", "on", "with", "by", "from", "at", "as", "is", "are", "be", "this",
        "that", "it", "its", "page", "screen", "view", "api", "v1", "v2", "v3", "v4", "v5", "index", "modal", "content",
        "component", "get", "post", "put", "delete", "any", "head", "id", "new", "s",
    ];

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex CamelBreak();

    /// <summary>Normalised words: camelCase split, lower case, plural and -ing folded, route parameters and stop words dropped.</summary>
    public static HashSet<string> Words(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var s = Regex.Replace(text, @"[:{][A-Za-z0-9_]+\}?", " ");
        s = CamelBreak().Replace(s, " ");
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(s.ToLowerInvariant(), "[a-z0-9]+"))
        {
            var w = Fold(m.Value);
            if (w.Length > 0 && !Stop.Contains(w)) words.Add(w);
        }
        return words;
    }

    static string Fold(string w)
    {
        if (w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal)) w = w[..^3];
        if (w.Length > 4 && w.EndsWith("ies", StringComparison.Ordinal)) return w[..^3] + "y";
        if (w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal)) return w[..^1];
        return w;
    }

    /// <summary>The names a page goes by: its route or entry point, its routed components, and its titles.</summary>
    public static List<HashSet<string>> PageNames(PageDef page, JsonObject? recorded)
    {
        var names = new List<HashSet<string>>();
        var parts = page.Title.Split(" · ");
        names.Add(Words(parts[0].Contains(' ') && page.Kind == PageKind.Backend ? parts[0][(parts[0].IndexOf(' ') + 1)..] : parts[0]));
        if (parts.Length > 1) names.AddRange(parts[1].Split(", ").Select(Words));
        if ((string?)recorded?["title"] is { } title) names.Add(Words(title));
        return [.. names.Where(n => n.Count > 0)];
    }
}

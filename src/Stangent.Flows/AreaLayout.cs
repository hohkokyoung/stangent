using Stangent.Graph;

namespace Stangent.Flows;

/// <summary>
/// Where each flow's files sit (D230, D232). Without <c>specs/areas.yaml</c>, every flow in <c>specs/flows/</c>. With it, a start flow in
/// <c>specs/areas/&lt;slug&gt;/</c> of its area; one tied or in no area in <c>specs/areas/unplaced/</c>; a shared flow with the one folder
/// whose start flows reach it, else in <c>specs/areas/shared/</c>. The same flows and <c>areas.yaml</c> give the same layout.
/// </summary>
public sealed class AreaLayout
{
    public const string Root = "specs/areas";
    public const string SharedSlug = "shared";
    public const string UnplacedSlug = "unplaced";
    public const string Page = "page.md";
    static readonly StringComparer O = StringComparer.Ordinal;

    /// <summary>Flow id → the folder its <c>.json</c> and <c>.md</c> sit in, under the repo.</summary>
    public SortedDictionary<string, string> FolderOf { get; } = new(O);
    /// <summary>Area name → its folder's slug (empty when flat).</summary>
    public SortedDictionary<string, string> SlugOf { get; } = new(O);
    /// <summary>Start flows in <c>unplaced/</c>: a tie still open, or no area holds them.</summary>
    public SortedSet<string> NotPlaced { get; } = new(O);
    public bool ByArea { get; private init; }
    public Placement? Placement { get; private init; }

    public string File(string id) => $"{FolderOf[id]}/{id}.json";
    public string Markdown(string id) => $"{FolderOf[id]}/{id}.md";
    public string AreaFolder(string area) => $"{Root}/{SlugOf[area]}";

    public static AreaLayout Flat(IEnumerable<string> ids)
    {
        var layout = new AreaLayout();
        foreach (var id in ids) layout.FolderOf[id] = FlowFiles.Folder;
        return layout;
    }

    /// <summary>The layout for flows just built: by area when <c>specs/areas.yaml</c> exists, else flat.</summary>
    public static AreaLayout For(CodeGraph graph, string root, FlowSet set)
    {
        var yaml = Path.Combine(root, AreaFiles.Confirmed);
        return System.IO.File.Exists(yaml) ? ByAreas(Areas.Of(graph, root, set), AreaFiles.Parse(System.IO.File.ReadAllText(yaml))) : Flat(set.Flows.Select(f => f.Id));
    }

    /// <summary>D230, D232: start flows by their area; shared flows with their one user folder, else <c>shared/</c>.</summary>
    public static AreaLayout ByAreas(Areas a, AreasFile file)
    {
        var placement = a.Place(file);
        var layout = new AreaLayout { ByArea = true, Placement = placement };
        foreach (var (name, slug) in Slugs(file.Areas.Select(x => x.Name))) layout.SlugOf[name] = slug;
        foreach (var id in a.Starts.Keys)
        {
            if (placement.AreaOf.TryGetValue(id, out var area) && layout.SlugOf.ContainsKey(area)) layout.FolderOf[id] = layout.AreaFolder(area);
            else { layout.FolderOf[id] = $"{Root}/{UnplacedSlug}"; layout.NotPlaced.Add(id); }
        }
        // A shared flow's users: the folders of the start flows reaching it, an unplaced start counting as its folder.
        var users = new Dictionary<string, SortedSet<string>>(O);
        foreach (var id in a.Starts.Keys)
            foreach (var s in a.Reach(id, sharedOnly: true))
                (users.TryGetValue(s, out var set) ? set : users[s] = new SortedSet<string>(O)).Add(layout.FolderOf[id]);
        foreach (var id in a.Flows.Keys.Where(id => !layout.FolderOf.ContainsKey(id)))
            layout.FolderOf[id] = users.TryGetValue(id, out var u) && u.Count == 1 ? u.Min! : $"{Root}/{SharedSlug}";
        return layout;
    }

    /// <summary>
    /// D232: an area's folder is its name as a flow id is made (D176: ASCII letters and digits lowercased, the rest one dash;
    /// <c>Settings › Indexers</c> → <c>settings-indexers</c>), <c>area</c> if nothing is left. Two names giving one slug: the first in
    /// ordinal order keeps it, the others take <c>-2</c>, <c>-3</c>; <c>shared</c> and <c>unplaced</c> are never an area's.
    /// </summary>
    public static SortedDictionary<string, string> Slugs(IEnumerable<string> names)
    {
        var list = names.Distinct(O).Order(O).ToList();
        var bases = list.ToDictionary(n => n, n => FlowBuilder.Slug(n) is { Length: > 0 } s ? s : "area", O);
        var used = new HashSet<string>([SharedSlug, UnplacedSlug], O);
        var result = new SortedDictionary<string, string>(O);
        foreach (var n in list) if (used.Add(bases[n])) result[n] = bases[n];
        foreach (var n in list.Where(n => !result.ContainsKey(n)))
            for (var i = 2; ; i++)
                if (used.Add($"{bases[n]}-{i}")) { result[n] = $"{bases[n]}-{i}"; break; }
        return result;
    }

    /// <summary>A relative link from a folder to a file, both under the repo, with <c>/</c> on every system.</summary>
    public static string Relative(string fromDir, string toPath)
    {
        var from = fromDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var to = toPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var common = 0;
        while (common < from.Length && common < to.Length - 1 && from[common] == to[common]) common++;
        return string.Join('/', Enumerable.Repeat("..", from.Length - common).Concat(to.Skip(common)));
    }
}

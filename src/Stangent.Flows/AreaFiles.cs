using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Stangent.Flows;

/// <summary>
/// Areas on disk (D180, D225): <c>specs/areas.proposed.yaml</c> (written by <c>areas propose</c>, never <c>areas.yaml</c>),
/// <c>specs/areas.yaml</c> (the person's, read by <c>areas check</c>) and the code-made <c>specs/areas.json</c> (contract <c>areas</c> v1).
/// </summary>
public static class AreaFiles
{
    public const string Contract = "areas";
    public const int Version = 1;
    public const string Proposed = "specs/areas.proposed.yaml";
    public const string Confirmed = "specs/areas.yaml";
    public const string Json = "specs/areas.json";
    static readonly StringComparer O = StringComparer.Ordinal;

    // ---- propose ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// D218–D226: menu areas (each kept menu's top-level items, same names merged), homes no menu reaches joined to the menu area holding
    /// most of their screens or else areas of their own, App frame, then backend folders for jobs no majority placed; areas over 40 split by
    /// their menu children (menu areas) or their next folder (folder areas), else flagged.
    /// </summary>
    public static (AreasFile File, Placement Placement) Propose(Areas a)
    {
        var specs = new List<AreaSpec>();
        var items = new Dictionary<AreaSpec, List<MenuItemInfo>>();
        var kind = new Dictionary<AreaSpec, string>();
        AreaSpec Get(string name, string k)
        {
            var found = specs.FirstOrDefault(x => x.Name == name);
            if (found is not null) return found;
            var spec = new AreaSpec { Name = name };
            specs.Add(spec);
            kind[spec] = k;
            items[spec] = [];
            return spec;
        }
        string Unique(string name, string path) => specs.Any(x => x.Name == name) ? $"{name} ({path})" : name;

        foreach (var top in a.TopItems)
        {
            var spec = Get(Areas.AreaName(top), "menu");
            items[spec].Add(top);
            foreach (var addr in top.Subtree().Select(i => i.Address).Where(x => x != "")) if (!spec.Menu.Contains(addr, O)) spec.Menu.Add(addr);
        }
        // Homes: a home's screens on a menu item join it to that item's area (the one holding most of them); else it's an area itself.
        var topOf = a.TopItems.SelectMany(t => t.Subtree().Select(i => (Item: i, Top: t))).ToDictionary(x => x.Item, x => x.Top);
        foreach (var (home, name) in a.Homes)
        {
            var votes = a.Screens.Values.Where(s => s.Home == home && s.Item is not null).GroupBy(s => Areas.AreaName(topOf[s.Item!]))
                .Select(x => (Area: x.Key, Count: x.Count())).OrderByDescending(x => x.Count).ThenBy(x => specs.FindIndex(sp => sp.Name == x.Area)).ToList();
            var spec = votes.Count > 0 ? specs.First(sp => sp.Name == votes[0].Area) : Get(Unique(name, home), "folder");
            if (!spec.Folders.Contains(home, O)) spec.Folders.Add(home);
        }
        var frame = a.Starts.Values.SelectMany(s => s.Places).Where(p => p.Frame).Select(p => p.Folder).Distinct(O).Order(O).ToList();
        AreaSpec? frameSpec = null;
        if (frame.Count > 0)
        {
            frameSpec = new AreaSpec { Name = Areas.FrameName };
            frameSpec.Folders.AddRange(frame);
            kind[frameSpec] = "frame";
            items[frameSpec] = [];
        }
        List<AreaSpec> All() => frameSpec is null ? specs : [.. specs, frameSpec];

        // Jobs no majority placed: their backend folder, an area of its own unless one already holds it.
        bool Backends(Placement p)
        {
            var added = false;
            foreach (var s in a.Starts.Values.Where(s => s.BackendFolder is not null && p.Uncovered.Contains(s.Id)).OrderBy(s => s.BackendFolder, O))
                if (!specs.Any(x => x.Folders.Contains(s.BackendFolder!, O))) { Get(Unique(s.BackendName!, s.BackendFolder!), "folder").Folders.Add(s.BackendFolder!); added = true; }
            return added;
        }
        var file = new AreasFile { Areas = All() };
        var placement = a.Place(file);
        Backends(placement);

        // Splits (D221, D224, D226).
        var large = new List<Large>();
        var done = new HashSet<AreaSpec>();
        for (var changed = true; changed;)
        {
            changed = false;
            file = new AreasFile { Areas = All() };
            placement = a.Place(file);
            if (Backends(placement)) { changed = true; continue; }
            var counts = placement.AreaOf.Values.GroupBy(x => x, O).ToDictionary(x => x.Key, x => x.Count(), O);
            foreach (var spec in All().ToList())
            {
                if (counts.GetValueOrDefault(spec.Name) <= Areas.Big || done.Contains(spec)) continue;
                done.Add(spec);
                var at = specs.IndexOf(spec);
                if (kind[spec] == "menu" && items[spec].Any(i => i.Children.Count > 0))
                {
                    var subs = new List<AreaSpec>();
                    foreach (var child in items[spec].SelectMany(i => i.Children))
                    {
                        var sub = specs.FirstOrDefault(x => x.Name == $"{spec.Name} › {child.Name}") ?? new AreaSpec { Name = $"{spec.Name} › {child.Name}" };
                        if (!subs.Contains(sub)) { subs.Add(sub); kind[sub] = "menu"; items[sub] = []; }
                        items[sub].Add(child);
                        foreach (var addr in child.Subtree().Select(i => i.Address).Where(x => x != ""))
                        {
                            spec.Menu.Remove(addr);
                            if (!sub.Menu.Contains(addr, O)) sub.Menu.Add(addr);
                        }
                    }
                    // A home folder most of whose menu screens sit in one part moves to it (Series/ to Media › Series).
                    foreach (var home in spec.Folders.ToList())
                    {
                        var onItems = a.Screens.Values.Where(sc => sc.Home == home && sc.Item?.Address is { Length: > 0 }).ToList();
                        var best = subs.Select(sub => (Sub: sub, Count: onItems.Count(sc => sub.Menu.Contains(sc.Item!.Address, O)))).OrderByDescending(x => x.Count).FirstOrDefault();
                        if (best.Sub is not null && best.Count * 2 > onItems.Count) { spec.Folders.Remove(home); best.Sub.Folders.Add(home); }
                    }
                    specs.InsertRange(at + 1, subs);
                    changed = true;
                    break;
                }
                if (kind[spec] == "folder" && at >= 0)
                {
                    var flows = placement.AreaOf.Where(x => x.Value == spec.Name).Select(x => a.Starts[x.Key]).ToList();
                    var deeper = new SortedSet<string>(O);
                    foreach (var f in spec.Folders)
                        foreach (var dir in flows.SelectMany(PlaceFolders))
                            if (dir.StartsWith(f + "/", StringComparison.Ordinal)) deeper.Add(f + "/" + dir[(f.Length + 1)..].Split('/')[0]);
                    if (deeper.Count >= 2)
                    {
                        var subs = deeper.Select(d => new AreaSpec { Name = $"{spec.Name} › {d[(d.LastIndexOf('/') + 1)..]}", Folders = { d } }).ToList();
                        foreach (var sub in subs) { kind[sub] = "folder"; items[sub] = []; }
                        specs.InsertRange(at + 1, subs);
                        changed = true;
                        break;
                    }
                }
            }
        }
        // A part a split left with no flows folds back into its parent (Settings › Metadata Source into Settings).
        var flowCount = placement.AreaOf.Values.GroupBy(x => x, O).ToDictionary(x => x.Key, x => x.Count(), O);
        foreach (var part in specs.ToList().AsEnumerable().Reverse())
        {
            if (flowCount.GetValueOrDefault(part.Name) > 0) continue;
            var parent = specs.Where(p => p != part && part.Name.StartsWith(p.Name + " › ", StringComparison.Ordinal)).OrderByDescending(p => p.Name.Length).FirstOrDefault();
            if (parent is null) continue;
            foreach (var m in part.Menu) if (!parent.Menu.Contains(m, O)) parent.Menu.Add(m);
            foreach (var f in part.Folders) if (!parent.Folders.Contains(f, O)) parent.Folders.Add(f);
            specs.Remove(part);
        }
        // An area no flow sits in (a display-only screen's home, a heading whose parts took everything) isn't proposed.
        foreach (var empty in specs.Where(x => flowCount.GetValueOrDefault(x.Name) == 0).ToList()) specs.Remove(empty);
        placement = a.Place(new AreasFile { Areas = All() });
        var final = placement.AreaOf.Values.GroupBy(x => x, O).ToDictionary(x => x.Key, x => x.Count(), O);
        foreach (var spec in All())
            if (final.GetValueOrDefault(spec.Name) > Areas.Big) large.Add(new Large(spec.Name, final[spec.Name], "over 40, nothing to split by"));
        var ties = placement.Open.Select(t => new Tie(t.Key, t.Value, "")).ToList();
        return (new AreasFile { Areas = All(), Large = large, Ties = ties }, placement);

        IEnumerable<string> PlaceFolders(StartFlow s) => s.Places.Count > 0 ? s.Places.Select(p => p.Folder) : s.Backend is null ? [] : [s.Backend];
    }

    // ---- areas.yaml ------------------------------------------------------------------------------------------------------------

    static string Q(string s) => $"'{s.Replace("'", "''")}'";

    /// <summary><c>areas.yaml</c>'s text: fixed order and quoting, so the same areas give the same bytes; each area's flow count as a comment.</summary>
    public static string Render(AreasFile file, string commit, Placement placement)
    {
        var counts = placement.AreaOf.Values.GroupBy(x => x, O).ToDictionary(x => x.Key, x => x.Count(), O);
        var sb = new StringBuilder();
        sb.Append($"# Areas proposed by `stangent areas propose` (D218–D228) from commit {commit}.\n");
        sb.Append("# Copy to specs/areas.yaml and confirm it there: rename an area (`name`), merge two by listing both sources under one name,\n");
        sb.Append("# split one by giving a menu address or a deeper folder its own area, and answer each tie with one of its `choose`.\n");
        sb.Append("# The deepest match wins; new flows land by themselves. `stangent areas check` reads specs/areas.yaml.\n");
        sb.Append("areas:\n");
        foreach (var a in file.Areas)
        {
            var n = counts.GetValueOrDefault(a.Name);
            sb.Append($"- name: {Q(a.Name)}  # {n} {(n == 1 ? "flow" : "flows")}\n");
            if (a.Menu.Count > 0) { sb.Append("  menu:\n"); foreach (var m in a.Menu) sb.Append($"  - {Q(m)}\n"); }
            if (a.Folders.Count > 0) { sb.Append("  folders:\n"); foreach (var f in a.Folders) sb.Append($"  - {Q(f)}\n"); }
        }
        sb.Append(file.Large.Count == 0 ? "large: []\n" : "large:\n");
        foreach (var l in file.Large) sb.Append($"- area: {Q(l.Area)}\n  flows: {l.Flows}\n  why: {Q(l.Why)}\n");
        sb.Append(file.Ties.Count == 0 ? "ties: []\n" : "ties:\n");
        foreach (var t in file.Ties)
        {
            sb.Append($"- flow: {Q(t.Flow)}\n  choose:\n");
            foreach (var c in t.Choose) sb.Append($"  - {Q(c)}\n");
            sb.Append($"  answer: {Q(t.Answer)}\n");
        }
        return sb.ToString();
    }

    sealed class YamlFile
    {
        public List<YamlArea>? Areas { get; set; }
        public List<YamlLarge>? Large { get; set; }
        public List<YamlTie>? Ties { get; set; }
    }
    sealed class YamlArea { public string? Name { get; set; } public List<string>? Menu { get; set; } public List<string>? Folders { get; set; } }
    sealed class YamlLarge { public string? Area { get; set; } public int Flows { get; set; } public string? Why { get; set; } }
    sealed class YamlTie { public string? Flow { get; set; } public List<string>? Choose { get; set; } public string? Answer { get; set; } }

    public static AreasFile Parse(string text)
    {
        YamlFile y;
        try
        {
            y = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build().Deserialize<YamlFile>(text) ?? new YamlFile();
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            throw new StangentException("invalid_areas", $"{Confirmed} isn't valid: {e.Message}", ExitCodes.Failed);
        }
        return new AreasFile
        {
            Areas = [.. (y.Areas ?? []).Select(a => new AreaSpec { Name = a.Name ?? "", Menu = a.Menu ?? [], Folders = a.Folders ?? [] })],
            Large = [.. (y.Large ?? []).Select(l => new Large(l.Area ?? "", l.Flows, l.Why ?? ""))],
            Ties = [.. (y.Ties ?? []).Select(t => new Tie(t.Flow ?? "", t.Choose ?? [], t.Answer ?? ""))],
        };
    }

    // ---- check -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// D225: every start flow in exactly one area; menu items and folders no area covers; ties unanswered and answers whose flow is gone
    /// or that name no area; an address or folder two areas list. Returns the report and <c>areas.json</c>.
    /// </summary>
    public static (JsonObject Report, JsonObject AreasJson) Check(Areas a, AreasFile file)
    {
        var problems = new List<string>();
        foreach (var dup in file.Areas.GroupBy(x => x.Name, O).Where(x => x.Count() > 1)) problems.Add($"area '{dup.Key}' is listed {dup.Count()} times");
        foreach (var x in file.Areas.Where(x => x.Name.Trim() == "")) problems.Add("an area has no name");
        foreach (var dup in file.Areas.SelectMany(x => x.Menu.Distinct(O).Select(m => (m, x.Name))).GroupBy(x => x.m, O).Where(x => x.Count() > 1))
            problems.Add($"menu address '{dup.Key}' is in {string.Join(" and ", dup.Select(d => $"'{d.Name}'"))}");
        foreach (var dup in file.Areas.SelectMany(x => x.Folders.Distinct(O).Select(f => (f, x.Name))).GroupBy(x => x.f, O).Where(x => x.Count() > 1))
            problems.Add($"folder '{dup.Key}' is in {string.Join(" and ", dup.Select(d => $"'{d.Name}'"))}");

        var placement = a.Place(file);
        foreach (var id in placement.Uncovered) problems.Add($"start flow '{id}' is in no area");
        foreach (var (id, choose) in placement.Open) problems.Add($"tie unanswered: '{id}' could be {string.Join(", ", choose.Select(c => $"'{c}'"))}");
        var names = file.Areas.Select(x => x.Name).ToHashSet(O);
        foreach (var t in file.Ties)
        {
            if (!a.Starts.ContainsKey(t.Flow)) problems.Add($"tie answered for '{t.Flow}', which is no longer a start flow");
            else if (t.Answer != "" && !names.Contains(t.Answer)) problems.Add($"tie for '{t.Flow}' answers '{t.Answer}', which is no area");
        }
        // Menu items and folders a start flow sits on that no area lists (the flow may still have landed through its other places).
        var listed = file.Areas.SelectMany(x => x.Menu).ToHashSet(O);
        var used = a.Starts.Values.SelectMany(s => s.Places).ToList();
        foreach (var m in used.Where(p => p.Address is not null && !listed.Contains(p.Address)).Select(p => p.Address!).Distinct(O).Order(O))
            problems.Add($"menu item '{m}' is in no area");
        var folders = file.Areas.SelectMany(x => x.Folders).ToList();
        bool Covered(string dir) => folders.Any(f => dir == f || dir.StartsWith(f + "/", StringComparison.Ordinal));
        var needed = used.Where(p => p.Address is null || !listed.Contains(p.Address)).Select(p => a.HomeOf(p.Folder) ?? p.Folder)
            .Concat(a.Starts.Values.Where(s => s.BackendFolder is not null && placement.Uncovered.Contains(s.Id)).Select(s => s.BackendFolder!));
        foreach (var f in needed.Where(f => !Covered(f)).Distinct(O).Order(O)) problems.Add($"folder '{f}' is in no area");

        var json = AreasJson(a, file, placement);
        var counts = placement.AreaOf.Values.GroupBy(x => x, O).ToDictionary(x => x.Key, x => x.Count(), O);
        var report = new JsonObject
        {
            ["ok"] = problems.Count == 0,
            ["commit"] = a.Commit,
            ["file"] = Confirmed,
            ["startFlows"] = a.Starts.Count,
            ["placed"] = placement.AreaOf.Count,
            ["areas"] = new JsonArray([.. file.Areas.Select(x => (JsonNode)new JsonObject { ["name"] = x.Name, ["flows"] = counts.GetValueOrDefault(x.Name) })]),
            ["over40"] = new JsonArray([.. file.Areas.Where(x => counts.GetValueOrDefault(x.Name) > Areas.Big).Select(x => (JsonNode)x.Name)]),
            ["problems"] = new JsonArray([.. problems.Select(p => (JsonNode)p)]),
        };
        return (report, json);
    }

    /// <summary>The code-made <c>areas.json</c> (D225): every start flow's area, and per area the shared flows its starts reach (D218).</summary>
    public static JsonObject AreasJson(Areas a, AreasFile file, Placement placement) => new()
    {
        ["contract"] = Contract, ["version"] = Version, ["commit"] = a.Commit,
        ["areas"] = new JsonArray([.. file.Areas.Select(x =>
        {
            var flows = placement.AreaOf.Where(f => f.Value == x.Name).Select(f => f.Key).Order(O).ToList();
            var shared = flows.SelectMany(f => a.Reach(f, sharedOnly: true)).Distinct(O).Order(O).ToList();
            return (JsonNode)new JsonObject
            {
                ["name"] = x.Name,
                ["flows"] = new JsonArray([.. flows.Select(f => (JsonNode)f)]),
                ["shared"] = new JsonArray([.. shared.Select(f => (JsonNode)f)]),
            };
        })]),
        ["flows"] = new JsonObject([.. placement.AreaOf.Select(f => KeyValuePair.Create(f.Key, (JsonNode?)f.Value))]),
        ["open"] = new JsonObject([.. placement.Open.Select(f => KeyValuePair.Create(f.Key, (JsonNode?)new JsonArray([.. f.Value.Select(c => (JsonNode)c)])))]),
        ["unplaced"] = new JsonArray([.. placement.Uncovered.Select(f => (JsonNode)f)]),
    };

    public static void WriteText(string root, string rel, string text)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && File.ReadAllText(path) == text) return;
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}

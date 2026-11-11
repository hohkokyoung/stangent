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
        // Homes: a home's screens on menu items join it to the area holding more than half of them; else it's an area itself.
        var topOf = a.TopItems.SelectMany(t => t.Subtree().Select(i => (Item: i, Top: t))).ToDictionary(x => x.Item, x => x.Top);
        foreach (var (home, name) in a.Homes)
        {
            var votes = a.Screens.Values.Where(s => s.Home == home && s.Item is not null).GroupBy(s => Areas.AreaName(topOf[s.Item!]))
                .Select(x => (Area: x.Key, Count: x.Count())).OrderByDescending(x => x.Count).ThenBy(x => specs.FindIndex(sp => sp.Name == x.Area)).ToList();
            // D229: only an area holding more than half of them; a home split between menu areas stays an area of its own.
            var spec = votes.Count > 0 && votes[0].Count * 2 > votes.Sum(v => v.Count) ? specs.First(sp => sp.Name == votes[0].Area) : Get(Unique(name, home), "folder");
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

    /// <summary>
    /// D234: a proposal built on a confirmed <c>areas.yaml</c>: its areas as the person left them (names, merges, splits, areas added by
    /// hand) and its tie answers, carried while the flow still ties and the answer is still a choice; then what code finds new, by the
    /// same rules as a fresh proposal and with the answers applied: a menu address no area lists goes to the area listing its nearest
    /// listed parent item, else an area of its own top item's name; a home or frame folder no area covers is an area of its own; a job or
    /// handler in no area adds its backend folder (D223). Nothing is split. Returns the file, its placement, what was added and the
    /// answers dropped.
    /// </summary>
    public static (AreasFile File, Placement Placement, List<string> Added, List<string> Dropped) Extend(Areas a, AreasFile confirmed)
    {
        var specs = confirmed.Areas.Select(x => new AreaSpec { Name = x.Name, Menu = [.. x.Menu], Folders = [.. x.Folders] }).ToList();
        var added = new List<string>();
        AreaSpec Named(string name)
        {
            var spec = specs.FirstOrDefault(x => x.Name == name);
            if (spec is null) { specs.Add(spec = new AreaSpec { Name = name }); added.Add($"area '{name}'"); }
            return spec;
        }
        string Unique(string name, string path) => specs.Any(x => x.Name == name) ? $"{name} ({path})" : name;
        bool Covered(string dir) => specs.Any(x => x.Folders.Any(f => dir == f || dir.StartsWith(f + "/", StringComparison.Ordinal)));
        void AddFolder(AreaSpec spec, string folder) { spec.Folders.Add(folder); added.Add($"folder '{folder}' to '{spec.Name}'"); }
        var tops = a.TopItems.SelectMany(t => t.Subtree().Select(i => (Item: i, Top: t))).ToList();
        List<Tie> Ties() => [.. a.Place(new AreasFile { Areas = specs }).Open.Select(t =>
            new Tie(t.Key, t.Value, confirmed.Ties.FirstOrDefault(c => c.Flow == t.Key && c.Answer != "" && t.Value.Contains(c.Answer, O))?.Answer ?? ""))];

        for (var changed = true; changed;)
        {
            changed = false;
            var placement = a.Place(new AreasFile { Areas = specs, Ties = Ties() });
            foreach (var s in placement.Uncovered.Select(id => a.Starts[id]))
            {
                foreach (var p in s.Places)
                {
                    var listed = specs.SelectMany(x => x.Menu).ToHashSet(O);
                    if (p.Address is not null && !listed.Contains(p.Address) && tops.FirstOrDefault(t => t.Item.Address == p.Address) is { Item: not null } hit)
                    {
                        AreaSpec? spec = null;
                        for (var up = hit.Item.Parent; up is not null && spec is null; up = up.Parent)
                            if (up.Address != "") spec = specs.FirstOrDefault(x => x.Menu.Contains(up.Address, O));
                        spec ??= Named(Areas.AreaName(hit.Top));
                        spec.Menu.Add(p.Address);
                        added.Add($"menu '{p.Address}' to '{spec.Name}'");
                        changed = true;
                        continue;
                    }
                    var home = p.Frame ? p.Folder : a.HomeOf(p.Folder) ?? p.Folder;
                    if (Covered(home)) continue;
                    AddFolder(p.Frame ? Named(Areas.FrameName) : Named(Unique(a.Homes.GetValueOrDefault(home) ?? home[(home.LastIndexOf('/') + 1)..], home)), home);
                    changed = true;
                }
                if (s.BackendFolder is not null && !Covered(s.BackendFolder))
                {
                    AddFolder(Named(Unique(s.BackendName!, s.BackendFolder)), s.BackendFolder);
                    changed = true;
                }
                if (changed) break;   // place again: one addition can place others (a click placed leads its jobs)
            }
        }
        var ties = Ties();
        var final = a.Place(new AreasFile { Areas = specs, Ties = ties });
        var counts = final.AreaOf.Values.GroupBy(x => x, O).ToDictionary(x => x.Key, x => x.Count(), O);
        var large = specs.Where(x => counts.GetValueOrDefault(x.Name) > Areas.Big)
            .Select(x => new Large(x.Name, counts[x.Name], confirmed.Large.FirstOrDefault(l => l.Area == x.Name)?.Why ?? "over 40, nothing to split by")).ToList();
        var dropped = new List<string>();
        foreach (var t in confirmed.Ties.Where(t => t.Answer != "" && !ties.Any(n => n.Flow == t.Flow && n.Answer == t.Answer)))
            dropped.Add(!a.Starts.ContainsKey(t.Flow) ? $"'{t.Flow}': no longer a start flow"
                : ties.FirstOrDefault(n => n.Flow == t.Flow) is { } now ? $"'{t.Flow}': '{t.Answer}' is no longer one of its choices ({string.Join(", ", now.Choose.Select(c => $"'{c}'"))})"
                : $"'{t.Flow}': no longer a tie");
        return (new AreasFile { Areas = specs, Large = large, Ties = ties }, final, added, dropped);
    }

    // ---- areas.yaml ------------------------------------------------------------------------------------------------------------

    static string Q(string s) => $"'{s.Replace("'", "''")}'";

    /// <summary><c>areas.yaml</c>'s text: fixed order and quoting, so the same areas give the same bytes; each area's flow count as a comment.</summary>
    public static string Render(AreasFile file, string commit, Placement placement, bool builtOnConfirmed = false)
    {
        var counts = placement.AreaOf.Values.GroupBy(x => x, O).ToDictionary(x => x.Key, x => x.Count(), O);
        var sb = new StringBuilder();
        sb.Append($"# Areas proposed by `stangent areas propose` (D218–D228) from commit {commit}.\n");
        if (builtOnConfirmed) sb.Append($"# Built on {Confirmed} (D234): its areas and answers kept, only what code found new added; `--fresh` proposes from scratch.\n");
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
    /// or that name no area; an address or folder two areas list; flow files not where the file puts them (D230: run <c>flows build</c>).
    /// Returns the report and <c>areas.json</c>.
    /// </summary>
    public static (JsonObject Report, JsonObject AreasJson) Check(Areas a, AreasFile file, AreaLayout? layout = null)
    {
        layout ??= AreaLayout.ByAreas(a, file);
        var problems = new List<string>();
        foreach (var dup in file.Areas.GroupBy(x => x.Name, O).Where(x => x.Count() > 1)) problems.Add($"area '{dup.Key}' is listed {dup.Count()} times");
        foreach (var x in file.Areas.Where(x => x.Name.Trim() == "")) problems.Add("an area has no name");
        foreach (var dup in file.Areas.SelectMany(x => x.Menu.Distinct(O).Select(m => (m, x.Name))).GroupBy(x => x.m, O).Where(x => x.Count() > 1))
            problems.Add($"menu address '{dup.Key}' is in {string.Join(" and ", dup.Select(d => $"'{d.Name}'"))}");
        foreach (var dup in file.Areas.SelectMany(x => x.Folders.Distinct(O).Select(f => (f, x.Name))).GroupBy(x => x.f, O).Where(x => x.Count() > 1))
            problems.Add($"folder '{dup.Key}' is in {string.Join(" and ", dup.Select(d => $"'{d.Name}'"))}");

        var placement = layout.Placement!;
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

        // D230: flows build writes each flow where this file puts it; the index says where they are now.
        var misplaced = a.Flows.Values.Where(f => f.File != layout.File(f.Id)).Select(f => f.Id).ToList();
        if (misplaced.Count > 0)
            problems.Add($"{misplaced.Count} flow {(misplaced.Count == 1 ? "file isn't" : "files aren't")} where {Confirmed} puts them (e.g. '{misplaced[0]}' belongs in {layout.FolderOf[misplaced[0]]}/): run `stangent flows build`");

        var json = AreasJson(a, file, placement, layout);
        var counts = placement.AreaOf.Values.GroupBy(x => x, O).ToDictionary(x => x.Key, x => x.Count(), O);
        var report = new JsonObject
        {
            ["ok"] = problems.Count == 0,
            ["commit"] = a.Commit,
            ["file"] = Confirmed,
            ["startFlows"] = a.Starts.Count,
            ["placed"] = placement.AreaOf.Count,
            ["areas"] = new JsonArray([.. file.Areas.Select(x => (JsonNode)new JsonObject { ["name"] = x.Name, ["flows"] = counts.GetValueOrDefault(x.Name) })]),
            ["inUnplacedFolder"] = new JsonArray([.. layout.NotPlaced.Select(f => (JsonNode)f)]),
            ["misplaced"] = misplaced.Count,
            ["over40"] = new JsonArray([.. file.Areas.Where(x => counts.GetValueOrDefault(x.Name) > Areas.Big).Select(x => (JsonNode)x.Name)]),
            ["problems"] = new JsonArray([.. problems.Select(p => (JsonNode)p)]),
        };
        return (report, json);
    }

    /// <summary>The code-made <c>areas.json</c> (D225): every start flow's area, and per area the shared flows its starts reach (D218).</summary>
    public static JsonObject AreasJson(Areas a, AreasFile file, Placement placement, AreaLayout? layout = null) => new()
    {
        ["contract"] = Contract, ["version"] = Version, ["commit"] = a.Commit,
        ["areas"] = new JsonArray([.. file.Areas.Select(x =>
        {
            var flows = placement.AreaOf.Where(f => f.Value == x.Name).Select(f => f.Key).Order(O).ToList();
            var shared = flows.SelectMany(f => a.Reach(f, sharedOnly: true)).Distinct(O).Order(O).ToList();
            return (JsonNode)new JsonObject
            {
                ["name"] = x.Name,
                ["folder"] = layout is not null && layout.SlugOf.ContainsKey(x.Name) ? layout.AreaFolder(x.Name) : null,
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

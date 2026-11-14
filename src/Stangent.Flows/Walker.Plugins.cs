using Stangent.Graph;
using Stangent.Graph.Detectors;

namespace Stangent.Flows;

internal sealed partial class Walker
{
    /// <summary>
    /// D305(1): a call that runs <paramref name="run"/>, an implementation in a plugin or add-on project other than the caller's,
    /// lists each outside service the plugin's own code calls (D302's libraries, outside HTTP, D198/D199's ❓ HTTP and SMTP) as a 🟡
    /// outside call at the call's line: "may call …through the plugin …". Found by a search of the plugin's code, which isn't walked
    /// for this; the walk into <paramref name="run"/> itself goes on as before.
    /// </summary>
    void PluginCalls(string caller, Item item, string run, At at, List<Condition> conds, int depth)
    {
        if (!g.Nodes.TryGetValue(run, out var node) || !FlowGraph.IsPluginProject(node.Project)) return;
        if (g.Nodes.TryGetValue(caller, out var from) && from.Project == node.Project) return;
        var project = FlowGraph.ProjectName(node.Project);
        var type = g.TypeName(Stangent.Graph.Detectors.DocIds.ContainingType(run) ?? run);
        foreach (var (service, calls) in g.PluginOutsideCalls(run, s))
        {
            var shown = string.Join(", ", calls.Take(3)) + (calls.Count > 3 ? $" and {calls.Count - 3} more" : "");
            Add(item, new Step
            {
                What = $"may call {(service.StartsWith("outside ", StringComparison.Ordinal) ? $"outside the app over {service["outside ".Length..]}" : $"{service} outside the app")} through the plugin {project} ({type}): {shown}", At = at, Kind = "outside-call", Conditions = conds, Depth = depth,
                End = new End("outside-call", $"{service} (plugin {project})"), Guessed = true,
                GuessReason = $"the plugin {project} runs only when it's installed and chosen; its outside calls are found in its own code, which isn't walked here",
            });
        }
    }
}

internal sealed partial class FlowGraph
{
    static readonly HashSet<string> PluginFolders = new(["plugins", "plugin", "addons", "add-ons", "extensions"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// D305(1)'s rule: a plugin or add-on project is one whose project file sits under a folder named <c>Plugins</c>, <c>Plugin</c>,
    /// <c>AddOns</c>, <c>Add-ons</c> or <c>Extensions</c> (any case): <c>project:src/Plugins/Nop.Plugin.Tax.Avalara/….csproj</c>.
    /// </summary>
    public static bool IsPluginProject(string? project)
    {
        if (string.IsNullOrEmpty(project)) return false;
        var path = project.StartsWith("project:", StringComparison.Ordinal) ? project["project:".Length..] : project;
        var parts = path.Split('/', '\\');
        return parts.Length > 1 && parts[..^1].Any(PluginFolders.Contains);
    }

    /// <summary>A project's name from its id: <c>project:src/Plugins/Nop.Plugin.Tax.Avalara/Nop.Plugin.Tax.Avalara.csproj</c> → <c>Nop.Plugin.Tax.Avalara</c>.</summary>
    public static string ProjectName(string? project)
    {
        var path = project ?? "";
        var name = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? name[..^".csproj".Length] : name;
    }

    readonly Dictionary<string, List<(string Service, List<string> Calls)>> pluginCalls = new(StringComparer.Ordinal);

    /// <summary>
    /// D305(1): the outside services the code reachable from <paramref name="start"/> inside its own project calls, by service (a
    /// listed library's package, "outside HTTP", "outside SMTP"), each with the calls that reach it, in a fixed order.
    /// </summary>
    public List<(string Service, List<string> Calls)> PluginOutsideCalls(string start, FlowSettings s)
    {
        if (pluginCalls.TryGetValue(start, out var have)) return have;
        var project = Nodes.TryGetValue(start, out var sn) ? sn.Project : null;
        var found = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        void Found(string service, string call)
        {
            if (!found.TryGetValue(service, out var set)) found[service] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(call);
        }
        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
        var queue = new Queue<string>([start]);
        while (queue.Count > 0 && seen.Count < 5000)
        {
            var id = queue.Dequeue();
            foreach (var f in Facts(id, "call-site"))
            {
                if (f["kind"] == "package")
                {
                    var name = f["ns"] == "" ? f["package"] : $"{f["ns"]}.{(f["method"].LastIndexOf('.') is var d and > 0 ? f["method"][..d] : f["method"])}";
                    if (s.IsOutside(name) && !Walker.LocalBuilder(f["method"])) Found(f["package"], f["method"]);
                    continue;
                }
                foreach (var t in Dispatch(f["target"]).Prepend(f["target"]))
                    if (t != "" && Nodes.TryGetValue(t, out var n) && n.Project == project && !n.IsTest && seen.Add(t)) queue.Enqueue(t);
            }
            foreach (var c in Facts(id, "call").Where(f => f.Detector == "http-services")) Found("outside HTTP", $"{c["method"]} {c["url"]}".Trim());
            foreach (var b in BlindByOwner[id])
                if (b.Reason.StartsWith("outside HTTP ", StringComparison.Ordinal)) Found("outside HTTP", b.Reason["outside HTTP ".Length..]);
                else if (b.Reason.StartsWith("outside SMTP ", StringComparison.Ordinal)) Found("outside SMTP", b.Reason["outside SMTP ".Length..]);
        }
        return pluginCalls[start] = [.. found.Select(x => (x.Key, x.Value.ToList()))];
    }
}

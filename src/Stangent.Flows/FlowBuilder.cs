using System.Text;
using Stangent.Graph;

namespace Stangent.Flows;

/// <summary>Every flow code finds in one graph, and every start it found that isn't one, with why (D177 item 5).</summary>
public sealed record FlowSet(string Commit, FlowSettings Settings, List<Flow> Flows, List<Skipped> Skipped);

/// <summary>
/// Lays out flows from the code graph (D170–D184), code only. A flow starts at a click or submit, a screen opening or a system job
/// (D171), plus one per event handler, the flows a sent message links to (D173). Each is a tree walked forward from its start,
/// step by step through the call sites the graph recorded (D184), each branch ending as D172 and D174 say.
/// </summary>
public static class FlowBuilder
{
    public static FlowSet Build(CodeGraph graph, string root, FlowSettings? settings = null)
    {
        settings ??= new FlowSettings();
        var g = new FlowGraph(graph, root);
        var names = new Translations(root);
        var flows = new List<Flow>();
        var skipped = new List<Skipped>();
        var onLoadSeen = new HashSet<Fact>(ReferenceEqualityComparer.Instance);

        Clicks(g, settings, names, flows, skipped);
        Screens(g, settings, flows, skipped, onLoadSeen);
        Jobs(g, settings, flows);
        Handlers(g, settings, flows);
        // On-load code no screen flow reached: said, never dropped (D177 item 5).
        foreach (var f in g.AllFacts("on-load").Where(f => !onLoadSeen.Contains(f) && !g.IsTest(f.Owner) && f["framework"] != "mvc")
                     .OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Owner, StringComparer.Ordinal))
            skipped.Add(new Skipped("screen-opens", f.Owner, new At(f.File ?? "", f.Line, g.Code(f.File, f.Line)),
                "runs on load in code no route's screen reaches (a dialog, or a component shown only from another flow)"));

        AssignIds(flows);
        var byPoint = flows.Where(f => f.Kind is "job" or "handler").GroupBy(f => f.Start.Point, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First().Id, StringComparer.Ordinal);
        foreach (var flow in flows)
            for (var i = 0; i < flow.Links.Count; i++)
                if (flow.Links[i].ToFlow is { } point && byPoint.TryGetValue(point, out var id)) flow.Links[i] = flow.Links[i] with { ToFlow = id };
                else if (flow.Links[i].ToFlow is not null) flow.Links[i] = flow.Links[i] with { ToFlow = null };
        skipped.Sort((a, b) => string.CompareOrdinal($"{a.Kind}\u0000{a.At}\u0000{a.Point}", $"{b.Kind}\u0000{b.At}\u0000{b.Point}"));
        return new FlowSet(graph.Commit, settings, [.. flows.OrderBy(f => f.Id, StringComparer.Ordinal)], skipped);
    }

    // ---- starts (D171) ------------------------------------------------------------------------------------------------

    static void Clicks(FlowGraph g, FlowSettings s, Translations names, List<Flow> flows, List<Skipped> skipped)
    {
        var events = g.AllFacts("ui-event").Where(f => !g.IsTest(f.Owner))
            .OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Owner, StringComparer.Ordinal).ToList();
        foreach (var group in events.GroupBy(f => f["handler"], StringComparer.Ordinal))
        {
            var first = group.First();
            var at = new At(first.File ?? "", first.Line, g.Code(first.File, first.Line));
            var handler = group.Key;
            string? reason = handler switch
            {
                "" => "a plain button: no handler or form it runs",
                "inline" => "its handler is written inline in the markup, not a point of its own",
                _ when handler.StartsWith("prop:", StringComparison.Ordinal) => $"its handler is a prop ({handler[5..]}): the flow starts where the prop's value is given",
                _ when !g.Nodes.ContainsKey(handler) => "its handler isn't on the map",
                _ => null,
            };
            if (reason is not null)
            {
                foreach (var f in group) skipped.Add(new Skipped("click", f.Owner, new At(f.File ?? "", f.Line, g.Code(f.File, f.Line)), reason));
                continue;
            }
            var triggers = group.Select(f => $"{f["element"]} {f["event"]}{(f["text"] != "" ? $" \"{f["text"]}\"" : "")} ({f.File}:{f.Line})").Distinct(StringComparer.Ordinal).ToList();
            var name = group.Select(f => names.Text(f["keys"])).FirstOrDefault(t => t is not null)
                ?? group.Select(f => f["text"]).FirstOrDefault(t => t != "") ?? FlowGraph.ShortName(g.Nodes[handler]);
            var flow = NewFlow(g, "click", name, new StartInfo($"{first["element"]} {first["event"]}", at, handler, triggers), handler);
            var walker = new Walker(g, s, flow);
            walker.Root(handler);
            if (!walker.Meaningful)
            {
                foreach (var f in group) skipped.Add(new Skipped("click", handler, new At(f.File ?? "", f.Line, g.Code(f.File, f.Line)),
                    "screen behaviour only: its handler reaches no server call, save or message (D171)"));
                continue;
            }
            walker.Finish();
            flows.Add(flow);
        }
    }

    static void Screens(FlowGraph g, FlowSettings s, List<Flow> flows, List<Skipped> skipped, HashSet<Fact> onLoadSeen)
    {
        foreach (var page in g.Nodes.Values.Where(n => n.Kind == NodeKind.Page && !n.IsTest).OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            var at = new At(page.File ?? "", page.Line, g.Code(page.File, page.Line));
            var shows = g.Out(page.Id, EdgeKind.RoutesTo).Concat(g.Out(page.Id, EdgeKind.Renders)).Select(e => e.Target).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var flow = NewFlow(g, "screen-opens", page.Name, new StartInfo("the screen opens", at, page.Id, [page.Name]), page.Id);
            var walker = new Walker(g, s, flow) { OnLoadSeen = onLoadSeen };
            walker.Root(page.Id, shows);
            if (!walker.Meaningful)
            {
                skipped.Add(new Skipped("screen-opens", page.Id, at, "display only: opening it reaches no server call, save or message (D176 item 5)"));
                foreach (var p in flow.Reached.Where(p => p != page.Id && g.Facts(p, "on-load").Any()).Order(StringComparer.Ordinal))
                    if (g.Facts(p, "on-load").OrderBy(f => f.Line).First() is { } f)
                        skipped.Add(new Skipped("screen-opens", p, new At(f.File ?? "", f.Line, g.Code(f.File, f.Line)), $"runs on load in {page.Name}, a display-only screen"));
                continue;
            }
            walker.Finish();
            flows.Add(flow);
        }
        // MVC: a GET action that returns a view is its page opening (D171, D182).
        foreach (var f in g.AllFacts("on-load").Where(f => f["framework"] == "mvc" && !g.IsTest(f.Owner)).GroupBy(f => f.Owner, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!g.Nodes.TryGetValue(f.Key, out var action)) continue;
            foreach (var x in f) onLoadSeen.Add(x);
            var line = g.NameLine(action);
            var at = new At(action.File ?? "", line, g.Code(action.File, line));
            var route = g.In(action.Id, EdgeKind.RoutesTo).Select(e => g.Name(e.Source)).Order(StringComparer.Ordinal).FirstOrDefault();
            var flow = NewFlow(g, "screen-opens", route ?? action.Name, new StartInfo("the page is requested", at, action.Id, [action.Name]), action.Id);
            var walker = new Walker(g, s, flow) { OnLoadSeen = onLoadSeen };
            walker.Root(action.Id);
            walker.Finish();
            flows.Add(flow);
        }
    }

    static void Jobs(FlowGraph g, FlowSettings s, List<Flow> flows)
    {
        foreach (var job in g.AllFacts("is-job").Where(f => !g.IsTest(f.Owner) && g.Nodes.ContainsKey(f.Owner)).GroupBy(f => f.Owner, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var facts = job.OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ToList();
            var first = facts[0];
            var node = g.Nodes[job.Key];
            var name = first["kind"] == "command" && first["command"] != "" ? g.Name(first["command"])
                : first["kind"] == "recurring" && first["id"] != "" ? first["id"] : node.Name;
            var at = new At(first.File ?? "", first.Line, g.Code(first.File, first.Line));
            var schedule = string.Join("; ", facts.Select(f => f["schedule"] != "" ? f["schedule"] : f["scheduleCode"]).Where(x => x != "").Distinct(StringComparer.Ordinal));
            var flow = NewFlow(g, "job", name, new StartInfo($"a system job ({first["kind"]})", at, job.Key, [.. facts.Select(f => $"{f["kind"]} {f["interface"]}{f["id"]} ({f.File}:{f.Line})".Replace("  ", " ", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)]), job.Key);
            flow.Actor = new Actor("system", schedule != "" ? $"a system job, run on its schedule: {schedule}" : $"a system job ({first["kind"]}): runs when queued or scheduled", at, false);
            var walker = new Walker(g, s, flow) { ActorFixed = true };
            walker.Root(job.Key);
            walker.Finish();
            flows.Add(flow);
        }
    }

    static void Handlers(FlowGraph g, FlowSettings s, List<Flow> flows)
    {
        var jobs = g.AllFacts("is-job").Select(f => f.Owner).ToHashSet(StringComparer.Ordinal);
        foreach (var h in g.AllFacts("handle").Where(f => !g.IsTest(f.Owner) && !jobs.Contains(f.Owner) && g.Nodes.ContainsKey(f.Owner))
                     .GroupBy(f => f.Owner, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var fact = h.First();
            var node = g.Nodes[h.Key];
            var line = g.NameLine(node);
            var at = new At(node.File ?? "", line, g.Code(node.File, line));
            var flow = NewFlow(g, "handler", $"{node.Name} on {g.Name(fact["event"])}", new StartInfo($"handles {g.Name(fact["event"])}", at, h.Key, [.. h.Select(f => g.Name(f["event"])).Distinct(StringComparer.Ordinal)]), h.Key);
            flow.Actor = new Actor("system", $"runs when {g.Name(fact["event"])} is sent", at, false);
            var walker = new Walker(g, s, flow) { ActorFixed = true };
            walker.Root(h.Key);
            walker.Finish();
            flows.Add(flow);
        }
    }

    static Flow NewFlow(FlowGraph g, string kind, string name, StartInfo start, string point)
    {
        var file = g.Nodes.TryGetValue(point, out var n) && n.File is not null ? n.File : start.At.File;
        return new Flow
        {
            Id = Slug($"{kind}-{IdName(g, kind, point, name)}"), Name = name, Kind = kind, Start = start,
            Actor = new Actor("unknown", "not worked out yet", null, true), Home = Path.GetDirectoryName(file)?.Replace('\\', '/') ?? "",
        };
    }

    static string IdName(FlowGraph g, string kind, string point, string name) => kind switch
    {
        "screen-opens" when point.StartsWith("page:", StringComparison.Ordinal) => name,
        _ when g.Nodes.TryGetValue(point, out var n) && n.Kind != NodeKind.Form => n.Name,
        _ => name,
    };

    /// <summary>Ids from names; two flows with one name get <c>-2</c>, <c>-3</c> in the order of their start points (D176).</summary>
    static void AssignIds(List<Flow> flows)
    {
        foreach (var same in flows.GroupBy(f => f.Id, StringComparer.Ordinal).Where(x => x.Count() > 1))
        {
            var i = 1;
            foreach (var f in same.OrderBy(f => f.Start.Point, StringComparer.Ordinal).ThenBy(f => f.Start.At.ToString(), StringComparer.Ordinal))
                if (i++ > 1) f.Id = $"{f.Id}-{i - 1}";
        }
    }

    public static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length > 90 ? slug[..90].TrimEnd('-') : slug;
    }
}

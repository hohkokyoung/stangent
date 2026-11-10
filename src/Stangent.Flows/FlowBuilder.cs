using System.Text;
using Stangent.Graph;

namespace Stangent.Flows;

/// <summary>Every flow code finds in one graph, and every start it found that isn't one, with why (D177 item 5).</summary>
public sealed record FlowSet(string Commit, FlowSettings Settings, List<Flow> Flows, List<Skipped> Skipped);

/// <summary>
/// Lays out flows from the code graph (D170–D184), code only. A flow starts at a click or submit, a screen opening or a system job
/// (D171), plus one per event handler, the flows a sent message links to (D173), and one per hub or depth stop a flow continues
/// into (<c>shared</c>, D189). Each is a tree walked forward from its start,
/// step by step through the call sites the graph recorded (D184), each branch ending as D172 and D174 say.
/// </summary>
public static partial class FlowBuilder
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

        Shared(g, settings, flows);

        AssignIds(flows);
        var byPoint = flows.Where(f => f.Kind is "job" or "handler").GroupBy(f => f.Start.Point, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First().Id, StringComparer.Ordinal);
        var shared = flows.Where(f => f.Kind == "shared").ToDictionary(f => f.Start.Point, f => f.Id, StringComparer.Ordinal);
        // D204: a screen link goes to the flow of that screen opening.
        var screens = flows.Where(f => f.Kind == "screen-opens").GroupBy(f => f.Start.Point, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Select(f => f.Id).Order(StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var notBuilt = skipped.Where(x => x.Kind == "screen-opens").GroupBy(x => x.Point, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First().Reason, StringComparer.Ordinal);
        foreach (var flow in flows)
            for (var i = 0; i < flow.Links.Count; i++)
            {
                var link = flow.Links[i];
                if (link.ToFlow is { } point && (link.Kind switch { "continues" => shared, "screen" => screens, _ => (Dictionary<string, string>)byPoint }).TryGetValue(point, out var id)) flow.Links[i] = link with { ToFlow = id };
                // D204: a screen whose opening isn't a flow says why, from the skipped list.
                else if (link.Kind == "screen" && link.ToFlow is { } screen)
                    flow.Links[i] = link with { ToFlow = null, Sentence = $"Sends the user to {link.To}; opening it is no flow: {(notBuilt.TryGetValue(screen, out var why) ? why : "no screen-opens flow starts there")} ({link.At})" };
                else if (link.ToFlow is not null) flow.Links[i] = link with { ToFlow = null };
            }
        QueuedWork(flows);
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

    /// <summary>
    /// D189 (D173's promise): each hub or depth stop a flow continues into is a flow of its own, kind <c>shared</c>, walked once
    /// from that point; the shared flows' own stops get theirs too, until none is left.
    /// </summary>
    static void Shared(FlowGraph g, FlowSettings s, List<Flow> flows)
    {
        var done = new HashSet<string>(StringComparer.Ordinal);
        var todo = new SortedSet<string>(Continues(flows), StringComparer.Ordinal);
        while (todo.Count > 0)
        {
            var point = todo.Min!;
            todo.Remove(point);
            if (!done.Add(point) || !g.Nodes.TryGetValue(point, out var node)) continue;
            var line = g.NameLine(node);
            var at = new At(node.File ?? "", line, g.Code(node.File, line));
            var flow = NewFlow(g, "shared", node.Name, new StartInfo("shared code, walked once for the flows that continue into it", at, point, []), point);
            flow.Actor = new Actor("the flow that continues here", "shared code: it runs as part of each flow that links to it", null, false);
            var walker = new Walker(g, s, flow) { ActorFixed = true };
            walker.RootShared(point);
            walker.Finish();
            flows.Add(flow);
            foreach (var next in Continues([flow])) if (!done.Contains(next)) todo.Add(next);
        }
    }

    /// <summary>
    /// D205: work saved for a job to pick up later. A table named as a queue (a word Queue, Queued, Pending or Outbox in its name)
    /// that a system job reads, directly or in shared code it continues into, is that job's work list: a save to it elsewhere links
    /// <c>runs-later</c> to the job (🟡). The link sits on the call that entered the method making the save, or continuing into
    /// shared code that makes it, so it reads as "this call leaves work for the job"; a save in the flow's own start sits where it's written.
    /// </summary>
    internal static void QueuedWork(List<Flow> flows)
    {
        var byId = flows.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var reachedOf = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        // The shared flows a flow continues into, transitively (D189).
        HashSet<string> Reached(Flow f)
        {
            if (reachedOf.TryGetValue(f.Id, out var done)) return done;
            var set = new HashSet<string>(StringComparer.Ordinal);
            var todo = new Stack<Flow>([f]);
            while (todo.TryPop(out var x))
                foreach (var l in x.Links.Where(l => l.Kind == "continues" && l.ToFlow is not null))
                    if (set.Add(l.ToFlow!) && byId.TryGetValue(l.ToFlow!, out var sh)) todo.Push(sh);
            return reachedOf[f.Id] = set;
        }
        // A save that removes work (a delete, remove or clear) leaves nothing for the job.
        static bool Adds(Step st) => st.What.StartsWith("saves ", StringComparison.Ordinal) && !RemovesWork(st.At.Code) && !RemovesWork(st.What);
        static IEnumerable<string> Saved(Flow f) => f.Steps.Where(st => st.Kind == "database" && st.End is not null && Adds(st)).SelectMany(st => TablesIn(st.End!.Detail));
        HashSet<string> SavedQueues(Flow f) =>
            [.. Saved(f).Concat(Reached(f).Where(byId.ContainsKey).SelectMany(id => Saved(byId[id]))).Where(IsQueueTable)];
        // The job's work list: the queue tables it reads before it saves anything, in walk order with the shared code it continues
        // into taken in place. Reads after a save are the job looking up what it changes, not the work it was left.
        IEnumerable<Step> InOrder(Flow f, HashSet<string> seen)
        {
            if (!seen.Add(f.Id)) yield break;
            var shared = f.Links.Where(l => l.Kind == "continues" && l.ToFlow is not null && l.Step is not null).ToLookup(l => l.Step!.Value);
            foreach (var st in f.Steps.OrderBy(st => st.N))
            {
                yield return st;
                foreach (var l in shared[st.N])
                    if (byId.TryGetValue(l.ToFlow!, out var sh)) foreach (var inner in InOrder(sh, seen)) yield return inner;
            }
        }
        HashSet<string> WorkList(Flow job)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var st in InOrder(job, new HashSet<string>(StringComparer.Ordinal)).Where(st => st.Kind == "database" && st.End is not null))
            {
                if (!st.What.StartsWith("reads ", StringComparison.Ordinal)) break;
                set.UnionWith(TablesIn(st.End!.Detail).Where(IsQueueTable));
            }
            return set;
        }
        var jobs = flows.Where(f => f.Kind == "job").OrderBy(f => f.Id, StringComparer.Ordinal)
            .Select(j => (Job: j, Reads: WorkList(j), Reached: Reached(j))).Where(j => j.Reads.Count > 0).ToList();
        if (jobs.Count == 0) return;
        foreach (var flow in flows)
        {
            var steps = flow.Steps.ToDictionary(st => st.N);
            var anchors = new List<(Step At, string Table)>();
            foreach (var st in flow.Steps.Where(st => st.Kind == "database" && st.End is not null && Adds(st)))
                foreach (var t in TablesIn(st.End!.Detail).Where(IsQueueTable)) anchors.Add((st.From is { } p ? steps[p] : st, t));
            foreach (var l in flow.Links.Where(l => l.Kind == "continues" && l.ToFlow is not null && l.Step is not null))
                if (byId.TryGetValue(l.ToFlow!, out var sh))
                    foreach (var t in SavedQueues(sh).Order(StringComparer.Ordinal))
                        anchors.Add((steps[l.Step!.Value] is var st && st.From is { } p ? steps[p] : steps[l.Step!.Value], t));
            var made = new HashSet<(int, string)>();
            foreach (var (at, table) in anchors.OrderBy(a => a.At.N).ThenBy(a => a.Table, StringComparer.Ordinal))
                foreach (var (job, _, reached) in jobs.Where(j => j.Reads.Contains(table)))
                {
                    // The job's own saves (marking the work done) aren't work left for it.
                    if (job == flow || reached.Contains(flow.Id) || !made.Add((at.N, job.Id))) continue;
                    var sentence = $"Saves {table}, which the job {job.Name} reads and works through later, in the background ({at.At})";
                    flow.Links.Add(new Link("runs-later", job.Name, job.Id, at.At, sentence, at.N, true));
                    flow.Guesses.Add(new Guess(at.N, sentence, "a table named as a queue that a system job reads"));
                }
        }
    }

    /// <summary>The tables a database step's end names: <c>Order, OrderNote (IOrderService.Search…, shared code …)</c>.</summary>
    static IEnumerable<string> TablesIn(string detail) =>
        (detail.IndexOf(" (", StringComparison.Ordinal) is var i and >= 0 ? detail[..i] : detail).Split(',').Select(t => t.Trim()).Where(t => t.Length > 0);

    static bool RemovesWork(string code) => RemoveCall().IsMatch(code);

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(?:Delete|Remove|Clear|Purge|Truncate)\w*\s*[(<]", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex RemoveCall();

    /// <summary>A table named as a queue: one of its words (split at capitals, <c>_</c>, <c>-</c>, <c>.</c>) is queue, queued, pending or outbox.</summary>
    internal static bool IsQueueTable(string table)
    {
        var words = new List<string>();
        var w = new StringBuilder();
        foreach (var c in table)
        {
            if (!char.IsLetterOrDigit(c) || char.IsUpper(c) && w.Length > 0 && !char.IsUpper(w[^1])) { if (w.Length > 0) words.Add(w.ToString()); w.Clear(); }
            if (char.IsLetterOrDigit(c)) w.Append(c);
        }
        if (w.Length > 0) words.Add(w.ToString());
        return words.Any(x => x.ToLowerInvariant() is "queue" or "queues" or "queued" or "pending" or "outbox");
    }

    static IEnumerable<string> Continues(IEnumerable<Flow> flows) =>
        flows.SelectMany(f => f.Links).Where(l => l.Kind == "continues" && l.ToFlow is not null).Select(l => l.ToFlow!);

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

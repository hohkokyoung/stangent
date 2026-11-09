using Stangent.Graph;

namespace Stangent.Flows;

/// <summary>
/// Walks one flow forward from its start (D172): each point's call sites in source-line order (D176), into what each call runs,
/// until a branch ends. Steps are numbered in walk order at the end, after display-only parts are left out.
/// </summary>
internal sealed class Walker(FlowGraph g, FlowSettings s, Flow flow)
{
    sealed class Item
    {
        public required Step Step;
        public List<Item> Children { get; } = [];
        public List<(Exit Exit, Item? Of)> Exits { get; } = [];
        public List<Link> Links { get; } = [];
        public List<Unknown> Unknowns { get; } = [];
        public Item? BackTo, SameAs;
        public Item? Parent;
    }

    readonly Item root = new() { Step = new Step { What = "start", At = flow.Start.At, Kind = "start" } };
    readonly Dictionary<string, Item> walked = new(StringComparer.Ordinal);
    readonly HashSet<string> endpointsSeen = new(StringComparer.Ordinal);
    string? firstAction;
    public HashSet<Fact>? OnLoadSeen { get; init; }
    public bool ActorFixed { get; init; }
    static readonly HashSet<string> MeaningfulKinds = ["http", "database", "outside-call", "message", "queue"];

    /// <summary>The flow reaches a server call, a save, an outside call, a message or queued work (D171's test for a click).</summary>
    public bool Meaningful => Subtree(root).Any(i => MeaningfulKinds.Contains(i.Step.Kind));

    public void Root(string point, IReadOnlyList<string>? shown = null)
    {
        walked[point] = root;
        // A route's guards run before its screen opens (Angular canActivate, canMatch…).
        foreach (var guard in g.Facts(point, "guard").OrderBy(f => f.Line).ThenBy(f => f["guard"], StringComparer.Ordinal))
        {
            var at = new At(guard.File ?? "", guard.Line, g.Code(guard.File, guard.Line));
            var item = Add(root, new Step { What = $"checks the route's guard {guard["name"]} ({guard["via"]})", At = at, Kind = "guard", Point = guard["guard"], Depth = 0 });
            Into(item, guard["guard"], 0, [point]);
        }
        if (g.Facts(point, "auth").Any() || g.Facts(point, "endpoint-types").Any()) BeforeAction(point, root, 0, [point]);
        Visit(point, root, 0, [point]);
        foreach (var c in shown ?? [])
            if (g.Nodes.ContainsKey(c) && walked.TryAdd(c, root)) Visit(c, root, 0, [point, c]);
    }

    // ---- the walk ------------------------------------------------------------------------------------------------------

    void Visit(string point, Item parent, int depth, List<string> branch)
    {
        if (!g.Nodes.TryGetValue(point, out var node)) return;
        flow.Reached.Add(point);
        var file = node.File ?? "";
        var callSites = g.Facts(point, "call-site").ToList();
        var lines = callSites.Select(f => f.Line)
            .Concat(g.Facts(point, "access").Select(f => f.Line)).Concat(g.Facts(point, "call").Select(f => f.Line))
            .Concat(g.Facts(point, "queue").Select(f => f.Line)).Concat(g.Facts(point, "job").Select(f => f.Line))
            .Concat(g.Facts(point, "http-match").Select(f => f.Line)).Concat(g.Facts(point, "exit").Select(f => f.Line))
            .Concat(g.Facts(point, "on-load").Select(f => f.Line)).Concat(g.BlindByOwner[point].Select(b => b.Line))
            .Distinct().Order().ToList();
        var later = g.Facts(point, "job").Select(f => f["runs"]).ToHashSet(StringComparer.Ordinal);
        var conditions = g.Facts(point, "condition").ToLookup(f => f.Line);

        foreach (var line in lines)
        {
            var at = new At(FileAt(point, line, file), line, g.Code(FileAt(point, line, file), line));
            var sites = callSites.Where(f => f.Line == line).OrderBy(f => f["target"], StringComparer.Ordinal).ToList();
            var conds = conditions[line].OrderBy(f => f["target"], StringComparer.Ordinal).ToList();
            List<Condition> CondsFor(string? target) =>
                FlowGraph.ConditionsOf(conds.FirstOrDefault(c => target is not null && c["target"] == target) ?? conds.FirstOrDefault() ?? Empty);
            var consumed = new HashSet<string>(StringComparer.Ordinal);

            foreach (var http in g.Facts(point, "http-match").Where(f => f.Line == line).OrderBy(f => f["endpoint"], StringComparer.Ordinal))
            {
                Http(http, at, CondsFor(null), parent, depth, branch);
                consumed.UnionWith(sites.Select(x => x["target"]));
            }
            foreach (var access in g.Facts(point, "access").Where(f => f.Line == line && g.TableOf.ContainsKey(f["entity"])).OrderBy(f => f["entity"], StringComparer.Ordinal))
            {
                var table = g.TableOf[access["entity"]];
                Add(parent, new Step
                {
                    What = $"{(access["mode"] == "write" ? "saves" : "reads")} {table}", At = at, Kind = "database", Conditions = CondsFor(null),
                    End = new End("database", table), Depth = depth, Guessed = access["via"] is "repository" or "navigation",
                    GuessReason = access["via"] == "repository" ? "a repository method read as a save or read by its name" : access["via"] == "navigation" ? "a navigation collection's Add or Remove read as a save" : null,
                });
                consumed.UnionWith(sites.Select(x => x["target"]));
            }
            foreach (var call in g.Facts(point, "call").Where(f => f.Line == line && f.Detector == "http-services").OrderBy(f => f["url"], StringComparer.Ordinal))
            {
                Add(parent, new Step
                {
                    What = $"calls {call["method"]} {call["url"]} outside the app".Replace("  ", " ", StringComparison.Ordinal), At = at, Kind = "outside-call",
                    Conditions = CondsFor(null), End = new End("outside-call", $"{call["method"]} {call["url"]}".Trim()), Depth = depth,
                });
                consumed.UnionWith(sites.Select(x => x["target"]));
            }
            foreach (var q in g.Facts(point, "queue").Where(f => f.Line == line).OrderBy(f => f["type"], StringComparer.Ordinal))
            {
                var jobs = g.Out(point, EdgeKind.Queues).Select(e => e.Target).Where(t => g.JobsByCommand[q["type"]].Contains(t, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                var item = Add(parent, new Step { What = $"queues {g.TypeName(q["type"])}, which runs later, in the background", At = at, Kind = "queue", Conditions = CondsFor(null), Depth = depth, Guessed = true, GuessReason = "a queued command matched to its job by type" });
                foreach (var j in jobs) item.Links.Add(new Link("runs-later", g.TypeName(q["type"]), j, at, $"Queues the job {g.TypeName(q["type"])}, which runs later, in the background ({at})", null, true));
                if (jobs.Count == 0) item.Unknowns.Add(new Unknown(null, point, at, $"no job runs {g.TypeName(q["type"])}"));
                consumed.UnionWith(sites.Select(x => x["target"]));
            }
            foreach (var j in g.Facts(point, "job").Where(f => f.Line == line).OrderBy(f => f["runs"], StringComparer.Ordinal))
            {
                var item = Add(parent, new Step { What = $"queues {g.Name(j["runs"])} through {j["via"]}, which runs later, in the background ({j["schedule"]})", At = at, Kind = "queue", Conditions = CondsFor(null), Depth = depth });
                item.Links.Add(new Link("runs-later", g.Name(j["runs"]), j["runs"], at, $"Queues {g.Name(j["runs"])}, which runs later, in the background ({at})", null, false));
                consumed.UnionWith(sites.Select(x => x["target"]));
            }
            foreach (var exit in g.Facts(point, "exit").Where(f => f.Line == line).OrderBy(f => f["text"], StringComparer.Ordinal))
                parent.Exits.Add((new Exit(exit["kind"], ExitSentence(exit), at, FlowGraph.ConditionsOf(exit), null, point), parent));
            foreach (var load in g.Facts(point, "on-load").Where(f => f.Line == line && f["framework"] != "mvc").OrderBy(f => f["handler"], StringComparer.Ordinal))
            {
                OnLoadSeen?.Add(load);
                var h = load["handler"];
                consumed.Add(h);
                if (h == point || !g.Nodes.ContainsKey(h)) continue;
                var item = Add(parent, new Step { What = $"runs on open: {load["hook"]} ({load["runs"]}{(load["deps"] != "" ? $"; again when {load["deps"].Replace("|", ", ", StringComparison.Ordinal)} changes" : "")})", At = at, Kind = "on-load", Point = h, Conditions = CondsFor(h), Depth = depth, Runs = load["runs"] });
                Into(item, h, depth, branch);
            }
            foreach (var site in sites)
            {
                var target = site["target"];
                if (site["kind"] == "package")
                {
                    // D188: a package's code is a step, not walked (it isn't on the map).
                    if (consumed.Count == 0 && !later.Contains(target))
                        Add(parent, new Step { What = $"calls {site["method"]} in the package {site["package"]}", At = at, Kind = "package", Conditions = CondsFor(""), Depth = depth,
                            End = new End("unknown", $"a package's code ({site["package"]}), not walked") });
                    continue;
                }
                if (consumed.Contains(target) || later.Contains(target)) continue;
                Call(point, site, target, at, CondsFor(target), parent, depth, branch);
            }
            foreach (var blind in g.BlindByOwner[point].Where(b => b.Line == line).OrderBy(b => b.Reason, StringComparer.Ordinal))
                Add(parent, new Step
                {
                    What = $"can't be followed: {blind.Reason}", At = new At(blind.File, blind.Line, g.Code(blind.File, blind.Line)),
                    // An HTTP call the graph couldn't match is still a server call (D171's test), ❓ where it goes.
                    Kind = blind.Reason.StartsWith("HTTP ", StringComparison.Ordinal) ? "http" : "unknown", End = new End("unknown", blind.Reason), Depth = depth,
                });
        }
    }

    static readonly Fact Empty = new("", "", "", null, 0, new Dictionary<string, string>());

    /// <summary>Facts and call sites carry their own file: a view's inline script belongs to the view (N5).</summary>
    string FileAt(string point, int line, string fallback) =>
        g.Facts(point, "call-site").FirstOrDefault(f => f.Line == line)?.File ?? g.Facts(point, "condition").FirstOrDefault(f => f.Line == line)?.File
        ?? g.Facts(point, "exit").FirstOrDefault(f => f.Line == line)?.File ?? g.Facts(point, "http-match").FirstOrDefault(f => f.Line == line)?.File ?? fallback;

    void Call(string point, Fact site, string target, At at, List<Condition> conds, Item parent, int depth, List<string> branch)
    {
        if (!g.Nodes.TryGetValue(target, out var tnode)) return;
        if (FlowGraph.ConstructedType(target) is { } made)
        {
            // A message sent (D172 item 3): its handlers are linked flows; the walk never goes through the bus. A command a job
            // runs is queued work, read from the queue facts, not a message.
            var handlers = g.HandlersByEvent[made].Where(h => !g.IsTest(h)).Distinct(StringComparer.Ordinal).ToList();
            if (handlers.Count == 0 || g.JobsByCommand[made].Any()) return;
            var item = Add(parent, new Step { What = $"sends {g.TypeName(made)}", At = at, Kind = "message", Conditions = conds, End = new End("message", g.TypeName(made)), Depth = depth, Point = made });
            foreach (var h in handlers)
                item.Links.Add(new Link("message", g.Name(h), h, at, $"Sends {g.TypeName(made)}, handled by {g.Name(h)} ({at})", null, true));
            return;
        }
        var kind = site["kind"] == "renders" ? "renders" : "call";
        var guessedSite = site["label"] == "1";
        // G5: a value taken from a hook's result (`const { refetch } = useX()`) and called: the hook stands for it, a guess.
        var byValue = !guessedSite && IsHook(target) && !at.Code.Contains(FlowGraph.ShortName(tnode), StringComparison.Ordinal);
        if (g.Callers(target) >= s.HubCallers && kind == "call")
        {
            // D172's order puts a table before a hub: shared code whose own body reads or saves a table ends there, named.
            var tables = g.Dispatch(target).SelectMany(t => g.Out(t, EdgeKind.Writes).Select(e => (Table: g.Name(e.Target), Write: true, Guess: e.Label != Label.Certain))
                .Concat(g.Out(t, EdgeKind.Reads).Select(e => (Table: g.Name(e.Target), Write: false, Guess: e.Label != Label.Certain)))).Distinct().OrderBy(x => x.Table, StringComparer.Ordinal).ToList();
            if (tables.Count > 0)
            {
                var names = string.Join(", ", tables.Select(x => x.Table).Distinct(StringComparer.Ordinal));
                Add(parent, new Step
                {
                    What = $"{(tables.Any(x => x.Write) ? "saves" : "reads")} {names} through {tnode.Name}", At = at, Kind = "database", Point = target, Conditions = conds, Depth = depth,
                    End = new End("database", $"{names} ({tnode.Name}, shared code used by {g.Callers(target)} places)"), Guessed = guessedSite || tables.Any(x => x.Guess),
                    GuessReason = guessedSite ? OverloadReason : tables.Any(x => x.Guess) ? "a save or read found by a detector's pattern (a repository method's name, a navigation collection)" : null,
                });
                return;
            }
            var item = Add(parent, new Step
            {
                What = $"calls {tnode.Name}", At = at, Kind = kind, Point = target, Conditions = conds, Depth = depth,
                End = new End("hub", $"{tnode.Name}: shared code used by {g.Callers(target)} places"), Guessed = guessedSite || byValue,
                GuessReason = guessedSite ? OverloadReason : byValue ? $"calls a value taken from {tnode.Name}'s result; what it runs isn't known, so the hook stands for it" : null,
            });
            item.Links.Add(new Link("continues", tnode.Name, null, at, $"Stops here: shared code used by {g.Callers(target)} places; {tnode.Name}'s own walk continues it ({at})", null, false));
            return;
        }
        var runs = g.Dispatch(target).Where(t => g.Nodes.TryGetValue(t, out var n) && n.File is not null && !n.IsTest).ToList();
        foreach (var run in runs)
        {
            var several = runs.Count > 1;
            var item = Add(parent, new Step
            {
                What = kind == "renders" ? $"shows {g.Name(run)}" : $"calls {g.Name(run)}", At = at, Kind = kind, Point = run, Conditions = conds, Depth = depth,
                Guessed = several || guessedSite || byValue,
                GuessReason = several ? $"one of {runs.Count}: {string.Join(", ", runs.Select(g.Name))}" : guessedSite ? OverloadReason
                    : byValue ? $"calls a value taken from {tnode.Name}'s result; what it runs isn't known, so the hook stands for it" : null,
            });
            Into(item, run, depth, branch);
        }
    }

    const string OverloadReason = "the compiler couldn't pick one overload, so each in-repo candidate is a branch";

    /// <summary>Walks into a point unless it's already on this branch (a loop), already walked, or past the depth limit.</summary>
    void Into(Item item, string point, int depth, List<string> branch)
    {
        if (branch.Contains(point, StringComparer.Ordinal)) { item.BackTo = walked.GetValueOrDefault(point) ?? root; return; }
        if (walked.TryGetValue(point, out var before)) { item.SameAs = before; return; }
        if (depth + 1 > s.Depth)
        {
            item.Step.End = new End("depth", $"{s.Depth} real calls; not walked: {g.Name(point)}");
            item.Links.Add(new Link("continues", g.Name(point), null, item.Step.At, $"Stops here: {s.Depth} calls deep; {g.Name(point)}'s own walk continues it ({item.Step.At})", null, false));
            return;
        }
        walked[point] = item;
        Visit(point, item, depth + 1, [.. branch, point]);
        // A hook's own function that reaches no server work may still run the hook's library callbacks (a mutation's function runs
        // when it calls `mutate`): the hook's own body is walked too, 🟡 (D184).
        if (HookOf(point) is { } hook && !branch.Contains(hook, StringComparer.Ordinal) && !Subtree(item).Skip(1).Any(i => MeaningfulKinds.Contains(i.Step.Kind)) && g.Nodes.ContainsKey(hook))
        {
            var h = Add(item, new Step { What = $"runs {g.Name(hook)}'s own work, through a library callback", At = item.Step.At, Kind = "call", Point = hook, Depth = depth + 1,
                Guessed = true, GuessReason = $"{g.Name(point)} reaches no server work itself; what {g.Name(hook)} hands a library (a mutation's function) may run when it's called" });
            if (walked.TryGetValue(hook, out var seen)) h.SameAs = seen;
            else { walked[hook] = h; Visit(hook, h, depth + 1, [.. branch, point, hook]); }
        }
        // A component that shows nothing reaching the server is display only: named, not walked into (D176 item 5).
        if (item.Step.Kind == "renders" && !Subtree(item).Skip(1).Any(i => MeaningfulKinds.Contains(i.Step.Kind)))
        {
            item.Parent!.Children.Remove(item);
            walked.Remove(point);
            if (!flow.DisplayOnly.Contains(g.Name(point), StringComparer.Ordinal)) flow.DisplayOnly.Add(g.Name(point));
        }
    }

    void Http(Fact http, At at, List<Condition> conds, Item parent, int depth, List<string> branch)
    {
        var endpoint = http["endpoint"];
        var guessed = http["label"] != "certain";
        var item = Add(parent, new Step
        {
            What = $"calls {http["verb"]} {http["url"]}".TrimEnd(), At = at, Kind = "http", Point = endpoint, Conditions = conds, Depth = depth,
            Guessed = guessed, GuessReason = guessed ? (http["reason"] != "" ? http["reason"] : "no reason recorded") : null,
        });
        var actions = endpoint == "" ? [] : g.Out(endpoint, EdgeKind.RoutesTo).Select(e => e.Target).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (actions.Count == 0)
        {
            item.Step.End = new End("unknown", endpoint == "" ? $"no endpoint fits: {http["reason"]}" : $"no action on the map serves {g.Name(endpoint)}");
            return;
        }
        endpointsSeen.Add(endpoint);
        foreach (var action in actions)
        {
            if (!g.Nodes.TryGetValue(action, out var a)) continue;
            firstAction ??= action;
            var line = g.NameLine(a);
            var aat = new At(a.File ?? "", line, g.Code(a.File, line));
            // The route hop doesn't count as a call (D172).
            var step = Add(item, new Step { What = $"runs {a.Name}", At = aat, Kind = "call", Point = action, Depth = depth });
            if (branch.Contains(action, StringComparer.Ordinal)) { step.BackTo = walked.GetValueOrDefault(action) ?? root; continue; }
            if (walked.TryGetValue(action, out var before)) { step.SameAs = before; continue; }
            walked[action] = step;
            BeforeAction(action, step, depth, [.. branch, action]);
            Visit(action, step, depth, [.. branch, action]);
        }
    }

    static readonly string[] FilterMethods = ["OnAuthorization", "OnAuthorizationAsync", "OnResourceExecuting", "OnResourceExecutionAsync", "OnActionExecuting", "OnActionExecutionAsync"];

    /// <summary>
    /// What ASP.NET runs before an action: its access filters (global, the controller's, the action's own: the <c>auth</c> facts'
    /// <c>filterType</c>), then the controller's own <c>OnActionExecuting</c> overrides up its base classes. Each is a step walked
    /// like a call, so its checks and throws are the flow's.
    /// </summary>
    void BeforeAction(string action, Item parent, int depth, List<string> branch)
    {
        var project = g.Nodes.TryGetValue(action, out var an) ? an.Project : null;
        var filters = (project is null ? [] : g.AllFacts("auth").Where(f => f["from"] == "global" && f.Owner == project))
            .Concat(g.Facts(action, "auth").Where(f => f["rule"] == "filter").OrderBy(f => f["from"] == "method" ? 1 : 0))
            .Select(f => f["filterType"]).Where(t => t != "" && g.Nodes.ContainsKey(t)).Distinct(StringComparer.Ordinal).ToList();
        var steps = filters.SelectMany(t => Members(t, FilterMethods)).ToList();
        if (DocIds(action) is { } controller) steps.AddRange(Members(controller, ["OnActionExecuting", "OnActionExecutionAsync"]));
        foreach (var m in steps.Distinct(StringComparer.Ordinal))
        {
            var node = g.Nodes[m];
            var line = g.NameLine(node);
            var item = Add(parent, new Step { What = $"runs first: {node.Name}", At = new At(node.File ?? "", line, g.Code(node.File, line)), Kind = "filter", Point = m, Depth = depth });
            Into(item, m, depth, branch);
        }
    }

    /// <summary>The hook a hook's own function belongs to: <c>ts|f.ts#useX.save</c> → <c>ts|f.ts#useX</c>.</summary>
    static string? HookOf(string point)
    {
        var hash = point.IndexOf('#');
        if (!point.StartsWith("ts|", StringComparison.Ordinal) || hash < 0) return null;
        var name = point[(hash + 1)..];
        var dot = name.IndexOf('.');
        return dot > 3 && name.StartsWith("use", StringComparison.Ordinal) && char.IsUpper(name[3]) ? point[..(hash + 1 + dot)] : null;
    }

    static bool IsHook(string point)
    {
        var hash = point.IndexOf('#');
        if (!point.StartsWith("ts|", StringComparison.Ordinal) || hash < 0) return false;
        var name = point[(hash + 1)..];
        return name.Length > 3 && name.StartsWith("use", StringComparison.Ordinal) && char.IsUpper(name[3]) && !name.Contains('.');
    }

    static string? DocIds(string member) => Stangent.Graph.Detectors.DocIds.ContainingType(member);

    /// <summary>A type's own members with these names, then its bases' in the repo, nearest first.</summary>
    IEnumerable<string> Members(string type, string[] names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var overridden = new HashSet<string>(StringComparer.Ordinal);
        for (var t = type; t is not null && seen.Add(t); t = g.Out(t, EdgeKind.Inherits).Select(e => e.Target).Where(g.Nodes.ContainsKey).Order(StringComparer.Ordinal).FirstOrDefault())
            foreach (var m in g.Out(t, EdgeKind.Contains).Select(e => e.Target).Where(m => g.Nodes.TryGetValue(m, out var n) && n.File is not null && names.Contains(FlowGraph.ShortName(n))).Order(StringComparer.Ordinal))
                if (overridden.Add(FlowGraph.ShortName(g.Nodes[m]) + "(" + m.Split('(').ElementAtOrDefault(1))) yield return m;
    }

    Item Add(Item parent, Step step)
    {
        var item = new Item { Step = step, Parent = parent };
        parent.Children.Add(item);
        return item;
    }

    static string ExitSentence(Fact exit) => exit["kind"] switch
    {
        "throw" => $"throws {(exit["exception"] != "" ? exit["exception"] : "an exception")}",
        "Redirect" => $"sends the user elsewhere ({exit["result"]})",
        "StatusCode" => $"answers {exit["status"]}",
        _ => $"answers {exit["kind"]}{(exit["status"] != "" ? $" ({exit["status"]})" : "")}",
    };

    static IEnumerable<Item> Subtree(Item i)
    {
        yield return i;
        foreach (var c in i.Children)
            foreach (var x in Subtree(c)) yield return x;
    }

    // ---- finishing: numbers, links, guesses, scenarios, actor, checks --------------------------------------------------

    public void Finish()
    {
        var order = Subtree(root).Skip(1).ToList();
        var numbers = new Dictionary<Item, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < order.Count; i++) numbers[order[i]] = i + 1;
        foreach (var item in order)
        {
            var st = item.Step;
            st.N = numbers[item];
            var step = new Step
            {
                N = st.N, From = item.Parent is { } p && numbers.TryGetValue(p, out var pn) ? pn : null, What = st.What, At = st.At, Point = st.Point,
                Guessed = st.Guessed, GuessReason = st.GuessReason, Conditions = st.Conditions, End = st.End, Kind = st.Kind, Runs = st.Runs, Depth = st.Depth,
                BackTo = item.BackTo is { } b ? (numbers.TryGetValue(b, out var bn) ? bn : 0) : null,
                SameAs = item.SameAs is { } sa && numbers.TryGetValue(sa, out var sn) ? sn : null,
            };
            flow.Steps.Add(step);
            if (step.Guessed) flow.Guesses.Add(new Guess(step.N, step.What, step.GuessReason ?? "no reason recorded"));
        }
        foreach (var item in Subtree(root))
        {
            int? n = item == root ? null : numbers[item];
            foreach (var l in item.Links)
            {
                flow.Links.Add(l with { Step = n });
                if (l.Guessed) flow.Guesses.Add(new Guess(n, l.Sentence, l.Kind == "message" ? "handlers found by the event's type" : "a queued command matched to its job by type"));
            }
            foreach (var u in item.Unknowns) flow.Unknowns.Add(u with { Step = n });
            if (item.Step.Kind != "package" && (item.Step.Kind == "unknown" || item.Step.End?.Kind == "unknown")) flow.Unknowns.Add(new Unknown(n, item.Step.Point ?? "", item.Step.At, item.Step.End?.Detail ?? item.Step.What));
            foreach (var (exit, _) in item.Exits)
            {
                flow.Exits.Add(exit with { Step = n });
                // Scenario: the conditions on the way to the exit, innermost first (D179).
                var path = new List<Condition>(exit.Conditions);
                for (var up = item; up is not null && up != root; up = up.Parent) path.AddRange(up.Step.Conditions);
                flow.Scenarios.Add(new Scenario($"{exit.What} ({exit.At})", path, flow.Exits.Count));
            }
        }
        flow.Scenarios.Insert(0, new Scenario("main path: no early exit taken", [], null));
        if (!ActorFixed) flow.Actor = ActorOf();
        if (flow.Actor.Guessed) flow.Guesses.Insert(0, new Guess(null, $"actor: {flow.Actor.Who}", flow.Actor.Why));
        Checks();
        flow.DisplayOnly.Sort(StringComparer.Ordinal);
    }

    /// <summary>The actor by D179's fixed rule: the access rule on the first endpoint the flow calls, else the screen's guard.</summary>
    Actor ActorOf()
    {
        var action = firstAction ?? (g.Nodes.TryGetValue(flow.Start.Point, out var n) && g.Facts(n.Id, "auth").Any() ? n.Id : null);
        if (action is not null)
        {
            var auths = g.Facts(action, "auth").OrderBy(f => f["from"] == "method" ? 0 : 1).ThenBy(f => f["from"], StringComparer.Ordinal).ToList();
            var claims = auths.Where(f => f["rule"] == "filter" && f["basis"] is "denies" or "name").ToList();
            var at = auths.FirstOrDefault() is { } a0 && a0.File is not null ? new At(a0.File, a0.Line, g.Code(a0.File, a0.Line)) : null;
            if (claims.Count > 0)
                return new Actor($"user who passes {string.Join(" and ", claims.Select(c => $"{c["filter"]}({c["args"]})"))}", $"access filter on the action ({claims[0]["from"]})", at, false);
            if (auths.FirstOrDefault(f => f["rule"] == "anonymous") is { } anon)
                return new Actor("anyone, signed in or not", $"[AllowAnonymous] ({anon["from"]})", at, false);
            if (auths.FirstOrDefault(f => f["rule"] == "authorize") is { } auth)
                return new Actor($"signed-in user{(auth["roles"] != "" ? $" in role {auth["roles"]}" : "")}{(auth["policy"] != "" ? $" meeting policy {auth["policy"]}" : "")}", $"[Authorize] ({auth["from"]})", at, false);
            // The fallback policy is set where the app starts, often another project than the controllers': the action's own
            // project's first, else the repo's only one (two apps with different ones → not guessed).
            var project = g.Nodes.TryGetValue(action, out var an) ? an.Project : null;
            var fallbacks = g.AllFacts("fallback-auth").Where(f => f["authenticatedUser"] == "required").ToList();
            var fb = fallbacks.FirstOrDefault(f => f.Owner == project) ?? (fallbacks.Select(f => f.Owner).Distinct(StringComparer.Ordinal).Count() == 1 ? fallbacks[0] : null);
            if (fb is not null)
                return new Actor("signed-in user", "no access rule on the action; the app's fallback policy requires a signed-in user", fb.File is null ? null : new At(fb.File, fb.Line, g.Code(fb.File, fb.Line)), false);
            return new Actor("anyone", "no [Authorize], access filter or fallback policy found on the first endpoint", at, true);
        }
        var guards = g.Facts(flow.Start.Point, "guard").OrderBy(f => f["name"], StringComparer.Ordinal).ToList();
        if (guards.Count > 0)
            return new Actor($"user who passes {string.Join(", ", guards.Select(x => x["name"]).Distinct(StringComparer.Ordinal))}", "the route's guards", new At(guards[0].File ?? "", guards[0].Line, g.Code(guards[0].File, guards[0].Line)), true);
        return new Actor("unknown", "the flow calls no endpoint and its screen has no guard", null, true);
    }

    /// <summary>The validation rules on the request body of every endpoint the flow calls (D174, D179).</summary>
    void Checks()
    {
        var bodies = flow.Steps.Where(st => st.Kind == "call" && st.Point is { } p && g.Facts(p, "endpoint-types").Any())
            .SelectMany(st => g.Facts(st.Point!, "endpoint-types").Select(f => f["body"])).Where(b => b != "").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        foreach (var body in bodies)
        {
            foreach (var prop in g.Out(body, EdgeKind.Contains).Select(e => e.Target).Where(t => g.Nodes.TryGetValue(t, out var n) && n.Kind == NodeKind.Property).Order(StringComparer.Ordinal))
                if (!flow.Inputs.Contains(g.Name(prop), StringComparer.Ordinal)) flow.Inputs.Add(g.Name(prop));
            foreach (var rule in g.AllRules(body))
                flow.Checks.Add(new Check($"{g.Name(rule.Owner)}: {rule["rule"]}{(rule["args"] != "" ? $"({rule["args"]})" : "")}", rule["rule"], rule.Owner,
                    new At(rule.File ?? "", rule.Line, g.Code(rule.File, rule.Line)), rule["message"],
                    rule["modifiers"].Split('|').Contains("AsWarning", StringComparer.Ordinal) || rule["severity"] == "Warning"));
        }
    }
}

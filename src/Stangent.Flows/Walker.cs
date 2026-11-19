using Stangent.Graph;

namespace Stangent.Flows;

/// <summary>
/// Walks one flow forward from its start (D172): each point's call sites in source-line order (D176), into what each call runs,
/// until a branch ends. Steps are numbered in walk order at the end, after display-only parts are left out.
/// </summary>
internal sealed partial class Walker(FlowGraph g, FlowSettings s, Flow flow)
{
    sealed class Item
    {
        public required Step Step;
        public List<Item> Children { get; } = [];
        public List<(Exit Exit, Item? Of)> Exits { get; } = [];
        /// <summary>D300: thrown errors a caller's <c>catch</c> takes: not stops.</summary>
        public List<Exit> Handled { get; } = [];
        /// <summary>The point whose code this item's step is written in (its <c>At</c>'s point); null for the start's own steps.</summary>
        public string? In;
        public List<Link> Links { get; } = [];
        public List<Unknown> Unknowns { get; } = [];
        public Item? BackTo, SameAs;
        public Item? Parent;
        /// <summary>D316: under this step a mutation is started (a value from a hook's result is called, <c>mutate(…)</c>), so a request a
        /// mutation hook declares below it is sent.</summary>
        public bool Starts;
    }

    readonly Item root = new() { Step = new Step { What = "start", At = flow.Start.At, Kind = "start" } };
    readonly Dictionary<string, Item> walked = new(StringComparer.Ordinal);
    /// <summary>D316: points holding a mutation's request or function, walked where no mutation was started (their requests only
    /// declared): kept apart, so a later started call into the same point walks it again and follows the request.</summary>
    readonly Dictionary<string, Item> walkedDeclared = new(StringComparer.Ordinal);
    /// <summary>Every point walked, either way.</summary>
    IEnumerable<string> WalkedPoints => walked.Keys.Union(walkedDeclared.Keys, StringComparer.Ordinal);

    /// <summary>D316: the walked map for a call into <paramref name="point"/> under <paramref name="at"/>.</summary>
    Dictionary<string, Item> WalkedFor(string point, Item at) => !StartedAt(at) && HoldsMutation(point) ? walkedDeclared : walked;

    /// <summary>D316: the point's own code hands a mutation hook a request or a function (a <c>mutation:</c> deferral, D214).</summary>
    bool HoldsMutation(string point) =>
        g.Facts(point, "http-match").Concat(g.Facts(point, "call-site")).Any(f => f["deferred"].StartsWith("mutation:", StringComparison.Ordinal));
    readonly HashSet<string> endpointsSeen = new(StringComparer.Ordinal);
    /// <summary>D191: an onclick's route, waiting for the line in its script that sends to the URL the onclick passed (<c>point@arg</c>).</summary>
    readonly Dictionary<string, List<Fact>> pending = new(StringComparer.Ordinal);
    string? firstAction;
    readonly Stack<string> visiting = new();
    /// <summary>D310: the class of the object each visited point runs on (null: not known), and whether it is the shared flow's own
    /// receiver, carried down from its start through calls on <c>this</c>.</summary>
    readonly Stack<Receiver> receivers = new();
    /// <summary>D310: a shared flow walked for one receiving class (the class a flow entered it through); null: the class its code is in.</summary>
    public string? RootReceiver { get; init; }
    /// <summary>D310: a virtual call on <c>this</c> made on the shared flow's own receiver still runs one of several overrides, so a
    /// receiving class would narrow it: the shared flow is walked once per receiving class.</summary>
    public bool ReceiverDependent { get; private set; }
    /// <summary>D310: shared flows this one continues into through calls on <c>this</c> made on its own receiver.</summary>
    public HashSet<string> RootSelfLinks { get; } = new(StringComparer.Ordinal);
    public HashSet<Fact>? OnLoadSeen { get; init; }
    /// <summary>D316: the walk starts inside a started mutation (a shared flow entered by a call that starts it): requests mutation hooks
    /// declare are followed. False for a flow's own start and for a shared flow entered where nothing starts one.</summary>
    public bool StartedRoot { get; init; }
    /// <summary>D316: the walk followed a mutation's request or function only because <see cref="StartedRoot"/> said it was started, so a
    /// flow entering this shared code where nothing starts one needs a walk of its own.</summary>
    public bool MutationDependent { get; private set; }
    public bool ActorFixed { get; init; }
    static readonly HashSet<string> MeaningfulKinds = ["http", "database", "outside-call", "message", "queue", "push", "file"];

    /// <summary>The flow reaches a server call, a save, an outside call, a message or queued work (D171's test for a click).</summary>
    public bool Meaningful => Subtree(root).Any(i => MeaningfulKinds.Contains(i.Step.Kind));
    /// <summary>D306: the keepers of code the walk left for a later call (a "waits" end), in name order.</summary>
    public IReadOnlyList<string> Waits => [.. Subtree(root).Where(i => i.Step.End?.Kind == "waits").Select(i => i.Step.End!.Detail).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

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

    /// <summary>
    /// A shared flow's start (D189): the hub or depth stop's own code, walked once for every flow that continues into it. A point
    /// written to an interface runs its implementations, one branch each (🟡 when several), as a call to it would.
    /// </summary>
    public void RootShared(string point)
    {
        walked[point] = root;
        var self = new Receiver(RootReceiver ?? DocIds(point), true);
        var runs = RootReceiver is null ? Runs(point) : Narrow(Runs(point), self.Type);
        if (runs.Count == 1 && runs[0] == point) { Visit(point, root, 0, [point], self); return; }
        foreach (var run in runs)
        {
            var node = g.Nodes[run];
            var line = g.NameLine(node);
            var item = Add(root, new Step
            {
                What = $"runs {node.Name}", At = new At(node.File ?? "", line, g.Code(node.File, line)), Kind = "call", Point = run, Depth = 0,
                Guessed = runs.Count > 1, GuessReason = runs.Count > 1 ? OneOf(point, runs) : null,
            });
            PluginCalls(point, item, run, item.Step.At, [], 0);
            Into(item, run, 0, [point], self);
        }
    }

    /// <summary>What a shared flow for a call to <paramref name="target"/> starts at: the one point it runs, else the target itself.</summary>
    string SharedPoint(string target) => Runs(target) is [var only] ? only : target;

    /// <summary>D310: a call on <c>this</c> narrowed to <paramref name="runs"/>: the one it runs, else the target itself.</summary>
    static string SharedPoint(string target, List<string> runs) => runs is [var only] ? only : target;

    /// <summary>
    /// D310: a shared flow entered through a call on <c>this</c> runs on the caller's object: the link carries its class when that is
    /// narrower than the class the shared code is written in, so the shared flow can be walked for that class.
    /// </summary>
    Link SharedLink(Link link, Receiver? receiver, bool started)
    {
        // D316: a shared flow entered where no mutation was started is walked so (its mutation hooks' requests only declared).
        if (!started) link = link with { Declared = true };
        if (receiver is not { Type: { } type } r || link.ToFlow is not { } to) return link;
        if (r.FromRoot) RootSelfLinks.Add(to);
        return type == DocIds(to) ? link : link with { Receiver = type };
    }

    List<string> Runs(string target) => [.. g.Dispatch(target).Where(t => g.Nodes.TryGetValue(t, out var n) && n.File is not null && !n.IsTest)];

    // ---- the walk ------------------------------------------------------------------------------------------------------

    void Visit(string point, Item parent, int depth, List<string> branch, Receiver? receiver = null)
    {
        if (!g.Nodes.TryGetValue(point, out var node)) return;
        visiting.Push(point);
        // D310: a point entered other than through `this` runs on an object of its own class (or one built on it).
        receivers.Push(receiver ?? new Receiver(DocIds(point), false));
        try { VisitLines(point, node, parent, depth, branch); }
        finally { visiting.Pop(); receivers.Pop(); }
    }

    /// <summary>D310: the class an object is known to be (it or a class built on it), and whether it is a shared flow's own receiver.</summary>
    internal readonly record struct Receiver(string? Type, bool FromRoot);

    /// <summary>
    /// D310: the overrides a virtual call on <c>this</c> can run when the object is <paramref name="receiver"/> or a class built on it:
    /// the one the receiver's nearest class provides (itself, else its nearest base), and those of classes built on it. Not known, or
    /// nothing fits: every one, as before.
    /// </summary>
    List<string> Narrow(List<string> runs, string? receiver)
    {
        if (receiver is null || runs.Count <= 1) return runs;
        var below = g.Implementers(receiver);
        below.Remove(receiver);
        var typeOf = runs.ToDictionary(r => r, r => DocIds(r), StringComparer.Ordinal);
        var nearest = g.BaseChain(receiver).FirstOrDefault(t => runs.Any(r => typeOf[r] == t));
        var kept = runs.Where(r => typeOf[r] is { } t && (t == nearest || below.Contains(t))).ToList();
        return kept.Count == 0 ? runs : kept;
    }

    void VisitLines(string point, Node node, Item parent, int depth, List<string> branch)
    {
        flow.Reached.Add(point);
        var file = node.File ?? "";
        var callSites = g.Facts(point, "call-site").ToList();
        var lines = callSites.Select(f => f.Line)
            .Concat(g.Facts(point, "access").Select(f => f.Line)).Concat(g.Facts(point, "call").Select(f => f.Line))
            .Concat(g.Facts(point, "queue").Select(f => f.Line)).Concat(g.Facts(point, "job").Select(f => f.Line))
            .Concat(g.Facts(point, "http-match").Select(f => f.Line)).Concat(g.Facts(point, "exit").Select(f => f.Line))
            .Concat(g.Facts(point, "on-load").Select(f => f.Line)).Concat(g.BlindByOwner[point].Select(b => b.Line))
            .Concat(g.Facts(point, "navigate").Select(f => f.Line)).Concat(g.Facts(point, "creates-one-of").Select(f => f.Line))
            .Concat(g.Facts(point, "file-change").Select(f => f.Line)).Concat(g.Facts(point, "send").Select(f => f.Line)).Concat(g.Facts(point, "send-arg").Select(f => f.Line))
            .Distinct().Order().ToList();
        // D193: a component's fields are made with it: an initialiser that calls something (`x = this.svc.makeResource(…)`) runs
        // when the component is created, before its own code. A field holding a function runs only when called: not here.
        if (node.Kind == NodeKind.Component)
            foreach (var field in g.Out(point, EdgeKind.Contains).Select(e => e.Target).Distinct(StringComparer.Ordinal)
                         .Where(t => g.Nodes.TryGetValue(t, out var n) && n.Kind is NodeKind.Property or NodeKind.Field && !g.HoldsFunction(n) && g.Facts(t, "call-site").Any())
                         .OrderBy(t => g.Nodes[t].Line).ThenBy(t => t, StringComparer.Ordinal))
                if (!branch.Contains(field, StringComparer.Ordinal) && walked.TryAdd(field, parent)) Visit(field, parent, depth, [.. branch, field]);
        var later = g.Facts(point, "job").Select(f => f["runs"]).ToHashSet(StringComparer.Ordinal);
        var conditions = g.Facts(point, "condition").ToLookup(f => f.Line);
        // D306: code handed over to be kept for a later call (a menu action's callback set at selection time) is one boundary step
        // per keeper, at the first line of it, so the cut shows instead of the code silently missing.
        var keptLines = KeptWork(point).GroupBy(f => f["deferred"][5..], StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Min(f => f.Line), StringComparer.Ordinal);

        foreach (var line in lines)
        {
            var at = new At(FileAt(point, line, file), line, g.Code(FileAt(point, line, file), line));
            // D201: code in a function handed to code that keeps it runs later, when something else calls it: not this flow's.
            static bool Now(Fact f) => !f["deferred"].StartsWith("kept:", StringComparison.Ordinal);
            var sites = callSites.Where(f => f.Line == line && Now(f)).OrderBy(f => f["target"], StringComparer.Ordinal).ToList();
            var conds = conditions[line].OrderBy(f => f["target"], StringComparer.Ordinal).ToList();
            // D215: a call with no condition fact of its own has none; another call's on the line (one inside a function it's
            // given, `_cache.Get(k, () => Load())`, or after its `?.`) isn't its. A step not tied to a call takes the line's.
            var ownedBy = callSites.Where(f => f.Line == line).Select(f => f["target"]).Where(t => t != "").ToHashSet(StringComparer.Ordinal);
            List<Condition> CondsFor(string? target) =>
                FlowGraph.ConditionsOf(conds.FirstOrDefault(c => target is not null && c["target"] == target)
                    ?? conds.FirstOrDefault(c => target is null or "" || !ownedBy.Contains(c["target"])) ?? Empty);
            var consumed = new HashSet<string>(StringComparer.Ordinal);

            var https = Narrow(g.Facts(point, "http-match").Where(f => f.Line == line && Now(f)), branch).OrderBy(f => f["endpoint"], StringComparer.Ordinal).ToList();
            if (https.Count > 0 && OnclickScript(point, line) is { } script)
            {
                // D191: the button's onclick runs a script function with the route as an argument; the script is walked, and the
                // route is the request where the script sends to that argument. If it never does, the route stays here.
                var key = $"{script.Point}@{script.Arg}";
                pending[key] = https;
                var item = Add(parent, new Step { What = $"runs {g.Name(script.Point)}, the button's onclick", At = at, Kind = "call", Point = script.Point, Depth = depth,
                    Guessed = true, GuessReason = "the view's onclick calls a script function by its global name; which script files the page loads isn't read" });
                Into(item, script.Point, depth, branch);
                if (pending.Remove(key, out var left)) https = left; else https = [];
            }
            foreach (var http in https)
            {
                Http(http, at, CondsFor(null), parent, depth, branch);
                consumed.UnionWith(sites.Select(x => x["target"]));
            }
            var opened = false;
            foreach (var open in g.Facts(point, "call-open").Where(f => f.Line == line && Now(f)).OrderBy(f => f["open"], StringComparer.Ordinal))
            {
                if (!pending.Remove(open["open"], out var routes)) continue;
                opened = true;
                foreach (var http in routes) Http(http, at, CondsFor(null), parent, depth, branch, $"the URL is the one the button's onclick passes ({open["verb"]} here)");
            }
            // D201: one step per table and mode on a line (a repository method and the query inside it read the same table once),
            // the proved one first.
            foreach (var access in g.Facts(point, "access").Where(f => f.Line == line && g.TableOf.ContainsKey(f["entity"]))
                         .GroupBy(f => (Table: g.TableOf[f["entity"]], Mode: f["mode"])).OrderBy(x => x.Key.Table, StringComparer.Ordinal).ThenBy(x => x.Key.Mode, StringComparer.Ordinal)
                         .Select(x => x.OrderBy(f => f["via"] is "repository" or "navigation" ? 1 : 0).ThenBy(f => f["entity"], StringComparer.Ordinal).First()))
            {
                var table = g.TableOf[access["entity"]];
                // Step 6 (D236): a repository call followed into the database library's own call is proved, not read off its name.
                var proved = access["via"] == "repository" && g.Facts(point, "access-proof").Any(p => p.Line == line && p["entity"] == access["entity"] && p["mode"] == access["mode"]);
                Add(parent, new Step
                {
                    What = $"{(access["mode"] == "write" ? "saves" : "reads")} {table}", At = at, Kind = "database", Conditions = CondsFor(null),
                    End = new End("database", table), Depth = depth, Guessed = (access["via"] == "repository" && !proved) || access["via"] == "navigation",
                    GuessReason = proved ? null : access["via"] == "repository" ? "a repository method read as a save or read by its name" : access["via"] == "navigation" ? "a navigation collection's Add or Remove read as a save" : null,
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
                var item = Add(parent, new Step { What = $"queues {g.TypeName(q["type"])}, which runs later, in the background", At = at, Kind = "queue", Conditions = CondsFor(null), Depth = depth, Guessed = true, GuessReason = QueueReason });
                foreach (var j in jobs) item.Links.Add(new Link("runs-later", g.TypeName(q["type"]), j, at, $"Queues the job {g.TypeName(q["type"])}, which runs later, in the background ({at})", null, true, QueueReason));
                if (jobs.Count == 0) item.Unknowns.Add(new Unknown(null, point, at, $"no job runs {g.TypeName(q["type"])}"));
                consumed.UnionWith(sites.Select(x => x["target"]));
            }
            foreach (var j in g.Facts(point, "job").Where(f => f.Line == line).OrderBy(f => f["runs"], StringComparer.Ordinal))
            {
                var item = Add(parent, new Step { What = $"queues {g.Name(j["runs"])} through {j["via"]}, which runs later, in the background ({j["schedule"]})", At = at, Kind = "queue", Conditions = CondsFor(null), Depth = depth });
                item.Links.Add(new Link("runs-later", g.Name(j["runs"]), j["runs"], at, $"Queues {g.Name(j["runs"])}, which runs later, in the background ({at})", null, false));
                consumed.UnionWith(sites.Select(x => x["target"]));
            }
            // Step 6 (fresh check S27): an instance made by reflection and cast to a type: one of that type's implementations.
            foreach (var made in g.Facts(point, "creates-one-of").Where(f => f.Line == line))
            {
                var impls = g.Implementers(made["type"]).Where(t => t != made["type"] && g.Nodes.TryGetValue(t, out var n) && n.Kind == NodeKind.Type && !n.IsTest)
                    .Order(StringComparer.Ordinal).ToList();
                var type = g.TypeName(made["type"]);
                var step = Add(parent, new Step
                {
                    What = $"makes an instance of a type it is given, as {type}", At = at, Kind = "call", Conditions = CondsFor(null), Depth = depth,
                    Guessed = impls.Count > 0, GuessReason = impls.Count > 0
                        ? $"one of {impls.Count}: {string.Join(", ", impls.Select(g.TypeName))}; which one runs depends on the type it is given at run time" : null,
                });
                if (impls.Count == 0) step.Unknowns.Add(new Unknown(null, point, at, $"reflection: an instance cast to {type}, which nothing in the repo implements"));
            }
            // D192: a navigation that isn't an exit leaves for another screen: a step ending `screen`.
            foreach (var nav in g.Facts(point, "navigate").Where(f => f.Line == line && Now(f)).OrderBy(f => f["to"], StringComparer.Ordinal))
            {
                var step = Add(parent, new Step { What = $"goes to {nav["to"]} ({nav["via"]})", At = at, Kind = "navigate",
                    Conditions = WithWhen(When(nav, at), nav["conditions"] != "" ? FlowGraph.ConditionsOf(nav) : CondsFor(null)), Depth = depth,
                    End = new End("screen", nav["to"]) });
                ScreenLinks(step, at, nav["to"], g.PagesAt(nav["to"], at.File, nav["root"] == "1"));
            }
            // D292: a SignalR message pushed to the browser, sent directly or through the app's wrapper (whose call isn't walked).
            foreach (var push in Pushes(point, line, sites).OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var item = Add(parent, new Step
                {
                    What = push.Name is { } name ? $"pushes {name} to the browser" : "pushes a message named at runtime to the browser", At = at, Kind = "push",
                    Conditions = CondsFor(push.Target), End = new End("push", push.Name ?? "a message named at runtime"), Depth = depth, Guessed = push.Name is not null && push.Target is not null,
                    GuessReason = push.Name is not null && push.Target is not null ? "sent through the app's own wrapper, whose name parameter reaches SignalR's SendAsync" : null,
                });
                if (push.Name is null) item.Unknowns.Add(new Unknown(null, point, at, "SignalR message name built at runtime"));
                if (push.Target is not null) consumed.Add(push.Target);
            }
            // D294: a file written or deleted (the walk already goes through an app's own wrappers, so the call at the bottom is enough).
            var fileMethods = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fc in g.Facts(point, "file-change").Where(f => f.Line == line && Now(f)).OrderBy(f => f["method"], StringComparer.Ordinal))
            {
                if (!fileMethods.Add(fc["method"])) continue;
                Add(parent, new Step { What = $"{fc["change"]} a file through {fc["method"]}", At = at, Kind = "file", Conditions = CondsFor(""), End = new End("file", $"{fc["change"]} a file ({fc["method"]})"), Depth = depth });
            }
            foreach (var exit in g.Facts(point, "exit").Where(f => f.Line == line && Now(f)).OrderBy(f => f["text"], StringComparer.Ordinal))
            {
                var stop = new Exit(exit["kind"], ExitSentence(exit), at, FlowGraph.ConditionsOf(exit), null, point);
                if (exit["kind"] == "throw")
                {
                    // D300: a thrown error a catch on the way takes is handled, not a stop.
                    var (caught, unsure, turned, ends) = Caught(point, line, exit, parent);
                    if (turned is not null) stop = stop with { What = $"{stop.What}, {turned}" };
                    if (caught is not null) { parent.Handled.Add(stop with { CaughtBy = caught, CatchEnds = ends }); continue; }
                    if (unsure is not null) stop = stop with { Unsure = unsure };
                }
                parent.Exits.Add((stop, parent));
                // D204: a redirect leaves for another screen: a link to that screen's opening flow.
                if (exit["kind"] == "Redirect")
                {
                    // Step 6 (fresh check S21): a redirect to the request's own URL (http ↔ https) is the same page: certain.
                    if (exit["samePage"] == "1" || (exit["urlFrom"] != "" && g.Dispatch(exit["urlFrom"]).Any(t => g.Facts(t, "request-url").Any())))
                    {
                        parent.Links.Add(new Link("screen", "the same page", null, at, $"Sends the user back to the page they asked for, at its own address ({at})", null, false));
                        continue;
                    }
                    var (to, screens) = point.StartsWith("ts|", StringComparison.Ordinal) ? (exit["result"], g.PagesAt(exit["result"], at.File, exit["root"] == "1")) : g.RedirectTarget(exit, point);
                    ScreenLinks(parent, at, to, screens);
                }
            }
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
                    // D294: a file write the package makes (NetVips, ImageSharp, CopyToAsync onto a file) is its file step already.
                    if (fileMethods.Contains(site["method"])) continue;
                    // D188: a package's code is a step, not walked (it isn't on the map).
                    if (consumed.Count == 0 && !later.Contains(target) && IdentityStore(site) is { } store)
                    {
                        // D200: ASP.NET Identity's managers read and save the user store; which tables, the package's own code picks.
                        // D291: sorted by the method's verb; a verb on neither list stays "reads or saves".
                        var verb = IdentityVerb(site["method"]);
                        Add(parent, new Step { What = $"{verb ?? "reads or saves"} the Identity user store through {site["method"]}", At = at, Kind = "database", Conditions = CondsFor(""), Depth = depth,
                            End = new End("database", store), Guessed = true,
                            GuessReason = verb is null
                                ? "an ASP.NET Identity manager's async call reads or saves its store (the user, role, claim and token tables); which ones, the package's code picks"
                                : $"an ASP.NET Identity manager's call read as one that {verb} its store by its method's verb; which tables, the package's code picks" });
                    }
                    else if (consumed.Count == 0 && !later.Contains(target) && s.IsOutside(site["ns"] == "" ? site["package"] : $"{site["ns"]}.{TypeOf(site["method"])}") && OutsideDup(point, line))
                        continue;   // D304(9): D199 already has this line's outside call
                    else if (consumed.Count == 0 && !later.Contains(target) && !LocalBuilder(site["method"]) && s.IsOutside(site["ns"] == "" ? site["package"] : $"{site["ns"]}.{TypeOf(site["method"])}"))
                        // D302: a known outside service's client library: an outside call, not a package call.
                        Add(parent, new Step { What = $"calls {site["method"]} outside the app ({site["package"]})", At = at, Kind = "outside-call", Conditions = CondsFor(""), Depth = depth,
                            End = new End("outside-call", $"{site["package"]}: {site["method"]}") });
                    else if (consumed.Count == 0 && !later.Contains(target))
                        Add(parent, new Step { What = $"calls {site["method"]} in the package {site["package"]}", At = at, Kind = "package", Conditions = CondsFor(""), Depth = depth,
                            // D199: an MVC action returning a view or a redirect leads to that screen.
                            End = MvcResult(site, at) is { } screen ? new End("screen", screen) : new End("unknown", $"a package's code ({site["package"]}), not walked") });
                    continue;
                }
                if (consumed.Contains(target) || later.Contains(target)) continue;
                Call(point, site, target, at, CondsFor(target), parent, depth, branch);
            }
            foreach (var keeper in keptLines.Where(k => k.Value == line).Select(k => k.Key).Order(StringComparer.Ordinal))
                Add(parent, new Step
                {
                    What = $"leaves code for later ({keeper}): it runs only when something calls it afterwards, such as a later click, so it isn't walked here",
                    At = at, Kind = "call", Conditions = CondsFor(null), End = new End("waits", $"kept by {keeper}, run by a later call"), Depth = depth,
                });
            foreach (var blind in g.BlindByOwner[point].Where(b => b.Line == line && !(opened && b.Reason.EndsWith("URL built at runtime", StringComparison.Ordinal))).OrderBy(b => b.Reason, StringComparer.Ordinal))
            {
                // D316: an HTTP call whose URL is built at runtime, made by a mutation's function (or by a hook that hands one its
                // function, such as `useApiMutation`) where no mutation was started: declared, not sent.
                if (blind.Reason.StartsWith("HTTP ", StringComparison.Ordinal)
                    && callSites.Any(f => f.Line == line && (f["deferred"].StartsWith("mutation:", StringComparison.Ordinal) || f["target"] != "" && HoldsMutation(f["target"])))
                    && !Sent(parent))
                {
                    var request = blind.Reason["HTTP ".Length..].Replace(" to a URL built at runtime", " (URL built at runtime)", StringComparison.Ordinal);
                    Add(parent, new Step
                    {
                        What = $"declares {request}, sent only when a click (or other code) calls `mutate(…)`, so not followed here",
                        At = new At(blind.File, blind.Line, g.Code(blind.File, blind.Line)), Kind = "call", Depth = depth, End = new End("waits", "declared by a mutation hook, sent when `mutate(…)` is called"),
                    });
                    continue;
                }
                var bat = new At(blind.File, blind.Line, g.Code(blind.File, blind.Line));
                // D198: an outside HTTP call whose URL is built at runtime is still an outside call: it ends `outside-call`, ❓ where.
                // D199: so is a mail client's (`outside SMTP …`).
                if (blind.Reason.StartsWith("outside HTTP ", StringComparison.Ordinal) || blind.Reason.StartsWith("outside SMTP ", StringComparison.Ordinal))
                {
                    var outside = Add(parent, new Step { What = $"calls outside the app: {blind.Reason}", At = bat, Kind = "outside-call", Conditions = CondsFor(null), Depth = depth,
                        End = new End("outside-call", blind.Reason) });
                    outside.Unknowns.Add(new Unknown(null, point, bat, blind.Reason));
                    continue;
                }
                Add(parent, new Step
                {
                    What = $"can't be followed: {blind.Reason}", At = bat,
                    // An HTTP call the graph couldn't match is still a server call (D171's test), ❓ where it goes.
                    Kind = blind.Reason.StartsWith("HTTP ", StringComparison.Ordinal) ? "http" : "unknown", End = new End("unknown", blind.Reason), Depth = depth,
                });
            }
        }
    }

    /// <summary>D306: the facts of a point whose code is kept for a later call (D201's <c>kept:</c>) and would do something: a call
    /// into the repo, a request, a navigation, a file change or a stop.</summary>
    IEnumerable<Fact> KeptWork(string point)
    {
        static bool Kept(Fact f) => f["deferred"].StartsWith("kept:", StringComparison.Ordinal) && f["deferred"].Length > 5;
        return g.Facts(point, "call-site").Where(f => Kept(f) && g.Nodes.ContainsKey(f["target"]))
            .Concat(g.Facts(point, "http-match").Where(Kept)).Concat(g.Facts(point, "navigate").Where(Kept))
            .Concat(g.Facts(point, "file-change").Where(Kept)).Concat(g.Facts(point, "exit").Where(Kept));
    }

    /// <summary>
    /// D190: a URL its callers give is one match per value, each saying which point gave it (<c>origin</c>). A flow keeps those its
    /// own code gave: an origin on its branch, reached, or called by a point on the branch or the component or hook holding one
    /// (the component that called <c>useManageIndexer</c>). None of them → every value, 🟡 as each already is.
    /// </summary>
    IEnumerable<Fact> Narrow(IEnumerable<Fact> matches, List<string> branch)
    {
        var all = matches.ToList();
        var given = all.Where(f => f["origin"] != "").ToList();
        if (given.Count == 0) return all;
        var context = new HashSet<string>(flow.Reached, StringComparer.Ordinal);
        foreach (var p in branch.Append(flow.Start.Point))
            foreach (var holder in Holders(p))
            {
                context.Add(holder);
                foreach (var site in g.Facts(holder, "call-site")) context.Add(site["target"]);
            }
        var kept = given.Where(f => context.Contains(f["origin"])).ToList();
        return kept.Count == 0 ? all : all.Where(f => f["origin"] == "" || kept.Contains(f));
    }

    /// <summary>D191: the script function a form's onclick calls with its route, when exactly one script point has that name.</summary>
    (string Point, string Arg)? OnclickScript(string point, int line)
    {
        var form = g.Facts(point, "form").FirstOrDefault(f => f.Line == line && f["script"] != "");
        if (form is null) return null;
        var name = form["script"];
        var named = g.ScriptPoints[name].ToList();
        return named.Count == 1 ? (named[0], form["scriptArg"]) : null;
    }

    /// <summary>A point and the points holding it: <c>ts|f.tsx#A.b.c</c> → itself, <c>…#A.b</c>, <c>…#A</c>.</summary>
    static IEnumerable<string> Holders(string point)
    {
        yield return point;
        var hash = point.IndexOf('#');
        if (hash < 0) yield break;
        for (var dot = point.LastIndexOf('.'); dot > hash; dot = point.LastIndexOf('.', dot - 1)) yield return point[..dot];
    }

    /// <summary>D200: an async call on ASP.NET Identity's <c>UserManager</c>, <c>SignInManager</c> or <c>RoleManager</c>: its store, named.</summary>
    static string? IdentityStore(Fact site)
    {
        var method = site["method"];
        var dot = method.LastIndexOf('.');
        if (dot < 0 || site["package"] is not ("Microsoft.Extensions.Identity.Core" or "Microsoft.AspNetCore.Identity" or "Microsoft.Extensions.Identity.Stores")) return null;
        var (type, name) = (method[..dot], method[(dot + 1)..]);
        return type is "UserManager" or "SignInManager" or "RoleManager" && name.EndsWith("Async", StringComparison.Ordinal)
            ? $"the Identity user store ({method})" : null;
    }

    static readonly string[] SaveVerbs = ["Add", "Update", "Remove", "Set", "Create", "Delete", "Reset", "Confirm", "Change"];
    static readonly string[] ReadVerbs = ["Get", "Find", "Verify", "Is", "Check", "Generate"];

    /// <summary>D291: "saves" or "reads" by the Identity method's first word (<c>UserManager.RemoveAuthenticationTokenAsync</c> saves); null for any other.</summary>
    internal static string? IdentityVerb(string method)
    {
        var name = method[(method.LastIndexOf('.') + 1)..];
        static bool Starts(string name, string verb) => name.StartsWith(verb, StringComparison.Ordinal) && (name.Length == verb.Length || !char.IsLower(name[verb.Length]));
        // D304(3): a sign-in check (SignInManager.CheckPasswordSignInAsync) may write the failed count and the lockout: "may save".
        if (name.StartsWith("Check", StringComparison.Ordinal) && name.Contains("SignIn", StringComparison.Ordinal)) return null;
        return SaveVerbs.Any(v => Starts(name, v)) ? "saves" : ReadVerbs.Any(v => Starts(name, v)) ? "reads" : null;
    }

    /// <summary>D304(9): a client library's local builder (<c>BlobServiceClient.GetBlobContainerClient</c>, <c>Create…Client</c>) makes an object; it calls nothing outside.</summary>
    internal static bool LocalBuilder(string method)
    {
        var name = method[(method.LastIndexOf('.') + 1)..];
        return (name.StartsWith("Get", StringComparison.Ordinal) || name.StartsWith("Create", StringComparison.Ordinal)) && name.EndsWith("Client", StringComparison.Ordinal);
    }

    /// <summary>D304(9): D199 or D198 already makes an outside step on this line (<c>outside SMTP …</c>, <c>outside HTTP …</c>).</summary>
    bool OutsideDup(string point, int line) =>
        g.BlindByOwner[point].Any(b => b.Line == line && (b.Reason.StartsWith("outside HTTP ", StringComparison.Ordinal) || b.Reason.StartsWith("outside SMTP ", StringComparison.Ordinal)));

    static string TypeOf(string method) => method.LastIndexOf('.') is var dot and > 0 ? method[..dot] : method;

    /// <summary>
    /// D292: the SignalR messages a line pushes: G10's <c>send</c> facts (sent directly, no call walked), the <c>send-arg</c> of a call
    /// to the app's wrapper (that call isn't walked), and a call to a wrapper with no name known: a push named at runtime (❓).
    /// </summary>
    List<(string? Name, string? Target)> Pushes(string point, int line, List<Fact> sites)
    {
        var list = new List<(string?, string?)>();
        foreach (var f in g.Facts(point, "send").Where(f => f.Line == line && f.Detector == "signalr")) list.Add((f["name"], null));
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in g.Facts(point, "send-arg").Where(f => f.Line == line && f.Detector == "signalr" && g.IsPushWrapper(f["target"], f["index"])))
        {
            var target = sites.FirstOrDefault(x => x["target"] == f["target"] || g.Out(x["target"], EdgeKind.DispatchesTo).Any(e => e.Target == f["target"]) || x["target"] != "" && f["target"] != "" && SameMember(x["target"], f["target"]))?["target"] ?? f["target"];
            list.Add((f["value"], target));
            named.Add(target);
        }
        foreach (var x in sites.Where(x => x["kind"] == "calls" && x["target"] != "" && !named.Contains(x["target"]) && g.IsPushWrapper(x["target"])))
            if (named.Add(x["target"])) list.Add((null, x["target"]));
        return [.. list.Distinct()];
    }

    /// <summary>The same member written for a generic type's own definition or a use of it (ids differ only in type arguments).</summary>
    static bool SameMember(string a, string b) => a.Split('(')[0] == b.Split('(')[0];

    /// <summary>
    /// D300: whether a thrown error is caught on its way up: the point's own <c>try</c> blocks around the line, innermost first, then
    /// each caller's around its call, up the walk (an HTTP hop, a filter or a flow's start ends it). A bare catch or one of
    /// <c>Exception</c> takes everything; a typed one the type and the types built on it; a <c>when</c> filter isn't certain, so it
    /// doesn't count; one that throws anything lets it go on up (D304: a new error it throws is what climbs on, "turned into X at
    /// file:line"). Its catch's place and what the catch returns (D304 item 7), or why it can't be told (❓; D304 item 10: a typed
    /// catch facing an error of unknown type).
    /// </summary>
    (At? Caught, string? Unsure, string? Turned, string? Ends) Caught(string point, int line, Fact exit, Item parent)
    {
        var types = exit["exceptionTypes"].Split('|', StringSplitOptions.RemoveEmptyEntries);
        string? unsure = null;
        var turned = new List<string>();
        string? Turned() => turned.Count == 0 ? null : string.Join(", then ", turned);
        string? p = point;
        var l = line;
        var cur = parent;
        while (p is not null)
        {
            foreach (var t in g.Facts(p, "try").Where(f => int.Parse(f["from"], System.Globalization.CultureInfo.InvariantCulture) <= l && l <= int.Parse(f["to"], System.Globalization.CultureInfo.InvariantCulture))
                         .OrderByDescending(f => int.Parse(f["from"], System.Globalization.CultureInfo.InvariantCulture)).ThenBy(f => f.Line))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(t["catches"]);
                foreach (var c in doc.RootElement.EnumerateArray())
                {
                    var type = c.GetProperty("type").GetString() ?? "?";
                    var at = new At(t.File ?? "", c.GetProperty("line").GetInt32(), g.Code(t.File, c.GetProperty("line").GetInt32()));
                    if (c.TryGetProperty("filtered", out var fl) && fl.ValueKind == System.Text.Json.JsonValueKind.True) continue;
                    if (type == "?") { unsure ??= $"can't tell whether the catch at `{at}` takes it: its type can't be worked out"; continue; }
                    var takes = type is "*" or "System.Exception" || types.Contains(type, StringComparer.Ordinal);
                    if (!takes)
                    {
                        if (types.Length == 0) unsure ??= $"can't tell whether the catch at `{at}` takes it: the error's type isn't known";
                        continue;
                    }
                    if (c.TryGetProperty("rethrows", out var rt) && rt.ValueKind == System.Text.Json.JsonValueKind.True)
                    {
                        if (c.TryGetProperty("becomes", out var bc) && bc.GetString() is { Length: > 0 } chain)
                        {
                            types = chain.Split('|', StringSplitOptions.RemoveEmptyEntries);
                            var name = types[0][(types[0].LastIndexOf('.') + 1)..];
                            turned.Add($"turned into {name} at {at}");
                        }
                        break;   // on up, past this try
                    }
                    return (at, null, Turned(), c.TryGetProperty("ends", out var en) ? en.GetString() : null);
                }
            }
            if (cur == root || cur.Step.Kind != "call" || cur.Parent is null || cur.Parent.Step.Kind == "http") break;
            p = cur.In;
            l = cur.Step.At.Line;
            cur = cur.Parent;
        }
        return (null, unsure, Turned(), null);
    }

    static readonly HashSet<string> MvcResults = ["View", "PartialView", "ViewComponent", "Redirect", "RedirectPermanent", "RedirectToAction", "RedirectToActionPermanent",
        "RedirectToRoute", "RedirectToRoutePermanent", "RedirectToPage", "RedirectToPagePermanent", "LocalRedirect", "LocalRedirectPermanent"];

    /// <summary>D199: ASP.NET MVC's own <c>View(…)</c> or <c>Redirect…(…)</c>, returned on its line: what the user is shown next.</summary>
    static string? MvcResult(Fact site, At at)
    {
        var method = site["method"];
        var dot = method.LastIndexOf('.');
        var (type, name) = dot < 0 ? ("", method) : (method[..dot], method[(dot + 1)..]);
        if (!site["package"].StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal) || type is not ("Controller" or "ControllerBase" or "PageModel" or "ViewComponent")
            || !MvcResults.Contains(name) || !at.Code.TrimStart().StartsWith("return ", StringComparison.Ordinal)) return null;
        return name.Contains("View", StringComparison.Ordinal) ? $"the view it shows ({name})" : $"the page it sends the user to ({name})";
    }

    static readonly Fact Empty = new("", "", "", null, 0, new Dictionary<string, string>());

    /// <summary>Facts and call sites carry their own file: a view's inline script belongs to the view (N5).</summary>
    string FileAt(string point, int line, string fallback) =>
        g.Facts(point, "call-site").FirstOrDefault(f => f.Line == line)?.File ?? g.Facts(point, "condition").FirstOrDefault(f => f.Line == line)?.File
        ?? g.Facts(point, "exit").FirstOrDefault(f => f.Line == line)?.File ?? g.Facts(point, "http-match").FirstOrDefault(f => f.Line == line)?.File
        // D201: a route's resolvers sit in the file of the route that names them (a lazy child's parent), not the page's.
        ?? g.Facts(point, "on-load").FirstOrDefault(f => f.Line == line)?.File ?? g.Facts(point, "guard").FirstOrDefault(f => f.Line == line)?.File
        ?? g.Facts(point, "navigate").FirstOrDefault(f => f.Line == line)?.File ?? fallback;

    void Call(string point, Fact site, string target, At at, List<Condition> conds, Item parent, int depth, List<string> branch)
    {
        // D206: a message is sent whether or not its class writes a constructor (an implicit one isn't on the map).
        if (FlowGraph.ConstructedType(target) is { } made)
        {
            // A message sent (D172 item 3): its handlers are linked flows; the walk never goes through the bus. A command a job
            // runs is queued work, read from the queue facts, not a message.
            var handlers = g.HandlersByEvent[made].Where(h => !g.IsTest(h)).Distinct(StringComparer.Ordinal).ToList();
            if (handlers.Count == 0 || g.JobsByCommand[made].Any()) return;
            var item = Add(parent, new Step { What = $"sends {g.TypeName(made)}", At = at, Kind = "message", Conditions = conds, End = new End("message", g.TypeName(made)), Depth = depth, Point = made });
            foreach (var h in handlers)
                item.Links.Add(new Link("message", g.Name(h), h, at, $"Sends {g.TypeName(made)}, handled by {g.Name(h)} ({at})", null, true, "handlers found by the event's type"));
            return;
        }
        if (!g.Nodes.TryGetValue(target, out var tnode)) return;
        // D189: reading a field or property (an Angular signal read, `this.seriesId()`) runs no code of its own: no step, no hub.
        // One that holds a function (`getPageUrl = (n) => …`) is code, called like a method.
        if (tnode.Kind is NodeKind.Field or NodeKind.Property && !g.HoldsFunction(tnode)) return;
        var kind = site["kind"] == "renders" ? "renders" : "call";
        var callback = site["callback"];
        var guessedSite = site["label"] == "1";
        // D201: code inside a function registered as an event listener runs when the event comes.
        var when = When(site, at);
        // D316: a mutation's own function (its `mutationFn`, `onSuccess`) runs only once the mutation is started: not walked where none was.
        if (when is { Kind: "mutation" } && !Sent(parent))
        {
            Add(parent, new Step
            {
                What = $"leaves {tnode.Name} to the mutation it sets up: it runs only when a click (or other code) calls `mutate(…)`, so not followed here", At = at, Kind = "call",
                Conditions = conds, Depth = depth, End = new End("waits", $"declared by {when.Case}, run when `mutate(…)` is called"),
            });
            return;
        }
        conds = WithWhen(when, conds);
        // D316: calling a value taken from a function's result (`mutate(…)`, `deleteEpisodeFile()`) starts what the function set up.
        var starts = site["fromResult"] == "1";
        // G5, D210: a value taken from a function's result (`const predicate = getPredicate(t)`, `const { refetch } = useX()`) and
        // called, when layer 2 couldn't tell which function was handed back: the function stands for it, a guess.
        // Step 6 (fresh check S4): `mutate(…)` taken from a hook that keeps a mutation's function runs that function: certain.
        var byValue = !guessedSite && site["fromResult"] == "1" && !StartsMutation(site);
        // D201: `base.M()` runs that method itself, no override.
        var runs = site["base"] == "1" && g.Nodes.TryGetValue(target, out var own) && own.File is not null ? [target] : Runs(target);
        // D310: a call on `this` runs on the same object: only the overrides its class can run, and the callee runs on it too.
        var onThis = site["self"] == "1" && site["base"] != "1" && receivers.Count > 0;
        var cur = onThis ? receivers.Peek() : default;
        Receiver? into = onThis ? cur : null;
        if (onThis)
        {
            runs = Narrow(runs, cur.Type);
            if (cur.FromRoot && runs.Count > 1) ReceiverDependent = true;
        }
        if (g.Callers(target) >= s.HubCallers && kind == "call")
        {
            // D310: a call on `this` narrowed to one override names that override.
            var hub = onThis && runs is [var one] && one != target && g.Nodes.TryGetValue(one, out var narrowed) ? narrowed : tnode;
            // D172's order puts a table before a hub: shared code whose own body reads or saves a table ends there, named.
            // Step 6 (D236, D237 item 2): guessed when the read or save itself is (a repository call not proved, a navigation
            // Add), as on a direct step; a table whose name comes from a mapping isn't "a repository method's name".
            var tables = (onThis ? runs : g.Dispatch(target)).SelectMany(t => g.Out(t, EdgeKind.Writes).Select(e => (Table: g.Name(e.Target), Write: true, Guess: AccessGuessed(t, g.Name(e.Target), "write")))
                .Concat(g.Out(t, EdgeKind.Reads).Select(e => (Table: g.Name(e.Target), Write: false, Guess: AccessGuessed(t, g.Name(e.Target), "read"))))).Distinct().OrderBy(x => x.Table, StringComparer.Ordinal).ToList();
            if (tables.Count > 0)
            {
                var names = string.Join(", ", tables.Select(x => x.Table).Distinct(StringComparer.Ordinal));
                var reads = Add(parent, new Step
                {
                    What = $"{(tables.Any(x => x.Write) ? "saves" : "reads")} {names} through {hub.Name}", At = at, Kind = "database", Point = hub.Id, Conditions = conds, Depth = depth,
                    End = new End("database", $"{names} ({hub.Name}, shared code used by {g.Callers(target)} places)"), Guessed = guessedSite || tables.Any(x => x.Guess),
                    GuessReason = guessedSite ? SiteReason(target) : tables.Any(x => x.Guess) ? "a save or read found by a detector's pattern (a repository method's name, a navigation collection)" : null,
                });
                // D196: its own code still runs (the save itself, events it sends): the shared walk continues it, as for any hub.
                reads.Links.Add(SharedLink(new Link("continues", hub.Name, onThis ? SharedPoint(target, runs) : SharedPoint(target), at, $"Shared code used by {g.Callers(target)} places, reading or saving {names}; {hub.Name}'s own walk continues it ({at})", null, false), into, starts || StartedAt(parent)));
                return;
            }
            var item = Add(parent, new Step
            {
                What = $"calls {hub.Name}{Mutate(site)}", At = at, Kind = kind, Point = hub.Id, Conditions = conds, Depth = depth,
                End = new End("hub", $"{hub.Name}: shared code used by {g.Callers(target)} places"), Guessed = guessedSite || byValue,
                GuessReason = guessedSite ? SiteReason(target) : byValue ? ValueReason(hub.Name) : null,
            });
            item.Links.Add(SharedLink(new Link("continues", hub.Name, onThis ? SharedPoint(target, runs) : SharedPoint(target), at, $"Stops here: shared code used by {g.Callers(target)} places; {hub.Name}'s own walk continues it ({at})", null, false), into, starts || StartedAt(parent)));
            return;
        }
        // D201: an event bus calling its handler interface (`handler.Handle(event)`) hands the event to its handlers, each a
        // flow of its own (D173): the bus isn't walked into them.
        if (runs.Count > 1 && (runs.All(g.HandlerPoints.Contains) || IsHandlerInterface(target)))
        {
            Add(parent, new Step { What = $"hands the event to its handlers ({runs.Count} handle events of this kind), each a flow of its own", At = at, Kind = "message", Point = target,
                Conditions = conds, Depth = depth, End = new End("message", $"the event's handlers, through {tnode.Name}"), Guessed = true,
                GuessReason = "an event bus calls each handler of the event's type; which ones run depends on the event sent" });
            return;
        }
        foreach (var run in runs)
        {
            var several = runs.Count > 1;
            var item = Add(parent, new Step
            {
                What = kind == "renders" ? $"shows {g.Name(run)}" : callback != "" ? $"when the request answers ({callback}), runs {g.Name(run)}" : $"calls {g.Name(run)}{Mutate(site)}", At = at, Kind = kind, Point = run, Conditions = conds, Depth = depth,
                Guessed = several || guessedSite || byValue,
                GuessReason = several ? OneOf(target, runs) + (onThis ? "; it is called on `this`, and which class the object is isn't known here" : "") : guessedSite ? SiteReason(target)
                    : byValue ? ValueReason(tnode.Name) : null,
            });
            item.Starts = starts;
            PluginCalls(point, item, run, at, conds, depth);
            Into(item, run, depth, branch, into);
            TimerLink(item, when, at, g.Name(run));
        }
    }

    /// <summary>
    /// D295: code a browser timer runs (<c>setTimeout</c>, <c>setInterval</c>, rxjs <c>timer</c>/<c>interval</c>) that calls the server
    /// or changes something: a "runs later, on a timer" link at its step. A timer that only tweaks the screen gets none.
    /// </summary>
    void TimerLink(Item item, Condition? when, At at, string name)
    {
        if (when is null || !(when.Kind == "timer" || when.Kind == "stream" && RxTimer().IsMatch(when.Code))) return;
        if (!Subtree(item).Any(i => MeaningfulKinds.Contains(i.Step.Kind)) && item.SameAs is null) return;
        if (item.SameAs is { } same && !Subtree(same).Any(i => MeaningfulKinds.Contains(i.Step.Kind))) return;
        var timer = when.Kind == "timer" ? when.Code : when.Code.Split('(')[0];
        item.Links.Add(new Link("runs-later", name, null, at, $"Runs {name} later, on a timer ({timer}) ({at})", null, false));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(timer|interval)\s*\(")]
    private static partial System.Text.RegularExpressions.Regex RxTimer();

    /// <summary>D204: a link to each screen a redirect or navigation can open; none known: one link saying where it goes, to no flow.</summary>
    void ScreenLinks(Item item, At at, string to, IReadOnlyList<string> screens)
    {
        if (screens.Count == 0)
        {
            item.Links.Add(new Link("screen", to == "" ? "a page chosen at runtime" : to, null, at,
                $"Sends the user to {(to == "" ? "a page chosen at runtime" : to)}; no screen in the code matches it, so it links to no flow ({at})", null, true,
                to == "" ? "the page it sends the user to is chosen at runtime" : $"no screen in the code matches {to}"));
            return;
        }
        foreach (var screen in screens)
            item.Links.Add(new Link("screen", g.ScreenName(screen), screen, at,
                screens.Count == 1 ? $"Sends the user to {g.ScreenName(screen)}, whose opening is its own flow ({at})"
                    : $"Sends the user to {to}, which {screens.Count} screens fit; {g.ScreenName(screen)} is one ({at})", null, screens.Count > 1,
                    screens.Count > 1 ? $"{to} fits {screens.Count} screens; each is a link" : null));
    }

    /// <summary>Whether a point's read or save of a table is a guess: every access to it there is a repository call no proof
    /// followed, or a navigation collection's Add; a table line with no access fact behind it (a join table) is a guess too.</summary>
    bool AccessGuessed(string point, string table, string mode)
    {
        var accesses = g.Facts(point, "access").Where(f => f["mode"] == mode && g.TableOf.TryGetValue(f["entity"], out var t) && t == table).ToList();
        if (accesses.Count == 0) return true;
        return accesses.All(f => f["via"] == "navigation" || f["via"] == "join"
            || (f["via"] == "repository" && !g.Facts(point, "access-proof").Any(p => p.Line == f.Line && p["entity"] == f["entity"] && p["mode"] == mode)));
    }

    /// <summary>D239: "one of N", naming what it depends on: the object of that type the call is made on when it runs.</summary>
    string OneOf(string target, List<string> runs)
    {
        var name = g.Name(target);
        var dot = name.LastIndexOf('.');
        var type = dot > 0 ? name[..dot] : name;
        return $"one of {runs.Count}: {string.Join(", ", runs.Select(g.Name))}; which one runs depends on the {type} the call is made on at run time";
    }

    /// <summary>Fresh check S4, S7: a call of <c>mutate</c>/<c>mutateAsync</c> taken from the result of a function that keeps a
    /// mutation's function (a call-site of its own deferred <c>mutation:</c>, D214).</summary>
    internal bool StartsMutation(Fact site) =>
        site["fromResult"] == "1" && site["target"] != "" && MutateCall().IsMatch(g.Code(site.File, site.Line))
        && g.Facts(site["target"], "call-site").Any(f => f["deferred"].StartsWith("mutation:", StringComparison.Ordinal));

    /// <summary>".mutate" when the call starts a hook's mutation (S4): the step names what its line calls.</summary>
    string Mutate(Fact site) => StartsMutation(site) && MutateCall().Match(g.Code(site.File, site.Line)) is { Success: true } m ? $".{m.Value.TrimEnd('(', ' ')}" : "";

    [System.Text.RegularExpressions.GeneratedRegex(@"\bmutate(Async)?\s*\(")]
    private static partial System.Text.RegularExpressions.Regex MutateCall();

    const string QueueReason = "a queued command matched to its job by type";

    const string OverloadReason = "the compiler couldn't pick one overload, so each in-repo candidate is a branch";

    /// <summary>
    /// D240 (was D201, D202, D212–D214's 🟡): code that certainly runs, only later or only when something happens, carries a
    /// condition saying when: a listener's function when the event comes, a stream's callback when it sends a value, a timer's
    /// function after its delay, a mutation's request when it's started, an element of a component that draws nothing while a
    /// prop is unset. Null: it runs now.
    /// </summary>
    internal static Condition? When(Fact f, At at) => f["deferred"] switch
    {
        var d when d.StartsWith("listener:", StringComparison.Ordinal) => new Condition("listener", d[9..], at.File, at.Line),
        var d when d.StartsWith("stream:", StringComparison.Ordinal) => d.LastIndexOf(" when ", StringComparison.Ordinal) is var w and > 7
            ? new Condition("stream", d[7..w], at.File, at.Line, d[(w + 6)..]) : new Condition("stream", d[7..], at.File, at.Line),
        var d when d.StartsWith("timer:", StringComparison.Ordinal) => new Condition("timer", d[6..], at.File, at.Line),
        var d when d.StartsWith("mutation:", StringComparison.Ordinal) => new Condition("mutation", "mutate(…)", at.File, at.Line, d[9..]),
        _ => Drawn(f["closedBy"], at),
    };

    /// <summary>D213, D240: an element inside a component that draws nothing while a prop is unset (<c>Modal|!isOpen|isOpen=open</c>).</summary>
    static Condition? Drawn(string closedBy, At at)
    {
        var parts = closedBy.Split('|');
        if (parts.Length != 3) return null;
        var shown = parts[1].StartsWith('!') ? parts[1][1..] : $"!({parts[1]})";
        return new Condition("drawn", shown, at.File, at.Line, $"{parts[0]} draws nothing otherwise; here {parts[2]}");
    }

    /// <summary>The step's conditions with D240's "when" first, so it is what the step reads with.</summary>
    static List<Condition> WithWhen(Condition? when, List<Condition> conds) => when is null ? conds : [when, .. conds];

    /// <summary>D201: a method of a handler interface (<c>IConsumer&lt;T&gt;.HandleEventAsync</c>): the bus calls each handler through it.</summary>
    bool IsHandlerInterface(string target)
    {
        var type = DocIds(target);
        var colon = type?.IndexOf("T:", StringComparison.Ordinal) ?? -1;
        return type is not null && colon >= 0 && g.HandlerInterfaces.Contains(type[(colon + 2)..]);
    }

    /// <summary>D201: a script's global declared in several files is a choice of files, not of overloads.</summary>
    static string SiteReason(string target) => target.StartsWith("ts|", StringComparison.Ordinal)
        ? "scripts declare a global with this name in more than one file; which one the page loads isn't read" : OverloadReason;

    /// <summary>Walks into a point unless it's already on this branch (a loop), already walked, or past the depth limit.</summary>
    void Into(Item item, string point, int depth, List<string> branch, Receiver? receiver = null)
    {
        var walked = WalkedFor(point, item);
        if (branch.Contains(point, StringComparer.Ordinal)) { item.BackTo = walked.GetValueOrDefault(point) ?? root; return; }
        if (walked.TryGetValue(point, out var before)) { item.SameAs = before; return; }
        if (depth + 1 > s.Depth)
        {
            item.Step.End = new End("depth", $"{s.Depth} real calls; not walked: {g.Name(point)}");
            item.Links.Add(SharedLink(new Link("continues", g.Name(point), point, item.Step.At, $"Stops here: {s.Depth} calls deep; {g.Name(point)}'s own walk continues it ({item.Step.At})", null, false), receiver, StartedAt(item)));
            return;
        }
        walked[point] = item;
        Visit(point, item, depth + 1, [.. branch, point], receiver);
        // A hook's own function that reaches no server work may still run the hook's library callbacks (a mutation's function runs
        // when it calls `mutate`): the hook's own body is walked too, 🟡 (D184).
        if (HookOf(point) is { } hook && !branch.Contains(hook, StringComparer.Ordinal) && !Subtree(item).Skip(1).Any(i => MeaningfulKinds.Contains(i.Step.Kind)) && g.Nodes.ContainsKey(hook))
        {
            // Fresh check S7: a function that calls `mutate` from its own hook starts the mutation that hook set up: the hook's own
            // body (the request it sets up) is walked under this call, certain, with no step of its own.
            if (g.Facts(point, "call-site").Any(StartsMutation))
            {
                item.Starts = true;
                if (this.walked.TryAdd(hook, item)) Visit(hook, item, depth + 1, [.. branch, point, hook]);
                return;
            }
            var h = Add(item, new Step
            {
                What = $"runs {g.Name(hook)}'s own work, through a library callback", At = item.Step.At, Kind = "call", Point = hook, Depth = depth + 1,
                Guessed = true, GuessReason = $"{g.Name(point)} reaches no server work itself; what {g.Name(hook)} hands a library (a mutation's function) may run when it's called",
            });
            // D316: a function the hook handed back is running, so what it set up may be started.
            h.Starts = true;
            if (this.walked.TryGetValue(hook, out var seen)) h.SameAs = seen;
            else { this.walked[hook] = h; Visit(hook, h, depth + 1, [.. branch, point, hook]); }
        }
        // A component that shows nothing reaching the server is display only: named, not walked into (D176 item 5).
        if (item.Step.Kind == "renders" && !Subtree(item).Skip(1).Any(i => MeaningfulKinds.Contains(i.Step.Kind)))
        {
            item.Parent!.Children.Remove(item);
            walked.Remove(point);
            if (!flow.DisplayOnly.Contains(g.Name(point), StringComparer.Ordinal)) flow.DisplayOnly.Add(g.Name(point));
        }
    }

    void Http(Fact http, At at, List<Condition> conds, Item parent, int depth, List<string> branch, string? because = null)
    {
        var endpoint = http["endpoint"];
        if (When(http, at) is { Kind: "mutation" } && !Sent(parent))
        {
            Declares(http, at, conds, parent, depth);
            return;
        }
        conds = WithWhen(When(http, at), conds);
        var guessed = http["label"] != "certain" || because is not null;
        var reason = string.Join("; ", new[] { because, http["reason"] }.Where(r => !string.IsNullOrEmpty(r)));
        var item = Add(parent, new Step
        {
            // D201: a form's or onclick's match has no URL of its own: the endpoint names it.
            What = http["url"] == "" && endpoint != "" ? $"calls {g.Name(endpoint)}" : $"calls {http["verb"]} {http["url"]}".TrimEnd(), At = at, Kind = "http", Point = endpoint, Conditions = conds, Depth = depth,
            Guessed = guessed, GuessReason = guessed ? (reason != "" ? reason : "no reason recorded") : null,
        });
        TimerLink(item, When(http, at), at, item.Step.What["calls ".Length..]);
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

    /// <summary>
    /// D316: whether a mutation started on the way here: a step above it starts one (it calls a value from a hook's result, such as
    /// <c>mutate(…)</c>, or a function a hook hands back), or the shared flow this walk is for was entered by such a call.
    /// </summary>
    bool Sent(Item parent)
    {
        for (var x = parent; x is not null && x != root; x = x.Parent) if (x.Starts) return true;
        if (StartedRoot) MutationDependent = true;
        return StartedRoot;
    }

    /// <summary>Whether a step above <paramref name="parent"/> (or the shared flow's entry) starts a mutation, without marking anything.</summary>
    bool StartedAt(Item parent)
    {
        for (var x = parent; x is not null && x != root; x = x.Parent) if (x.Starts) return true;
        return StartedRoot;
    }

    /// <summary>
    /// D316: a request a mutation hook declares (React Query's <c>useMutation</c>, a wrapper such as <c>useApiMutation</c>) where no
    /// mutation was started: the hook only sets it up, and it is sent when something (a click) calls <c>mutate(…)</c>; the flows of those
    /// clicks reach it through that call. One step per request, nothing walked beyond it (no endpoint, no backend, no changes).
    /// </summary>
    void Declares(Fact http, At at, List<Condition> conds, Item parent, int depth)
    {
        var request = http["url"] == "" && http["endpoint"] != "" ? g.Name(http["endpoint"]) : $"{http["verb"]} {http["url"]}".Trim();
        if (parent.Children.Any(c => c.Step.End?.Kind == "waits" && c.Step.At == at && c.Step.What.StartsWith($"declares {request},", StringComparison.Ordinal))) return;
        var hook = When(http, at)!.Case;
        Add(parent, new Step
        {
            What = $"declares {request}, sent only when a click (or other code) calls `mutate(…)`, so not followed here", At = at, Kind = "call", Conditions = conds, Depth = depth,
            End = new End("waits", $"declared by {hook}, sent when `mutate(…)` is called"),
        });
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
        var facts = (project is null ? [] : g.AllFacts("auth").Where(f => f["from"] == "global" && f.Owner == project))
            .Concat(g.Facts(action, "auth").Where(f => f["rule"] == "filter").OrderBy(f => f["from"] == "method" ? 1 : 0)).ToList();
        var filters = facts.Select(f => f["filterType"]).Where(t => t != "" && g.Nodes.ContainsKey(t)).Distinct(StringComparer.Ordinal).ToList();
        // D304(5): the endpoint's own attribute value, named on the filter's stops ("answers 400 for DisallowRole(roles = ["Read Only"])").
        var argsOf = facts.Where(f => f["filterType"] != "" && f["args"] != "").GroupBy(f => f["filterType"], StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => string.Join(", ", x.Select(f => $"{f["filter"]}({f["args"]})").Distinct(StringComparer.Ordinal)), StringComparer.Ordinal);
        var steps = filters.SelectMany(t => Members(t, FilterMethods).Select(m => (m, (string?)t))).ToList();
        if (DocIds(action) is { } controller) steps.AddRange(Members(controller, ["OnActionExecuting", "OnActionExecutionAsync"]).Select(m => (m, (string?)null)));
        var done = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (m, filterType) in steps)
        {
            if (!done.Add(m)) continue;
            var node = g.Nodes[m];
            var line = g.NameLine(node);
            var item = Add(parent, new Step { What = $"runs first: {node.Name}", At = new At(node.File ?? "", line, g.Code(node.File, line)), Kind = "filter", Point = m, Depth = depth });
            // D310: the controller's own OnActionExecuting runs on the controller the action is in.
            Into(item, m, depth, branch, filterType is null && DocIds(action) is { } controllerType ? new Receiver(controllerType, false) : null);
            var args = filterType is not null ? argsOf.GetValueOrDefault(filterType) : null;
            foreach (var i in Subtree(item))
                for (var k = 0; k < i.Exits.Count; k++)
                    if (FilterStop(i.Exits[k].Exit, args) is { } what) i.Exits[k] = (i.Exits[k].Exit with { What = what }, i.Exits[k].Of);
        }
    }

    /// <summary>
    /// A filter's stop, worded for what it does (D304): a redirect only tidies the address or sends the user to sign in (item 8,
    /// "sends the user to …"), and a refusal names the endpoint's attribute value (item 5). Null when nothing changes.
    /// </summary>
    internal static string? FilterStop(Exit e, string? args) => e.Kind switch
    {
        "throw" => null,
        "Redirect" when e.What.StartsWith("sends the user elsewhere (", StringComparison.Ordinal) => $"sends the user to another address{e.What["sends the user elsewhere".Length..]}",
        "Challenge" => $"sends the user to sign in (Challenge){(args is null ? "" : $" for {args}")}",
        _ when args is not null && !e.What.Contains(" for ", StringComparison.Ordinal) => $"{e.What} for {args}",
        _ => null,
    };

    /// <summary>The hook a hook's own function belongs to: <c>ts|f.ts#useX.save</c> → <c>ts|f.ts#useX</c>.</summary>
    static string? HookOf(string point)
    {
        var hash = point.IndexOf('#');
        if (!point.StartsWith("ts|", StringComparison.Ordinal) || hash < 0) return null;
        var name = point[(hash + 1)..];
        var dot = name.IndexOf('.');
        return dot > 3 && name.StartsWith("use", StringComparison.Ordinal) && char.IsUpper(name[3]) ? point[..(hash + 1 + dot)] : null;
    }

    static string ValueReason(string name) => $"calls a value taken from {name}'s result; what it runs isn't known, so {name} stands for it";

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
        var item = new Item { Step = step, Parent = parent, In = visiting.Count > 0 ? visiting.Peek() : null };
        parent.Children.Add(item);
        return item;
    }

    static string ExitSentence(Fact exit) => exit["kind"] switch
    {
        "throw" => $"throws {(exit["exception"] != "" ? exit["exception"] : "an exception")}",
        // D305(2): the action a redirect names, when it names one (`RedirectToAction("List")` → List).
        "Redirect" => $"sends the user elsewhere ({(exit["action"] != "" ? exit["action"] : exit["result"])})",
        "StatusCode" => $"answers {exit["status"]}",
        "Result" => $"answers with {exit["status"]}",   // D301: a filter's result that isn't on D176's list (an EmptyResult)
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
                if (l.Guessed) flow.Guesses.Add(new Guess(n, l.Sentence, l.Reason ?? "no reason recorded"));
            }
            foreach (var u in item.Unknowns) flow.Unknowns.Add(u with { Step = n });
            if (item.Step.Kind != "package" && (item.Step.Kind == "unknown" || item.Step.End?.Kind == "unknown")) flow.Unknowns.Add(new Unknown(n, item.Step.Point ?? "", item.Step.At, item.Step.End?.Detail ?? item.Step.What));
            foreach (var h in item.Handled) flow.Handled.Add(h with { Step = n });
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

    /// <summary>
    /// D208: the validators a controller runs before each action: `validates` facts in its classes' OnActionExecuting (or
    /// OnActionExecutionAsync) and in the methods of those classes they call, transitively.
    /// </summary>
    IEnumerable<Fact> BeforeAction(List<string> chain)
    {
        var types = chain.ToHashSet(StringComparer.Ordinal);
        var todo = new Stack<string>(chain.SelectMany(t => g.Out(t, EdgeKind.Contains)).Select(e => e.Target)
            .Where(m => g.Nodes.TryGetValue(m, out var n) && n.Name.EndsWith(".OnActionExecuting", StringComparison.Ordinal) || g.Nodes.TryGetValue(m, out var n2) && n2.Name.EndsWith(".OnActionExecutionAsync", StringComparison.Ordinal)));
        var done = new HashSet<string>(StringComparer.Ordinal);
        while (todo.TryPop(out var m))
        {
            if (!done.Add(m)) continue;
            foreach (var v in g.Facts(m, "validates")) yield return v;
            foreach (var site in g.Facts(m, "call-site"))
                if (Stangent.Graph.Detectors.DocIds.ContainingType(site["target"]) is { } t && types.Contains(t)) todo.Push(site["target"]);
        }
    }

    /// <summary>The validation rules on the request body of every endpoint the flow calls (D174, D179), then every `if` in the code it
    /// walks that rejects the input (D203), by file and line.</summary>
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
        foreach (var f in WalkedPoints.SelectMany(p => g.Facts(p, "rejects")).Where(f => f.File is not null)
                     .OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Owner, StringComparer.Ordinal))
            flow.Checks.Add(new Check($"rejects when `{f["condition"]}`", "guard", f.Owner, new At(f.File!, f.Line, g.Code(f.File, f.Line)), f["message"], false));
        // D208: FluentValidation validators the flow runs: one run in code it walks lists the rules written in its class (or the
        // classes implementing the interface it's called through); a controller action lists the rules its controller's classes
        // write on a validator member that the controller's own OnActionExecuting runs, when the action takes a request body.
        var seen = flow.Checks.Select(c => $"{c.At}|{c.Rule}").ToHashSet(StringComparer.Ordinal);
        var ran = new List<Fact>();
        foreach (var v in WalkedPoints.SelectMany(p => g.Facts(p, "validates")))
        {
            foreach (var type in v["receiver"] == "" ? Enumerable.Empty<string>() : g.Implementers(v["receiver"]).Order(StringComparer.Ordinal))
                ran.AddRange(g.RulesIn[type]);
            if (v["member"] != "" && Stangent.Graph.Detectors.DocIds.ContainingType(v.Owner) is { } owner)
            {
                // The class running it and its bases: rules a subclass writes come in through the action that subclass serves (below).
                var chain = g.BaseChain(owner).ToHashSet(StringComparer.Ordinal);
                ran.AddRange(g.RulesOn[v["member"]].Where(r => chain.Contains(r["in"])));
            }
        }
        // Only an action that takes a request body: the validators check the resource it's sent, not a GET.
        foreach (var action in WalkedPoints.Where(p => g.Facts(p, "endpoint-types").Any(f => f["body"] != "")).Order(StringComparer.Ordinal))
        {
            if (Stangent.Graph.Detectors.DocIds.ContainingType(action) is not { } controller) continue;
            var chain = g.BaseChain(controller);
            var inChain = chain.ToHashSet(StringComparer.Ordinal);
            foreach (var v in BeforeAction(chain))
                if (v["member"] != "") ran.AddRange(g.RulesOn[v["member"]].Where(r => inChain.Contains(r["in"])));
                else if (v["receiver"] != "") foreach (var type in g.Implementers(v["receiver"]).Order(StringComparer.Ordinal)) ran.AddRange(g.RulesIn[type]);
        }
        // D209: an Angular signal form's rules, for a flow whose code reads the form or its model (`this.formModel()`).
        foreach (var p in WalkedPoints)
            foreach (var site in g.Facts(p, "call-site"))
                ran.AddRange(g.FormRules[site["target"]]);
        foreach (var rule in ran.Where(r => r.File is not null).DistinctBy(r => (r.File, r.Line, r["rule"], r.Owner))
                     .OrderBy(r => r.File, StringComparer.Ordinal).ThenBy(r => r.Line).ThenBy(r => r.Owner, StringComparer.Ordinal))
        {
            var at = new At(rule.File!, rule.Line, g.Code(rule.File, rule.Line));
            if (seen.Contains($"{at}|{rule["rule"]}")) continue;
            seen.Add($"{at}|{rule["rule"]}");
            var subject = rule["via"] == "angular-signal-forms" ? $"{g.Name(rule.Owner)}.{rule["property"]}" : g.Name(rule.Owner);
            flow.Checks.Add(new Check($"{subject}: {rule["rule"]}{(rule["args"] != "" ? $"({rule["args"]})" : "")}{(rule["set"] != "" ? $" ({rule["set"]})" : "")}", rule["rule"], rule.Owner,
                at, rule["message"], rule["modifiers"].Split('|').Contains("AsWarning", StringComparer.Ordinal) || rule["severity"] == "Warning"));
        }
        // D207: request filters on an action the flow runs check the request before it does.
        foreach (var f in WalkedPoints.SelectMany(p => g.Facts(p, "request-check")).Where(f => f.File is not null)
                     .OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Owner, StringComparer.Ordinal))
            flow.Checks.Add(new Check($"[{f["filter"]}] {f["does"]}", "request-filter", f.Owner, new At(f.File!, f.Line, g.Code(f.File, f.Line)), "", false));
    }
}

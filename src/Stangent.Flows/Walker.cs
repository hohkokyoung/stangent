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
    /// <summary>D191: an onclick's route, waiting for the line in its script that sends to the URL the onclick passed (<c>point@arg</c>).</summary>
    readonly Dictionary<string, List<Fact>> pending = new(StringComparer.Ordinal);
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

    /// <summary>
    /// A shared flow's start (D189): the hub or depth stop's own code, walked once for every flow that continues into it. A point
    /// written to an interface runs its implementations, one branch each (🟡 when several), as a call to it would.
    /// </summary>
    public void RootShared(string point)
    {
        walked[point] = root;
        var runs = Runs(point);
        if (runs.Count == 1 && runs[0] == point) { Visit(point, root, 0, [point]); return; }
        foreach (var run in runs)
        {
            var node = g.Nodes[run];
            var line = g.NameLine(node);
            var item = Add(root, new Step
            {
                What = $"runs {node.Name}", At = new At(node.File ?? "", line, g.Code(node.File, line)), Kind = "call", Point = run, Depth = 0,
                Guessed = runs.Count > 1, GuessReason = runs.Count > 1 ? $"one of {runs.Count}: {string.Join(", ", runs.Select(g.Name))}" : null,
            });
            Into(item, run, 0, [point]);
        }
    }

    /// <summary>What a shared flow for a call to <paramref name="target"/> starts at: the one point it runs, else the target itself.</summary>
    string SharedPoint(string target) => Runs(target) is [var only] ? only : target;

    List<string> Runs(string target) => [.. g.Dispatch(target).Where(t => g.Nodes.TryGetValue(t, out var n) && n.File is not null && !n.IsTest)];

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
            .Concat(g.Facts(point, "navigate").Select(f => f.Line))
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

        foreach (var line in lines)
        {
            var at = new At(FileAt(point, line, file), line, g.Code(FileAt(point, line, file), line));
            // D201: code in a function handed to code that keeps it runs later, when something else calls it: not this flow's.
            static bool Now(Fact f) => !f["deferred"].StartsWith("kept:", StringComparison.Ordinal);
            var sites = callSites.Where(f => f.Line == line && Now(f)).OrderBy(f => f["target"], StringComparer.Ordinal).ToList();
            var conds = conditions[line].OrderBy(f => f["target"], StringComparer.Ordinal).ToList();
            List<Condition> CondsFor(string? target) =>
                FlowGraph.ConditionsOf(conds.FirstOrDefault(c => target is not null && c["target"] == target) ?? conds.FirstOrDefault() ?? Empty);
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
            // D192: a navigation that isn't an exit leaves for another screen: a step ending `screen`.
            foreach (var nav in g.Facts(point, "navigate").Where(f => f.Line == line && Now(f)).OrderBy(f => f["to"], StringComparer.Ordinal))
            {
                var step = Add(parent, new Step { What = $"goes to {nav["to"]} ({nav["via"]})", At = at, Kind = "navigate", Conditions = nav["conditions"] != "" ? FlowGraph.ConditionsOf(nav) : CondsFor(null), Depth = depth,
                    Guessed = Later(nav) is not null, GuessReason = Later(nav),
                    End = new End("screen", nav["to"]) });
                ScreenLinks(step, at, nav["to"], g.PagesAt(nav["to"], at.File, nav["root"] == "1"));
            }
            foreach (var exit in g.Facts(point, "exit").Where(f => f.Line == line && Now(f)).OrderBy(f => f["text"], StringComparer.Ordinal))
            {
                parent.Exits.Add((new Exit(exit["kind"], ExitSentence(exit), at, FlowGraph.ConditionsOf(exit), null, point), parent));
                // D204: a redirect leaves for another screen: a link to that screen's opening flow.
                if (exit["kind"] == "Redirect")
                {
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
                    // D188: a package's code is a step, not walked (it isn't on the map).
                    if (consumed.Count == 0 && !later.Contains(target) && IdentityStore(site) is { } store)
                        // D200: ASP.NET Identity's managers read and save the user store; which tables, the package's own code picks.
                        Add(parent, new Step { What = $"reads or saves the Identity user store through {site["method"]}", At = at, Kind = "database", Conditions = CondsFor(""), Depth = depth,
                            End = new End("database", store), Guessed = true,
                            GuessReason = "an ASP.NET Identity manager's async call reads or saves its store (the user, role, claim and token tables); which ones, the package's code picks" });
                    else if (consumed.Count == 0 && !later.Contains(target))
                        Add(parent, new Step { What = $"calls {site["method"]} in the package {site["package"]}", At = at, Kind = "package", Conditions = CondsFor(""), Depth = depth,
                            // D199: an MVC action returning a view or a redirect leads to that screen.
                            End = MvcResult(site, at) is { } screen ? new End("screen", screen) : new End("unknown", $"a package's code ({site["package"]}), not walked") });
                    continue;
                }
                if (consumed.Contains(target) || later.Contains(target)) continue;
                Call(point, site, target, at, CondsFor(target), parent, depth, branch);
            }
            foreach (var blind in g.BlindByOwner[point].Where(b => b.Line == line && !(opened && b.Reason.EndsWith("URL built at runtime", StringComparison.Ordinal))).OrderBy(b => b.Reason, StringComparer.Ordinal))
            {
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
                item.Links.Add(new Link("message", g.Name(h), h, at, $"Sends {g.TypeName(made)}, handled by {g.Name(h)} ({at})", null, true));
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
        var listener = Later(site);
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
                var reads = Add(parent, new Step
                {
                    What = $"{(tables.Any(x => x.Write) ? "saves" : "reads")} {names} through {tnode.Name}", At = at, Kind = "database", Point = target, Conditions = conds, Depth = depth,
                    End = new End("database", $"{names} ({tnode.Name}, shared code used by {g.Callers(target)} places)"), Guessed = guessedSite || tables.Any(x => x.Guess),
                    GuessReason = guessedSite ? SiteReason(target) : tables.Any(x => x.Guess) ? "a save or read found by a detector's pattern (a repository method's name, a navigation collection)" : null,
                });
                // D196: its own code still runs (the save itself, events it sends): the shared walk continues it, as for any hub.
                reads.Links.Add(new Link("continues", tnode.Name, SharedPoint(target), at, $"Shared code used by {g.Callers(target)} places, reading or saving {names}; {tnode.Name}'s own walk continues it ({at})", null, false));
                return;
            }
            var item = Add(parent, new Step
            {
                What = $"calls {tnode.Name}", At = at, Kind = kind, Point = target, Conditions = conds, Depth = depth,
                End = new End("hub", $"{tnode.Name}: shared code used by {g.Callers(target)} places"), Guessed = guessedSite || byValue || listener is not null,
                GuessReason = guessedSite ? SiteReason(target) : listener ?? (byValue ? $"calls a value taken from {tnode.Name}'s result; what it runs isn't known, so the hook stands for it" : null),
            });
            item.Links.Add(new Link("continues", tnode.Name, SharedPoint(target), at, $"Stops here: shared code used by {g.Callers(target)} places; {tnode.Name}'s own walk continues it ({at})", null, false));
            return;
        }
        // D201: `base.M()` runs that method itself, no override.
        var runs = site["base"] == "1" && g.Nodes.TryGetValue(target, out var own) && own.File is not null ? [target] : Runs(target);
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
                What = kind == "renders" ? $"shows {g.Name(run)}" : callback != "" ? $"when the request answers ({callback}), runs {g.Name(run)}" : $"calls {g.Name(run)}", At = at, Kind = kind, Point = run, Conditions = conds, Depth = depth,
                Guessed = several || guessedSite || byValue || listener is not null,
                GuessReason = several ? $"one of {runs.Count}: {string.Join(", ", runs.Select(g.Name))}" : guessedSite ? SiteReason(target)
                    : listener ?? (byValue ? $"calls a value taken from {tnode.Name}'s result; what it runs isn't known, so the hook stands for it" : null),
            });
            Into(item, run, depth, branch);
        }
    }

    /// <summary>D204: a link to each screen a redirect or navigation can open; none known: one link saying where it goes, to no flow.</summary>
    void ScreenLinks(Item item, At at, string to, IReadOnlyList<string> screens)
    {
        if (screens.Count == 0)
        {
            item.Links.Add(new Link("screen", to == "" ? "a page chosen at runtime" : to, null, at,
                $"Sends the user to {(to == "" ? "a page chosen at runtime" : to)}; no screen in the code matches it, so it links to no flow ({at})", null, true));
            return;
        }
        foreach (var screen in screens)
            item.Links.Add(new Link("screen", g.ScreenName(screen), screen, at,
                screens.Count == 1 ? $"Sends the user to {g.ScreenName(screen)}, whose opening is its own flow ({at})"
                    : $"Sends the user to {to}, which {screens.Count} screens fit; {g.ScreenName(screen)} is one ({at})", null, screens.Count > 1));
    }

    const string OverloadReason = "the compiler couldn't pick one overload, so each in-repo candidate is a branch";

    /// <summary>D201, D202: why code runs later than its point: a listener's function, or a callback on a stream that isn't a request.</summary>
    static string? Later(Fact f) => f["deferred"] switch
    {
        var d when d.StartsWith("listener:", StringComparison.Ordinal) => $"runs when the event comes: a function registered with {d[9..]}(…)",
        var d when d.StartsWith("stream:", StringComparison.Ordinal) => $"runs when the stream sends a value ({d[7..]}), not when this code runs",
        _ => null,
    };

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
    void Into(Item item, string point, int depth, List<string> branch)
    {
        if (branch.Contains(point, StringComparer.Ordinal)) { item.BackTo = walked.GetValueOrDefault(point) ?? root; return; }
        if (walked.TryGetValue(point, out var before)) { item.SameAs = before; return; }
        if (depth + 1 > s.Depth)
        {
            item.Step.End = new End("depth", $"{s.Depth} real calls; not walked: {g.Name(point)}");
            item.Links.Add(new Link("continues", g.Name(point), point, item.Step.At, $"Stops here: {s.Depth} calls deep; {g.Name(point)}'s own walk continues it ({item.Step.At})", null, false));
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

    void Http(Fact http, At at, List<Condition> conds, Item parent, int depth, List<string> branch, string? because = null)
    {
        var endpoint = http["endpoint"];
        var later = Later(http);
        var guessed = http["label"] != "certain" || because is not null || later is not null;
        var reason = string.Join("; ", new[] { because, later, http["reason"] }.Where(r => !string.IsNullOrEmpty(r)));
        var item = Add(parent, new Step
        {
            // D201: a form's or onclick's match has no URL of its own: the endpoint names it.
            What = http["url"] == "" && endpoint != "" ? $"calls {g.Name(endpoint)}" : $"calls {http["verb"]} {http["url"]}".TrimEnd(), At = at, Kind = "http", Point = endpoint, Conditions = conds, Depth = depth,
            Guessed = guessed, GuessReason = guessed ? (reason != "" ? reason : "no reason recorded") : null,
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
        foreach (var f in walked.Keys.SelectMany(p => g.Facts(p, "rejects")).Where(f => f.File is not null)
                     .OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ThenBy(f => f.Owner, StringComparer.Ordinal))
            flow.Checks.Add(new Check($"rejects when `{f["condition"]}`", "guard", f.Owner, new At(f.File!, f.Line, g.Code(f.File, f.Line)), f["message"], false));
    }
}

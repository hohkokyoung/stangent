using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Stangent.Flows;

/// <summary>
/// One cause a 🟡 reason or ❓ why can have (step 6, D235): its family, which of D166's four buckets it goes in, the fixed pattern
/// matching its text, the step kinds it may sit on (D237 item 2 by code: a reason on the wrong kind of step isn't true to its
/// cause; null: any), and for a missing general rule not fixed yet, why it waits.
/// </summary>
public sealed record GuessCause(string Id, string Family, string Bucket, string Pattern, string[]? Kinds, string? Waits = null)
{
    internal Regex Rx { get; } = new(Pattern, RegexOptions.CultureInvariant);
}

/// <summary>
/// <c>stangent flows guesses</c> (step 6, D235–D237): every 🟡 and ❓ line on a flow, counted by code line (one guess in the
/// graph, however many flows show it), put in a cause and a bucket by a fixed table of patterns, ranked by the start flows it
/// touches. Checks bar items 1 (every line in a bucket) and 2 by code (every reason on the kind of step its cause belongs to).
/// </summary>
public static class GuessReport
{
    public const string MissingRule = "missing general rule", AppSpecific = "app-specific", Unknowable = "unknowable", RunTime = "only known at run time";
    public static readonly string[] Buckets = [MissingRule, AppSpecific, Unknowable, RunTime];

    static readonly string[] Db = ["database"], Http = ["http"], Calls = ["call", "renders", "on-load"], Later = ["call", "http", "navigate"];

    /// <summary>🟡 causes, first match wins on the reason's first part (a reason joined by "; " is decided by what comes first).</summary>
    public static readonly GuessCause[] Guessed =
    [
        new("one-of-n", "one of several implementations", RunTime, @"^one of \d+:", Calls),
        new("overload", "one of several implementations", MissingRule, @"couldn't pick one overload", Calls, "the compiler's own pick needs the argument types at the call"),
        new("repository", "save or read read off a name", MissingRule, @"repository method read as a save|detector's pattern", Db),
        new("navigation", "save or read read off a name", MissingRule, @"navigation collection's Add or Remove", Db, "whether a later SaveChanges saves the collection isn't followed"),
        new("identity", "save or read read off a name", MissingRule, @"Identity manager's async call", Db, "the package's store code isn't in the repo"),
        new("handlers", "handlers or jobs found by type", MissingRule, @"handlers found by the event's type|event bus calls each handler", ["message", "call"], "the bus's registration of handlers is a framework scan, not read"),
        new("queued-command", "handlers or jobs found by type", MissingRule, @"queued command matched to its job", ["queue"], "the command queue's job lookup is a framework scan, not read"),
        new("queue-table", "handlers or jobs found by type", AppSpecific, @"table named as a queue", ["call", "database"]),
        // D240: a mutation's request, a stream's or timer's or listener's function and a component's element that draws only
        // while a prop is set are conditions on their step, not 🟡; what stays is a function a library keeps whose running isn't known.
        new("library-callback", "runs later or maybe", MissingRule, @"reaches no server work itself", ["call"], "whether the function starts what the library keeps isn't followed"),
        new("function-result", "runs later or maybe", MissingRule, @"calls a value taken from", Later, "what another function returns isn't followed (D210)"),
        new("drawn-when", "runs later or maybe", MissingRule, @"^shown only when", ["renders"]),
        new("endpoints-fit", "URL or target match", MissingRule, @"endpoints fit", Http),
        new("url-parameter", "URL or target match", RunTime, @"is a parameter: one of|is one of \d+ the code building|the one the button's onclick passes", Http),
        new("onclick", "URL or target match", MissingRule, @"onclick|scripts declare a global", ["http", "call"]),
        new("mvc-view", "URL or target match", MissingRule, @"controller or action taken|method taken from the forms", Http, "a form naming no controller is matched by the view's folder"),
        new("no-screen", "URL or target match", MissingRule, @"^no screen in the code matches", null, "the address matches no screen's route"),
        new("screen-at-runtime", "URL or target match", RunTime, @"sends the user to is chosen at runtime", null),
        new("several-screens", "URL or target match", RunTime, @"fits \d+ screens", null),
        new("generated-client", "URL or target match", MissingRule, @"naming of .*not by a path", Http, "the generated client's own paths aren't read"),
        new("actor", "actor", AppSpecific, @"calls no endpoint|no \[Authorize\]|no guard", null),
    ];

    /// <summary>❓ causes, first match wins.</summary>
    public static readonly GuessCause[] Unknown =
    [
        new("outside-not-followed", "outside call", MissingRule, @"method and path not followed", null, "the client's own request building isn't followed"),
        new("outside-runtime", "outside call", RunTime, @"^outside .*(built at runtime|set at runtime)", null),
        new("outside-no-service", "outside call", AppSpecific, @"base address isn't known|no service with that name", null),
        new("frontend-url", "frontend URL", RunTime, @"^HTTP \w+ to a URL built|only fixed part is the API root", null),
        new("frontend-no-endpoint", "frontend URL", MissingRule, @"matched no backend endpoint|^no endpoint fits|no action on the map serves", null, "the request's address fits no endpoint in the graph"),
        new("reflection", "reflection", Unknowable, @"^reflection:", null),
        new("command-at-runtime", "type known only at run time", RunTime, @"type is known only at runtime", null),
        new("plugin-view", "view not found", MissingRule, @"^view ~/Plugins/.* not found", null),
        new("view-not-found", "view not found", MissingRule, @"^view .* not found", null, "MVC's lookup rules find no view file"),
        new("view-at-runtime", "view chosen at run time", RunTime, @"(?i)^view chosen at runtime", null),
        new("config-key", "setting from outside the repo", AppSpecific, @"^configuration key .* is in no appsettings", null),
        new("no-job", "handlers or jobs found by type", MissingRule, @"^no job runs", null, "no job in the graph runs the queued command"),
    ];

    /// <summary>The cause a reason's text has by the table; null: unsorted (fails bar item 1).</summary>
    public static GuessCause? Classify(string text, IEnumerable<GuessCause> table)
    {
        var first = text.Split("; ")[0];
        return table.FirstOrDefault(c => c.Rx.IsMatch(first)) ?? table.FirstOrDefault(c => c.Rx.IsMatch(text));
    }

    sealed class Line(string mark, string at, string code, GuessCause? cause, string text)
    {
        public string Mark { get; } = mark;
        public string At { get; } = at;
        public string Code { get; } = code;
        public GuessCause? Cause { get; } = cause;
        public string Text { get; } = text;
        public int Occurrences { get; set; }
        public HashSet<string> Starts { get; } = new(StringComparer.Ordinal);
        public HashSet<string> WrongKinds { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>The report over a built flow set (the same commit gives the same counts).</summary>
    public static JsonObject Report(FlowSet set, int top = 20, string? only = null)
    {
        var lines = new Dictionary<string, Line>(StringComparer.Ordinal);
        Line Get(string mark, At at, GuessCause? cause, string text)
        {
            var key = $"{mark}|{at.File}:{at.Line}|{cause?.Id ?? text}";
            if (!lines.TryGetValue(key, out var l)) lines[key] = l = new Line(mark, $"{at.File}:{at.Line}", at.Code, cause, text);
            return l;
        }
        foreach (var f in set.Flows)
        {
            var start = f.Kind != "shared";
            var steps = f.Steps.ToDictionary(s => s.N);
            foreach (var x in f.Guesses)
            {
                var step = x.Step is { } n ? steps.GetValueOrDefault(n) : null;
                var cause = Classify(x.Reason, Guessed);
                var l = Get("🟡", step?.At ?? f.Start.At, cause, x.Reason);
                l.Occurrences++;
                if (start) l.Starts.Add(f.Id);
                var kind = step?.Kind ?? "-";
                if (cause?.Kinds is { } kinds && !kinds.Contains(kind, StringComparer.Ordinal)) l.WrongKinds.Add(kind);
            }
            foreach (var u in f.Unknowns)
            {
                var l = Get("❓", u.At, Classify(u.Why, Unknown), u.Why);
                l.Occurrences++;
                if (start) l.Starts.Add(f.Id);
            }
        }
        var all = lines.Values.OrderByDescending(l => l.Starts.Count).ThenByDescending(l => l.Occurrences).ThenBy(l => l.At, StringComparer.Ordinal).ThenBy(l => l.Mark, StringComparer.Ordinal).ToList();
        var unsorted = all.Where(l => l.Cause is null).ToList();
        var wrong = all.Where(l => l.WrongKinds.Count > 0).ToList();
        JsonObject Count(IEnumerable<Line> ls)
        {
            var list = ls.ToList();
            return new JsonObject { ["lines"] = list.Count, ["occurrences"] = list.Sum(l => l.Occurrences), ["startFlows"] = list.SelectMany(l => l.Starts).Distinct(StringComparer.Ordinal).Count() };
        }
        JsonObject Marks(string mark, GuessCause[] table)
        {
            var mine = all.Where(l => l.Mark == mark).ToList();
            return new JsonObject
            {
                ["total"] = Count(mine),
                ["byBucket"] = new JsonObject([.. Buckets.Select(b => KeyValuePair.Create(b, (JsonNode?)Count(mine.Where(l => l.Cause?.Bucket == b))))]),
                ["byCause"] = new JsonArray([.. table.Select(c => (c, ls: mine.Where(l => l.Cause == c).ToList())).Where(x => x.ls.Count > 0)
                    .OrderByDescending(x => x.ls.Count).ThenBy(x => x.c.Id, StringComparer.Ordinal)
                    .Select(x => (JsonNode)new JsonObject
                    {
                        ["cause"] = x.c.Id, ["family"] = x.c.Family, ["bucket"] = x.c.Bucket, ["counts"] = Count(x.ls),
                        ["waits"] = x.c.Waits,
                    })]),
            };
        }
        JsonObject Item(Line l) => new()
        {
            ["mark"] = l.Mark, ["at"] = l.At, ["code"] = l.Code.Length > 120 ? l.Code[..120] + "…" : l.Code, ["cause"] = l.Cause?.Id, ["bucket"] = l.Cause?.Bucket,
            ["startFlows"] = l.Starts.Count, ["occurrences"] = l.Occurrences, ["reason"] = l.Text,
        };
        var problems = new JsonArray([
            .. unsorted.Select(l => (JsonNode)$"item 1: {l.Mark} {l.At} fits no cause: {l.Text}"),
            .. wrong.Select(l => (JsonNode)$"item 2: {l.Mark} {l.At} says '{l.Cause!.Id}' on a {string.Join("/", l.WrongKinds.Order(StringComparer.Ordinal))} step: {l.Text}"),
        ]);
        return new JsonObject
        {
            ["commit"] = set.Commit,
            ["ok"] = problems.Count == 0,
            ["guessed"] = Marks("🟡", Guessed),
            ["unknown"] = Marks("❓", Unknown),
            ["problems"] = problems,
            [only is null ? "top" : "lines"] = new JsonArray([.. (only is null ? all.Take(top) : all.Where(l => l.Cause?.Id == only)).Select(l => (JsonNode)Item(l))]),
        };
    }
}

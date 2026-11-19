using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;

namespace Stangent.Flows;

/// <summary>One box of a chart: its id in the Mermaid text, its shape (start, change, diamond, refused, redirect, follow-on) and its label.</summary>
public sealed record ChartNode(string Id, string Shape, string Label);

/// <summary>One line of a chart; <see cref="Dashed"/>: an outside service's failure (D273).</summary>
public sealed record ChartEdge(string From, string To, string? Label, bool Dashed);

/// <summary>A stop (an exit or refusal) of a flow file: its step and line, as the page cites it (D284).</summary>
public sealed record ChartStop(int? Step, string At)
{
    public override string ToString() => Step is { } n ? $"stops at step {n} (`{At}`)" : $"stops at `{At}`";
}

/// <summary>A line in one of a click's lists: what it says, where, its steps (D284), its stops, and how many of the app's start flows pass it.</summary>
public sealed record ChartRow(string Text, string At, IReadOnlyList<int> Steps, IReadOnlyList<ChartStop> Stops, int Flows = 0)
{
    /// <summary>D314(2): a "what runs afterwards" row: what sets them off ("after sends X (step 19)") and the flows it sets off, by id.</summary>
    public string? Lead { get; init; }
    public IReadOnlyList<string> Targets { get; init; } = [];
}

/// <summary>One row of the scenario table (D279): a stable id, the path, what decides it, the steps it passes or skips (D284), how it ends.</summary>
public sealed record ScenarioRow(string Id, string Scenario, string Decides, string Steps, string Ends);

/// <summary>D311(1): a change box's words without its steps, the key of the same change, its steps and line.</summary>
public sealed record BoxInfo(string Group, string Head, IReadOnlyList<int> Steps, string At, bool WithLine);

/// <summary>The boxes and lines of one chart.</summary>
public sealed class ChartGraph
{
    public List<ChartNode> Nodes { get; } = [];
    public List<ChartEdge> Edges { get; } = [];
    public int Count(string shape) => Nodes.Count(n => n.Shape == shape);
}

/// <summary>How many of a flow file's steps are certain, guessed (🟡) and can't be told (❓) (D283).</summary>
public sealed record StepCounts(int Certain, int Guessed, int Unknown)
{
    public static StepCounts operator +(StepCounts x, StepCounts y) => new(x.Certain + y.Certain, x.Guessed + y.Guessed, x.Unknown + y.Unknown);
    public static StepCounts Zero { get; } = new(0, 0, 0);
}

/// <summary>A click that changes something (D271), as its part of the developer page shows it (D279).</summary>
public sealed class FlowChart
{
    public required string Flow { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; set; }
    public required StepCounts Counts { get; init; }
    /// <summary>D273's chart; drawn on the page only when <see cref="Drawn"/>.</summary>
    public ChartGraph Chart { get; } = new();
    /// <summary>D282: false when the chart has more nodes than the cap; the page then says how many it would have had.</summary>
    public bool Drawn { get; set; } = true;
    public List<ScenarioRow> Scenarios { get; } = [];
    /// <summary>Per service (the class making the outside call), how it fails.</summary>
    public SortedDictionary<string, List<ChartRow>> ServiceFailures { get; } = new(StringComparer.Ordinal);
    /// <summary>What runs afterwards on its own: per change, the flows it sets off.</summary>
    public List<ChartRow> FollowOns { get; } = [];
    /// <summary>Stops in code many flows pass through (at least the hub threshold), with their flow counts.</summary>
    public List<ChartRow> SharedGuards { get; } = [];
    /// <summary>D281: framework changes, not drawn.</summary>
    public List<ChartRow> SharedChanges { get; } = [];
    /// <summary>D300: thrown errors a catch on the way takes (D304: what the catch then does, when known). One row per error and catch.</summary>
    public List<ChartRow> Handled { get; } = [];
    /// <summary>Detailed diamonds per condition kind (if, else, unless, loop, case, catch, …; "checks" and "exit" for the refusal diamonds).</summary>
    public SortedDictionary<string, int> DiamondKinds { get; } = new(StringComparer.Ordinal);
    /// <summary>D311(1): each change box's words and steps, by node id, for the lists that show a repeated change once.</summary>
    public Dictionary<string, BoxInfo> Boxes { get; } = new(StringComparer.Ordinal);
    /// <summary>Its changes are all ❓ steps (D271 counts them as changes).</summary>
    public bool OnlyUnknowns { get; set; }
    /// <summary>D313: each change box's kind, words and service, in chart order, for the index's "start here" lines.</summary>
    public List<(string Kind, string Text, string? Service)> Made { get; } = [];
    /// <summary>D313: where to start reading it, from its flow file; null for a shared part.</summary>
    public StartHere? Start { get; set; }
    /// <summary>D314(3): per change box (same order as <see cref="Made"/>), the nearest deciding condition on its path and how many there are; null when unconditional.</summary>
    public List<(string Phrase, string At, int Count)?> MadeWhen { get; } = [];
    /// <summary>D314(4): the earlier flow on the page whose field checks (K rows) are the same; its K rows are then written once, there.</summary>
    public (string Title, string Flow)? SameChecksAs { get; set; }
}

/// <summary>D313: one request a start flow sends: the endpoint (method and path; ❓ <see cref="Unsure"/> when the flow can't tell which) and the action it lands on.</summary>
public sealed record StartRequest(string Endpoint, bool Unsure, string? Back)
{
    /// <summary>The graph matched it to its endpoint by a guess (🟡, its reason in the flow's file).</summary>
    public bool Guessed { get; init; }
}

/// <summary>
/// D313: where to start reading a start flow, from its flow file only: the frontend line it starts at (a click's handler, a screen's
/// route), the C# method it starts in (a handler's Handle, a job's Execute, a server-made screen's action) and its line, and the
/// requests it sends with the action each lands on. <see cref="Asks"/>: a click or screen, which should send one (❓ when none is found).
/// </summary>
public sealed record StartHere(string? Front, string? Entry, string? EntryAt, IReadOnlyList<StartRequest> Requests, bool Asks)
{
    /// <summary>A screen the server makes (it starts in a controller action): the route it opens at, from the flow's name; "" when
    /// the name doesn't say (❓); null for any other flow.</summary>
    public string? Route { get; init; }
}

/// <summary>A start flow with no chart, one line on the page (D271, D281), and why.</summary>
public sealed record DisplayOnly(string Flow, string Kind, string Name, string Why, StepCounts Counts)
{
    /// <summary>D306(3): the name as the page shows it, with what tells it from another of the same name in the area.</summary>
    public string Shown { get; set; } = Name;
    /// <summary>Changes listed in the line: framework changes (D281) and setup steps (D273).</summary>
    public List<ChartRow> Listed { get; } = [];
    /// <summary>D287: its own stops, one sub-bullet each (same text and condition share one).</summary>
    public List<ChartRow> Stops { get; } = [];
    /// <summary>D287: its stops at a line the hub threshold of start flows or more have; only counted on the page.</summary>
    public List<ChartRow> SharedGuards { get; } = [];
    /// <summary>D300: its handled errors, one sub-bullet per error and catch.</summary>
    public List<ChartRow> Handled { get; } = [];
    /// <summary>D293: who its messages and queued work set off, one sub-bullet per change.</summary>
    public List<ChartRow> FollowOns { get; } = [];
}

/// <summary>
/// D306: a shared flow shown once on an area's page: reached by <see cref="Reach"/> of the area's start flows (or longer than the
/// step limit); its part is a chart like a click's, or one line when it has nothing to draw. <see cref="Changes"/> and
/// <see cref="Stops"/> are what each flow's line into it says.
/// </summary>
public sealed record SharedPart(string Flow, string Name, int Reach, int StepCount, int Changes, int Stops)
{
    /// <summary>D307: the words of its changes, each once, in flow order.</summary>
    public IReadOnlyList<string> Named { get; init; } = [];
    public FlowChart? Chart { get; set; }
    public DisplayOnly? Line { get; set; }
}

/// <summary>What the page shows, set in <c>stangent.yaml</c>'s <c>pages:</c> (D281, D282).</summary>
public sealed record PageSettings(int FrameworkShare = PageSettings.DefaultShare, int ChartCap = PageSettings.DefaultCap)
{
    public const int DefaultShare = 10;
    public const int DefaultCap = 60;
    /// <summary>D281's floor: a change in fewer flows than this is never a framework change, whatever its share.</summary>
    public const int FrameworkFloor = 10;
    public static PageSettings From(StangentConfig c) => new(c.Pages.FrameworkShare, c.Pages.ChartCap);
}

/// <summary>An area's developer page (D278–D284), in the summary's section order, then its one-line flows.</summary>
public sealed class AreaCharts
{
    public required string Area { get; init; }
    public required string Folder { get; init; }
    public required string Commit { get; init; }
    public int HubCallers { get; init; }
    public int Depth { get; init; }
    public required PageSettings Settings { get; init; }
    /// <summary>The app's start flows, the base of D281's share.</summary>
    public int AppFlows { get; init; }
    public List<FlowChart> Charts { get; } = [];
    public List<DisplayOnly> DisplayOnly { get; } = [];
    /// <summary>D306: heavy shared work, each shown once in a part of its own, in name order.</summary>
    public List<SharedPart> Shared { get; } = [];
    /// <summary>D306: the cut the page was built with (null: no heavy shared flow), for gate 1 to read the flows the same way.</summary>
    public SharedCut? Cut { get; set; }
    public List<string> Missing { get; } = [];
    /// <summary>Every ❓ of the area's start flows, in page order: flow, step, why, line.</summary>
    public List<(string Flow, int? Step, string Why, string At)> CantSee { get; } = [];
    /// <summary>D311(2): every shared flow the area's start flows reach, as the page's "Shared flows" list counts them.</summary>
    public List<string> Reached { get; } = [];
    /// <summary>D311(2): those left out of the list: they change nothing, stop nothing and call nothing outside (<see cref="PageChart.Quiet"/>).</summary>
    public HashSet<string> Quiet { get; } = new(StringComparer.Ordinal);
    /// <summary>A screen's changes and stops drawn under its clicks instead, by place (<c>step|at</c>, <c>exit|at</c>).</summary>
    public Dictionary<string, HashSet<string>> InClicks { get; } = new(StringComparer.Ordinal);
    /// <summary>D314(1): the area this one is a part of (<c>&lt;Area&gt; › …</c>), linked from this page's folder; null when it is no part.</summary>
    public (string Name, string Link)? PartOf { get; set; }
    /// <summary>D314(1): the parent's other parts, linked.</summary>
    public List<(string Name, string Link)> Siblings { get; } = [];
    /// <summary>D314(1): this area's own parts, linked, each with its start flows by readable name.</summary>
    public List<(string Name, string Link, string Flows)> Parts { get; } = [];
    /// <summary>D314(2): each flow a receiver line names: its class and method (or name) and a link to its file from this page, when known.</summary>
    public Dictionary<string, (string Name, string? Link)> Named { get; } = new(StringComparer.Ordinal);
    /// <summary>D316: the controllers the area's start flows land on, with their endpoints none of them calls and who does.</summary>
    public List<ElsewhereController> Elsewhere { get; } = [];
    public StepCounts Steps => Charts.Aggregate(StepCounts.Zero, (s, c) => s + c.Counts) + DisplayOnly.Aggregate(StepCounts.Zero, (s, d) => s + d.Counts);
}

/// <summary>What every area of one build reads once: the flow files and how many start flows pass each line.</summary>
public sealed class PageCache(string root, Areas a)
{
    internal FlowReader Reader { get; } = new(root, a);
    internal PageChart.SharedCounts Counts { get; } = new(root, a);
    PageElsewhere.Index? elsewhere;
    /// <summary>D316: each start flow's endpoints and each endpoint's callers, for the "Elsewhere" lists.</summary>
    internal PageElsewhere.Index Elsewhere => elsewhere ??= new(Reader, a);
}

/// <summary>
/// The developer page by code, no AI (D278–D285): per start flow that changes something (a save, a delete, a message, queued work,
/// an outside call or a ❓ step, D271), D273's flowchart with full labels and step numbers (D284), capped (D282); a scenario table,
/// one row per path, holding the field checks; outside services failing; what runs afterwards on its own; guards shared with
/// other flows; changes made in shared code (D281's framework changes, not drawn). Display-only flows are one line each. The
/// page opens with a YAML note (D283). The same flows, <c>areas.yaml</c> and settings give the same bytes.
/// </summary>
public static class PageChart
{
    /// <summary>The first line of the developer page's body, inside D233's two lines; a page off the switch drops a body that starts with it.</summary>
    public const string BodyMarker = "<!-- developer page by code, no AI (D278–D285): rebuilt by `stangent areas check` while components.pages is on; do not edit -->";
    /// <summary>The line before each start flow's part, for <c>pages verify</c> to find it.</summary>
    public static string FlowMarker(string id) => $"<!-- flow {id} -->";
    /// <summary>D306: the line before each shared part, for <c>pages verify</c> and scripts to find it.</summary>
    public static string SharedMarker(string id) => $"<!-- shared {id} -->";
    /// <summary>D306: the heading over the shared parts.</summary>
    public const string SharedHeading = "## Shared work, shown once";
    /// <summary>D306: a shared flow reached by at least this many of an area's start flows is shown once, in a part of its own.</summary>
    public const int SharedReach = 3;
    /// <summary>D306: so is one longer than this many steps.</summary>
    public const int SharedSteps = 150;
    /// <summary>D309: but never one of this many steps or fewer: a small helper stays inline, under the click's own conditions.</summary>
    public const int SharedMinSteps = 30;
    /// <summary>D307: a line into a part names the part's changes when it has at most this many.</summary>
    public const int SharedNamed = 3;
    static readonly StringComparer O = StringComparer.Ordinal;
    /// <summary>Conditions read "only when …" (D240): code that certainly runs, not a decision.</summary>
    static readonly HashSet<string> WhenKinds = new(["mutation", "timer", "stream", "listener", "drawn"], StringComparer.Ordinal);
    /// <summary>An outside call's error status.</summary>
    static readonly Regex HttpError = new(@"StatusCode|HasHttpError|IsSuccessStatusCode|HttpStatusCode|EnsureSuccess|\bresponse\.ok\b|\bres\.ok\b|\.status\s*(!==?|===?|>=?|<=?)", RegexOptions.CultureInvariant);

    // The setup-step test (D273): the save's every deciding condition says a value is empty, and that value was assigned by a read of the same table.
    static readonly Regex[] Empty =
    [
        new(@"^\(?\s*(?<v>[A-Za-z_][\w.]*)\s*(==\s*null|is\s+null|===?\s*(null|undefined))\s*\)?$", RegexOptions.CultureInvariant),
        new(@"^!\s*(?<v>[A-Za-z_][\w.]*)\.Any\(\)$", RegexOptions.CultureInvariant),
        new(@"^(?<v>[A-Za-z_][\w.]*)\.(Count|Length|length)(\(\))?\s*===?\s*0$", RegexOptions.CultureInvariant),
        new(@"^!\s*(?<v>[A-Za-z_][\w.]*)$", RegexOptions.CultureInvariant),
    ];

    sealed class Element
    {
        public required string Kind { get; init; }   // checks, save, outside-call, message, queue, later, unknown, exit
        public required string Text { get; init; }
        public required string At { get; init; }
        public string Label { get; set; } = "certain";
        public required List<SummaryCondition> Path { get; init; }
        public required (int, int, int) Order { get; init; }
        public string Key { get; init; } = "";
        public string? Service { get; init; }
        public List<int> Steps { get; } = [];
        public List<ChartStop> Stops { get; } = [];
        public List<SummaryCondition> Own { get; init; } = [];
        public List<string> FollowOns { get; } = [];
        public string? Table { get; init; }
        public bool Redirect { get; init; }
        /// <summary>D305(2): an exit's last step before it in the code (its method's steps above it and all under them); null when none is.</summary>
        public int? After { get; init; }
        /// <summary>D311(3): an exception stop's kind and class (what it throws, where); null for any other stop.</summary>
        public string? Kin { get; init; }
        /// <summary>D296: the steps whose changes this repeat carries ("as in step 31").</summary>
        public List<int> AsIn { get; } = [];
        /// <summary>D297: the code line added when another box on the chart would read the same.</summary>
        public bool WithLine { get; set; }
        /// <summary>D311(1): what a list line says of it without its steps, and what makes two lines the same change.</summary>
        public BoxInfo Info => new(Kind == "shared" ? $"shared|{Text}{Mark(Label)}" : $"{Kind}|{Text}{Mark(Label)}|{At}", Text + Mark(Label), Steps, At, WithLine);
        public string Box => Text + Mark(Label) + (AsIn.Count > 0 || WithLine ? $" ({string.Join(", ", new[] { Steps.Count > 0 ? StepList(Steps) : "", AsIn.Count > 0 ? $"as in {StepList(AsIn)}" : "", WithLine ? $"`{ShortAt(At)}`" : "" }.Where(x => x != ""))})" : Tag(Steps, At));
    }

    /// <summary>
    /// D311(1): list lines (a too-large chart's **Changes**, a scenario row's skipped changes), the same change repeated word for word
    /// (a repeat "as in step N" under other conditions: its own box on a chart) shown once with every step it occurs at. A line
    /// alone keeps its words as the chart box reads.
    /// </summary>
    internal static List<string> Once(IEnumerable<(string Label, BoxInfo Info)> boxes)
    {
        var groups = new List<List<(string Label, BoxInfo Info)>>();
        var at = new Dictionary<string, int>(O);
        foreach (var b in boxes)
            if (at.TryGetValue(b.Info.Group, out var i)) groups[i].Add(b);
            else { at[b.Info.Group] = groups.Count; groups.Add([b]); }
        return [.. groups.Select(g => g.Count == 1 ? g[0].Label : Merged(g.Select(x => x.Info).ToList()))];
    }

    static string Merged(List<BoxInfo> same)
    {
        var steps = same.SelectMany(x => x.Steps).Distinct().Order().ToList();
        var line = same.Any(x => x.WithLine) ? $", `{ShortAt(same[0].At)}`" : "";
        return steps.Count == 0 ? same[0].Head + Tag(steps, same[0].At) : $"{same[0].Head} ({StepList(steps)}{line})";
    }

    /// <summary>A line's file name and line, without its folders: <c>GenericAttributeService.cs:140</c>.</summary>
    static string ShortAt(string at) => at.Contains('/') ? at[(at.LastIndexOf('/') + 1)..] : at;

    /// <summary>" (step 8)", " (steps 8, 10)", or the line when there's no step.</summary>
    static string Tag(IReadOnlyList<int> steps, string at) =>
        steps.Count == 0 ? (at == "" ? "" : $" (at {at})") : $" ({StepList(steps)})";

    static string StepList(IEnumerable<int> steps)
    {
        var list = steps.Distinct().Order().ToList();
        return $"step{(list.Count == 1 ? "" : "s")} {string.Join(", ", list)}";
    }

    /// <summary>The page of one area, placed by <paramref name="placement"/>.</summary>
    public static AreaCharts Build(string root, Areas a, Placement placement, string area, string folder, PageSettings? settings = null, PageCache? cache = null, AreaLayout? layout = null)
    {
        cache ??= new PageCache(root, a);
        settings ??= new PageSettings();
        var reader = cache.Reader;
        var (hub, depth) = IndexSettings(root);
        var result = new AreaCharts
        {
            Area = area, Folder = folder, Commit = a.Commit, HubCallers = hub, Depth = depth, Settings = settings,
            AppFlows = a.Flows.Values.Count(f => f.Kind != "shared"),
        };
        var starts = placement.AreaOf.Where(x => x.Value == area && a.Flows.ContainsKey(x.Key)).Select(x => a.Flows[x.Key])
            .OrderBy(f => f.Name.ToLowerInvariant(), O).ThenBy(f => f.Id, O).ToList();
        foreach (var f in starts) if (reader.Load(f.Id) is null) result.Missing.Add(f.Id);
        var readable = starts.Where(f => reader.Load(f.Id) is not null).ToList();

        // The summary's order (D253): each screen with the clicks placed on it, clicks on no screen, handlers by message, jobs.
        var screens = readable.Where(f => f.Kind == "screen-opens").ToList();
        var byScreen = screens.ToDictionary(s => s.Id, _ => new List<IndexFlow>(), O);
        var points = screens.GroupBy(s => s.Point, O).ToDictionary(g => g.Key, g => g.First().Id, O);
        var clicksOn = screens.ToDictionary(s => s.Id, _ => new List<IndexFlow>(), O);   // every click on a screen, for the screen's own chart
        var loose = new List<IndexFlow>();
        foreach (var c in readable.Where(f => f.Kind == "click"))
        {
            var on = a.ScreensOf(c.Id).Where(points.ContainsKey).ToList();
            foreach (var p in on) foreach (var s in screens.Where(s => s.Point == p)) clicksOn[s.Id].Add(c);
            var first = screens.FirstOrDefault(s => on.Contains(s.Point));
            if (first is not null) byScreen[first.Id].Add(c); else loose.Add(c);
        }
        var order = new List<IndexFlow>();
        foreach (var s in screens) { order.Add(s); order.AddRange(byScreen[s.Id]); }
        order.AddRange(loose);
        order.AddRange(readable.Where(f => f.Kind == "handler").GroupBy(f => PageSummary.Message(reader.Load(f.Id)!), O).OrderBy(g => g.Key, O).SelectMany(g => g));
        order.AddRange(readable.Where(f => f.Kind == "job"));
        order.AddRange(readable.Where(f => f.Kind is not ("screen-opens" or "click" or "handler" or "job")));

        var rules = new Rules(cache.Counts, hub, settings, result.AppFlows);
        var cut = Heavy(reader, a, area, readable, rules);
        result.Cut = cut.Cut;
        foreach (var f in order)
        {
            var flow = reader.Load(f.Id)!;
            var mine = f.Kind == "screen-opens" ? ClickPlaces(reader, clicksOn[f.Id], cut.Cut) : null;
            if (mine is not null) result.InClicks[f.Id] = mine;
            var counts = CountSteps(flow);
            foreach (var u in (flow["unknowns"] as JsonArray ?? []).OfType<JsonObject>())
                result.CantSee.Add((f.Id, (int?)u["step"], (string?)u["why"] ?? "", (string?)u["at"] ?? ""));
            var chart = Chart(reader, flow, rules, mine, counts, out var why, out var listed, out var followOns, cut.Cut);
            if (chart is null)
            {
                var d = new DisplayOnly(f.Id, f.Kind, f.Name == "" ? f.Id : f.Name, why!, counts);
                d.Listed.AddRange(listed);
                d.FollowOns.AddRange(followOns);
                OneLineStops(flow, rules, mine, d);
                d.Handled.AddRange(HandledRows(flow, mine));
                result.DisplayOnly.Add(d);
            }
            else
            {
                chart.Handled.AddRange(HandledRows(flow, mine));
                chart.Drawn = chart.Chart.Nodes.Count <= settings.ChartCap;
                chart.Start = StartOf(flow);
                result.Charts.Add(chart);
            }
        }
        // D306: each heavy shared flow once, in a part of its own: a chart like a click's, or one line with its stops.
        foreach (var part in cut.Parts)
        {
            var flow = reader.Load(part.Flow)!;
            var counts = CountSteps(flow);
            var title = $"Shared work: {part.Name}";
            var chart = Chart(reader, flow, rules, null, counts, out var why, out var listed, out var followOns, cut.Cut);
            if (chart is null)
            {
                var d = new DisplayOnly(part.Flow, "shared", title, why!, counts);
                d.Listed.AddRange(listed);
                d.FollowOns.AddRange(followOns);
                OneLineStops(flow, rules, null, d);
                d.Handled.AddRange(HandledRows(flow, null));
                part.Line = d;
            }
            else
            {
                Retitle(chart, title);
                chart.Handled.AddRange(HandledRows(flow, null));
                chart.Drawn = chart.Chart.Nodes.Count <= settings.ChartCap;
                part.Chart = chart;
            }
            result.Shared.Add(part);
        }
        Distinct(reader, result);
        // D313(2): each kind's charted flows small to large by chart nodes (too large to draw last), ties in the summary's order.
        var sorted = result.Charts.OrderBy(c => KindRank(c.Kind)).ThenBy(c => c.Drawn ? 0 : 1).ThenBy(c => c.Chart.Nodes.Count).ToList();
        result.Charts.Clear();
        result.Charts.AddRange(sorted);
        var place = result.Charts.Select(c => c.Flow).Concat(result.DisplayOnly.Select(d => d.Flow)).Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, O);
        var seen = result.CantSee.OrderBy(x => place.GetValueOrDefault(x.Flow, int.MaxValue)).ToList();
        result.CantSee.Clear();
        result.CantSee.AddRange(seen);
        // D311(2): the shared flows the page lists, and those it leaves out because they do nothing a reader acts on.
        var memo = new Dictionary<string, bool>(O);
        foreach (var id in placement.AreaOf.Where(x => x.Value == area && a.Flows.ContainsKey(x.Key)).SelectMany(x => a.Reach(x.Key, sharedOnly: true)).Distinct(O).Order(O))
        {
            result.Reached.Add(id);
            if (Quiet(reader, id, memo)) result.Quiet.Add(id);
        }
        SameChecks(result);
        result.Elsewhere.AddRange(PageElsewhere.Build(cache.Elsewhere, a, placement, layout, area, starts.Select(f => f.Id)));
        Name(a, layout, area, result);
        if (layout is not null) Parts(a, layout, area, result);
        return result;
    }

    /// <summary>
    /// D314(4): a flow whose field checks (its K rows: field, rules and line) are the same set as an earlier flow's on the page, in
    /// page order (the clicks, then the shared parts), says "same checks as" that flow instead of repeating them; only a whole set,
    /// of <see cref="SameChecksFrom"/> rows or more.
    /// </summary>
    static void SameChecks(AreaCharts result)
    {
        static string Key(FlowChart c) => string.Join("\n", c.Scenarios.Where(r => IsCheckRow(r)).Select(r => $"{r.Scenario}\u0001{r.Decides}"));
        var first = new Dictionary<string, FlowChart>(O);
        foreach (var c in result.Charts.Concat(result.Shared.Select(p => p.Chart).OfType<FlowChart>()))
        {
            var key = Key(c);
            // A single check row is no shorter as a pointer to another flow, and its words say more: it stays.
            if (c.Scenarios.Count(IsCheckRow) < SameChecksFrom) continue;
            if (first.TryGetValue(key, out var earlier)) c.SameChecksAs = (earlier.Title, earlier.Flow);
            else first[key] = c;
        }
    }

    /// <summary>D314(4): a flow's checks are written once only when it has at least this many K rows.</summary>
    public const int SameChecksFrom = 2;

    internal static bool IsCheckRow(ScenarioRow r) => r.Id.Length > 1 && r.Id[0] == 'K' && r.Id[1..].All(char.IsAsciiDigit);

    /// <summary>D314(2): each flow a receiver line names, by its class and method (a start point's <see cref="Member"/>), else its name, linked from the page's folder.</summary>
    static void Name(Areas a, AreaLayout? layout, string area, AreaCharts result)
    {
        var rows = result.Charts.SelectMany(c => c.FollowOns)
            .Concat(result.DisplayOnly.SelectMany(d => d.FollowOns))
            .Concat(result.Shared.SelectMany(p => p.Chart?.FollowOns ?? p.Line?.FollowOns ?? []));
        var here = layout?.SlugOf.ContainsKey(area) == true ? layout.AreaFolder(area) : null;
        foreach (var id in rows.SelectMany(r => r.Targets).Distinct(O).Order(O))
        {
            if (!a.Flows.TryGetValue(id, out var f)) { result.Named[id] = ("", null); continue; }
            // A generic class's arity mark (`ExtraFileService`1`) is left off: it reads as a stray quote.
            var name = Regex.Replace(Member(f.Point) is { Length: > 0 } m ? m : Readable(f.Name), @"`\d+", "", RegexOptions.CultureInvariant);
            var link = here is not null && layout!.FolderOf.ContainsKey(id) ? AreaLayout.Relative(here, layout.Markdown(id)) : null;
            result.Named[id] = (name, link);
        }
    }

    static readonly Regex Qualified = new(@"\b(?:[A-Za-z_]\w*\.){2,}([A-Za-z_]\w*)\b", RegexOptions.CultureInvariant);

    /// <summary>D314(1): a name without its namespaces: <c>NzbDrone.Core.Tv.Commands.MoveSeriesCommand</c> → <c>MoveSeriesCommand</c>; <c>X.Handle</c> stays.</summary>
    internal static string Readable(string name) => Qualified.Replace(name, "$1");

    /// <summary>
    /// D314(1): a split area's parts (<c>&lt;Area&gt; › …</c>, each area whose nearest named parent is this one, as D227 finds parents)
    /// with their start flows by readable name; a part's parent and its sibling parts. Links are relative to this page's folder.
    /// </summary>
    static void Parts(Areas a, AreaLayout layout, string area, AreaCharts result)
    {
        var names = layout.SlugOf.Keys.ToList();
        string? ParentOf(string n) => names.Where(p => p != n && n.StartsWith(p + " › ", StringComparison.Ordinal)).OrderByDescending(p => p.Length).FirstOrDefault();
        var here = layout.AreaFolder(area);
        string Link(string n) => AreaLayout.Relative(here, $"{layout.AreaFolder(n)}/{AreaLayout.Page}");
        foreach (var part in names.Where(n => ParentOf(n) == area).OrderBy(n => n, O))
            result.Parts.Add((part, Link(part), StartNames(a, layout.Placement!, part)));
        if (ParentOf(area) is { } parent)
        {
            result.PartOf = (parent, Link(parent));
            foreach (var s in names.Where(n => n != area && ParentOf(n) == parent).OrderBy(n => n, O)) result.Siblings.Add((s, Link(s)));
        }
    }

    /// <summary>
    /// D314(1): an area's start flows by readable name, by kind: screens by address, clicks by label ("Monitored"; the same label
    /// twice "×2"), handlers and jobs by name without namespaces. Every flow is named.
    /// </summary>
    internal static string StartNames(Areas a, Placement placement, string area)
    {
        var flows = placement.AreaOf.Where(x => x.Value == area && a.Flows.ContainsKey(x.Key)).Select(x => a.Flows[x.Key]).ToList();
        if (flows.Count == 0) return "no start flows";
        static string Say(IndexFlow f) => f.Kind switch
        {
            "click" => $"\"{(f.Name == "" ? f.Id : f.Name)}\"",
            _ => $"`{Readable(f.Name == "" ? f.Id : f.Name).Replace('`', '\'')}`",
        };
        var kinds = new (string Kind, string One, string Many)[] { ("screen-opens", "screen", "screens"), ("click", "click", "clicks"), ("handler", "handler", "handlers"), ("job", "job", "jobs") };
        var said = new List<string>();
        foreach (var group in flows.GroupBy(f => kinds.Any(k => k.Kind == f.Kind) ? f.Kind : "other").OrderBy(g => KindRank(g.Key)))
        {
            var k = kinds.FirstOrDefault(x => x.Kind == group.Key);
            var label = group.Count() == 1 ? k.One ?? "other" : k.Many ?? "others";
            var each = group.Select(Say).GroupBy(x => x, O).OrderBy(g => g.Key.ToLowerInvariant(), O).ThenBy(g => g.Key, O)
                .Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ×{g.Count()}");
            said.Add($"{label} {string.Join(", ", each)}");
        }
        return $"{Plural(flows.Count, "start flow")}: {string.Join("; ", said)}";
    }

    /// <summary>D313(2): the order of start flow kinds on a page: screens, clicks, handlers, jobs, anything else.</summary>
    internal static int KindRank(string kind) => kind switch { "screen-opens" => 0, "click" => 1, "handler" => 2, "job" => 3, _ => 4 };

    static readonly Regex AskedFor = new(@"HTTP (?<v>[A-Z]+) (?<u>\S+?)(?=:\s|:$|\s|$)", RegexOptions.CultureInvariant);
    static readonly Regex Routed = new(@"^[A-Z]+ /\S*$", RegexOptions.CultureInvariant);
    static readonly Regex Host = new(@"^https?://[^/]+", RegexOptions.CultureInvariant);

    /// <summary>
    /// D313(1): where to start reading a start flow, from its file only. A start in a C# method (a handler, a job, a server-made
    /// screen) is that method and its line; any other start is its frontend line (a click's handler, a screen's route). Its own
    /// <c>http</c> steps are its requests: a matched one is the endpoint's method and path, landing on the step it leads to; one
    /// to this app the graph couldn't match is ❓ with what it asked for; one to another host (<c>to 'api.github.com'</c>) is an
    /// outside call, not a request.
    /// </summary>
    internal static StartHere StartOf(JsonObject flow)
    {
        var start = flow["start"] as JsonObject;
        var point = (string?)start?["point"] ?? "";
        var at = (string?)start?["at"];
        var kind = (string?)flow["kind"] ?? "";
        var server = point.Contains("|M:", StringComparison.Ordinal);
        var steps = FlowReader.Steps(flow);
        var requests = new List<StartRequest>();
        var said = new HashSet<string>(O);
        foreach (var st in steps.Values.Where(s => (string?)s["kind"] == "http").OrderBy(s => (int)s["n"]!))
        {
            var p = (string?)st["point"] ?? "";
            var n = (int)st["n"]!;
            StartRequest r;
            if (p.StartsWith("endpoint:", StringComparison.Ordinal) && p.IndexOf('|') is var bar and > 0)
            {
                var back = steps.Values.Where(s => (int?)s["from"] == n).OrderBy(s => (int)s["n"]!).Select(s => (string?)s["at"]).FirstOrDefault();
                r = new StartRequest(p[(bar + 1)..], false, back) { Guessed = (string?)st["label"] == "guessed" };
            }
            else
            {
                var detail = (string?)(st["end"] as JsonObject)?["detail"] ?? (string?)st["what"] ?? "";
                if (server || detail.Contains(" to '", StringComparison.Ordinal)) continue;   // an outside call, not one of this app's requests
                var m = AskedFor.Match(detail);
                r = new StartRequest(m.Success ? $"{m.Groups["v"].Value} {Host.Replace(m.Groups["u"].Value, "")}" : "", true, null);
            }
            if (said.Add(r.Endpoint + "\u0001" + r.Back)) requests.Add(r);
        }
        var name = (string?)flow["name"] ?? "";
        var route = server && kind == "screen-opens" && requests.Count == 0 ? Routed.IsMatch(name) ? name : "" : null;
        return new StartHere(server ? null : at, server ? Member(point) : null, server ? at : null, requests, kind is "click" or "screen-opens") { Route = route };
    }

    /// <summary>D313(1): the index's lines under a flow: where to start (its entry, its requests) and what it changes and sends.</summary>
    internal static string StartLines(FlowChart c)
    {
        var sb = new StringBuilder();
        static string Code(string text) => $"`{text.Replace('`', '\'')}`";
        static string Req(StartRequest r) => r.Unsure ? (r.Endpoint == "" ? "❓ request" : $"❓ {Code(r.Endpoint)}") : r.Guessed ? $"🟡 {Code(r.Endpoint)}" : Code(r.Endpoint);
        if (c.Start is { } s)
        {
            var from = s.Front is { } f ? Code(f) : s.EntryAt is { } e ? $"{Code(s.Entry ?? "")} at {Code(e)}" : null;
            static string Lands(StartRequest r) => $"{Req(r)} → {(r.Back is { } b ? Code(b) : "❓")}";
            var reqs = s.Requests;
            if (reqs.Count == 0 && s.Asks && s.Route is null) reqs = [new StartRequest("", true, null)];
            if (s.Route is { } route)   // a screen the server makes: its route lands on the action it starts in
                sb.Append($"  - start here: {(route == "" ? "❓ request" : Code(route))} → {from}\n");
            else if (reqs.Count <= 1)
            {
                var parts = new List<string>();
                if (from is not null) parts.Add(from);
                if (reqs.Count == 1) parts.Add(reqs[0].Unsure && reqs[0].Back is null ? Req(reqs[0]) : Lands(reqs[0]));
                if (parts.Count > 0) sb.Append($"  - start here: {string.Join(" → ", parts)}\n");
            }
            else
            {
                sb.Append($"  - start here: {from ?? "?"}, {reqs.Count} requests:\n");
                foreach (var r in reqs.Take(Listed)) sb.Append($"    - {(r.Unsure && r.Back is null ? Req(r) : Lands(r))}\n");
                if (reqs.Count > Listed) sb.Append($"    - +{reqs.Count - Listed} more\n");
            }
        }
        static string Esc(string t) => Line(t).Replace("<", "\\<", StringComparison.Ordinal);
        static List<string> Distinct(IEnumerable<string> xs) => [.. xs.Distinct(StringComparer.Ordinal)];
        var made = c.Made.Select((m, i) => (m.Kind, m.Text, m.Service, When: i < c.MadeWhen.Count ? c.MadeWhen[i] : null)).ToList();
        // D314(3): a change made only under a condition says so, with its nearest condition and line ("…" when there are more);
        // the same change made anywhere unconditionally stays plain.
        static List<string> When(IEnumerable<(string Key, (string Phrase, string At, int Count)? When)> xs, Func<string, string> show) =>
            [.. xs.GroupBy(x => x.Key, StringComparer.Ordinal).Select(g => g.Any(x => x.When is null) ? show(g.Key)
                : g.First().When is var (phrase, at, count) ? $"{show(g.Key)} (only when {phrase}, `{at}`{(count > 1 ? ", …" : "")})" : show(g.Key))];
        var changes = When(made.Where(m => m.Kind is "save" or "file" or "push" or "queue" or "later").Select(m => (Esc(Brief(Plain(m.Text))), m.When)), x => x);
        var outside = Distinct(made.Where(m => m.Kind == "outside-call" && m.Service is not null).Select(m => m.Service!));
        static string Sent(string t) => t.StartsWith("sends ", StringComparison.Ordinal) ? t[6..] : t;
        var sends = When(made.Where(m => m.Kind == "message").Select(m => (Sent(Plain(m.Text)), m.When)), Code);
        var said = new List<string>();
        static string Few(List<string> xs) => string.Join(", ", xs.Take(Listed)) + (xs.Count > Listed ? $", +{xs.Count - Listed} more" : "");
        if (changes.Count > 0) said.Add(Few(changes));
        if (outside.Count > 0) said.Add($"calls outside: {Few([.. outside.Select(Code)])}");
        if (sends.Count > 0) said.Add($"sends {Few(sends)}");
        if (said.Count > 0) sb.Append($"  - changes: {string.Join(" · ", said)}\n");
        return sb.ToString();
    }

    /// <summary>
    /// D314(3): a deciding condition said for when it holds, the D311 way: a known shape in words ("has series", "Title is empty"),
    /// else its code in backticks (<c>`queueMove`</c>; an else's or guard's as "`x` is false", a negated one plainly).
    /// </summary>
    internal static string OnlyIf(SummaryCondition c)
    {
        if (Holds(c) is { } p) return p;
        var code = c.Code.Trim().Replace('`', '\'');
        return c.Kind switch
        {
            "if" => Negated(code) is { } plain ? $"`{plain}` is false" : $"`{code}`",
            "else" or "unless" => Negated(code) is { } plain ? $"`{plain}`" : $"`{code}` is false",
            "loop" => $"for each item of `{code}`",
            "case" => c.Case is null or "" or "_" or "default" ? $"`{code}` matches no other case" : $"`{code}` is {c.Case}",
            "catch" => $"on error `{code}`",
            "null" => $"`{code}` is null",
            "set" => $"`{code}` is set",
            "cache-miss" => $"nothing is cached for `{code}`",
            _ => $"`{code}`",
        };
    }

    /// <summary>D313(1): queued work without "which runs later, …": the line says what, the flow's part says when.</summary>
    static string Brief(string text) => text.IndexOf(", which runs later", StringComparison.Ordinal) is var i and > 0 ? text[..i] : text;

    /// <summary>D313(1): how many requests, changes, services or events a start-here line names before "+N more".</summary>
    const int Listed = 3;

    /// <summary>D313(4): the line saying what the page's marks mean.</summary>
    public const string Legend = "**How to read this page:** 🟡 guessed: code picked the likeliest target and says why; ❓ can't tell: code couldn't follow it and says why; \"step N\" is the Nth step in that flow's own file, `<flow id>.md` beside this page (or in the folder its link names).";

    /// <summary>D311(2): the step kinds a quiet shared flow may hold: plain calls, package code and filters.</summary>
    static readonly HashSet<string> QuietKinds = new(["call", "package", "filter"], StringComparer.Ordinal);

    /// <summary>
    /// D311(2): a shared flow that changes nothing, stops nothing and calls nothing outside, nor does anything it continues into:
    /// only plain calls, package code and filters (no database step, read or save, no server call, no file, message, push or
    /// queued work, no plugin call), no stop, no ❓, and no link but "continues" into flows as quiet (<c>SqlBuilder.Where</c>,
    /// <c>StringExtensions.IsNullOrWhiteSpace</c>). A flow with no readable file isn't quiet.
    /// </summary>
    internal static bool Quiet(FlowReader reader, string id, Dictionary<string, bool> memo)
    {
        if (memo.TryGetValue(id, out var known)) return known;
        memo[id] = true;   // a cycle back here adds nothing loud
        var quiet = reader.Load(id) is { } flow
            && (flow["exits"] as JsonArray ?? []).Count == 0
            && (flow["unknowns"] as JsonArray ?? []).Count == 0
            && FlowReader.Steps(flow).Values.All(st => QuietKinds.Contains((string?)st["kind"] ?? "") && !PluginCall((string?)st["what"] ?? ""))
            && (flow["links"] as JsonArray ?? []).OfType<JsonObject>().All(l => (string?)l["kind"] == "continues" && (string?)l["flow"] is { } to && Quiet(reader, to, memo));
        memo[id] = quiet;
        return quiet;
    }

    /// <summary>A chart's title and its start box, which reads the same.</summary>
    static void Retitle(FlowChart chart, string title)
    {
        chart.Title = title;
        var i = chart.Chart.Nodes.FindIndex(n => n.Shape == "start");
        if (i >= 0) chart.Chart.Nodes[i] = chart.Chart.Nodes[i] with { Label = title };
    }

    /// <summary>
    /// D306(3): start flows of one area whose titles read the same each add what tells them apart: their handler, component or
    /// method (<c>SceneMappingService.Handle</c>), then, if still the same, their start's code line.
    /// </summary>
    static void Distinct(FlowReader reader, AreaCharts result)
    {
        List<string> Whats(string id)
        {
            var start = reader.Load(id)?["start"] as JsonObject;
            var point = (string?)start?["point"] ?? "";
            return [$" ({Member(point)})", $" `{ShortAt((string?)start?["at"] ?? "")}`", Parameters(point) is { Length: > 0 } ps ? $" ({ps})" : "", $" (`{id}`)"];
        }
        // Each round adds the next thing to the titles still the same, until none are.
        static List<string> Apart(List<string> titles, List<List<string>> whats)
        {
            var list = titles.ToList();
            for (var round = 0; round < 4; round++)
            {
                var same = list.Select(t => list.Count(x => x == t) > 1).ToList();
                if (!same.Contains(true)) break;
                for (var i = 0; i < list.Count; i++)
                    if (same[i] && whats[i][round] is { Length: > 0 } add && add != " ()" && add != " ``") list[i] += add;
            }
            return list;
        }
        var charts = Apart([.. result.Charts.Select(c => c.Title)], [.. result.Charts.Select(c => Whats(c.Flow))]);
        for (var i = 0; i < charts.Count; i++) if (charts[i] != result.Charts[i].Title) Retitle(result.Charts[i], charts[i]);
        var lines = Apart([.. result.DisplayOnly.Select(d => d.Name)], [.. result.DisplayOnly.Select(d => Whats(d.Flow))]);
        for (var i = 0; i < lines.Count; i++) result.DisplayOnly[i].Shown = lines[i];
        // Shared parts by their names, which are already their methods: the code line, then the parameters, then the id.
        var parts = Apart([.. result.Shared.Select(p => p.Chart?.Title ?? p.Line!.Name)], [.. result.Shared.Select(p => Whats(p.Flow) is var w ? (List<string>)["", w[1], w[2], w[3]] : [])]);
        for (var i = 0; i < parts.Count; i++)
        {
            if (result.Shared[i].Chart is { } c) { if (c.Title != parts[i]) Retitle(c, parts[i]); }
            else result.Shared[i].Line = result.Shared[i].Line! with { Name = parts[i], Shown = parts[i] };
        }
    }

    /// <summary>A C# point's parameter types, short (<c>EpisodeSearchCommand</c>); "" for none or a script's point.</summary>
    internal static string Parameters(string point)
    {
        var open = point.IndexOf('(');
        if (point.IndexOf("|M:", StringComparison.Ordinal) < 0 || open < 0 || !point.EndsWith(')')) return "";
        var inner = point[(open + 1)..^1];
        var parts = new List<string>();
        int depth = 0, from = 0;
        for (var i = 0; i <= inner.Length; i++)
        {
            if (i < inner.Length && inner[i] is '{' or '(' or '[') depth++;
            else if (i < inner.Length && inner[i] is '}' or ')' or ']') depth--;
            else if (i == inner.Length || (inner[i] == ',' && depth == 0))
            {
                parts.Add(Regex.Replace(inner[from..i], @"(?:\w+\.)+(\w+)", "$1", RegexOptions.CultureInvariant));
                from = i + 1;
            }
        }
        return string.Join(", ", parts.Where(x => x != "")).Replace('{', '<').Replace('}', '>');
    }

    /// <summary>
    /// The method a start point names, with its type: <c>SceneMappingService.Handle</c> from a C# point, <c>AddNewSeriesModalContent.handleAddSeriesPress</c>
    /// from a script's; "" when it names none.
    /// </summary>
    internal static string Member(string point)
    {
        string name;
        if (point.IndexOf("|M:", StringComparison.Ordinal) is var m and >= 0)
        {
            name = point[(m + 3)..];
            if (name.IndexOf('(') is var p and >= 0) name = name[..p];
        }
        else if (point.IndexOf('#') is var h and >= 0) name = point[(h + 1)..];
        else return "";
        var parts = name.Split('.');
        return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : name;
    }

    /// <summary>
    /// D306: an area's heavy shared flows: shared flows the area's start flows continue into (through other shared flows too), reached
    /// by <see cref="SharedReach"/> or more of them or longer than <see cref="SharedSteps"/> steps, that bring a flow something to show
    /// (a save, push, file, plugin call or message). Each is shown once; a flow continuing into one shows a line that runs it.
    /// </summary>
    static (SharedCut? Cut, List<SharedPart> Parts) Heavy(FlowReader reader, Areas a, string area, List<IndexFlow> starts, Rules rules)
    {
        var reach = new Dictionary<string, HashSet<string>>(O);
        foreach (var f in starts)
        {
            var seen = new HashSet<string>(O);
            var queue = new Queue<string>([f.Id]);
            while (queue.Count > 0)
                foreach (var t in Continues(reader.Load(queue.Dequeue())))
                    if (a.Flows.TryGetValue(t, out var tf) && tf.Kind == "shared" && seen.Add(t))
                    {
                        (reach.TryGetValue(t, out var r) ? r : reach[t] = new HashSet<string>(O)).Add(f.Id);
                        queue.Enqueue(t);
                    }
        }
        var heavy = new SortedSet<string>(O);
        var parts = new Dictionary<string, SharedPart>(O);
        var frameworks = new Dictionary<string, int>(O);
        foreach (var (id, by) in reach)
        {
            if (reader.Load(id) is not { } flow) continue;
            var size = FlowReader.Steps(flow).Count;
            if (size <= SharedMinSteps || (by.Count < SharedReach && size <= SharedSteps)) continue;
            // D307: only what a flow reaching it showed (D260's saves, D304(6)'s messages) moves; stops alone move nothing.
            var (changes, named, places) = Brought(reader, id);
            if (changes == 0) continue;
            frameworks[id] = places.Select(rules.Counts.Change).Where(rules.Framework).DefaultIfEmpty(0).Max() is var most and > 0 && places.All(x => rules.Framework(rules.Counts.Change(x))) ? most : 0;
            heavy.Add(id);
            var stops = (flow["exits"] as JsonArray ?? []).OfType<JsonObject>().Select(e => (string?)e["at"] ?? "").Distinct(O).Count();
            parts[id] = new SharedPart(id, (string?)flow["name"] ?? id, by.Count, size, changes, stops) { Named = named };
        }
        if (heavy.Count == 0) return (null, []);
        var cut = new SharedCut($"area:{area}", heavy, id =>
        {
            var p = parts[id];
            var named = p.Named.Count <= SharedNamed ? $" ({string.Join(", ", p.Named)})" : "";
            return $"runs {p.Name} (see its part): {p.Changes} change{(p.Changes == 1 ? "" : "s")}{named}, {p.Stops} stop{(p.Stops == 1 ? "" : "s")}";
        });
        foreach (var (id, flows) in frameworks) if (flows > 0) cut.Framework[cut.Box(id)] = flows;
        return (cut, [.. parts.Values.OrderBy(p => p.Name, O).ThenBy(p => p.Flow, O)]);
    }

    /// <summary>The shared flows a flow file's <c>continues</c> links go into.</summary>
    static IEnumerable<string> Continues(JsonObject? flow) =>
        (flow?["links"] as JsonArray ?? []).OfType<JsonObject>().Where(l => (string?)l["kind"] == "continues").Select(l => (string?)l["flow"]).OfType<string>();

    /// <summary>D306, D307: the changes a shared flow leads to: its own change steps and those of the shared code it runs (D260's saves, pushes, files, plugin calls), each line once.</summary>
    static (int Count, List<string> Named, List<string> Places) Brought(FlowReader reader, string id)
    {
        var keys = new HashSet<string>(O);
        var named = new List<string>();
        var places = new List<string>();
        void Add(string kind, string at, string text)
        {
            if (!keys.Add($"{kind}|{at}|{text}")) return;
            places.Add(at);
            if (!named.Contains(text)) named.Add(text);
        }
        foreach (var sv in reader.SharedSaves(id)) Add(sv.Kind, sv.At, Plain(sv.Text));
        foreach (var m in reader.SharedMessages(id))
        {
            var sentence = (string?)m["sentence"] ?? "";
            var comma = sentence.IndexOf(", handled by ", StringComparison.Ordinal);
            var said = comma > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..comma] : sentence;
            Add("message", (string?)m["at"] ?? "", said);
        }
        return (keys.Count, named, places);
    }

    /// <summary>Each area's developer page for <c>areas check</c> (D285), by area and folder, the flow files read once for all of them.</summary>
    public static Func<string, string, AreaCharts> Pages(string root, Areas a, AreaLayout layout, PageSettings settings)
    {
        var cache = new PageCache(root, a);
        return (area, folder) => Build(root, a, layout.Placement!, area, folder, settings, cache, layout);
    }

    /// <summary>The hub threshold and depth flows were built with (<c>settings</c> in the flow index, D172); 10 and 8 when it doesn't say.</summary>
    static (int Hub, int Depth) IndexSettings(string root)
    {
        var path = Path.Combine(root, FlowFiles.IndexFile);
        if (!File.Exists(path)) return (10, 8);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("settings", out var s)) return (10, 8);
        return (s.TryGetProperty("hubCallers", out var h) && h.TryGetInt32(out var n) ? n : 10, s.TryGetProperty("depth", out var d) && d.TryGetInt32(out var m) ? m : 8);
    }

    /// <summary>A flow file's steps by label (D283): ❓ the steps an unknown sits on (and each unknown with no step), 🟡 those guessed, the rest certain.</summary>
    public static StepCounts CountSteps(JsonObject flow)
    {
        var steps = FlowReader.Steps(flow);
        var unknown = (flow["unknowns"] as JsonArray ?? []).OfType<JsonObject>().Where(u => u["step"] is not null).Select(u => (int)u["step"]!).ToHashSet();
        var loose = (flow["unknowns"] as JsonArray ?? []).OfType<JsonObject>().Count(u => u["step"] is null);
        var guessed = (flow["guesses"] as JsonArray ?? []).OfType<JsonObject>().Where(g => g["step"] is not null).Select(g => (int)g["step"]!)
            .Concat(steps.Values.Where(s => (string?)s["label"] == "guessed").Select(s => (int)s["n"]!)).Where(n => !unknown.Contains(n)).ToHashSet();
        return new(steps.Keys.Count(n => !unknown.Contains(n) && !guessed.Contains(n)), guessed.Count, unknown.Count + loose);
    }

    /// <summary>The rules one build applies: shared guards by the hub threshold; framework changes by D281's share and floor.</summary>
    sealed class Rules(SharedCounts counts, int hub, PageSettings settings, int appFlows)
    {
        public SharedCounts Counts => counts;
        public int Hub => hub;
        /// <summary>D281: more than the share of the app's start flows pass it, and at least the floor.</summary>
        public bool Framework(int flows) => flows >= PageSettings.FrameworkFloor && (long)flows * 100 > (long)settings.FrameworkShare * appFlows;
    }

    /// <summary>
    /// How many start flows pass a line, read once over every flow file in the index the first time it's asked. An exit: the start
    /// flows (every flow but shared ones) with an exit at that line. A change: the start flows with a change step, a ❓ or a
    /// runs-later link at that line, plus every start flow that continues, through shared flows, into a flow that has one.
    /// </summary>
    internal sealed class SharedCounts(string root, Areas a)
    {
        Dictionary<string, int>? exits;
        Dictionary<string, List<string>>? changes;
        readonly Dictionary<string, int> changeCount = new(StringComparer.Ordinal);
        readonly Dictionary<string, HashSet<string>> reach = new(StringComparer.Ordinal);
        Dictionary<string, List<string>>? callers;

        void Read()
        {
            exits = new(O);
            changes = new(O);
            callers = new(O);
            foreach (var f in a.Flows.Values.OrderBy(f => f.Id, O))
            {
                foreach (var l in f.Links)
                    if (a.Flows.TryGetValue(l, out var t) && t.Kind == "shared")
                        (callers.TryGetValue(l, out var cs) ? cs : callers[l] = []).Add(f.Id);
                var path = Path.Combine(root, f.File ?? $"{FlowFiles.Folder}/{f.Id}.json");
                if (!File.Exists(path)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                    var r = doc.RootElement;
                    var seen = new HashSet<string>(O);
                    if (f.Kind != "shared" && r.TryGetProperty("exits", out var ex))
                        foreach (var e in ex.EnumerateArray())
                            if (e.TryGetProperty("at", out var x) && x.GetString() is { } s && seen.Add(s)) exits[s] = exits.GetValueOrDefault(s) + 1;
                    var places = new HashSet<string>(O);
                    if (r.TryGetProperty("steps", out var steps))
                        foreach (var st in steps.EnumerateArray())
                        {
                            var kind = st.TryGetProperty("kind", out var k) ? k.GetString() : null;
                            var what = st.TryGetProperty("what", out var w) ? w.GetString() ?? "" : "";
                            var change = kind == "unknown" || PageChart.Change(kind, what) is not null;
                            if (change && st.TryGetProperty("at", out var at) && at.GetString() is { } s) places.Add(s);
                        }
                    foreach (var key in new[] { "unknowns", "links" })
                        if (r.TryGetProperty(key, out var list))
                            foreach (var u in list.EnumerateArray())
                                if ((key == "unknowns" || (u.TryGetProperty("kind", out var lk) && lk.GetString() == "runs-later")) && u.TryGetProperty("at", out var at) && at.GetString() is { } s)
                                    places.Add(s);
                    foreach (var p in places) (changes.TryGetValue(p, out var fs) ? fs : changes[p] = []).Add(f.Id);
                }
                catch (JsonException) { }
            }
        }

        public int Exit(string at)
        {
            if (exits is null) Read();
            return exits!.GetValueOrDefault(at);
        }

        public int Change(string at)
        {
            if (changes is null) Read();
            if (changeCount.TryGetValue(at, out var n)) return n;
            var starts = new HashSet<string>(O);
            foreach (var f in changes!.GetValueOrDefault(at) ?? []) starts.UnionWith(Reach(f));
            return changeCount[at] = starts.Count;
        }

        /// <summary>The start flows that run a flow: itself, or for a shared flow every start flow continuing into it.</summary>
        HashSet<string> Reach(string id)
        {
            if (reach.TryGetValue(id, out var have)) return have;
            var set = new HashSet<string>(O);
            if (!a.Flows.TryGetValue(id, out var f) || f.Kind != "shared") { set.Add(id); return reach[id] = set; }
            var seen = new HashSet<string>(O) { id };
            var queue = new Queue<string>([id]);
            while (queue.Count > 0)
                foreach (var c in callers!.GetValueOrDefault(queue.Dequeue()) ?? [])
                    if (seen.Add(c))
                    {
                        if (a.Flows[c].Kind == "shared") queue.Enqueue(c); else set.Add(c);
                    }
            return reach[id] = set;
        }
    }

    /// <summary>The changes, exits and checks of a screen's clicks, by place: the screen's walk reaches them through the click, so they're the click's chart.</summary>
    static HashSet<string> ClickPlaces(FlowReader reader, List<IndexFlow> clicks, SharedCut? cut)
    {
        var set = new HashSet<string>(O);
        foreach (var c in clicks)
        {
            if (reader.Load(c.Id) is not { } flow) continue;
            foreach (var s in FlowReader.Steps(flow).Values) if (Change(s) is not null) set.Add($"step|{(string?)s["at"]}");
            foreach (var u in (flow["unknowns"] as JsonArray ?? []).OfType<JsonObject>()) set.Add($"step|{(string?)u["at"]}");
            foreach (var e in (flow["exits"] as JsonArray ?? []).OfType<JsonObject>()) set.Add($"exit|{(string?)e["at"]}");
            foreach (var e in (flow["handled"] as JsonArray ?? []).OfType<JsonObject>()) set.Add($"handled|{(string?)e["at"]}");
            foreach (var l in ReceiverLinks(reader, (flow["links"] as JsonArray ?? []).OfType<JsonObject>(), cut).Where(l => (string?)l["kind"] == "message")) set.Add($"message|{(string?)l["at"]}");
            foreach (var k in (flow["checks"] as JsonArray ?? []).OfType<JsonObject>()) set.Add($"check|{(string?)k["at"]}");
            foreach (var l in (flow["links"] as JsonArray ?? []).OfType<JsonObject>())
                if ((string?)l["kind"] == "continues" && (string?)l["flow"] is not null) foreach (var sv in SharedOf(reader, l, false, cut)) set.Add($"step|{sv.At}");
                else if ((string?)l["kind"] == "runs-later") set.Add($"later|{(string?)l["at"]}");
        }
        return set;
    }

    /// <summary>What change a step is, if any: save, outside-call, message, queue.</summary>
    internal static string? Change(JsonObject st) => Change((string?)st["kind"], (string?)st["what"] ?? "");

    /// <summary>
    /// What change a step of this kind and words is: a save (D291: "reads or saves" is one that may save), an outside call, a message,
    /// queued work, a push to the browser (D292) or a file written or deleted (D294); null for anything else.
    /// </summary>
    internal static string? Change(string? kind, string what) => kind switch
    {
        "database" => what.StartsWith("reads or saves", StringComparison.Ordinal) || !what.StartsWith("reads", StringComparison.Ordinal) ? "save" : null,
        "outside-call" => "outside-call",
        "message" => "message",
        "queue" => "queue",
        "push" => "push",
        "file" => "file",
        _ => null,
    };

    /// <summary>D305(1): a plugin's outside call, listed at the call that may run the plugin.</summary>
    internal static bool PluginCall(string what) => what.StartsWith("may call ", StringComparison.Ordinal) && what.Contains(" through the plugin ", StringComparison.Ordinal);

    /// <summary>D291: a step that reads or saves, shown as one that may save.</summary>
    internal static string ChangeText(string what) =>
        what.StartsWith("reads or saves ", StringComparison.Ordinal) ? $"may save {what["reads or saves ".Length..]}" : what;

    /// <summary>D306(4): a change's words without where in shared code it is made ("saves X (in shared code Y)" → "saves X").</summary>
    internal static string Plain(string text)
    {
        var i = text.IndexOf(" (in shared code ", StringComparison.Ordinal);
        return i > 0 && text.EndsWith(')') ? text[..i] : text;
    }

    static string FileStem(string at)
    {
        var file = at.Contains(':') ? at[..at.LastIndexOf(':')] : at;
        var name = Path.GetFileName(file);
        var dot = name.IndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    static string? TableOf(string text) => text.Split(' ') is { Length: >= 2 } w && w[0].ToLowerInvariant() is "saves" or "reads" ? w[1].TrimEnd(',') : null;

    static FlowChart? Chart(FlowReader reader, JsonObject flow, Rules rules, HashSet<string>? clickPlaces, StepCounts stepCounts, out string? why, out List<ChartRow> listed, out List<ChartRow> followOns, SharedCut? cut = null)
    {
        why = null;
        listed = [];
        followOns = [];
        var id = (string)flow["id"]!;
        var kind = (string)flow["kind"]!;
        var steps = FlowReader.Steps(flow);
        var children = steps.Values.Where(s => s["from"] is not null).GroupBy(s => (int)s["from"]!).ToDictionary(g => g.Key, g => g.Select(s => (int)s["n"]!).Order().ToList());
        var exits = (flow["exits"] as JsonArray ?? []).OfType<JsonObject>().Where(e => clickPlaces?.Contains($"exit|{(string?)e["at"]}") != true).ToList();
        // An "unless" that is an exit's own guard is the exit, drawn already.
        var exitGuards = (flow["exits"] as JsonArray ?? []).OfType<JsonObject>().SelectMany(e => (e["conditions"] as JsonArray ?? []).OfType<JsonObject>()).Where(c => (string?)c["kind"] != "unless")
            .Select(c => $"{(string?)c["at"]}|{(string?)c["code"]}").ToHashSet(O);
        List<SummaryCondition> Decide(IEnumerable<SummaryCondition> cs) => [.. cs.Where(c => !WhenKinds.Contains(c.Kind) && !(c.Kind == "unless" && exitGuards.Contains($"{c.At}|{c.Code}")))];
        bool Mine(string place) => clickPlaces?.Contains(place) != true;
        // D284: a condition's step is the first step that writes it.
        var condStep = new Dictionary<string, int>(O);
        foreach (var st in steps.Values.OrderBy(s => (int)s["n"]!))
            foreach (var c in FlowReader.Conditions(st["conditions"] as JsonArray)) condStep.TryAdd(c.Key, (int)st["n"]!);

        var unknownAt = (flow["unknowns"] as JsonArray ?? []).OfType<JsonObject>().Where(u => u["step"] is not null).GroupBy(u => (int)u["step"]!).ToDictionary(g => g.Key, g => g.First());
        var links = (flow["links"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var elements = new List<Element>();
        var seq = 0;

        // Changes, in flow order.
        foreach (var st in steps.Values.OrderBy(s => (int)s["n"]!))
        {
            var n = (int)st["n"]!;
            var at = (string?)st["at"] ?? "";
            var change = Change(st);
            var unknown = unknownAt.ContainsKey(n);
            if (change is null && !unknown) continue;
            if (!Mine($"step|{at}")) continue;
            var what = (string)st["what"]!;
            var label = unknown ? "unknown" : (string?)st["label"] == "guessed" ? "guessed" : "certain";
            if (change == "message" && links.Any(l => (string?)l["kind"] == "message" && (int?)l["step"] == n && (string?)l["label"] == "guessed") && label == "certain") label = "guessed";
            var service = change == "outside-call" ? FileStem(at) : null;
            var text = change switch
            {
                null => what.StartsWith("can't be followed", StringComparison.Ordinal) ? what : $"can't be followed: {(string)unknownAt[n]["why"]!}",
                "outside-call" => $"{service} {Clean(what)}",
                _ => Clean(ChangeText(what)),
            };
            var k = change ?? "unknown";
            var e = new Element
            {
                Kind = k, Text = text, At = at, Label = label, Path = Decide(FlowReader.PathConditions(steps, n, true)), Order = (n, 1, seq++),
                Key = $"{k}|{text}|{at}", Service = service, Table = change == "save" ? TableOf(what) : null, Own = Decide(FlowReader.Conditions(st["conditions"] as JsonArray, true)),
            };
            e.Steps.Add(n);
            elements.Add(e);
        }
        // Saves in shared code the flow continues into (D260), at the step that continues there.
        foreach (var l in links)
        {
            var step = (int?)l["step"];
            var path = step is { } sn ? FlowReader.PathConditions(steps, sn, true) : [];
            if ((string?)l["kind"] == "continues" && (string?)l["flow"] is not null)
                foreach (var save in SharedOf(reader, l, true, cut))
                {
                    if (!Mine($"step|{save.At}")) continue;
                    var outside = save.Kind == "outside-call";
                    var text = outside ? $"{FileStem(save.At)} {Clean(save.Text)}" : save.Text;
                    var e = new Element
                    {
                        Kind = save.Kind, Text = text, At = save.At, Label = save.Label, Path = Decide(PageSummary.Merge(path, save.Conditions)),
                        // D306: one box per shared part, however many places run it.
                        Order = (step ?? int.MaxValue, 2, seq++), Key = save.Kind == "shared" ? $"shared|{text}" : $"{save.Kind}|{text}|{save.At}",
                        Table = outside || save.Kind == "shared" ? null : TableOf(save.Text), Own = Decide(save.Conditions),
                        Service = outside ? FileStem(save.At) : null,
                    };
                    if (step is { } s) e.Steps.Add(s);
                    elements.Add(e);
                }
        }
        // D296: a call "as in step N" carries step N's changes, under the repeat's own conditions.
        foreach (var c in Carried(reader, steps, children, links, cut))
        {
            if (!Mine($"step|{c.At}")) continue;
            var path = Decide(PageSummary.Merge(FlowReader.PathConditions(steps, c.Step, true), c.Inside));
            var original = Decide(c.From is { } from ? FlowReader.PathConditions(steps, from, true) : PageSummary.Merge(FlowReader.PathConditions(steps, c.Of, true), c.Inside));
            var same = path.Select(x => x.Key).ToHashSet(O).SetEquals(original.Select(x => x.Key));
            var k = c.Kind;
            var text = k == "outside-call" ? $"{FileStem(c.At)} {Clean(c.Text)}" : Clean(ChangeText(c.Text));
            var e = new Element
            {
                Kind = k, Text = text, At = c.At, Label = c.Label, Path = path, Order = (c.Step, 2, seq++),
                Key = k == "shared" && same ? $"shared|{text}" : same ? $"{k}|{text}|{c.At}" : $"{k}|{text}|{c.At}|as|{string.Join("\u0002", path.Select(x => x.Key))}",
                Service = k == "outside-call" ? FileStem(c.At) : null, Table = k == "save" ? TableOf(c.Text) : null, Own = Decide(c.Inside),
            };
            e.Steps.Add(c.Step);
            if (!same) e.AsIn.Add(c.From ?? c.Of);
            elements.Add(e);
        }
        foreach (var u in (flow["unknowns"] as JsonArray ?? []).OfType<JsonObject>().Where(u => u["step"] is null))
        {
            var at = (string?)u["at"] ?? "";
            if (!Mine($"step|{at}")) continue;
            var text = $"can't be followed: {(string)u["why"]!}";
            elements.Add(new Element { Kind = "unknown", Text = text, At = at, Label = "unknown", Path = [], Order = (int.MaxValue, 3, seq++), Key = $"unknown|{text}|{at}" });
        }

        // D281: a framework change is listed under shared code, not drawn, and doesn't make the flow a changing one.
        var shared = new List<ChartRow>();
        var sharedAt = new Dictionary<string, int>(O);
        var sharedByStep = new Dictionary<int, Element>();
        bool Framework(Element e)
        {
            // D306, D307: a line into a shared part is drawn, unless every change in the part is a framework change.
            if (e.Kind == "shared")
            {
                if (cut?.Framework.TryGetValue(e.Text, out var most) != true) return false;
                var bk = $"{e.Text}\u0001{e.At}";
                if (sharedAt.TryGetValue(bk, out var bi)) shared[bi] = shared[bi] with { Steps = [.. shared[bi].Steps.Concat(e.Steps).Distinct().Order()] };
                else { sharedAt[bk] = shared.Count; shared.Add(new ChartRow(e.Text, e.At, [.. e.Steps], [], most)); }
                foreach (var st in e.Steps) sharedByStep.TryAdd(st, e);
                return true;
            }
            var n = rules.Counts.Change(e.At);
            if (!rules.Framework(n)) return false;
            // D306(4): the same change at the same line, made directly and again through shared code, is one line with all its steps.
            var plain = Plain(e.Text);
            var key = $"{plain}\u0001{e.At}";
            if (sharedAt.TryGetValue(key, out var i))
                shared[i] = shared[i] with { Text = shared[i].Text == e.Text ? e.Text : plain, Steps = [.. shared[i].Steps.Concat(e.Steps).Distinct().Order()] };
            else { sharedAt[key] = shared.Count; shared.Add(new ChartRow(e.Text, e.At, [.. e.Steps], [], n)); }
            foreach (var st in e.Steps) sharedByStep.TryAdd(st, e);
            return true;
        }
        elements = [.. elements.Where(e => !Framework(e))];

        // Drawn once (D273): the first place a change is found; later places add their steps and follow-ons.
        var changes = new List<Element>();
        var byKey = new Dictionary<string, Element>(O);
        foreach (var e in elements.OrderBy(e => e.Order))
            if (byKey.TryAdd(e.Key, e)) changes.Add(e);
            else
            {
                var first = byKey[e.Key];
                foreach (var s in e.Steps) if (!first.Steps.Contains(s)) first.Steps.Add(s);
                foreach (var s in e.AsIn) if (!first.AsIn.Contains(s)) first.AsIn.Add(s);
                if (e.Label != "certain" && first.Label == "certain") first.Label = e.Label;
            }
        // What runs afterwards on its own (D273): a message's handlers and queued jobs; D304(6): the messages sent in shared flows it
        // continues into too, each its own send.
        var unDrawn = new List<Element>();
        foreach (var l in ReceiverLinks(reader, links, cut))
        {
            var step = (int?)l["step"];
            var target = (string?)l["flow"] ?? (string?)l["to"] ?? "";
            var sharedIn = (string?)l["sharedIn"];
            var on = sharedIn is null && step is { } sn ? changes.FirstOrDefault(c => c.Steps.Contains(sn) && c.Kind is not ("later" or "unknown")) : null;
            // D293: a send in shared code (D281's framework changes) isn't drawn, but who it sets off still runs afterwards.
            if (on is null && sharedIn is null && step is { } fs && sharedByStep.TryGetValue(fs, out var sharedSend)) on = sharedSend;
            if (on is null && (string?)l["kind"] == "message")
            {
                var at = (string?)l["at"] ?? "";
                if (!Mine($"message|{at}")) continue;
                var key = $"message|{at}|{step}";
                if (!byKey.TryGetValue(key, out on))
                {
                    var sentence = (string)l["sentence"]!;
                    var comma = sentence.IndexOf(", handled by ", StringComparison.Ordinal);
                    var said = comma > 0 ? char.ToLowerInvariant(sentence[0]) + sentence[1..comma] : sentence;
                    on = new Element { Kind = "message", Text = sharedIn is null ? said : $"{said} (in shared code {sharedIn})", At = at, Path = [], Order = (step ?? int.MaxValue, 1, seq++), Key = key };
                    if (step is { } ms) on.Steps.Add(ms);
                    byKey[key] = on;
                    unDrawn.Add(on);
                }
            }
            if ((string?)l["kind"] == "runs-later" && on is null)
            {
                var at = (string?)l["at"] ?? "";
                if (!Mine($"later|{at}")) continue;
                var sentence = (string)l["sentence"]!;
                var paren = sentence.LastIndexOf(" (", StringComparison.Ordinal);
                var text = paren > 0 && sentence.EndsWith(')') ? sentence[..paren] : sentence;
                var key = $"later|{target}";
                if (!byKey.TryGetValue(key, out on))
                {
                    var later = new Element
                    {
                        Kind = "later", Text = text, At = at, Label = (string?)l["label"] == "guessed" ? "guessed" : "certain",
                        Path = step is { } ls ? Decide(FlowReader.PathConditions(steps, ls, true)) : [], Order = (step ?? int.MaxValue, 1, seq++), Key = key,
                        Table = TableOf(text),
                    };
                    if (step is { } s) later.Steps.Add(s);
                    byKey[key] = on = later;
                    // D293: queued in shared code: not drawn, but what it sets off is still listed.
                    if (Framework(later)) unDrawn.Add(later);
                    else changes.Add(on);
                }
                else if (step is { } s && !on.Steps.Contains(s)) on.Steps.Add(s);
            }
            if (on is not null && target != "" && !on.FollowOns.Contains(target)) on.FollowOns.Add(target);
        }

        // D273: a setup step, a save whose only deciding conditions say nothing is saved yet, isn't drawn; it is a row of the table.
        var reads = Reads(reader, flow);
        var setups = new List<(Element Save, string Rule)>();
        foreach (var c in changes.Where(c => c.Kind == "save").ToList())
            if (Setup(c, reads) is { } rule)
            {
                setups.Add((c, rule));
                changes.Remove(c);
            }
        // D309: a flow whose changes are runs of shared parts is still charted, one box per part, so its own conditions stay in sight.
        if (changes.Count == 0)
        {
            var runs = changes.ToList();
            followOns = FollowOnRows(changes, setups, sharedByStep, unDrawn);
            listed = [.. shared, .. setups.Select(s => new ChartRow($"{s.Save.Text} when {string.Join(" and ", s.Save.Own.Select(p => $"`{p.Code}`"))} ({s.Rule})", s.Save.At, [.. s.Save.Steps], [], 0)),
                .. runs.Select(r => new ChartRow(r.Text, r.At, [.. r.Steps.Distinct().Order()], [], 0))];
            var parts = new List<string>();
            if (shared.Count > 0) parts.Add("only changes made in shared code");
            if (setups.Count > 0) parts.Add("only setup steps");
            if (runs.Count > 0) parts.Add("only work shown in shared parts");
            why = parts.Count > 0 ? string.Join(" and ", parts)
                : clickPlaces is not null && HasAny(steps, links, reader, cut) ? "its changes are drawn in its clicks' charts" : "only reads or shows";
            return null;
        }

        var chart = new FlowChart { Flow = id, Kind = kind, Title = PageSummary.StartText(flow), Counts = stepCounts };
        chart.SharedChanges.AddRange(shared);
        chart.OnlyUnknowns = changes.All(c => c.Kind == "unknown");
        changes.Sort((x, y) => x.Order.CompareTo(y.Order));
        chart.FollowOns.AddRange(FollowOnRows(changes, setups, sharedByStep, unDrawn));

        // Field checks as one diamond; their rows are the table's (D279). Checks on a method (guards) aren't drawn: their stops are exits.
        var fieldGroups = new List<(string Field, string Text, string At, int? Step)>();
        foreach (var g in PageSummary.CheckGroups(flow))
        {
            var mineChecks = g.Checks.Where(c => Mine($"check|{(string?)c["at"]}")).ToList();
            if (mineChecks.Count == 0) continue;
            if (mineChecks.All(c => ((string?)c["property"] ?? "").Contains("|M:", StringComparison.Ordinal) || (string?)c["rule"] == "guard")) continue;
            var checkSteps = mineChecks.Select(c => CheckStep(c, steps)).OfType<int>().ToList();
            fieldGroups.Add((g.Field, g.Text, (string?)mineChecks[0]["at"] ?? "", checkSteps.Count == 0 ? null : checkSteps.Min()));
        }
        var all = new List<Element>(changes);
        if (fieldGroups.Count > 0)
            all.Add(new Element { Kind = "checks", Text = "All checks pass?", At = "", Path = [], Order = (int.MinValue, 0, 0) });

        // Each exit: an outside service's failure, a guard shared with many flows, or drawn (one "Refused" end; redirects their own).
        var exitKeys = new Dictionary<string, Element>(O);
        var serviceKeys = new Dictionary<string, int>(O);
        var guardKeys = new Dictionary<string, int>(O);
        var drawnExits = new List<Element>();
        foreach (var e in exits)
        {
            var written = FlowReader.Conditions(e["conditions"] as JsonArray, true);
            var own = Decide(written) is { Count: > 0 } decided ? decided : written;   // an earlier exit's guard ("not: user == null") is that exit, drawn already
            var what = (string)e["what"]! + (e["unsure"] is not null ? " ❓" : "");
            var at = (string?)e["at"] ?? "";
            var step = StopStep(e, steps);
            var stop = new ChartStop(step, at);
            var key = $"{what}\u0001{string.Join("\u0002", own.Select(c => $"{c.Code}|{c.Kind}|{c.Case}"))}";
            var when = own.Count == 0 ? "" : $" when {string.Join(" and ", own.Select(CondText))}";
            if ((own.Any(c => c.Kind == "catch") || own.Any(c => HttpError.IsMatch(c.Code))) && step is { } sn && FirstOutside(steps, children, sn) is { } call)
            {
                var service = FileStem((string?)call["at"] ?? "");
                if (!chart.ServiceFailures.TryGetValue(service, out var rows)) chart.ServiceFailures[service] = rows = [];
                if (serviceKeys.TryGetValue($"{service}\u0001{key}", out var i)) rows[i] = rows[i] with { Stops = [.. rows[i].Stops, stop] };
                else { serviceKeys[$"{service}\u0001{key}"] = rows.Count; rows.Add(new ChartRow($"{what}{when}", at, step is { } s1 ? [s1] : [], [stop])); }
                continue;
            }
            var count = rules.Counts.Exit(at);
            if (count >= rules.Hub)
            {
                if (guardKeys.TryGetValue(key, out var i)) chart.SharedGuards[i] = chart.SharedGuards[i] with { Stops = [.. chart.SharedGuards[i].Stops, stop] };
                else { guardKeys[key] = chart.SharedGuards.Count; chart.SharedGuards.Add(new ChartRow($"{what}{when}", at, step is { } s2 ? [s2] : [], [stop], count)); }
                continue;
            }
            if (exitKeys.TryGetValue(key, out var same)) { same.Stops.Add(stop); if (step is { } s3 && !same.Steps.Contains(s3)) same.Steps.Add(s3); continue; }
            var redirect = (string?)e["kind"] == "Redirect";
            // D305(2): where the exit sits in its method's code: after the steps written above it there (and everything under them).
            var after = (int?)e["step"] is { } parent ? ExitAfter(steps, children, parent, at) : null;
            var x = new Element
            {
                Kind = "exit", Text = redirect ? SendsTo(what) : what, At = at, Path = step is { } es ? Decide(FlowReader.PathConditions(steps, es, true)) : [], Own = own,
                Order = after is { } af ? (af, 3, seq++) : (step ?? int.MaxValue, 0, seq++), Redirect = redirect, After = after,
                Kin = (string?)e["kind"] == "throw" ? $"{what}\u0001{ClassOf((string?)e["point"])}" : null,
            };
            if (step is { } s4) x.Steps.Add(s4);
            x.Stops.Add(stop);
            exitKeys[key] = x;
            drawnExits.Add(x);
            all.Add(x);
        }

        // D297: boxes that would read the same on one chart each add their code line.
        foreach (var same in all.Where(e => e.Kind is not ("exit" or "checks")).GroupBy(e => e.Box, O).Where(x => x.Count() > 1))
            foreach (var e in same) e.WithLine = true;
        var decisions = Draw(chart, [.. all.OrderBy(e => e.Order)], condStep);
        Scenarios(chart, changes, decisions, fieldGroups, drawnExits, setups);
        return chart;
    }

    /// <summary>
    /// What runs afterwards on its own (D273, D293): per change with follow-ons, drawn or not (a setup step, a send in shared code,
    /// a message with no change step of its own), the flows it sets off.
    /// </summary>
    static List<ChartRow> FollowOnRows(List<Element> changes, List<(Element Save, string Rule)> setups, Dictionary<int, Element> shared, List<Element> unDrawn)
    {
        var rows = new List<ChartRow>();
        var seen = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        foreach (var c in changes.Concat(setups.Select(x => x.Save)).Concat(shared.Values).Concat(unDrawn).OrderBy(c => c.Order))
            if (c.FollowOns.Count > 0 && seen.Add(c))
                rows.Add(new ChartRow($"after {c.Text}{Tag(c.Steps, c.At)}: {string.Join(", ", c.FollowOns.Select(f => $"`{f}`"))}", c.At, [.. c.Steps], [])
                {
                    Lead = $"after {c.Text}{Tag(c.Steps, c.At)}", Targets = [.. c.FollowOns],
                });
        return rows;
    }

    /// <summary>
    /// D293, D304(6): the links whose receivers run afterwards: the flow's own <c>message</c> and <c>runs-later</c> links, then the
    /// <c>message</c> links of each shared flow it continues into (and those it continues into), at the step that continues there.
    /// </summary>
    internal static List<JsonObject> ReceiverLinks(FlowReader reader, IEnumerable<JsonObject> links, SharedCut? cut = null)
    {
        var all = links.ToList();
        var list = all.Where(l => (string?)l["kind"] is "message" or "runs-later").ToList();
        var own = list.Select(l => $"{(string?)l["at"]}|{(string?)l["flow"] ?? (string?)l["to"]}").ToHashSet(O);
        // D306: a heavy shared flow's receivers are listed in its own part.
        foreach (var c in all.Where(l => (string?)l["kind"] == "continues" && (string?)l["flow"] is { } t && cut?.Heavy.Contains(t) != true))
            foreach (var m in reader.SharedMessages((string)c["flow"]!, cut))
                if (own.Add($"{(string?)m["at"]}|{(string?)m["flow"] ?? (string?)m["to"]}|{(int?)c["step"]}"))
                {
                    var copy = (JsonObject)m.DeepClone();
                    copy["step"] = (int?)c["step"];
                    list.Add(copy);
                }
        return list;
    }

    /// <summary>D300: a flow's handled errors, one row per error and the catch that takes it, every place it's thrown listed.</summary>
    static List<ChartRow> HandledRows(JsonObject flow, HashSet<string>? clickPlaces)
    {
        var steps = FlowReader.Steps(flow);
        var rows = new List<(string What, string Caught, string? Returns, List<(int? Step, string At)> Places)>();
        foreach (var e in (flow["handled"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var at = (string?)e["at"] ?? "";
            if (clickPlaces?.Contains($"handled|{at}") == true) continue;
            var what = (string)e["what"]!;
            if (what.StartsWith("throws ", StringComparison.Ordinal)) what = what["throws ".Length..];
            var caught = (string?)e["caughtBy"] ?? "";
            var place = (StopStep(e, steps), at);
            var row = rows.FindIndex(r => r.What == what && r.Caught == caught);
            if (row < 0) rows.Add((what, caught, (string?)e["catchReturns"], [place]));
            else if (!rows[row].Places.Contains(place)) rows[row].Places.Add(place);
        }
        // D304(7): say only what is known. A catch that itself ends the request (Kavita's `catch (KavitaException) { return BadRequest(…); }`)
        // names its stop; one that returns, or falls out to a return right after its try, names what it returns; else just the catch.
        var stopsIn = new Dictionary<string, string>(O);
        foreach (var e in (flow["exits"] as JsonArray ?? []).OfType<JsonObject>())
            foreach (var c in (e["conditions"] as JsonArray ?? []).OfType<JsonObject>().Where(c => (string?)c["kind"] == "catch"))
                stopsIn.TryAdd((string?)c["at"] ?? "", $"{(string)e["what"]!} (`{(string?)e["at"]}`)");
        return [.. rows.Select(r => new ChartRow(
            $"handled: {r.What} at {string.Join(", ", r.Places.Select(p => p.Step is { } n ? $"`{p.At}` (step {n})" : $"`{p.At}`"))}, caught by `{r.Caught}`{(stopsIn.TryGetValue(r.Caught, out var then) ? $", which then {then}" : r.Returns is { Length: > 0 } ret ? $", which then returns {ret}" : "")}",
            r.Places[0].At, [.. r.Places.Select(p => p.Step).OfType<int>()], []))];
    }

    /// <summary>A change a repeated call carries (D296): the repeat's step, the step it's "as in", the change's own step (null when it is a save in shared code), its kind, words, line, label and the conditions between.</summary>
    internal sealed record CarriedChange(int Step, int Of, int? From, string Kind, string Text, string At, string Label, List<SummaryCondition> Inside);

    /// <summary>
    /// D296: for each step marked "as in step N" (<c>sameAs</c>), the changes under step N (its own change steps, and the saves in
    /// shared code it continues into), with the conditions met between step N and each change; a repeat inside step N's walk
    /// carries its own step's too. Unknowns aren't carried: they're listed where they are.
    /// </summary>
    internal static List<CarriedChange> Carried(FlowReader reader, Dictionary<int, JsonObject> steps, Dictionary<int, List<int>> children, List<JsonObject> links, SharedCut? cut = null)
    {
        var list = new List<CarriedChange>();
        var linksAt = links.Where(l => (string?)l["kind"] == "continues" && l["step"] is not null && (string?)l["flow"] is not null).ToLookup(l => (int)l["step"]!);
        foreach (var st in steps.Values.Where(s => s["sameAs"] is not null).OrderBy(s => (int)s["n"]!))
        {
            var n = (int)st["n"]!;
            var of = (int)st["sameAs"]!;
            var seen = new HashSet<int>();
            var todo = new Stack<(int Step, List<SummaryCondition> Inside)>();
            todo.Push((of, []));
            var found = new List<CarriedChange>();
            while (todo.Count > 0)
            {
                var (k, inside) = todo.Pop();
                if (!seen.Add(k) || !steps.TryGetValue(k, out var at)) continue;
                var own = k == of ? inside : PageSummary.Merge(inside, FlowReader.Conditions(at["conditions"] as JsonArray, true));
                if (k != of && Change(at) is { } change)
                    found.Add(new CarriedChange(n, of, k, change, (string)at["what"]!, (string?)at["at"] ?? "", (string?)at["label"] == "guessed" ? "guessed" : "certain", own));
                foreach (var l in linksAt[k])
                    foreach (var save in SharedOf(reader, l, true, cut))
                        found.Add(new CarriedChange(n, k, null, save.Kind == "shared" ? "shared" : SharedKind(save.Text), save.Text, save.At, save.Label, PageSummary.Merge(own, save.Conditions)));
                if (at["sameAs"] is { } again && (int)again != of) todo.Push(((int)again, own));
                if (children.TryGetValue(k, out var cs)) foreach (var c in cs.OrderDescending()) todo.Push((c, own));
            }
            list.AddRange(found.OrderBy(f => f.From ?? f.Of).ThenBy(f => f.At, O));
        }
        return list;
    }

    /// <summary>The change kind of a shared-code change by its words (D292, D294 widened shared saves to pushes and files).</summary>
    internal static string SharedKind(string text) => text.StartsWith("pushes ", StringComparison.Ordinal) ? "push"
        : text.StartsWith("writes a file", StringComparison.Ordinal) || text.StartsWith("deletes a file", StringComparison.Ordinal) ? "file" : "save";

    /// <summary>
    /// D287: a one-line flow's stops, by the rules a charted click's use: one at a line the hub threshold of start flows or more
    /// have is a shared guard; the rest are its own, the same text and condition sharing one row with every stop listed.
    /// </summary>
    static void OneLineStops(JsonObject flow, Rules rules, HashSet<string>? clickPlaces, DisplayOnly d)
    {
        var all = (flow["exits"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var steps = FlowReader.Steps(flow);
        var exitGuards = all.SelectMany(e => (e["conditions"] as JsonArray ?? []).OfType<JsonObject>()).Where(c => (string?)c["kind"] != "unless")
            .Select(c => $"{(string?)c["at"]}|{(string?)c["code"]}").ToHashSet(O);
        var own = new Dictionary<string, int>(O);
        var shared = new Dictionary<string, int>(O);
        var kins = new List<(string? Kin, List<SummaryCondition> Conds)>();
        foreach (var e in all.Where(e => clickPlaces?.Contains($"exit|{(string?)e["at"]}") != true))
        {
            var written = FlowReader.Conditions(e["conditions"] as JsonArray, true);
            var decided = written.Where(c => !WhenKinds.Contains(c.Kind) && !(c.Kind == "unless" && exitGuards.Contains($"{c.At}|{c.Code}"))).ToList();
            var conds = decided.Count > 0 ? decided : written;
            var what = ((string?)e["kind"] == "Redirect" ? SendsTo((string)e["what"]!) : (string)e["what"]!) + (e["unsure"] is not null ? " ❓" : "");
            var at = (string?)e["at"] ?? "";
            var step = StopStep(e, steps);
            var stop = new ChartStop(step, at);
            var key = $"{what}\u0001{string.Join("\u0002", conds.Select(c => $"{c.Code}|{c.Kind}|{c.Case}"))}";
            var count = rules.Counts.Exit(at);
            var (rows, keys) = count >= rules.Hub ? (d.SharedGuards, shared) : (d.Stops, own);
            if (keys.TryGetValue(key, out var i)) rows[i] = rows[i] with { Stops = [.. rows[i].Stops, stop] };
            else
            {
                keys[key] = rows.Count;
                if (rows == d.Stops) kins.Add(((string?)e["kind"] == "throw" ? $"{what}\u0001{ClassOf((string?)e["point"])}" : null, conds));
                rows.Add(new ChartRow($"{what}{(conds.Count == 0 ? "" : $" when {string.Join(" and ", conds.Select(CondText))}")}", at, step is { } n ? [n] : [], [stop], count));
            }
        }
        // D311(3): stops of one exception kind thrown in one class are one sub-bullet listing their lines, each with its conditions.
        var merged = new List<ChartRow>();
        var done = new HashSet<string>(O);
        for (var r = 0; r < d.Stops.Count; r++)
        {
            var k = kins[r].Kin;
            var same = k is null ? [] : Enumerable.Range(0, d.Stops.Count).Where(j => kins[j].Kin == k).ToList();
            if (k is null || same.Count < 2) { merged.Add(d.Stops[r]); continue; }
            if (!done.Add(k)) continue;
            var places = Places([.. same.Select(j => (d.Stops[j].Stops, (IReadOnlyList<SummaryCondition>)kins[j].Conds))]);
            var what = k[..k.IndexOf('\u0001')];
            merged.Add(new ChartRow($"{what} ({Plural(same.Sum(j => d.Stops[j].Stops.Count), "place")} in {k[(k.IndexOf('\u0001') + 1)..]}): {places}", d.Stops[r].At,
                [.. same.SelectMany(j => d.Stops[j].Steps)], [.. same.SelectMany(j => d.Stops[j].Stops)]));
        }
        d.Stops.Clear();
        d.Stops.AddRange(merged);
    }

    /// <summary>
    /// A stop's step (D284): the flow file's, or when it gives none, the first step on the same line (D288; a redirect stop often has
    /// no step while the call it makes is one, as nopCommerce's billing address at <c>CheckoutController.cs:633</c> is step 107).
    /// </summary>
    internal static int? StopStep(JsonObject exit, Dictionary<int, JsonObject> steps)
    {
        if ((int?)exit["step"] is { } n) return n;
        var at = (string?)exit["at"];
        if (string.IsNullOrEmpty(at)) return null;
        var same = steps.Values.Where(s => (string?)s["at"] == at).Select(s => (int)s["n"]!).ToList();
        return same.Count == 0 ? null : same.Min();
    }

    /// <summary>
    /// D288: the step a field check runs at when the flow calls its validator mid-flow: the first step calling a <c>Validate…</c> method of
    /// the type the check's file is named after (or its interface, <c>I</c> + name). Null for a check bound to the request, run before the steps.
    /// </summary>
    static int? CheckStep(JsonObject check, Dictionary<int, JsonObject> steps)
    {
        var stem = FileStem((string?)check["at"] ?? "");
        if (stem == "") return null;
        int? best = null;
        foreach (var st in steps.Values)
        {
            var point = (string?)st["point"] ?? "";
            var m = point.IndexOf("|M:", StringComparison.Ordinal);
            if (m < 0) continue;
            var member = point[(m + 3)..];
            if (member.IndexOf('(') is var paren and >= 0) member = member[..paren];
            var parts = member.Split('.');
            if (parts.Length < 2) continue;
            var type = parts[^2];
            if (type.IndexOf('`') is var tick and >= 0) type = type[..tick];
            if ((type == stem || type == $"I{stem}") && parts[^1].StartsWith("Validate", StringComparison.Ordinal) && (best is null || (int)st["n"]! < best))
                best = (int)st["n"]!;
        }
        return best;
    }

    /// <summary>A step's words without an empty "()" at the end ("runs later, in the background ()", D288).</summary>
    static string Clean(string what) => what.EndsWith(" ()", StringComparison.Ordinal) ? what[..^3] : what;

    /// <summary>"sends the user elsewhere (X)" read as "sends the user to X".</summary>
    static string SendsTo(string what) =>
        what.StartsWith("sends the user elsewhere (", StringComparison.Ordinal) && what.EndsWith(')') ? $"sends the user to {what["sends the user elsewhere (".Length..^1]}" : what;

    static bool HasAny(Dictionary<int, JsonObject> steps, List<JsonObject> links, FlowReader reader, SharedCut? cut) =>
        steps.Values.Any(s => Change(s) is not null) || links.Any(l => (string?)l["kind"] == "runs-later") ||
        links.Any(l => (string?)l["kind"] == "continues" && (string?)l["flow"] is not null && SharedOf(reader, l, false, cut).Count > 0);

    /// <summary>
    /// What a <c>continues</c> link brings to the flow that has it: the saves (and pushes, files, plugin calls) of the shared flow it
    /// continues into; D306: for a heavy shared flow, one line that runs it, at the link's line.
    /// </summary>
    internal static IReadOnlyList<SharedSave> SharedOf(FlowReader reader, JsonObject link, bool outerFirst, SharedCut? cut)
    {
        var target = (string)link["flow"]!;
        if (cut is not null && cut.Heavy.Contains(target)) return [new SharedSave(cut.Box(target), (string?)link["at"] ?? "", "", "certain", null, [], "shared")];
        return reader.SharedSaves(target, outerFirst, cut);
    }

    /// <summary>The first outside call (by step number) at or under step <paramref name="n"/>: the call the exit's method makes.</summary>
    static JsonObject? FirstOutside(Dictionary<int, JsonObject> steps, Dictionary<int, List<int>> children, int n)
    {
        var found = new List<int>();
        var seen = new HashSet<int>();
        var stack = new Stack<int>([n]);
        while (stack.Count > 0)
        {
            var k = stack.Pop();
            if (!seen.Add(k) || !steps.TryGetValue(k, out var st)) continue;
            if ((string?)st["kind"] == "outside-call") found.Add(k);
            if (children.TryGetValue(k, out var cs)) foreach (var c in cs) stack.Push(c);
        }
        return found.Count == 0 ? null : steps[found.Min()];
    }

    /// <summary>Every read in the flow and the shared code it continues into: its table and code.</summary>
    static List<(string Table, string Code)> Reads(FlowReader reader, JsonObject flow)
    {
        var list = new List<(string, string)>();
        var seen = new HashSet<string>(O);
        var queue = new Queue<JsonObject>([flow]);
        while (queue.Count > 0)
        {
            var f = queue.Dequeue();
            foreach (var s in FlowReader.Steps(f).Values.OrderBy(s => (int)s["n"]!))
                if ((string?)s["kind"] == "database" && TableOf((string)s["what"]!) is { } t && ((string)s["what"]!).StartsWith("reads", StringComparison.Ordinal))
                    list.Add((t, (string?)s["code"] ?? ""));
            foreach (var l in (f["links"] as JsonArray ?? []).OfType<JsonObject>())
                if ((string?)l["kind"] == "continues" && (string?)l["flow"] is { } t && seen.Add(t) && reader.Load(t) is { } next) queue.Enqueue(next);
        }
        return list;
    }

    /// <summary>
    /// The setup-step test (D273), conservative: a save with at least one deciding condition in the code that saves, every one of
    /// them an <c>if</c> (or a <c>??</c> null test) of the form <c>v == null</c>, <c>v is null</c>, <c>!v.Any()</c>,
    /// <c>v.Count == 0</c> or <c>!v</c>, where <c>v</c> is assigned by a read of the same table in this flow or the shared code it continues into.
    /// </summary>
    static string? Setup(Element save, List<(string Table, string Code)> reads)
    {
        if (save.Table is null || save.Own.Count == 0) return null;
        var names = new List<string>();
        foreach (var c in save.Own)
        {
            if (c.Kind is not ("if" or "null")) return null;
            var v = Empty.Select(r => r.Match(c.Code.Trim())).FirstOrDefault(m => m.Success)?.Groups["v"].Value;
            if (v is null) return null;
            if (!reads.Any(r => r.Table == save.Table && Regex.IsMatch(r.Code, $@"(^|[^\w.]){Regex.Escape(v)}\s*=[^=>]", RegexOptions.CultureInvariant))) return null;
            names.Add(v);
        }
        return $"nothing saved yet: {string.Join(", ", names.Distinct(O))} read from {save.Table}";
    }

    // D311(4): the condition shapes code phrases, a short fixed list; anything else stays raw.
    const string Chain = @"[A-Za-z_$][\w$]*(?:\??\.[A-Za-z_$][\w$]*)*";
    static readonly Regex IsEmptyCall = new($@"^(?:string|String)\.IsNullOr(?:Empty|WhiteSpace)\(\s*(?<x>{Chain})\s*\)$", RegexOptions.CultureInvariant);
    static readonly Regex IsNull = new($@"^(?<x>{Chain})\s*(?:===?\s*(?:null|undefined)|is\s+null)$", RegexOptions.CultureInvariant);
    static readonly Regex NotNull = new($@"^(?<x>{Chain})\s*(?:!==?\s*(?:null|undefined)|is\s+not\s+null)$", RegexOptions.CultureInvariant);
    static readonly Regex AnyCall = new($@"^(?<x>{Chain})\.Any\(\)$", RegexOptions.CultureInvariant);
    static readonly Regex CountZero = new($@"^(?<x>{Chain})\.(?:Count|Length|length)(?:\(\))?\s*===?\s*0$", RegexOptions.CultureInvariant);
    static readonly Regex CountMore = new($@"^(?<x>{Chain})\.(?:Count|Length|length)(?:\(\))?\s*(?:>|!==?)\s*0$", RegexOptions.CultureInvariant);
    static readonly Regex Flag = new($@"^{Chain}\??\.(?<m>(?:Is|Has|Can|Should|Allow|Enable)[A-Z]\w*)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// D311(4): a common condition shape said in words, by code (deterministic): <c>string.IsNullOrEmpty(dto.Title)</c> → "Title is
    /// empty", <c>x == null</c> → "no x", <c>x != null</c> → "has x", <c>!xs.Any()</c> or <c>xs.Count == 0</c> → "xs is empty",
    /// <c>xs.Any()</c> → "xs has items", a flag member <c>x.IsLocked</c> → "IsLocked"; with <paramref name="negate"/>, its other side
    /// ("Title is not empty", "has x", "not IsLocked"). A name is its last member. Null for any other shape (<c>a == b</c> stays raw).
    /// </summary>
    internal static string? Phrase(string code, bool negate = false)
    {
        var c = code.Trim();
        if (Negated(c) is { } plain) { c = plain; negate = !negate; }
        else if (c.Length > 2 && c[0] == '(' && Negated("!" + c) is { } inner && inner != c) c = inner;
        static string Name(Match m) => m.Groups["x"].Value is var x && x.LastIndexOf('.') is var dot and >= 0 ? x[(dot + 1)..] : m.Groups["x"].Value;
        if (IsEmptyCall.Match(c) is { Success: true } e) return negate ? $"{Name(e)} is not empty" : $"{Name(e)} is empty";
        if (IsNull.Match(c) is { Success: true } n) return negate ? $"has {Name(n)}" : $"no {Name(n)}";
        if (NotNull.Match(c) is { Success: true } h) return negate ? $"no {Name(h)}" : $"has {Name(h)}";
        if (CountZero.Match(c) is { Success: true } z) return negate ? $"{Name(z)} has items" : $"{Name(z)} is empty";
        if (AnyCall.Match(c) is { Success: true } a) return negate ? $"{Name(a)} is empty" : $"{Name(a)} has items";
        if (CountMore.Match(c) is { Success: true } m) return negate ? $"{Name(m)} is empty" : $"{Name(m)} has items";
        if (Flag.Match(c) is { Success: true } f) return negate ? $"not {f.Groups["m"].Value}" : f.Groups["m"].Value;
        return null;
    }

    /// <summary>D311(4): the phrase for when a condition holds (an if's code; an else's or guard's other side), or null.</summary>
    static string? Holds(SummaryCondition c) => c.Kind switch
    {
        "else" or "unless" => Phrase(c.Code, true),
        "if" => Phrase(c.Code),
        _ => null,
    };

    /// <summary>D311(4): a condition as a list line says it: the phrase with its raw code after it, or as a diamond reads it.</summary>
    static string CondText(SummaryCondition c) => Holds(c) is { } p ? $"{p} (`{c.Code.Trim()}`)" : Cond(c);

    /// <summary>A condition as a diamond reads it: its code, said the way round its kind needs (D311(4): phrased when its shape is known).</summary>
    static string Cond(SummaryCondition c) => Holds(c) ?? c.Kind switch
    {
        "else" or "unless" => Not(c.Code),
        "catch" => $"on error {c.Code}",
        "loop" => $"for each: {c.Code}",
        "case" => $"case {c.Case ?? ""}: {c.Code}".Replace("case : ", "case: ", StringComparison.Ordinal),
        "null" => $"{c.Code} is null",
        "cache-miss" => $"nothing cached: {c.Code}",
        "set" => $"{c.Code} is set",
        _ => c.Code,
    };

    /// <summary>
    /// D310: a diamond reads positively, as D297's rows do, never "not: x". An else or guard (<c>unless</c>) on a condition that isn't a
    /// plain <c>!x</c> is drawn as the condition itself with its yes and no lines swapped: the guard <c>if (string.IsNullOrEmpty(t)) throw</c>
    /// puts the save under "string.IsNullOrEmpty(t)? no", not "not: string.IsNullOrEmpty(t)? yes".
    /// </summary>
    static bool Flipped(SummaryCondition c) => c.Kind is "else" or "unless" && Negated(c.Code.Trim()) is null;

    /// <summary>D310's flipped diamond: the condition's own code, D311(4) phrased when its shape is known.</summary>
    static string Said(string code) => Phrase(code) ?? code.Trim();

    /// <summary>D310: one of several conditions on a diamond, positively: a flipped one as "(x) is false".</summary>
    static string Positively(SummaryCondition c)
    {
        if (!Flipped(c) || Holds(c) is not null) return Cond(c);
        var code = c.Code.Trim();
        return $"{(code.Contains(' ', StringComparison.Ordinal) ? $"({code})" : code)} is false";
    }

    /// <summary>The other side of a diamond, for the scenario table: "not: x" for an if, "x" for an else or unless, "no items in x" for a loop.</summary>
    static string Other(SummaryCondition c) => c.Kind switch
    {
        "else" or "unless" => c.Code,
        "loop" => $"no items in: {c.Code}",
        "cache-miss" => $"already cached: {c.Code}",
        "catch" => $"no error {c.Code}",
        "null" => $"{c.Code} is not null",
        "set" => $"{c.Code} is not set",
        _ => Not(c.Code),
    };

    /// <summary>D297: a diamond's "no" side, said positively: "`x` is false" for an if, "`x` is true" for an else or guard, "`xs` has no items" for a loop.</summary>
    static string Positive(SummaryCondition c) => c.Kind switch
    {
        // A negated simple condition plainly (D288's nit): "`!x` is false" reads as "`x` is true".
        "if" when Negated(c.Code.Trim()) is { } plain => $"`{plain}` is true",
        "else" or "unless" when Negated(c.Code.Trim()) is { } plain => $"`{plain}` is false",
        "else" or "unless" => $"`{c.Code}` is true",
        "loop" => $"`{c.Code}` has no items",
        "cache-miss" => $"`{c.Code}` is already cached",
        "catch" => $"no error is caught (`{c.Code}`)",
        "null" => $"`{c.Code}` is not null",
        "set" => $"`{c.Code}` is not set",
        "case" => $"`{c.Code}` is not {(string.IsNullOrEmpty(c.Case) ? "this case" : c.Case)}",
        _ => $"`{c.Code}` is false",
    };

    /// <summary>D311(4): a diamond's "no" side in words when its shape is known (the other side of <see cref="Holds"/>), or null.</summary>
    static string? Fails(SummaryCondition c) => c.Kind switch
    {
        "else" or "unless" => Phrase(c.Code),
        "if" => Phrase(c.Code, true),
        _ => null,
    };

    /// <summary>D297: when what a diamond's "no" side skips does run: its "yes" side, said positively (D311(4): in words when known).</summary>
    static string OnlyWhen(SummaryCondition c) => Holds(c) is { } p ? $"when {p}" : c.Kind switch
    {
        "else" or "unless" => "when it is false",
        "loop" => "for each item",
        "cache-miss" => "when nothing is cached",
        "catch" => "on that error",
        "null" => "when it is null",
        "set" => "when it is set",
        "case" => "in that case",
        _ => "when it is true",
    };

    /// <summary>
    /// A condition's other side (D288): a negated simple condition plainly (<c>!hasLicense</c> → <c>hasLicense</c>, <c>!(a &amp;&amp; b)</c> →
    /// <c>a &amp;&amp; b</c>); a combined one starting with <c>!</c> as "not: (…)" around the whole; anything else "not: x".
    /// </summary>
    internal static string Not(string code)
    {
        var c = code.Trim();
        if (Negated(c) is { } plain) return plain;
        return c.StartsWith('!') ? $"not: ({c})" : $"not: {c}";
    }

    /// <summary>The operand of <c>!x</c> when <c>x</c> is one operand: a name, member access or call (no space or operator outside brackets), or a fully bracketed group's inner text.</summary>
    static string? Negated(string c)
    {
        if (c.Length < 2 || c[0] != '!' || c[1] == '=') return null;
        var rest = c[1..].Trim();
        if (rest == "" || rest[0] == '!') return null;
        int depth = 0, firstClose = -1;
        for (var i = 0; i < rest.Length; i++)
        {
            var ch = rest[i];
            if (ch is '(' or '[') depth++;
            else if (ch is ')' or ']')
            {
                if (--depth < 0) return null;
                if (depth == 0 && firstClose < 0) firstClose = i;
            }
            else if (depth == 0 && !(char.IsLetterOrDigit(ch) || ch is '_' or '.' or '$' or '?' or '@')) return null;
        }
        if (depth != 0) return null;
        return rest[0] == '(' && firstClose == rest.Length - 1 ? rest[1..^1].Trim() : rest;
    }

    static string Mark(string label) => label switch { "guessed" => " 🟡", "unknown" => " ❓", _ => "" };

    /// <summary>A diamond's two lines: a loop's run its body for each item, then go on ("after the loop"), so neither reads as "skip".</summary>
    static (string Yes, string No) Sides(SummaryCondition c) => c.Kind == "loop" ? ("each", "after the loop") : ("yes", "no");

    /// <summary>A diamond drawn over a run of elements: its condition and the elements under it.</summary>
    sealed record Decision(string Diamond, SummaryCondition Condition, List<Element> Under);

    /// <summary>
    /// D273's chart: elements in flow order; the elements under the same condition (within the same parent) share its one diamond
    /// ("yes" into them, "no" past them), so a loop is drawn once with everything inside it under it (D288). One "Refused" end, redirects their own end, one follow-on box per change, no "Done". Every box but the
    /// start, "Refused" and "All checks pass?" carries its step numbers (D284).
    /// </summary>
    static List<Decision> Draw(FlowChart chart, List<Element> elements, Dictionary<string, int> condStep)
    {
        var g = chart.Chart;
        int nc = 0, nd = 0, nf = 0, ng = 0;
        g.Nodes.Add(new ChartNode("S", "start", chart.Title));
        string? refused = null;
        string Refused()
        {
            if (refused is null) { refused = "R"; g.Nodes.Add(new ChartNode("R", "refused", "Refused")); }
            return refused;
        }
        void Connect(List<(string From, string? Label)> pending, string to)
        {
            foreach (var (from, label) in pending) g.Edges.Add(new ChartEdge(from, to, label, false));
        }
        var boxes = new Dictionary<Element, string>();
        var decisions = new List<Decision>();
        List<(string, string?)> Node(Element e, List<(string, string?)> pending)
        {
            switch (e.Kind)
            {
                case "checks":
                {
                    var d = $"D{++nd}";
                    g.Nodes.Add(new ChartNode(d, "diamond", "All checks pass?"));
                    chart.DiamondKinds["checks"] = chart.DiamondKinds.GetValueOrDefault("checks") + 1;
                    Connect(pending, d);
                    g.Edges.Add(new ChartEdge(d, Refused(), "no", false));
                    return [(d, "yes")];
                }
                case "exit":
                {
                    string end;
                    if (e.Redirect) { end = $"G{++ng}"; g.Nodes.Add(new ChartNode(end, "redirect", e.Text + Tag(e.Steps, e.At))); }
                    else end = Refused();
                    if (e.Own.Count == 0) { Connect(pending, end); return pending; }   // stops whenever reached: the path goes on beside it
                    var d = $"D{++nd}";
                    // D310: read positively; a lone condition said the other way round swaps its lines.
                    var flip = e.Own is [var only] && Flipped(only);
                    g.Nodes.Add(new ChartNode(d, "diamond", (flip ? Said(e.Own[0].Code) : string.Join(" and ", e.Own.Select(Positively))) + "?" + Tag(e.Steps, e.At)));
                    chart.DiamondKinds["exit"] = chart.DiamondKinds.GetValueOrDefault("exit") + 1;
                    Connect(pending, d);
                    g.Edges.Add(new ChartEdge(d, end, flip ? "no" : "yes", false));
                    return [(d, flip ? "yes" : "no")];
                }
                default:
                {
                    var b = $"C{++nc}";
                    g.Nodes.Add(new ChartNode(b, "change", e.Box));
                    chart.Boxes[b] = e.Info;
                    chart.Made.Add((e.Kind, e.Text, e.Service));
                    chart.MadeWhen.Add(e.Path.Count == 0 ? null : (OnlyIf(e.Path[^1]), ShortAt(e.Path[^1].At), e.Path.Count));
                    boxes[e] = b;
                    Connect(pending, b);
                    if (e.FollowOns.Count > 0)
                    {
                        var f = $"F{++nf}";
                        g.Nodes.Add(new ChartNode(f, "follow-on", $"then, on its own: {e.FollowOns.Count} flow{(e.FollowOns.Count == 1 ? "" : "s")}{Tag(e.Steps, e.At)}"));
                        g.Edges.Add(new ChartEdge(b, f, "then", false));
                    }
                    return [(b, null)];
                }
            }
        }
        List<(string, string?)> Block(List<Element> items, int depth, List<(string, string?)> pending)
        {
            var i = 0;
            while (i < items.Count)
            {
                var e = items[i];
                if (e.Path.Count > depth)
                {
                    var c = e.Path[depth];
                    bool Under(Element x) => x.Path.Count > depth && x.Path[depth].Key == c.Key;
                    // Every later element under the same condition joins this run, not just the next ones; the rest keep their order after it.
                    items = [.. items.Take(i), .. items.Skip(i).Where(Under), .. items.Skip(i).Where(x => !Under(x))];
                    var j = i;
                    while (j < items.Count && Under(items[j])) j++;
                    var d = $"D{++nd}";
                    List<int> step = condStep.TryGetValue(c.Key, out var cs) ? [cs] : [.. items[i..j].SelectMany(x => x.Steps).Take(1)];
                    // D310: an else or guard whose condition isn't a plain `!x` shows the condition itself, its lines swapped.
                    var flip = Flipped(c);
                    g.Nodes.Add(new ChartNode(d, "diamond", (flip ? Said(c.Code) : Cond(c)) + "?" + Tag(step, c.At)));
                    chart.DiamondKinds[c.Kind] = chart.DiamondKinds.GetValueOrDefault(c.Kind) + 1;
                    Connect(pending, d);
                    var (yes, no) = flip ? ("no", "yes") : Sides(c);
                    decisions.Add(new Decision(d, c, items[i..j]));
                    var after = Block(items[i..j], depth + 1, [(d, yes)]);
                    pending = [.. after, (d, no)];
                    i = j;
                    continue;
                }
                pending = Node(e, pending);
                i++;
            }
            return pending;
        }
        Block(elements, 0, [("S", null)]);
        // D273: an outside service's failure as one dashed line from its call's box to "Refused".
        foreach (var (service, _) in chart.ServiceFailures)
        {
            var box = boxes.Where(x => x.Key.Service == service).OrderBy(x => x.Key.Order).Select(x => x.Value).FirstOrDefault();
            g.Edges.Add(new ChartEdge(box ?? "S", Refused(), $"{service} fails", true));
        }
        return decisions;
    }

    /// <summary>The changes a stop at <paramref name="step"/> has passed: earlier in flow order, and on no condition the stop isn't under.</summary>
    static List<int> Passed(List<Element> changes, int? step, List<SummaryCondition> path) => PassedUpTo(changes, step is { } n ? n - 1 : null, path);

    /// <summary>
    /// D305(2): the changes a stop passes: at or before step <paramref name="last"/> in walk order (null: any), on no condition the
    /// stop isn't under. Steps are numbered in source order inside each method, so "at or before the exit's last step above it"
    /// is "written before it on its path".
    /// </summary>
    static List<int> PassedUpTo(List<Element> changes, int? last, List<SummaryCondition> path)
    {
        var keys = path.Select(c => c.Key).ToHashSet(O);
        return [.. changes.Where(c => c.Steps.Count > 0 && (last is null || c.Steps.Min() <= last) && c.Path.All(p => keys.Contains(p.Key))).SelectMany(c => c.Steps).Distinct().Order()];
    }

    /// <summary>The changes an exit passes (D305(2)): those written before it on its path, under its own conditions as well as its method's.</summary>
    static List<int> Passed(List<Element> changes, Element exit) =>
        exit.After is { } after ? PassedUpTo(changes, after, [.. exit.Path, .. exit.Own]) : Passed(changes, exit.Steps.Count > 0 ? exit.Steps.Min() : null, exit.Path);

    /// <summary>
    /// D305(2): the last step written before an exit in its method (<paramref name="parent"/>, the step whose code holds it): its
    /// children in the exit's file on lines up to the exit's, each with all its steps. A child in another file (a filter, an
    /// implementation run) can't be placed by its line: it counts only when a child after it in the exit's file does. Null when none
    /// comes before it.
    /// </summary>
    internal static int? ExitAfter(Dictionary<int, JsonObject> steps, Dictionary<int, List<int>> children, int parent, string at)
    {
        static (string File, int Line) Split(string at) =>
            at.LastIndexOf(':') is var c and > 0 && int.TryParse(at[(c + 1)..], out var l) ? (at[..c], l) : (at, 0);
        var (file, line) = Split(at);
        if (line == 0 || !children.TryGetValue(parent, out var kids)) return null;
        int Last(int n) => children.TryGetValue(n, out var k) && k.Count > 0 ? Math.Max(n, k.Max(Last)) : n;
        int? after = null;
        foreach (var k in kids)
        {
            var (f, l) = Split((string?)steps[k]["at"] ?? "");
            if (f != file) continue;   // placed by the next child in the exit's own file
            if (l > line) break;
            after = Last(k);
        }
        return after;
    }

    static string Passes(List<int> steps) => steps.Count == 0 ? "passes no change" : $"passes {StepList(steps)}";

    /// <summary>
    /// D308: a row's steps said by the shorter of the two lists, the main-path steps it passes or the ones it skips, with runs of
    /// consecutive numbers as ranges, so a long flow's rows don't repeat every step.
    /// </summary>
    /// <summary>D308: flows whose main path has at most this many change steps keep D284's full lists.</summary>
    internal const int ShortFrom = 20;

    internal static string PassesShort(List<int> passed, List<int> main)
    {
        if (main.Count <= ShortFrom || passed.Count == 0) return Passes(passed);
        var skipped = main.Where(s => !passed.Contains(s)).ToList();
        if (skipped.Count == 0) return main.Count == 1 ? $"passes {Ranges(passed)}" : "passes every main-path step";
        return passed.Count <= skipped.Count ? $"passes {Ranges(passed)}" : $"passes every main-path step except {Ranges(skipped)}";
    }

    /// <summary>D308: "steps 2, 917–921, 923": runs of consecutive numbers (three or more) as ranges.</summary>
    internal static string Ranges(IEnumerable<int> steps)
    {
        var list = steps.Distinct().Order().ToList();
        var parts = new List<string>();
        for (var i = 0; i < list.Count;)
        {
            var j = i;
            while (j + 1 < list.Count && list[j + 1] == list[j] + 1) j++;
            if (j - i >= 2) parts.Add($"{list[i]}–{list[j]}");
            else for (var k = i; k <= j; k++) parts.Add(list[k].ToString(System.Globalization.CultureInfo.InvariantCulture));
            i = j + 1;
        }
        return $"step{(list.Count == 1 ? "" : "s")} {string.Join(", ", parts)}";
    }

    /// <summary>
    /// The scenario table (D279), in a fixed order: the main path; each deciding condition's other side (D); each field check group
    /// (K, the only place the field checks are listed); each failing service (S); each other stop (X, redirects worded "sends the
    /// user to …"); each setup step (U). Every row names its steps (D284) and every stop its step and line.
    /// </summary>
    static void Scenarios(FlowChart chart, List<Element> changes, List<Decision> decisions, List<(string Field, string Text, string At, int? Step)> fields,
        List<Element> exits, List<(Element Save, string Rule)> setups)
    {
        // D311(4): the phrase first when the shape is known; the raw code and its line stay.
        static string Code(SummaryCondition c) => $"{(Holds(c) is { } p ? $"{p}: " : "")}`{c.Code}` (`{c.At}`)";
        var main = changes.SelectMany(c => c.Steps).Distinct().Order().ToList();
        // D288: some of these run only on one branch, so the main path names the steps that can run, not that do.
        chart.Scenarios.Add(new ScenarioRow("main", "Main path", "every check passes, no stop taken", main.Count == 0 ? "passes no change" : $"can pass {(main.Count <= ShortFrom ? StepList(main) : Ranges(main))}",
            main.Count == 0 ? "ends" : $"ends after step {main[^1]}"));
        // One row per condition, under the id of its (first) diamond on the chart (D288); everything under any of its diamonds is skipped.
        foreach (var group in decisions.GroupBy(x => x.Condition.Key, O))
        {
            var x = group.First();
            if (OnlyAStop(x.Condition, changes, exits)) continue;   // D310: the stop's own row says it
            var under = group.SelectMany(y => y.Under).Distinct().Where(e => e.Kind is not ("exit" or "checks")).ToList();
            if (under.Count == 0) continue;   // a diamond over stops only: the stops' own rows say it
            var skipped = under.SelectMany(e => e.Steps).Distinct().Order().ToList();
            var passed = main.Where(s => !skipped.Contains(s)).ToList();
            // D297: the condition said positively, and what it skips said with when those run.
            // D308: the shorter list names the steps; past 5 skipped changes the ending counts them instead of naming each.
            var full = main.Count <= ShortFrom;
            var steps = full || skipped.Count == 0 ? $"{Passes(passed)}; skips {(skipped.Count == 0 ? "no numbered step" : StepList(skipped))}"
                : passed.Count == 0 ? $"passes no change; skips {Ranges(skipped)}"
                : passed.Count <= skipped.Count ? $"passes {Ranges(passed)}; skips the main path's other {Plural(skipped.Count, "step")}"
                : $"skips {Ranges(skipped)}; passes the main path's other {Plural(passed.Count, "step")}";
            var named = Once(under.Select(e => (e.Box, e.Info)));
            var what = named.Count <= 5 ? string.Join(", ", named)
                : !full && passed.Count <= skipped.Count ? $"the main path's other {under.Count} changes" : $"{under.Count} changes, at the steps it skips";
            chart.Scenarios.Add(new ScenarioRow(x.Diamond, $"When {Fails(x.Condition) ?? Positive(x.Condition)}", $"`{x.Condition.Code}` (`{x.Condition.At}`)", steps,
                $"skips {what}, which run only {OnlyWhen(x.Condition)}"));
        }
        var k = 0;
        foreach (var (field, text, at, step) in fields)
            chart.Scenarios.Add(new ScenarioRow($"K{++k}", $"{field} fails a check", $"{(text.StartsWith("checks ", StringComparison.Ordinal) ? text["checks ".Length..] : text)} (`{at}`)",
                step is { } cn ? $"checked at step {cn}" : "checked before the steps (no step number)", "Refused"));
        var s = 0;
        foreach (var (service, rows) in chart.ServiceFailures)
        {
            var first = rows.SelectMany(r => r.Stops).Select(x => x.Step).Min();
            var call = changes.FirstOrDefault(c => c.Service == service);
            var passed = Passed(changes, first, call?.Path ?? []);
            chart.Scenarios.Add(new ScenarioRow($"S{++s}", $"{service} fails", $"{rows.Count} way{(rows.Count == 1 ? "" : "s")}, under Outside services failing", PassesShort(passed, main), "Refused"));
        }
        var n = 0;
        // D311(3): stops of one exception kind thrown in one class are one row listing their lines, each with its conditions.
        var kin = exits.Where(x => x.Kin is not null && !x.Redirect).GroupBy(x => x.Kin!, O).Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.ToList(), O);
        foreach (var x in exits)
        {
            if (x.Kin is { } kk && kin.TryGetValue(kk, out var same))
            {
                if (same[0] != x) continue;
                var each = same.Select(m => Passed(changes, m)).ToList();
                var union = each.SelectMany(p => p).Distinct().Order().ToList();
                var steps = each.All(p => p.SequenceEqual(each[0])) ? PassesShort(each[0], main) : $"{PassesShort(union, main)}, fewer at some places";
                var places = Places([.. same.Select(m => ((IReadOnlyList<ChartStop>)m.Stops, (IReadOnlyList<SummaryCondition>)m.Own))]);
                chart.Scenarios.Add(new ScenarioRow($"X{++n}", $"Stops: {x.Text} ({Plural(same.Sum(m => m.Stops.Count), "place")} in {kk[(kk.IndexOf('\u0001') + 1)..]})",
                    places, steps, $"Refused: {x.Text}, {string.Join(", ", same.SelectMany(m => m.Stops))}"));
                continue;
            }
            var passed = Passed(changes, x);
            var decides = x.Own.Count == 0 ? "always, when reached" : string.Join("; ", x.Own.Select(Code));
            // D305(2): a redirect written after changes on its path is how the work ends, not a refusal.
            if (x.Redirect && passed.Count > 0)
            {
                var kinds = changes.Where(c => c.Steps.Any(passed.Contains)).Select(c => c.Kind).Distinct().ToList();
                var work = kinds is ["save"] ? "the saves" : "the changes";
                var to = x.Text.StartsWith("sends the user to ", StringComparison.Ordinal) ? $"sending the user to {x.Text["sends the user to ".Length..]}" : x.Text;
                chart.Scenarios.Add(new ScenarioRow($"X{++n}", $"Ends after the work: {x.Text}", decides, PassesShort(passed, main),
                    $"ends by {to}, after {work}{(main.Count <= ShortFrom || passed.Count <= 5 ? $" ({StepList(passed)})" : "")}; {string.Join(", ", x.Stops)}"));
                continue;
            }
            chart.Scenarios.Add(new ScenarioRow($"X{++n}", x.Redirect ? "Sent elsewhere" : $"Stops: {x.Text}", decides, PassesShort(passed, main),
                $"{(x.Redirect ? x.Text : $"Refused: {x.Text}")}, {string.Join(", ", x.Stops)}"));
        }
        var u = 0;
        foreach (var (save, rule) in setups)
            chart.Scenarios.Add(new ScenarioRow($"U{++u}", $"Setup: {save.Text}{Mark(save.Label)}", $"{string.Join(" and ", save.Own.Select(Code))}: {rule}",
                $"also passes {(save.Steps.Count == 0 ? $"`{save.At}`" : StepList(save.Steps))}", "goes on"));
    }

    /// <summary>
    /// D311(3): the places of merged stops, compactly: the conditions every place has once ("when `comparison` matches no case", a
    /// switch's default said so), then each place's lines (the file named once while it stays the same) with its other conditions.
    /// </summary>
    internal static string Places(IReadOnlyList<(IReadOnlyList<ChartStop> Stops, IReadOnlyList<SummaryCondition> Own)> members)
    {
        static string Say(SummaryCondition c) => c.Kind == "case" && c.Case is "_" or "default" ? $"`{c.Code}` matches no case" : CondText(c);
        var said = members.Select(m => m.Own.Select(Say).Distinct(O).ToList()).ToList();
        var common = said[0].Where(x => said.All(o => o.Contains(x))).ToList();
        string? file = null;
        string Lines(IReadOnlyList<ChartStop> stops) => string.Join(", ", stops.Select(t => t.At).Distinct(O).Select(at =>
        {
            var colon = at.LastIndexOf(':');
            var f = colon > 0 ? at[..colon] : at;
            var shown = f == file && colon > 0 ? at[colon..] : ShortAt(at);
            file = f;
            return $"`{shown}`";
        }));
        var each = members.Select((m, i) => (Lines: Lines(m.Stops), Others: said[i].Where(x => !common.Contains(x)).ToList()))
            .Select(x => x.Others.Count == 0 ? x.Lines : $"{x.Lines} (when {string.Join(" and ", x.Others)})");
        return $"{(common.Count > 0 ? $"when {string.Join(" and ", common)}, " : "")}at {string.Join("; ", each)}";
    }

    /// <summary>D311(3): the class a method point belongs to (<c>Asm|M:Ns.ReadingListFilter.&lt;G&gt;$….HasTitle(…)</c> → <c>ReadingListFilter</c>); the point itself when it has no member.</summary>
    internal static string ClassOf(string? point)
    {
        var p = point ?? "";
        if (p.IndexOf("|M:", StringComparison.Ordinal) is var m and >= 0) p = p[(m + 3)..];
        else if (p.IndexOf('|') is var bar and >= 0) return p[(bar + 1)..];
        else return p;
        if (p.IndexOf('(') is var paren and >= 0) p = p[..paren];
        var parts = p.Split('.').Where(x => x != "" && x[0] != '<').ToList();
        if (parts.Count < 2) return parts.FirstOrDefault() ?? "";
        var type = parts[^2];
        return type.IndexOf('`') is var tick and >= 0 ? type[..tick] : type;
    }

    /// <summary>
    /// D310: a condition whose other side is only a stop, when that stop's own row (X) already carries the same condition (its code at
    /// the same line): the scenario row would say again what the stop row says. An else's or a guard's (<c>unless</c>) other side is the
    /// <c>if</c> before it; an if's is its else, so it counts only when a stop carries the else. Any change on that other side (a change
    /// under the same condition the other way round) keeps the row. Kavita's reading-list save: "title empty … skips the save" (the guard
    /// throws) beside the stop row "throws", which lists `string.IsNullOrEmpty(dto.Title)`.
    /// </summary>
    static bool OnlyAStop(SummaryCondition c, List<Element> changes, List<Element> exits)
    {
        bool Same(SummaryCondition o) => o.At == c.At && o.Code.Trim() == c.Code.Trim();
        static bool OtherWay(string a, string b) => (a == "if") != (b == "if");
        if (c.Kind is not ("if" or "else" or "unless")) return false;
        var stops = exits.SelectMany(e => e.Own).Where(Same).ToList();
        if (stops.Count == 0 || c.Kind == "if" && !stops.Any(o => o.Kind is "else" or "unless")) return false;
        return !changes.Any(e => e.Path.Concat(e.Own).Any(p => Same(p) && p.Kind is "if" or "else" or "unless" && OtherWay(p.Kind, c.Kind)));
    }

    // ---- output ---------------------------------------------------------------------------------------------------------

    /// <summary>A label safe inside a quoted Mermaid label, in full (D279): one line, and the characters Mermaid reads escaped as entities.</summary>
    public static string Escape(string text)
    {
        var one = Regex.Replace(text, @"\s+", " ").Trim();
        var sb = new StringBuilder();
        foreach (var ch in one)
            sb.Append(ch switch
            {
                '#' => "#35;", '"' => "#quot;", '<' => "#lt;", '>' => "#gt;", '`' => "#96;", '|' => "#124;",
                '[' => "#91;", ']' => "#93;", '{' => "#123;", '}' => "#125;", '(' => "#40;", ')' => "#41;", ';' => "#59;",
                _ => ch.ToString(),
            });
        return sb.ToString();
    }

    public static string Mermaid(ChartGraph c)
    {
        var sb = new StringBuilder("```mermaid\nflowchart TD\n");
        foreach (var n in c.Nodes)
        {
            var l = Escape(n.Label);
            sb.Append("  ").Append(n.Id).Append(n.Shape switch
            {
                "start" => $"([\"{l}\"])",
                "diamond" => $"{{\"{l}\"}}",
                "refused" => $"[/\"{l}\"/]",
                "redirect" => $">\"{l}\"]",
                "follow-on" => $"[[\"{l}\"]]",
                _ => $"[\"{l}\"]",
            }).Append('\n');
        }
        foreach (var e in c.Edges)
            sb.Append($"  {e.From} {(e.Dashed ? "-.->" : "-->")}{(e.Label is null ? "" : $"|{Escape(e.Label)}|")} {e.To}\n");
        sb.Append("```\n");
        return sb.ToString();
    }

    static string Cell(string text) => Regex.Replace(text, @"\s+", " ").Trim().Replace("|", "\\|", StringComparison.Ordinal);

    static string Line(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    static string Labels(StepCounts c) => $"{Plural(c.Certain + c.Guessed + c.Unknown, "step")}: 🟡 {c.Guessed} guessed, ❓ {c.Unknown} can't tell";

    static string Plural(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";

    static string Flows(int n, int of) => $"in {n} of {of} flows";

    /// <summary>D306: why a shared flow has a part of its own.</summary>
    static string Reached(SharedPart p) =>
        $"run by {p.Reach} of this area's start flow{(p.Reach == 1 ? "" : "s")}, {Plural(p.StepCount, "step")}: {Plural(p.Changes, "change")}, {Plural(p.Stops, "stop")}";

    /// <summary>One changing flow's part of the page (D279), in order: chart (or the line saying it's too large), scenarios, services, follow-ons, shared guards, shared changes.</summary>
    public static string FlowMarkdown(FlowChart c, AreaCharts a, SharedPart? part = null)
    {
        var sb = new StringBuilder();
        sb.Append(part is null ? FlowMarker(c.Flow) : SharedMarker(c.Flow)).Append('\n');
        sb.Append($"## {Line(c.Title)}\n\n`{c.Flow}` · {c.Kind}{(part is null ? "" : $" · {Reached(part)}")} · {Labels(c.Counts)}\n\n");
        if (c.Drawn) sb.Append(Mermaid(c.Chart)).Append('\n');
        else
        {
            sb.Append($"No chart: it would have {c.Chart.Nodes.Count} nodes, over the chart cap of {a.Settings.ChartCap} (`pages.chartCap`). Every path is a row below.\n\n");
            // The boxes it would have drawn, for the reader and for gate 1 (D289).
            sb.Append("**Changes**\n\n");
            foreach (var l in Once(c.Chart.Nodes.Where(n => n.Shape == "change").Select(n => (n.Label, c.Boxes[n.Id])))) sb.Append($"- {Line(l)}\n");
            sb.Append('\n');
        }
        sb.Append("**Scenarios**\n\n| Id | Scenario | What decides it | Steps | Ends with |\n|---|---|---|---|---|\n");
        var checks = c.Scenarios.Where(IsCheckRow).ToList();
        var once = c.SameChecksAs is not null && checks.Count > 0;
        foreach (var r in c.Scenarios)
        {
            if (once && IsCheckRow(r))
            {
                // D314(4): the same field checks as an earlier flow on this page are written once, there; this flow keeps where it checks them.
                if (r != checks[0]) continue;
                var (title, flow) = c.SameChecksAs!.Value;
                var ids = checks.Count == 1 ? checks[0].Id : $"{checks[0].Id}–{checks[^1].Id}";
                var at = checks.Select(x => x.Steps).Distinct(O).ToList();
                sb.Append($"| {ids} | {Cell($"same checks as [{Line(title)}](#{Anchor(title)}) ({ids}), `{flow}`")} | {Cell($"{Plural(checks.Count, "field")}, as written there")} | {Cell(string.Join("; ", at))} | Refused |\n");
                continue;
            }
            sb.Append($"| {r.Id} | {Cell(r.Scenario)} | {Cell(r.Decides)} | {Cell(r.Steps)} | {Cell(r.Ends)} |\n");
        }
        sb.Append('\n');
        if (c.ServiceFailures.Count > 0)
        {
            sb.Append("**Outside services failing**\n\n");
            foreach (var (service, rows) in c.ServiceFailures)
                foreach (var r in rows) sb.Append($"- {service}: {Line(r.Text)}, {string.Join(", ", r.Stops)}\n");
            sb.Append('\n');
        }
        if (c.FollowOns.Count > 0)
        {
            sb.Append("**What runs afterwards on its own**\n\n");
            foreach (var r in c.FollowOns) sb.Append(Receivers(r, a, "- ", "  "));
            sb.Append('\n');
        }
        if (c.Handled.Count > 0)
        {
            sb.Append("**Errors handled on the way (not stops)**\n\n");
            foreach (var r in c.Handled) sb.Append($"- {Line(r.Text)}\n");
            sb.Append('\n');
        }
        if (c.SharedGuards.Count > 0)
        {
            sb.Append("**Guards shared with other flows**\n\n");
            foreach (var r in c.SharedGuards) sb.Append($"- {Line(r.Text)}, {string.Join(", ", r.Stops)}, {Flows(r.Flows, a.AppFlows)}\n");
            sb.Append('\n');
        }
        if (c.SharedChanges.Count > 0)
        {
            sb.Append("**Changes made in shared code**\n\n");
            foreach (var r in c.SharedChanges) sb.Append($"- {Line(r.Text)}{Tag(r.Steps, "")} `{r.At}`, {Flows(r.Flows, a.AppFlows)}\n");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>D314(2): a long receiver list goes on sub-bullets, one flow each.</summary>
    public const int ReceiversInline = 12;

    /// <summary>
    /// D314(2): a "what runs afterwards" row naming every flow it sets off by class and method, its id a link to its file
    /// (`SeriesDeletedHandler.Handle` ([`handler-…`](…))); over <see cref="ReceiversInline"/> flows, one sub-bullet each under the
    /// row, which keeps the send's step. Nothing is cut.
    /// </summary>
    static string Receivers(ChartRow r, AreaCharts a, string bullet, string indent)
    {
        if (r.Lead is null || r.Targets.Count == 0) return $"{bullet}{Line(r.Text)}\n";
        string One(string id)
        {
            var (name, link) = a.Named.GetValueOrDefault(id, ("", null));
            var tag = link is null ? $"`{id}`" : $"[`{id}`]({link})";
            return name == "" ? tag : $"`{name.Replace('`', '\'')}` ({tag})";
        }
        if (r.Targets.Count <= ReceiversInline) return $"{bullet}{Line(r.Lead)}: {string.Join(", ", r.Targets.Select(One))}\n";
        var sb = new StringBuilder($"{bullet}{Line(r.Lead)}, {Plural(r.Targets.Count, "flow")}:\n");
        foreach (var t in r.Targets) sb.Append($"{indent}- {One(t)}\n");
        return sb.ToString();
    }

    /// <summary>D314(4): a heading's anchor as GitHub makes it: lower case, letters, digits, spaces, '-' and '_' kept, spaces to '-'.</summary>
    internal static string Anchor(string heading)
    {
        var sb = new StringBuilder();
        foreach (var ch in Line(heading).ToLowerInvariant())
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_') sb.Append(ch);
            else if (ch == ' ') sb.Append('-');
        return sb.ToString();
    }

    /// <summary>A one-line flow (D271, D281): what it is, why it has no chart, and anything it lists.</summary>
    static string OneLine(DisplayOnly d, AreaCharts a)
    {
        var listed = d.Listed.Select(r => $"{Line(r.Text)}{Tag(r.Steps, "")} `{r.At}`{(r.Flows > 0 ? $", {Flows(r.Flows, a.AppFlows)}" : "")}").ToList();
        var sb = new StringBuilder($"- `{d.Flow}` ({d.Kind}) {Line(d.Shown)}: {d.Why}{(listed.Count > 0 ? $": {string.Join("; ", listed)}" : "")} · {Labels(d.Counts)}\n");
        foreach (var r in d.Stops) sb.Append($"  - {string.Join(", ", r.Stops)}: {Line(r.Text)}\n");
        foreach (var r in d.Handled) sb.Append($"  - {Line(r.Text)}\n");
        foreach (var r in d.FollowOns) sb.Append(Receivers(r, a, "  - runs afterwards on its own: ", "    "));
        if (d.SharedGuards.Count > 0) sb.Append($"  - + {Plural(d.SharedGuards.Count, "shared guard")}\n");
        return sb.ToString();
    }

    /// <summary>The page's body between D233's two lines (D279): contents, each changing flow's part, the one-line flows, what code couldn't see.</summary>
    public static string Body(AreaCharts a)
    {
        var sb = new StringBuilder(BodyMarker).Append('\n');
        sb.Append('\n').Append(Legend).Append('\n');
        // D314(1): a part names the area it belongs to and its sibling parts; a split area lists its parts and their start flows.
        if (a.PartOf is var (parent, up))
        {
            sb.Append($"\n**Part of [{Line(parent)}]({up}).**");
            if (a.Siblings.Count > 0) sb.Append($" Its other parts: {string.Join(", ", a.Siblings.Select(x => $"[{Line(x.Name)}]({x.Link})"))}.");
            sb.Append('\n');
        }
        if (a.Parts.Count > 0)
        {
            sb.Append("\n## Parts of this area\n\n");
            foreach (var (name, link, flows) in a.Parts) sb.Append($"- [{Line(name)}]({link}): {Line(flows)}\n");
        }
        // D316: the controllers' other endpoints, which live on other areas' pages.
        sb.Append(PageElsewhere.Render(a.Elsewhere));
        if (a.Charts.Count > 0)
        {
            sb.Append("\n## Clicks that change something\n\n");
            foreach (var c in a.Charts)
                sb.Append($"- {Line(c.Title)} ([`{c.Flow}`]({c.Flow}.md)): {(c.Drawn ? $"chart, {c.Chart.Nodes.Count} nodes" : $"too large to draw, {c.Chart.Nodes.Count} nodes")}, {Plural(c.Scenarios.Count, "scenario")}\n")
                    .Append(StartLines(c));
            sb.Append('\n');
            foreach (var c in a.Charts) sb.Append(FlowMarkdown(c, a));
        }
        else sb.Append('\n');
        // D306: heavy shared work, each once, after the flows that run it.
        if (a.Shared.Count > 0)
        {
            sb.Append($"{SharedHeading}\n\n");
            foreach (var p in a.Shared)
                sb.Append($"- {Line(p.Chart?.Title ?? p.Line!.Name)} (`{p.Flow}`): {Reached(p)}, {(p.Chart is { } c ? c.Drawn ? $"chart, {c.Chart.Nodes.Count} nodes, {Plural(c.Scenarios.Count, "scenario")}" : $"too large to draw, {c.Chart.Nodes.Count} nodes, {Plural(c.Scenarios.Count, "scenario")}" : "one line")}\n");
            sb.Append('\n');
            foreach (var p in a.Shared)
                if (p.Chart is { } c) sb.Append(FlowMarkdown(c, a, p));
                else sb.Append($"{SharedMarker(p.Flow)}\n## {Line(p.Line!.Name)}\n\n`{p.Flow}` · shared · {Reached(p)}\n\n{OneLine(p.Line, a)}\n");
        }
        if (a.DisplayOnly.Count > 0)
        {
            sb.Append("## One line each\n\n");
            foreach (var d in a.DisplayOnly) sb.Append(OneLine(d, a));
            sb.Append('\n');
        }
        if (a.CantSee.Count > 0)
        {
            sb.Append("## What code couldn't see\n\n");
            foreach (var (flow, step, why, at) in a.CantSee) sb.Append($"- ❓ `{flow}`{(step is { } n ? $" step {n}" : "")}: {Line(why)}{(at == "" ? "" : $" (`{at}`)")}\n");
            sb.Append('\n');
        }
        if (a.Missing.Count > 0) sb.Append($"**Flows with no file, left out:** {string.Join(", ", a.Missing.Select(m => $"`{m}`"))}\n\n");
        return sb.ToString();
    }

    /// <summary>A YAML double-quoted string.</summary>
    static string Q(string s) => $"\"{s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    /// <summary>
    /// D283's note, a YAML block between <c>---</c> lines that opens the page: the area, made by code with no AI, the commit, the
    /// settings that change what it shows, clicks charted / one line / too large to draw, steps by label. Counts only, no date.
    /// </summary>
    public static string Note(AreaCharts a)
    {
        var steps = a.Steps;
        var inv = CultureInfo.InvariantCulture;
        return string.Create(inv, $"""
            ---
            area: {Q(a.Area)}
            madeBy: code, no AI
            commit: {Q(a.Commit)}
            settings:
              hubCallers: {a.HubCallers}
              callDepth: {a.Depth}
              frameworkSharePercent: {a.Settings.FrameworkShare}
              chartCap: {a.Settings.ChartCap}
            clicks:
              charted: {a.Charts.Count(c => c.Drawn)}
              oneLine: {a.DisplayOnly.Count}
              tooLargeToDraw: {a.Charts.Count(c => !c.Drawn)}
            steps:
              certain: {steps.Certain}
              guessed: {steps.Guessed}
              cantTell: {steps.Unknown}
            ---

            """);
    }

    /// <summary><c>pages chart --format counts</c>: a Markdown table, one row per changing flow, then the totals and their spread.</summary>
    public static string Counts(AreaCharts a)
    {
        var sb = new StringBuilder($"# {a.Area}: chart counts\n\n");
        sb.Append("| Flow | Kind | Nodes | Drawn | Boxes | Diamonds | Ends | Follow-ons | Edges | Scenario rows | Shared guards | Shared changes | Guessed | Can't tell |\n");
        sb.Append("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|\n");
        foreach (var c in a.Charts)
        {
            var d = c.Chart;
            sb.Append($"| {c.Flow} | {c.Kind} | {d.Nodes.Count} | {(c.Drawn ? "yes" : "no")} | {d.Count("change")} | {d.Count("diamond")} | {d.Count("refused") + d.Count("redirect")} | {d.Count("follow-on")} | {d.Edges.Count} | {c.Scenarios.Count} | {c.SharedGuards.Count} | {c.SharedChanges.Count} | {c.Counts.Guessed} | {c.Counts.Unknown} |\n");
        }
        sb.Append('\n');
        foreach (var (k, v) in Totals(a)) sb.Append($"- {k}: {v?.ToJsonString()}\n");
        return sb.ToString();
    }

    static int Percentile(List<int> sorted, double p) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];

    static JsonObject Spread(IEnumerable<int> values, params int[] over)
    {
        var xs = values.Order().ToList();
        var o = new JsonObject
        {
            ["min"] = xs.Count == 0 ? 0 : xs[0], ["median"] = Percentile(xs, 0.5), ["p75"] = Percentile(xs, 0.75), ["p90"] = Percentile(xs, 0.9), ["max"] = xs.Count == 0 ? 0 : xs[^1],
        };
        foreach (var n in over) o[$"over{n}"] = xs.Count(x => x > n);
        return o;
    }

    public static JsonObject Totals(AreaCharts a)
    {
        var kinds = new SortedDictionary<string, int>(O);
        foreach (var c in a.Charts) foreach (var (k, v) in c.DiamondKinds) kinds[k] = kinds.GetValueOrDefault(k) + v;
        return new JsonObject
        {
            ["flows"] = a.Charts.Count + a.DisplayOnly.Count, ["charts"] = a.Charts.Count, ["drawn"] = a.Charts.Count(c => c.Drawn), ["tooLarge"] = a.Charts.Count(c => !c.Drawn),
            ["displayOnly"] = a.DisplayOnly.Count, ["chartsOnlyUnknown"] = a.Charts.Count(c => c.OnlyUnknowns),
            ["nodes"] = Spread(a.Charts.Select(c => c.Chart.Nodes.Count), 15, 25, a.Settings.ChartCap),
            ["scenarioRows"] = Spread(a.Charts.Select(c => c.Scenarios.Count), 10, 20),
            ["sharedGuards"] = a.Charts.Sum(c => c.SharedGuards.Count), ["sharedChanges"] = a.Charts.Sum(c => c.SharedChanges.Count),
            ["diamondKinds"] = new JsonObject(kinds.Select(k => KeyValuePair.Create(k.Key, (JsonNode?)k.Value))),
            ["hubCallers"] = a.HubCallers, ["frameworkShare"] = a.Settings.FrameworkShare, ["chartCap"] = a.Settings.ChartCap, ["appFlows"] = a.AppFlows,
        };
    }

    static JsonObject Graph(ChartGraph g) => new()
    {
        ["mermaid"] = Mermaid(g),
        ["nodes"] = new JsonArray([.. g.Nodes.Select(n => (JsonNode)new JsonObject { ["id"] = n.Id, ["shape"] = n.Shape, ["label"] = n.Label })]),
        ["edges"] = new JsonArray([.. g.Edges.Select(e =>
        {
            var o = new JsonObject { ["from"] = e.From, ["to"] = e.To, ["dashed"] = e.Dashed };
            if (e.Label is not null) o["label"] = e.Label;
            return (JsonNode)o;
        })]),
    };

    static JsonObject Counted(StepCounts c) => new() { ["certain"] = c.Certain, ["guessed"] = c.Guessed, ["cantTell"] = c.Unknown };

    /// <summary><c>pages chart --format json</c>.</summary>
    public static JsonObject ToJson(AreaCharts a) => new()
    {
        ["commit"] = a.Commit, ["area"] = a.Area, ["folder"] = a.Folder, ["steps"] = Counted(a.Steps),
        ["charts"] = new JsonArray([.. a.Charts.Select(c => (JsonNode)new JsonObject
        {
            ["flow"] = c.Flow, ["kind"] = c.Kind, ["title"] = c.Title, ["drawn"] = c.Drawn, ["chart"] = Graph(c.Chart), ["steps"] = Counted(c.Counts),
            ["scenarios"] = new JsonArray([.. c.Scenarios.Select(r => (JsonNode)new JsonObject { ["id"] = r.Id, ["scenario"] = r.Scenario, ["decides"] = r.Decides, ["steps"] = r.Steps, ["ends"] = r.Ends })]),
            ["serviceFailures"] = new JsonObject(c.ServiceFailures.Select(s => KeyValuePair.Create(s.Key, (JsonNode?)Rows(s.Value)))),
            ["followOns"] = Rows(c.FollowOns), ["sharedGuards"] = Rows(c.SharedGuards), ["sharedChanges"] = Rows(c.SharedChanges), ["handled"] = Rows(c.Handled),
            ["onlyUnknowns"] = c.OnlyUnknowns,
        })]),
        ["displayOnly"] = new JsonArray([.. a.DisplayOnly.Select(d => (JsonNode)new JsonObject { ["flow"] = d.Flow, ["kind"] = d.Kind, ["name"] = d.Name, ["why"] = d.Why, ["listed"] = Rows(d.Listed), ["stops"] = Rows(d.Stops), ["sharedGuards"] = Rows(d.SharedGuards), ["handled"] = Rows(d.Handled), ["followOns"] = Rows(d.FollowOns), ["steps"] = Counted(d.Counts) })]),
        ["missing"] = new JsonArray([.. a.Missing.Select(m => (JsonNode)m)]),
        ["totals"] = Totals(a),
    };

    static JsonArray Rows(List<ChartRow> rows) => [.. rows.Select(r => (JsonNode)new JsonObject
    {
        ["text"] = r.Text, ["at"] = r.At, ["steps"] = new JsonArray([.. r.Steps.Select(s => (JsonNode)s)]), ["flows"] = r.Flows,
        ["stops"] = new JsonArray([.. r.Stops.Select(s => (JsonNode)s.ToString())]),
    })];
}

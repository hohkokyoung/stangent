using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Stangent.Flows;

/// <summary>
/// Gate 1 of D280, by code and free: for every changing flow on an area's page, each change and each stop of its flow file is on
/// the page, matched by step number (D284); and the page rebuilds byte for byte, and is what is on disk. A changing flow's changes
/// (its own and the saves in shared code it continues into) count when its chart's change boxes, or above the chart cap (D282) its
/// list of changes, its "Changes made in shared code" list (D281) or a setup row name their steps, or with no step their line; a step
/// named anywhere else ("skips step 133") doesn't count (D289). A stop counts when its part says "stops at step N (`file:line`)".
/// A one-line flow's changes count when its line names their steps, its own stops when a sub-bullet under it says so (D287); its
/// shared guards are counted. A screen's changes and stops drawn under its clicks are counted as theirs. A start flow with no
/// readable file, and a change with neither a step nor a line, are reported (D289).
/// </summary>
public static class PageGate
{
    static readonly StringComparer O = StringComparer.Ordinal;
    // D304(4): "as in step N" names the step a repeat copies, so it doesn't mention step N.
    static readonly Regex Mentioned = new(@"(?<!\bas in )\bsteps? (?<n>\d+(?:, \d+)*)", RegexOptions.CultureInvariant);
    static readonly Regex ChangeBox = new(@"^\s+C\d+\[""(?<label>.*)""\]$", RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>Checks every area's page, or the one named (by name or folder); <c>ok</c> is false when anything is missing, a rebuild differs or a page on disk isn't current.</summary>
    public static JsonObject Verify(string root, Areas a, AreaLayout layout, PageSettings settings, string? only = null)
    {
        var cache = new PageCache(root, a);
        var list = new JsonArray();
        var ok = true;
        int changes = 0, stops = 0, missingCount = 0, oneLineGuards = 0, handled = 0, receivers = 0;
        var picked = layout.SlugOf.Where(x => only is null || x.Key == only || x.Value == only).OrderBy(x => x.Key, O).ToList();
        if (only is not null && picked.Count == 0)
            throw new Stangent.Core.StangentException("not_found", $"No area '{only}' in {AreaFiles.Confirmed}.", Stangent.Core.ExitCodes.NotFound);
        foreach (var (area, slug) in picked)
        {
            var charts = PageChart.Build(root, a, layout.Placement!, area, slug, settings, cache, layout);
            var path = Path.Combine(root, AreaLayout.Root, slug, AreaLayout.Page);
            var old = File.Exists(path) ? File.ReadAllText(path) : null;
            var text = AreaPages.Text(a, area, layout, old, charts);
            var again = AreaPages.Text(a, area, layout, old, PageChart.Build(root, a, layout.Placement!, area, slug, settings, new PageCache(root, a), layout));
            var onDisk = old is null ? "missing" : old == text ? "same" : "stale";
            var result = Check(cache.Reader, charts, text);
            changes += result.Changes;
            stops += result.Stops;
            handled += result.Handled;
            receivers += result.Receivers;
            oneLineGuards += result.OneLineGuards;
            missingCount += result.Missing.Count;
            var good = result.Missing.Count == 0 && text == again && onDisk == "same";
            ok &= good;
            list.Add(new JsonObject
            {
                ["area"] = area, ["folder"] = slug, ["ok"] = good, ["sameBytes"] = text == again, ["onDisk"] = onDisk,
                ["flows"] = charts.Charts.Count + charts.DisplayOnly.Count, ["charted"] = charts.Charts.Count(c => c.Drawn), ["tooLarge"] = charts.Charts.Count(c => !c.Drawn),
                ["oneLine"] = charts.DisplayOnly.Count, ["sharedParts"] = charts.Shared.Count, ["changes"] = result.Changes, ["stops"] = result.Stops, ["handled"] = result.Handled, ["receivers"] = result.Receivers, ["oneLineSharedGuards"] = result.OneLineGuards,
                ["sharedFlows"] = charts.Reached.Count, ["sharedLeftOut"] = result.SharedLeftOut,
                ["missing"] = new JsonArray([.. result.Missing]),
            });
        }
        var report = new JsonObject
        {
            ["ok"] = ok, ["areas"] = list, ["changes"] = changes, ["stops"] = stops, ["handled"] = handled, ["receivers"] = receivers, ["missing"] = missingCount, ["oneLineSharedGuards"] = oneLineGuards,
        };
        if (list.OfType<JsonObject>().Any(x => (string)x["onDisk"]! != "same")) report["hint"] = "A page on disk isn't what code makes now: run `stangent areas check` with components.pages on.";
        return report;
    }

    /// <summary>What gate 1 found on one page's text.</summary>
    public sealed record Result(int Changes, int Stops, int OneLineGuards, List<JsonNode> Missing, int Handled = 0, int Receivers = 0, int SharedLeftOut = 0);

    /// <summary>Gate 1 on one page's text: every change and stop of each flow file is on it.</summary>
    internal static Result Check(FlowReader reader, AreaCharts charts, string text)
    {
        var missing = new List<JsonNode>();
        int changes = 0, stops = 0, oneLineGuards = 0, handled = 0, receivers = 0;
        // D289: each change counted once per area, a save in shared code by its own line however many flows reach it; every one still checked.
        var counted = new HashSet<string>(O);
        void Miss(string flow, string what, int? step, string at, string label) =>
            missing.Add(new JsonObject { ["flow"] = flow, ["what"] = what, ["step"] = step, ["at"] = at, ["text"] = label });

        // D289: a start flow with no readable file is never drawn, so nothing on the page can stand for it.
        foreach (var m in charts.Missing) Miss(m, "flow file", null, "", "start flow with no readable flow file");
        var cut = charts.Cut;
        // D306: each line running a shared part names a part that is on the page.
        var parts = cut is null ? new Dictionary<string, string>(O) : cut.Heavy.GroupBy(cut.Box, O).ToDictionary(g => g.Key, g => g.First(), O);
        void Linked(string flowId, int? step, string at, string kind, string label)
        {
            if (kind == "shared" && (!parts.TryGetValue(label, out var target) || !text.Contains(PageChart.SharedMarker(target) + "\n", StringComparison.Ordinal)))
                Miss(flowId, "shared part", step, at, label);
        }
        void CheckChart(FlowChart c, string part, HashSet<string> excused)
        {
            if (reader.Load(c.Flow) is not { } flow) { Miss(c.Flow, "flow file", null, "", "start flow with no readable flow file"); return; }
            // D289: only the change boxes (above the cap, the list of changes), the shared-code list and setup rows stand for changes.
            var boxes = c.Drawn
                ? string.Join("\n", ChangeBox.Matches(Between(part, "```mermaid\n", "```\n")).Select(m => m.Groups["label"].Value))
                : Between(part, "**Changes**\n\n", "\n\n");
            var shared = Between(part, "**Changes made in shared code**", "\n\n**");
            var setup = string.Join("\n", part.Split('\n').Where(l => l.StartsWith("| U", StringComparison.Ordinal)));
            var changeText = $"{boxes}\n{shared}\n{setup}";
            var exitSteps = FlowReader.Steps(flow);
            foreach (var (step, at, label, id, kind) in Changes(reader, flow, cut))
            {
                if (excused.Contains($"step|{at}") || excused.Contains($"later|{at}")) continue;
                if (counted.Add(id)) changes++;
                if (!Found(step, at, kind, changeText)) Miss(c.Flow, "change", step, at, label);
                Linked(c.Flow, step, at, kind ?? "", label);
            }
            foreach (var e in (flow["exits"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var at = (string?)e["at"] ?? "";
                if (excused.Contains($"exit|{at}")) continue;
                stops++;
                var step = PageChart.StopStep(e, exitSteps);
                var stop = new ChartStop(step, at).ToString();
                if (!part.Contains(stop, StringComparison.Ordinal)) Miss(c.Flow, "stop", step, at, (string?)e["what"] ?? "");
            }
            var handledText = Between(part, "**Errors handled on the way (not stops)**\n\n", "\n\n");
            handled += CheckHandled(c.Flow, flow, excused, handledText, Miss);
            receivers += CheckReceivers(reader, c.Flow, flow, excused, Between(part, "**What runs afterwards on its own**\n\n", "\n\n"), Miss, cut);
        }
        void CheckLine(DisplayOnly d)
        {
            if (reader.Load(d.Flow) is not { } flow) { Miss(d.Flow, "flow file", null, "", "start flow with no readable flow file"); return; }
            var lines = text.Split('\n');
            var first = Array.FindIndex(lines, l => l.StartsWith($"- `{d.Flow}` ", StringComparison.Ordinal));
            var line = first < 0 ? "" : lines[first];
            var block = first < 0 ? "" : string.Join("\n", lines.Skip(first).Take(1).Concat(lines.Skip(first + 1).TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal) && l.TrimStart().StartsWith("- ", StringComparison.Ordinal))));
            if (line == "") Miss(d.Flow, "line", null, "", "no line for this flow");
            var excused = charts.InClicks.GetValueOrDefault(d.Flow) ?? [];
            var exitSteps = FlowReader.Steps(flow);
            foreach (var (step, at, label, id, kind) in Changes(reader, flow, cut))
            {
                if (excused.Contains($"step|{at}") || excused.Contains($"later|{at}")) continue;
                if (counted.Add(id)) changes++;
                if (!Found(step, at, kind, line)) Miss(d.Flow, "change", step, at, label);
                Linked(d.Flow, step, at, kind ?? "", label);
            }
            // D287: its own stops are under its line; its shared guards are only counted.
            var guards = d.SharedGuards.SelectMany(r => r.Stops).Select(x => x.ToString()).ToHashSet(O);
            foreach (var e in (flow["exits"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var at = (string?)e["at"] ?? "";
                if (excused.Contains($"exit|{at}")) continue;
                var step = PageChart.StopStep(e, exitSteps);
                var stop = new ChartStop(step, at).ToString();
                if (guards.Contains(stop)) { oneLineGuards++; continue; }
                stops++;
                if (!block.Contains(stop, StringComparison.Ordinal)) Miss(d.Flow, "stop", step, at, (string?)e["what"] ?? "");
            }
            handled += CheckHandled(d.Flow, flow, excused, block, Miss);
            receivers += CheckReceivers(reader, d.Flow, flow, excused, block, Miss, cut);
        }
        foreach (var c in charts.Charts) CheckChart(c, Part(text, PageChart.FlowMarker(c.Flow)), charts.InClicks.GetValueOrDefault(c.Flow) ?? []);
        foreach (var d in charts.DisplayOnly) CheckLine(d);
        // D306: each shared part is checked like a flow's: its changes and stops are in its part.
        foreach (var p in charts.Shared)
        {
            var part = Part(text, PageChart.SharedMarker(p.Flow));
            if (part == "") Miss(p.Flow, "shared part", null, "", "no part for this shared flow");
            if (p.Chart is { } c) CheckChart(c, part, []);
            else if (p.Line is { } d) CheckLine(d);
        }
        // D311(2): every shared flow the area reaches is in the "Shared flows" list or, quiet by the flow file, counted in its left-out line.
        var sharedFrom = text.IndexOf("\n## Shared flows (", StringComparison.Ordinal);
        var sharedLines = sharedFrom < 0 ? [] : text[(sharedFrom + 1)..].Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal)).ToList();
        var memo = new Dictionary<string, bool>(O);
        var leftOut = 0;
        foreach (var id in charts.Reached)
        {
            if (sharedLines.Any(l => l.StartsWith("- [", StringComparison.Ordinal) && l.EndsWith($" `{id}`", StringComparison.Ordinal))) continue;
            leftOut++;
            if (!charts.Quiet.Contains(id) || !PageChart.Quiet(reader, id, memo)) Miss(id, "shared flow", null, "", "neither listed under Shared flows nor quiet");
        }
        if (leftOut > 0 && !sharedLines.Any(l => l.StartsWith($"{AreaPages.LeftOutStart}{leftOut} that ", StringComparison.Ordinal)))
            Miss("", "shared flow", null, "", $"no line counting the {leftOut} shared flows left out");
        return new(changes, stops, oneLineGuards, missing, handled, receivers, leftOut);
    }

    /// <summary>D300: every handled error of the flow file is on a line of its part naming its line and the catch that takes it.</summary>
    static int CheckHandled(string flowId, JsonObject flow, HashSet<string> excused, string text, Action<string, string, int?, string, string> miss)
    {
        var n = 0;
        var lines = text.Split('\n');
        foreach (var e in (flow["handled"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var at = (string?)e["at"] ?? "";
            if (excused.Contains($"handled|{at}")) continue;
            n++;
            var caught = $"caught by `{(string?)e["caughtBy"]}`";
            if (!lines.Any(l => l.Contains("handled: ", StringComparison.Ordinal) && l.Contains($"`{at}`", StringComparison.Ordinal) && l.Contains(caught, StringComparison.Ordinal)))
                miss(flowId, "handled", (int?)e["step"], at, (string?)e["what"] ?? "");
        }
        return n;
    }

    /// <summary>
    /// D293: every receiver the flow file names (its <c>message</c> and <c>runs-later</c> links; D304(6): and the messages of the
    /// shared flows it continues into) is under "What runs afterwards", on the line of its own send: the line names the send's step
    /// (with no step, anywhere in the text).
    /// </summary>
    static int CheckReceivers(FlowReader reader, string flowId, JsonObject flow, HashSet<string> excused, string text, Action<string, string, int?, string, string> miss, SharedCut? cut)
    {
        var n = 0;
        var lines = text.Split('\n');
        foreach (var l in PageChart.ReceiverLinks(reader, (flow["links"] as JsonArray ?? []).OfType<JsonObject>(), cut))
        {
            var at = (string?)l["at"] ?? "";
            if (excused.Contains($"step|{at}") || excused.Contains($"later|{at}") || excused.Contains($"message|{at}")) continue;
            n++;
            var target = (string?)l["flow"] ?? (string?)l["to"] ?? "";
            var step = (int?)l["step"];
            if (target == "" || !lines.Select((x, i) => (x, i)).Any(x => x.x.Contains($"`{target}`", StringComparison.Ordinal) && (step is not { } s || Steps(x.x).Contains(s) || Steps(Above(lines, x.i)).Contains(s))))
                miss(flowId, "receiver", step, at, target);
        }
        return n;
    }

    /// <summary>
    /// A change is found by its step number; with none, by its line; with neither it can't be matched (D289). D304(4): with a step,
    /// the same line of the change text (a box, a list item, a row, a one-line flow's line) must mention the
    /// step and carry the change's kind word, so one change's box can't stand for another's.
    /// </summary>
    internal static bool Found(int? step, string at, string? kind, string text)
    {
        if (step is not { } n) return at != "" && text.Contains(at, StringComparison.Ordinal);
        var words = KindWords(kind);
        return text.Split('\n').Any(l => Steps(l).Contains(n) && (words.Length == 0 || words.Any(w => l.Contains(w, StringComparison.Ordinal))));
    }

    /// <summary>D304(4): the words a change of this kind is written with on the page; none for a kind not known (an unknown, queued work by link).</summary>
    static string[] KindWords(string? kind) => kind switch
    {
        "save" => ["save"],
        "outside-call" => ["outside"],
        "message" => ["sends", "hands the event"],
        "queue" => ["queues"],
        "push" => ["pushes"],
        "file" => ["file"],
        "shared" => ["see its part"],
        _ => [],
    };

    /// <summary>
    /// A flow file's changes (D271): change steps, steps a ❓ sits on, unknowns with no step, queued work, and the saves in shared
    /// code it continues into (D260, at the step that continues there; D289); each with its step and line.
    /// </summary>
    static List<(int? Step, string At, string Label, string Id, string? Kind)> Changes(FlowReader reader, JsonObject flow, SharedCut? cut)
    {
        var id = (string?)flow["id"] ?? "";
        var list = new List<(int?, string, string, string, string?)>();
        var unknownSteps = (flow["unknowns"] as JsonArray ?? []).OfType<JsonObject>().Where(u => u["step"] is not null).Select(u => (int)u["step"]!).ToHashSet();
        foreach (var st in FlowReader.Steps(flow).Values.OrderBy(s => (int)s["n"]!))
            if (PageChart.Change(st) is not null || unknownSteps.Contains((int)st["n"]!))
                list.Add(((int)st["n"]!, (string?)st["at"] ?? "", (string?)st["what"] ?? "", $"{id}|{(int)st["n"]!}|{(string?)st["at"]}", PageChart.Change(st)));
        foreach (var u in (flow["unknowns"] as JsonArray ?? []).OfType<JsonObject>().Where(u => u["step"] is null))
            list.Add((null, (string?)u["at"] ?? "", (string?)u["why"] ?? "", $"{id}||{(string?)u["at"]}|{(string?)u["why"]}", null));
        foreach (var l in (flow["links"] as JsonArray ?? []).OfType<JsonObject>().Where(l => (string?)l["kind"] == "runs-later"))
            list.Add(((int?)l["step"], (string?)l["at"] ?? "", (string?)l["sentence"] ?? "", $"{id}|later|{(int?)l["step"]}|{(string?)l["at"]}", null));
        // D296: a repeat ("as in step N") carries step N's changes; its own step must stand for them.
        var steps = FlowReader.Steps(flow);
        var children = steps.Values.Where(s => s["from"] is not null).GroupBy(s => (int)s["from"]!).ToDictionary(g => g.Key, g => g.Select(s => (int)s["n"]!).Order().ToList());
        foreach (var c in PageChart.Carried(reader, steps, children, [.. (flow["links"] as JsonArray ?? []).OfType<JsonObject>()], cut))
            list.Add((c.Step, c.At, c.Text, $"{id}|as|{c.Step}|{c.From?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? c.At}", c.Kind));
        var seen = new HashSet<string>(O);
        foreach (var l in (flow["links"] as JsonArray ?? []).OfType<JsonObject>())
            if ((string?)l["kind"] == "continues" && (string?)l["flow"] is not null)
                foreach (var save in PageChart.SharedOf(reader, l, false, cut))
                    if (seen.Add($"{(int?)l["step"]}|{save.At}|{save.Kind}"))
                        // D306: a line into a shared part stands for everything in it; the part itself is checked on its own.
                        list.Add(((int?)l["step"], save.At, save.Text, save.Kind == "shared" ? $"{id}|part|{(int?)l["step"]}|{save.Text}" : $"shared|{save.At}",
                            save.Kind is "outside-call" or "shared" ? save.Kind : null));
        return list;
    }

    /// <summary>
    /// D314(2): the bullet a sub-bullet sits under (the nearest line above it indented less), or "" for a top-level line; a long
    /// receiver list is one sub-bullet per flow under the line naming its send's step.
    /// </summary>
    static string Above(string[] lines, int i)
    {
        static int Indent(string l) => l.Length - l.TrimStart(' ').Length;
        var own = Indent(lines[i]);
        if (own == 0) return "";
        for (var j = i - 1; j >= 0; j--)
            if (lines[j].TrimStart().StartsWith("- ", StringComparison.Ordinal) && Indent(lines[j]) < own) return lines[j];
        return "";
    }

    static HashSet<int> Steps(string text) =>
        [.. Mentioned.Matches(text).SelectMany(m => m.Groups["n"].Value.Split(", ").Select(int.Parse))];

    /// <summary>A flow's part of the page: from its marker to the next flow's, the one-line list, the can't-see list or the end of layer 4's words.</summary>
    static string Part(string text, string marker)
    {
        var start = text.IndexOf(marker + "\n", StringComparison.Ordinal);
        if (start < 0) return "";
        var ends = new[] { "\n<!-- flow ", "\n<!-- shared ", "\n" + PageChart.SharedHeading, "\n## One line each", "\n## What code couldn't see", "\n" + AreaPages.WordsEnd }
            .Select(e => text.IndexOf(e, start + 1, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(text.Length).Min();
        return text[start..ends];
    }

    static string Between(string text, string from, string to)
    {
        var s = text.IndexOf(from, StringComparison.Ordinal);
        if (s < 0) return "";
        s += from.Length;
        var e = text.IndexOf(to, s, StringComparison.Ordinal);
        return e < 0 ? text[s..] : text[s..e];
    }
}

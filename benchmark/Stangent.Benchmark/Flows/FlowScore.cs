using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Flows;
using Stangent.Graph;

namespace Stangent.Benchmark.Flows;

/// <summary>
/// Scores a clone's built flows (<c>specs/flows/</c>) against the frozen key (D177, D183): each key flow is matched to a built
/// one by its start, then each key item by its <c>at</c> (path:line), else by its <c>code</c> in the same file. Reports D177's bar
/// items 1–5: no wrong ✅, ≥95% of the key's steps, ends and conditions found, every 🟡 and ❓ with a reason, byte for byte,
/// no silent skips.
/// </summary>
/// <remarks><c>dotnet run --project benchmark/Stangent.Benchmark -- --score-flows &lt;clone&gt; &lt;key-dir&gt; [--out file]</c></remarks>
public static partial class FlowScore
{
    public const double Bar = 0.95;

    sealed record Built(string Id, string Kind, JsonObject Json, List<JsonObject> Steps, List<JsonObject> Exits, List<JsonObject> Links, List<JsonObject> Checks)
    {
        public string Start => (string)Json["start"]!["at"]!;
        public IEnumerable<string> Triggers => Json["start"]!["triggers"]!.AsArray().Select(t => (string)t!);
    }

    public static JsonObject Run(string clone, string keyDir)
    {
        var dir = Path.Combine(clone, FlowFiles.Folder);
        var built = Directory.EnumerateFiles(dir, "*.json").Where(p => Path.GetFileName(p) != "index.json").Order(StringComparer.Ordinal)
            .Select(p => JsonNode.Parse(File.ReadAllText(p))!.AsObject())
            .Select(j => new Built((string)j["id"]!, (string)j["kind"]!, j, Objects(j["steps"]), Objects(j["exits"]), Objects(j["links"]), Objects(j["checks"])))
            .ToList();
        var graph = GraphStore.Load(Path.Combine(clone, ".stangent", "graph.sqlite")) ?? throw new InvalidOperationException("no graph in the clone");
        // The key names an entity ("AppUserProgress"), a built end the table mapping it ("AppUserProgresses"): the graph's own map.
        var nodes = graph.AllNodes.GroupBy(n => n.Id, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        tables = graph.AllEdges.Where(e => e.Kind == EdgeKind.Maps && nodes.TryGetValue(e.Source, out var t) && t.Kind == NodeKind.Table && nodes.ContainsKey(e.Target))
            .GroupBy(e => TypeShort(nodes[e.Target].Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(e => nodes[e.Source].Name).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var flows = new JsonArray();
        var totals = new Dictionary<string, (int Found, int Of)>(StringComparer.Ordinal);
        foreach (var keyFile in Directory.EnumerateFiles(keyDir, "*.json").Order(StringComparer.Ordinal))
        {
            var key = JsonNode.Parse(File.ReadAllText(keyFile))!.AsObject();
            var result = ScoreFlow(clone, key, built);
            foreach (var (name, value) in result["items"]!.AsObject())
            {
                var (f, o) = totals.GetValueOrDefault(name);
                totals[name] = (f + (int)value!["found"]!, o + (int)value["of"]!);
            }
            flows.Add(result);
        }
        var bar2 = new[] { "steps", "ends", "conditions" }.Aggregate((Found: 0, Of: 0), (a, n) => (a.Found + totals.GetValueOrDefault(n).Found, a.Of + totals.GetValueOrDefault(n).Of));
        var wrong = WrongCertain(clone, built);
        var check = FlowFiles.Check(clone, graph, new FlowSettings());
        // Item 4: two builds from the same graph, rendered, must be the same bytes (the check above compares one with the disk).
        var a = FlowFiles.Render(FlowBuilder.Build(graph, clone));
        var b = FlowFiles.Render(FlowBuilder.Build(graph, clone));
        var same = a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);
        var problems = check["problems"]!.AsArray().Select(p => (string)p!).ToList();
        var reasons = problems.Count(p => p.StartsWith("reasons:", StringComparison.Ordinal));
        var bytes = problems.Count(p => p.StartsWith("byte-for-byte:", StringComparison.Ordinal));
        var silent = problems.Count(p => p.StartsWith("silent skip:", StringComparison.Ordinal));
        return new JsonObject
        {
            ["clone"] = clone, ["commit"] = graph.Commit, ["builtFlows"] = built.Count,
            ["bar"] = new JsonObject
            {
                ["1_noWrongCertain"] = new JsonObject { ["pass"] = wrong.Count == 0, ["certainSteps"] = built.Sum(f => f.Steps.Count(s => (string)s["label"]! == "certain")), ["wrong"] = new JsonArray([.. wrong.Take(40).Select(w => (JsonNode)w)]), ["wrongCount"] = wrong.Count },
                ["2_found"] = new JsonObject { ["pass"] = bar2.Of > 0 && bar2.Found >= Bar * bar2.Of, ["found"] = bar2.Found, ["of"] = bar2.Of, ["share"] = Share(bar2.Found, bar2.Of) },
                ["3_reasons"] = new JsonObject { ["pass"] = reasons == 0, ["problems"] = reasons, ["guesses"] = check["guesses"]!.DeepClone(), ["unknowns"] = check["unknowns"]!.DeepClone() },
                ["4_byteForByte"] = new JsonObject { ["pass"] = same && bytes == 0, ["twoBuildsSame"] = same, ["filesDifferFromDisk"] = bytes },
                ["5_noSilentSkips"] = new JsonObject { ["pass"] = silent == 0, ["starts"] = check["starts"]!.DeepClone(), ["flows"] = check["flows"]!.DeepClone(), ["skipped"] = check["skipped"]!.DeepClone(), ["silent"] = silent },
            },
            ["items"] = new JsonObject([.. totals.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => KeyValuePair.Create(t.Key, (JsonNode?)Count(t.Value.Found, t.Value.Of)))]),
            ["flows"] = flows,
        };
    }

    static JsonObject Count(int found, int of) => new() { ["found"] = found, ["of"] = of, ["share"] = Share(found, of) };
    static double Share(int found, int of) => of == 0 ? 1 : Math.Round((double)found / of, 4);
    static List<JsonObject> Objects(JsonNode? array) => array is JsonArray a ? [.. a.OfType<JsonObject>()] : [];

    /// <summary>Whitespace runs as one space, as D183 reads a quote.</summary>
    public static string Norm(string code) => Spaces().Replace(code, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>A built item sits where a key item does: the same line, or the same file with the key's quote starting with its line's code.</summary>
    static bool Same(string keyAt, string keyCode, JsonObject item)
    {
        var at = (string?)item["at"];
        if (at is null) return false;
        if (at == keyAt) return true;
        var file = keyAt[..keyAt.LastIndexOf(':')];
        var code = (string?)item["code"] ?? "";
        return at.StartsWith(file + ":", StringComparison.Ordinal) && code.Length > 3 && Norm(keyCode).StartsWith(Norm(code), StringComparison.Ordinal);
    }

    static JsonObject ScoreFlow(string clone, JsonObject key, List<Built> built)
    {
        var kind = (string)key["kind"]!;
        var start = key["start"]!.AsObject();
        var startAt = (string)start["at"]!;
        var startCode = (string)start["code"]!;
        var keySteps = Objects(key["steps"]);
        var keyExits = Objects(key["exits"]);
        // The key's start may be the button, the form, the route, the load hook or the job's registration: a built flow of the
        // same kind starting there, run by it, or holding a step there. Several → the one finding the most of the key.
        var candidates = built.Where(b => b.Kind == kind && (b.Start == startAt || b.Triggers.Any(t => t.EndsWith($"({startAt})", StringComparison.Ordinal))
            || b.Steps.Any(s => Same(startAt, startCode, s)) || ((string)b.Json["start"]!["code"]! is { Length: > 3 } c && Norm(startCode).StartsWith(Norm(c), StringComparison.Ordinal) && b.Start.StartsWith(startAt[..startAt.LastIndexOf(':')] + ":", StringComparison.Ordinal)))).ToList();
        if (candidates.Count == 0) candidates = built.Where(b => b.Kind == kind && keySteps.Take(3).Any(k => b.Steps.Any(s => Same((string)k["at"]!, (string)k["code"]!, s)))).ToList();
        var byId = built.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var scored = candidates.Select(c => (Flow: c, Result: Items(key, keySteps, keyExits, Expand(c, byId)))).OrderByDescending(x => x.Result.Found).ThenBy(x => x.Flow.Id, StringComparer.Ordinal).ToList();
        var best = scored.FirstOrDefault();
        var items = best.Flow is null ? Items(key, keySteps, keyExits, null) : best.Result;
        return new JsonObject
        {
            ["key"] = $"{key["repo"]}/{key["id"]}", ["kind"] = kind, ["start"] = startAt,
            ["built"] = best.Flow?.Id, ["candidates"] = new JsonArray([.. candidates.Select(c => (JsonNode)c.Id)]),
            ["items"] = items.Counts, ["missed"] = items.Missed,
        };
    }

    sealed record ItemResult(JsonObject Counts, JsonArray Missed, int Found);

    static ItemResult Items(JsonObject key, List<JsonObject> keySteps, List<JsonObject> keyExits, Built? b)
    {
        var steps = b?.Steps ?? [];
        var exits = b?.Exits ?? [];
        var counts = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        var missed = new JsonArray();
        void Tally(string what, bool found, JsonObject item, string? extra = null)
        {
            var (f, o) = counts.GetValueOrDefault(what);
            counts[what] = (f + (found ? 1 : 0), o + 1);
            if (!found) missed.Add(new JsonObject { ["item"] = what, ["at"] = (string?)item["at"], ["code"] = Short((string?)item["code"]), ["note"] = extra });
        }
        foreach (var k in keySteps)
        {
            var at = (string)k["at"]!;
            var code = (string)k["code"]!;
            if (QuotesNothing(code)) { Tally("quotesNothing", true, k); continue; }
            var match = steps.Where(s => Same(at, code, s)).ToList();
            Tally("steps", match.Count > 0, k);
            if (k["end"] is JsonObject end)
            {
                var endKind = (string)end["kind"]!;
                var detail = (string?)end["detail"] ?? "";
                // R1 (D185, tightened 2026-11-09): the code line decides where a table is touched. A key end on a call Stangent
                // walks into counts when Stangent's steps below it reach an end of the same kind, and for `database` the same table
                // the key names. A key `unknown` or `depth` that only says the key didn't follow the call is no claim about the
                // code: where Stangent walks on, it leaves the count (neither found nor missed).
                var direct = match.Any(s => (string?)s["end"]?["kind"] == endKind);
                if (!direct && NotFollowed(endKind, detail) && match.Any(s => Below(steps, (int)s["n"]!).Any()))
                {
                    Tally("endsLeftOut", true, k);
                    goto Conditions;
                }
                // R11 (owner, 2026-11-10): a key `hub` end counts on a step that runs that hub and ends `database` (D172 names the
                // table a hub's own code reads first); a plain table read on the line (no point) doesn't.
                var endFound = direct
                    || (endKind == "hub" && match.Any(s => (string?)s["end"]?["kind"] == "database" && s["point"] is not null))
                    || (endKind == "database" && match.Any(s => Below(steps, (int)s["n"]!).Any(d => (string?)d["end"]?["kind"] == "database" && SameTable(detail, (string?)d["end"]!["detail"] ?? ""))))
                    || (endKind is "outside-call" or "message" or "screen" && match.Any(s => Below(steps, (int)s["n"]!).Any(d => (string?)d["end"]?["kind"] == endKind)));
                Tally("ends", endFound, k, match.Count == 0 ? $"end {endKind}: no step there" : $"end {endKind}: built {string.Join(",", match.Select(s => (string?)s["end"]?["kind"] ?? "none").Distinct())}");
            }
            Conditions:
            foreach (var c in Objects(k["conditions"]))
                Tally("conditions", match.Any(s => Objects(s["conditions"]).Any(bc => (string?)bc["at"] == (string)c["at"]!)), c, $"on step {at}");
        }
        foreach (var k in keyExits)
        {
            var at = (string)k["at"]!;
            // R8 (owner, 2026-11-10): a key item quoting a line with no code on it (`};`) claims nothing the code does: it
            // leaves the count, its conditions with it.
            if (QuotesNothing((string)k["code"]!)) { Tally("quotesNothing", true, k); continue; }
            var match = exits.Where(e => Same(at, (string)k["code"]!, e)).ToList();
            // An exit ends its branch (D174): counted with the ends.
            Tally("ends", match.Count > 0, k, "exit");
            // R10 (owner, 2026-11-10): an exit's condition counts when it's on the exit or on a step on the way to it (its
            // scenario, D179): a check written around the call that leads to a throw is the throw's condition too.
            foreach (var c in Objects(k["conditions"]))
                Tally("conditions", match.Any(e => OnTheWay(e, steps).Any(bc => (string?)bc["at"] == (string)c["at"]!)), c, $"on exit {at}");
        }
        // Links by line and kind, each built link used once.
        var links = (b?.Links ?? []).Select(l => ((string?)l["at"], (string)l["kind"]!)).ToList();
        foreach (var k in Objects(key["links"]))
        {
            var want = ((string?)k["at"], (string)k["kind"]!);
            var i = links.IndexOf(want);
            Tally("links", i >= 0, k, $"link {want.Item2} to {k["to"]}");
            if (i >= 0) links.RemoveAt(i);
        }
        foreach (var k in Objects(key["checks"]))
            Tally("checks", (b?.Checks ?? []).Any(c => Same((string)k["at"]!, (string)k["code"]!, c) || ((string?)c["at"])?.Split(':')[0] == ((string)k["at"]!).Split(':')[0] && Lines((string)k["at"]!, (string)k["code"]!).Contains((string)c["at"]!)), k);
        var found = counts.Where(c => c.Key is "steps" or "ends" or "conditions").Sum(c => c.Value.Item1);
        return new ItemResult(new JsonObject([.. counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => KeyValuePair.Create(c.Key, (JsonNode?)Count(c.Value.Item1, c.Value.Item2)))]), missed, found);
    }

    /// <summary>R10: an exit's own conditions, then those of each step from the one it's in up to the start.</summary>
    static IEnumerable<JsonObject> OnTheWay(JsonObject exit, List<JsonObject> steps)
    {
        foreach (var c in Objects(exit["conditions"])) yield return c;
        var byN = steps.GroupBy(s => (int)s["n"]!).ToDictionary(g => g.Key, g => g.First());
        var seen = new HashSet<int>();
        for (var n = (int?)exit["step"]; n is { } at && seen.Add(at) && byN.TryGetValue(at, out var st); n = (int?)st["from"])
            foreach (var c in Objects(st["conditions"])) yield return c;
    }

    /// <summary>R8: a quoted line holding no letter or digit (`};`, `}`, `)`): punctuation, no code.</summary>
    static bool QuotesNothing(string code) => !code.Any(char.IsLetterOrDigit);

    /// <summary>A key end that only says the key didn't follow the call (R1): a depth stop, or an <c>unknown</c> "not walked" or "same … as".</summary>
    static bool NotFollowed(string kind, string detail) =>
        kind == "depth" || kind == "unknown" && NotFollowedText().IsMatch(detail);

    [GeneratedRegex(@"not walked|^same |same code as|^re-runs ")]
    private static partial Regex NotFollowedText();

    /// <summary>
    /// The key's <c>database</c> detail and a built one name a table in common (R1). The key names tables before any "(": "Series
    /// table", "Volume and Chapter", "Library rows for the user"; a built end lists them before " (": "Series, Episodes (…)".
    /// </summary>
    static bool SameTable(string keyDetail, string builtDetail)
    {
        static string Head(string d) => d.IndexOf('(') is var i and >= 0 ? d[..i] : d;
        var want = TableSplit().Split(Head(keyDetail)).Select(p => p.Trim().Split(' ')[0]).Where(t => t.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in want.ToList()) if (Tables.TryGetValue(name, out var mapped)) want.UnionWith(mapped);
        return Head(builtDetail).Split(',').Select(t => t.Trim()).Any(want.Contains);
    }

    [ThreadStatic] static Dictionary<string, HashSet<string>>? tables;
    static Dictionary<string, HashSet<string>> Tables => tables ?? [];

    static string TypeShort(string name)
    {
        var lt = name.IndexOf('<');
        var head = lt >= 0 ? name[..lt] : name;
        return head[(head.LastIndexOf('.') + 1)..];
    }

    [GeneratedRegex(@",| and | with |/")]
    private static partial Regex TableSplit();

    /// <summary>
    /// D189: a flow with the shared flows its <c>continues</c> links point at merged in, transitively: each shared flow's steps hang
    /// under the hub step, renumbered after the flow's own, its exits and links moved with them. A shared flow reached a second
    /// time isn't merged again; that hub step is <c>sameAs</c> the first.
    /// </summary>
    static Built Expand(Built b, Dictionary<string, Built> byId)
    {
        static JsonObject Copy(JsonObject o) => o.DeepClone().AsObject();
        var steps = b.Steps.Select(Copy).ToList();
        var exits = b.Exits.Select(Copy).ToList();
        var links = b.Links.Select(Copy).ToList();
        var checks = b.Checks.Select(Copy).ToList();
        var byN = steps.ToDictionary(s => (int)s["n"]!);
        var next = steps.Count == 0 ? 0 : byN.Keys.Max();
        var merged = new Dictionary<string, int>(StringComparer.Ordinal);
        var todo = new Queue<JsonObject>(links.Where(Continues));
        while (todo.Count > 0)
        {
            var link = todo.Dequeue();
            var id = (string)link["flow"]!;
            var hub = (int)link["step"]!;
            if (merged.TryGetValue(id, out var first))
            {
                if (first != hub && byN[hub]["sameAs"] is null) byN[hub]["sameAs"] = first;
                continue;
            }
            if (!byId.TryGetValue(id, out var shared)) continue;
            merged[id] = hub;
            var offset = next;
            int Moved(JsonNode? n) => n is null ? hub : (int)n + offset;
            foreach (var st in shared.Steps.Select(Copy))
            {
                st["n"] = (int)st["n"]! + offset;
                st["from"] = Moved(st["from"]);
                if (st["sameAs"] is { } sa) st["sameAs"] = (int)sa + offset;
                if (st["backTo"] is { } bt) st["backTo"] = (int)bt == 0 ? hub : (int)bt + offset;
                steps.Add(st);
                byN[(int)st["n"]!] = st;
                next = Math.Max(next, (int)st["n"]!);
            }
            foreach (var e in shared.Exits.Select(Copy)) { e["step"] = Moved(e["step"]); exits.Add(e); }
            // D203: a guard that rejects the input in shared code the flow runs is one of the flow's checks.
            checks.AddRange(shared.Checks.Select(Copy));
            foreach (var l in shared.Links.Select(Copy))
            {
                l["step"] = Moved(l["step"]);
                links.Add(l);
                if (Continues(l)) todo.Enqueue(l);
            }
        }
        return b with { Steps = steps, Exits = exits, Links = links, Checks = checks };

        static bool Continues(JsonObject l) => (string?)l["kind"] == "continues" && l["flow"] is not null && l["step"] is not null;
    }

    /// <summary>Every step under step <paramref name="n"/>, through <c>from</c>, following <c>sameAs</c> to the steps walked there.</summary>
    static IEnumerable<JsonObject> Below(List<JsonObject> steps, int n)
    {
        var children = steps.Where(s => s["from"] is not null).ToLookup(s => (int)s["from"]!);
        var byN = steps.ToDictionary(s => (int)s["n"]!);
        var seen = new HashSet<int>();
        var stack = new Stack<int>([n]);
        if (byN.TryGetValue(n, out var self) && self["sameAs"] is { } sa) stack.Push((int)sa);
        while (stack.Count > 0)
        {
            var at = stack.Pop();
            if (!seen.Add(at)) continue;
            foreach (var c in children[at])
            {
                yield return c;
                stack.Push((int)c["n"]!);
                if (c["sameAs"] is { } same) stack.Push((int)same);
            }
        }
    }

    /// <summary>The lines a key quote spans (D183: it may run over lines), for a check one rule of a chain sits on.</summary>
    static HashSet<string> Lines(string at, string code)
    {
        var colon = at.LastIndexOf(':');
        var line = int.Parse(at[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        var n = code.Count(c => c == '\n');
        return [.. Enumerable.Range(line, n + 1).Select(l => $"{at[..colon]}:{l}")];
    }

    static string? Short(string? code) => code is null ? null : Norm(code).Length > 120 ? Norm(code)[..120] : Norm(code);

    /// <summary>
    /// Item 1, by code: a ✅ step must sit on a line that does what it says. A call names what it runs on that line; a save, a send
    /// or an outside call sits on a line of code. A step whose line doesn't hold it is a wrong ✅.
    /// </summary>
    static List<string> WrongCertain(string clone, List<Built> built)
    {
        var wrong = new List<string>();
        var cache = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var f in built)
            foreach (var s in f.Steps.Where(s => (string)s["label"]! == "certain"))
            {
                var at = (string)s["at"]!;
                var colon = at.LastIndexOf(':');
                var file = at[..colon];
                var line = int.Parse(at[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture);
                if (!cache.TryGetValue(file, out var lines))
                    cache[file] = lines = File.Exists(Path.Combine(clone, file)) ? File.ReadAllText(Path.Combine(clone, file)).Replace("\r\n", "\n").Split('\n') : [];
                var text = line >= 1 && line <= lines.Length ? string.Join(" ", lines.Skip(line - 1).Take(11)) : "";
                var kind = (string)s["kind"]!;
                var what = (string)s["what"]!;
                var name = kind is "call" or "renders" ? Last(what) : null;
                var ok = text.Trim().Length > 0 && (name is null || text.Contains(name, StringComparison.Ordinal) || ByValue(name, text));
                if (!ok) wrong.Add($"{f.Id} step {s["n"]}: \"{what}\" at {at}: `{Short(lines.Length >= line && line >= 1 ? lines[line - 1] : "")}`");
            }
        return wrong;
    }

    /// <summary>The member a call step names: "calls SeriesService.AddSeries" → AddSeries; "runs X.Y" → Y; a constructor → its type.</summary>
    static string Last(string what)
    {
        var name = what[(what.IndexOf(' ') + 1)..];
        if (name.EndsWith(".ctor", StringComparison.Ordinal)) name = name[..^5];
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    /// <summary>A hook's value called by its own name (G5: <c>addSeries(…)</c> from <c>useAddSeries()</c>).</summary>
    static bool ByValue(string name, string text) =>
        name.StartsWith("use", StringComparison.Ordinal) && name.Length > 3 && text.Contains(name[3..], StringComparison.OrdinalIgnoreCase);
}

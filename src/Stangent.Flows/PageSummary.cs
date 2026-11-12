using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;

namespace Stangent.Flows;

/// <summary>A condition on the way to an item (D251): where it is, its code, its kind, and how the flow's Markdown reads it.</summary>
public sealed record SummaryCondition(string At, string Code, string Kind, string? Case, string Text)
{
    internal string Key => $"{At}|{Code}|{Kind}|{Case}";
}

/// <summary>
/// One numbered item of a section (D251): what a flow does that a reader cares about, with its place in the code, its label
/// (certain ✅, guessed 🟡, unknown ❓) and every condition on the way to it. <see cref="MustCite"/>: the page writer must cite it (D255 item 1).
/// </summary>
public sealed class SummaryItem
{
    public required string Id { get; set; }
    public required string Kind { get; init; }
    public required string Text { get; init; }
    public List<string> Flows { get; } = [];
    public required string At { get; init; }
    public required string Code { get; init; }
    public required string Label { get; init; }
    public string? Reason { get; init; }
    public required IReadOnlyList<SummaryCondition> Conditions { get; init; }
    public bool MustCite => Kind is "who" or "save" or "message" or "outside-call" or "exit" or "check" || Label != "certain";
    public string Mark => Label switch { "guessed" => "🟡", "unknown" => "❓", _ => "✅" };

    /// <summary>Two items with one key are one item (their flows joined). Who is one item per wording, wherever the rule is written.</summary>
    internal string Key => Kind == "who" ? $"who\u0001{Text}\u0001{Label}" : $"{Place}\u0001{string.Join("\u0002", Conditions.Select(c => c.Key))}";
    /// <summary>What and where, without the conditions.</summary>
    internal string Place => $"{Kind}\u0001{Text}\u0001{At}\u0001{Label}";

    public JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["id"] = Id, ["kind"] = Kind, ["text"] = Text, ["at"] = At, ["code"] = Code, ["label"] = Label, ["mustCite"] = MustCite,
            ["flows"] = new JsonArray([.. Flows.Select(f => (JsonNode)f)]),
            ["conditions"] = new JsonArray([.. Conditions.Select(c =>
            {
                var x = new JsonObject { ["at"] = c.At, ["code"] = c.Code, ["kind"] = c.Kind, ["text"] = c.Text };
                if (c.Case is not null) x["case"] = c.Case;
                return (JsonNode)x;
            })]),
        };
        if (Reason is not null) o["reason"] = Reason;
        return o;
    }
}

/// <summary>One section of an area's page (D253): its id (stable across runs), heading, the group it sits under, its flows and items.</summary>
public sealed class SummarySection
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>in-short, screen, clicks, message, jobs.</summary>
    public required string Kind { get; init; }
    /// <summary>The heading it sits under: empty, or "On its own" (handlers and jobs).</summary>
    public string Group { get; init; } = "";
    public List<string> Flows { get; } = [];
    public List<SummaryItem> Items { get; } = [];

    /// <summary>D254: the hash of what the writer reads for this section, before the rules and model are added.</summary>
    public string ItemsHash => CanonicalJson.Hash(CanonicalJson.SerializeCompact(Json()));

    public JsonObject Json() => new()
    {
        ["id"] = Id, ["title"] = Title, ["kind"] = Kind, ["group"] = Group,
        ["flows"] = new JsonArray([.. Flows.Select(f => (JsonNode)f)]),
        ["items"] = new JsonArray([.. Items.Select(i => (JsonNode)i.ToJson())]),
    };
}

/// <summary>An area's summary (D251, D253): its sections in page order, "In short" first.</summary>
public sealed class AreaSummary
{
    public required string Area { get; init; }
    public required string Folder { get; init; }
    public required string Commit { get; init; }
    public List<SummarySection> Sections { get; } = [];
    /// <summary>Flows the summary couldn't read (no file on disk): listed, never skipped silently (D111).</summary>
    public List<string> Missing { get; } = [];

    public SummarySection? Find(string id) => Sections.FirstOrDefault(s => s.Id == id);

    public JsonObject ToJson() => new()
    {
        ["contract"] = PageSummary.Contract, ["version"] = PageSummary.Version, ["commit"] = Commit, ["area"] = Area, ["folder"] = Folder,
        ["sections"] = new JsonArray([.. Sections.Select(s => (JsonNode)s.Json())]),
        ["cantTell"] = new JsonArray([.. Sections.SelectMany(s => s.Items.Where(i => i.Label == "unknown").Select(i => (JsonNode)new JsonObject { ["section"] = s.Id, ["item"] = i.Id }))]),
        ["missing"] = new JsonArray([.. Missing.Select(m => (JsonNode)m)]),
        ["counts"] = Counts(),
    };

    public JsonObject Counts() => new()
    {
        ["sections"] = Sections.Count, ["flows"] = Sections.Where(s => s.Id != PageSummary.InShort).Sum(s => s.Flows.Count), ["items"] = Sections.Sum(s => s.Items.Count),
        ["mustCite"] = Sections.Sum(s => s.Items.Count(i => i.MustCite)),
        ["guessed"] = Sections.Sum(s => s.Items.Count(i => i.Label == "guessed")), ["unknown"] = Sections.Sum(s => s.Items.Count(i => i.Label == "unknown")),
        ["byKind"] = new JsonObject(Sections.SelectMany(s => s.Items).GroupBy(i => i.Kind).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => KeyValuePair.Create(g.Key, (JsonNode?)g.Count()))),
    };
}

/// <summary>
/// Layer 4's summary (D251–D253), by code, no AI: each start flow of an area cut into numbered items (who, start, saves, reads,
/// messages, outside calls, screens opened, what it sets off later, early exits, checks grouped by field, texts it shows, what
/// can't be followed), each with every condition on the way to it, laid out in D253's sections. Read from the flow files
/// (contract <c>flows</c> v1); the same flows and <c>areas.yaml</c> give the same bytes. The page writer words these items and
/// nothing else; the linking judge reads the same items.
/// </summary>
public static class PageSummary
{
    public const string Contract = "pages";
    public const int Version = 1;
    public const string InShort = "in-short";
    public const string OnItsOwn = "On its own";
    static readonly StringComparer O = StringComparer.Ordinal;

    /// <summary>Calls that turn a key into the words shown (D252): a stack's translation function, by its own name, with a quoted key first.</summary>
    static readonly HashSet<string> TranslationCalls = new(["translate", "t", "T", "instant", "GetResource", "GetResourceAsync", "Localize"], O);
    static readonly Regex Key = new(@"\b(?<fn>[A-Za-z_]\w*)\s*\(\s*(?<q>['""])(?<key>[^'""\r\n]+)\k<q>", RegexOptions.CultureInvariant);

    /// <summary>The summary of one area, placed by <paramref name="placement"/> (from <c>areas.yaml</c>, or a proposal for a look before it's confirmed).</summary>
    public static AreaSummary Build(string root, Areas a, Placement placement, string area, string folder)
    {
        var reader = new FlowReader(root, a);
        var summary = new AreaSummary { Area = area, Folder = folder, Commit = a.Commit };
        var starts = placement.AreaOf.Where(x => x.Value == area && a.Flows.ContainsKey(x.Key)).Select(x => a.Flows[x.Key])
            .OrderBy(f => f.Name.ToLowerInvariant(), O).ThenBy(f => f.Id, O).ToList();
        foreach (var f in starts) if (reader.Load(f.Id) is null) summary.Missing.Add(f.Id);
        var readable = starts.Where(f => reader.Load(f.Id) is not null).ToList();
        var translations = new Translations(root, resources: true);

        // Who: said once for the area when every start flow shares it (D251), else per section.
        var whoOf = readable.ToDictionary(f => f.Id, f => Who(reader.Load(f.Id)!), O);
        var shared = whoOf.Values.Select(w => w.Text).Distinct(O).Count() == 1 && whoOf.Count > 0;

        var inShort = new SummarySection { Id = InShort, Title = "In short", Kind = "in-short" };
        summary.Sections.Add(inShort);
        if (shared)
        {
            var w = whoOf.Values.First();
            var item = w.Item();
            item.Flows.AddRange(readable.Select(f => f.Id));
            inShort.Items.Add(item);
            inShort.Flows.AddRange(readable.Select(f => f.Id));
        }

        // Screens, each with the clicks placed on it (D219, D253); clicks on no screen of this area after them.
        var screens = readable.Where(f => f.Kind == "screen-opens").ToList();
        var sections = new List<(SummarySection Section, List<IndexFlow> Flows)>();
        var byPoint = new Dictionary<string, (SummarySection, List<IndexFlow>)>(O);
        foreach (var s in screens)
        {
            var sec = new SummarySection { Id = s.Id, Title = s.Name == "" ? s.Id : s.Name, Kind = "screen" };
            var entry = (sec, new List<IndexFlow> { s });
            sections.Add(entry);
            byPoint.TryAdd(s.Point, entry);
        }
        var loose = new List<IndexFlow>();
        foreach (var c in readable.Where(f => f.Kind == "click"))
        {
            var on = a.ScreensOf(c.Id).Where(byPoint.ContainsKey).ToList();
            var first = sections.FirstOrDefault(x => on.Contains(x.Flows[0].Point));
            if (first.Section is not null) first.Flows.Add(c); else loose.Add(c);
        }
        if (loose.Count > 0) sections.Add((new SummarySection { Id = "clicks", Title = "Clicks on no screen of this area", Kind = "clicks" }, loose));
        // On its own: handlers grouped by the message they answer, then jobs together (D253).
        foreach (var g in readable.Where(f => f.Kind == "handler").GroupBy(f => Message(reader.Load(f.Id)!), O).OrderBy(g => g.Key, O))
        {
            var name = g.Key.Contains('.') && !g.Key.Contains('<') ? g.Key[(g.Key.LastIndexOf('.') + 1)..] : g.Key;
            sections.Add((new SummarySection { Id = $"on-{(FlowBuilder.Slug(g.Key) is { Length: > 0 } s ? s : "message")}", Title = $"When {name} is sent", Kind = "message", Group = OnItsOwn }, [.. g]));
        }
        var jobs = readable.Where(f => f.Kind == "job").ToList();
        if (jobs.Count > 0) sections.Add((new SummarySection { Id = "jobs", Title = "Jobs", Kind = "jobs", Group = OnItsOwn }, jobs));

        foreach (var (sec, flows) in sections)
        {
            var items = new List<SummaryItem>();
            var seen = new Dictionary<string, SummaryItem>(O);
            void Add(SummaryItem item, string flow)
            {
                if (seen.TryGetValue(item.Key, out var had)) { if (!had.Flows.Contains(flow)) had.Flows.Add(flow); return; }
                item.Flows.Add(flow);
                seen[item.Key] = item;
                items.Add(item);
            }
            if (!shared)
                foreach (var f in flows) Add(whoOf[f.Id].Item(), f.Id);
            foreach (var f in flows)
            {
                sec.Flows.Add(f.Id);
                foreach (var item in Cut(reader, reader.Load(f.Id)!, translations)) Add(item, f.Id);
            }
            if (sec.Kind == "screen") FoldClicks(items, flows[0].Id);
            Number(items);
            sec.Items.AddRange(items);
            summary.Sections.Add(sec);
        }
        Number(inShort.Items);
        return summary;
    }

    /// <summary>
    /// A screen's flow walks into the clicks it shows, so it reaches their items again, under the click's conditions and how the
    /// screen shows it (D251: "saves Series" carries "only while the add dialog is open" and "only when Add is pressed"). Such a pair
    /// is one item: the screen's, which holds every condition of the click's, with both flows. Copies on another path stay apart.
    /// </summary>
    static void FoldClicks(List<SummaryItem> items, string screen)
    {
        var fromScreen = items.Where(i => i.Flows.Contains(screen)).ToList();
        foreach (var click in items.Where(i => !i.Flows.Contains(screen) && i.Kind != "who" && i.Kind != "start").ToList())
        {
            var need = click.Conditions.Select(c => c.Key).ToHashSet(O);
            var into = fromScreen.FirstOrDefault(x => x.Place == click.Place && need.IsSubsetOf(x.Conditions.Select(c => c.Key)));
            if (into is null) continue;
            foreach (var f in click.Flows) if (!into.Flows.Contains(f)) into.Flows.Add(f);
            items.Remove(click);
        }
    }

    /// <summary>D251's ids: a letter per kind (W who, T start, A what it does, S shows, U can't be followed, X early exits, K checks), numbered in order.</summary>
    static void Number(List<SummaryItem> items)
    {
        static string Letter(string kind) => kind switch
        {
            "who" => "W", "start" => "T", "exit" => "X", "check" => "K", "shows" => "S", "unknown" => "U", _ => "A",
        };
        static int Rank(string kind) => kind switch { "who" => 0, "start" => 1, "exit" => 3, "check" => 4, _ => 2 };
        var ordered = items.Select((i, n) => (i, n)).OrderBy(x => Rank(x.i.Kind)).ThenBy(x => x.n).Select(x => x.i).ToList();
        var counters = new Dictionary<string, int>(O);
        foreach (var i in ordered)
        {
            var l = Letter(i.Kind);
            counters[l] = counters.GetValueOrDefault(l) + 1;
            i.Id = $"{l}{counters[l]}";
        }
        items.Clear();
        items.AddRange(ordered);
    }

    sealed record WhoInfo(string Text, string Label, string? Reason, string At, string Code)
    {
        public SummaryItem Item() => new() { Id = "", Kind = "who", Text = Text, At = At, Code = Code, Label = Label, Reason = Reason, Conditions = [] };
    }

    /// <summary>D251: the common kinds by code's template; a named access rule as the flow states it, for the writer to word.</summary>
    static WhoInfo Who(JsonObject flow)
    {
        var actor = flow["actor"] as JsonObject ?? new JsonObject();
        var who = (string?)actor["who"] ?? "unknown";
        var at = (string?)actor["at"] ?? "";
        var code = (string?)actor["code"] ?? "";
        var label = (string?)actor["label"] == "guessed" ? "guessed" : "certain";
        var why = (string?)actor["why"];
        return who switch
        {
            "signed-in user" => new("who: anyone signed in", label, label == "certain" ? null : why, at, code),
            "anyone, signed in or not" => new("who: anyone, signed in or not", label, label == "certain" ? null : why, at, code),
            "system" => new("who: the app itself, on its own", label, label == "certain" ? null : why, at, code),
            "unknown" => new("who: not known from the code", "unknown", why ?? "who can start it isn't known", at, code),
            _ => new($"who: {who}", label, label == "certain" ? null : why, at, code),
        };
    }

    static string Message(JsonObject flow)
    {
        var start = flow["start"] as JsonObject;
        var triggers = start?["triggers"] as JsonArray;
        return triggers is { Count: > 0 } ? (string)triggers[0]! : (string?)start?["what"] ?? (string)flow["id"]!;
    }

    /// <summary>One start flow cut into items (D251). Calls that end nowhere (package code, hubs, caches) give none; a save is never dropped, even inside shared code.</summary>
    static List<SummaryItem> Cut(FlowReader reader, JsonObject flow, Translations translations)
    {
        var items = new List<SummaryItem>();
        var id = (string)flow["id"]!;
        var kind = (string)flow["kind"]!;
        var name = (string?)flow["name"] ?? id;
        var start = flow["start"] as JsonObject ?? new JsonObject();
        var triggers = (start["triggers"] as JsonArray)?.Select(t => (string)t!).ToList() ?? [];
        var startText = kind switch
        {
            "click" => $"starts with the click \"{name}\"",
            "screen-opens" => $"opens the screen {name}",
            "job" => $"runs as a job{(triggers.Count > 0 ? $" ({string.Join("; ", triggers)})" : "")}",
            "handler" => $"runs when {Message(flow)} is sent",
            _ => $"starts: {(string?)start["what"] ?? name}",
        };
        items.Add(new SummaryItem { Id = "", Kind = "start", Text = startText, At = (string?)start["at"] ?? "", Code = (string?)start["code"] ?? "", Label = "certain", Conditions = [] });

        var steps = FlowReader.Steps(flow);
        var unknownAt = (flow["unknowns"] as JsonArray ?? []).OfType<JsonObject>().Where(u => u["step"] is not null)
            .GroupBy(u => (int)u["step"]!).ToDictionary(g => g.Key, g => g.First());
        var attached = new HashSet<int>();
        var links = (flow["links"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var guesses = (flow["guesses"] as JsonArray ?? []).OfType<JsonObject>().ToList();

        foreach (var st in steps.Values.OrderBy(s => (int)s["n"]!))
        {
            var n = (int)st["n"]!;
            var stepKind = (string)st["kind"]!;
            var what = (string)st["what"]!;
            string? itemKind = stepKind switch
            {
                "database" => what.StartsWith("reads", StringComparison.Ordinal) ? "read" : "save",
                "outside-call" => "outside-call",
                "message" => "message",
                "queue" => "queue",
                _ => null,
            };
            var text = what;
            string? shows = null;
            if (itemKind is null && stepKind == "call" && Shows(st, translations) is { } s) { itemKind = "shows"; shows = s; }
            if (itemKind is null) continue;
            if (shows is not null) text = shows;
            var label = (string?)st["label"] == "guessed" ? "guessed" : "certain";
            var reason = label == "guessed" ? (string?)st["reason"] : null;
            if (itemKind == "message")
            {
                var handlers = links.Where(l => (string?)l["kind"] == "message" && (int?)l["step"] == n).ToList();
                if (handlers.Count > 0)
                {
                    text += $" → handled by {string.Join(", ", handlers.Select(h => (string)h["to"]!).Distinct(O))}";
                    if (handlers.Any(h => (string?)h["label"] == "guessed"))
                    {
                        label = "guessed";
                        reason ??= guesses.Where(g => (int?)g["step"] == n).Select(g => (string)g["reason"]!).Distinct(O).FirstOrDefault() ?? "its handlers were found by the message's type";
                    }
                }
            }
            if (unknownAt.TryGetValue(n, out var u)) { label = "unknown"; reason = (string)u["why"]!; attached.Add(n); }
            items.Add(new SummaryItem
            {
                Id = "", Kind = itemKind, Text = text, At = (string?)st["at"] ?? "", Code = (string?)st["code"] ?? "", Label = label, Reason = reason,
                Conditions = FlowReader.PathConditions(steps, n),
            });
        }

        var shared = new SaveSet();
        foreach (var l in links)
        {
            var lk = (string?)l["kind"];
            var step = (int?)l["step"];
            var conds = step is { } sn ? FlowReader.PathConditions(steps, sn) : [];
            if (lk is "screen" or "runs-later" && step is { } ls && items.Any(i => i.Kind == "queue" && steps.TryGetValue(ls, out var q) && (string?)q["at"] == i.At)) continue;   // the queue step says it already
            if (lk is "screen" or "runs-later")
            {
                var guessed = (string?)l["label"] == "guessed";
                items.Add(new SummaryItem
                {
                    Id = "", Kind = lk == "screen" ? "screen" : "later", Text = (string)l["sentence"]!, At = (string?)l["at"] ?? "", Code = (string?)l["code"] ?? "",
                    Label = guessed ? "guessed" : "certain", Reason = guessed ? (string?)l["reason"] ?? (step is { } gs ? guesses.Where(g => (int?)g["step"] == gs).Select(g => (string)g["reason"]!).FirstOrDefault() : null) ?? "a guessed link" : null,
                    Conditions = conds,
                });
            }
            else if (lk == "continues" && (string?)l["flow"] is { } target)
                foreach (var save in reader.SharedSaves(target)) shared.Add(save with { Conditions = Merge(conds, save.Conditions) });
        }
        foreach (var save in shared.List)
            items.Add(new SummaryItem { Id = "", Kind = "save", Text = save.Text, At = save.At, Code = save.Code, Label = save.Label, Reason = save.Reason, Conditions = save.Conditions });

        foreach (var u in (flow["unknowns"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var step = (int?)u["step"];
            if (step is { } sn && attached.Contains(sn)) continue;
            items.Add(new SummaryItem
            {
                Id = "", Kind = "unknown", Text = $"can't be followed: {(string)u["why"]!}", At = (string?)u["at"] ?? "", Code = (string?)u["code"] ?? "",
                Label = "unknown", Reason = (string)u["why"]!, Conditions = step is { } s ? FlowReader.PathConditions(steps, s) : [],
            });
        }

        foreach (var e in (flow["exits"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var step = (int?)e["step"];
            var conds = Merge(step is { } sn ? FlowReader.PathConditions(steps, sn) : [], FlowReader.Conditions(e["conditions"] as JsonArray));
            items.Add(new SummaryItem { Id = "", Kind = "exit", Text = $"stops: {(string)e["what"]!}", At = (string?)e["at"] ?? "", Code = (string?)e["code"] ?? "", Label = "certain", Conditions = conds });
        }

        // Checks grouped by the field they check (D251): "SeriesResource.TvdbId: GreaterThan(0), SeriesExistsValidator".
        var checks = (flow["checks"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        foreach (var g in checks.GroupBy(c => Field(c), O))
        {
            var rules = g.Select(c =>
            {
                var rule = (string)c["what"]! is var w && w.IndexOf(": ", StringComparison.Ordinal) is var i and >= 0 ? w[(i + 2)..] : (string)c["rule"]!;
                var msg = (string?)c["message"] ?? "";
                return $"{rule}{(msg != "" ? $" \"{msg}\"" : "")}{((bool?)c["onlyWarns"] == true ? " (only warns)" : "")}";
            }).Distinct(O).ToList();
            var first = g.First();
            items.Add(new SummaryItem { Id = "", Kind = "check", Text = $"checks {g.Key}: {string.Join(", ", rules)}", At = (string?)first["at"] ?? "", Code = (string?)first["code"] ?? "", Label = "certain", Conditions = [] });
        }
        return items;
    }

    static string Field(JsonObject check)
    {
        var what = (string)check["what"]!;
        var i = what.IndexOf(": ", StringComparison.Ordinal);
        return Short(i >= 0 ? what[..i] : (string?)check["property"] ?? what);
    }

    /// <summary>A member's id as a person reads it: <c>Sonarr.Core|M:NzbDrone.Core.Tv.AddSeriesService.SetPropertiesAndValidate(…)</c> → <c>AddSeriesService.SetPropertiesAndValidate</c>.</summary>
    static string Short(string id)
    {
        var bar = id.IndexOf('|');
        if (bar < 0 || id.Length < bar + 3 || id[bar + 2] != ':') return id;
        var name = id[(bar + 3)..];
        var paren = name.IndexOf('(');
        if (paren >= 0) name = name[..paren];
        var parts = name.Split('.');
        return string.Join('.', parts.TakeLast(2)).Replace("`1", "").Replace("`2", "");
    }

    /// <summary>D252: a translation call with a quoted key, turned into its English text through the translation files D228 reads.</summary>
    static string? Shows(JsonObject step, Translations translations)
    {
        var what = (string)step["what"]!;
        if (!what.StartsWith("calls ", StringComparison.Ordinal)) return null;
        var callee = what["calls ".Length..].Split(' ')[0];
        var fn = callee.Contains('.') ? callee[(callee.LastIndexOf('.') + 1)..] : callee;
        if (!TranslationCalls.Contains(fn)) return null;
        var code = (string?)step["code"] ?? "";
        foreach (Match m in Key.Matches(code))
        {
            if (m.Groups["fn"].Value != fn) continue;
            var key = m.Groups["key"].Value;
            return translations.Text(key) is { } text ? $"shows \"{text}\" (text key '{key}')" : $"shows the text with key '{key}' (no English text found for it)";
        }
        return null;
    }

    internal static List<SummaryCondition> Merge(IEnumerable<SummaryCondition> outer, IEnumerable<SummaryCondition> inner)
    {
        var list = new List<SummaryCondition>();
        var seen = new HashSet<string>(O);
        foreach (var c in outer.Concat(inner)) if (seen.Add(c.Key)) list.Add(c);
        return list;
    }

    // ---- Markdown -----------------------------------------------------------------------------------------------------------

    /// <summary>One item as a line: id, label, what, where, and its conditions (the writer's input and the code-made list).</summary>
    public static string ItemLine(SummaryItem i, bool withId)
    {
        var sb = new StringBuilder("- ");
        if (withId) sb.Append($"**{i.Id}** ");
        sb.Append($"{i.Mark} {i.Text}");
        if (i.Reason is not null && i.Label != "certain") sb.Append($" ({(i.Label == "guessed" ? "guessed" : "not known")}: {i.Reason})");
        if (i.At != "") sb.Append($" (`{i.At}`)");
        sb.Append('\n');
        for (var n = 0; n < i.Conditions.Count; n++) sb.Append($"  - condition {n + 1}: {i.Conditions[n].Text} (`{i.Conditions[n].At}`)\n");
        return sb.ToString();
    }

    /// <summary>The whole summary as Markdown, for a person to read (<c>pages summary --format md</c>).</summary>
    public static string Markdown(AreaSummary s)
    {
        var sb = new StringBuilder($"# {s.Area}: the summary layer 4 words (D251)\n\n");
        string? group = null;
        foreach (var sec in s.Sections)
        {
            if (sec.Group != "" && sec.Group != group) sb.Append($"## {sec.Group}\n\n");
            group = sec.Group;
            sb.Append($"{(sec.Group != "" ? "###" : "##")} {sec.Title} (`{sec.Id}`)\n\n");
            if (sec.Items.Count == 0) sb.Append("(no items of its own)\n\n");
            foreach (var i in sec.Items) sb.Append(ItemLine(i, true));
            if (sec.Items.Count > 0) sb.Append('\n');
        }
        if (s.Missing.Count > 0) sb.Append($"**Flows with no file, left out:** {string.Join(", ", s.Missing)}\n");
        return sb.ToString();
    }
}

/// <summary>Reads flow files (contract <c>flows</c> v1) by id through <c>specs/flows/index.json</c>'s <c>file</c>, each once.</summary>
sealed class FlowReader(string root, Areas a)
{
    readonly Dictionary<string, JsonObject?> cache = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<SharedSave>> saves = new(StringComparer.Ordinal);

    public JsonObject? Load(string id)
    {
        if (cache.TryGetValue(id, out var have)) return have;
        var rel = a.Flows.TryGetValue(id, out var f) && f.File is not null ? f.File : $"{FlowFiles.Folder}/{id}.json";
        var path = Path.Combine(root, rel);
        JsonObject? json = null;
        if (File.Exists(path))
            try { json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; } catch (System.Text.Json.JsonException) { }
        return cache[id] = json;
    }

    public static Dictionary<int, JsonObject> Steps(JsonObject flow) =>
        (flow["steps"] as JsonArray ?? []).OfType<JsonObject>().GroupBy(s => (int)s["n"]!).ToDictionary(g => g.Key, g => g.First());

    public static List<SummaryCondition> Conditions(JsonArray? cs) => [.. (cs ?? []).OfType<JsonObject>().Select(c =>
    {
        var at = (string?)c["at"] ?? "";
        var colon = at.LastIndexOf(':');
        var cond = new Condition((string)c["kind"]!, (string)c["code"]!, colon > 0 ? at[..colon] : at, colon > 0 && int.TryParse(at[(colon + 1)..], out var line) ? line : 0, (string?)c["case"]);
        return new SummaryCondition(at, cond.Code, cond.Kind, cond.Case, FlowFiles.ConditionText(cond));
    })];

    /// <summary>Every condition from the flow's start down to step <paramref name="n"/>: the outermost first (D251).</summary>
    public static List<SummaryCondition> PathConditions(Dictionary<int, JsonObject> steps, int n)
    {
        var chain = new List<JsonObject>();
        var seen = new HashSet<int>();
        for (int? at = n; at is { } k && steps.TryGetValue(k, out var st) && seen.Add(k); at = (int?)st["from"]) chain.Add(st);
        chain.Reverse();
        return PageSummary.Merge([], chain.SelectMany(s => Conditions(s["conditions"] as JsonArray)));
    }

    /// <summary>
    /// The saves a shared flow makes, itself or through the shared flows it continues into: each save once, by its text, line and
    /// label, with only the conditions on every path to it (a save reached along many paths would otherwise be many items).
    /// </summary>
    public IReadOnlyList<SharedSave> SharedSaves(string id) => SharedSaves(id, []);

    List<SharedSave> SharedSaves(string id, HashSet<string> walking)
    {
        if (saves.TryGetValue(id, out var have)) return have;
        if (!walking.Add(id) || Load(id) is not { } flow) return [];
        var found = new SaveSet();
        var name = (string?)flow["name"] ?? id;
        var steps = Steps(flow);
        foreach (var st in steps.Values.OrderBy(s => (int)s["n"]!))
        {
            if ((string?)st["kind"] != "database" || ((string)st["what"]!).StartsWith("reads", StringComparison.Ordinal)) continue;
            var guessed = (string?)st["label"] == "guessed";
            found.Add(new SharedSave($"{(string)st["what"]!} (in shared code {name})", (string?)st["at"] ?? "", (string?)st["code"] ?? "", guessed ? "guessed" : "certain",
                guessed ? (string?)st["reason"] : null, PathConditions(steps, (int)st["n"]!)));
        }
        foreach (var l in (flow["links"] as JsonArray ?? []).OfType<JsonObject>())
            if ((string?)l["kind"] == "continues" && (string?)l["flow"] is { } target && target != id)
            {
                var outer = (int?)l["step"] is { } sn ? PathConditions(steps, sn) : [];
                foreach (var s in SharedSaves(target, walking)) found.Add(s with { Conditions = PageSummary.Merge(outer, s.Conditions) });
            }
        walking.Remove(id);
        return saves[id] = found.List;
    }
}

/// <summary>A save inside shared code, with the conditions on every path to it.</summary>
public sealed record SharedSave(string Text, string At, string Code, string Label, string? Reason, List<SummaryCondition> Conditions)
{
    internal string Key => $"{Text}\u0001{At}\u0001{Label}";
}

/// <summary>Saves by text, line and label, in the order first found; a save found again keeps only the conditions both paths share.</summary>
sealed class SaveSet
{
    readonly Dictionary<string, int> at = new(StringComparer.Ordinal);
    public List<SharedSave> List { get; } = [];

    public void Add(SharedSave s)
    {
        if (!at.TryGetValue(s.Key, out var i)) { at[s.Key] = List.Count; List.Add(s); return; }
        var keep = s.Conditions.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        List[i] = List[i] with { Conditions = [.. List[i].Conditions.Where(c => keep.Contains(c.Key))] };
    }
}

using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>
/// D403: the review's "left out" comes from a list code makes. Every screen in scope and every control on it
/// (<c>specs/inventory.json</c>'s <c>screens[].lines</c>: buttons, fields, menu entries), each with an id; code ticks off an
/// item whose name the FSD already says word for word; the reviewer answers the rest, covered (a quote, checked) or left
/// out; an item it skips is "not checked". A left-out item is an undocumented finding like any other.
/// </summary>
public static partial class FsdReview
{
    public const string ChecklistJson = "checklist.json";
    public const string ChecklistFile = "checklist.md";
    public static readonly string[] Outcomes = ["code", "covered", "left-out", "not-checked"];

    /// <summary>A screen in scope: its route, its title as the inventory gives it, and its controls.</summary>
    public sealed record Screen(string Route, string? Title, List<JsonObject> Lines);

    /// <summary>The inventory's screens under <c>fsd.scope</c> (D400's rule, kept from its patch): a route equal to a prefix
    /// or under it; every screen when the scope is empty or <c>/</c>. One screen per route. Null with no inventory.</summary>
    public static List<Screen>? ScreensInScope(string root, IReadOnlyList<string> scope, out string? inventoryCommit)
    {
        inventoryCommit = null;
        if (PageRun.LoadJson(Path.Combine(root, Stangent.Flows.InventoryFiles.JsonFile)) is not JsonObject inventory) return null;
        inventoryCommit = (string?)inventory["commit"];
        var prefixes = scope.Select(RouteKey).ToList();
        var all = prefixes.Count == 0 || prefixes.Contains("/");
        return [.. (inventory["screens"] as JsonArray ?? []).OfType<JsonObject>()
            // A screen id is "page:<app>|<route>": the route is everything after the first bar.
            .Select(s => (string?)s["id"] is { } id && id.IndexOf('|') is var bar and >= 0
                ? new Screen(id[(bar + 1)..].Trim(), (string?)s["title"], [.. (s["lines"] as JsonArray ?? []).OfType<JsonObject>()]) : null)
            .OfType<Screen>()
            .Where(s => s.Route.StartsWith('/') && (all || prefixes.Any(p => RouteKey(s.Route) == p || RouteKey(s.Route).StartsWith(p + "/", StringComparison.Ordinal))))
            // Two apps with one route are one screen to the FSD: every control of both is kept.
            .GroupBy(s => RouteKey(s.Route)).Select(g => g.First() with { Lines = [.. g.SelectMany(s => s.Lines)] })
            .OrderBy(s => s.Route, StringComparer.Ordinal)];
    }

    /// <summary>A route as compared: no trailing slash, case folded (the merge's rule for screens).</summary>
    static string RouteKey(string route) => route.Trim().TrimEnd('/').ToLowerInvariant() is { Length: > 0 } r ? r : "/";

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    [GeneratedRegex(@"\s*\([^()]*\)\s*$")]
    private static partial Regex TrailingRoute();

    /// <summary>The checklist for one run: <c>{inventoryCommit, items}</c>, each item <c>{id, screen, kind, label, code, at,
    /// coveredBy}</c> (<c>coveredBy</c>: the section whose text names it, when code ticked it off). Null with no inventory.</summary>
    public static JsonObject? BuildChecklist(string root, IReadOnlyList<string> scope, JsonObject document)
    {
        if (ScreensInScope(root, scope, out var commit) is not { } screens) return null;
        // Each section's own text, in document order: the first that names an item is where code says it is covered.
        var texts = Sections(document).Select(s => ((string)s["id"]!, Fold(((string)s["title"]!) + "\n" + (string)s["text"]!))).ToList();
        var items = new JsonArray();
        for (var i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            var sid = $"S{i + 1:D2}";
            var title = ScreenTitle(screen);
            items.Add(Item(sid, screen.Route, "screen", title, null, null, Named(texts, screen.Route, isRoute: true) ?? (title is null ? null : Named(texts, title, isRoute: false))));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var n = 0;
            foreach (var line in screen.Lines)
            {
                var kind = (string?)line["kind"] ?? "control";
                var at = (string?)line["at"];
                var named = (bool?)line["named"] == true;
                var label = named ? ((string?)line["name"])?.Trim() : null;
                if (label is { Length: 0 }) label = null;
                if (!seen.Add($"{kind}\u0001{label ?? (string?)line["code"]}\u0001{at}")) continue;
                n++;
                items.Add(Item($"{sid}.{n:D3}", screen.Route, kind, label, (string?)line["code"], at, label is null ? null : Named(texts, label, isRoute: false)));
            }
        }
        return new JsonObject { ["inventoryCommit"] = commit, ["items"] = items };
    }

    static JsonObject Item(string id, string screen, string kind, string? label, string? code, string? at, string? coveredBy) => new()
    {
        ["id"] = id, ["screen"] = screen, ["kind"] = kind, ["label"] = label, ["code"] = code, ["at"] = at, ["coveredBy"] = coveredBy,
    };

    /// <summary>A screen's title worth searching for: the inventory's title without its "(route)", none when it is a
    /// placeholder ("❓ …", "Loading…", "{{num}} Items").</summary>
    static string? ScreenTitle(Screen s)
    {
        var t = TrailingRoute().Replace(s.Title ?? "", "").Trim();
        return t.Length == 0 || t.StartsWith('❓') || t.Contains("{{", StringComparison.Ordinal) || t.Contains("...", StringComparison.Ordinal) || t.Contains('…') ? null : t;
    }

    /// <summary>D403 (2), my call: the first section whose own text names it word for word (case folded, emphasis, table
    /// bars and list marks ignored, whole words). Only a name of two words or more counts, or a route: a one-word label
    /// ("Save", "All", "Edit") is in almost any FSD, so it says nothing about this control and goes to the reviewer.</summary>
    static string? Named(List<(string Id, string Text)> sections, string name, bool isRoute)
    {
        var q = Quotes.Normalize(Prep(name, cells: true, marks: false)).ToLowerInvariant();
        // The root route "/" is any stray slash in prose: never ticked by code.
        if (q.Length == 0 || (!isRoute && Word().Matches(q).Count < 2) || (isRoute && Word().Matches(q).Count == 0)) return null;
        foreach (var (id, text) in sections)
        {
            var t = Quotes.Normalize(Prep(text, cells: true, marks: true)).ToLowerInvariant();
            if (isRoute ? RouteIn(q, t) : WholeWords(q, t)) return id;
        }
        return null;
    }

    /// <summary>A route in the text with nothing route-like on after it: <c>/library</c> isn't named by <c>/library/:id</c>.</summary>
    static bool RouteIn(string route, string text)
    {
        for (var at = text.IndexOf(route, StringComparison.Ordinal); at >= 0; at = text.IndexOf(route, at + 1, StringComparison.Ordinal))
        {
            var end = at + route.Length;
            var before = at == 0 ? ' ' : text[at - 1];
            var after = end == text.Length ? ' ' : text[end];
            if (!char.IsLetterOrDigit(before) && before is not '/' and not '-' && !char.IsLetterOrDigit(after) && after is not '/' and not ':' and not '-') return true;
        }
        return false;
    }

    /// <summary>What the reviewer reads (<see cref="ChecklistFile"/>): the items code didn't tick off, by screen.</summary>
    public static string ChecklistMarkdown(JsonObject checklist)
    {
        var items = checklist["items"]!.AsArray().OfType<JsonObject>().ToList();
        var ticked = items.Count(i => i["coveredBy"] is not null);
        var sb = new StringBuilder("# Checklist: every screen in scope and every control on it (made by code from the app's source)\n\n");
        sb.Append($"{items.Count} items. {ticked} of them are not listed here: code already found their names word for word in the FSD.\n");
        sb.Append("Each line below is one item: its id, what it is (screen, button, field or menu entry), its label as the app shows it ");
        sb.Append("(or, with no label, its name in the code), and where it is in the code.\n");
        foreach (var screen in items.GroupBy(i => (string)i["screen"]!))
        {
            var open = screen.Where(i => i["coveredBy"] is null).ToList();
            if (open.Count == 0) continue;
            sb.Append($"\n## Screen `{screen.Key}`\n\n");
            foreach (var i in open)
            {
                var kind = (string)i["kind"]!;
                var what = kind == "screen" ? $"the whole screen `{(string)i["screen"]!}`" + ((string?)i["label"] is { } t ? $" (“{t}”)" : "")
                    : (string?)i["label"] is { } label ? $"“{label}”" : $"(no label; in the code: `{(string?)i["code"] ?? "?"}`)";
                sb.Append($"- {(string)i["id"]!} · {kind} · {what}");
                if ((string?)i["at"] is { } at) sb.Append($" · `{at}`");
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>The reviewer's checklist answers checked (D403 (3), (4)): errors for the retry, and each item's outcome.
    /// Items code ticked off are <c>code</c>; a valid covered answer <c>covered</c>; left out, or a covered answer whose quote
    /// fails, <c>left-out</c> (a finding); unanswered <c>not-checked</c>.</summary>
    sealed class ChecklistChecker(JsonObject checklist, Dictionary<string, string> sectionText, Func<string, string?> evidenceProblem)
    {
        readonly List<JsonObject> items = [.. checklist["items"]!.AsArray().OfType<JsonObject>()];

        public List<string> Errors(JsonArray? answers)
        {
            var errors = new List<string>();
            var byId = items.ToDictionary(i => (string)i["id"]!, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in (answers ?? []).OfType<JsonObject>())
            {
                var id = (Str(a["id"]))?.Trim() ?? "";
                if (!byId.TryGetValue(id, out var item)) { errors.Add($"checklist: there's no item \"{Short(id)}\" in the checklist"); continue; }
                if (!seen.Add(id)) { errors.Add($"checklist: {id} is answered twice; answer each item once"); continue; }
                if (item["coveredBy"] is not null) continue;
                errors.AddRange(Problems(item, a).Select(p => $"checklist: {id}: {p}"));
            }
            var missing = items.Where(i => i["coveredBy"] is null && !seen.Contains((string)i["id"]!)).Select(i => (string)i["id"]!).ToList();
            if (missing.Count > 0)
                errors.Add($"checklist: {missing.Count} items not answered: {string.Join(", ", missing.Take(40))}{(missing.Count > 40 ? ", …" : "")}");
            return errors;
        }

        /// <summary>One answer's problems; none means it stands as given.</summary>
        List<string> Problems(JsonObject item, JsonObject a)
        {
            var problems = new List<string>();
            var section = Blank(Str(a["section"]));
            if (section is not null && !sectionText.ContainsKey(section))
                problems.Add($"there's no section \"{Short(section)}\" in the FSD; give the id exactly as in its heading's brackets");
            switch (Str(a["answer"]))
            {
                case "covered":
                    var quote = Blank(Str(a["fsdQuote"]));
                    if (section is null) problems.Add("a covered item names the FSD section that describes it");
                    if (quote is null) problems.Add("a covered item quotes the FSD sentence that describes it");
                    else if (section is not null && sectionText.TryGetValue(section, out var text) && !Quoted(quote, text))
                        problems.Add($"the fsdQuote \"{Short(quote)}\" isn't word for word in section {section} (copy whole sentences, at least 4 words, or a whole line)");
                    break;
                case "left-out":
                    var evidence = Evidence(a);
                    foreach (var e in evidence) if (evidenceProblem(e) is { } p) problems.Add(p);
                    if ((string)item["kind"]! == "screen" && !evidence.Any(e => PathLine().IsMatch(e)))
                        problems.Add("a left-out screen gives the line that defines its route (path:line) in evidence");
                    break;
                default:
                    problems.Add($"answer \"{Short(Str(a["answer"]) ?? "")}\" isn't covered or left-out");
                    break;
            }
            return problems;
        }

        /// <summary>A string, or null for anything else: a number where an id should be is an answer not checked, not a crash.</summary>
        static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        static List<string> Evidence(JsonObject a) => [.. (a["evidence"] as JsonArray ?? []).Select(e => Str(e)?.Trim() ?? "").Where(e => e.Length > 0)];

        /// <summary>The run's outcome for every item, and the left-out ones as undocumented findings. Called after the
        /// retry (or when nothing failed): what still fails is settled here, never sent back.</summary>
        public (JsonArray Items, List<JsonObject> Findings) Settle(JsonArray? answers)
        {
            var first = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var a in (answers ?? []).OfType<JsonObject>())
                if ((Str(a["id"]))?.Trim() is { } id) first.TryAdd(id, a);
            var outcomes = new JsonArray();
            var findings = new List<JsonObject>();
            foreach (var item in items)
            {
                var id = (string)item["id"]!;
                var o = (JsonObject)item.DeepClone();
                o.Remove("coveredBy");
                if (item["coveredBy"] is not null)
                {
                    o["result"] = "code";
                    o["section"] = (string)item["coveredBy"]!;
                }
                else if (!first.TryGetValue(id, out var a))
                    o["result"] = "not-checked";
                else
                {
                    var problems = Problems(item, a);
                    var section = Blank(Str(a["section"])) is { } s && sectionText.ContainsKey(s) ? s : null;
                    if (Str(a["answer"]) == "covered" && problems.Count == 0)
                    {
                        o["result"] = "covered";
                        o["section"] = section;
                        o["fsdQuote"] = Blank(Str(a["fsdQuote"]));
                    }
                    else if (Str(a["answer"]) is "covered" or "left-out")
                    {
                        // A covered answer whose quote fails counts as left out (D403 (4)); a left-out answer keeps the
                        // evidence that passes. Either way the reasons are kept beside it.
                        o["result"] = "left-out";
                        o["section"] = section;
                        if (problems.Count > 0) o["why"] = new JsonArray([.. problems.Select(p => (JsonNode)p)]);
                        var evidence = new List<string>();
                        if ((string?)item["at"] is { } at) evidence.Add(at);
                        if (Str(a["answer"]) == "left-out")
                            foreach (var e in Evidence(a)) if (evidenceProblem(e) is null && !evidence.Contains(e)) evidence.Add(e);
                        var agentNote = Str(a["answer"]) == "left-out" ? Blank(Str(a["note"])) : null;
                        findings.Add(new JsonObject
                        {
                            ["state"] = "undocumented", ["section"] = section, ["fsdQuote"] = null, ["screen"] = (string)item["screen"]!,
                            ["note"] = LeftOutNote(item) + (agentNote is null ? "" : " " + agentNote),
                            ["evidence"] = new JsonArray([.. evidence.Select(e => (JsonNode)e)]),
                            ["item"] = id,
                        });
                    }
                    else
                    {
                        o["result"] = "not-checked";
                        o["why"] = new JsonArray([.. problems.Select(p => (JsonNode)p)]);
                    }
                }
                outcomes.Add(o);
            }
            return (outcomes, findings);
        }
    }

    /// <summary>Code's words for a left-out item, naming it as the app does, so the finding says what it is whatever the
    /// agent adds.</summary>
    static string LeftOutNote(JsonObject item)
    {
        var kind = (string)item["kind"]!;
        var screen = (string)item["screen"]!;
        if (kind == "screen") return $"The FSD doesn't describe the screen {screen}" + ((string?)item["label"] is { } t ? $" (“{t}”)." : ".");
        var name = (string?)item["label"] is { } label ? $"“{label}”" : $"with no label (`{(string?)item["code"] ?? "?"}` in the code)";
        return $"The FSD doesn't mention the {kind.Replace('-', ' ')} {name} on {screen}.";
    }
    /// <summary>D405: matches back, from the checklist. An item a run covered with a checked quote, and no run left out, is a
    /// claim that the code does what that sentence says: a match finding per covering run (joined by item in the merge), for
    /// the PM to confirm or answer no (D402 (2)). Code checked the quote and the line, not that the code does it.</summary>
    static Dictionary<int, List<JsonObject>> ChecklistMatches(List<JsonObject> runs)
    {
        var lists = runs.Where(r => r["checklist"] is JsonArray).Select(r => ((int)r["run"]!, r["checklist"]!.AsArray().OfType<JsonObject>().ToList())).ToList();
        var leftOut = lists.SelectMany(l => l.Item2).Where(i => (string?)i["result"] == "left-out").Select(i => (string)i["id"]!).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<int, List<JsonObject>>();
        foreach (var (run, items) in lists)
            result[run] = [.. items
                .Where(i => (string?)i["result"] == "covered" && !leftOut.Contains((string)i["id"]!)
                    && (string?)i["section"] is not null && (string?)i["fsdQuote"] is not null && (string?)i["at"] is not null)
                .Select(i => new JsonObject
                {
                    ["state"] = "match", ["section"] = (string)i["section"]!, ["fsdQuote"] = (string)i["fsdQuote"]!, ["screen"] = (string)i["screen"]!,
                    ["note"] = MatchNote(i), ["evidence"] = new JsonArray((JsonNode)(string)i["at"]!), ["item"] = (string)i["id"]!,
                })];
        return result;
    }

    static string MatchNote(JsonObject item)
    {
        var kind = (string)item["kind"]!;
        var screen = (string)item["screen"]!;
        var name = kind == "screen" ? $"the screen {screen}" : $"the {kind.Replace('-', ' ')} " + ((string?)item["label"] is { } label ? $"“{label}”" : $"`{(string?)item["code"] ?? "?"}`") + $" on {screen}";
        return $"The checklist: the FSD describes {name} here (its quote checked by code); that the code does what the sentence says is the PM's to confirm.";
    }

    /// <summary>The runs' checklists as one (D403 (5)): an item is left out when any run left it out (its finding, unioned as
    /// findings are), else covered when a run covered it with a checked quote, else covered by code, else not checked. Each
    /// item keeps every run's outcome, so a disagreement shows. Null when a run had no checklist.</summary>
    static JsonObject? MergeChecklist(string root, string doc, List<JsonObject> runs, JsonArray findings)
    {
        if (runs.Count == 0 || runs.All(r => r["checklist"] is not JsonArray)) return null;
        // Some runs with a checklist and some without (one prepared before D403, or with the inventory gone): their
        // left-out items would have no list to sit in, so the older runs are prepared again.
        if (runs.Any(r => r["checklist"] is not JsonArray))
            throw new StangentException("runs_differ", $"Some review runs of {doc} had code's checklist and some didn't (prepared before D403, or the inventory was missing); prepare them all again with --again.", ExitCodes.Usage);
        static string Shape(JsonObject r) => string.Join("\n", r["checklist"]!.AsArray().OfType<JsonObject>().Select(i => $"{(string)i["id"]!}\u0001{(string?)i["at"]}\u0001{(string?)i["label"]}"));
        if (runs.Select(Shape).Distinct().Count() > 1)
            throw new StangentException("runs_differ", $"The review runs of {doc} were given different checklists (the inventory changed between them); prepare them again with --again.", ExitCodes.Usage);
        var inventoryCommit = (string?)(PageRun.LoadJson(Path.Combine(ListDir(root, doc, (int)runs[0]["run"]!), ChecklistJson)) as JsonObject)?["inventoryCommit"];
        var byItem = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in findings.OfType<JsonObject>()) if ((string?)f["item"] is { } item) byItem.TryAdd(item, (string)f["id"]!);
        var lists = runs.Select(r => r["checklist"]!.AsArray().OfType<JsonObject>().ToDictionary(i => (string)i["id"]!, StringComparer.Ordinal)).ToList();
        var items = new JsonArray();
        foreach (var first in runs[0]["checklist"]!.AsArray().OfType<JsonObject>())
        {
            var id = (string)first["id"]!;
            var each = lists.Select(l => l.GetValueOrDefault(id)).ToList();
            var outcomes = each.Select(x => (string?)x?["result"] ?? "not-checked").ToList();
            var outcome = outcomes.Contains("left-out") ? "left-out" : outcomes.Contains("covered") ? "covered" : outcomes.Contains("code") ? "code" : "not-checked";
            var pick = each.First(x => ((string?)x?["result"] ?? "not-checked") == outcome);
            items.Add(new JsonObject
            {
                ["id"] = id, ["screen"] = (string)first["screen"]!, ["kind"] = (string)first["kind"]!, ["label"] = (string?)first["label"],
                ["code"] = (string?)first["code"], ["at"] = (string?)first["at"], ["result"] = outcome,
                ["section"] = (string?)pick?["section"], ["fsdQuote"] = outcome == "covered" ? (string?)pick?["fsdQuote"] : null,
                ["byRun"] = new JsonArray([.. outcomes.Select(x => (JsonNode)x)]),
                ["finding"] = byItem.GetValueOrDefault(id),
            });
        }
        int Count(string o) => items.Count(i => (string)i!["result"]! == o);
        return new JsonObject
        {
            ["inventoryCommit"] = inventoryCommit, ["listed"] = items.Count, ["coveredByCode"] = Count("code"), ["coveredByAgent"] = Count("covered"),
            ["leftOut"] = Count("left-out"), ["notChecked"] = Count("not-checked"), ["items"] = items,
        };
    }

    /// <summary>review.md's coverage line (D403 (5)).</summary>
    static string ChecklistLine(JsonObject? c, string commit) => c is null
        ? "- No checklist: there was no `specs/inventory.json` (run `stangent inventory`), so code didn't list the screens and controls in scope and \"left out\" is only what the AI noticed\n"
        : $"- Checklist (D403): {(int)c["listed"]!} screens and controls in scope listed by code; {(int)c["coveredByCode"]!} covered by code (their name is in the FSD word for word), "
          + $"{(int)c["coveredByAgent"]!} covered by the AI (its FSD quote checked), {(int)c["leftOut"]!} left out, {(int)c["notChecked"]!} not checked"
          + ((string?)c["inventoryCommit"] is { } at && at != commit ? $"; the inventory was built at `{at}`, not the reviewed commit" : "") + "\n";

    /// <summary>review.md's "Left out" list by screen and its "Not checked" list (D403 (5)).</summary>
    static void ChecklistReport(StringBuilder sb, JsonObject? c, Dictionary<string, JsonObject> findings, int runCount)
    {
        if (c is null) return;
        var items = c["items"]!.AsArray().OfType<JsonObject>().ToList();
        string What(JsonObject i) => (string)i["kind"]! == "screen" ? "the whole screen" + ((string?)i["label"] is { } t ? $" “{t}”" : "")
            : $"{((string)i["kind"]!).Replace('-', ' ')} " + ((string?)i["label"] is { } l ? $"“{l}”" : $"with no label (`{(string?)i["code"] ?? "?"}`)");
        var left = items.Where(i => (string)i["result"]! == "left-out").ToList();
        if (left.Count > 0)
        {
            sb.Append("## Left out, by screen (the checklist)\n\nScreens and controls the code has that the FSD doesn't describe, from code's list; each is a finding to rule keep or remove.\n\n");
            foreach (var screen in left.GroupBy(i => (string)i["screen"]!))
            {
                sb.Append($"### `{screen.Key}`\n\n");
                foreach (var i in screen)
                {
                    var f = (string?)i["finding"] is { } fid ? findings.GetValueOrDefault(fid) : null;
                    sb.Append(f is null ? "- " : $"- **{(string)f["id"]!}** ").Append(What(i));
                    if ((string?)i["at"] is { } at) sb.Append($" `{at}`");
                    var runs = i["byRun"]!.AsArray().Select(x => (string)x!).ToList();
                    var said = runs.Select((o, k) => (o, k)).Where(x => x.o == "left-out").Select(x => x.k + 1).ToList();
                    sb.Append(said.Count == runCount ? " _(every run)_" : $" _(run {string.Join(", ", said)}; " + string.Join(", ", runs.Select((o, k) => (o, k)).Where(x => x.o != "left-out").Select(x => $"run {x.k + 1}: {x.o}")) + ")_");
                    if (f is not null && (string?)f["note"] is { } note && note.IndexOf(". ", StringComparison.Ordinal) is var cut and >= 0 && cut + 2 < note.Length)
                        sb.Append(" — ").Append(note[(cut + 2)..]);
                    sb.Append('\n');
                }
                sb.Append('\n');
            }
        }
        var open = items.Where(i => (string)i["result"]! == "not-checked").ToList();
        if (open.Count > 0)
        {
            sb.Append("## Not checked (the checklist)\n\nNo run gave these a checked answer, so whether the FSD describes them is not known.\n\n");
            foreach (var screen in open.GroupBy(i => (string)i["screen"]!))
                sb.Append($"- `{screen.Key}`: ").Append(string.Join("; ", screen.Select(i => $"{(string)i["id"]!} {What(i)}"))).Append('\n');
            sb.Append('\n');
        }
    }
    /// <summary>The list part's check (D404), reached through <c>fsd review-check</c> once the review is recorded: the
    /// lister's answers checked as D403 (3), (4), with a retry of its own. It never fails the run: with no answer after the
    /// retry every item it had is "not checked". Its outcome and left-out findings are added to the run's
    /// <c>checked.json</c>, so the merge reads one file per run.</summary>
    static RunStep CheckList(StangentRepo repo, string doc, int run, Usage? usage)
    {
        var dir = ListDir(repo.Root, doc, run);
        var state = PageRun.LoadJson(Path.Combine(dir, "state.json")) as JsonObject
            ?? throw new StangentException("not_prepared", $"Run {run} of {doc} has no list part. Prepare it again: `stangent fsd review {doc} --run {run} --again`.", ExitCodes.Usage);
        var file = Path.Combine(dir, OutputFile);
        if ((string?)state["stage"] == "retry" && usage is null && !File.Exists(file))
            throw new StangentException("no_output", $"Run {run}'s list retry has no answer yet ({Rel(repo.Root, file)}). Run the lister with the retry's prompt, or pass --usage for a call that returned nothing.", ExitCodes.Usage);
        state["usage"] = Usage.FromJson(state["usage"]).Add(usage ?? Usage.None).ToJson();
        var document = FsdOutline.Load(repo.Root, doc);
        if ((string)document["original"]!["hash"]! != (string)state["fsdVersion"]!)
            throw new StangentException("fsd_changed", $"{doc} has a new version since run {run} was prepared. Prepare it again: `stangent fsd review {doc} --run {run} --again`.", ExitCodes.Usage);
        var checkedPath = Path.Combine(RunDir(repo.Root, doc, run), "checked.json");
        if (PageRun.LoadJson(Path.Combine(dir, ChecklistJson)) is not JsonObject list || PageRun.LoadJson(checkedPath) is not JsonObject result)
            throw new StangentException("prepared_before", $"Run {run} of {doc} has lost its checklist or its review. Prepare it again: `stangent fsd review {doc} --run {run} --again`.", ExitCodes.Usage);

        var errors = new List<string>();
        var output = PageChecks.Parse(File.Exists(file) ? File.ReadAllText(file) : "", errors) as JsonObject;
        if (errors.Count == 0 && output is null) errors.Add("The output isn't a JSON object.");
        if (output is null && (!File.Exists(file) || new FileInfo(file).Length == 0))
            errors.Insert(0, "You returned no answer (most likely you ran out of turns or budget). This time, write the answers you have so far.");
        // Schema errors go to the retry, but the answers are read one by one either way: an odd one is not checked.
        if (output is not null) errors.AddRange(SchemaRegistry.Embedded.ValidateDeep(ListOutputSchema, output.ToJsonString()).Select(e => $"Schema: {e}"));
        var answers = output?["checklist"] as JsonArray;
        var checker = new FindingChecker(repo.Root, document, (string)state["commit"]!);
        var items = new ChecklistChecker(list, checker.SectionText, checker.EvidenceProblem);
        var all = errors.Concat(items.Errors(answers)).ToList();

        if (all.Count > 0 && (bool?)state["retryUsed"] != true)
        {
            state["retryUsed"] = true;
            Move(repo.Root, state, "retry", "the code check failed");
            SaveState(dir, state);
            var sb = new StringBuilder("# The code check failed\n\nSome of your output wasn't accepted. Fix every problem below and write the whole output again, ");
            sb.Append("every answer included (after one more check, a covered item whose quote still fails counts as left out, and an item with no answer is listed as not checked).\n\n");
            foreach (var e in all.Take(60)) sb.Append($"- {e}\n");
            if (all.Count > 60) sb.Append($"- … and {all.Count - 60} more\n");
            File.WriteAllText(Path.Combine(dir, "feedback.md"), sb.ToString());
            var previous = Path.Combine(dir, "previous-" + OutputFile);
            if (File.Exists(file)) File.Move(file, previous, overwrite: true); else File.WriteAllText(previous, "(no output)");
            var next = Next(repo.Root, dir, "retry");
            return next with
            {
                Errors = all,
                Prompt = next.Prompt + $"\nYour previous attempt: {Rel(repo.Root, previous)}\nWhat to fix: {Rel(repo.Root, Path.Combine(dir, "feedback.md"))}",
            };
        }

        var (outcomes, leftOut) = items.Settle(answers);
        var findings = result["findings"]!.AsArray();
        foreach (var f in leftOut) findings.Add(f);
        result["checklist"] = outcomes;
        result["usage"] = Usage.FromJson(result["usage"]).Add(Usage.FromJson(state["usage"])).ToJson();
        result["listRetried"] = (bool?)state["retryUsed"] == true;
        Quotes.WriteAtomic(checkedPath, CanonicalJson.Serialize(result));
        var open = outcomes.Count(o => (string)o!["result"]! == "not-checked");
        Move(repo.Root, state, "checked", open > 0 ? $"recorded, {open} not checked" : "recorded");
        SaveState(dir, state);
        return new RunStep("recorded", "none", Errors: open > 0 ? [$"{open} checklist items not checked"] : null);
    }
}

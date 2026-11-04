using System.Text;
using System.Text.Json.Nodes;
using Stangent.Spec.Requirements;

namespace Stangent.Spec.Fsd;

/// <summary>
/// Two versions of a document, section by section, by code (D117). Only exact signals match on their own, in this order:
/// the same body and heading path; the same body, unique on both sides; the same heading path, unique on both sides. Anything
/// left that shares a whole sentence with a section on the other side, or its heading path with one left there too, is doubtful,
/// for a person. The rest is added or removed.
/// There's no score and no threshold, so nothing here can be tuned to a document.
/// </summary>
public static class FsdSectionDiff
{
    /// <summary>A sentence counts as shared only with at least this many words: "See above." or "Not applicable." says nothing about where a section went.</summary>
    public const int MinSentenceWords = 4;

    const char PathMark = '\u0001';

    sealed record Side(JsonObject Json, string Id, string Title, string Path, string Text, string? PermanentId)
    {
        public string ParentPath => Path.Contains(PathMark) ? Path[..Path.LastIndexOf(PathMark)] : "";
        public string ShownPath => Path.Replace(PathMark.ToString(), " / ", StringComparison.Ordinal);
    }

    static List<Side> Read(JsonArray sections)
    {
        var all = sections.OfType<JsonObject>().ToList();
        var byId = all.ToDictionary(s => (string)s["id"]!, StringComparer.Ordinal);
        string PathOf(JsonObject s) => (string?)s["parent"] is { } p && byId.TryGetValue(p, out var up) ? PathOf(up) + PathMark + (string)s["title"]! : (string)s["title"]!;
        return [.. all.Select(s => new Side(s, (string)s["id"]!, (string)s["title"]!, PathOf(s), (string)s["text"]!, (string?)s["permanentId"]))];
    }

    /// <summary>The sentences a section can be recognised by, each once, in order.</summary>
    public static List<string> SharedSentences(string text) =>
        [.. Sentences.Of(text).Select(s => s.Text).Where(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w != "|") >= MinSentenceWords).Distinct(StringComparer.Ordinal)];

    /// <summary>One entry per section: the candidate's in its order, then the current version's that have no match.</summary>
    public static List<JsonObject> Compare(JsonArray current, JsonArray candidate)
    {
        var olds = Read(current);
        var news = Read(candidate);
        var match = new Dictionary<Side, (Side Old, string Kind)>();
        var taken = new HashSet<Side>();

        void Rule<TKey>(Func<Side, TKey> key, Func<Side, Side, string> kind, Func<Side, bool>? eligible = null) where TKey : notnull
        {
            var oldCount = olds.GroupBy(key).ToDictionary(g => g.Key, g => g.ToList());
            var newCount = news.GroupBy(key).ToDictionary(g => g.Key, g => g.Count());
            foreach (var n in news.Where(n => !match.ContainsKey(n) && (eligible?.Invoke(n) ?? true)))
            {
                // Unique in the whole document on both sides: a pair picked from among several is a guess.
                if (newCount[key(n)] != 1 || !oldCount.TryGetValue(key(n), out var same) || same.Count != 1 || taken.Contains(same[0])) continue;
                match[n] = (same[0], kind(same[0], n));
                taken.Add(same[0]);
            }
        }

        Rule(s => (s.Path, s.Text), (_, _) => "unchanged");
        Rule(s => s.Text, (o, n) => o.Title != n.Title && o.ParentPath == n.ParentPath ? "renamed" : "moved", s => s.Text.Trim().Length > 0);
        Rule(s => s.Path, (_, _) => "modified");

        var oldBySentence = BySentence(olds);
        var newBySentence = BySentence(news);
        var entries = new List<JsonObject>();
        foreach (var n in news)
        {
            if (match.TryGetValue(n, out var m))
            {
                var e = new JsonObject { ["kind"] = m.Kind, ["permanentId"] = m.Old.PermanentId, ["from"] = m.Old.Id, ["to"] = n.Id, ["title"] = n.Title };
                if (m.Old.Id != n.Id) e["numberChanged"] = true;
                if (m.Old.Title != n.Title) e["fromTitle"] = m.Old.Title;
                if (m.Old.Path != n.Path) { e["fromPath"] = m.Old.ShownPath; e["toPath"] = n.ShownPath; }
                entries.Add(e);
                continue;
            }
            var shared = Shared(n, oldBySentence);
            var heading = SameHeading(n, olds.Where(o => !taken.Contains(o)), shared);
            entries.Add(shared.Count + heading.Count == 0
                ? new JsonObject { ["kind"] = "added", ["to"] = n.Id, ["title"] = n.Title }
                : new JsonObject
                {
                    ["kind"] = "doubtful", ["side"] = "candidate", ["to"] = n.Id, ["title"] = n.Title, ["path"] = n.ShownPath,
                    ["candidates"] = new JsonArray([.. shared.Select(c => (JsonNode)new JsonObject
                    {
                        ["permanentId"] = c.Other.PermanentId, ["from"] = c.Other.Id, ["title"] = c.Other.Title, ["sentence"] = c.Sentence, ["alreadyMatched"] = taken.Contains(c.Other),
                    }), .. heading.Select(o => (JsonNode)new JsonObject
                    {
                        ["permanentId"] = o.PermanentId, ["from"] = o.Id, ["title"] = o.Title, ["sentence"] = null, ["sameHeading"] = true, ["alreadyMatched"] = false,
                    })]),
                });
        }
        foreach (var o in olds.Where(o => !taken.Contains(o)))
        {
            var shared = Shared(o, newBySentence);
            var heading = SameHeading(o, news.Where(n => !match.ContainsKey(n)), shared);
            entries.Add(shared.Count + heading.Count == 0
                ? new JsonObject { ["kind"] = "removed", ["permanentId"] = o.PermanentId, ["from"] = o.Id, ["title"] = o.Title }
                : new JsonObject
                {
                    ["kind"] = "doubtful", ["side"] = "current", ["permanentId"] = o.PermanentId, ["from"] = o.Id, ["title"] = o.Title, ["path"] = o.ShownPath,
                    ["candidates"] = new JsonArray([.. shared.Select(c => (JsonNode)new JsonObject
                    {
                        ["to"] = c.Other.Id, ["title"] = c.Other.Title, ["sentence"] = c.Sentence, ["alreadyMatched"] = match.ContainsKey(c.Other),
                    }), .. heading.Select(n => (JsonNode)new JsonObject
                    {
                        ["to"] = n.Id, ["title"] = n.Title, ["sentence"] = null, ["sameHeading"] = true, ["alreadyMatched"] = false,
                    })]),
                });
        }
        return entries;
    }

    static Dictionary<string, List<Side>> BySentence(List<Side> sections)
    {
        var map = new Dictionary<string, List<Side>>(StringComparer.Ordinal);
        foreach (var s in sections)
            foreach (var sentence in SharedSentences(s.Text))
                (map.TryGetValue(sentence, out var list) ? list : map[sentence] = []).Add(s);
        return map;
    }

    /// <summary>The sections on the other side sharing a sentence with this one, in that document's order, with the first sentence shared.</summary>
    static List<(Side Other, string Sentence)> Shared(Side section, Dictionary<string, List<Side>> other)
    {
        var found = new List<(Side, string)>();
        var seen = new HashSet<Side>();
        foreach (var sentence in SharedSentences(section.Text))
            foreach (var s in other.GetValueOrDefault(sentence) ?? [])
                if (seen.Add(s)) found.Add((s, sentence));
        return found;
    }

    /// <summary>The sections left unmatched on the other side under this one's heading path, not already listed for a sentence. A path shared by several
    /// can't match on its own, and setting an id aside over a rewrite under a repeated heading would be silent.</summary>
    static List<Side> SameHeading(Side section, IEnumerable<Side> left, List<(Side Other, string Sentence)> shared) =>
        [.. left.Where(s => s.Path == section.Path && shared.All(c => c.Other != s))];

    /// <summary>Each outcome's count. <c>doubtful</c> is what's still open, so it reaches zero; <c>numberChanged</c> counts across the outcomes.</summary>
    public static JsonObject Counts(IEnumerable<JsonObject> entries)
    {
        var list = entries.ToList();
        var claimed = list.Select(e => (string?)e["same_as"]).OfType<string>().ToHashSet(StringComparer.Ordinal);
        int Kind(string k) => list.Count(e => (string)e["kind"]! == k && !(k == "removed" && claimed.Contains((string)e["permanentId"]!)));
        return new JsonObject
        {
            ["unchanged"] = Kind("unchanged"), ["moved"] = Kind("moved"), ["renamed"] = Kind("renamed"), ["modified"] = Kind("modified"),
            ["added"] = Kind("added"), ["removed"] = Kind("removed"),
            ["doubtful"] = list.Count(e => (string)e["kind"]! == "doubtful" && (string?)e["state"] == "doubtful"),
            ["settled"] = list.Count(e => (string)e["kind"]! == "doubtful" && (string?)e["state"] != "doubtful"),
            ["numberChanged"] = list.Count(e => e["numberChanged"] is not null),
        };
    }

    /// <summary><c>SECTION_DIFF.md</c>: for people, from the JSON form. The same diff gives the same bytes.</summary>
    public static string Markdown(JsonObject diff)
    {
        var doc = (string)diff["doc"]!;
        var entries = diff["sections"]!.AsArray().OfType<JsonObject>().ToList();
        var claimed = entries.Select(e => (string?)e["same_as"]).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var sb = new StringBuilder($"# Section diff: {doc}\n\n");
        sb.Append($"Current version `{((string)diff["from"]!)[..12]}`, candidate `{((string)diff["to"]!)[..12]}`. The current version keeps serving until `stangent fsd promote {doc}`.\n\n");
        sb.Append("| Outcome | Sections |\n|---|---|\n");
        foreach (var name in new[] { "unchanged", "moved", "renamed", "modified", "added", "removed", "doubtful", "settled", "numberChanged" }) sb.Append($"| {name} | {diff["counts"]![name]} |\n");
        var open = entries.Where(e => (string)e["kind"]! == "doubtful").ToList();
        if (open.Count > 0)
        {
            sb.Append("\n## Doubtful: a person decides\n\nCode found a shared sentence or the same heading, and nothing exact, so it doesn't guess.\n");
            foreach (var e in open)
            {
                var candidateSide = (string)e["side"]! == "candidate";
                var name = candidateSide ? $"§{e["to"]}" : $"{e["permanentId"]} (§{e["from"]})";
                sb.Append($"\n### {name} {e["title"]}: {e["state"]}");
                if ((string?)e["decided_by"] is { } by) sb.Append($", by {by}");
                sb.Append(candidateSide ? " (in the candidate)\n\n" : " (in the current version)\n\n");
                foreach (var c in e["candidates"]!.AsArray().OfType<JsonObject>())
                {
                    var where = candidateSide ? $"{c["permanentId"]} §{c["from"]}" : $"§{c["to"]}";
                    sb.Append($"- {where} {c["title"]}{((bool)c["alreadyMatched"]! ? " (already matched)" : "")}: {((string?)c["sentence"] is { } sentence ? $"\"{sentence}\"" : "the same heading")}\n");
                }
                sb.Append(candidateSide
                    ? $"\nSettle: `stangent fsd resolve {doc} \"{e["to"]}\" --same-as <S-id>` or `--new`\n"
                    : $"\nSettle: `stangent fsd resolve {doc} {e["permanentId"]} --removed`, or claim it from a candidate section with `--same-as`\n");
            }
        }
        foreach (var kind in new[] { "moved", "renamed", "modified", "added", "removed" })
        {
            var some = entries.Where(e => (string)e["kind"]! == kind && !(kind == "removed" && claimed.Contains((string)e["permanentId"]!))).ToList();
            if (some.Count == 0) continue;
            sb.Append($"\n## {char.ToUpperInvariant(kind[0])}{kind[1..]}\n\n");
            foreach (var e in some) sb.Append($"- {Line(e)}\n");
        }
        var renumbered = entries.Where(e => (string)e["kind"]! == "unchanged" && e["numberChanged"] is not null).ToList();
        if (renumbered.Count > 0)
        {
            sb.Append("\n## Unchanged, with a new number\n\n");
            foreach (var e in renumbered) sb.Append($"- {Line(e)}\n");
        }
        return sb.ToString();
    }

    static string Line(JsonObject e)
    {
        var id = (string?)e["permanentId"] is { } s ? s + " " : "";
        var numbers = ((string?)e["from"], (string?)e["to"]) switch
        {
            ({ } f, { } t) when f != t => $"§{f} → §{t}",
            (_, { } t) => $"§{t}",
            ({ } f, _) => $"§{f}",
            _ => "",
        };
        var was = (string?)e["fromPath"] is { } path ? $" (was {path})" : "";
        return $"{id}{numbers} {e["title"]}{was}";
    }
}

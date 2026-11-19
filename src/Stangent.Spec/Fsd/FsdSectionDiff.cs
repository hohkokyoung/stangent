using System.Text.Json.Nodes;
using Stangent.Core;

namespace Stangent.Spec.Fsd;

/// <summary>
/// Two versions of a document, section by section, by code (D117, D385). Only exact signals match on their own, in this order:
/// with the last imported file's sections (<see cref="BaseSection"/>, phase fs layer 5), the same heading path and text hash as the
/// last file gave it, then the same text hash, each unique on both sides; then, against the repo's text, the same body and heading
/// path; the same body, unique on both sides; the same heading path as the last file had, then as the repo has, unique on both sides. Anything
/// left that shares a whole sentence with a section on the other side, or its heading path with one left there too, is doubtful,
/// for a person. The rest is added or removed.
/// There's no score and no threshold, so nothing here can be tuned to a document.
/// </summary>
public static class FsdSectionDiff
{
    /// <summary>A sentence counts as shared only with at least this many words: "See above." or "Not applicable." says nothing about where a section went.</summary>
    public const int MinSentenceWords = 4;

    internal const char PathMark = '\u0001';
    static readonly string EmptyHash = CanonicalJson.Hash("");

    /// <summary>A section as the last imported file gave it (phase fs layer 5, D385): its permanent id, heading path and text hash.
    /// <c>InRepo</c> false: the repo no longer has it, so only these two can match it.</summary>
    public sealed record BaseSection(string PermanentId, string Path, string TextHash);

    sealed record Side(JsonObject Json, string Id, string Title, string Path, string Text, string? PermanentId)
    {
        public BaseSection? Base { get; init; }
        /// <summary>A section the last file had and the repo no longer has: matched only by what the file gave it.</summary>
        public bool RepoGone { get; init; }
        public string TextHash => CanonicalJson.Hash(Text);
        public string? ContentHash => (string?)Json["contentHash"];
        public List<(string Name, string Hash)> Files => [.. (Json["attachments"]?.AsArray() ?? []).OfType<JsonObject>().Select(a => ((string)a["fileName"]!, (string)a["hash"]!))];
        public string ParentPath => Path.Contains(PathMark) ? Path[..Path.LastIndexOf(PathMark)] : "";
        public string ShownPath => Path.Replace(PathMark.ToString(), " / ", StringComparison.Ordinal);
    }

    static List<Side> Read(JsonArray sections)
    {
        var all = sections.OfType<JsonObject>().ToList();
        var paths = Paths(all);
        return [.. all.Select(s => new Side(s, (string)s["id"]!, (string)s["title"]!, paths[(string)s["id"]!], (string)s["text"]!, (string?)s["permanentId"]))];
    }

    /// <summary>Each section's heading path, by its id: its parents' titles and its own, as the matcher compares them.</summary>
    public static Dictionary<string, string> Paths(IReadOnlyList<JsonObject> sections)
    {
        var byId = sections.GroupBy(s => (string)s["id"]!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        string PathOf(JsonObject s) => (string?)s["parent"] is { } p && byId.TryGetValue(p, out var up) ? PathOf(up) + PathMark + (string)s["title"]! : (string)s["title"]!;
        return byId.ToDictionary(kv => kv.Key, kv => PathOf(kv.Value), StringComparer.Ordinal);
    }

    /// <summary>A heading path as people read it: <c>Fruit / Pears</c>.</summary>
    public static string Shown(string path) => path.Replace(PathMark.ToString(), " / ", StringComparison.Ordinal);

    /// <summary>The sentences a section can be recognised by, each once, in order.</summary>
    public static List<string> SharedSentences(string text) =>
        [.. Sentences.Of(text).Select(s => s.Text).Where(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w != "|") >= MinSentenceWords).Distinct(StringComparer.Ordinal)];

    /// <summary>Files on two sides, compared: by name and bytes, and a name on both sides with other bytes is that file changed.</summary>
    internal static List<JsonObject> Changes(List<(string Name, string Hash)> before, List<(string Name, string Hash)> after)
    {
        var (left, right) = (before.Where(b => !after.Contains(b)).ToList(), after.Where(a => !before.Contains(a)).ToList());
        var changes = new List<JsonObject>();
        foreach (var r in right)
        {
            var was = left.FindIndex(l => l.Name == r.Name);
            changes.Add(new JsonObject { ["fileName"] = r.Name, ["change"] = was >= 0 ? "changed" : "added" });
            if (was >= 0) left.RemoveAt(was);
        }
        changes.AddRange(left.Select(l => new JsonObject { ["fileName"] = l.Name, ["change"] = "removed" }));
        return changes;
    }

    /// <summary>What changed in a matched section's attachments. Nothing when either side has no content hash, an older document, where matching falls back on the text (D123).</summary>
    static JsonArray? FileChanges(Side old, Side now) =>
        old.ContentHash is null || now.ContentHash is null || Changes(old.Files, now.Files) is not { Count: > 0 } changes ? null : new JsonArray([.. changes.Select(c => (JsonNode)c)]);

    /// <summary>
    /// I.6 (D131): for each doubtful section, the unmatched sections on the other side, closest first, by how many of their words they share (of all the words either has).
    /// Only an order for a person reading them: no threshold anywhere, and nothing here settles a match, so a score can't make one. Sections sharing no word aren't listed.
    /// </summary>
    static void Suggest(List<JsonObject> entries, List<Side> olds, List<Side> news, Dictionary<Side, (Side Old, string Kind)> match, HashSet<Side> taken)
    {
        var words = new Dictionary<Side, HashSet<string>>();
        HashSet<string> Words(Side s)
        {
            if (!words.TryGetValue(s, out var w))
                words[s] = w = [.. System.Text.RegularExpressions.Regex.Matches((s.Title + " " + s.Text).ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value)];
            return w;
        }
        var oldById = olds.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var newById = news.ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var e in entries.Where(e => (string)e["kind"]! == "doubtful"))
        {
            var candidateSide = (string?)e["side"] == "candidate";
            var me = candidateSide ? newById[(string)e["to"]!] : oldById[(string)e["from"]!];
            var others = candidateSide ? olds.Where(o => !taken.Contains(o)) : news.Where(n => !match.ContainsKey(n));
            e["suggestions"] = new JsonArray([.. others
                .Select(o => (Other: o, Shared: Words(me).Intersect(Words(o)).Count(), All: Words(me).Union(Words(o)).Count()))
                .Where(x => x.Shared > 0)
                .OrderByDescending(x => (double)x.Shared / x.All).ThenBy(x => x.Other.Id, StringComparer.Ordinal)
                .Select(x =>
                {
                    var o = new JsonObject();
                    if (candidateSide) { o["permanentId"] = x.Other.PermanentId; o["from"] = x.Other.Id; }
                    else o["to"] = x.Other.Id;
                    o["title"] = x.Other.Title;
                    o["similarity"] = Math.Round((double)x.Shared / x.All, 2);
                    return (JsonNode)o;
                })]);
        }
    }

    /// <summary>One entry per section: the candidate's in its order, then the current version's that have no match. With
    /// <paramref name="lastFile"/> (phase fs layer 5), each current section also carries its text hash and heading path as the last
    /// imported file gave them, keyed by permanent id, and those match first; one the current version no longer has is matched
    /// by them alone, and left out of the entries when nothing matches it (<c>repoGone</c> on its entry when something does).</summary>
    public static List<JsonObject> Compare(JsonArray current, JsonArray candidate, IReadOnlyList<BaseSection>? lastFile = null)
    {
        var olds = Read(current);
        var baseById = (lastFile ?? []).ToDictionary(b => b.PermanentId, StringComparer.Ordinal);
        olds = [.. olds.Select(o => o.PermanentId is { } p && baseById.TryGetValue(p, out var b) ? o with { Base = b } : o)];
        var here = olds.Select(o => o.PermanentId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        olds.AddRange((lastFile ?? []).Where(b => !here.Contains(b.PermanentId)).Select(b => new Side(
            new JsonObject { ["id"] = b.PermanentId, ["title"] = b.Path.Split(PathMark)[^1], ["text"] = "" }, b.PermanentId, b.Path.Split(PathMark)[^1], b.Path, "", b.PermanentId) { Base = b, RepoGone = true }));
        var news = Read(candidate);
        var match = new Dictionary<Side, (Side Old, string Kind)>();
        var taken = new HashSet<Side>();

        // Keys that can be null: a section with no such key takes no part in that rule, on either side.
        void Exact(Func<Side, string?> oldKey, Func<Side, string?> newKey)
        {
            var oldCount = olds.Where(o => oldKey(o) is not null).GroupBy(o => oldKey(o)!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var newCount = news.Where(n => newKey(n) is not null).GroupBy(n => newKey(n)!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var n in news.Where(n => !match.ContainsKey(n) && newKey(n) is not null))
            {
                if (newCount[newKey(n)!] != 1 || !oldCount.TryGetValue(newKey(n)!, out var same) || same.Count != 1 || taken.Contains(same[0])) continue;
                var o = same[0];
                match[n] = (o, o.RepoGone ? "modified" : o.Path == n.Path && o.Text == n.Text ? "unchanged" : o.Text == n.Text ? (o.Title != n.Title && o.ParentPath == n.ParentPath ? "renamed" : "moved") : "modified");
                taken.Add(o);
            }
        }
        if (lastFile is not null)
        {
            Exact(o => o.Base is { } b ? b.Path + PathMark + PathMark + b.TextHash : null, n => n.Path + PathMark + PathMark + n.TextHash);
            Exact(o => o.Base is { } b && !string.IsNullOrEmpty(b.TextHash) && b.TextHash != EmptyHash ? b.TextHash : null, n => n.Text.Trim().Length > 0 ? n.TextHash : null);
        }

        void Rule<TKey>(Func<Side, TKey> key, Func<Side, Side, string> kind, Func<Side, bool>? eligible = null) where TKey : notnull
        {
            var oldCount = olds.Where(o => !o.RepoGone).GroupBy(key).ToDictionary(g => g.Key, g => g.ToList());
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
        if (lastFile is not null) Exact(o => o.Base?.Path, n => n.Path);
        // A section the repo took out takes no part in the rest: only what the file gave it could say it's the same one.
        olds.RemoveAll(o => o.RepoGone && !taken.Contains(o));
        Rule(s => s.Path, (_, _) => "modified");

        var oldBySentence = BySentence(olds);
        var newBySentence = BySentence(news);
        var entries = new List<JsonObject>();
        foreach (var n in news)
        {
            if (match.TryGetValue(n, out var m))
            {
                // The same words with other files is a changed section: it reads modified, and says which file.
                var files = FileChanges(m.Old, n);
                var e = new JsonObject { ["kind"] = m.Kind == "unchanged" && files is not null ? "modified" : m.Kind, ["permanentId"] = m.Old.PermanentId, ["from"] = m.Old.Id, ["to"] = n.Id, ["title"] = n.Title };
                if (files is not null) e["attachmentsChanged"] = files;
                if (m.Old.Id != n.Id) e["numberChanged"] = true;
                if (m.Old.Title != n.Title) e["fromTitle"] = m.Old.Title;
                if (m.Old.Path != n.Path) { e["fromPath"] = m.Old.ShownPath; e["toPath"] = n.ShownPath; }
                if (m.Old.RepoGone) e["repoGone"] = true;
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
        Suggest(entries, olds, news, match, taken);
        MarkSplits(entries);
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

    /// <summary>
    /// I.5: one section of the current version whose sentences went to two or more sections no other section claims. Stays doubtful, since which part keeps the
    /// id is a person's call and links rest on it, but it reads as what it is. A sentence is the same test the doubtful ones pass, so there's no score here.
    /// </summary>
    static void MarkSplits(List<JsonObject> entries)
    {
        var parts = entries.Where(e => (string)e["kind"]! == "doubtful" && (string?)e["side"] == "candidate").ToDictionary(e => (string)e["to"]!, StringComparer.Ordinal);
        foreach (var old in entries.Where(e => (string)e["kind"]! == "doubtful" && (string?)e["side"] == "current"))
        {
            var into = old["candidates"]!.AsArray().OfType<JsonObject>().Where(c => c["sentence"] is not null && (bool)c["alreadyMatched"]! == false)
                .Select(c => (string)c["to"]!).Distinct(StringComparer.Ordinal).ToList();
            if (into.Count < 2) continue;
            var onlyFromThis = into.All(to => parts.TryGetValue(to, out var part) && part["candidates"]!.AsArray().OfType<JsonObject>()
                .Where(c => c["sentence"] is not null).All(c => (string?)c["permanentId"] == (string?)old["permanentId"]));
            if (!onlyFromThis) continue;
            old["splitInto"] = new JsonArray([.. into.Select(t => (JsonNode)t)]);
            foreach (var to in into) parts[to]["splitFrom"] = (string?)old["permanentId"];
        }
    }
}

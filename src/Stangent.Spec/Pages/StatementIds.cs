using System.Text.Json.Nodes;
using Stangent.Core.Evidence;

namespace Stangent.Spec.Pages;

/// <summary>One statement as the matching sees it: its section, its id, its words and the symbols it cites.</summary>
public sealed record StatementRef(string Section, string Id, string Text, IReadOnlyList<string> Cites)
{
    public static List<StatementRef> Of(JsonObject page) => [.. PageChecks.Statements(page).Select(s => new StatementRef(s.Section, s.Id, (string?)s.Statement["text"] ?? "",
        [.. (s.Statement["cites"] as JsonArray ?? []).Select(c => (string)c!)]))];
}

/// <summary>What matching found. <see cref="Kept"/> gives a fresh statement's place the old id it keeps.</summary>
public sealed record IdMatch(IReadOnlyDictionary<int, string> Kept, IReadOnlySet<int> Rephrased, IReadOnlyList<string> Retired);

/// <summary>A page's statement ids after a regeneration: one per fresh statement, in order.</summary>
public sealed record IdAssignment(IReadOnlyList<string> Ids, int KeptSameText, int KeptRephrased, IReadOnlyList<string> Retired, IReadOnlyList<string> New);

/// <summary>
/// Statement ids stay with their statements (2.3, D118). A page's ids at adoption become permanent, in a ledger per page beside it.
/// Matching is by code and exact signals only, with no score: the same words keep their id, and a rephrase with exactly the same
/// cited symbols, in the same section, alone on both sides, keeps its id. Anything else gets a new id, and a retired number never comes back.
/// </summary>
public static class StatementIds
{
    /// <summary>The ledger beside a page's JSON. It holds no <c>sections</c> and no <c>schemaVersion</c>, so the page lists pass over it.</summary>
    public static string LedgerFile(string pageJson) => pageJson[..^".json".Length] + ".ids.json";

    /// <summary>The lock stays out of specs/, which is committed.</summary>
    public static PermanentIds Ledger(string root, string pageJson) =>
        new(LedgerFile(pageJson), Path.Combine(root, ".stangent", "spec", "ids.lock"));

    static string Format(string letter) => letter + "{0}";

    /// <summary>A page's current ids become permanent: each letter's counter goes up to its highest number, and never down.</summary>
    public static void Adopt(JsonObject page, PermanentIds ledger)
    {
        foreach (var letter in StatementRef.Of(page).Select(s => (s.Id[..1], int.TryParse(s.Id[1..], out var n) ? n : 0)).GroupBy(x => x.Item1))
            ledger.Raise(letter.Key, Format(letter.Key), letter.Max(x => x.Item2));
    }

    public static IdMatch Match(IReadOnlyList<StatementRef> old, IReadOnlyList<StatementRef> fresh)
    {
        var kept = new Dictionary<int, string>();
        var rephrased = new HashSet<int>();
        var left = old.ToList();

        // The same words: in the same section first, then wherever they went. An id's letter says where a statement began.
        foreach (var sameSection in new[] { true, false })
            for (var i = 0; i < fresh.Count; i++)
            {
                if (kept.ContainsKey(i)) continue;
                var words = Quotes.Normalize(fresh[i].Text);
                var was = left.FirstOrDefault(o => Quotes.Normalize(o.Text) == words && (!sameSection || o.Section == fresh[i].Section));
                if (was is null) continue;
                kept[i] = was.Id;
                left.Remove(was);
            }

        // A rephrase: the same section and exactly the same symbols, alone on both sides among what's left.
        string Signal(StatementRef s) => s.Section + "\u0001" + string.Join("\u0001", s.Cites.Distinct().Order(StringComparer.Ordinal));
        var oldBySignal = left.ToLookup(Signal);
        var freshLeft = Enumerable.Range(0, fresh.Count).Where(i => !kept.ContainsKey(i)).ToLookup(i => Signal(fresh[i]));
        foreach (var group in freshLeft.Where(g => g.Count() == 1 && oldBySignal[g.Key].Count() == 1))
        {
            var was = oldBySignal[group.Key].Single();
            kept[group.Single()] = was.Id;
            rephrased.Add(group.Single());
            left.Remove(was);
        }
        return new IdMatch(kept, rephrased, [.. left.Select(o => o.Id)]);
    }

    /// <summary>Gives a page's fresh statements their ids: adopts the old page, keeps what matched, marks the rest removed, and issues new numbers.</summary>
    public static IdAssignment Assign(JsonObject? previous, IReadOnlyList<StatementRef> fresh, PermanentIds ledger)
    {
        if (previous is not null) Adopt(previous, ledger);
        var match = Match(previous is null ? [] : StatementRef.Of(previous), fresh);
        foreach (var id in match.Retired) ledger.Remove(id[..1], id);
        var fresher = new List<string>();
        var ids = new List<string>(fresh.Count);
        for (var i = 0; i < fresh.Count; i++)
        {
            if (match.Kept.TryGetValue(i, out var id)) { ids.Add(id); continue; }
            var letter = PageChecks.Letter(fresh[i].Section);
            ids.Add(ledger.Issue(letter, Format(letter)));
            fresher.Add(ids[^1]);
        }
        return new IdAssignment(ids, match.Kept.Count - match.Rephrased.Count, match.Rephrased.Count, match.Retired, fresher);
    }
}

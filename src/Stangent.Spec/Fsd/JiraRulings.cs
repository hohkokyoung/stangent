using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Graph;

namespace Stangent.Spec.Fsd;

/// <summary>
/// Truth map layer 5 (D364, D374): a ruling from a Jira <c>Ruling:</c> reply, drafted into <c>rulings.yaml</c> the way
/// <c>fsd rule</c> writes (only the entry's lines change), with <c>jira: { key, comment, author, created }</c>, never
/// committed. A reply about an entry rules that entry; a reply when nothing covered the ticket (or the rule in words on a
/// match) becomes an entry of its own, keyed <c>ticket:&lt;KEY&gt;</c>, on the ticket's code. Code only: a reply that
/// doesn't fit the entry is refused with why, never guessed.
/// </summary>
public static partial class JiraRulings
{
    public const string TicketLink = "ticket:";

    /// <summary>An entry drafted from a ticket's reply, not found by a review: refreshes keep it as it is.</summary>
    public static bool FromTicket(RulingEntry e) => e.KeyLink?.StartsWith(TicketLink, StringComparison.Ordinal) == true;

    public sealed record Reply(string Ticket, string Comment, string Author, string Created, string Text);

    /// <summary>What a reply says: one of the fixed words (<c>code-right</c>, <c>fsd-right</c>, <c>keep</c>, <c>remove</c>,
    /// <c>yes</c>, <c>no</c>), or the rule in the PM's words; null when there is nothing after <c>Ruling:</c>.</summary>
    public sealed record Said(string? Word, string? Words, string? Unclear = null)
    {
        public override string ToString() => Unclear is not null ? "unclear" : Word ?? $"in their words: “{Words}”";
    }

    static readonly (string Word, string[] Forms)[] Forms =
    [
        ("code-right", ["code right", "code-right", "the code is right", "code is right", "the code", "code"]),
        ("fsd-right", ["fsd right", "fsd-right", "the fsd is right", "fsd is right", "the fsd", "fsd"]),
        ("keep", ["keep", "keep it"]),
        ("remove", ["remove", "remove it"]),
        ("yes", ["yes", "y", "confirmed"]),
        ("no", ["no", "n", "not a match", "not-a-match"]),
    ];

    /// <summary>The text after <c>Ruling:</c> as one of the fixed words, or the PM's words (restated).</summary>
    public static Said? Read(string text)
    {
        var t = Strip(text);
        if (t.Length == 0) return null;
        var norm = Norm(t);
        foreach (var (word, forms) in Forms)
            if (forms.Contains(norm)) return new Said(word, null);
        // "yes, it does that", "keep it, thanks": a fixed word with more after it could be the answer or a rule; asked, not guessed.
        if (Regex.Match(t, @"^(?<head>[^,;:.!()\-–—]+)[,;:.!()\-–—]+\s*(?<rest>\S.*)$") is { Success: true } m
            && Forms.FirstOrDefault(f => f.Forms.Contains(Norm(m.Groups["head"].Value))) is { Word: { } lead })
            return new Said(null, null, $"it starts with `{lead}` but says more after it; reply with the word alone, or with the rule alone in your own words");
        return new Said(null, t);
    }

    static string Norm(string t) => Regex.Replace(t.ToLowerInvariant(), @"\s+", " ").Trim().TrimEnd('.', '!').Trim();

    /// <summary>The text of a reply line starting <c>Ruling:</c> (bold or italic marks allowed), or null.</summary>
    public static string? RulingLine(string line) => RulingRegex().Match(line) is { Success: true } m ? Strip(m.Groups["text"].Value) : null;

    [GeneratedRegex(@"^\s*(?:(?:[-•]|\d+[.)])\s+)?[*_]{0,2}ruling[*_]{0,2}\s*:[*_]{0,2}(?<text>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex RulingRegex();

    static string Strip(string s) => s.Trim().Trim('*', '_', '`').Trim();

    /// <summary>Where this reply is already drafted (or committed): the doc and the entry, or null.</summary>
    public static (string Doc, string Entry)? Recorded(string root, string ticket, string comment)
    {
        foreach (var doc in Docs(root))
        {
            var (file, _) = Rulings.Load(root, doc);
            if (file?.All.FirstOrDefault(e => (string?)e.Jira?["key"] == ticket && (string?)e.Jira?["comment"] == comment) is { } e) return (doc, e.Describe());
        }
        return null;
    }

    /// <summary>The FSDs with a <c>rulings.yaml</c>.</summary>
    public static List<string> Docs(string root)
    {
        var dir = Path.Combine(root, "specs", "fsd");
        return Directory.Exists(dir)
            ? [.. Directory.GetDirectories(dir).Where(d => File.Exists(Path.Combine(d, "rulings.yaml"))).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>
    /// Drafts the reply's ruling into <paramref name="doc"/>'s <c>rulings.yaml</c>. <paramref name="key"/> is the entry the
    /// question was about (its truth map key) with its <paramref name="state"/>, or null when nothing covered the ticket;
    /// <paramref name="sectionId"/> and <paramref name="evidence"/> place an entry of the ticket's own. Returns
    /// <c>result: drafted</c> with what changed, or <c>refused</c> with why (nothing written).
    /// </summary>
    public static JsonObject Draft(StangentRepo repo, string doc, JsonObject? key, string? state, string? sectionId, IReadOnlyList<string> evidence, Reply reply, bool contradicted = false)
    {
        var root = repo.Root;
        var path = Rulings.PathFor(root, doc);
        JsonObject Refused(string why) => new() { ["result"] = "refused", ["doc"] = doc, ["why"] = why };
        if (!File.Exists(path)) return Refused($"{doc} has no rulings.yaml");
        var yaml = File.ReadAllText(path);
        var (file, parseProblems) = Rulings.Parse(yaml);
        var problems = TruthMap.Problems(file, parseProblems);
        if (problems.Count > 0) return Refused($"rulings.yaml needs fixing first ({problems[0]})");
        if (Read(reply.Text) is not { } said) return Refused("nothing follows `Ruling:`");
        if (said.Unclear is { } unclear) return Refused(unclear);
        // "Should the rule change?" (a contradicted ticket): yes or no says whether, not to what, so the rule itself is asked for.
        if (contradicted && said.Word is "yes" or "no")
            return Refused($"the question asked whether the agreed rule should change, and `{said.Word}` doesn't say what the rule is: reply `Ruling: the FSD is right`, `Ruling: the code is right`, or the rule in your own words");

        RulingEntry target;
        string? replaced = null;
        var created = false;
        if (key is not null && state is not null)
        {
            var probe = new RulingEntry { KeySection = (string?)key["section"], KeyQuote = (string?)key["quote"], KeyLink = (string?)key["link"], State = state };
            var found = file.WithSections.FirstOrDefault(x => x.Entry.Problem is null && x.Entry.Key == probe.Key);
            if (found.Entry is null) return Refused($"the entry it was about ({probe.Describe()}) isn't in rulings.yaml any more: a review refresh changed it");
            if (found.Entry.State != state) return Refused($"the entry it was about ({probe.Describe()}) is now a {found.Entry.State}, not the {state} the question asked about");
            if (found.Entry.IsMatch && said.Words is not null)
            {
                // The rule in words on a match ("what should it do?"): a match can't be restated (D371), so it becomes an entry of the ticket's own in that section.
                (target, created) = TicketEntry(file, reply.Ticket, found.Entry.KeySection, found.Entry.Evidence, repo);
            }
            else target = found.Entry;
        }
        else
        {
            if (evidence.Count == 0) return Refused("nothing covered the ticket and it has no starting point in the code to tie a rule to: add one with `stangent ticket points --add`");
            (target, created) = TicketEntry(file, reply.Ticket, sectionId, evidence, repo);
        }

        if (contradicted && target.IsMatch && said.Word is not null)
            return Refused($"the question asked whether the agreed rule should change, and on a sentence the code does as the FSD says `{said.Word}` doesn't say to what: reply with the rule in your own words");
        var ruling = RulingFor(target, said, key is null, out var misfit);
        if (ruling is null) return Refused(misfit!);
        if (target.Ruling is { } old && !created) replaced = old == "restated" ? $"restated “{target.Statement}”" : old;
        (target.Ruling, target.RulingRaw) = (ruling, null);
        target.Statement = ruling == "restated" ? said.Words : null;
        target.Flagged = null;
        target.New = false;
        target.Jira = new JsonObject { ["key"] = reply.Ticket, ["comment"] = reply.Comment, ["author"] = reply.Author, ["created"] = reply.Created };
        var after = TruthMap.Problems(file, []);
        if (after.Count > 0) return Refused($"it would leave rulings.yaml invalid ({after[0]})");
        PmAnswers.Save(path, yaml, file);
        var o = new JsonObject
        {
            ["result"] = "drafted", ["doc"] = doc, ["file"] = Path.GetRelativePath(root, path).Replace('\\', '/'),
            ["entry"] = target.Describe(), ["newEntry"] = created, ["ruling"] = ruling, ["statement"] = target.Statement,
            ["replaced"] = replaced, ["committed"] = false,
        };
        if (target.NotFound is not null) o["warning"] = $"the entry stays marked: the review of `{target.NotFound}` didn't find it again, so it can't back a ticket until a review finds it or it is dropped";
        return o;
    }

    /// <summary>The ruling the reply gives this entry, or null with why it doesn't fit (D364: listed, never guessed).</summary>
    static string? RulingFor(RulingEntry e, Said said, bool notCovered, out string? misfit)
    {
        misfit = null;
        if (said.Words is not null)
        {
            if (!e.IsMatch) return "restated";
            misfit = "a match takes yes or no";
            return null;
        }
        var w = said.Word!;
        switch (e.State)
        {
            case "match":
                if (w == "yes") return "confirmed";
                if (w == "no") return "not-a-match";
                misfit = $"the question asked whether the code does what the FSD says, so `{w}` doesn't answer it: reply `Ruling: yes`, `Ruling: no`, or the rule in your words";
                return null;
            case "undocumented":
                // "The code is right" about code no FSD sentence covers means keep it as it is.
                if (w is "keep" or "code-right") return "keep";
                if (w == "remove") return "remove";
                misfit = notCovered || e.KeySection is null
                    ? $"nothing in the FSD covers this, so `{w}` can't be the answer: reply `Ruling: keep`, `Ruling: remove`, or the rule in your own words"
                    : $"this is code the FSD doesn't mention, so `{w}` doesn't answer it: reply `Ruling: keep`, `Ruling: remove`, or the rule in your own words";
                return null;
            default:
                if (w is "code-right" or "fsd-right") return w;
                misfit = $"the question asked whether the code or the FSD is right, so `{w}` doesn't answer it: reply `Ruling: the code is right`, `Ruling: the FSD is right`, or the rule in your own words";
                return null;
        }
    }

    /// <summary>The ticket's own entry (<c>ticket:&lt;KEY&gt;</c>, undocumented), found or added in its place: in the section
    /// after its differences and undocumented code by key, or with the code no section covers, by key, so a refresh moves no line.</summary>
    static (RulingEntry Entry, bool Created) TicketEntry(RulingsFile file, string ticket, string? sectionId, IReadOnlyList<string> evidence, StangentRepo repo)
    {
        var probe = new RulingEntry { KeySection = sectionId, KeyLink = TicketLink + ticket, State = "undocumented" };
        if (file.WithSections.FirstOrDefault(x => x.Entry.Problem is null && x.Entry.Key == probe.Key).Entry is { } existing) return (existing, false);
        var linker = new TruthLinks(repo.Root, GraphStore.Load(GraphStore.PathFor(repo)));
        var e = new RulingEntry
        {
            KeySection = sectionId, KeyLink = probe.KeyLink, State = "undocumented",
            Evidence = [.. evidence], Link = linker.Link(new JsonObject { ["state"] = "undocumented", ["evidence"] = new JsonArray([.. evidence.Select(v => (JsonNode)v)]) }).Link,
            Notes = [$"From {ticket}: no FSD sentence settled this ticket, so the PM answered it in Jira."],
        };
        int Group(RulingEntry x) => x.IsDifference ? 0 : x.IsUndocumented ? 1 : 2;
        var list = sectionId is null ? file.Undocumented : file.Sections.First(s => s.SectionId == sectionId).Entries;
        var at = list.FindIndex(x => sectionId is null ? string.CompareOrdinal(x.KeyLink, e.KeyLink) > 0 : Group(x) > 1 || (Group(x) == 1 && string.CompareOrdinal(x.Key, e.Key) > 0));
        list.Insert(at < 0 ? list.Count : at, e);
        return (e, true);
    }
}

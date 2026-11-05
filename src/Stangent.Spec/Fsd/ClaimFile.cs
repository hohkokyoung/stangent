using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.Evidence;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Fsd;

/// <summary>One claim as <c>claims.json</c> holds it.</summary>
public sealed record StoredClaim(string Id, string Section, int Position, string? Flow, string Kind, string Shape, string Words, string? Problem)
{
    public bool Judged => Kind == FsdClaims.JudgeKinds.Claim;
    public string Normalized => Quotes.Normalize(Words);
}

/// <summary>A document's claims, as they're on file.</summary>
public sealed class ClaimSet(IReadOnlyList<StoredClaim> claims)
{
    readonly Dictionary<string, StoredClaim> byId = claims.ToDictionary(c => c.Id, StringComparer.Ordinal);
    public IReadOnlyList<StoredClaim> All => claims;
    public StoredClaim? Find(string id) => byId.GetValueOrDefault(id);
    public StoredClaim? At(string? section, int position) => claims.FirstOrDefault(c => c.Section == section && c.Position == position);
    public IEnumerable<StoredClaim> Of(string? section) => claims.Where(c => c.Section == section);
    public static IReadOnlyList<IGrouping<string, StoredClaim>> Groups(IEnumerable<StoredClaim> claims) => [.. claims.Where(c => c.Judged).GroupBy(c => c.Normalized, StringComparer.Ordinal)];
}

/// <summary>
/// Permanent claim ids (D145): the <c>claims</c> scope of a document's <c>ids.json</c>, and <c>specs/fsd/&lt;doc&gt;/claims.json</c>. D118's rule of exact signals: the same section, the same
/// normalised words and the same flow, and among exact repeats their order, keeps its id. Anything else gets a new one, and an old one is retired for good. Written by the step before a judge and by
/// <c>fsd claims</c>, from <c>document.json</c> alone, so it works on a document imported before the switch went on and needs no import to catch up.
/// </summary>
public static class ClaimFile
{
    public const string Component = "claim-links", Schema = "fsd-claims.v1.json";
    const string Scope = "claims", Format = "C-{0:D4}";

    /// <summary>On only with what it needs (D121): with <c>fsd-claims</c> off, <c>stangent components</c> says it's blocked, and links are made as before.</summary>
    public static bool On(StangentConfig config) => StangentComponents.IsOn(config, Component) && StangentComponents.IsOn(config, FsdClaims.Component);

    public static string PathIn(string root, string doc) => Path.Combine(FsdImport.DocDir(root, doc), "claims.json");

    public static ClaimSet Load(string root, string doc)
    {
        if (!File.Exists(PathIn(root, doc))) return new ClaimSet([]);
        return new ClaimSet([.. JsonNode.Parse(File.ReadAllText(PathIn(root, doc)))!["claims"]!.AsArray().OfType<JsonObject>().Select(c => new StoredClaim(
            (string)c["id"]!, (string)c["section"]!, (int)c["position"]!, (string?)c["flow"], (string)c["kind"]!, (string)c["shape"]!, (string)c["words"]!, (string?)c["problem"]))]);
    }

    /// <summary>Whether this id was given out and later retired, which is a different thing from one nobody ever gave out.</summary>
    public static bool Retired(string root, string doc, string id) => File.Exists(Path.Combine(FsdImport.DocDir(root, doc), "ids.json")) && FsdVersions.Ledger(root, doc).Removed(Scope).Contains(id);

    /// <summary>Whether the section's text, cut now, still holds these words. Claims are short and a step may be three words, so <see cref="Quotes.InText"/>'s rule of four doesn't apply.</summary>
    public static bool StillIn(string words, JsonObject section) =>
        FsdClaims.Split((string)section["text"]!).Any(c => Quotes.Normalize(c.Words) == Quotes.Normalize(words));

    static string Key(string section, string normalized, string? flow) => string.Join('\u0001', section, normalized, Quotes.Normalize(flow ?? ""));

    /// <summary>Brings the ledger and the file up to the current document. Doing it again changes no byte. Returns the counts.</summary>
    public static JsonObject Sync(string root, string doc)
    {
        using var _ = Lock(root, doc);
        var document = FsdOutline.Load(root, doc);
        FsdVersions.EnsureIds(root, doc, document);
        var ledger = FsdVersions.Ledger(root, doc);
        var old = Load(root, doc).All;
        if (old.Count > 0) ledger.Raise(Scope, Format, old.Max(c => int.Parse(c.Id[2..])));

        var pool = old.GroupBy(c => Key(c.Section, c.Normalized, c.Flow), StringComparer.Ordinal).ToDictionary(g => g.Key, g => new Queue<string>(g.Select(c => c.Id)), StringComparer.Ordinal);
        var live = new List<StoredClaim>();
        var issued = 0;
        foreach (var s in document["sections"]!.AsArray().OfType<JsonObject>().Where(s => ((string)s["text"]!).Trim().Length > 0))
        {
            var section = (string)s["permanentId"]!;
            foreach (var c in FsdClaims.Split((string)s["text"]!))
            {
                string id;
                if (pool.TryGetValue(Key(section, Quotes.Normalize(c.Words), c.Flow), out var kept) && kept.Count > 0) id = kept.Dequeue();
                else { id = ledger.Issue(Scope, Format); issued++; }
                live.Add(new StoredClaim(id, section, c.Position, c.Flow, c.JudgeKind, c.Kind, c.Words, c.Problem));
            }
        }
        // Everything the ledger gave out that holds no claim now is retired, which also covers a claims.json somebody deleted.
        var held = live.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var retired = 0;
        foreach (var id in ledger.Active(Scope).Where(id => !held.Contains(id)).ToList()) { ledger.Remove(Scope, id); retired++; }

        var file = new JsonObject
        {
            ["version"] = 1, ["doc"] = doc,
            ["claims"] = new JsonArray([.. live.Select(c =>
            {
                var o = new JsonObject { ["id"] = c.Id, ["section"] = c.Section, ["position"] = c.Position, ["flow"] = c.Flow, ["kind"] = c.Kind, ["shape"] = c.Shape, ["words"] = c.Words };
                if (c.Problem is not null) o["problem"] = c.Problem;
                return (JsonNode)o;
            })]),
        };
        var errors = SchemaRegistry.Embedded.Validate(Schema, file.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_claims", "claims.json broke fsd-claims v1 (a stangent bug).", ExitCodes.Failed, errors);
        var text = CanonicalJson.Serialize(file);
        if (!File.Exists(PathIn(root, doc)) || File.ReadAllText(PathIn(root, doc)) != text) Quotes.WriteAtomic(PathIn(root, doc), text);

        var judged = live.Where(c => c.Judged).ToList();
        return new JsonObject
        {
            ["claims"] = live.Count, ["toJudge"] = judged.Count, ["setAside"] = live.Count - judged.Count,
            ["setAsideByKind"] = new JsonObject(live.Where(c => !c.Judged).GroupBy(c => c.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => KeyValuePair.Create(g.Key, (JsonNode?)g.Count()))),
            ["groups"] = judged.GroupBy(c => c.Section).Sum(g => ClaimSet.Groups(g).Count), ["issued"] = issued, ["retired"] = retired,
        };
    }

    static FileStream Lock(string root, string doc)
    {
        var path = Path.Combine(root, ".stangent", "fsd", doc, "claims.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (var i = 0; ; i++)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 600) { Thread.Sleep(50); }
        }
    }
}

/// <summary>What a link judge reads and returns with <c>claim-links</c> on (D145): ids out, ids back, and code fills the words.</summary>
public static class ClaimLinks
{
    public const string Instructions = "link-judge-claims.md", FinerInstructions = "link-judge-claims-finer.md";
    public const string OutputSchema = "link-judge-claims-output.v1.json", FinerOutputSchema = "link-judge-claims-finer-output.v1.json";

    /// <summary>
    /// The judge's input: the section's permanent id and title, its claims by id with repeats shown once, and the pages as ever. No number, so a section that only moved keeps its paid links (D117).
    /// An attachment's text isn't cut into claims, so it isn't here (D145's cost).
    /// </summary>
    public static string Bundle(ClaimSet claims, JsonObject section, string doc, IEnumerable<string> pages, SpecIndex index, bool finer)
    {
        var sb = new StringBuilder(SharedHead.Step(finer ? FinerInstructions : Instructions, finer ? FinerOutputSchema : OutputSchema));
        var own = claims.Of((string?)section["permanentId"]).ToList();
        sb.Append($"# The FSD section\n\n- document: `{doc}`\n- section: `{(string)section["permanentId"]!}` {(string)section["title"]!}\n\n# Its claims\n\n");
        var groups = ClaimSet.Groups(own);
        // Each flow is written once and named by number, since on a document with long flow names and many repeats the names would outweigh the claims.
        var flows = groups.SelectMany(g => g).Select(c => c.Flow).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (flows.Count > 0)
        {
            sb.Append("The flows:\n\n");
            for (var i = 0; i < flows.Count; i++) sb.Append($"- F{i + 1}: {flows[i]}\n");
            sb.Append('\n');
        }
        foreach (var group in groups)
        {
            var under = group.Select(c => c.Flow).OfType<string>().Distinct(StringComparer.Ordinal).Select(f => $"F{flows.IndexOf(f) + 1}").ToList();
            sb.Append($"- **{string.Join(", ", group.Select(c => c.Id))}**{(under.Count == 0 ? "" : $" ({string.Join(", ", under)})")} {group.Key}\n");
        }
        var context = own.Where(c => c.Kind is FsdClaims.JudgeKinds.ColumnHeading or FsdClaims.JudgeKinds.LabelRow).ToList();
        if (context.Count > 0)
        {
            sb.Append("\n# Headings and labels in the section\n\nFor reading the claims above. They aren't claims, and need no link.\n\n");
            foreach (var line in context.Select(c => c.Normalized).Distinct(StringComparer.Ordinal)) sb.Append($"- {line}\n");
        }
        return sb.Append(LinkRun.PagesBlock(pages, index)).ToString();
    }

    /// <summary>
    /// Checks a judge's link and fills its quotes from the ids. An id that isn't listed is an error, which takes the usual one retry. <c>fsdQuote</c> here is for the second check to read;
    /// each entry gets its own claim's words. A statement's quote is its whole text.
    /// </summary>
    public static List<string> Fill(JsonObject link, ClaimSet claims, string? section, SpecIndex index)
    {
        var errors = new List<string>();
        var state = (string)link["state"]!;
        var ids = (link["claims"] as JsonArray)?.Select(c => (string)c!).Distinct(StringComparer.Ordinal).ToList();
        if (state == "undocumented")
        {
            if (ids is not null) errors.Add("an undocumented link names no claim");
        }
        else if (ids is null) errors.Add($"a {state} link needs claims");
        else
        {
            var words = new List<string>();
            foreach (var id in ids)
            {
                if (claims.Find(id) is { Judged: true } c && c.Section == section) words.Add(c.Normalized);
                else errors.Add($"{id} isn't one of the claims listed for this section");
            }
            link["claims"] = new JsonArray([.. ids.Select(i => (JsonNode)i)]);
            link["fsdQuote"] = string.Join(" / ", words.Distinct(StringComparer.Ordinal));
        }
        FillPageQuote(link, index);
        return errors;
    }

    static void FillPageQuote(JsonObject link, SpecIndex index)
    {
        if ((string?)link["page"] is { } page && (string?)link["statement"] is { } statement && index.Statement(page, statement) is { } found) link["pageQuote"] = (string)found["text"]!;
        else link.Remove("pageQuote");
    }

    /// <summary>
    /// An answer on a line applies to all its claims: every id nobody named takes the line's first answer. A flow answered separately keeps its own. Two different states for one claim and one statement can't
    /// both be recorded as what the judge meant, so that's an error. Fills in <c>claims</c> and returns the errors by link.
    /// </summary>
    public static Dictionary<JsonObject, List<string>> Expand(IReadOnlyList<JsonObject> links, ClaimSet claims, string? section)
    {
        var named = links.SelectMany(l => (l["claims"] as JsonArray ?? []).Select(c => (string)c!)).ToHashSet(StringComparer.Ordinal);
        foreach (var group in ClaimSet.Groups(claims.Of(section)))
        {
            var first = links.FirstOrDefault(l => (l["claims"] as JsonArray ?? []).Any(c => group.Any(g => g.Id == (string)c!)));
            if (first is null) continue;
            foreach (var c in group.Where(c => !named.Contains(c.Id))) first["claims"]!.AsArray().Add((JsonNode)c.Id);
        }
        var errors = new Dictionary<JsonObject, List<string>>();
        var said = new Dictionary<(string, string?, string?), string>();
        foreach (var l in links)
            foreach (var id in (l["claims"] as JsonArray ?? []).Select(c => (string)c!))
            {
                var key = (id, (string?)l["page"], (string?)l["statement"]);
                if (said.TryGetValue(key, out var state) && state != (string)l["state"]!)
                    (errors.TryGetValue(l, out var list) ? list : errors[l] = []).Add($"{id} is both {state} and {(string)l["state"]!} on the same statement");
                else said[key] = (string)l["state"]!;
            }
        return errors;
    }

    /// <summary>The second check raised a finding from an agree link. It speaks of claims through the link, so code takes the quotes from the ids again, and whatever words it copied count for nothing.</summary>
    public static void FillRaised(JsonObject raised, JsonObject link, SpecIndex index)
    {
        if ((string)raised["state"]! == "undocumented") { raised.Remove("fsdQuote"); raised.Remove("claims"); }
        else { raised["claims"] = link["claims"]!.DeepClone(); raised["fsdQuote"] = link["fsdQuote"]!.DeepClone(); }
        FillPageQuote(raised, index);
    }

    /// <summary>What's left without an answer: claims to judge that no link names.</summary>
    public static JsonArray Unanswered(ClaimSet claims, string? section, IEnumerable<JsonObject> links)
    {
        var named = links.SelectMany(l => (l["claims"] as JsonArray ?? []).Select(c => (string)c!)).ToHashSet(StringComparer.Ordinal);
        return new JsonArray([.. claims.Of(section).Where(c => c.Judged && !named.Contains(c.Id)).Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["flow"] = c.Flow, ["words"] = c.Words })]);
    }

    /// <summary>
    /// Whether a link from before the switch is the same link as this one (D145), by exact signals: the same state, page and statement, the old FSD quote inside this claim's words whole-word, and the old page quote inside the
    /// statement's text now. A quote that spans two claims doesn't match, and nor does a link about a requirement.
    /// </summary>
    public static bool SameLink(JsonObject old, JsonObject fresh)
    {
        if (old["fsd"]?["claim"] is not null || old["fsd"]?["requirement"] is not null) return false;
        if ((string?)old["state"] != (string?)fresh["state"] || (string?)old["page"]?["id"] != (string?)fresh["page"]?["id"] || (string?)old["page"]?["statement"] != (string?)fresh["page"]?["statement"]) return false;
        if ((string?)old["page"]?["quote"] is { } page && !Inside(page, (string?)fresh["page"]?["quote"] ?? "")) return false;
        return (string?)old["fsd"]?["quote"] is { } quote ? Inside(quote, (string?)fresh["fsd"]?["quote"] ?? "") : (string?)fresh["fsd"]?["claim"] == TraceFile.NoClaim;
    }

    /// <summary>The quote, normalised, is in the text, starting and ending at a word's edge.</summary>
    static bool Inside(string quote, string text)
    {
        var q = Quotes.Normalize(quote);
        var t = Quotes.Normalize(text);
        if (q.Length == 0) return false;
        for (var at = t.IndexOf(q, StringComparison.Ordinal); at >= 0; at = t.IndexOf(q, at + 1, StringComparison.Ordinal))
        {
            var starts = at == 0 || !char.IsLetterOrDigit(t[at - 1]) || !char.IsLetterOrDigit(q[0]);
            var end = at + q.Length;
            if (starts && (end == t.Length || !char.IsLetterOrDigit(t[end]) || !char.IsLetterOrDigit(q[^1]))) return true;
        }
        return false;
    }
}

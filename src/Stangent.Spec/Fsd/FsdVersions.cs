using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Core.Evidence;
using Stangent.Graph;

namespace Stangent.Spec.Fsd;

/// <summary>
/// A document's permanent ids (D117): each section has one beside its number, from a ledger per document. A new file is compared
/// with the committed FSD and taken section by section (<see cref="FsdCompare"/>, D385); the candidate and promote step went with D388.
/// </summary>
public static class FsdVersions
{
    const string Scope = "sections", Format = "S-{0:D4}";

    /// <summary>The document's ledger. The lock stays out of <c>specs/</c>, which is committed.</summary>
    public static PermanentIds Ledger(string root, string doc) =>
        new(Path.Combine(FsdImport.DocDir(root, doc), "ids.json"), Path.Combine(root, ".stangent", "fsd", doc, "ids.lock"));

    /// <summary>Gives each section its next id. A ledger deleted or behind catches up with the document first, so a live id is never given again.</summary>
    internal static void IssueIds(string root, string doc, JsonObject current, IEnumerable<JsonObject> sections)
    {
        var ledger = Ledger(root, doc);
        var highest = current["sections"]!.AsArray().OfType<JsonObject>().Select(s => (string?)s["permanentId"]).OfType<string>().Select(s => int.Parse(s[2..])).DefaultIfEmpty(0).Max();
        if (highest > 0) ledger.Raise(Scope, Format, highest);
        foreach (var s in sections) s["permanentId"] = ledger.Issue(Scope, Format);
    }

    /// <summary>A document from before gets what later versions added, as <c>fsd migrate</c> moves it (phase fs layer 2): permanent ids (v2),
    /// in document order, tables and structured problems (v3). Changes only the record in hand, which the caller saves; numbers, text and
    /// hashes stay, so saved AI work stays valid. A v4 document's ids are on its headings, and <c>fsd check</c> gives a new heading one.</summary>
    public static int EnsureIds(string root, string doc, JsonObject current)
    {
        var missing = current["sections"]!.AsArray().OfType<JsonObject>().Where(s => s["permanentId"] is null).ToList();
        if (missing.Count > 0) IssueIds(root, doc, current, missing);
        if (current["tables"] is null) FsdObjects.Assign(root, doc, null, current);
        ImportProblems.Upgrade(current);
        ImportProblems.Attach(current);
        return missing.Count;
    }

    internal static void Validate(JsonObject doc) => FsdDocument.Validate(doc);

    /// <summary>Who's deciding: from git, never typed (D58). With no name set there's nothing true to record, so it stops.</summary>
    public static string GitPerson(string root)
    {
        var name = Git.Run(root, "config user.name")?.Trim();
        return !string.IsNullOrEmpty(name) ? name
            : throw new StangentException("no_git_identity", "A person decides this, and their name comes from git: set git config user.name first (D58).", ExitCodes.Usage);
    }
}

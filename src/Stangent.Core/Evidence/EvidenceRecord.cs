using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Stangent.Core.Evidence;

public enum CitationKind { Code, Document, Page, Test, Url }

/// <summary>How far a claim is backed: <c>Ran</c> a test or run shows it, <c>Stated</c> a person or the FSD said it,
/// <c>Read</c> read in the code only, <c>Guess</c> nothing backs it yet (D113).</summary>
public enum EvidenceStrength { Ran, Stated, Read, Guess }

public enum CheckerKind { Code, Agent, Person }

public sealed record LineRange(int Start, int End);

public sealed record Citation
{
    public required CitationKind Kind { get; init; }
    /// <summary><c>repo/path</c>, or a document id.</summary>
    public required string Ref { get; init; }
    public LineRange? Lines { get; init; }
    public string? Symbol { get; init; }
    public string? Quote { get; init; }
    /// <summary>What a reader must know about these lines, such as a partial type's first part only.</summary>
    public string? Note { get; init; }
}

public sealed record EvidenceChecker
{
    public required CheckerKind Kind { get; init; }
    public required string Name { get; init; }
    public string? InputsHash { get; init; }
}

/// <summary>One claim's proof, contract <c>evidence</c> v1 (D113). Consumers move onto it when each is rebuilt (D112).</summary>
public sealed record EvidenceRecord
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public required string Id { get; init; }
    public required Citation Citation { get; init; }
    public string? Commit { get; init; }
    public string? ContentHash { get; init; }
    public required EvidenceStrength Strength { get; init; }
    public required IReadOnlyList<EvidenceChecker> CheckedBy { get; init; }

    /// <summary>Reads evidence that passes the validator; anything else is refused with what's wrong in the details.</summary>
    public static EvidenceRecord Parse(string json)
    {
        var errors = EvidenceValidator.Validate(json);
        if (errors.Count > 0) throw new StangentException("invalid_evidence", "This evidence breaks the evidence contract.", details: errors);
        return JsonSerializer.Deserialize<EvidenceRecord>(json, Options)!;
    }

    public IReadOnlyList<string> Validate() => EvidenceValidator.Validate(ToJson());

    /// <summary>Canonical form: the same evidence always gives the same bytes.</summary>
    public string ToJson() => CanonicalJson.SerializeCompact(JsonSerializer.SerializeToNode(this, Options));
}

/// <summary>The contract's schema plus what a schema can't say. The AI proposes, this checks (D18).</summary>
public static class EvidenceValidator
{
    public const string Schema = "evidence.v1.json";

    /// <summary>Errors as "path: what's wrong" lines; empty when valid.</summary>
    public static IReadOnlyList<string> Validate(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { return ["#: not valid JSON"]; }

        var errors = SchemaRegistry.Embedded.Validate(Schema, json);
        // The rules below read the fields, so the shape must hold first.
        if (errors.Count > 0)
            return [.. errors.Select(e => e.EndsWith(": NotAnyOf") ? "#: a code or test citation needs a commit, a document citation needs a contentHash" : e)];

        var problems = new List<string>();
        // A regex `$` also matches before a final line break.
        if (((string)node!["id"]!).EndsWith('\n')) problems.Add("id: ends in a line break");
        if (node["citation"]!["lines"] is JsonObject lines && (int)lines["start"]! > (int)lines["end"]!)
            problems.Add("citation.lines: start is after end");

        var strength = (string)node["strength"]!;
        var checkers = node["checkedBy"]!.AsArray().Select(c => (string)c!["kind"]!).ToList();
        if (checkers.Count == 0 && strength != "guess")
            problems.Add("checkedBy: only a guess may have no checker");
        if (strength == "ran" && !checkers.Exists(k => k is "code" or "person"))
            problems.Add("strength: ran needs a checker of kind code or person, not an agent alone");
        return problems;
    }
}

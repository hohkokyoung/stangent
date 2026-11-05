using System.Text;
using Stangent.Core;

namespace Stangent.Spec.Pages;

/// <summary>
/// The instructions and output schema a step's input starts with (D130): one copy, in this program. A bundle on disk holds a
/// pointer on its first line and what's unique to it; <see cref="Assemble"/> gives back the full text, byte for byte, just
/// before a call. Input hashes are taken on the full text, so saved pages and links stay valid. Only the first line is ever
/// read as a pointer, so a line in the app's own source that looks like one is just source.
/// </summary>
public static class SharedHead
{
    const string Open = "⟦stangent shared ", Close = "⟧\n";

    public const string WriterInstructions = "page-writer.md", VerifierInstructions = "page-verifier.md";

    /// <summary>The steps stored this way. A file whose start isn't one of these is stored whole, so nothing is ever lost.</summary>
    static readonly (bool Page, string[] Instructions, string Schema)[] Known =
    [
        (true, [WriterInstructions], Bundles.WriterSchema),
        (false, [VerifierInstructions], Bundles.VerifierSchema),
        (false, ["link-judge.md"], "link-judge-output.v1.json"),
        (false, ["link-judge-finer.md"], "link-judge-finer-output.v1.json"),
        (false, ["requirement-judge.md"], "requirement-judge-output.v1.json"),
        (false, ["requirement-judge-finer.md"], "requirement-judge-finer-output.v1.json"),
        (false, ["link-judge.md", "link-judge-group.md"], "link-judge-group-output.v1.json"),
        (false, ["link-judge-finer.md", "link-judge-group.md"], "link-judge-group-finer-output.v1.json"),
        (false, ["link-judge-claims.md"], "link-judge-claims-output.v1.json"),
        (false, ["link-judge-claims-finer.md"], "link-judge-claims-finer-output.v1.json"),
        (false, ["agree-checker.md"], "agree-checker-output.v1.json"),
        (false, ["requirement-agree-checker.md"], "agree-checker-output.v1.json"),
    ];

    /// <summary>The full head of a step's input. <paramref name="page"/> is the writer's form, one heading level down under its title.</summary>
    public static string Text(bool page, IReadOnlyList<string> instructions, string schema, bool withSchema = true)
    {
        var sb = new StringBuilder(page ? "# stangent page bundle\n\n## Instructions\n\n" : "# Instructions\n\n");
        foreach (var name in instructions) sb.Append(Bundles.Instructions(name));
        sb.Append('\n');
        if (withSchema) sb.Append(page ? "## " : "# ").Append("Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText(schema).TrimEnd('\n')).Append("\n```\n\n");
        return sb.ToString();
    }

    public static string Step(string instructions, string schema) => Text(false, [instructions], schema);

    /// <summary>What goes on disk: the pointer and what's unique. The full text if its start isn't a step this knows.</summary>
    public static string Store(string full)
    {
        foreach (var (page, instructions, schema) in Known)
        {
            var head = Text(page, instructions, schema);
            if (full.StartsWith(head, StringComparison.Ordinal))
                return $"{Open}{(page ? "page" : "step")} instructions={string.Join(',', instructions)} schema={schema}{Close}{full[head.Length..]}";
        }
        return full;
    }

    public static bool IsStored(string text) => text.StartsWith(Open, StringComparison.Ordinal);

    /// <summary>The full text, the same bytes <see cref="Store"/> was given. A file with no pointer comes back as it is.</summary>
    /// <param name="schemaOnce">Leave the schema out: the call sends it another way (J.4). Saved hashes don't move, since they're taken on the full text.</param>
    public static string Assemble(string stored, bool schemaOnce = false)
    {
        if (!IsStored(stored)) return stored;
        var end = stored.IndexOf(Close, StringComparison.Ordinal);
        if (end < 0) throw new StangentException("bad_bundle", "A bundle's first line is an unfinished pointer.");
        var parts = stored[Open.Length..end].Split(' ');
        var known = parts.Length == 3 && parts[1].StartsWith("instructions=", StringComparison.Ordinal) && parts[2].StartsWith("schema=", StringComparison.Ordinal)
            ? Known.FirstOrDefault(k => k.Page == (parts[0] == "page") && string.Join(',', k.Instructions) == parts[1]["instructions=".Length..] && k.Schema == parts[2]["schema=".Length..])
            : default;
        if (known.Schema is null) throw new StangentException("bad_bundle", $"A bundle points at text this stangent doesn't have: {stored[..end]}. Run its prepare step again.");
        return Text(known.Page, known.Instructions, known.Schema, !schemaOnce) + stored[(end + Close.Length)..];
    }
}

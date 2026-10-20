using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;

namespace Stangent.Spec.Pages;

/// <summary>
/// Everything one AI step reads (D60): the instructions, the output schema, the page, its slice with each point's source
/// and label, and the page's previous version. <see cref="InputHash"/> covers everything but the previous version, so
/// recording a page doesn't change its own input: an unchanged hash means no AI call (D18, D55).
/// </summary>
public sealed class Bundle
{
    public required string Text { get; init; }
    public required string InputHash { get; init; }
    /// <summary>Characters before the previous version: what the size cap is measured on.</summary>
    public required int Chars { get; init; }
    /// <summary>The slice's source text, one line per entry, whitespace-normalised: where quotes must come from.</summary>
    public required IReadOnlyList<string> SourceLines { get; init; }
}

public static class Bundles
{
    public const string WriterSchema = "page-writer-output.v1.json";
    public const string VerifierSchema = "page-verifier-output.v1.json";

    /// <summary>Kinds whose whole span is shown. Types show their declaration line only: their members are points of their own.</summary>
    static readonly HashSet<string> WithBody = [NodeKind.Method, NodeKind.Property, NodeKind.Field, NodeKind.Component, NodeKind.Template];

    public static string Instructions(string name)
    {
        using var stream = typeof(Bundles).Assembly.GetManifestResourceStream("instructions/" + name)
            ?? throw new StangentException("missing_instructions", $"No instructions named {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    public static Bundle Build(PageSet set, PageDef page, JsonObject? previous)
    {
        var slice = set.Slices[page.Id];
        var source = set.Source;
        var sb = new StringBuilder();
        sb.Append("# stangent page bundle\n\n");
        sb.Append("## Instructions\n\n").Append(Instructions("page-writer.md")).Append('\n');
        sb.Append("## Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText(WriterSchema).TrimEnd('\n')).Append("\n```\n\n");

        sb.Append("## Page\n\n");
        sb.Append($"- id: `{page.Id}`\n- kind: {page.Kind}\n- entry point: `{page.Entry}`\n- from the code: {page.CodeTitle()}\n");
        sb.Append($"- slice: the walk from the entry point, at most {slice.Depth} lines deep ({set.Settings.Describe()})\n\n");

        // Points listed and shown: code with a body, and the points a page names (endpoints, pages, handlers, tables,
        // config keys, linked components). Types, properties and fields are in the slice but shown inside the code
        // that uses them; listing hundreds of one-line declarations would bury the code that matters (Phase 2 step 2).
        var listed = slice.Nodes.Where(n => Listed(n)).ToList();
        sb.Append("## Symbols\n\n");
        sb.Append("Cite these ids exactly. ✅ the compiler proved how it's reached; 🟡 a detector guessed it. Depth is lines from the entry point. ");
        sb.Append("`stop`: linked but not described here (it has its own page). `handler` and `table`: side effects, one step back.\n\n");
        sb.Append("| id | kind | label | depth | role | lines |\n|---|---|---|---|---|---|\n");
        foreach (var n in listed)
            sb.Append($"| `{n.Node.Id}` | {n.Node.Kind} | {Mark(n.Label)} | {n.Depth} | {n.Role} | {Lines(n.Node)} |\n");

        var ids = listed.Select(n => n.Node.Id).ToHashSet(StringComparer.Ordinal);
        var keyLines = slice.Edges.Where(e => !QuietKinds.Contains(e.Kind) && ids.Contains(e.Source) && ids.Contains(e.Target)).ToList();
        sb.Append("\n## Lines\n\nFrom the dependent to what it depends on. Calls are left out: the source shows them.\n\n");
        foreach (var e in keyLines)
            sb.Append($"- `{e.Source}` {e.Kind} `{e.Target}` {Mark(e.Label)}\n");
        if (keyLines.Count == 0) sb.Append("None besides calls.\n");

        // Source, one block per file in the order the walk reached them; a member inside one already shown isn't repeated.
        sb.Append("\n## Source\n");
        var sourceLines = new List<string>();
        var shown = listed.Where(n => n.Role is "walk" or "handler" && n.Node.File is not null && n.Node.Line > 0).ToList();
        foreach (var file in shown.Select(n => n.Node.File!).Distinct())
        {
            var lines = source.Lines(file);
            if (lines is null) continue;
            var members = shown.Where(n => n.Node.File == file).OrderBy(n => n.Node.Line).ThenByDescending(n => n.Node.EndLine).ToList();
            var block = new StringBuilder();
            var coveredTo = 0;
            foreach (var m in members)
            {
                var from = Math.Min(m.Node.Line, lines.Length);
                var to = WithBody.Contains(m.Node.Kind) && m.Role == "walk" ? Math.Min(Math.Max(m.Node.Line, m.Node.EndLine), lines.Length) : from;
                if (to <= coveredTo) continue; // inside a member already shown
                from = Math.Max(from, coveredTo + 1);
                block.Append($"{Comment(file)} ⟦{m.Node.Id} {Mark(m.Label)} depth {m.Depth}⟧ lines {from}-{to}\n");
                for (var i = from; i <= to; i++)
                {
                    block.Append(lines[i - 1]).Append('\n');
                    if (Quotes.Normalize(lines[i - 1]) is { Length: > 0 } l) sourceLines.Add(l);
                }
                coveredTo = to;
            }
            if (block.Length > 0) sb.Append($"\n### {file}\n\n```{Fence(file)}\n{block}```\n");
        }

        sb.Append("\n## Couldn't see\n\n");
        if (slice.Unknowns.Count == 0) sb.Append("Nothing: every line in the slice was resolved.\n");
        foreach (var u in slice.Unknowns) sb.Append($"- `{u.File}:{u.Line}` {u.Reason} (in `{u.Owner}`)\n");
        if (slice.CutOff > 0) sb.Append($"- the depth limit cut off {slice.CutOff} more point(s) one line further on\n");

        var hashed = sb.ToString();
        sb.Append("\n## Previous version\n\n");
        sb.Append(previous is null ? "None: this is the page's first version.\n" : "```json\n" + CanonicalJson.Serialize(previous) + "```\n");
        return new Bundle { Text = sb.ToString(), InputHash = CanonicalJson.Hash(hashed), Chars = hashed.Length, SourceLines = sourceLines };
    }

    public static string CodeTitle(this PageDef page) => page.Title;

    static readonly HashSet<string> QuietKinds = [EdgeKind.Calls, EdgeKind.Uses, EdgeKind.MapsField];

    static readonly HashSet<string> NamedKinds = [NodeKind.Endpoint, NodeKind.Page, NodeKind.Table, NodeKind.Column, NodeKind.Config];

    static bool Listed(SliceNode n) =>
        n.Role is "stop" or "handler" or "table" || WithBody.Contains(n.Node.Kind) && n.Node.Kind is not (NodeKind.Property or NodeKind.Field)
        || NamedKinds.Contains(n.Node.Kind);

    static string Comment(string file) => Path.GetExtension(file) is ".html" or ".cshtml" ? "<!--" : "//";

    static string Lines(Node n) => n.File is null || n.Line <= 0 ? "" : n.EndLine > n.Line ? $"{n.File}:{n.Line}-{n.EndLine}" : $"{n.File}:{n.Line}";

    static string Mark(Label l) => l == Label.Certain ? "✅" : "🟡";

    static string Fence(string? file) => Path.GetExtension(file ?? "") switch
    {
        ".cs" => "csharp", ".ts" => "ts", ".tsx" => "tsx", ".js" or ".jsx" => "js", ".html" => "html", ".cshtml" => "cshtml", _ => "",
    };
}

/// <summary>How quotes are matched (D18): whitespace runs collapse to one space, ends trimmed, case kept.</summary>
public static class Quotes
{
    public static string Normalize(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>The quote, normalised, is part of one source line, or of consecutive lines joined by a space.</summary>
    public static bool InSource(string quote, IReadOnlyList<string> lines)
    {
        var q = Normalize(quote);
        if (q.Length < 4) return false;
        var parts = q.Split(" ").Length;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(q, StringComparison.Ordinal)) return true;
            // A quote spanning lines: join this line with the next few and look again.
            var joined = lines[i];
            for (var j = i + 1; j < lines.Count && j < i + 1 + parts; j++)
            {
                joined += " " + lines[j];
                if (joined.Contains(q, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }
}

using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;

namespace Stangent.Spec.Pages;

/// <summary>
/// A page's line citations (2.3, contract <c>reference-lines</c> v1): for each symbol its statements cite, <c>path:lines</c> at the graph's
/// commit, or why there are none. Filled by code from the graph, never the AI. They live in a file beside the page, because every input
/// hash that reaches a page reads the page's JSON or the slice: a file nothing hashes can't invalidate saved work.
/// </summary>
public static class PageLines
{
    public const string Schema = "reference-lines.v1.json";

    /// <summary>Holds no <c>sections</c> and no <c>schemaVersion</c>, so the page lists pass over it.</summary>
    public static string FileFor(string pageJson) => pageJson[..^".json".Length] + ".lines.json";

    public static JsonObject? Load(string pageJson) => PageRun.LoadJson(FileFor(pageJson)) as JsonObject;

    /// <summary>One symbol a statement cites, where the line map gives it a file. <see cref="Number"/> is its place among all the statement's citations, from 1.</summary>
    public sealed record CodeLine(int Number, string Symbol, string Path, JsonObject? Lines, string? Note);

    /// <summary>The code a statement cites, from its page's line map. A symbol the map holds no file for is left out; <see cref="CodeLine.Lines"/> is null when it holds only the file.</summary>
    public static IEnumerable<CodeLine> Of(JsonObject statement, JsonObject lines)
    {
        var n = 0;
        foreach (var cite in statement["cites"]!.AsArray().Select(c => (string)c!))
        {
            n++;
            if (lines["symbols"]?[cite] is not JsonObject at || (string?)at["file"] is not { } path) continue;
            var range = at["start"] is null ? null : new JsonObject { ["start"] = (int)at["start"]!, ["end"] = (int)at["end"]! };
            yield return new CodeLine(n, cite, path, range, (string?)at["note"] ?? (string?)at["why"]);
        }
    }

    /// <summary>Why nothing on the page can be cited, or null. A dirty tree doesn't: a file that differs is answered per symbol, and a clean file's lines at the commit are still right.</summary>
    static string? PageWide(CodeGraph graph) =>
        SymbolLines.CommitOf(graph) is null ? "the graph wasn't built at a git commit"
        : graph.DirtyFiles is null || graph.NotInCommit is null ? "the graph doesn't say whether its tree was clean; rebuild it"
        : null;

    public static JsonObject Build(JsonObject page, CodeGraph graph, Func<string, SymbolLines?> lookup)
    {
        var note = PageWide(graph);
        var symbols = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var cite in PageChecks.Statements(page).SelectMany(s => s.Statement["cites"]!.AsArray().Select(c => (string)c!)).Distinct(StringComparer.Ordinal))
            symbols[cite] = note is not null ? new JsonObject { ["why"] = note } : Symbol(lookup(cite));
        return new JsonObject
        {
            ["version"] = 1, ["page"] = (string)page["id"]!, ["commit"] = note is null ? SymbolLines.CommitOf(graph) : null, ["note"] = note,
            ["symbols"] = new JsonObject(symbols),
        };
    }

    static JsonObject Symbol(SymbolLines? at)
    {
        if (at is null) return new JsonObject { ["why"] = "this symbol isn't in the code graph any more" };
        // A file that may not match the commit is never cited; ToCitation holds that rule.
        if (at.ToCitation("") is null) return new JsonObject { ["why"] = at.Note ?? "this symbol has no place in a file" };
        var o = new JsonObject { ["file"] = at.File };
        if (at.StartLine is { } start && at.EndLine is { } end)
        {
            o["start"] = start; o["end"] = end;
            // Citable lines carry a note only when they aren't the whole of it: a partial type's first part.
            if (at.Note is { } note) o["note"] = note;
        }
        else o["why"] = at.Note ?? "the graph doesn't hold this symbol's lines";
        return o;
    }

    public static void Write(string pageJson, JsonObject lines)
    {
        var errors = SchemaRegistry.Embedded.Validate(Schema, lines.ToJsonString());
        if (errors.Count > 0) throw new StangentException("invalid_lines", "A page's line citations broke reference-lines v1 (a stangent bug).", ExitCodes.Failed, errors);
        Quotes.WriteAtomic(FileFor(pageJson), CanonicalJson.Serialize(lines));
    }

    /// <summary><c>path:12-30</c>, <c>path:12</c>, or the path alone when the graph holds only the file.</summary>
    public static string? Cite(JsonObject? symbol) => (string?)symbol?["file"] is not { } file ? null
        : symbol["start"] is null ? file
        : (int)symbol["start"]! == (int)symbol["end"]! ? $"{file}:{(int)symbol["start"]!}"
        : $"{file}:{(int)symbol["start"]!}-{(int)symbol["end"]!}";

    /// <summary>
    /// Fills citations for pages already recorded, and adopts their statement ids, with no AI. A page's JSON isn't touched, so nothing
    /// hashed changes. Its Markdown is rendered again, linking to the pages that are recorded.
    /// </summary>
    public static JsonObject Refresh(string root, CodeGraph graph, string? only, bool on = true, StangentConfig? config = null)
    {
        if (!on) return new JsonObject { ["lineCitations"] = "off", ["pages"] = 0, ["note"] = "line-citations is off, so no citation was written." };
        var paths = new SpecPaths(root);
        var files = new List<(string File, JsonObject Page)>();
        if (Directory.Exists(paths.ReferenceDir))
            foreach (var file in Directory.EnumerateFiles(paths.ReferenceDir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                if (file != paths.FailedFile && PageRun.LoadJson(file) is JsonObject p && p["sections"] is not null)
                    files.Add((file, p));
        var pathOf = files.ToDictionary(f => (string)f.Page["id"]!, f => Path.GetRelativePath(paths.ReferenceDir, f.File)[..^".json".Length].Replace('\\', '/'), StringComparer.Ordinal);
        var chosen = only is null ? files : [.. files.Where(f => (string)f.Page["id"]! == only || pathOf[(string)f.Page["id"]!] == only)];
        if (only is not null && chosen.Count == 0) throw new StangentException("not_found", $"No recorded page '{only}'.", ExitCodes.NotFound);

        var lookup = SymbolLines.Lookup(graph);
        var distinct = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        int symbols = 0, withLines = 0;
        var why = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (file, page) in chosen)
        {
            StatementIds.Adopt(page, StatementIds.Ledger(root, file));
            var lines = Build(page, graph, lookup);
            Write(file, lines);
            Quotes.WriteAtomic(file[..^".json".Length] + ".md", PageRender.Markdown(page, id => pathOf.GetValueOrDefault(id), lines, PageCorrections.Marks(root, (string)page["id"]!, config)));
            foreach (var (id, s) in lines["symbols"]!.AsObject())
            {
                distinct[id] = s!.AsObject();
                symbols++;
                if (s!["start"] is not null) withLines++;
                else
                {
                    var reason = (string)s["why"]!;
                    why[reason] = why.GetValueOrDefault(reason) + 1;
                }
            }
        }
        PageNavigation.Write(paths);
        return new JsonObject
        {
            ["commit"] = SymbolLines.CommitOf(graph), ["pages"] = chosen.Count,
            ["citations"] = new JsonObject { ["total"] = symbols, ["withLines"] = withLines, ["withoutLines"] = symbols - withLines },
            ["symbols"] = new JsonObject { ["total"] = distinct.Count, ["withLines"] = distinct.Values.Count(s => s["start"] is not null), ["withoutLines"] = distinct.Values.Count(s => s["start"] is null) },
            ["why"] = new JsonObject(why.Select(w => KeyValuePair.Create(w.Key, (JsonNode?)w.Value))),
        };
    }
}

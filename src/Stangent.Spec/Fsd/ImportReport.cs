using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Fsd;

/// <summary>
/// What an import came to (D122): a verdict from its structured problems, and <c>import-report.md</c>, grouped by kind with counts, made from <c>document.json</c>.
/// <c>failed</c> is an import that stopped, so nothing was written and the verdict is only in its envelope. No step reads the report back, and it's in no hash.
/// </summary>
public static class ImportReport
{
    public const string FileName = "import-report.md", Ok = "ok", Warnings = "ok-with-warnings", Failed = "failed";

    /// <summary>The codes that mean this file was read and couldn't come in, as opposed to a mistake in the command.</summary>
    public static readonly IReadOnlySet<string> Stops = new HashSet<string>(StringComparer.Ordinal)
        { "no_structure", "no_text", "pdf_locked", "invalid_pdf", "invalid_docx", "docx_locked", "docx_tracked_changes", "document_labelled" };

    public static string Verdict(JsonObject doc) => doc["problems"]!.AsArray().Count == 0 ? Ok : Warnings;

    public static string Markdown(JsonObject doc)
    {
        var problems = doc["problems"]!.AsArray().OfType<JsonObject>().ToList();
        var original = doc["original"]!;
        var sb = new StringBuilder($"# Import report: {(string)doc["title"]!} (`{(string)doc["id"]!}`)\n\n");
        sb.Append("Made by code from `document.json`. Nothing reads it back, and it's in no hash.\n\n");
        sb.Append($"**Verdict: {Verdict(doc)}.** {(problems.Count == 0 ? "Nothing to read." : $"{problems.Count} things couldn't be brought in exactly, listed below.")}\n\n");
        sb.Append($"- File: `{(string)original["fileName"]!}`, `{((string)original["hash"]!)[..12]}`, in archive `{(string)original["archive"]!}` at `{(string)original["path"]!}`\n");
        sb.Append($"- Sections: {doc["sections"]!.AsArray().Count}. Pictures: {doc["images"]!.AsArray().Count}.");
        if (doc["tables"] is JsonArray tables) sb.Append($" Tables: {tables.Count}.");
        sb.Append('\n');
        foreach (var label in (doc["labels"]?.AsArray() ?? []).OfType<JsonObject>())
            sb.Append($"- Sensitivity label `{(string?)label["name"] ?? (string)label["id"]!}`, allowed by {(string)label["allowedBy"]!}\n");
        if (problems.Count == 0) return sb.ToString();

        // Words that didn't arrive come first; the rest by name, so the same document reads the same.
        var kinds = problems.GroupBy(p => (string)p["kind"]!).OrderBy(g => g.Key == "completeness" ? 0 : 1).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        sb.Append("\n| Kind | Count |\n|---|---|\n");
        foreach (var g in kinds) sb.Append($"| {g.Key} | {g.Count()} |\n");
        foreach (var g in kinds)
        {
            sb.Append($"\n## {g.Key} ({g.Count()})\n\n");
            foreach (var p in g) sb.Append($"- {(string)p["detail"]!}\n");
        }
        return sb.ToString();
    }

    /// <summary>Writes it into the folder that holds this version; left alone when it already says this.</summary>
    public static void Write(string dir, JsonObject doc)
    {
        var path = Path.Combine(dir, FileName);
        var text = Markdown(doc);
        if (!File.Exists(path) || File.ReadAllText(path) != text) Quotes.WriteAtomic(path, text);
    }

    public static JsonObject AddTo(JsonObject result, JsonObject doc, string relativeDir)
    {
        result["verdict"] = Verdict(doc);
        result["report"] = $"{relativeDir}/{FileName}";
        return result;
    }
}

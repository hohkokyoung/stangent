using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Spec.Trace;

/// <summary>
/// <c>stangent trace links</c> (D133): each link of a document with its page statement and the code lines that statement cites, looked up
/// when read and stored nowhere. The lines are the page's citations, so they say where to look, not that the AI compared the sentence with
/// the code. It only reads: nothing here writes a project file or takes a lock; the command leaves its usual telemetry event.
/// </summary>
public static class TraceLinks
{
    public const string CodeFrom = "page-citations";

    public static JsonObject Run(string root, string doc, string? section)
    {
        var document = FsdOutline.Load(root, doc);
        var only = section is null ? null : (string)FsdOutline.Find(document, section)["id"]!;
        var index = SpecIndex.Load(root);
        var links = new JsonArray();
        var byState = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var withCode = 0;
        foreach (var e in TraceFile.Load(root)["entries"]!.AsArray().OfType<JsonObject>())
        {
            if ((string?)e["fsd"]?["doc"] != doc || (only is not null && (string?)e["fsd"]?["section"] != only)) continue;
            var link = Link(e, index);
            links.Add((JsonNode)link);
            // Only what's there, so a file with no flag reads as it always has.
            if (TraceFindings.SecurityView(e) is { } flag) link["security"] = flag;
            var state = (string)e["state"]!;
            byState[state] = byState.GetValueOrDefault(state) + 1;
            if (link["code"]!.AsArray().Count > 0) withCode++;
        }
        var result = new JsonObject
        {
            ["doc"] = doc, ["section"] = only, ["links"] = links,
            ["counts"] = new JsonObject
            {
                ["links"] = links.Count, ["byState"] = new JsonObject(byState.Select(s => KeyValuePair.Create(s.Key, (JsonNode?)s.Value))),
                ["withCode"] = withCode, ["withoutCode"] = links.Count - withCode,
            },
        };
        var flags = links.Where(k => k!["security"] is not null).Select(k => (string?)k!["security"]!["answer"]).ToList();
        if (flags.Count > 0) result["counts"]!["securityUnanswered"] = flags.Count(a => a == "unanswered");
        return result;
    }

    static JsonObject Link(JsonObject e, SpecIndex index)
    {
        var pageId = (string?)e["page"]?["id"];
        var statementId = (string?)e["page"]?["statement"];
        var map = pageId is not null && index.PageFiles.TryGetValue(pageId, out var file) ? PageLines.Load(file) : null;
        var statement = pageId is not null && statementId is not null ? index.Statement(pageId, statementId) : null;
        var code = new JsonArray();
        if (statement is not null && map is not null && (string?)map["commit"] is not null)
            foreach (var at in PageLines.Of(statement, map).Where(at => !IsPage(at.Symbol)))
            {
                var line = new JsonObject { ["path"] = at.Path, ["lines"] = at.Lines, ["symbol"] = at.Symbol };
                // Whatever limits these lines goes with them, as it does on a finding's evidence.
                if (at.Note is not null) line["note"] = at.Note;
                code.Add((JsonNode)line);
            }
        return new JsonObject
        {
            ["id"] = (string)e["id"]!, ["state"] = (string)e["state"]!, ["finding"] = (string?)e["finding"],
            ["section"] = (string?)e["fsd"]?["section"], ["fsdQuote"] = (string?)e["fsd"]?["quote"],
            ["page"] = pageId is null ? null : new JsonObject { ["id"] = pageId, ["statement"] = statementId, ["quote"] = (string?)e["page"]?["quote"] },
            ["code"] = code,
            ["codeFrom"] = code.Count > 0 ? CodeFrom : null,
            ["codeNote"] = code.Count > 0 ? null
                : statementId is null ? "no page statement"
                : statement is null ? "statement not on the page"
                : map is null ? "page has no line map"
                : "no code cited",
            ["commit"] = (string?)map?["commit"],
        };
    }

    /// <summary>A screen in the graph has a place in a file, but it's a page, not code a statement rests on.</summary>
    static bool IsPage(string symbol) => symbol.StartsWith("page:", StringComparison.Ordinal);
}

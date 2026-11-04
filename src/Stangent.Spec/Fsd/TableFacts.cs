using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Pages;
using Stangent.Spec.Trace;
using YamlDotNet.RepresentationModel;

namespace Stangent.Spec.Fsd;

/// <summary>One line of <c>specs/fsd/&lt;doc&gt;/table-map.yaml</c>: which column of which table means what.</summary>
public sealed record TableMapping(string Table, string? Sheet, string Column, string Kind, string Target, string? ValueColumn)
{
    public string Key => $"{Table}\u0001{Sheet}\u0001{Column}\u0001{Kind}";
}

/// <summary>The mapping file a person writes or approves (C.2, D124), contract <c>table-map</c> v1. An AI may propose one later; not now.</summary>
public sealed record TableMap(IReadOnlyList<TableMapping> Tables, IReadOnlyDictionary<string, string> Aliases)
{
    public static string PathIn(string root, string doc) => Path.Combine(FsdImport.DocDir(root, doc), "table-map.yaml");

    public static TableMap? Load(string root, string doc)
    {
        if (!File.Exists(PathIn(root, doc))) return null;
        var stream = new YamlStream();
        try { stream.Load(new StringReader(File.ReadAllText(PathIn(root, doc)))); }
        catch (YamlDotNet.Core.YamlException e) { throw new StangentException("invalid_table_map", $"table-map.yaml isn't valid YAML: {e.Message}"); }
        var json = stream.Documents.Count == 0 ? null : TraceFile.ToJson(stream.Documents[0].RootNode, "") as JsonObject;
        var problems = json is null ? ["it's empty"] : SchemaRegistry.Embedded.Validate("table-map.v1.json", json.ToJsonString());
        if (problems.Count > 0) throw new StangentException("invalid_table_map", $"table-map.yaml: {string.Join("; ", problems)}");
        var tables = (json!["tables"]?.AsArray() ?? []).OfType<JsonObject>().Select(t => new TableMapping((string)t["table"]!, (string?)t["sheet"], (string)t["column"]!, (string)t["kind"]!, (string)t["target"]!, (string?)t["valueColumn"])).ToList();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in (json["aliases"]?.AsArray() ?? []).OfType<JsonObject>()) aliases[(string)a["fsd"]!] = (string)a["code"]!;
        return new TableMap(tables, aliases);
    }
}

/// <summary>
/// Compares a document's tables with facts from the code, by code, with no AI (C.2, D124). This is a second route to code beside pages: D68 kept direct links out as noisy and that question is
/// still the owner's, so it's off by default and every result says which route it took. A name matches in any case and nothing else: any other difference is declared as an alias.
/// </summary>
public static class TableFacts
{
    public const string Route = "table-facts";
    const string RouteLabel = "table facts compared by code: a second route to code beside the pages, off by default, D68's question still open for the owner";

    sealed record CodeFact(string Name, string? Value, string Symbol, string? File, int Line, int EndLine, int FrontendFields = 0);
    sealed record CodeTarget(string Symbol, string? File, int Line, int EndLine, List<CodeFact> Facts);

    public static JsonObject Off() => new()
    {
        ["compared"] = false, ["off"] = "table-facts is off (fsd.compareTables), so nothing was compared or written. That isn't a pass.",
    };

    public static JsonObject Compare(string root, string doc, CodeGraph graph)
    {
        var tables = FsdTables.All(root, doc);
        var map = TableMap.Load(root, doc);
        var result = new JsonObject { ["compared"] = true, ["doc"] = doc, ["route"] = Route, ["label"] = RouteLabel };
        var problems = new JsonArray();
        var compared = new JsonArray();
        var entries = new List<JsonObject>();
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var lines = new CodeLines(graph);
        foreach (var m in map?.Tables ?? [])
        {
            var (done, why) = One(root, doc, m, tables, map!, graph, lines, entries);
            if (done is not null) compared.Add((JsonNode)done);
            if (why is null) continue;
            // What it couldn't compare says nothing about what's on file, so a finding from an earlier run stays.
            failed.Add(m.Key);
            problems.Add((JsonNode)new JsonObject { ["table"] = m.Table, ["sheet"] = m.Sheet, ["column"] = m.Column, ["why"] = why });
        }
        if (map is null) result["noMap"] = $"no {Path.GetRelativePath(root, TableMap.PathIn(root, doc)).Replace('\\', '/')}: write one, naming each table, column, kind and target, to compare anything";
        else
        {
            bool Replaced(JsonObject e) => (string?)e["state"] == TraceFile.TableFact && (string?)e["fsd"]?["doc"] == doc
                && !failed.Contains($"{(string?)e["table_fact"]?["table"]}\u0001{(string?)e["table_fact"]?["sheet"]}\u0001{(string?)e["table_fact"]?["column"]}\u0001{(string?)e["table_fact"]?["kind"]}");
            LinkRun.ApplyEntries(root, doc, null, entries, Replaced);
        }
        var mapped = (map?.Tables ?? []).Select(m => (m.Table, m.Sheet)).ToHashSet();
        result["counts"] = new JsonObject
        {
            ["matches"] = compared.Sum(c => c!["matches"]!.AsArray().Count), ["differs"] = compared.Sum(c => c!["differs"]!.AsArray().Count),
            ["fsdOnly"] = compared.Sum(c => c!["fsdOnly"]!.AsArray().Count), ["codeOnly"] = compared.Sum(c => c!["codeOnly"]!.AsArray().Count),
            ["findings"] = entries.Count, ["unmapped"] = tables.Count(t => !mapped.Contains((t.Id, t.Sheet))), ["problems"] = problems.Count,
        };
        result["tables"] = compared;
        // Nothing is silently unchecked: every table no line names is listed, and each attachment no sheet could be read from.
        result["unmapped"] = new JsonArray([.. tables.Where(t => !mapped.Contains((t.Id, t.Sheet))).Select(t => (JsonNode)FsdTables.Describe(t))]);
        result["attachmentsNotRead"] = FsdTables.NotRead(root, doc);
        result["problems"] = problems;
        result["notDone"] = new JsonArray("constants", "SQL seed data");
        return result;
    }

    static (JsonObject? Done, string? Why) One(string root, string doc, TableMapping m, List<FsdTable> tables, TableMap map, CodeGraph graph, CodeLines lines, List<JsonObject> entries)
    {
        var named = tables.Where(t => t.Id == m.Table && (m.Sheet is null || t.Sheet == m.Sheet)).ToList();
        if (named.Count == 0) return (null, m.Sheet is null ? $"the document has no table {m.Table}" : $"{m.Table} has no sheet {m.Sheet}");
        if (named.Count > 1) return (null, $"{m.Table} has {named.Count} sheets ({string.Join(", ", named.Select(t => t.Sheet))}): name one with `sheet`");
        var table = named[0];
        var key = Column(table, m.Column);
        if (key < 0) return (null, $"{table.Name} has no column \"{m.Column}\": its columns are {string.Join(", ", table.Columns)}");
        var value = m.ValueColumn is null ? -1 : Column(table, m.ValueColumn);
        if (m.ValueColumn is not null && value < 0) return (null, $"{table.Name} has no column \"{m.ValueColumn}\": its columns are {string.Join(", ", table.Columns)}");
        if (m.ValueColumn is not null && m.Kind != "enum-member") return (null, "valueColumn is for enum-member only: the graph holds no value for an API field");
        var (target, why) = m.Kind == "api-field" ? ApiFields(graph, m.Target) : EnumMembers(root, graph, m.Target);
        if (target is null) return (null, why);
        if (table.Section is null) return (null, $"{table.Name} is attached to no section, so a difference in it has nowhere to be recorded");

        var byName = target.Facts.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var matches = new JsonArray();
        var differs = new JsonArray();
        var fsdOnly = new JsonArray();
        var skipped = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonObject Item(FsdTableRow? row, string? name, CodeFact? fact, bool alias = false)
        {
            var o = new JsonObject();
            if (row is not null) o["row"] = row.Row;
            if (name is not null) o["fsd"] = name;
            if (fact is not null) { o["code"] = fact.Name; o["at"] = At(fact, target); }
            if (alias) o["alias"] = true;
            return o;
        }
        foreach (var row in table.Rows)
        {
            var name = key < row.Cells.Count ? row.Cells[key].Trim() : "";
            if (name.Length == 0) { skipped.Add((JsonNode)new JsonObject { ["row"] = row.Row, ["why"] = "no name in this column" }); continue; }
            if (!seen.Add(name)) { skipped.Add((JsonNode)new JsonObject { ["row"] = row.Row, ["fsd"] = name, ["why"] = "the same name is on an earlier row" }); continue; }
            var fact = byName.GetValueOrDefault(name);
            var alias = fact is null && map.Aliases.TryGetValue(name, out var aliased) && byName.TryGetValue(aliased, out fact);
            if (fact is null)
            {
                fsdOnly.Add((JsonNode)Item(row, name, null));
                entries.Add(Finding(doc, m, table, row, key, "fsd-only", name, null, null, null, null, target, lines, $"{table.Name}, {m.Column}: \"{name}\" is in the FSD and not in {m.Target}"));
                continue;
            }
            claimed.Add(fact.Name);
            var item = Item(row, name, fact, alias);
            var stated = value >= 0 && value < row.Cells.Count ? row.Cells[value].Trim() : null;
            if (fact.FrontendFields > 0) item["frontendFields"] = fact.FrontendFields;
            if (value >= 0 && fact.Value is null) item["valueNotKnown"] = "the code's value can't be worked out, so it wasn't compared";
            if (value >= 0 && fact.Value is not null && stated != fact.Value)
            {
                item["fsdValue"] = stated;
                item["codeValue"] = fact.Value;
                differs.Add((JsonNode)item);
                entries.Add(Finding(doc, m, table, row, string.IsNullOrEmpty(stated) ? key : value, "differs", name, stated, fact.Name, fact.Value, fact, target, lines, $"{table.Name}, {m.Column}: \"{name}\" is {stated} in the FSD and {fact.Value} in the code"));
                continue;
            }
            matches.Add((JsonNode)item);
        }
        var codeOnly = new JsonArray();
        foreach (var fact in target.Facts.Where(f => !claimed.Contains(f.Name)).OrderBy(f => f.Line).ThenBy(f => f.Name, StringComparer.Ordinal))
        {
            codeOnly.Add((JsonNode)Item(null, null, fact));
            entries.Add(Finding(doc, m, table, null, key, "code-only", null, null, fact.Name, fact.Value, fact, target, lines, $"{table.Name}, {m.Column}: \"{fact.Name}\" is in {m.Target} and not in the FSD"));
        }
        var done = new JsonObject
        {
            ["table"] = m.Table, ["column"] = m.Column, ["kind"] = m.Kind, ["target"] = m.Target, ["symbol"] = target.Symbol, ["section"] = (string)table.Section["id"]!,
            ["matches"] = matches, ["differs"] = differs, ["fsdOnly"] = fsdOnly, ["codeOnly"] = codeOnly, ["skippedRows"] = skipped,
        };
        if (m.Sheet is not null) done["sheet"] = m.Sheet;
        return (done, null);
    }

    static int Column(FsdTable table, string name) => table.Columns.ToList().FindIndex(c => Quotes.Normalize(c) == Quotes.Normalize(name));

    static string? At(CodeFact fact, CodeTarget target) => (fact.File ?? target.File) is { } file ? $"{file}:{(fact.File is null ? target.Line : fact.Line)}" : null;

    /// <summary>A difference, as a finding of its own kind. Its quote is a cell of the table, or the column's header for a name only the code has.</summary>
    static JsonObject Finding(string doc, TableMapping m, FsdTable table, FsdTableRow? row, int cell, string result, string? fsdName, string? fsdValue, string? codeName, string? codeValue,
        CodeFact? atFact, CodeTarget target, CodeLines lines, string note)
    {
        var quote = row is null ? table.Columns[cell] : row.Cells[Math.Min(cell, row.Cells.Count - 1)];
        var e = LinkRun.Entry(TraceFile.TableFact, doc, table.Section, quote, null, null, null, "code", null, note);
        var fsd = (JsonObject)e["fsd"]!;
        if (table.AttachmentFile is not null)
        {
            fsd["attachment"] = table.Id;
            fsd["attachmentFile"] = table.AttachmentFile;
            if (table.Sheet is not null) fsd["attachmentSheet"] = table.Sheet;
            fsd["attachmentRow"] = (row?.Row ?? table.HeaderRow).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var fact = new JsonObject
        {
            ["route"] = Route, ["result"] = result, ["table"] = table.Id, ["column"] = m.Column, ["row"] = row?.Row ?? table.HeaderRow, ["kind"] = m.Kind, ["target"] = m.Target, ["strength"] = "read",
            ["key"] = string.Join('|', table.Id, table.Sheet ?? "", m.Column, m.Kind, m.Target, result, fsdName ?? "", fsdValue ?? "", codeName ?? "", codeValue ?? ""),
        };
        if (table.Sheet is not null) fact["sheet"] = table.Sheet;
        if (fsdName is not null) fact["fsdName"] = fsdName;
        if (fsdValue is not null) fact["fsdValue"] = fsdValue;
        if (codeName is not null) fact["codeName"] = codeName;
        if (codeValue is not null) fact["codeValue"] = codeValue;
        var (symbol, path, start, end) = atFact is not null && atFact.File is not null ? (atFact.Symbol, atFact.File, atFact.Line, atFact.EndLine) : (target.Symbol, target.File, target.Line, target.EndLine);
        if (path is not null && start > 0)
        {
            fact["symbol"] = symbol;
            if (lines.Commit is { } commit && lines.Match(path))
            {
                fact["path"] = path;
                fact["start"] = start;
                fact["end"] = Math.Max(start, end);
                fact["commit"] = commit;
            }
            else fact["linesNot"] = "this file may not match the graph's commit, so no lines are cited";
        }
        e["table_fact"] = fact;
        e["id"] = TraceFile.IdOf(e, (string)fsd["section"]!);
        return e;
    }

    static (CodeTarget? Target, string? Why) ApiFields(CodeGraph graph, string target)
    {
        var shapes = graph.AllFacts.Where(f => f.Kind == "json-shape").GroupBy(f => f.Owner).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var nodes = graph.AllNodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        string? owner;
        var parts = target.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts[2] is "request" or "response")
        {
            var endpoints = nodes.Keys.Where(id => id.StartsWith("endpoint:", StringComparison.Ordinal) && id.EndsWith($"|{parts[0]} {parts[1]}", StringComparison.Ordinal)).ToList();
            if (endpoints.Count != 1) return (null, endpoints.Count == 0 ? $"no endpoint {parts[0]} {parts[1]} on the graph" : $"{parts[0]} {parts[1]} is on the graph {endpoints.Count} times: {string.Join(", ", endpoints)}");
            var handlers = graph.AllEdges.Where(e => e.Source == endpoints[0] && e.Kind == EdgeKind.RoutesTo).Select(e => e.Target).ToHashSet(StringComparer.Ordinal);
            var types = graph.AllFacts.FirstOrDefault(f => f.Kind == "endpoint-types" && (handlers.Contains(f.Owner) || f.Owner == endpoints[0]));
            owner = types?[parts[2] == "response" ? "response" : "body"];
            if (string.IsNullOrEmpty(owner)) return (null, $"the graph has no {parts[2]} type for {parts[0]} {parts[1]}");
        }
        else
        {
            var named = shapes.Keys.Where(id => id == target || nodes.GetValueOrDefault(id) is { } n && (n.Name == target || n.Name.EndsWith("." + target, StringComparison.Ordinal))).ToList();
            if (named.Count == 0) return (null, $"no API request or response type named {target} on the graph: only a type an endpoint sends or receives has its fields read");
            if (named.Count > 1) return (null, $"{target} names {named.Count} types: {string.Join(", ", named.Order(StringComparer.Ordinal))}. Use one's graph id.");
            owner = named[0];
        }
        if (!shapes.TryGetValue(owner, out var shape)) return (null, $"the graph holds no fields for {owner}");
        var byFrontend = graph.AllEdges.Where(e => e.Kind == EdgeKind.MapsField).GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var type = nodes.GetValueOrDefault(owner);
        var facts = (System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(shape["props"]) ?? []).Select(p =>
        {
            var id = p.GetValueOrDefault("id") ?? "";
            var node = nodes.GetValueOrDefault(id);
            return new CodeFact(p.GetValueOrDefault("json") ?? "", null, id, node?.File, node?.Line ?? 0, node?.EndLine ?? 0, byFrontend.GetValueOrDefault(id));
        }).ToList();
        return (new CodeTarget(owner, type?.File, type?.Line ?? 0, type?.EndLine ?? 0, facts), null);
    }

    static (CodeTarget? Target, string? Why) EnumMembers(string root, CodeGraph graph, string target)
    {
        if (CodeEnums.Load(root) is not { } read) return (null, "no enums on file: run `stangent graph build` with table-facts on");
        if (read.Commit != graph.Commit) return (null, $"the enums on file were read at {read.Commit} and the graph is at {graph.Commit}: run `stangent graph build`");
        var named = read.Enums.Where(e => e.Id == target || e.Name == target).ToList();
        if (named.Count == 0) return (null, $"no enum named {target} in the code");
        if (named.Count > 1) return (null, $"{target} names {named.Count} enums: {string.Join(", ", named.Select(e => e.Id))}. Use one's graph id.");
        var e = named[0];
        return (new CodeTarget(e.Id, e.File, e.Line, e.EndLine, [.. e.Members.Select(m => new CodeFact(m.Name, m.Value, e.Id, e.File, m.Line, m.Line))]), null);
    }

    /// <summary>What a finding's lines are as of: nothing is cited for a file that may not match the commit, as with a page's line citations.</summary>
    sealed class CodeLines(CodeGraph graph)
    {
        public string? Commit { get; } = SymbolLines.CommitOf(graph);
        public bool Match(string file) => Commit is not null && SymbolLines.NotAtCommit(graph, file) is null;
    }
}

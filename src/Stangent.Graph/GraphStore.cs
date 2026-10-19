using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Stangent.Core;

namespace Stangent.Graph;

/// <summary>
/// Saves and loads the graph (D25): <c>.stangent/graph.sqlite</c>, git-ignored, tagged with its commit.
/// Rows are always written in sorted order and nothing time-dependent is stored, so the same
/// graph gives the same database and the same export.
/// </summary>
public static class GraphStore
{
    public const int SchemaVersion = 3; // v2: rows keyed by owner as well; v3: 📜 history lines (older files are rebuilt)
    const string LinkedOwner = "";

    public static string PathFor(StangentRepo repo) => Path.Combine(repo.Root, ".stangent", "graph.sqlite");

    public static void Save(CodeGraph graph, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.Delete(temp);
        using (var db = Open(temp))
        {
            Exec(db, """
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE fragments (project TEXT PRIMARY KEY, fingerprint TEXT NOT NULL);
                -- Keyed by owner too: a file compiled into several projects appears in each project's
                -- fragment, and a reused fragment must load back exactly as it was saved.
                CREATE TABLE nodes (id TEXT NOT NULL, kind TEXT NOT NULL, name TEXT NOT NULL, project TEXT,
                  file TEXT, line INTEGER NOT NULL, end_line INTEGER NOT NULL, is_test INTEGER NOT NULL, owner TEXT NOT NULL,
                  PRIMARY KEY (id, owner));
                CREATE TABLE edges (source TEXT NOT NULL, target TEXT NOT NULL, kind TEXT NOT NULL, label INTEGER NOT NULL,
                  detector TEXT NOT NULL, owner TEXT NOT NULL, PRIMARY KEY (source, target, kind, detector, owner));
                CREATE TABLE blind_spots (owner_node TEXT NOT NULL, file TEXT NOT NULL, line INTEGER NOT NULL,
                  reason TEXT NOT NULL, detector TEXT NOT NULL, owner TEXT NOT NULL);
                CREATE TABLE facts (owner TEXT NOT NULL, detector TEXT NOT NULL, kind TEXT NOT NULL, owner_node TEXT NOT NULL,
                  file TEXT, line INTEGER NOT NULL, data TEXT NOT NULL);
                CREATE TABLE load_errors (message TEXT NOT NULL);
                CREATE TABLE history_lines (source TEXT NOT NULL, target TEXT NOT NULL, shared INTEGER NOT NULL,
                  of INTEGER NOT NULL, PRIMARY KEY (source, target));
                """);
            using var tx = db.BeginTransaction();
            Exec(db, "INSERT INTO meta VALUES ('schemaVersion', $v), ('commit', $c)", ("$v", SchemaVersion.ToString()), ("$c", graph.Commit));
            foreach (var f in graph.Fragments.Values)
            {
                Exec(db, "INSERT INTO fragments VALUES ($p, $f)", ("$p", f.ProjectId), ("$f", f.Fingerprint));
                WriteParts(db, f.ProjectId, f.Nodes, f.Edges, f.BlindSpots, f.Facts);
            }
            WriteParts(db, LinkedOwner, graph.LinkedNodes, graph.LinkedEdges, graph.LinkedBlindSpots, []);
            foreach (var e in graph.LoadErrors.Order(StringComparer.Ordinal))
                Exec(db, "INSERT INTO load_errors VALUES ($m)", ("$m", e));
            if (graph.History is { } history)
                Exec(db, "INSERT INTO meta VALUES ('history', $h)", ("$h", CanonicalJson.SerializeCompact(HistoryJson(history))));
            foreach (var l in SortHistory(graph.HistoryLines))
                Exec(db, "INSERT INTO history_lines VALUES ($s, $t, $n, $of)", ("$s", l.Source), ("$t", l.Target), ("$n", l.Shared), ("$of", l.Of));
            Exec(db, """
                CREATE INDEX edges_target ON edges (target);
                CREATE INDEX nodes_file ON nodes (file, line);
                CREATE INDEX blind_owner ON blind_spots (owner_node);
                CREATE INDEX history_target ON history_lines (target);
                """);
            tx.Commit();
        }
        File.Move(temp, path, overwrite: true);
    }

    public static CodeGraph? Load(string path)
    {
        if (!File.Exists(path)) return null;
        using var db = Open(path);
        if (Scalar(db, "SELECT value FROM meta WHERE key = 'schemaVersion'") != SchemaVersion.ToString())
            return null; // an older layout is rebuilt rather than migrated
        var graph = new CodeGraph { Commit = Scalar(db, "SELECT value FROM meta WHERE key = 'commit'") ?? "" };
        foreach (var r in Rows(db, "SELECT project, fingerprint FROM fragments ORDER BY project"))
            graph.Fragments[r.GetString(0)] = new ProjectFragment { ProjectId = r.GetString(0), Fingerprint = r.GetString(1) };

        foreach (var r in Rows(db, "SELECT id, kind, name, project, file, line, end_line, is_test, owner FROM nodes ORDER BY owner, id"))
            Parts(graph, r.GetString(8), out var nodes, out _, out _, out _)
                .Add(new Node(r.GetString(0), r.GetString(1), r.GetString(2), Str(r, 3), Str(r, 4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7) == 1));
        foreach (var r in Rows(db, "SELECT source, target, kind, label, detector, owner FROM edges ORDER BY owner, source, target, kind, detector"))
        {
            Parts(graph, r.GetString(5), out _, out var edges, out _, out _);
            edges.Add(new Edge(r.GetString(0), r.GetString(1), r.GetString(2), (Label)r.GetInt32(3), r.GetString(4)));
        }
        foreach (var r in Rows(db, "SELECT owner_node, file, line, reason, detector, owner FROM blind_spots ORDER BY owner, owner_node, file, line, reason"))
        {
            Parts(graph, r.GetString(5), out _, out _, out var spots, out _);
            spots.Add(new BlindSpot(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4)));
        }
        foreach (var r in Rows(db, "SELECT owner, detector, kind, owner_node, file, line, data FROM facts ORDER BY rowid"))
        {
            Parts(graph, r.GetString(0), out _, out _, out _, out var facts);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(6))!;
            facts!.Add(new Fact(r.GetString(1), r.GetString(2), r.GetString(3), Str(r, 4), r.GetInt32(5), data));
        }
        foreach (var r in Rows(db, "SELECT message FROM load_errors ORDER BY message"))
            graph.LoadErrors.Add(r.GetString(0));
        if (Scalar(db, "SELECT value FROM meta WHERE key = 'history'") is { } history)
            graph.History = HistoryFrom(JsonNode.Parse(history)!.AsObject());
        foreach (var r in Rows(db, "SELECT source, target, shared, of FROM history_lines ORDER BY target, source"))
            graph.HistoryLines.Add(new HistoryLine(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3)));
        return graph;
    }

    /// <summary>
    /// One gzip'd, canonical JSON-lines file: the unit CI shares (D25). Two exports of the same
    /// graph are byte-identical, which is how determinism is tested.
    /// </summary>
    public static void Export(CodeGraph graph, string path)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(gzip, new UTF8Encoding(false)) { NewLine = "\n" };
        foreach (var line in CanonicalLines(graph)) writer.WriteLine(line);
    }

    public static CodeGraph Import(string path)
    {
        var graph = new CodeGraph();
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        while (reader.ReadLine() is { } line)
        {
            var o = JsonNode.Parse(line)!.AsObject();
            var owner = (string?)o["owner"] ?? LinkedOwner;
            switch ((string)o["t"]!)
            {
                case "meta":
                    if ((int)o["schemaVersion"]! != SchemaVersion)
                        throw new StangentException("unsupported_graph", $"{path}: graph schema v{(int)o["schemaVersion"]!} (expected v{SchemaVersion}).");
                    graph.Commit = (string)o["commit"]!;
                    if (o["history"] is JsonObject h) graph.History = HistoryFrom(h);
                    break;
                case "history":
                    graph.HistoryLines.Add(new HistoryLine((string)o["source"]!, (string)o["target"]!, (int)o["shared"]!, (int)o["of"]!));
                    break;
                case "fragment":
                    graph.Fragments[(string)o["project"]!] = new ProjectFragment { ProjectId = (string)o["project"]!, Fingerprint = (string)o["fingerprint"]! };
                    break;
                case "node":
                    Parts(graph, owner, out var nodes, out _, out _, out _);
                    nodes.Add(new Node((string)o["id"]!, (string)o["kind"]!, (string)o["name"]!, (string?)o["project"], (string?)o["file"],
                        (int)o["line"]!, (int)o["endLine"]!, (bool)o["isTest"]!));
                    break;
                case "edge":
                    Parts(graph, owner, out _, out var edges, out _, out _);
                    edges.Add(new Edge((string)o["source"]!, (string)o["target"]!, (string)o["kind"]!, (Label)(int)o["label"]!, (string)o["detector"]!));
                    break;
                case "blind":
                    Parts(graph, owner, out _, out _, out var spots, out _);
                    spots.Add(new BlindSpot((string)o["node"]!, (string)o["file"]!, (int)o["line"]!, (string)o["reason"]!, (string)o["detector"]!));
                    break;
                case "fact":
                    Parts(graph, owner, out _, out _, out _, out var facts);
                    facts!.Add(new Fact((string)o["detector"]!, (string)o["kind"]!, (string)o["node"]!, (string?)o["file"], (int)o["line"]!,
                        o["data"]!.AsObject().ToDictionary(p => p.Key, p => (string)p.Value!)));
                    break;
                case "loadError":
                    graph.LoadErrors.Add((string)o["message"]!);
                    break;
            }
        }
        return graph;
    }

    public static IEnumerable<string> CanonicalLines(CodeGraph graph)
    {
        yield return Line(new()
        {
            ["t"] = "meta", ["schemaVersion"] = SchemaVersion, ["commit"] = graph.Commit,
            ["history"] = graph.History is { } history ? HistoryJson(history) : null,
        });
        foreach (var f in graph.Fragments.Values)
            yield return Line(new() { ["t"] = "fragment", ["project"] = f.ProjectId, ["fingerprint"] = f.Fingerprint });
        foreach (var (owner, nodes, edges, spots, facts) in Owners(graph))
        {
            foreach (var n in nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
                yield return Line(new()
                {
                    ["t"] = "node", ["owner"] = owner, ["id"] = n.Id, ["kind"] = n.Kind, ["name"] = n.Name, ["project"] = n.Project,
                    ["file"] = n.File, ["line"] = n.Line, ["endLine"] = n.EndLine, ["isTest"] = n.IsTest,
                });
            foreach (var e in SortEdges(edges))
                yield return Line(new()
                {
                    ["t"] = "edge", ["owner"] = owner, ["source"] = e.Source, ["target"] = e.Target, ["kind"] = e.Kind,
                    ["label"] = (int)e.Label, ["detector"] = e.Detector,
                });
            foreach (var b in SortSpots(spots))
                yield return Line(new()
                {
                    ["t"] = "blind", ["owner"] = owner, ["node"] = b.Owner, ["file"] = b.File, ["line"] = b.Line,
                    ["reason"] = b.Reason, ["detector"] = b.Detector,
                });
            foreach (var x in facts)
                yield return Line(new()
                {
                    ["t"] = "fact", ["owner"] = owner, ["detector"] = x.Detector, ["kind"] = x.Kind, ["node"] = x.Owner,
                    ["file"] = x.File, ["line"] = x.Line,
                    ["data"] = new JsonObject(x.Data.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => KeyValuePair.Create(d.Key, (JsonNode?)d.Value))),
                });
        }
        foreach (var l in SortHistory(graph.HistoryLines))
            yield return Line(new() { ["t"] = "history", ["source"] = l.Source, ["target"] = l.Target, ["shared"] = l.Shared, ["of"] = l.Of });
        foreach (var e in graph.LoadErrors.Order(StringComparer.Ordinal))
            yield return Line(new() { ["t"] = "loadError", ["message"] = e });
    }

    static IEnumerable<HistoryLine> SortHistory(IEnumerable<HistoryLine> lines) =>
        lines.OrderBy(l => l.Target, StringComparer.Ordinal).ThenBy(l => l.Source, StringComparer.Ordinal);

    static JsonObject HistoryJson(HistoryInfo h) => new()
    {
        ["status"] = h.Status, ["commit"] = h.Commit, ["oldest"] = h.Oldest, ["commitsRead"] = h.CommitsRead, ["commitsUsed"] = h.CommitsUsed,
        ["dropped"] = new JsonObject(h.Dropped.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => KeyValuePair.Create(d.Key, (JsonNode?)d.Value))),
        ["renamesFollowed"] = h.RenamesFollowed, ["fingerprint"] = h.Fingerprint, ["note"] = h.Note,
    };

    static HistoryInfo HistoryFrom(JsonObject o) => new(
        (string)o["status"]!, (string)o["commit"]!, (string)o["oldest"]!, (int)o["commitsRead"]!, (int)o["commitsUsed"]!,
        o["dropped"]!.AsObject().ToDictionary(p => p.Key, p => (int)p.Value!), (bool)o["renamesFollowed"]!, (string)o["fingerprint"]!, (string?)o["note"]);

    static IEnumerable<(string, List<Node>, List<Edge>, List<BlindSpot>, List<Fact>)> Owners(CodeGraph graph)
    {
        foreach (var f in graph.Fragments.Values) yield return (f.ProjectId, f.Nodes, f.Edges, f.BlindSpots, f.Facts);
        yield return (LinkedOwner, graph.LinkedNodes, graph.LinkedEdges, graph.LinkedBlindSpots, []);
    }

    static void WriteParts(SqliteConnection db, string owner, List<Node> nodes, List<Edge> edges, List<BlindSpot> spots, List<Fact> facts)
    {
        foreach (var n in nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
            Exec(db, "INSERT OR IGNORE INTO nodes VALUES ($id, $kind, $name, $project, $file, $line, $end, $test, $owner)",
                ("$id", n.Id), ("$kind", n.Kind), ("$name", n.Name), ("$project", n.Project), ("$file", n.File),
                ("$line", n.Line), ("$end", n.EndLine), ("$test", n.IsTest ? 1 : 0), ("$owner", owner));
        foreach (var e in SortEdges(edges))
            Exec(db, "INSERT OR IGNORE INTO edges VALUES ($s, $t, $k, $l, $d, $owner)",
                ("$s", e.Source), ("$t", e.Target), ("$k", e.Kind), ("$l", (int)e.Label), ("$d", e.Detector), ("$owner", owner));
        foreach (var b in SortSpots(spots))
            Exec(db, "INSERT INTO blind_spots VALUES ($n, $f, $l, $r, $d, $owner)",
                ("$n", b.Owner), ("$f", b.File), ("$l", b.Line), ("$r", b.Reason), ("$d", b.Detector), ("$owner", owner));
        foreach (var x in facts)
            Exec(db, "INSERT INTO facts VALUES ($owner, $d, $k, $n, $f, $l, $data)",
                ("$owner", owner), ("$d", x.Detector), ("$k", x.Kind), ("$n", x.Owner), ("$f", x.File), ("$l", x.Line),
                ("$data", JsonSerializer.Serialize(new SortedDictionary<string, string>(x.Data.ToDictionary(), StringComparer.Ordinal))));
    }

    public static IEnumerable<Edge> SortEdges(IEnumerable<Edge> edges) => edges
        .OrderBy(e => e.Source, StringComparer.Ordinal).ThenBy(e => e.Target, StringComparer.Ordinal)
        .ThenBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.Detector, StringComparer.Ordinal);

    static IEnumerable<BlindSpot> SortSpots(IEnumerable<BlindSpot> spots) => spots
        .OrderBy(b => b.Owner, StringComparer.Ordinal).ThenBy(b => b.File, StringComparer.Ordinal)
        .ThenBy(b => b.Line).ThenBy(b => b.Reason, StringComparer.Ordinal);

    static List<Node> Parts(CodeGraph graph, string owner, out List<Node> nodes, out List<Edge> edges, out List<BlindSpot> spots, out List<Fact>? facts)
    {
        if (owner == LinkedOwner)
        {
            (nodes, edges, spots, facts) = (graph.LinkedNodes, graph.LinkedEdges, graph.LinkedBlindSpots, null);
            return nodes;
        }
        var f = graph.Fragments[owner];
        (nodes, edges, spots, facts) = (f.Nodes, f.Edges, f.BlindSpots, f.Facts);
        return nodes;
    }

    static string Line(JsonObject o) => CanonicalJson.SerializeCompact(o);

    static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    static string? Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        try { return cmd.ExecuteScalar() as string; }
        catch (SqliteException) { return null; }
    }

    static void Exec(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    static IEnumerable<SqliteDataReader> Rows(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) yield return reader;
    }
}

using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>Comparing a document's tables with the code, by code (C.2, D124). Hand-built documents and a hand-built graph: there's no real FSD to pilot this on.</summary>
public sealed class TableFactsTests : IDisposable
{
    const string Doc = """
        # Order API

        | Field | Meaning |
        | customer | Who ordered |
        | total | What it came to |

        # Item API

        | Field | Meaning |
        | sku | The item's code |
        | weight | In grams |

        # Refund API

        | Field |
        | Customer Name |
        | id |

        # Statuses

        | Status | Code |
        | Open | 0 |
        | Closed | 1 |
        | Lost | 3 |

        # Not looked at

        | A | B |
        | 1 | 2 |

        """;

    const string Commit = "0123456789abcdef0123456789abcdef01234567";
    const string Order = "Shop|T:Shop.OrderDto", Item = "Shop|T:Shop.ItemDto", Refund = "Shop|T:Shop.RefundDto", Status = "Shop|T:Shop.OrderStatus";

    readonly TempRepo archive = new();
    readonly TempRepo repo;

    public TableFactsTests()
    {
        repo = new TempRepo($"version: 1\nfsd: {{ compareTables: true, archive: {{ name: a, path: '{archive.Root}', git: false }} }}\n");
        var file = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(file, DocxWriter.Write(Doc));
        FsdImport.Import(repo.Repo, file);
    }

    public void Dispose()
    {
        repo.Dispose();
        archive.Dispose();
    }

    string Root => repo.Root;

    static CodeGraph Graph()
    {
        var graph = new CodeGraph { Commit = Commit, DirtyFiles = [], NotInCommit = [] };
        var f = new ProjectFragment { ProjectId = "project:shop", Fingerprint = "x" };
        void Type(string id, string name, int line, params (string Json, string Prop, int Line)[] props)
        {
            f.Nodes.Add(new Node(id, NodeKind.Type, name, "project:shop", "src/Dtos.cs", line, line + 9));
            foreach (var (_, prop, at) in props) f.Nodes.Add(new Node(prop, NodeKind.Property, prop, "project:shop", "src/Dtos.cs", at, at));
            var shape = System.Text.Json.JsonSerializer.Serialize(props.Select(p => new Dictionary<string, string> { ["json"] = p.Json, ["id"] = p.Prop, ["type"] = "" }));
            f.Facts.Add(new Fact("aspnetcore-routes", "json-shape", id, null, 0, new Dictionary<string, string> { ["props"] = shape }));
        }
        Type(Order, "Shop.OrderDto", 1, ("customer", "Shop|P:Shop.OrderDto.Customer", 2), ("total", "Shop|P:Shop.OrderDto.Total", 3));
        Type(Item, "Shop.ItemDto", 20, ("sku", "Shop|P:Shop.ItemDto.Sku", 21), ("price", "Shop|P:Shop.ItemDto.Price", 22));
        Type(Refund, "Shop.RefundDto", 40, ("id", "Shop|P:Shop.RefundDto.Id", 41), ("customerName", "Shop|P:Shop.RefundDto.CustomerName", 42));
        f.Nodes.Add(new Node(Status, NodeKind.Type, "Shop.OrderStatus", "project:shop", "src/Status.cs", 1, 6));
        graph.Fragments[f.ProjectId] = f;
        return graph;
    }

    void Enums()
    {
        File.WriteAllText(CodeEnums.PathFor(Root), new JsonObject
        {
            ["commit"] = Commit,
            ["enums"] = new JsonArray(new JsonObject
            {
                ["id"] = Status, ["name"] = "OrderStatus", ["project"] = "project:shop", ["file"] = "src/Status.cs", ["line"] = 1, ["endLine"] = 6,
                ["members"] = new JsonArray(
                    new JsonObject { ["name"] = "Open", ["value"] = "0", ["line"] = 2 },
                    new JsonObject { ["name"] = "Closed", ["value"] = "2", ["line"] = 3 },
                    new JsonObject { ["name"] = "Archived", ["value"] = "3", ["line"] = 4 }),
            }),
        }.ToJsonString());
    }

    string Id(string title) => (string)FsdTables.All(Root, "shop").First(t => (string)t.Section!["title"]! == title).Id;

    string Map(string aliases = "") => "version: 1\ntables:\n"
        + $"  - {{ table: {Id("Order API")}, column: Field, kind: api-field, target: OrderDto }}\n"
        + $"  - {{ table: {Id("Item API")}, column: Field, kind: api-field, target: ItemDto }}\n"
        + $"  - {{ table: {Id("Refund API")}, column: Field, kind: api-field, target: RefundDto }}\n"
        + $"  - {{ table: {Id("Statuses")}, column: Status, kind: enum-member, target: OrderStatus, valueColumn: Code }}\n"
        + aliases;

    JsonObject Compare(string? map = null, CodeGraph? graph = null)
    {
        File.WriteAllText(TableMap.PathIn(Root, "shop"), map ?? Map());
        return TableFacts.Compare(Root, "shop", graph ?? Graph());
    }

    JsonObject Table(JsonObject result, string title) => result["tables"]!.AsArray().OfType<JsonObject>().First(t => (string)t["table"]! == Id(title));

    List<JsonObject> Findings() => TraceFile.Load(Root)["entries"]!.AsArray().OfType<JsonObject>().Where(e => (string?)e["state"] == TraceFile.TableFact).ToList();

    [Fact]
    public void Tables_lists_each_table_with_its_id_and_columns()
    {
        var tables = FsdTables.Listing(Root, "shop")["tables"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Equal(5, tables.Count);
        Assert.All(tables, t => Assert.StartsWith("T-", (string)t["id"]!));
        Assert.Equal(["Status", "Code"], tables[3]["columns"]!.AsArray().Select(c => (string)c!));
        Assert.Equal(3, (int)tables[3]["rows"]!);
    }

    [Fact]
    public void A_field_list_that_matches_has_no_finding()
    {
        Enums();
        var order = Table(Compare(), "Order API");
        Assert.Equal(["customer", "total"], order["matches"]!.AsArray().Select(m => (string)m!["code"]!));
        Assert.Empty(order["fsdOnly"]!.AsArray());
        Assert.Empty(order["codeOnly"]!.AsArray());
        Assert.DoesNotContain(Findings(), f => (string?)f["table_fact"]!["target"] == "OrderDto");
    }

    [Fact]
    public void A_field_list_that_differs_lists_each_side_as_a_finding_citing_cell_and_code()
    {
        Enums();
        var item = Table(Compare(), "Item API");
        Assert.Equal("weight", (string)item["fsdOnly"]![0]!["fsd"]!);
        Assert.Equal("price", (string)item["codeOnly"]![0]!["code"]!);

        var found = Findings().Where(f => (string?)f["table_fact"]!["target"] == "ItemDto").ToList();
        Assert.Equal(["code-only", "fsd-only"], found.Select(f => (string)f["table_fact"]!["result"]!).Order());
        var fsdOnly = found.Single(f => (string)f["table_fact"]!["result"]! == "fsd-only");
        Assert.Equal("weight", (string)fsdOnly["fsd"]!["quote"]!);
        Assert.Equal(3, (int)fsdOnly["table_fact"]!["row"]!);
        Assert.Null(fsdOnly["page"]);
        Assert.Equal("code", (string)fsdOnly["by"]!);
        Assert.Equal("table-facts", (string)fsdOnly["table_fact"]!["route"]!);
        Assert.Equal("read", (string)fsdOnly["table_fact"]!["strength"]!);

        var check = TraceCheck.Run(Root, Graph(), null, fix: false);
        Assert.True((bool)check["ok"]!, string.Join("; ", check["problems"]!.AsArray().Select(p => (string)p!)));
        var codeOnly = TraceFile.Load(Root)["entries"]!.AsArray().OfType<JsonObject>().Single(e => (string?)e["table_fact"]?["codeName"] == "price");
        var evidence = codeOnly["evidence"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Equal("stated", (string)evidence.Single(r => (string)r["citation"]!["kind"]! == "document")["strength"]!);
        var code = evidence.Single(r => (string)r["citation"]!["kind"]! == "code");
        Assert.Equal("read", (string)code["strength"]!);
        Assert.EndsWith("/src/Dtos.cs", (string)code["citation"]!["ref"]!);
        Assert.Equal(22, (int)code["citation"]!["lines"]!["start"]!);
        Assert.Equal(Commit, (string)code["commit"]!);
        Assert.Equal("Field", (string)evidence.Single(r => (string)r["citation"]!["kind"]! == "document")["citation"]!["quote"]!);
    }

    [Fact]
    public void A_renamed_field_needs_an_alias()
    {
        Enums();
        var without = Table(Compare(), "Refund API");
        Assert.Equal("Customer Name", (string)without["fsdOnly"]![0]!["fsd"]!);
        Assert.Equal("customerName", (string)without["codeOnly"]![0]!["code"]!);

        var with = Table(Compare(Map("aliases:\n  - { fsd: Customer Name, code: customerName }\n")), "Refund API");
        Assert.Empty(with["fsdOnly"]!.AsArray());
        Assert.Empty(with["codeOnly"]!.AsArray());
        var match = with["matches"]!.AsArray().OfType<JsonObject>().Single(m => (string)m["code"]! == "customerName");
        Assert.True((bool)match["alias"]!);
    }

    [Fact]
    public void An_enums_values_are_compared_and_a_wrong_one_differs()
    {
        Enums();
        var result = Compare();
        var statuses = Table(result, "Statuses");
        Assert.Equal("Open", (string)statuses["matches"]![0]!["fsd"]!);
        var differs = statuses["differs"]!.AsArray().OfType<JsonObject>().Single();
        Assert.Equal(("Closed", "1", "2"), ((string)differs["fsd"]!, (string)differs["fsdValue"]!, (string)differs["codeValue"]!));
        Assert.Equal("Lost", (string)statuses["fsdOnly"]![0]!["fsd"]!);
        Assert.Equal("Archived", (string)statuses["codeOnly"]![0]!["code"]!);
        var finding = Findings().Single(f => (string)f["table_fact"]!["result"]! == "differs");
        Assert.Equal("1", (string)finding["fsd"]!["quote"]!);
        Assert.Equal(3, (int)finding["table_fact"]!["start"]!);
    }

    [Fact]
    public void An_unmapped_table_is_listed_and_a_bad_line_is_a_problem_not_a_pass()
    {
        Enums();
        var result = Compare();
        Assert.Equal(Id("Not looked at"), (string)result["unmapped"]!.AsArray().Single()!["id"]!);
        Assert.Equal(1, (int)result["counts"]!["unmapped"]!);

        var bad = Compare($"version: 1\ntables:\n  - {{ table: {Id("Order API")}, column: Nope, kind: api-field, target: OrderDto }}\n  - {{ table: {Id("Item API")}, column: Field, kind: api-field, target: Missing }}\n");
        var problems = bad["problems"]!.AsArray().Select(p => (string)p!["why"]!).ToList();
        Assert.Contains(problems, p => p.Contains("no column \"Nope\"", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Missing", StringComparison.Ordinal));
        // What it couldn't compare says nothing about a finding on file.
        Assert.Contains(Findings(), f => (string)f["table_fact"]!["target"]! == "ItemDto");
    }

    [Fact]
    public void No_map_writes_nothing_and_says_so()
    {
        var result = TableFacts.Compare(Root, "shop", Graph());
        Assert.Contains("table-map.yaml", (string)result["noMap"]!);
        Assert.Equal(5, (int)result["counts"]!["unmapped"]!);
        Assert.False(File.Exists(TraceFile.PathIn(Root)));
    }

    [Fact]
    public void A_broken_map_stops()
    {
        File.WriteAllText(TableMap.PathIn(Root, "shop"), "version: 1\ntables:\n  - { table: T-0001, column: Field, kind: guess, target: x }\n");
        Assert.Equal("invalid_table_map", Assert.Throws<StangentException>(() => TableFacts.Compare(Root, "shop", Graph())).Code);
    }

    [Fact]
    public void Enums_from_another_commit_or_none_are_a_problem()
    {
        var none = Compare();
        Assert.Contains(none["problems"]!.AsArray(), p => ((string)p!["why"]!).Contains("graph build", StringComparison.Ordinal));
        Enums();
        var graph = Graph();
        graph.Commit = "fedcba9876543210fedcba9876543210fedcba98";
        Assert.Contains(Compare(graph: graph)["problems"]!.AsArray(), p => ((string)p!["why"]!).Contains("read at", StringComparison.Ordinal));
    }

    [Fact]
    public void A_table_fact_goes_to_the_BA_is_never_sent_to_disprove_and_a_judge_leaves_it_alone()
    {
        Enums();
        Compare();
        var check = TraceCheck.Run(Root, Graph(), null, fix: false);
        var listed = check["findings"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.NotEmpty(listed);
        Assert.All(listed, f =>
        {
            Assert.Equal("ba", (string)f["goesTo"]!);
            Assert.Contains("second route to code", (string)f["label"]!);
            Assert.Equal("table-facts", (string)f["tableFact"]!["route"]!);
        });
        Assert.False(TraceFindings.Testable(TraceFile.Load(Root)["entries"]!.AsArray().OfType<JsonObject>().First()));

        var section = FsdOutline.Load(Root, "shop")["sections"]!.AsArray().OfType<JsonObject>().First(s => (string)s["title"]! == "Item API");
        var before = Findings().Count;
        LinkRun.ApplyEntries(Root, "shop", (string)section["id"]!, []);
        Assert.Equal(before, Findings().Count);
    }

    [Fact]
    public void Comparing_again_gives_the_same_bytes_and_a_decided_difference_that_goes_away_is_kept_as_gone()
    {
        Enums();
        Compare();
        TraceCheck.Run(Root, Graph(), null, fix: false);
        var first = File.ReadAllText(TraceFile.PathIn(Root));
        Compare();
        TraceCheck.Run(Root, Graph(), null, fix: false);
        Assert.Equal(first, File.ReadAllText(TraceFile.PathIn(Root)));

        var trace = TraceFile.Load(Root);
        var decided = trace["entries"]!.AsArray().OfType<JsonObject>().First(e => (string?)e["table_fact"]?["fsdName"] == "weight");
        var finding = (string)decided["finding"]!;
        decided["decision"] = "fsd-wrong";
        decided["decided_by"] = "Pat";
        TraceFile.Save(Root, trace);

        var fixedGraph = Graph();
        var shape = fixedGraph.Fragments["project:shop"].Facts.First(f => f.Owner == Item);
        fixedGraph.Fragments["project:shop"].Facts.Remove(shape);
        fixedGraph.Fragments["project:shop"].Facts.Add(new Fact("aspnetcore-routes", "json-shape", Item, null, 0, new Dictionary<string, string>
        {
            ["props"] = System.Text.Json.JsonSerializer.Serialize(new[] { "sku", "weight", "price" }.Select(p => new Dictionary<string, string> { ["json"] = p, ["id"] = "Shop|P:Shop.ItemDto.Sku", ["type"] = "" })),
        }));
        Compare(graph: fixedGraph);
        var gone = TraceFile.Load(Root)["entries"]!.AsArray().OfType<JsonObject>().Single(e => (string?)e["finding"] == finding);
        Assert.True(TraceFindings.IsGone(gone));
    }

    [Fact]
    public void A_file_that_may_not_match_the_commit_cites_no_lines()
    {
        Enums();
        var graph = Graph();
        graph.DirtyFiles = ["src/Dtos.cs"];
        Compare(graph: graph);
        var dirty = Findings().First(f => (string)f["table_fact"]!["target"]! == "ItemDto");
        Assert.Null(dirty["table_fact"]!["path"]);
        Assert.NotNull(dirty["table_fact"]!["linesNot"]);
    }

    [Fact]
    public void A_sheet_in_an_attachment_is_a_table_by_its_A_id()
    {
        using var other = new TempRepo($"version: 1\nfsd: {{ compareTables: true, archive: {{ name: a, path: '{archive.Root}', git: false }} }}\n");
        var word = Path.Combine(other.Root, "attached.docx");
        File.WriteAllBytes(word, EmbeddedFilesTests.WordWith("rates.xlsx", XlsxFixture.Rates()));
        FsdImport.Import(other.Repo, word, "attached");
        var sheet = FsdTables.All(other.Root, "attached").First(t => t.Id.StartsWith("A-", StringComparison.Ordinal));
        Assert.NotNull(sheet.Sheet);
        Assert.NotEmpty(sheet.Columns);

        File.WriteAllText(TableMap.PathIn(other.Root, "attached"), $"version: 1\ntables:\n  - {{ table: {sheet.Id}, sheet: {sheet.Sheet}, column: {sheet.Columns[0]}, kind: enum-member, target: Tier }}\n");
        File.WriteAllText(CodeEnums.PathFor(other.Root), new JsonObject
        {
            ["commit"] = Commit,
            ["enums"] = new JsonArray(new JsonObject { ["id"] = Status, ["name"] = "Tier", ["project"] = "project:shop", ["file"] = "src/Status.cs", ["line"] = 1, ["endLine"] = 2, ["members"] = new JsonArray(new JsonObject { ["name"] = "Nobody", ["value"] = "0", ["line"] = 2 }) }),
        }.ToJsonString());
        var result = TableFacts.Compare(other.Root, "attached", Graph());
        Assert.True((int)result["counts"]!["fsdOnly"]! > 0);
        var found = TraceFile.Load(other.Root)["entries"]!.AsArray().OfType<JsonObject>().First(e => (string?)e["table_fact"]?["result"] == "fsd-only");
        Assert.Equal(sheet.Id, (string)found["fsd"]!["attachment"]!);
        Assert.Equal(sheet.Sheet, (string)found["fsd"]!["attachmentSheet"]!);
        var check = TraceCheck.Run(other.Root, Graph(), null, fix: false);
        Assert.True((bool)check["ok"]!, string.Join("; ", check["problems"]!.AsArray().Select(p => (string)p!)));
    }

    [Fact]
    public void It_is_off_by_default()
    {
        Assert.False(new StangentConfig().Fsd.CompareTables);
        Assert.False(StangentComponents.IsOn(new StangentConfig(), "table-facts"));
        Assert.False((bool)TableFacts.Off()["compared"]!);
    }
}

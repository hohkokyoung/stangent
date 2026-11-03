using System.IO.Compression;
using System.Text.Json.Nodes;
using Stangent.Core.Evidence;
using Stangent.Graph;

namespace Stangent.Core.Tests;

public class SymbolLinesTests(ShopGraph shop) : IClassFixture<ShopGraph>
{
    const string PlaceOrder = "Shop.Core|M:Shop.Core.OrderService.PlaceOrder(System.String,System.Decimal)";
    const string Commit = "0123456789abcdef0123456789abcdef01234567";

    static CodeGraph Graph(IReadOnlyList<string>? dirty, params Node[] nodes)
    {
        var graph = new CodeGraph { Commit = Commit, DirtyFiles = dirty, NotInCommit = dirty is null ? null : [] };
        graph.LinkedNodes.AddRange(nodes);
        return graph;
    }

    string LineOf(string file, int line) => File.ReadAllLines(Path.Combine(shop.Root, file))[line - 1];

    [Fact]
    public void A_csharp_method_gives_its_real_lines_at_the_graphs_commit()
    {
        var hit = Assert.Single(SymbolLines.Where(shop.Graph, PlaceOrder));
        Assert.Equal("Shop.Core/Orders.cs", hit.File);
        Assert.Contains("PlaceOrder", LineOf(hit.File!, hit.StartLine!.Value));
        Assert.True(hit.EndLine >= hit.StartLine);
        Assert.Equal(shop.Graph.Commit, hit.Commit);
        Assert.True(hit.LinesMatchCommit);
        Assert.Null(hit.Note);
    }

    [Fact]
    public void A_typescript_symbol_gives_its_real_lines_too()
    {
        var node = shop.Graph.AllNodes.First(n => n.Id.StartsWith("ts|", StringComparison.Ordinal) && n.Kind == NodeKind.Method && !n.IsTest);
        var hit = Assert.Single(SymbolLines.Where(shop.Graph, node.Id));
        Assert.Equal(node.File, hit.File);
        Assert.Contains(node.Name.Split('.').Last(), LineOf(hit.File!, hit.StartLine!.Value));
        Assert.True(hit.LinesMatchCommit);
    }

    [Fact]
    public void Its_citation_fills_valid_evidence()
    {
        var hit = Assert.Single(SymbolLines.Where(shop.Graph, PlaceOrder));
        var evidence = new EvidenceRecord
        {
            Id = "E-1", Citation = hit.ToCitation("shop")!, Commit = hit.Commit, Strength = EvidenceStrength.Read,
            CheckedBy = [new EvidenceChecker { Kind = CheckerKind.Code, Name = "graph where" }],
        };
        Assert.Empty(evidence.Validate());
        Assert.Equal("shop/Shop.Core/Orders.cs", evidence.Citation.Ref);

        var json = hit.ToJson("shop")["citation"]!.AsObject();
        Assert.Equal("code", (string)json["kind"]!);
        Assert.Equal(hit.StartLine, (int)json["lines"]!["start"]!);
    }

    [Fact]
    public void A_symbol_not_found_gives_nothing() => Assert.Empty(SymbolLines.Where(shop.Graph, "NoSuch.Symbol"));

    [Fact]
    public void Overloads_all_come_back_and_none_is_picked()
    {
        var graph = Graph([],
            new Node("A|M:T.Run", NodeKind.Method, "T.Run", "p", "T.cs", 3, 5),
            new Node("A|M:T.Run(System.Int32)", NodeKind.Method, "T.Run", "p", "T.cs", 7, 9));
        Assert.Equal([3, 7], SymbolLines.Where(graph, "T.Run").Select(h => h.StartLine!.Value).Order());
    }

    [Fact]
    public void A_point_with_no_lines_says_so_and_no_line_is_made_up()
    {
        var graph = Graph([],
            new Node("config:A|Key", NodeKind.Config, "Key", "p"),
            new Node("file:T.cs", NodeKind.File, "T.cs", "p", "T.cs"),
            new Node("A|M:T.Half", NodeKind.Method, "T.Half", "p", "T.cs", 4));

        var config = Assert.Single(SymbolLines.Where(graph, "config:A|Key"));
        Assert.Null(config.File);
        Assert.Null(config.StartLine);
        Assert.Null(config.ToCitation("r"));
        Assert.Contains("no place", config.Note);

        var file = Assert.Single(SymbolLines.Where(graph, "file:T.cs"));
        Assert.Null(file.StartLine);
        Assert.Null(file.ToCitation("r")!.Lines);

        var half = Assert.Single(SymbolLines.Where(graph, "A|M:T.Half"));
        Assert.Equal(4, half.StartLine);
        Assert.Null(half.EndLine);
        Assert.Null(half.ToCitation("r")!.Lines);
    }

    [Theory]
    [InlineData("Api/Client.g.cs", true)]
    [InlineData("Forms/Main.Designer.cs", true)]
    [InlineData("web/api.generated.ts", true)]
    [InlineData("Api/Client.cs", false)]
    public void A_generated_file_is_marked_and_its_lines_stay_real(string file, bool generated)
    {
        var hit = Assert.Single(SymbolLines.Where(Graph([], new Node("A|M:T.Run", NodeKind.Method, "T.Run", "p", file, 3, 5)), "A|M:T.Run"));
        Assert.Equal(generated, hit.Generated);
        Assert.Equal(3, hit.StartLine);
        Assert.True(hit.LinesMatchCommit);
    }

    [Fact]
    public void Without_knowing_the_tree_or_the_commit_nothing_is_cited()
    {
        var node = new Node("A|M:T.Run", NodeKind.Method, "T.Run", "p", "T.cs", 3, 5);
        var unknown = Assert.Single(SymbolLines.Where(Graph(null, node), node.Id));
        Assert.False(unknown.LinesMatchCommit);
        Assert.Null(unknown.ToCitation("r"));
        Assert.Contains("rebuild", unknown.Note);

        var noGit = new CodeGraph { Commit = "", DirtyFiles = null };
        noGit.LinkedNodes.Add(node);
        var none = Assert.Single(SymbolLines.Where(noGit, node.Id));
        Assert.Null(none.Commit);
        Assert.Null(none.ToCitation("r"));
    }

    [Fact]
    public void An_export_says_clean_or_dirty_and_keeps_it_through_import()
    {
        var clean = Graph([]);
        Assert.Contains("\"dirtyFiles\":[]", GraphStore.CanonicalLines(clean).First());
        Assert.Contains("\"notInCommit\":[]", GraphStore.CanonicalLines(clean).First());

        var path = Path.GetTempFileName();
        try
        {
            var dirty = Graph(["b.cs", "a.cs"]);
            Assert.Contains("\"dirtyFiles\":[\"a.cs\",\"b.cs\"]", GraphStore.CanonicalLines(dirty).First());
            GraphStore.Export(dirty, path);
            Assert.Equal(["a.cs", "b.cs"], GraphStore.Import(path).DirtyFiles!.Order());
            GraphStore.Save(dirty, path + ".sqlite");
            Assert.Equal(["a.cs", "b.cs"], GraphStore.Load(path + ".sqlite")!.DirtyFiles!.Order());
            GraphStore.Save(Graph(null), path + ".sqlite");
            Assert.Null(GraphStore.Load(path + ".sqlite")!.DirtyFiles);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".sqlite");
        }
    }

    [Fact]
    public void An_old_export_with_no_dirty_files_is_dont_know_not_clean()
    {
        var node = new Node("A|M:T.Run", NodeKind.Method, "T.Run", "p", "T.cs", 3, 5);
        var meta = JsonNode.Parse(GraphStore.CanonicalLines(Graph([], node)).First())!.AsObject();
        meta.Remove("dirtyFiles");
        meta.Remove("notInCommit");
        var lines = GraphStore.CanonicalLines(Graph([], node)).Skip(1).Prepend(meta.ToJsonString());
        var path = Path.GetTempFileName();
        try
        {
            using (var gzip = new GZipStream(File.Create(path), CompressionLevel.Fastest))
            using (var writer = new StreamWriter(gzip)) foreach (var line in lines) writer.WriteLine(line);

            var graph = GraphStore.Import(path);
            Assert.Null(graph.DirtyFiles);
            var hit = Assert.Single(SymbolLines.Where(graph, node.Id));
            Assert.False(hit.LinesMatchCommit);
            Assert.Null(hit.ToCitation("r"));
            Assert.Contains("rebuild", hit.Note);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task A_gitignored_compiled_file_isnt_in_the_commit_so_nothing_cites_it()
    {
        var root = ShopCopy.Create();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            await File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), "Gen/\n", ct);
            ShopCopy.Git(root, "add -A");
            ShopCopy.Git(root, "-c user.name=test -c user.email=test@example.invalid commit -q -m ignore");
            Directory.CreateDirectory(Path.Combine(root, "Shop.Core", "Gen"));
            await File.WriteAllTextAsync(Path.Combine(root, "Shop.Core", "Gen", "Client.cs"), "namespace Shop.Core;\n\npublic class GenClient\n{\n}\n", ct);

            var (graph, _) = await GraphBuilder.BuildAsync(StangentRepo.Discover(root), new BuildOptions(Restore: false), ct);
            Assert.DoesNotContain("Shop.Core/Gen/Client.cs", graph.DirtyFiles!);
            Assert.Contains("Shop.Core/Gen/Client.cs", graph.NotInCommit!);
            var hit = Assert.Single(SymbolLines.Where(graph, "Shop.Core|T:Shop.Core.GenClient"));
            Assert.False(hit.LinesMatchCommit);
            Assert.Null(hit.ToCitation("shop"));
            Assert.Contains("doesn't contain this file", hit.Note);
            Assert.True(Assert.Single(SymbolLines.Where(graph, PlaceOrder)).LinesMatchCommit);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_dirty_tree_says_so_and_a_partial_type_points_at_its_other_part()
    {
        var root = ShopCopy.Create();
        try
        {
            var repo = StangentRepo.Discover(root);
            var ct = TestContext.Current.CancellationToken;
            await File.WriteAllTextAsync(Path.Combine(root, "Shop.Core", "Parted.A.cs"), "namespace Shop.Core;\n\npublic partial class Parted\n{\n    public int One() => 1;\n}\n", ct);
            await File.WriteAllTextAsync(Path.Combine(root, "Shop.Core", "Parted.B.cs"), "namespace Shop.Core;\n\npublic partial class Parted\n{\n    public int Two() => 2;\n}\n", ct);

            var (graph, stats) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false), ct);
            Assert.Equal(["Shop.Core/Parted.A.cs", "Shop.Core/Parted.B.cs"], graph.DirtyFiles!);
            Assert.Equal(2, stats.DirtyFiles);
            var dirtyHit = Assert.Single(SymbolLines.Where(graph, "Shop.Core|T:Shop.Core.Parted"));
            Assert.False(dirtyHit.LinesMatchCommit);
            Assert.Null(dirtyHit.ToCitation("shop"));
            Assert.Contains("differed from the commit", dirtyHit.Note);
            // A file nobody touched still cites, though the tree is dirty.
            Assert.True(Assert.Single(SymbolLines.Where(graph, PlaceOrder)).LinesMatchCommit);

            ShopCopy.Git(root, "add -A");
            ShopCopy.Git(root, "-c user.name=test -c user.email=test@example.invalid commit -q -m parted");
            (graph, _) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false), ct);
            Assert.Empty(graph.DirtyFiles!);
            var hit = Assert.Single(SymbolLines.Where(graph, "Shop.Core|T:Shop.Core.Parted"));
            Assert.Equal("Shop.Core/Parted.A.cs", hit.File);
            Assert.Equal((3, 6), (hit.StartLine, hit.EndLine));
            Assert.Equal(["Shop.Core/Parted.B.cs"], hit.AlsoIn);
            Assert.Contains("partial", hit.Note);
            Assert.True(hit.LinesMatchCommit);
            Assert.Equal("Shop.Core/Parted.B.cs", Assert.Single(SymbolLines.Where(graph, "Shop.Core|M:Shop.Core.Parted.Two")).File);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

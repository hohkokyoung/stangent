using System.Text;
using Stangent.Benchmark.Split;
using Stangent.Graph;

namespace Stangent.Benchmark.Tests;

/// <summary>How hunks are cut, on hand-made graphs: cases bitwarden/server's history showed the first cut got wrong.</summary>
public class SplitterUnitTests
{
    static CodeGraph Graph(Node[] nodes, Edge[] edges)
    {
        var graph = new CodeGraph();
        var fragment = new ProjectFragment { ProjectId = "project:p", Fingerprint = "" };
        fragment.Nodes.AddRange(nodes);
        fragment.Edges.AddRange(edges);
        graph.Fragments[fragment.ProjectId] = fragment;
        return graph;
    }

    static Node N(string key, string kind, string file, int line, int end) => new("P|" + key, kind, key, "project:p", file, line, end);
    static Edge Contains(string type, string member) => new("P|" + type, "P|" + member, EdgeKind.Contains, Label.Certain);

    static FileChange Change(string path, char status, string before, string after, params Hunk[] hunks) =>
        new(path, status, false, hunks, FileChange.SplitLines(Encoding.UTF8.GetBytes(before)), FileChange.SplitLines(Encoding.UTF8.GetBytes(after)));

    static string Applied(Splitter s, SplitGroup g, string path) =>
        Encoding.UTF8.GetString(GroupApplier.Contents(s.Files, s.Pieces, g, f => [.. f.NewLines.SelectMany(l => l)])[path]!);

    // An unrelated member in another file, so a wrong cut shows up as an extra group.
    static readonly FileChange Other = Change("B.cs", 'M', "class U\n{\n    void Y() { }\n}\n", "class U\n{\n    void Y() { return; }\n}\n", new Hunk(3, 1, 3, 1));
    static readonly Node[] OtherNodes = [N("T:U", NodeKind.Type, "B.cs", 1, 4), N("M:U.Y", NodeKind.Method, "B.cs", 3, 3)];

    [Fact]
    public void A_member_whose_signature_changed_is_one_member_not_a_deletion_and_an_insertion()
    {
        const string before = "class T\n{\n    public T(int a)\n    {\n    }\n}\n";
        const string after = "class T\n{\n    public T(int a,\n             int b)\n    {\n    }\n}\n";
        var file = Change("A.cs", 'M', before, after, new Hunk(3, 1, 3, 2));
        var parent = Graph([N("T:T", NodeKind.Type, "A.cs", 1, 6), N("M:T.#ctor(System.Int32)", NodeKind.Method, "A.cs", 3, 5), .. OtherNodes], []);
        var change = Graph([N("T:T", NodeKind.Type, "A.cs", 1, 7), N("M:T.#ctor(System.Int32,System.Int32)", NodeKind.Method, "A.cs", 3, 6), .. OtherNodes], []);

        var s = new Splitter([file, Other], new GraphView(change), new GraphView(parent));
        var split = s.Group(Splitter.Within2, 2);

        Assert.Single(s.Pieces, p => p.File == "A.cs");
        var ctor = Assert.Single(split.Groups, g => g.Members.Contains("M:T.#ctor(System.Int32)"));
        Assert.Contains("M:T.#ctor(System.Int32,System.Int32)", ctor.Members);
        Assert.Equal(2, split.Groups.Count);
        Assert.Equal(0, split.JoinedBySharedHunk);
        Assert.Equal(after, Applied(s, ctor, "A.cs"));
    }

    [Fact]
    public void A_new_record_keeps_its_members_and_its_header()
    {
        const string after = "public record R(int A) : Base\n{\n    public int M() => 1;\n}\n";
        var file = Change("R.cs", 'A', "", after, new Hunk(0, 0, 1, 4));
        var change = Graph(
            [N("T:R", NodeKind.Type, "R.cs", 1, 4), N("P:R.A", NodeKind.Property, "R.cs", 1, 1), N("M:R.M", NodeKind.Method, "R.cs", 3, 3), .. OtherNodes],
            [Contains("T:R", "P:R.A"), Contains("T:R", "M:R.M")]);
        var parent = Graph([.. OtherNodes], []);

        var s = new Splitter([file, Other], new GraphView(change), new GraphView(parent));
        var split = s.Group(Splitter.Within2, 2);

        Assert.Contains("T:R", s.ChangedTypes);
        var record = Assert.Single(split.Groups, g => g.Members.Contains("T:R"));
        Assert.Equal(["M:R.M", "T:R"], record.Members); // the header line, properties and all, is the type's
        Assert.Equal(after, Applied(s, record, "R.cs"));
    }

    [Fact]
    public void Adjacent_overloads_in_one_run_keep_both_ids()
    {
        const string before = "class T\n{\n    void M(int a) { }\n    void M(string a) { }\n}\n";
        const string after = "class T\n{\n    void M(int a) { return; }\n    void M(string a) { return; }\n}\n";
        var file = Change("A.cs", 'M', before, after, new Hunk(3, 2, 3, 2));
        Node[] nodes = [N("T:T", NodeKind.Type, "A.cs", 1, 5), N("M:T.M(System.Int32)", NodeKind.Method, "A.cs", 3, 3), N("M:T.M(System.String)", NodeKind.Method, "A.cs", 4, 4), .. OtherNodes];

        var s = new Splitter([file, Other], new GraphView(Graph(nodes, [])), new GraphView(Graph(nodes, [])));

        Assert.Contains("M:T.M(System.Int32)", s.Members);
        Assert.Contains("M:T.M(System.String)", s.Members);
    }

    [Fact]
    public void A_positional_record_header_belongs_to_the_type_not_its_properties()
    {
        const string before = "record E(int A) : Error(Msg(A))\n{\n}\n";
        const string after = "record E(int A) : Error(Msg2(A))\n{\n}\n";
        var file = Change("E.cs", 'M', before, after, new Hunk(1, 1, 1, 1));
        Node[] nodes = [N("T:E", NodeKind.Type, "E.cs", 1, 3), N("P:E.A", NodeKind.Property, "E.cs", 1, 1), .. OtherNodes];

        var s = new Splitter([file, Other], new GraphView(Graph(nodes, [Contains("T:E", "P:E.A")])), new GraphView(Graph(nodes, [Contains("T:E", "P:E.A")])));

        Assert.Equal(["M:U.Y", "T:E"], s.Members);
        Assert.Contains("T:E", s.ChangedTypes);
    }

    [Fact]
    public void A_type_moved_to_another_namespace_is_one_member()
    {
        var oldFile = Change("A/X.cs", 'D', "namespace A;\nclass X\n{\n    void Run() { }\n}\n", "", new Hunk(1, 5, 0, 0));
        var newFile = Change("B/X.cs", 'A', "", "namespace B;\nclass X\n{\n    void Run() { }\n}\n", new Hunk(0, 0, 1, 5));
        var parent = Graph([new("P|T:A.X", NodeKind.Type, "A.X", "project:p", "A/X.cs", 2, 5), new("P|M:A.X.Run", NodeKind.Method, "X.Run", "project:p", "A/X.cs", 4, 4), .. OtherNodes],
            [Contains("T:A.X", "M:A.X.Run")]);
        var change = Graph([new("P|T:B.X", NodeKind.Type, "B.X", "project:p", "B/X.cs", 2, 5), new("P|M:B.X.Run", NodeKind.Method, "X.Run", "project:p", "B/X.cs", 4, 4), .. OtherNodes],
            [Contains("T:B.X", "M:B.X.Run")]);

        var split = new Splitter([oldFile, newFile, Other], new GraphView(change), new GraphView(parent)).Group(Splitter.Within2, 2);

        Assert.Equal([["M:A.X.Run", "M:B.X.Run", "T:A.X", "T:B.X"], ["M:U.Y"]], split.Groups.Select(g => g.Members.ToArray()));
    }
}

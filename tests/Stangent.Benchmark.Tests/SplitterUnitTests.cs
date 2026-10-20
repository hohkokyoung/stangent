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
    public void A_new_type_keeps_its_members_even_when_its_header_belongs_to_a_record_property()
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
        Assert.Equal(["M:R.M", "P:R.A", "T:R"], record.Members);
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
}

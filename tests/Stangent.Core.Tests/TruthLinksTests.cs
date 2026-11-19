using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Spec.Fsd;

namespace Stangent.Core.Tests;

/// <summary>D362's links: flow, else screen (inventory, with the App frame), else graph node, else file; the fingerprint
/// that flags a ruling; the fallback share. Files written by hand, no build.</summary>
public sealed class TruthLinksTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("stangent-truthlinks-").FullName;
    readonly ProjectFragment f = new() { ProjectId = "project:app", Fingerprint = "x" };

    public TruthLinksTests()
    {
        Write("src/Orders.cs", string.Join("\n", Enumerable.Range(1, 40).Select(i => $"// line {i}")) + "\n");
        Write("web/orders.html", string.Join("\n", Enumerable.Range(1, 20).Select(i => $"<p>{i}</p>")) + "\n");
        Write("web/nav.html", "<a>Home</a>\n<a>Help</a>\n");
        Write("src/Config.cs", "// a\n// b\n// c\n");
        Write("specs/flows/index.json", """
            { "flows": [
              { "id": "click-save", "file": "specs/flows/click-save.json", "name": "Save order", "kind": "click" },
              { "id": "shared-repo-save", "file": "specs/flows/shared-repo-save.json", "name": "Repository save", "kind": "shared" } ] }
            """);
        Write("specs/flows/click-save.json", """{ "id": "click-save", "steps": [ { "at": "src/Orders.cs:5", "point": "M:Orders.Save" }, { "at": "src/Orders.cs:12" } ] }""");
        Write("specs/flows/shared-repo-save.json", """{ "id": "shared-repo-save", "steps": [ { "at": "src/Orders.cs:5" } ] }""");
        Write("specs/inventory.json", """
            { "screens": [
                { "id": "page:p|/orders", "title": "Orders (/orders)", "lines": [ { "at": "web/orders.html:3" }, { "at": "web/orders.html:4" } ] },
                { "id": "page:p|/refunds", "title": "Refunds (/refunds)", "lines": [ { "at": "web/orders.html:9" } ] } ],
              "sharedParts": [],
              "appFrame": { "title": "App frame (on every screen)", "roots": [], "groups": [ { "id": "ts|web/nav.ts#Nav", "title": "Nav", "lines": [ { "at": "web/nav.html:1" } ] } ] } }
            """);
        Node("M:Orders.Save", NodeKind.Method, "Save", "src/Orders.cs", 4, 15);
        Node("T:Orders", NodeKind.Type, "Orders", "src/Orders.cs", 1, 40);
        Node("M:Orders.Cancel", NodeKind.Method, "Cancel", "src/Orders.cs", 20, 30);
        Node("P:Orders.Limit", NodeKind.Property, "Limit", "src/Orders.cs", 33, 33);
    }

    public void Dispose() => Directory.Delete(root, true);

    void Write(string rel, string text)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    void Node(string id, string kind, string name, string file, int line, int end) => f.Nodes.Add(new Node(id, kind, name, "project:app", file, line, end));

    CodeGraph Graph()
    {
        var graph = new CodeGraph { Commit = "c1", DirtyFiles = [], NotInCommit = [] };
        graph.Fragments[f.ProjectId] = f;
        return graph;
    }

    TruthLinks Linker() => new(root, Graph());

    static JsonObject F(string state, string? screen, params string[] evidence) => new()
    {
        ["state"] = state, ["section"] = "1", ["fsdQuote"] = null, ["screen"] = screen, ["note"] = "n",
        ["evidence"] = new JsonArray([.. evidence.Select(e => (JsonNode)e)]),
    };

    [Theory]
    [InlineData("src/Orders.cs:5", "flow", "click-save")]          // a step's own line; the start flow before the shared one
    [InlineData("src/Orders.cs:8", "flow", "click-save")]          // inside a method a flow walks
    [InlineData("web/orders.html:9", "screen", "page:p|/refunds")]  // an inventory line itself
    [InlineData("web/orders.html:5", "screen", "page:p|/orders")]   // a file holding inventory lines: the nearest one
    [InlineData("web/orders.html:8", "screen", "page:p|/refunds")]  // nearer after than before
    [InlineData("web/orders.html:15", "screen", "page:p|/refunds")]
    [InlineData("web/orders.html:1", "screen", "page:p|/orders")]
    [InlineData("src/Orders.cs:25-", "node", "M:Orders.Cancel")]   // a stray mark after the line still reads the line
    [InlineData("src/Orders.cs:25,27", "node", "M:Orders.Cancel")]
    [InlineData("src/Orders.cs:99999999999", "file", "src/Orders.cs")]   // no crash on a number past int: the file
    [InlineData("web/nav.html:2", "screen", "ts|web/nav.ts#Nav")]   // the App frame is a screen too
    [InlineData("src/Orders.cs:25", "node", "M:Orders.Cancel")]    // a method no flow walks: the smallest node
    [InlineData("src/Orders.cs:33", "node", "P:Orders.Limit")]
    [InlineData("src/Config.cs:2", "file", "src/Config.cs")]       // no node
    [InlineData("src/Orders.cs:24-26", "node", "M:Orders.Cancel")]
    [InlineData("src/Orders.cs:24-30", "node", "M:Orders.Cancel")]
    [InlineData("src", "file", "src")]                               // a folder, where a missing thing would be
    public void An_evidence_line_links_to_the_most_specific_thing_that_fits(string evidence, string kind, string id)
    {
        var link = Linker().LinkEvidence(evidence);
        Assert.Equal((kind, id), (link.Kind, link.Id));
    }

    [Fact]
    public void A_finding_takes_its_best_line_and_an_undocumented_screen_its_route()
    {
        var linker = Linker();
        var both = linker.Link(F("conflict", null, "src/Config.cs:1", "web/orders.html:3", "src/Orders.cs:12"));
        Assert.Equal(("flow", "click-save"), (both.Link.Kind, both.Link.Id));
        Assert.Equal(["file", "screen", "flow"], both.Lines.Select(l => l.Link.Kind));
        // An exact step beats a walked method on another line, whatever the order (D368).
        var exact = linker.Link(F("conflict", null, "src/Orders.cs:8", "src/Orders.cs:12"));
        Assert.Equal(("click-save", true), (exact.Link.Id, exact.Link.Exact));
        // Only an undocumented finding's route names its screen.
        Assert.Equal("file", linker.Link(F("gap", "/refunds", "src/Config.cs:1")).Link.Kind);
        var screen = linker.Link(F("undocumented", "/refunds/", "src/Config.cs:1"));
        Assert.Equal(("screen", "page:p|/refunds", "Refunds (/refunds)"), (screen.Link.Kind, screen.Link.Id, screen.Link.Title));
        Assert.False(screen.Link.Fallback);
        Assert.True(linker.Link(F("gap", null, "src/Config.cs:1")).Link.Fallback);
    }

    [Fact]
    public void The_fingerprint_changes_with_what_the_link_points_at_and_nothing_else()
    {
        var flow = Linker().LinkEvidence("src/Orders.cs:5").Hash;
        var node = Linker().LinkEvidence("src/Orders.cs:25").Hash;
        Write("src/Orders.cs", string.Join("\n", Enumerable.Range(1, 40).Select(i => i == 35 ? "// changed outside Cancel" : $"// line {i}")) + "\n");
        Assert.Equal(node, Linker().LinkEvidence("src/Orders.cs:25").Hash);     // a node: only its own lines count
        Assert.Equal(flow, Linker().LinkEvidence("src/Orders.cs:5").Hash);      // a flow: its file, not the code's
        Write("specs/flows/click-save.json", """{ "id": "click-save", "steps": [ { "at": "src/Orders.cs:5", "point": "M:Orders.Save" } ] }""");
        Assert.NotEqual(flow, Linker().LinkEvidence("src/Orders.cs:5").Hash);
        Write("src/Orders.cs", string.Join("\n", Enumerable.Range(1, 40).Select(i => i == 22 ? "// changed inside Cancel" : $"// line {i}")) + "\n");
        Assert.NotEqual(node, Linker().LinkEvidence("src/Orders.cs:25").Hash);
    }

    [Fact]
    public void The_summary_prints_the_fallback_share()
    {
        var counts = new Dictionary<string, int> { ["flow"] = 12, ["screen"] = 34, ["node"] = 5, ["file"] = 0 };
        Assert.Equal("51 findings: flow 12, screen 34, node 5, file 0 (fallback 9.8%)", TruthLinks.Summary(51, counts));
    }
}

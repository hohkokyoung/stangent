using Stangent.Flows;
using Stangent.Graph;

namespace Stangent.Core.Tests;

/// <summary>D325–D328, D334: the PM inventory on a small made-up app: placement, folding, dialogs, data menus, the skip list, server fields, the check.</summary>
public sealed class InventoryTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("stangent-inventory-").FullName;
    readonly ProjectFragment f = new() { ProjectId = "project:app", Fingerprint = "x" };

    public InventoryTests()
    {
        Write("web/src/assets/i18n/en.json", """{ "save": "Save", "edit": { "title": "Edit thing" } }""");
        Write("res/defaultResources.nopres.xml", """
            <Language Name="English">
              <LocaleResource Name="Shop.Fields.Name"><Value>Product name</Value></LocaleResource>
              <LocaleResource Name="Shop.Fields.Other"><Value>Other</Value></LocaleResource>
              <LocaleResource Name="NewznabApiKey"><Value>API Key</Value></LocaleResource>
            </Language>
            """);
        // A builder method assigning a menu list to a member (line 9), its first entry on line 10.
        Write("web/factory.ts", string.Join("\n", Enumerable.Range(1, 8).Select(_ => "//").Append("    this.seriesActions = [").Append("      { title: 'Mark read', callback: this.markRead },")));
    }

    public void Dispose() => Directory.Delete(root, true);

    void Write(string rel, string text)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    void Node(string id, string kind, string name, bool test = false) => f.Nodes.Add(new Node(id, kind, name, "project:app", "web/x.ts", 1, 1, test));
    void Edge(string from, string to, string kind) => f.Edges.Add(new Edge(from, to, kind, Label.Certain));

    void Fact(string kind, string owner, string file, int line, params (string Key, string Value)[] data) =>
        f.Facts.Add(new Fact("test", kind, owner, file, line, data.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal)));

    void Button(string owner, int line, string text, string handler = "inline", string keys = "") =>
        Fact("ui-event", owner, "web/x.html", line, ("element", "button"), ("event", "click"), ("handler", handler), ("keys", keys), ("text", text));

    void Component(string name)
    {
        Node($"ts|web/{name}.ts#{name}", NodeKind.Component, name);
    }

    static string C(string name) => $"ts|web/{name}.ts#{name}";

    CodeGraph Graph()
    {
        var graph = new CodeGraph { Commit = "c1", DirtyFiles = [], NotInCommit = [] };
        graph.Fragments[f.ProjectId] = f;
        return graph;
    }

    /// <summary>Two screens, a part only one renders (with a child of its own), a part both render, a part nobody reaches.</summary>
    void App()
    {
        Node("page:p|/a", NodeKind.Page, "/a");
        Node("page:p|/b", NodeKind.Page, "/b");
        foreach (var c in new[] { "Alpha", "Beta", "Child", "GrandChild", "SharedBar", "Orphan" }) Component(c);
        Node("ts|web/Alpha.html#Alpha.template", NodeKind.Template, "Alpha.template");
        Edge("page:p|/a", C("Alpha"), EdgeKind.RoutesTo);
        Edge("page:p|/b", C("Beta"), EdgeKind.RoutesTo);
        Edge(C("Alpha"), "ts|web/Alpha.html#Alpha.template", EdgeKind.Renders);
        Edge("ts|web/Alpha.html#Alpha.template", C("Child"), EdgeKind.Renders);
        Edge(C("Child"), C("GrandChild"), EdgeKind.Renders);
        Edge("ts|web/Alpha.html#Alpha.template", C("SharedBar"), EdgeKind.Renders);
        Edge(C("Beta"), C("SharedBar"), EdgeKind.Renders);
        Button("ts|web/Alpha.html#Alpha.template", 1, "", keys: "save");
        Button(C("GrandChild"), 2, "Deep button");
        Button(C("SharedBar"), 3, "Refresh");
        Button(C("SharedBar"), 4, "Sync");
        Button(C("Orphan"), 5, "Lonely");
    }

    [Fact]
    public void Parts_are_placed_under_one_screen_shared_with_a_pointer_or_not_placed_and_single_parent_parts_fold_into_their_parent()
    {
        App();
        var set = Inventory.Build(Graph(), root);
        var a = set.Screens.Single(s => s.Id == "page:p|/a");
        // GrandChild is rendered only by Child, Child only by Alpha: both fold, recursively, into the screen's own lines.
        Assert.Equal(["Save", "Deep button"], a.Lines.Select(l => l.Name.Words));
        var shared = Assert.Single(set.Shared);
        Assert.Equal(("Shared Bar", true, "2 actions"), (shared.Title, shared.TitleFromCode, shared.Size()));
        Assert.Equal(["page:p|/a", "page:p|/b"], shared.Screens);
        Assert.All(set.Screens, s => Assert.Equal("Shared Bar", Assert.Single(s.Pointers).Title));
        Assert.Equal("Orphan", Assert.Single(set.NotPlaced).Title);
        var md = InventoryFiles.Markdown(set);
        Assert.Contains("- uses Shared Bar (2 actions): see Shared parts", md);
        Assert.Contains("Appears on 2 screens: ❓ /a: no visible text found; ❓ /b: no visible text found.", md);
    }

    [Fact]
    public void A_dialog_opened_through_a_service_method_sits_under_the_screen_with_its_own_title()
    {
        App();
        Component("EditModal");
        Node("ts|web/EditModal.html#EditModal.template", NodeKind.Template, "EditModal.template");
        Edge(C("EditModal"), "ts|web/EditModal.html#EditModal.template", EdgeKind.Renders);
        Fact("screen-title", "ts|web/EditModal.html#EditModal.template", "web/edit.html", 1, ("via", "heading"), ("level", "4"), ("label", "edit.title"), ("labelText", ""));
        Fact("form-field", "ts|web/EditModal.html#EditModal.template", "web/edit.html", 3, ("via", "binding"), ("bind", "formControlName"), ("to", "form.title"), ("labelText", "Title"));
        // Alpha's click → Service.edit (one hop) → uses EditModal, which Alpha doesn't render.
        Node(C("Alpha") + ".open", NodeKind.Method, "Alpha.open");
        Node("ts|web/service.ts#Service.edit", NodeKind.Method, "Service.edit");
        Edge(C("Alpha") + ".open", "ts|web/service.ts#Service.edit", EdgeKind.Calls);
        Edge("ts|web/service.ts#Service.edit", C("EditModal"), EdgeKind.Uses);
        var set = Inventory.Build(Graph(), root);
        var dialog = Assert.Single(set.Screens.Single(s => s.Id == "page:p|/a").Dialogs);
        Assert.Equal(("Edit thing", false, "dialog"), (dialog.Title, dialog.TitleFromCode, dialog.Kind));
        Assert.Equal("Title", Assert.Single(dialog.Lines).Name.Words);
        Assert.Contains("### Dialog: Edit thing", InventoryFiles.Markdown(set));
    }

    [Fact]
    public void A_walk_longer_than_four_calls_does_not_open_a_dialog()
    {
        App();
        Component("FarModal");
        Fact("form-field", C("FarModal"), "web/far.tsx", 3, ("via", "native"), ("labelText", "Far"));
        Node(C("Alpha") + ".open", NodeKind.Method, "Alpha.open");
        var chain = Enumerable.Range(1, 5).Select(i => $"ts|web/s.ts#S.m{i}").ToList();
        foreach (var m in chain) Node(m, NodeKind.Method, m);
        Edge(C("Alpha") + ".open", chain[0], EdgeKind.Calls);
        for (var i = 0; i < 4; i++) Edge(chain[i], chain[i + 1], EdgeKind.Calls);
        Edge(chain[4], C("FarModal"), EdgeKind.Uses);   // five hops away
        var set = Inventory.Build(Graph(), root);
        Assert.Empty(set.Screens.Single(s => s.Id == "page:p|/a").Dialogs);
        Assert.Contains(set.NotPlaced, g => g.Title == "Far Modal" && g.Kind == "part");
    }

    [Fact]
    public void A_data_menu_is_titled_by_the_member_holding_it_and_placed_where_its_reader_is_reached()
    {
        App();
        Node("ts|web/factory.ts#Factory._reset", NodeKind.Method, "Factory._reset");
        Node("ts|web/factory.ts#Factory.seriesActions", NodeKind.Field, "Factory.seriesActions");
        Node("ts|web/factory.ts#Factory.getSeriesActions", NodeKind.Method, "Factory.getSeriesActions");
        Edge("ts|web/factory.ts#Factory.getSeriesActions", "ts|web/factory.ts#Factory.seriesActions", EdgeKind.Uses);
        Edge("ts|web/factory.ts#Factory._reset", "ts|web/factory.ts#Factory.seriesActions", EdgeKind.Uses);
        Node(C("Beta") + ".load", NodeKind.Method, "Beta.load");
        Edge(C("Beta") + ".load", "ts|web/factory.ts#Factory.getSeriesActions", EdgeKind.Calls);
        foreach (var (line, name) in new[] { (10, "Mark read"), (11, "Delete") })
            Fact("menu-item", "ts|web/factory.ts#Factory._reset", "web/factory.ts", line, ("via", "data"), ("menu", "web/factory.ts:10"), ("name", name), ("key", ""),
                ("address", ""), ("handler", "callback=this.markRead"));
        var set = Inventory.Build(Graph(), root);
        var menu = Assert.Single(set.Screens.Single(s => s.Id == "page:p|/b").Menus);
        Assert.Equal(("Series Actions", true), (menu.Title, menu.TitleFromCode));
        Assert.Equal(["Mark read", "Delete"], menu.Lines.Select(l => l.Name.Words));
        Assert.Equal((1, 1), (set.Menus, set.MenusPlaced));
        Assert.Equal("Series Actions", Inventory.MenuTitle("getSeriesActions"));
    }

    [Fact]
    public void The_fixed_skip_list_leaves_out_close_table_and_expand_controls_and_keeps_near_misses()
    {
        App();
        Button(C("Orphan"), 10, "Cancel");
        Button(C("Orphan"), 11, "", handler: "prop:onCloseClick");
        Button(C("Orphan"), 12, "Show more…");
        Button(C("Orphan"), 13, "Close account");
        Node(C("Orphan") + ".closeAndSave", NodeKind.Method, "Orphan.closeAndSave");
        Button(C("Orphan"), 14, "", handler: C("Orphan") + ".closeAndSave");
        Fact("form-field", C("Orphan"), "web/x.html", 15, ("via", "native"), ("labelText", "Items per page:"), ("to", "pageSize"));
        Fact("form-field", C("Orphan"), "web/x.html", 16, ("via", "native"), ("labelText", ""), ("to", "state.selectAll"));
        var set = Inventory.Build(Graph(), root);
        Assert.Equal([("web/x.html:10", "close"), ("web/x.html:11", "close"), ("web/x.html:12", "expand"), ("web/x.html:15", "table"), ("web/x.html:16", "table")],
            set.Skipped.Select(s => (s.Line.At, s.Reason)));
        var kept = set.NotPlaced.Single().Lines.Select(l => l.Name.Line).ToList();
        Assert.Contains("Close account", kept);
        Assert.Contains("❓ closeAndSave: no visible text found", kept);
        Assert.DoesNotContain("Cancel", InventoryFiles.Markdown(set));
        Assert.Null(Inventory.SkipAction("Back to library", "openLibrary"));
        Assert.Equal("table", Inventory.SkipAction(null, "handlePageChange"));
        // D335: a close-type handler doesn't skip a button showing its own words; bare "Next" is a real step; a page size saved to a model stays.
        Assert.Null(Inventory.SkipAction("Download", "close"));
        Assert.Equal("close", Inventory.SkipAction(null, "close"));
        Assert.Equal("close", Inventory.SkipAction("×", "close"));
        Assert.Null(Inventory.SkipAction("Next", "submit"));
        Assert.Equal("table", Inventory.SkipAction("Go to next page", "onNextPagePress"));
        Assert.Null(Inventory.SkipField("Page size", "PageSize", "Nop.Web.Areas.Admin.Models.Catalog.CategoryModel.PageSize"));
        Assert.Equal("table", Inventory.SkipField("Page size", "pageSize"));
        // D336: hidden server fields are set aside; dash-joined capitalised words are words, not a key.
        Assert.True(Inventory.IsHidden("order = 0, Label = \"X\", Type = Textbox, Hidden = Hidden"));
        Assert.True(Inventory.IsHidden("Label = \"X\", Hidden = HiddenType.Hidden"));
        Assert.False(Inventory.IsHidden("Label = \"X\", Hidden = HiddenType.HiddenIfNotSet"));
        Assert.False(Inventory.IsHidden("Label = \"X\""));
        Assert.False(Labels.LooksLikeKey("Multi-Episode"));
        Assert.True(Labels.LooksLikeKey("filter-scrobble"));
        Assert.True(Labels.LooksLikeKey("Admin.Common.Save"));
    }

    [Fact]
    public void A_display_name_label_merges_into_the_field_bound_to_it_is_set_aside_unbound_and_label_fields_group_by_class()
    {
        Node("M:Shop.ProductController.Edit", NodeKind.Method, "Shop.ProductController.Edit");
        Node("view:Views/Product/Edit.cshtml", NodeKind.View, "Edit.cshtml");
        Fact("on-load", "M:Shop.ProductController.Edit", "src/P.cs", 1, ("framework", "mvc"));
        Edge("M:Shop.ProductController.Edit", "view:Views/Product/Edit.cshtml", EdgeKind.Renders);
        Fact("form-field", "Shop|P:Shop.M.Name", "src/M.cs", 5, ("via", "server"), ("bind", "Name"), ("to", "Shop.M"), ("label", "Shop.Fields.Name"), ("attribute", "NopResourceDisplayName"));
        Fact("form-field", "Shop|P:Shop.M.Other", "src/M.cs", 6, ("via", "server"), ("bind", "Other"), ("to", "Shop.M"), ("label", "Shop.Fields.Other"), ("attribute", "DisplayNameAttribute"));
        Fact("form-field", "view:Views/Product/Edit.cshtml", "Views/Product/Edit.cshtml", 9, ("via", "binding"), ("bind", "asp-for"), ("to", "Name"), ("property", "P:Shop.M.Name"));
        Fact("form-field", "Feeds|P:Feeds.NewznabSettings.ApiKey", "src/N.cs", 3, ("via", "server"), ("bind", "ApiKey"), ("to", "Feeds.NewznabSettings"), ("label", "NewznabApiKey"), ("attribute", "FieldDefinition"));
        var set = Inventory.Build(Graph(), root);
        var line = Assert.Single(set.Screens.Single().Lines);
        Assert.Equal(("Product name", "src/M.cs"), (line.Name.Words, Assert.Single(line.Merged).File));
        var aside = Assert.Single(set.SetAside);
        Assert.Equal(("src/M.cs:6", "unbound-model-label"), (aside.At, aside.Reason));
        var cls = Assert.Single(set.NotPlaced);
        Assert.Equal(("server-class", "Newznab Settings", "API Key"), (cls.Kind, cls.Title, Assert.Single(cls.Lines).Name.Words));
        Assert.Contains("### Newznab Settings, 1 field ", InventoryFiles.Markdown(set));
    }

    [Fact]
    public void The_check_passes_on_fresh_files_is_stable_and_fails_on_a_hand_edited_one()
    {
        App();
        var graph = Graph();
        InventoryFiles.Write(root, Inventory.Build(graph, root));
        var first = File.ReadAllText(Path.Combine(root, InventoryFiles.JsonFile));
        Assert.Equal((0, 2), InventoryFiles.Write(root, Inventory.Build(graph, root)));
        Assert.Equal(first, File.ReadAllText(Path.Combine(root, InventoryFiles.JsonFile)));
        var ok = InventoryFiles.Check(root, graph);
        Assert.True((bool)ok["ok"]!, ok.ToJsonString());
        Assert.Equal(5, (int)ok["controls"]!);
        var md = Path.Combine(root, InventoryFiles.MarkdownFile);
        File.WriteAllText(md, File.ReadAllText(md).Replace("Lonely", "Lonely, edited"));
        var bad = InventoryFiles.Check(root, graph);
        Assert.False((bool)bad["ok"]!);
        Assert.Equal("byte-for-byte: specs/inventory.md isn't what this graph builds (run `stangent inventory`)", (string)bad["problems"]![0]!);
    }

    [Fact]
    public void Controls_in_test_code_are_not_lines()
    {
        App();
        Node("ts|web/Spec.ts#Spec", NodeKind.Component, "Spec", test: true);
        Button("ts|web/Spec.ts#Spec", 30, "Test only");
        var set = Inventory.Build(Graph(), root);
        Assert.DoesNotContain(set.AllLines, l => l.Name.Words == "Test only");
    }

    [Theory]
    [InlineData("SeriesIndexBody", "Series Index Body")]
    [InlineData("edit-reading-list", "Edit Reading List")]
    [InlineData("GROUPS", "Groups")]
    [InlineData("URLSettings", "URL Settings")]
    public void Code_names_are_said_readable(string name, string readable) => Assert.Equal(readable, Inventory.Readable(name));
}

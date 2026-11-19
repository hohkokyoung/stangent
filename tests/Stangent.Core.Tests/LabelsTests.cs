using Stangent.Flows;
using Stangent.Graph;

namespace Stangent.Core.Tests;

/// <summary>D327, D333: the label lookup, on a small made-up app with one translation file of each kind.</summary>
public sealed class LabelsTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("stangent-labels-").FullName;
    readonly ProjectFragment f = new() { ProjectId = "project:app", Fingerprint = "x" };

    public LabelsTests()
    {
        // A nested i18n file whose keys carry a template prefix, a flat one, and a resource XML file (keys looked up ignoring case).
        Write("web/src/assets/i18n/en.json", """{ "device-card": { "save": "Save device", "name-label": "Device name" }, "title": { "devices": "My Devices" }, "common": { "cancel": "Cancel" }, "dialog": { "cancel": "{{common.cancel}}", "posts": "Posts of {0}", "copy": "{{common.copy}}" } }""");
        Write("server/Localization/en.json", """{ "FeedUrl": "Feed URL", "FeedUrlHelpText": "Where the feed lives", "ModeFast": "Fast" }""");
        Write("res/defaultResources.nopres.xml", """
            <Language Name="English">
              <LocaleResource Name="Shop.Fields.Name"><Value>Product name</Value></LocaleResource>
              <LocaleResource Name="Shop.Fields.Name.Hint"><Value>The name shoppers see.</Value></LocaleResource>
              <LocaleResource Name="Shop.Products.Title"><Value>Products</Value></LocaleResource>
              <LocaleResource Name="Shop.Products.Heading"><Value>All products</Value></LocaleResource>
            </Language>
            """);
    }

    public void Dispose() => Directory.Delete(root, true);

    void Write(string rel, string text)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    Fact Add(string kind, string owner, int line, params (string Key, string Value)[] data)
    {
        var fact = new Fact("test", kind, owner, "src/x", line, data.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal));
        f.Facts.Add(fact);
        return fact;
    }

    void Node(string id, string kind, string name) => f.Nodes.Add(new Node(id, kind, name, "project:app", "src/x", 1, 1));

    Labels Build()
    {
        var graph = new CodeGraph { Commit = "c", DirtyFiles = [], NotInCommit = [] };
        graph.Fragments[f.ProjectId] = f;
        return new Labels(graph, root);
    }

    [Fact]
    public void A_buttons_key_is_looked_up_in_any_format_else_its_words_else_it_is_a_question_mark_with_its_handler()
    {
        Node("ts|card.ts#Card.save", NodeKind.Method, "Card.save");
        Node("ts|card.ts#Card.loadPage", NodeKind.Method, "Card.loadPage");
        var keyed = Add("ui-event", "t", 1, ("event", "click"), ("handler", "ts|card.ts#Card.save"), ("keys", "device-card.save"), ("text", ""));
        var resource = Add("ui-event", "t", 2, ("event", "submit"), ("handler", ""), ("keys", "shop.products.title"), ("text", ""));
        var worded = Add("ui-event", "t", 3, ("event", "click"), ("handler", "inline"), ("keys", ""), ("text", "  Refresh \n now "));
        var none = Add("ui-event", "t", 4, ("event", "click"), ("handler", "ts|card.ts#Card.loadPage"), ("keys", "device-card.missing"), ("text", "×"));
        // A handler handed to a component's prop takes the words of that component's own button run by the prop.
        Node("ts|logs.tsx#LogPage", NodeKind.Component, "LogPage");
        Node("ts|logs.tsx#LogFiles", NodeKind.Component, "LogFiles");
        Node("ts|logs.tsx#LogPage.refresh", NodeKind.Method, "LogPage.refresh");
        f.Edges.Add(new Edge("ts|logs.tsx#LogPage", "ts|logs.tsx#LogFiles", EdgeKind.Renders, Label.Certain));
        var passed = Add("ui-event", "ts|logs.tsx#LogPage", 5, ("element", "LogFiles"), ("event", "onRefreshPress"), ("handler", "ts|logs.tsx#LogPage.refresh"), ("keys", ""), ("text", ""));
        Add("ui-event", "ts|logs.tsx#LogFiles", 9, ("element", "Button"), ("event", "onPress"), ("handler", "prop:onRefreshPress"), ("keys", "FeedUrl"), ("text", ""));
        var referenced = Add("ui-event", "t", 6, ("event", "click"), ("handler", "inline"), ("keys", "dialog.cancel"), ("text", ""));
        var several = Add("ui-event", "t", 7, ("event", "click"), ("handler", "ts|a.ts#A.first|ts|b.ts#B.second"), ("keys", ""), ("text", ""));
        var labels = Build();
        // A value that only points at another key is that key's words; a list of handlers gives the first one's short name.
        Assert.Equal("Cancel", labels.Button(referenced).Words);
        Assert.Null(labels.Text("dialog.copy"));
        Assert.Equal("❓ first: no visible text found", labels.Button(several).Line);
        Assert.Equal(("Feed URL", "prop", "refresh"), (labels.Button(passed).Words, labels.Button(passed).Source, labels.Button(passed).Code));
        Assert.Equal(("Save device", "key"), (labels.Button(keyed).Words, labels.Button(keyed).Source));
        Assert.Equal("Products", labels.Button(resource).Words);
        Assert.Equal(("Refresh now", "text"), (labels.Button(worded).Words, labels.Button(worded).Source));
        Assert.Equal("❓ loadPage: no visible text found (key device-card.missing is in no translation file)", labels.Button(none).Line);
    }

    [Fact]
    public void A_fields_label_and_help_come_from_its_keys_its_words_or_the_property_asp_for_reaches()
    {
        var server = Add("form-field", "Shop|P:Shop.Models.ProductModel.Name", 10, ("via", "server"), ("bind", "Name"), ("to", "Shop.Models.ProductModel"),
            ("label", "Shop.Fields.Name"), ("help", ""), ("choices", ""));
        var razor = Add("form-field", "view:Edit.cshtml", 5, ("via", "binding"), ("bind", "asp-for"), ("to", "Locales[i].Name"), ("label", ""), ("labelText", ""),
            ("help", ""), ("helpText", ""), ("property", "P:Shop.Models.ProductModel.Name"), ("choices", ""));
        var provider = Add("form-field", "Feeds|P:Feeds.FeedSettings.Url", 3, ("via", "server"), ("bind", "Url"), ("label", "FeedUrl"), ("help", ""));
        var asWords = Add("form-field", "Feeds|P:Feeds.FeedSettings.Profiles", 4, ("via", "server"), ("bind", "Profiles"), ("label", "Language Profiles"), ("help", ""));
        var bare = Add("form-field", "t", 7, ("via", "native"), ("bind", ""), ("to", "form.notes"), ("label", ""), ("labelText", ""), ("help", ""), ("helpText", ""));
        var labels = Build();
        Assert.Equal(("Product name", "The name shoppers see.", "model"), (labels.Field(razor).Words, labels.Field(razor).Help, labels.Field(razor).Source));
        Assert.Equal(("Product name", "The name shoppers see."), (labels.Field(server).Words, labels.Field(server).Help));
        // Help beside the label key by a usual suffix, when the field gives none.
        Assert.Equal(("Feed URL", "Where the feed lives"), (labels.Field(provider).Words, labels.Field(provider).Help));
        Assert.Equal("❓ form.notes: no visible text found", labels.Field(bare).Line);
        // A key no file holds that reads as words is shown as written, as translation libraries do.
        Assert.Equal(("Language Profiles", "written"), (labels.Field(asWords).Words, labels.Field(asWords).Source));
    }

    [Fact]
    public void Choices_by_visible_names_an_enums_members_through_their_labels_a_lists_items_through_the_lookup()
    {
        Add("field-choice", "Feeds|T:Feeds.Mode", 1, ("enum", "Feeds.Mode"), ("member", "Slow"), ("value", "1"), ("label", ""), ("help", "slow but sure"));
        Add("field-choice", "Feeds|T:Feeds.Mode", 2, ("enum", "Feeds.Mode"), ("member", "Fast"), ("value", "0"), ("label", "ModeFast"), ("help", ""));
        var byEnum = Add("form-field", "f", 1, ("via", "server"), ("choices", "enum:Mode"));
        var byList = Add("form-field", "f", 2, ("via", "native"), ("choices", "list:Shop.Fields.Name|Large|Shop.Size.Huge"));
        var runtime = Add("form-field", "f", 3, ("via", "native"), ("choices", "runtime:sizes"));
        var placeholder = Add("form-field", "f", 4, ("via", "native"), ("choices", "list:---"));
        var labels = Build();
        Assert.Equal(["Fast", "❓ Slow: no visible text found"], labels.ChoicesOf(byEnum).Names.Select(n => n.Line));
        Assert.Equal(["Product name", "Large", "❓ Shop.Size.Huge: no visible text found"], labels.ChoicesOf(byList).Names.Select(n => n.Line));
        Assert.Equal(("runtime", false), (labels.ChoicesOf(placeholder).Kind, labels.ChoicesOf(placeholder).Visible));
        Assert.Equal(("runtime", "made at runtime (sizes)", false), (labels.ChoicesOf(runtime).Kind, labels.ChoicesOf(runtime).Note, labels.ChoicesOf(runtime).Visible));
    }

    [Fact]
    public void A_screen_is_named_by_its_routes_title_else_its_components_title_or_heading_and_an_actions_by_its_views()
    {
        Node("page:p|/devices", NodeKind.Page, "/devices");
        Node("page:p|/shelf", NodeKind.Page, "/shelf");
        Node("page:p|/plain", NodeKind.Page, "/plain");
        Node("page:p|/odd", NodeKind.Page, "/odd");
        Node("ts|shelf.ts#Shelf", NodeKind.Component, "Shelf");
        Node("ts|shelf.html#Shelf.template", NodeKind.Template, "Shelf.template");
        Node("M:Shop.ProductController.List", NodeKind.Method, "Shop.ProductController.List");
        Add("screen-title", "page:p|/devices", 4, ("via", "route"), ("title", "title.devices"));
        Add("screen-title", "page:p|/odd", 4, ("via", "route"), ("title", "title.unknown"));
        // A heading written in branches: the words without a placeholder are its plain form.
        Node("page:p|/posts", NodeKind.Page, "/posts");
        Add("screen-title", "page:p|/posts", 4, ("via", "heading"), ("level", "1"), ("label", "dialog.posts|common.cancel"), ("labelText", ""));
        f.Edges.Add(new Edge("page:p|/shelf", "ts|shelf.ts#Shelf", EdgeKind.RoutesTo, Label.Certain));
        f.Edges.Add(new Edge("ts|shelf.ts#Shelf", "ts|shelf.html#Shelf.template", EdgeKind.Renders, Label.Certain));
        Add("screen-title", "ts|shelf.html#Shelf.template", 3, ("via", "heading"), ("level", "4"), ("label", ""), ("labelText", "Bookshelf"));
        Add("screen-title", "ts|shelf.html#Shelf.template", 2, ("via", "heading"), ("level", "2"), ("label", "device-card.name-label"), ("labelText", ""));
        f.Edges.Add(new Edge("M:Shop.ProductController.List", "view:Views/Product/List.cshtml", EdgeKind.Renders, Label.Certain));
        f.Edges.Add(new Edge("route:products", "M:Shop.ProductController.List", EdgeKind.RoutesTo, Label.Certain));
        Node("route:products", NodeKind.Endpoint, "GET /products");
        Add("screen-title", "view:Views/Product/List.cshtml", 9, ("via", "heading"), ("level", "1"), ("label", "Shop.Products.Heading"), ("labelText", ""));
        Add("screen-title", "view:Views/Product/List.cshtml", 2, ("via", "view-data"), ("label", "Shop.Products.Title"), ("labelText", ""));
        // A screen component that wraps its body: the body's titled element counts, its headings don't.
        Node("page:p|/wrapped", NodeKind.Page, "/wrapped");
        Node("ts|w.tsx#Wrapped", NodeKind.Component, "Wrapped");
        Node("ts|w.tsx#Provider", NodeKind.Component, "Provider");
        Node("ts|w.tsx#Body", NodeKind.Component, "Body");
        f.Edges.Add(new Edge("page:p|/wrapped", "ts|w.tsx#Wrapped", EdgeKind.RoutesTo, Label.Certain));
        f.Edges.Add(new Edge("ts|w.tsx#Wrapped", "ts|w.tsx#Provider", EdgeKind.Renders, Label.Certain));
        f.Edges.Add(new Edge("ts|w.tsx#Provider", "ts|w.tsx#Body", EdgeKind.Renders, Label.Certain));
        Add("screen-title", "ts|w.tsx#Provider", 1, ("via", "heading"), ("level", "1"), ("label", ""), ("labelText", "A part's heading"));
        Add("screen-title", "ts|w.tsx#Body", 2, ("via", "title-attribute"), ("label", ""), ("labelText", "Wrapped page"));
        var labels = Build();
        Assert.Equal("Wrapped page (/wrapped)", Labels.ScreenLine(labels.Screen("page:p|/wrapped")));
        Assert.Equal("Cancel (/posts)", Labels.ScreenLine(labels.Screen("page:p|/posts")));
        Assert.Equal("My Devices (/devices)", Labels.ScreenLine(labels.Screen("page:p|/devices")));
        Assert.Equal("Device name (/shelf)", Labels.ScreenLine(labels.Screen("page:p|/shelf")));
        Assert.Equal("Products (GET /products)", Labels.ScreenLine(labels.Screen("M:Shop.ProductController.List")));
        Assert.Equal("❓ /plain: no visible text found", Labels.ScreenLine(labels.Screen("page:p|/plain")));
        // A route title that reads as a key and is in no file isn't shown as words.
        Assert.Equal("❓ /odd: no visible text found", Labels.ScreenLine(labels.Screen("page:p|/odd")));
    }

    [Theory]
    [InlineData("title.all-series", true)]
    [InlineData("Admin.Common.Save", true)]
    [InlineData("IndexerSettingsApiPath", true)]
    [InlineData("Save", false)]
    [InlineData("All series", false)]
    [InlineData("e.g. a name", false)]
    public void A_value_reads_as_a_key_when_it_is_one_token_joined_by_dots_dashes_or_humps(string value, bool key) =>
        Assert.Equal(key, Labels.LooksLikeKey(value));
}

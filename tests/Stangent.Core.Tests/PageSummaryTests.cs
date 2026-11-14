using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Flows;
using Stangent.Graph;

namespace Stangent.Core.Tests;

/// <summary>
/// Layer 4 (D251–D255): the summary code cuts from flows, on hand-made flow files in the three stacks (a React screen and its click,
/// an Angular click with a named access rule and shared code, Razor screens with server resources, handlers and a job).
/// </summary>
public sealed class PageSummaryTests : IDisposable
{
    const string Cfg = "web/tsconfig.json";
    const string Area = "Shop";
    readonly string root = Directory.CreateTempSubdirectory("stangent-pages-").FullName;
    readonly CodeGraph graph = new() { Commit = "abc" };
    readonly List<JsonObject> index = [];

    public void Dispose() => Directory.Delete(root, true);

    static JsonObject Cond(string kind, string at, string code, string? @case = null)
    {
        var o = new JsonObject { ["kind"] = kind, ["at"] = at, ["code"] = code };
        if (@case is not null) o["case"] = @case;
        return o;
    }

    static JsonObject Step(int n, int? from, string kind, string what, string at, string code, params JsonObject[] conds)
    {
        var o = new JsonObject { ["n"] = n, ["kind"] = kind, ["what"] = what, ["label"] = "certain", ["at"] = at, ["code"] = code, ["conditions"] = new JsonArray([.. conds.Select(c => (JsonNode)c.DeepClone())]) };
        if (from is not null) o["from"] = from;
        return o;
    }

    void FlowFile(string id, string kind, string name, string point, string start, JsonObject actor, JsonArray steps, JsonArray? links = null,
        JsonArray? exits = null, JsonArray? checks = null, JsonArray? unknowns = null, JsonArray? guesses = null, string[]? triggers = null)
    {
        var flow = new JsonObject
        {
            ["contract"] = "flows", ["version"] = 1, ["commit"] = "abc", ["id"] = id, ["name"] = name, ["kind"] = kind, ["home"] = "",
            ["start"] = new JsonObject { ["what"] = kind, ["point"] = point, ["at"] = start, ["code"] = "start()", ["triggers"] = new JsonArray([.. (triggers ?? []).Select(t => (JsonNode)t)]) },
            ["actor"] = actor, ["inputs"] = new JsonArray(), ["checks"] = checks ?? [], ["steps"] = steps, ["exits"] = exits ?? [], ["scenarios"] = new JsonArray(),
            ["links"] = links ?? [], ["guesses"] = guesses ?? [], ["unknowns"] = unknowns ?? [], ["displayOnly"] = new JsonArray(),
        };
        var file = $"specs/areas/shop/{id}.json";
        Directory.CreateDirectory(Path.Combine(root, "specs/areas/shop"));
        File.WriteAllText(Path.Combine(root, file), CanonicalJson.Serialize(flow));
        index.Add(new JsonObject
        {
            ["id"] = id, ["kind"] = kind, ["point"] = point, ["start"] = start, ["name"] = name, ["file"] = file,
            ["links"] = new JsonArray([.. (links ?? []).OfType<JsonObject>().Select(l => (string?)l["flow"]).OfType<string>().Distinct().Select(x => (JsonNode)x)]),
        });
    }

    static JsonObject Signed => new() { ["who"] = "signed-in user", ["why"] = "fallback policy", ["label"] = "certain", ["at"] = "src/Api/Startup.cs:10", ["code"] = "FallbackPolicy" };
    static JsonObject System => new() { ["who"] = "system", ["why"] = "runs when sent", ["label"] = "certain", ["at"] = "src/Core/H.cs:3", ["code"] = "Handle" };

    (Areas, Placement) Build()
    {
        // React: the /items screen routes to Items; its Save button's click walks into the API.
        var page = $"page:{Cfg}|/items";
        var items = $"ts|web/src/Items/Items.tsx#Items";
        graph.LinkedNodes.Add(new Node(page, NodeKind.Page, page, $"project:{Cfg}", null));
        graph.LinkedNodes.Add(new Node(items, NodeKind.Component, "Items", $"project:{Cfg}", "web/src/Items/Items.tsx"));
        graph.LinkedEdges.Add(new Edge(page, items, EdgeKind.RoutesTo, Label.Certain));
        var save = $"{items}.save";
        graph.LinkedNodes.Add(new Node(save, NodeKind.Method, "save", $"project:{Cfg}", "web/src/Items/Items.tsx"));
        graph.LinkedEdges.Add(new Edge(items, save, EdgeKind.Contains, Label.Certain));
        Directory.CreateDirectory(Path.Combine(root, "web/src/i18n"));
        File.WriteAllText(Path.Combine(root, "web/src/i18n/en.json"), """{ "SaveItem": "Save the item", "Title": "Your items" }""");

        var mutation = Cond("mutation", "web/src/Items/useSave.ts:4", "mutate(…)", "useMutation");
        var drawn = Cond("drawn", "web/src/Items/Items.tsx:9", "isOpen", "the dialog is open");
        FlowFile("screen-items", "screen-opens", "/items", page, "web/src/App.tsx:3", Signed, [
            Step(1, null, "call", "calls translate", "web/src/Items/Items.tsx:5", "title: translate('Title'),"),
            Step(2, null, "http", "calls POST /api/items", "web/src/Items/useSave.ts:5", "post()", drawn, mutation),
            Step(3, 2, "database", "saves Item", "src/Core/ItemService.cs:20", "_repo.Insert(item);"),
        ]);
        FlowFile("click-items-save", "click", "Save", save, "web/src/Items/Items.tsx:12", Signed, [
            Step(1, null, "call", "calls translate", "web/src/Items/Items.tsx:12", "<button>{translate('SaveItem')}</button>"),
            Step(2, null, "http", "calls POST /api/items", "web/src/Items/useSave.ts:5", "post()", mutation),
            Step(3, 2, "database", "saves Item", "src/Core/ItemService.cs:20", "_repo.Insert(item);"),
            Step(4, 2, "message", "sends ItemAdded", "src/Core/ItemService.cs:21", "Publish(new ItemAdded())"),
            Step(5, 2, "outside-call", "calls outside the app: outside HTTP GET: URL built at runtime", "src/Core/Proxy.cs:8", "_http.Get(url)"),
            Step(6, 2, "package", "calls Ensure.That in the package X", "src/Core/ItemService.cs:19", "Ensure.That(item)"),
        ],
        links: [new JsonObject { ["kind"] = "message", ["to"] = "AuditHandler.Handle", ["flow"] = "handler-audit", ["step"] = 4, ["label"] = "guessed", ["sentence"] = "Sends ItemAdded", ["at"] = "src/Core/ItemService.cs:21", ["code"] = "Publish" }],
        exits: [new JsonObject { ["kind"] = "throw", ["what"] = "throws BadRequestException", ["point"] = "p", ["step"] = 3, ["at"] = "src/Api/RestController.cs:30", ["code"] = "throw new BadRequestException();", ["conditions"] = new JsonArray(Cond("if", "src/Api/RestController.cs:29", "item == null")) }],
        checks: [
            new JsonObject { ["what"] = "ItemResource.Name: NotEmpty", ["rule"] = "NotEmpty", ["property"] = "Api|P:Api.ItemResource.Name", ["message"] = "", ["onlyWarns"] = false, ["at"] = "src/Api/ItemValidator.cs:5", ["code"] = ".NotEmpty()" },
            new JsonObject { ["what"] = "ItemResource.Name: MaxLength(50)", ["rule"] = "MaxLength", ["property"] = "Api|P:Api.ItemResource.Name", ["message"] = "Too long", ["onlyWarns"] = false, ["at"] = "src/Api/ItemValidator.cs:6", ["code"] = ".MaximumLength(50)" },
            new JsonObject { ["what"] = "Api|M:Api.Rest`1.ValidateId(System.Int32): guard", ["rule"] = "guard", ["property"] = "Api|M:Api.Rest`1.ValidateId(System.Int32)", ["message"] = "", ["onlyWarns"] = false, ["at"] = "src/Api/Rest.cs:3", ["code"] = "if (id <= 0)" },
        ],
        unknowns: [new JsonObject { ["point"] = "p", ["why"] = "outside HTTP GET: URL built at runtime", ["step"] = 5, ["at"] = "src/Core/Proxy.cs:8", ["code"] = "_http.Get(url)" }],
        guesses: [new JsonObject { ["what"] = "Sends ItemAdded, handled by AuditHandler.Handle", ["reason"] = "handlers found by the event's type", ["step"] = 4 }]);

        // Angular: a click on no screen, a named access rule, and shared code saving along two paths.
        FlowFile("click-reading-list-remove", "click", "Remove", "ts|web/src/lists/list.component.ts#ListComponent.remove", "web/src/lists/list.component.ts:30",
            new JsonObject { ["who"] = "user who passes DisallowRole(roles = [\"Read Only\"])", ["why"] = "attribute", ["label"] = "certain", ["at"] = "src/Api/ListController.cs:12", ["code"] = "[DisallowRole]" },
            [Step(1, null, "call", "calls ListService.Remove", "src/Api/ListController.cs:14", "_lists.Remove(id)", Cond("if", "src/Api/ListController.cs:13", "id > 0")),
             Step(2, null, "call", "calls ListService.Prune", "src/Api/ListController.cs:15", "_lists.Prune()", Cond("if", "src/Api/ListController.cs:13", "id > 0"), Cond("else", "src/Api/ListController.cs:14", "keep")),
             Step(3, 1, "database", "reads ReadingList", "src/Api/ListService.cs:7", "_db.Get(id)"),
             Step(4, 2, "database", "reads ReadingList", "src/Api/ListService.cs:20", "_db.Get(id)")],
            exits: [
                new JsonObject { ["kind"] = "throw", ["what"] = "throws KavitaException", ["point"] = "p", ["step"] = 1, ["at"] = "src/Api/Guard.cs:5", ["code"] = "throw new KavitaException();", ["conditions"] = new JsonArray(Cond("if", "src/Api/Guard.cs:4", "list == null")) },
                new JsonObject { ["kind"] = "throw", ["what"] = "throws KavitaException", ["point"] = "p", ["step"] = 2, ["at"] = "src/Api/Guard.cs:9", ["code"] = "throw new KavitaException();", ["conditions"] = new JsonArray(Cond("if", "src/Api/Guard.cs:8", "list == null")) },
            ],
            links: [
                new JsonObject { ["kind"] = "continues", ["to"] = "Repo.Delete", ["flow"] = "shared-repo-delete", ["step"] = 1, ["label"] = "certain", ["sentence"] = "Stops here", ["at"] = "src/Api/ListController.cs:14", ["code"] = "_lists.Remove(id)" },
                new JsonObject { ["kind"] = "continues", ["to"] = "Repo.Delete", ["flow"] = "shared-repo-delete", ["step"] = 2, ["label"] = "certain", ["sentence"] = "Stops here", ["at"] = "src/Api/ListController.cs:15", ["code"] = "_lists.Prune()" },
            ]);
        FlowFile("shared-repo-delete", "shared", "Repo.Delete", "Core|M:Core.Repo.Delete", "src/Core/Repo.cs:5",
            new JsonObject { ["who"] = "the flow that continues here", ["why"] = "", ["label"] = "certain" },
            [Step(1, null, "database", "saves ReadingList", "src/Core/Repo.cs:6", "_db.Delete(x)"), Step(2, null, "database", "reads ReadingList", "src/Core/Repo.cs:4", "_db.Get(x)")]);

        // Razor: a server-rendered screen showing a resource; two handlers answering one message; a job.
        Directory.CreateDirectory(Path.Combine(root, "src/Web/App_Data"));
        File.WriteAllText(Path.Combine(root, "src/Web/App_Data/defaultResources.nopres.xml"),
            """<Language><LocaleResource Name="Checkout.Title"><Value>Checkout</Value></LocaleResource></Language>""");
        FlowFile("screen-checkout", "screen-opens", "GET /checkout", "Web|M:Web.CheckoutController.Index", "src/Web/CheckoutController.cs:10",
            new JsonObject { ["who"] = "anyone, signed in or not", ["why"] = "AllowAnonymous", ["label"] = "certain", ["at"] = "src/Web/CheckoutController.cs:9", ["code"] = "[AllowAnonymous]" },
            [Step(1, null, "call", "calls ILocalizationService.GetResourceAsync", "src/Web/CheckoutController.cs:11", "await _loc.GetResourceAsync(\"checkout.title\")"),
             Step(2, null, "call", "calls SKCanvas.Translate", "src/Web/Img.cs:3", "surface.Translate(1, 0);")]);
        FlowFile("handler-audit", "handler", "AuditHandler.Handle on Core.Events.ItemAdded", "Core|M:Core.AuditHandler.Handle", "src/Core/AuditHandler.cs:3", System,
            [Step(1, null, "database", "saves AuditEntry", "src/Core/AuditHandler.cs:4", "_db.Insert(e)")], triggers: ["Core.Events.ItemAdded"]);
        FlowFile("handler-mail", "handler", "MailHandler.Handle on Core.Events.ItemAdded", "Core|M:Core.MailHandler.Handle", "src/Core/MailHandler.cs:3", System,
            [Step(1, null, "outside-call", "calls outside the app: outside SMTP send", "src/Core/MailHandler.cs:4", "smtp.Send(m)")], triggers: ["Core.Events.ItemAdded"]);
        FlowFile("job-cleanup", "job", "CleanupTask.Execute", "Core|M:Core.CleanupTask.Execute", "src/Core/CleanupTask.cs:3", System,
            [Step(1, null, "database", "saves ItemArchive", "src/Core/CleanupTask.cs:5", "_db.Insert(a)")], triggers: ["every 24 hours"]);

        Directory.CreateDirectory(Path.Combine(root, FlowFiles.Folder));
        File.WriteAllText(Path.Combine(root, FlowFiles.IndexFile), new JsonObject { ["commit"] = "abc", ["flows"] = new JsonArray([.. index.Select(f => (JsonNode)f.DeepClone())]) }.ToJsonString());
        var a = Areas.Load(graph, root);
        var placement = new Placement();
        foreach (var f in index.Where(f => (string)f["kind"]! != "shared")) placement.AreaOf[(string)f["id"]!] = Area;
        return (a, placement);
    }

    AreaSummary Summary(int limit = PageSummary.SectionLimit)
    {
        var (a, placement) = Build();
        return PageSummary.Build(root, a, placement, Area, "shop", limit);
    }

    [Fact]
    public void Exits_merge_by_what_they_throw_and_their_own_condition_and_reads_by_table()
    {
        var sec = Summary().Find("clicks")!;
        // The Angular click: the same exit written twice, and the same table read twice under different paths.
        var exit = sec.Items.Single(i => i.Kind == "exit");
        Assert.Equal("src/Api/Guard.cs:5", exit.At);
        Assert.Equal(["src/Api/Guard.cs:9"], exit.AlsoAt);
        Assert.Contains("and 1 other place", PageSummary.ItemLine(exit, true));
        var read = sec.Items.Single(i => i.Kind == "read");
        Assert.Equal(["id > 0"], read.Conditions.Select(c => c.Code));   // D265: only what both places share
        Assert.Single(read.AlsoAt);
    }

    [Fact]
    public void A_section_over_the_limit_splits_by_flow_in_order()
    {
        var s = Summary(limit: 1_000);
        var parts = s.Sections.Where(x => x.Id.StartsWith("screen-items-part-", StringComparison.Ordinal)).ToList();
        Assert.Equal(["screen-items-part-1", "screen-items-part-2"], parts.Select(x => x.Id));
        Assert.Equal("/items (1 of 2)", parts[0].Title);
        Assert.Equal([["screen-items"], ["click-items-save"]], parts.Select(x => x.Flows));
        Assert.Equal("W1", parts[1].Items[0].Id);   // each part numbers its own items
        Assert.Null(s.Find("screen-items"));
    }

    static SummaryItem Item(SummarySection s, string kind, string text) => s.Items.First(i => i.Kind == kind && i.Text.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void Sections_follow_D253_screens_with_their_clicks_then_loose_clicks_then_messages_and_jobs()
    {
        var s = Summary();
        Assert.Equal(["in-short", "screen-items", "screen-checkout", "clicks", "on-core-events-itemadded", "jobs"], s.Sections.Select(x => x.Id));
        Assert.Equal(["screen-items", "click-items-save"], s.Find("screen-items")!.Flows);
        Assert.Equal(["click-reading-list-remove"], s.Find("clicks")!.Flows);
        Assert.Equal("When ItemAdded is sent", s.Find("on-core-events-itemadded")!.Title);
        Assert.Equal(PageSummary.OnItsOwn, s.Find("jobs")!.Group);
        Assert.Empty(s.Missing);
    }

    [Fact]
    public void Who_is_said_per_section_by_template_or_as_the_named_rule_when_the_area_doesnt_share_one()
    {
        var s = Summary();
        Assert.Empty(s.Find("in-short")!.Items);
        Assert.Equal("who: anyone signed in", s.Find("screen-items")!.Items.Single(i => i.Kind == "who").Text);
        Assert.Equal("who: anyone, signed in or not", s.Find("screen-checkout")!.Items.Single(i => i.Kind == "who").Text);
        Assert.Equal("who: user who passes DisallowRole(roles = [\"Read Only\"])", s.Find("clicks")!.Items.Single(i => i.Kind == "who").Text);
        // Two handlers, one wording: one item with both flows.
        var who = s.Find("on-core-events-itemadded")!.Items.Single(i => i.Kind == "who");
        Assert.Equal("who: the app itself, on its own", who.Text);
        Assert.Equal(["handler-audit", "handler-mail"], who.Flows);
    }

    [Fact]
    public void Items_carry_every_condition_on_the_way_their_labels_and_must_cite()
    {
        var sec = Summary().Find("screen-items")!;
        // The screen reaches the click's save under the click's condition and the dialog's: one item, both flows (D251's example).
        var save = sec.Items.Single(i => i.Kind == "save");
        Assert.Equal(["screen-items", "click-items-save"], save.Flows);
        Assert.Equal(["drawn", "mutation"], save.Conditions.Select(c => c.Kind));
        Assert.StartsWith("only while `isOpen`", save.Conditions[0].Text);
        Assert.True(save.MustCite);
        var message = Item(sec, "message", "ItemAdded");
        Assert.Equal("guessed", message.Label);
        Assert.Equal("handlers found by the event's type", message.Reason);
        Assert.Contains("→ handled by AuditHandler.Handle", message.Text);
        var outside = Item(sec, "outside-call", "SMTP".Length > 0 ? "URL built" : "");
        Assert.Equal("unknown", outside.Label);
        Assert.DoesNotContain(sec.Items, i => i.Kind == "unknown");   // the ❓ sits on the call it's about
        var exit = Item(sec, "exit", "BadRequestException");
        Assert.Equal(["if"], exit.Conditions.Select(c => c.Kind));   // D264: its own condition, not the path
        Assert.DoesNotContain(sec.Items, i => i.Text.Contains("Ensure.That", StringComparison.Ordinal));   // package code: dropped
        Assert.False(Item(sec, "shows", "Save the item").MustCite);
    }

    [Fact]
    public void Shows_are_translation_calls_turned_into_English_through_JSON_and_XML_resources()
    {
        var s = Summary();
        Assert.Equal("shows \"Your items\" (text key 'Title')", Item(s.Find("screen-items")!, "shows", "Your items").Text);
        Assert.Equal("shows \"Checkout\" (text key 'checkout.title')", s.Find("screen-checkout")!.Items.Single(i => i.Kind == "shows").Text);   // a call named Translate with no quoted key isn't one
    }

    [Fact]
    public void Checks_group_by_field_and_a_guard_is_named_by_its_member()
    {
        var sec = Summary().Find("screen-items")!;
        Assert.Equal("checks ItemResource.Name: NotEmpty, MaxLength(50) \"Too long\"", Item(sec, "check", "ItemResource.Name").Text);
        Assert.Equal("checks Rest.ValidateId: guard", Item(sec, "check", "ValidateId").Text);
    }

    [Fact]
    public void A_save_in_shared_code_is_kept_once_with_only_the_conditions_on_every_path()
    {
        var sec = Summary().Find("clicks")!;
        var save = sec.Items.Single(i => i.Kind == "save");
        Assert.Equal("saves ReadingList (in shared code Repo.Delete)", save.Text);
        Assert.Equal(["id > 0"], save.Conditions.Select(c => c.Code));
        Assert.DoesNotContain(sec.Items, i => i.Kind == "read" && i.Text.Contains("shared", StringComparison.Ordinal));   // reads in shared code aren't followed
    }

    [Fact]
    public void Ids_are_a_letter_per_kind_numbered_in_order_and_the_bytes_repeat()
    {
        var s = Summary();
        var ids = s.Find("screen-items")!.Items.Select(i => i.Id).ToList();
        Assert.Equal("W1", ids[0]);
        Assert.Equal(["T1", "T2"], ids.Where(i => i.StartsWith('T')).ToList());
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(CanonicalJson.Serialize(s.ToJson()), CanonicalJson.Serialize(Summary().ToJson()));
    }
}

using System.Diagnostics;
using System.Text.Json.Nodes;
using Stangent.Graph;
using Stangent.Graph.Detectors;
using Stangent.Graph.Impact;

namespace Stangent.Core.Tests;

/// <summary>A fresh copy of <c>tests/fixtures/shop</c> committed to its own git repo, with its graph built once.</summary>
public sealed class ShopGraph : IAsyncLifetime
{
    public string Root { get; } = ShopCopy.Create();
    public StangentRepo Repo { get; private set; } = null!;
    public CodeGraph Graph { get; private set; } = null!;
    public BuildStats Stats { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Repo = StangentRepo.Discover(Root);
        (Graph, Stats) = await GraphBuilder.BuildAsync(Repo, new BuildOptions(), TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(Root, recursive: true);
        return ValueTask.CompletedTask;
    }
}

static class ShopCopy
{
    public static string Create()
    {
        var source = Path.Combine(RepoRoot.Find(), "tests", "fixtures", "shop");
        var root = Directory.CreateTempSubdirectory("stangent-shop-").FullName;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            if (rel.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, rel))!);
            File.Copy(file, Path.Combine(root, rel));
        }
        Git(root, "init -q");
        Git(root, "add -A");
        Git(root, "-c user.name=test -c user.email=test@example.invalid commit -q -m fixture");
        return root;
    }

    public static void Git(string root, string args)
    {
        using var p = Process.Start(new ProcessStartInfo("git", args) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {args}: {p.StandardError.ReadToEnd()}");
    }
}

public class GraphTests(ShopGraph shop) : IClassFixture<ShopGraph>
{
    const string PlaceOrder = "Shop.Core|M:Shop.Core.OrderService.PlaceOrder(System.String,System.Decimal)";
    const string IPlaceOrder = "Shop.Core|M:Shop.Core.IOrderService.PlaceOrder(System.String,System.Decimal)";

    IEnumerable<Edge> Edges(string kind) => shop.Graph.AllEdges.Where(e => e.Kind == kind);
    Node? NodeById(string id) => shop.Graph.AllNodes.FirstOrDefault(n => n.Id == id);

    [Fact]
    public void Every_project_loads_and_is_on_the_map()
    {
        Assert.Empty(shop.Stats.LoadErrors);
        Assert.Equal(4, shop.Stats.Projects);
        Assert.Equal(3, shop.Stats.Frontends);   // Angular, React, and Shop.Web's plain scripts (N5)
        Assert.Equal(7, shop.Graph.AllNodes.Count(n => n.Kind == NodeKind.Project)); // 4 C# + 3 frontends
    }

    [Fact]
    public void The_compiler_part_has_methods_calls_and_dispatch_through_interfaces()
    {
        Assert.Equal(NodeKind.Method, NodeById(PlaceOrder)?.Kind);
        Assert.Contains(Edges(EdgeKind.Calls), e => e.Source.EndsWith("Endpoints.CreateOrder(Shop.Core.IOrderService,Microsoft.Extensions.Configuration.IConfiguration)", StringComparison.Ordinal)
            && e.Target == IPlaceOrder && e.Label == Label.Certain);
        Assert.Contains(Edges(EdgeKind.DispatchesTo), e => e.Source == IPlaceOrder && e.Target == PlaceOrder);
        Assert.True(NodeById("Shop.Tests|M:Shop.Tests.OrderTests.Placing_an_order_keeps_the_customer")?.IsTest);
    }

    [Fact]
    public void Calls_from_top_level_statements_belong_to_the_generated_entry_point()
    {
        // Shop.Web/Program.cs uses top-level statements; its calls must still be on the map.
        Assert.Contains(shop.Graph.AllNodes, n => n.Id == "Shop.Web|M:Program.<Main>$(System.String[])" && n.File == "Shop.Web/Program.cs");
        Assert.Contains(Edges(EdgeKind.Uses), e => e.Source == "Shop.Web|M:Program.<Main>$(System.String[])" && e.Target == "Shop.Core|T:Shop.Core.PaymentOptions");
    }

    [Fact]
    public void Verbs_and_templates_come_from_an_apps_own_verb_attributes()
    {
        var endpoints = shop.Graph.AllNodes.Where(n => n.Kind == NodeKind.Endpoint && n.Name.Contains("/api/shelf")).Select(n => n.Name).Order().ToList();
        // : HttpPostAttribute; : HttpDeleteAttribute with base("{id:int}"); IActionHttpMethodProvider + a Template property.
        Assert.Equal(["DELETE /api/shelf/{id:int}", "GET /api/shelf/{id:int}", "POST /api/shelf", "PUT /api/shelf/{id:int?}"], endpoints);
        Assert.Contains(shop.Graph.AllNodes, n => n.Kind == NodeKind.Endpoint && n.Name == "GET /feed/v2/calendar/shop.ics");
        // F1: the override answers the PUT; the base method it overrides isn't routed, nor is a [NonAction] override's base.
        Assert.Equal(["Shop.Web|M:Shop.Web.Controllers.ShelfController.Save(System.Int32)"],
            Edges(EdgeKind.RoutesTo).Where(e => e.Source == "endpoint:Shop.Web|PUT /api/shelf/{id:int?}").Select(e => e.Target).ToList());
        // F4: a call that leaves off the optional id still reaches it.
        Assert.Contains(Edges(EdgeKind.CallsHttp), e => e.Source == React + "ShelfPage.tsx#ShelfPage.save" && e.Target == "endpoint:Shop.Web|PUT /api/shelf/{id:int?}");
        Assert.DoesNotContain(Edges(EdgeKind.RoutesTo), e => e.Target.Contains(".Find(", StringComparison.Ordinal));
        Assert.Equal(["Shop.Web|M:Shop.Web.Controllers.ShelfController.Get(System.Int32)"],
            Edges(EdgeKind.RoutesTo).Where(e => e.Source == "endpoint:Shop.Web|GET /api/shelf/{id:int}").Select(e => e.Target).ToList());
        Assert.Contains(Edges(EdgeKind.RoutesTo), e => e.Source == "endpoint:Shop.Web|POST /api/shelf"
            && e.Target == "Shop.Web|M:Shop.Web.Controllers.ShelfController.Add" && e.Label == Label.Certain);
    }

    [Fact]
    public void Outside_calls_through_other_clients_are_facts_or_blind_spots()
    {
        const string proxy = "Shop.Web|M:Shop.Web.RatesProxy.";
        var spots = shop.Graph.AllBlindSpots.Where(b => b.Owner.StartsWith(proxy, StringComparison.Ordinal)).Select(b => (b.Owner[proxy.Length..], b.Reason)).ToList();
        Assert.Contains(spots, b => b.Item1.StartsWith("Rates(", StringComparison.Ordinal) && b.Reason == "outside HTTP GET through IHttpClient: URL built at runtime");
        Assert.Contains(spots, b => b.Item1.StartsWith("Report(", StringComparison.Ordinal) && b.Reason == "outside HTTP POST through Flurl to https://audit.example…: rest of the URL built at runtime");
        // A whole literal URL is a call; its host is no service here, so linking says so. F6: headers don't change the URL.
        Assert.Contains(spots, b => b.Item1 == "Fixed" && b.Reason.Contains("rates.example", StringComparison.Ordinal));
        Assert.Contains(spots, b => b.Item1 == "Ping" && b.Reason.Contains("audit.example", StringComparison.Ordinal) && !b.Reason.Contains("runtime", StringComparison.Ordinal));
    }

    [Fact]
    public void Saves_and_reads_reach_their_tables()
    {
        Assert.Contains(Edges(EdgeKind.Maps), e => e.Source == "table:Shop.Core|ShelfItem" && e.Target == "Shop.Core|T:Shop.Core.ShelfItem" && e.Label == Label.Guessed);
        Assert.Contains(Edges(EdgeKind.Maps), e => e.Source == "table:Shop.Core|Ledgers" && e.Target == "Shop.Core|T:Shop.Core.Ledger");
        string Lines(string kind, string method) => string.Join(",", Edges(kind).Where(e => e.Source.StartsWith("Shop.Core|M:Shop.Core." + method, StringComparison.Ordinal)).Select(e => e.Target).Order());
        Assert.Equal("table:Shop.Core|Shelves", Lines(EdgeKind.Writes, "ShelfService.Open"));
        Assert.Equal("table:Shop.Core|ShelfItem", Lines(EdgeKind.Writes, "ShelfService.Stock"));
        Assert.Equal("table:Shop.Core|Shelves", Lines(EdgeKind.Reads, "ShelfService.Count"));
        Assert.Equal("", Lines(EdgeKind.Writes, "ShelfService.Count"));
        Assert.Equal("table:Shop.Core|Ledgers", Lines(EdgeKind.Writes, "LedgerService.Record"));
        Assert.Equal("table:Shop.Core|Ledgers", Lines(EdgeKind.Reads, "LedgerService.Find"));
        // F2: Entry(x).State =, context.Update(x) and ExecuteDelete are writes; AsTracking is a read.
        Assert.Equal("table:Shop.Core|Shelves", Lines(EdgeKind.Writes, "ShelfService.Edit"));
        Assert.Equal("table:Shop.Core|Shelves", Lines(EdgeKind.Writes, "ShelfService.Replace"));
        Assert.Equal("table:Shop.Core|Shelves", Lines(EdgeKind.Writes, "ShelfService.Empty"));
        Assert.Equal("table:Shop.Core|Shelves", Lines(EdgeKind.Reads, "ShelfService.Tracked"));
        Assert.Equal("table:Shop.Core|Shelves", Lines(EdgeKind.Reads, "ShelfService.Load"));   // R1
        // R2: a many-to-many add writes the join table the snapshot says links the two.
        Assert.Contains(Edges(EdgeKind.Writes), e => e.Source == "Shop.Web|M:Shop.Web.Data.CouponService.Tag(Shop.Web.Data.Coupon)" && e.Target == "table:Shop.Web|CouponCouponTag");
        // R3: a snapshot in another project names its context's tables, under the context's assembly; its navigation guesses go.
        Assert.Contains(Edges(EdgeKind.Maps), e => e.Source == "table:Shop.Core|Archives" && e.Label == Label.Certain);
        Assert.DoesNotContain(shop.Graph.AllNodes, n => n.Kind == NodeKind.Table && n.Name == "ArchiveNote");
        Assert.Contains(shop.Graph.AllNodes, n => n.Id == "table:Shop.Core|ShelfItem");   // another context's guess stays
        // R4: one set of column facts per entity.
        Assert.Single(shop.Graph.AllFacts, f => f.Kind == "column" && f.Owner == "Shop.Web|P:Shop.Web.Data.Coupon.Id");
        // F3: a list on an object that isn't an entity; F10: Allowed isn't All.
        Assert.Equal("", Lines(EdgeKind.Writes, "ShelfService.Plan"));
        Assert.Equal("", Lines(EdgeKind.Reads, "LedgerService.May"));
        // F5: a property configured as a column isn't a navigation.
        Assert.DoesNotContain(shop.Graph.AllNodes, n => n.Id == "table:Shop.Core|Label");
        // D164: the snapshot names the table; an owned type and a navigation it doesn't hold aren't tables; a join table is one.
        Assert.Contains(Edges(EdgeKind.Maps), e => e.Source == "table:Shop.Web|CouponCodes" && e.Target == "Shop.Web|T:Shop.Web.Data.Coupon" && e.Label == Label.Certain);
        Assert.DoesNotContain(shop.Graph.AllNodes, n => n.Kind == NodeKind.Table && (n.Name is "Coupons" or "NotATable" or "CouponNote"));
        Assert.Contains(shop.Graph.AllNodes, n => n.Id == "table:Shop.Web|CouponShelf");
    }

    [Fact]
    public void Authorisation_markers_are_facts_on_actions()
    {
        string Auth(string method) => string.Join(";", shop.Graph.AllFacts.Where(f => f.Kind == "auth" && f.Owner == "Shop.Web|M:Shop.Web.Controllers.ShelfController." + method)
            .Select(f => $"{f["rule"]} {f["policy"]} {f["from"]}"));
        Assert.Equal("authorize Staff class StaffController", Auth("Add"));   // inherited from a base controller
        Assert.Equal("anonymous  method", Auth("Get(System.Int32)"));        // the method's own [AllowAnonymous] wins

        // N3: an app's own access filters, with their arguments' values; other filters aren't access rules.
        string Filters(string method) => string.Join("; ", shop.Graph.AllFacts
            .Where(f => f.Kind == "auth" && f.Owner == "Shop.Web|M:Shop.Web.Controllers.AccountController." + method)
            .Select(f => f["rule"] == "filter" ? $"{f["filter"]}({f["args"]}) {f["stage"]} {f["from"]}" : $"{f["rule"]} {f["from"]}").Order(StringComparer.Ordinal));
        Assert.Equal("CheckAccessStore(ignore = false) other class AccountController", Filters("Register"));
        Assert.Equal("CheckAccessStore(ignore = false) other class AccountController", Filters("Register(Shop.Web.Models.RegisterModel)"));
        // [AllowAnonymous] doesn't stop a filter: only the filter's own code could.
        // T5, T6: the evidence for each: code that refuses, a name, or (🟡) only the stage it runs at or a result of its own. SaveLastPage is none.
        // V4: OwnedOnly refuses with "not found", in its base class: a refusal at the authorisation stage.
        Assert.Equal("CheckAccessStore name; HttpsOnly stage; Maintenance result; OwnedOnly denies; ValidateIp denies", string.Join("; ", shop.Graph.AllFacts
            .Where(f => f.Kind == "auth" && f.Owner == "Shop.Web|M:Shop.Web.Controllers.AccountController.Settings" && f["rule"] == "filter")
            .Select(f => $"{f["filter"]} {f["basis"]}").Order(StringComparer.Ordinal)));
        // T5: a global filter holds for every action: on the project, from "global".
        Assert.Contains(shop.Graph.AllFacts, f => f.Kind == "auth" && f.Owner == "project:Shop.Web/Shop.Web.csproj" && f["from"] == "global" && f["filter"] == "ValidateIp" && f["basis"] == "denies");
        Assert.Equal("CheckAccessStore(ignore = false) other class AccountController; CheckPermission(permission = \"Accounts.Manage\", deny = Json) "
            + "authorization method; SignedInFilter(type = typeof(SignedInFilter)) authorization method; anonymous method",
            Filters("Close"));
    }

    [Fact]
    public void Validation_rules_are_facts_on_the_properties_they_check()
    {
        string Rules(string prop) => string.Join("; ", shop.Graph.AllFacts.Where(f => f.Kind == "rule" && f.Owner == "Shop.Core|P:Shop.Core.NewOrder." + prop)
            .Select(f => $"{f["rule"]}({f["args"]}) {f["via"]} '{f["message"]}' {f["conditional"]}".TrimEnd()));
        Assert.Equal("Required() attribute ''; StringLength(40, MinimumLength = 2) attribute ''; "
            + "NotEmpty() fluentvalidation 'Texts.Get(\"Order.Customer.Required\")'; Within(40) fluentvalidation ''", Rules("Customer"));   // N4: WithMessageAwait is a message; T12: Within is a rule
        // N4: an enum argument by member name.
        Assert.Equal("DataType(EmailAddress)", string.Join("; ", shop.Graph.AllFacts
            .Where(f => f.Kind == "rule" && f.Owner == "Shop.Web|P:Shop.Web.Models.RegisterModel.Email").Select(f => $"{f["rule"]}({f["args"]})")));
        Assert.Equal("GreaterThan(0) fluentvalidation 'Order at least one' yes; NotEmpty() fluentvalidation ''", Rules("Quantity"));
        // V5: other modifiers stay on the rule they follow, by name; WithSeverity's value is the rule's severity.
        Assert.Equal("NotEmpty AsWarning ; GreaterThan WithSeverity Info", string.Join("; ", shop.Graph.AllFacts
            .Where(f => f.Kind == "rule" && f.Owner == "Shop.Core|P:Shop.Core.NewOrder.Note").Select(f => $"{f["rule"]} {f["modifiers"]} {f["severity"]}")));
        // F9: each rule carries its own line.
        Assert.Equal(2, shop.Graph.AllFacts.Where(f => f.Kind == "rule" && f.Owner == "Shop.Core|P:Shop.Core.NewOrder.Quantity").Select(f => f.Line).Distinct().Count());
    }

    [Fact]
    public void Razor_views_are_points_with_their_forms_buttons_and_what_they_render()
    {
        const string view = "view:Shop.Web/Views/Account/Register.cshtml";
        const string register = "endpoint:Shop.Web|POST /{lang}/register";
        const string account = "Shop.Web|M:Shop.Web.Controllers.AccountController.";
        string View(string path) => "view:Shop.Web/Views/" + path;
        bool Has(string kind, string source, string target, Label? label = null) =>
            Edges(kind).Any(e => e.Source == source && e.Target == target && (label is null || e.Label == label));

        // N1: a point per view, its @model, what it renders.
        Assert.Equal(NodeKind.View, NodeById(view)?.Kind);
        Assert.Equal("Shop.Web/Views/Account/Register.cshtml", NodeById(view)?.File);
        Assert.True(Has(EdgeKind.Uses, view, "Shop.Web|T:Shop.Web.Models.RegisterModel"));
        Assert.True(Has(EdgeKind.Uses, view, "Shop.Web|P:Shop.Web.Models.RegisterModel.Email"));   // asp-for
        Assert.True(Has(EdgeKind.Renders, view, View("Account/_Consent.cshtml")));                // <partial>, beside the view
        Assert.True(Has(EdgeKind.Renders, view, "Shop.Web|T:Shop.Web.Components.NewsletterViewComponent"));
        Assert.True(Has(EdgeKind.Renders, "Shop.Web|M:Shop.Web.Components.NewsletterViewComponent.Invoke", View("Shared/Components/Newsletter/Default.cshtml")));

        // Forms reach their endpoints: by route name through a constant, and by Html.BeginForm's action and controller.
        Assert.True(Has(EdgeKind.CallsHttp, view + "#form1", register, Label.Certain));
        Assert.True(Has(EdgeKind.CallsHttp, view + "#form3", register, Label.Certain));
        Assert.Equal("POST", shop.Graph.AllFacts.Single(f => f.Kind == "form" && f.Owner == view + "#form1")["method"]);
        // Close has no route, so its form reaches nothing: said, not silent.
        Assert.Contains(shop.Graph.AllBlindSpots, b => b.Owner == view + "#form2" && b.Reason.Contains("matched no endpoint"));

        // Buttons: which form they submit, their text keys, their line.
        var events = shop.Graph.AllFacts.Where(f => f.Kind == "ui-event" && f.Owner == view)
            .Select(f => $"{f.Line} {f["element"]} {f["event"]} {f["handler"].Replace(view, "")} [{f["keys"]}] '{f["text"]}' {f["id"]}").ToList();
        Assert.Equal([
            "10 button submit #form1 [Account.Register.Button] '' register-button",
            "14 input submit #form2 [] 'Close account' ",
            "18 button submit #form3 [] 'Register again' ",
            "20 button click  [Account.CheckEmail] '' check-email",
        ], events);

        // N2: View(…) reaches its view by MVC's lookup rules: the action's name, a name given, a partial in Shared.
        Assert.True(Has(EdgeKind.Renders, account + "Register", view));
        Assert.True(Has(EdgeKind.Renders, account + "Register(Shop.Web.Models.RegisterModel)", view));
        Assert.True(Has(EdgeKind.Renders, account + "Welcome", View("Account/Done.cshtml")));
        Assert.True(Has(EdgeKind.Renders, account + "Close", View("Shared/_Closed.cshtml")));

        // An area: [Area] from a base controller, ASP.NET's {area:exists} route, the form's default controller, a partial beside its view.
        const string admin = "view:Shop.Web/Areas/Admin/Views/StockAdmin/Clear.cshtml";
        Assert.True(Has(EdgeKind.CallsHttp, admin + "#form1", "endpoint:Shop.Web|POST /Admin/StockAdmin/Clear/{id?}"));
        Assert.True(Has(EdgeKind.Renders, "Shop.Web|M:Shop.Web.Areas.Admin.Controllers.StockAdminController.Clear", admin));
        Assert.True(Has(EdgeKind.Renders, admin, "view:Shop.Web/Areas/Admin/Views/StockAdmin/_Row.cshtml"));

        // N5: the app's own plain script: its jQuery call reaches an endpoint, and the button it binds by id calls it. Libraries and
        // minified files aren't read. The view's inline script belongs to the view.
        const string script = "ts|Shop.Web/wwwroot/js/shop.cart.js#checkEmail";
        Assert.True(Has(EdgeKind.Calls, view, script, Label.Guessed));
        Assert.True(Has(EdgeKind.CallsHttp, script, "endpoint:Shop.Web|POST /api/orders"));
        Assert.True(Has(EdgeKind.CallsHttp, view, "endpoint:Shop.Web|POST /api/legacy/place"));
        Assert.DoesNotContain(shop.Graph.AllNodes, n => n.File is { } f && (f.Contains("wwwroot/lib/") || f.EndsWith(".min.js", StringComparison.Ordinal)));
        Assert.Contains(shop.Graph.AllFacts, f => f.Kind == "dom-event" && f.Owner == view && f["selector"] == "#register-button" && f.Line == 22);
        // A URL the server writes into the script ('@Url.Action("Close")') is made at runtime: ❓, never a made-up path, even after
        // a comment holding a quote.
        Assert.Contains(shop.Graph.AllBlindSpots, b => b.Owner == view && b.Line == 25 && b.Reason == "HTTP POST to a URL built at runtime");

        // A view the compiler made nothing for is a blind spot.
        Assert.Contains(shop.Graph.AllBlindSpots, b => b.File == "Shop.Web/Views/Legacy/Old.cshtml" && b.Reason.StartsWith("view not read", StringComparison.Ordinal));
    }

    [Fact]
    public void Razor_forms_follow_the_form_tag_helper_and_each_buttons_own_target()
    {
        const string view = "view:Shop.Web/Views/Account/Settings.cshtml";
        const string ep = "endpoint:Shop.Web|";
        bool Has(string kind, string source, string target, Label? label = null) =>
            Edges(kind).Any(e => e.Source == source && e.Target == target && (label is null || e.Label == label));
        IEnumerable<string> Targets(string source) => Edges(EdgeKind.CallsHttp).Where(e => e.Source == source).Select(e => e.Target);
        string Handler(string text) => shop.Graph.AllFacts.Single(f => f.Kind == "ui-event" && f.Owner == view && f["text"] == text)["handler"];

        // T1: the form tag helper writes method="post" when it builds the form; a plain <form action> stays GET.
        Assert.Equal([ep + "POST /account/settings"], Targets(view + "#form1"));
        Assert.Equal([ep + "GET /account/settings"], Targets(view + "#form2"));
        // T3: formaction="@Url.Action(…)" with formmethod gives the button its own target; a server value that isn't one is ❓.
        Assert.Equal([ep + "GET /account/settings"], Targets(Handler("Preview")));
        Assert.Contains(shop.Graph.AllBlindSpots, b => b.Owner == Handler("Elsewhere") && b.Reason == "form target built at runtime");
        // T10: the endpoint with the form's verb wins over ANY at the same path.
        Assert.Equal([ep + "POST /account/notes"], Targets(view + "#form3"));
        // T9: a partial's submit button submits the form around the <partial>; with no form around it, ❓.
        Assert.True(Has(EdgeKind.Calls, "view:Shop.Web/Views/Account/_SaveButtons.cshtml", view + "#form1", Label.Guessed));
        Assert.Contains(shop.Graph.AllBlindSpots, b => b.Owner == "view:Shop.Web/Views/Account/_Orphan.cshtml" && b.Reason.StartsWith("submit button outside", StringComparison.Ordinal));
        // …and so does one in a partial of that partial.
        Assert.True(Has(EdgeKind.Calls, "view:Shop.Web/Views/Account/_DeleteButton.cshtml", view + "#form1", Label.Guessed));
        // T4: a component calling its base class's helper renders its own view: its default, or the name it passes.
        const string comps = "Shop.Web|M:Shop.Web.Components.";
        Assert.True(Has(EdgeKind.Renders, comps + "CartViewComponent.InvokeAsync", "view:Shop.Web/Views/Shared/Components/Cart/Default.cshtml", Label.Guessed));
        Assert.True(Has(EdgeKind.Renders, comps + "BadgeViewComponent.InvokeAsync", "view:Shop.Web/Views/Shared/Components/Badge/Small.cshtml", Label.Guessed));
        // T8: with an app's own view location rule, the theme's view of the same path is a candidate too, and neither is ✅.
        const string welcome = "Shop.Web|M:Shop.Web.Controllers.AccountController.Welcome";
        Assert.True(Has(EdgeKind.Renders, welcome, "view:Shop.Web/Views/Account/Done.cshtml", Label.Guessed));
        Assert.True(Has(EdgeKind.Renders, welcome, "view:Shop.Web/Themes/Dark/Views/Account/Done.cshtml", Label.Guessed));
        // T11: a partial named with a folder and no extension.
        Assert.True(Has(EdgeKind.Renders, view, "view:Shop.Web/Views/Shared/_Closed.cshtml"));

        // V1: a component's view in the page's controller folder; a partial of a shared component view, there too, with its form.
        Assert.True(Has(EdgeKind.Renders, comps + "TipsViewComponent.Invoke", "view:Shop.Web/Views/Account/Components/Tips/Default.cshtml", Label.Guessed));
        const string summary = "view:Shop.Web/Views/Shared/Components/Summary/Default.cshtml";
        Assert.True(Has(EdgeKind.Renders, summary, "view:Shop.Web/Views/Account/_Gift.cshtml", Label.Guessed));
        Assert.True(Has(EdgeKind.Calls, "view:Shop.Web/Views/Account/_Gift.cshtml", summary + "#form1", Label.Guessed));
        // V2: buttons that cancel their own submit don't submit the form around their partial; a popup opener reaches the page it opens (🟡).
        const string popup = "view:Shop.Web/Views/Account/_Popup.cshtml";
        Assert.False(Has(EdgeKind.Calls, popup, view + "#form1"));
        var opener = shop.Graph.AllFacts.Single(f => f.Kind == "ui-event" && f.Owner == popup && f["text"] == "Add note");
        Assert.Equal("click", opener["event"]);
        Assert.True(Has(EdgeKind.CallsHttp, opener["handler"], ep + "ANY /account/notes", Label.Guessed));
        Assert.DoesNotContain(shop.Graph.AllBlindSpots, b => b.Owner == popup);
        // A button written in a script's text isn't the page's markup.
        Assert.DoesNotContain(shop.Graph.AllFacts, f => f.Kind == "ui-event" && f.Owner == popup && f["text"] == "Edit");
        // W1: …but a submit button a script writes submits the form around it (🟡, built: script); one that returns false doesn't.
        const string grid = "view:Shop.Web/Views/Account/_Grid.cshtml";
        Assert.Equal("Restore script", string.Join("; ", shop.Graph.AllFacts.Where(f => f.Kind == "ui-event" && f.Owner == grid).Select(f => $"{f["text"]} {f["built"]}")));
        Assert.True(Has(EdgeKind.Calls, grid, view + "#form1", Label.Guessed));
        // …by GET first; with none, by whatever verb the action serves (its script may post it).
        var archive = shop.Graph.AllFacts.Single(f => f.Kind == "ui-event" && f.Owner == popup && f["text"] == "Archive selected")["handler"];
        Assert.True(Has(EdgeKind.CallsHttp, archive, ep + "POST /account/archive", Label.Guessed));
        // V3: a button's own target with no form in its file takes the method of the form around its partial (🟡); none: ❓.
        var upload = shop.Graph.AllFacts.Single(f => f.Kind == "ui-event" && f.Owner == "view:Shop.Web/Views/Account/_Upload.cshtml")["handler"];
        Assert.Equal([ep + "POST /account/notes"], Targets(upload));
        Assert.True(Has(EdgeKind.CallsHttp, upload, ep + "POST /account/notes", Label.Guessed));
        Assert.Contains(shop.Graph.AllBlindSpots, b => b.File == "Shop.Web/Views/Account/_Orphan.cshtml" && b.Reason.StartsWith("button with its own target outside any form", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_build_cross_checks_endpoints_with_openapi_and_tables_with_the_snapshot()
    {
        Assert.NotNull(shop.Stats.CrossCheck);
        var report = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(CrossCheck.PathFor(shop.Root)))!;
        var api = report["openapi"]!.AsArray().Single(a => (string?)a!["file"] == "Shop.Web/openapi.json")!;
        // R5: someone else's API (a client's spec) is set aside, not counted.
        Assert.Contains("not this repo's API", (string?)report["openapi"]!.AsArray().Single(a => (string?)a!["file"] == "vendor/payments-openapi.json")!["ignored"]);
        Assert.Equal("project:Shop.Web/Shop.Web.csproj", (string?)api["scope"]);
        // GET /api/stock/{id} is Shop.Stock's: served elsewhere in the repo, so not missing.
        Assert.Equal(["GET /api/gone"], api["missingFromGraph"]!.AsArray().Select(x => (string)x!).ToList());
        var notInOpenapi = api["notInOpenapi"]!.AsArray().Select(x => (string)x!).ToList();
        Assert.Contains("POST /api/shelf", notInOpenapi);
        Assert.DoesNotContain("POST /api/orders", notInOpenapi);
        Assert.DoesNotContain("GET /api/shelf/{id:int}", notInOpenapi);   // {id:int} and {id} are the same shape
        var ef = report["efSnapshot"]!.AsArray().Single(e => (string?)e!["context"] == "Shop.Web|T:Shop.Web.Data.WebDb")!;
        Assert.Contains(report["efSnapshot"]!.AsArray(), e => (string?)e!["context"] == "Shop.Core|T:Shop.Core.ArchiveDb" && (int?)e["tables"] == 1);
        Assert.Equal(["Shop.Web|T:Shop.Web.Data.CouponNote"], ef["foundByRulesOnly"]!.AsArray().Select(x => (string)x!).ToList());
        Assert.Empty(ef["inSnapshotOnly"]!.AsArray());
        Assert.Equal("/content/{}", CrossCheck.Normalize("/content/{**path:regex(^(?!api/).*)}"));
    }

    [Fact]
    public void Constants_messages_and_background_jobs_are_read()
    {
        string Value(string field) => shop.Graph.AllFacts.Single(f => f.Kind == "value" && f.Owner == "Shop.Web|F:Shop.Web.Messages." + field)["value"];
        Assert.Equal(("ShelfChanged", "Restocked", "12"), (Value("ShelfChanged"), Value("Restocked"), Value("MaxShelves")));   // G9
        // G10: sent through a wrapper behind an interface, sent directly, and listened for by the screen.
        const string message = "message:signalr|ShelfChanged";
        Assert.Contains(Edges(EdgeKind.Sends), e => e.Source == "Shop.Web|M:Shop.Web.ShelfNotifier.Changed" && e.Target == message);
        Assert.Contains(Edges(EdgeKind.Sends), e => e.Source == "Shop.Web|M:Shop.Web.EventHub.Ping" && e.Target == "message:signalr|ping");
        Assert.Contains(Edges(EdgeKind.Listens), e => e.Source == React + "ShelfLive.tsx#ShelfLive.listen" && e.Target == message);
        Assert.Equal(NodeKind.Message, NodeById(message)?.Kind);
        // G11: queued work says what it runs and when.
        var jobs = shop.Graph.AllFacts.Where(f => f.Kind == "job" && f.Owner.StartsWith("Shop.Web|M:Shop.Web.ShelfNotifier.", StringComparison.Ordinal))
            .Select(f => $"{f.Owner[(f.Owner.LastIndexOf('.') + 1)..]} {f["runs"][(f["runs"].LastIndexOf('.') + 1)..]} {f["id"]} {f["schedule"]}".Trim()).Order().ToList();
        Assert.Equal(["Later Recount  now, in the background", "Nightly Recount recount 0 3 * * *"], jobs);
    }

    [Fact]
    public void Routes_are_found_for_minimal_apis_attributes_and_conventional_routes()
    {
        var endpoints = shop.Graph.AllNodes.Where(n => n.Kind == NodeKind.Endpoint).Select(n => n.Name).ToList();
        Assert.Contains("POST /api/orders", endpoints);           // MapGroup("api/orders").MapPost("/")
        Assert.Contains("POST /api/legacy/place", endpoints);     // [Route] + [HttpPost("place")]
        Assert.Contains("ANY /checkout", endpoints);              // MapControllerRoute defaults → CheckoutController.Index
        Assert.Contains("GET /api/stock/{id:int}", endpoints);
        Assert.Contains(Edges(EdgeKind.RoutesTo), e => e.Source == "endpoint:Shop.Web|ANY /checkout"
            && e.Target == "Shop.Web|M:Shop.Web.Controllers.CheckoutController.Index" && e.Label == Label.Guessed);
    }

    [Fact]
    public void Di_events_tables_config_and_http_links_are_found()
    {
        Assert.Contains(Edges(EdgeKind.WiredTo), e => e.Source == "Shop.Core|T:Shop.Core.IOrderService" && e.Target == "Shop.Core|T:Shop.Core.OrderService");
        Assert.Contains(Edges(EdgeKind.TriggeredBy), e => e.Source == "Shop.Core|M:Shop.Core.SendReceiptOnOrderPlaced.HandleAsync(Shop.Core.OrderPlaced)"
            && e.Target == PlaceOrder);
        Assert.Contains(Edges(EdgeKind.Maps), e => e.Source == "table:Shop.Core|Order" && e.Target == "Shop.Core|T:Shop.Core.Order");
        Assert.Contains(Edges(EdgeKind.Maps), e => e.Source == "column:Shop.Core|Order.Total" && e.Target == "Shop.Core|P:Shop.Core.Order.Total");
        Assert.Contains(Edges(EdgeKind.ReadsConfig), e => e.Target == "config:Shop.Web|Shop:Region");
        Assert.Contains(Edges(EdgeKind.Binds), e => e.Source == "Shop.Core|P:Shop.Core.PaymentOptions.ApiKey" && e.Target == "config:Shop.Web|Payment:ApiKey");
        Assert.Equal("Shop.Web/appsettings.json", NodeById("config:Shop.Web|Payment:ApiKey")?.File);
        Assert.Contains(Edges(EdgeKind.CallsHttp), e => e.Source == "Shop.Web|M:Shop.Web.StockClient.GetStock(System.Int32)"
            && e.Target == "endpoint:Shop.Stock|GET /api/stock/{id:int}");
        Assert.DoesNotContain(Edges(EdgeKind.CallsHttp), e => e.Target == "endpoint:Shop.Stock|GET /api/stock/summary");
    }

    [Fact]
    public void Blind_spots_are_recorded_where_links_cannot_be_followed()
    {
        var spots = shop.Graph.AllBlindSpots.Select(b => b.Reason).ToList();
        Assert.Contains("reflection: Activator.CreateInstance", spots);
        Assert.Contains(spots, r => r.Contains("'Shop:NotInAppSettings' is in no appsettings file", StringComparison.Ordinal));
    }

    [Fact]
    public void Impact_of_a_method_stops_at_endpoints_handlers_and_tests_and_matches_its_contract()
    {
        var report = new ImpactAnalyzer(shop.Graph).Analyze(new ChangeSpec("symbol", "OrderService.PlaceOrder"), shop.Root);

        Assert.Empty(SchemaRegistry.Embedded.Validate("impact-report.v5.json", report.ToJsonString()));
        var groups = report["groups"]!.AsArray().ToDictionary(g => (string)g!["name"]!, g => g!);
        var endpoints = Names(groups["endpoints"]);
        Assert.Contains("POST /api/orders", endpoints);
        Assert.Contains("POST /api/legacy/place", endpoints);
        Assert.Contains("ANY /checkout", endpoints);
        Assert.DoesNotContain(endpoints, e => e.Contains("stock", StringComparison.Ordinal));
        Assert.Equal(2, (int)groups["endpoints"]["certain"]!);  // the minimal API and the attribute route
        // D206: a check an attribute registers for OrderPlaced handles it too.
        Assert.Equal(["SendReceiptOnOrderPlaced.HandleAsync", "StockLevelCheck.Check"], Names(groups["eventHandlers"]));
        // The C# test, and the Angular spec that reaches PlaceOrder through POST /api/orders.
        Assert.Equal(["OrderTests.Placing_an_order_keeps_the_customer", "OrderService › places an order"], Names(groups["tests"]));

        var md = ImpactAnalyzer.Markdown(report);
        Assert.Contains("✅", md);
        Assert.Contains("🟡 `ANY /checkout`", md);
    }

    [Fact]
    public void Impact_lists_the_nearest_first_and_can_stop_at_a_depth()
    {
        var analyzer = new ImpactAnalyzer(shop.Graph);
        var report = analyzer.Analyze(new ChangeSpec("symbol", PlaceOrder), shop.Root);
        var code = report["groups"]!.AsArray().First(g => (string)g!["name"]! == "code")!;
        var depths = code["items"]!.AsArray().Select(i => (int)i!["depth"]!).ToList();
        Assert.Equal(depths.Order(), depths);
        Assert.Equal(1, depths[0]);   // IOrderService.PlaceOrder dispatches straight to it
        var bands = code["bands"]!;
        Assert.Equal((int)code["total"]!, (int)bands["direct"]! + (int)bands["near"]! + (int)bands["far"]!);

        // POST /api/orders is 3 lines away (endpoint → CreateOrder → IOrderService → PlaceOrder).
        var direct = analyzer.Analyze(new ChangeSpec("symbol", PlaceOrder), shop.Root, maxDepth: 1);
        Assert.Empty(SchemaRegistry.Embedded.Validate("impact-report.v5.json", direct.ToJsonString()));
        Assert.Equal(0, (int)direct["groups"]![0]!["total"]!);
        Assert.Equal(1, (int)direct["maxDepth"]!);
        Assert.Contains("1 direct", ImpactAnalyzer.Markdown(report));
    }

    [Fact]
    public void Impact_follows_event_chains_but_not_the_dispatcher_behind_a_handler()
    {
        var analyzer = new ImpactAnalyzer(shop.Graph);
        var report = analyzer.Analyze(new ChangeSpec("symbol", PlaceOrder), shop.Root);
        // The handler is affected, but nothing that merely calls handlers in general is.
        Assert.Equal(2, (int)report["groups"]!.AsArray().First(g => (string)g!["name"]! == "eventHandlers")!["total"]!);
        Assert.DoesNotContain("PluginLoader.Load", Names(report["groups"]!.AsArray().First(g => (string)g!["name"]! == "code")!));
    }

    [Fact]
    public void Impact_reaches_another_service_through_its_http_endpoint()
    {
        var report = new ImpactAnalyzer(shop.Graph).Analyze(new ChangeSpec("symbol", "endpoint:Shop.Stock|GET /api/stock/{id:int}"), shop.Root);
        var code = Names(report["groups"]!.AsArray().First(g => (string)g!["name"]! == "code")!);
        Assert.Contains("StockClient.GetStock", code);
    }

    [Fact]
    public void A_changed_config_key_affects_the_settings_class_bound_to_it()
    {
        var analyzer = new ImpactAnalyzer(shop.Graph);
        var line = NodeById("config:Shop.Web|Payment:ApiKey")!.Line;
        Assert.Contains(analyzer.AtLines("Shop.Web/appsettings.json", line, line), n => n.Id == "config:Shop.Web|Payment:ApiKey");

        var report = analyzer.Analyze(new ChangeSpec("symbol", "config:Shop.Web|Payment:ApiKey"), shop.Root);
        Assert.Contains("PaymentOptions.ApiKey", Names(report["groups"]!.AsArray().First(g => (string)g!["name"]! == "code")!));
    }

    [Fact]
    public void A_git_diff_starts_from_the_methods_whose_lines_changed()
    {
        var file = Path.Combine(shop.Root, "Shop.Core", "Orders.cs");
        var original = File.ReadAllText(file);
        try
        {
            File.WriteAllText(file, original.Replace("Publish(new OrderPlaced(order.Id));", "Publish(new OrderPlaced(order.Id)); // changed"));
            var starts = new ImpactAnalyzer(shop.Graph).StartNodes(new ChangeSpec("diff", "HEAD"), shop.Root);
            Assert.Equal([PlaceOrder], starts.Select(s => s.Id));
        }
        finally
        {
            File.WriteAllText(file, original);
        }
    }

    static List<string> Names(JsonNode group) => [.. group["items"]!.AsArray().Select(i => (string)i!["name"]!)];

    // ---- Phase 1b: frontends --------------------------------------------------------------

    const string Angular = "ts|Shop.Web/ClientApp/src/app/";
    const string React = "ts|Shop.Web/react-app/src/";

    [Fact]
    public void A_broken_extractor_is_a_load_error_not_an_empty_frontend()
    {
        var fake = Path.Combine(Path.GetTempPath(), $"stangent-fake-{Guid.NewGuid():N}.mjs");
        File.WriteAllText(fake, "// prints nothing\n");
        try
        {
            var frontend = Stangent.Graph.Loading.FrontendScanner.Find(shop.Root).First();
            var (_, _, error) = Stangent.Graph.Loading.FrontendScanner.Extract(shop.Root, frontend, "x",
                new Dictionary<string, Dictionary<string, List<string>>>(), extractorOverride: fake);
            Assert.NotNull(error);
            Assert.Contains("produced no output", error);
        }
        finally
        {
            File.Delete(fake);
        }
    }

    [Fact]
    public void Frontend_components_templates_and_pages_are_on_the_map()
    {
        Assert.Equal(NodeKind.Component, NodeById(Angular + "order-form.component.ts#OrderFormComponent")?.Kind);
        Assert.Equal(NodeKind.Template, NodeById(Angular + "order-form.component.html#OrderFormComponent.template")?.Kind);
        Assert.Equal(NodeKind.Component, NodeById(React + "StockPage.tsx#StockPage")?.Kind);
        var pages = shop.Graph.AllNodes.Where(n => n.Kind == NodeKind.Page).Select(n => n.Name).Order().ToList();
        Assert.Equal(["/orders", "/orders/:id", "/orders/new", "/stock/:id"], pages);
        // The lazy child route resolves through import(...).then(m => m.TotalComponent).
        Assert.Contains(Edges(EdgeKind.RoutesTo), e => e.Source == "page:Shop.Web/ClientApp/tsconfig.json|/orders/:id" && e.Target == Angular + "total.component.ts#TotalComponent");
        Assert.True(NodeById(Angular + "order.service.spec.ts#test:OrderService › places an order")?.IsTest);
    }

    [Fact]
    public void Angular_templates_link_to_child_components_members_and_pipes()
    {
        var template = Angular + "order-form.component.html#OrderFormComponent.template";
        Assert.Contains(Edges(EdgeKind.Renders), e => e.Source == template && e.Target == Angular + "total.component.ts#TotalComponent" && e.Label == Label.Certain);
        Assert.Contains(Edges(EdgeKind.Calls), e => e.Source == template && e.Target == Angular + "order-form.component.ts#OrderFormComponent.submit");
        Assert.Contains(Edges(EdgeKind.Uses), e => e.Source == Angular + "total.component.ts#TotalComponent.template" && e.Target == Angular + "money.pipe.ts#MoneyPipe");
    }

    [Fact]
    public void Frontend_calls_reach_backend_endpoints_by_url_and_by_nswag_naming()
    {
        Assert.Contains(Edges(EdgeKind.CallsHttp), e => e.Source == Angular + "order.service.ts#OrderService.place" && e.Target == "endpoint:Shop.Web|POST /api/orders");
        // The fetch is in the component's own handler, a point of its own (G5) that the component uses.
        Assert.Contains(Edges(EdgeKind.CallsHttp), e => e.Source == React + "StockPage.tsx#StockPage.load" && e.Target == "endpoint:Shop.Stock|GET /api/stock/{id:int}");
        Assert.Contains(Edges(EdgeKind.Uses), e => e.Source == React + "StockPage.tsx#StockPage" && e.Target == React + "StockPage.tsx#StockPage.load");
        // OrdersClient isn't in the repo (NSwag generates it): createOrder → CreateOrder under /api/orders.
        Assert.Contains(Edges(EdgeKind.CallsHttp), e => e.Source == React + "OrderPage.tsx#OrderPage" && e.Target == "endpoint:Shop.Web|POST /api/orders" && e.Label == Label.Guessed);
    }

    [Fact]
    public void Typed_results_link_frontend_fields_to_backend_properties()
    {
        Assert.Contains(Edges(EdgeKind.MapsField), e => e.Source == Angular + "order.ts#Order.total" && e.Target == "Shop.Core|P:Shop.Core.Order.Total");
        Assert.Contains(Edges(EdgeKind.MapsField), e => e.Source == Angular + "order.ts#Order.customer" && e.Target == "Shop.Core|P:Shop.Core.Order.Customer");
        Assert.Contains(Edges(EdgeKind.MapsField), e => e.Source == Angular + "order.ts#Order.id" && e.Target == "Shop.Core|P:Shop.Core.Entity.Id");
    }

    [Fact]
    public void A_backend_field_change_reaches_the_frontend_page_that_shows_it()
    {
        // The whole code group: answers show 5 items per group, and which 5 depends on how many lines are ✅ (D175).
        var report = new ImpactAnalyzer(shop.Graph).Analyze(new ChangeSpec("symbol", "Shop.Core|P:Shop.Core.Order.Total"), shop.Root, expand: "code");
        Assert.Empty(SchemaRegistry.Embedded.Validate("impact-report.v5.json", report.ToJsonString()));
        var pages = Names(report["groups"]!.AsArray().First(g => (string)g!["name"]! == "pages")!);
        Assert.Contains("/orders", pages);
        Assert.Contains("OrderFormComponent.submit", Names(report["groups"]!.AsArray().First(g => (string)g!["name"]! == "code")!));
    }

    [Fact]
    public void A_backend_endpoint_change_reaches_pages_in_both_frontends_and_the_frontend_test()
    {
        var report = new ImpactAnalyzer(shop.Graph).Analyze(new ChangeSpec("symbol", "Endpoints.CreateOrder"), shop.Root, expand: "pages");
        var pages = Names(report["groups"]!.AsArray().First(g => (string)g!["name"]! == "pages")!);
        Assert.Contains("/orders", pages);       // Angular, through OrderService.place
        Assert.Contains("/orders/new", pages);   // React, through the NSwag client
        var tests = Names(report["groups"]!.AsArray().First(g => (string)g!["name"]! == "tests")!);
        Assert.Contains("OrderService › places an order", tests);
    }
}

public class GraphBuildTests
{
    [Fact]
    public async Task Builds_are_deterministic_and_an_incremental_build_equals_a_full_one()
    {
        var root = ShopCopy.Create();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var repo = StangentRepo.Discover(root);
            var (first, _) = await GraphBuilder.BuildAsync(repo, new BuildOptions(), ct);
            var (second, _) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false, Full: true), ct);
            Assert.Equal(Export(first, root, "a"), Export(second, root, "b"));

            File.AppendAllText(Path.Combine(root, "Shop.Stock", "Extra.cs"), "namespace Shop.Stock;\npublic static class Extra { public static int Twice(int x) => x * 2; }\n");
            var (incremental, stats) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false), ct);
            Assert.Equal(1, stats.Extracted);   // only Shop.Stock: nothing references it
            Assert.Equal(6, stats.Reused);      // the other 3 C# projects and the 3 frontends
            var (full, _) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false, Full: true), ct);
            Assert.Equal(Export(full, root, "c"), Export(incremental, root, "d"));
            Assert.Contains(incremental.AllNodes, n => n.Name == "Extra.Twice");

            // A frontend change re-extracts that frontend only, and still equals a full build.
            File.AppendAllText(Path.Combine(root, "Shop.Web", "react-app", "src", "Extra.ts"), "export const twice = (x: number) => x * 2;\n");
            var (frontInc, frontStats) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false), ct);
            Assert.Equal(1, frontStats.Extracted);
            var (frontFull, _) = await GraphBuilder.BuildAsync(repo, new BuildOptions(Restore: false, Full: true), ct);
            Assert.Equal(Export(frontFull, root, "e"), Export(frontInc, root, "f"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Changing_detector_settings_re_extracts_every_project()
    {
        var root = ShopCopy.Create();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            await GraphBuilder.BuildAsync(StangentRepo.Discover(root), new BuildOptions(), ct);
            var config = Path.Combine(root, StangentRepo.ConfigFile);
            File.WriteAllText(config, File.ReadAllText(config).Replace("entityBaseTypes: [Shop.Core.Entity]", "entityBaseTypes: []"));

            // Reload: the config is read when the repo is discovered.
            var (graph, stats) = await GraphBuilder.BuildAsync(StangentRepo.Discover(root), new BuildOptions(Restore: false), ct);
            Assert.Equal(7, stats.Extracted); // 4 C# projects and 3 frontends
            Assert.DoesNotContain(graph.AllNodes, n => n.Id == "table:Shop.Core|Order");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string Export(CodeGraph graph, string root, string name)
    {
        var path = Path.Combine(root, $"graph-{name}.jsonl.gz");
        GraphStore.Export(graph, path);
        var hash = CanonicalJson.Hash(Convert.ToBase64String(File.ReadAllBytes(path)));
        Assert.Equal(GraphStore.CanonicalLines(graph).Count(), GraphStore.CanonicalLines(GraphStore.Import(path)).Count());
        return hash;
    }
}

public class GraphUnitTests
{
    [Theory]
    [InlineData("/api/stock/{id:int}", "/api/stock/{*}", 9)]
    [InlineData("/api/stock/summary", "/api/stock/{*}", 7)]
    [InlineData("/api/stock/{id:int}", "/api/stock/7", 8)]
    [InlineData("/api/stock/summary", "/api/stock/other", -1)]
    [InlineData("/items/{id?}", "/items", 3)]
    [InlineData("/files/{*path}", "/files/a/b", 5)]
    public void Http_paths_score_by_how_closely_they_fit(string template, string path, int score) =>
        Assert.Equal(score, HttpServicesDetector.Score(template, path));

    [Fact]
    public void Appsettings_keys_are_flattened_with_their_lines()
    {
        var json = "{\n  // comment\n  \"A\": {\n    \"B\": 1,\n    \"C\": [\"x\", { \"D\": true }]\n  },\n  \"E\": null\n}\n"u8.ToArray();
        Assert.Equal([("A:B", 4), ("A:C:0", 5), ("A:C:1:D", 5), ("E", 7)], ConfigKeysDetector.Flatten(json));
    }

    [Fact]
    public void Diff_hunks_become_line_ranges_on_the_new_side()
    {
        var diff = "diff --git a/x.cs b/x.cs\n--- a/x.cs\n+++ b/x.cs\n@@ -3,0 +4,2 @@\n+a\n+b\n@@ -10 +12 @@\n-c\n+d\n@@ -20,3 +23,0 @@\n-e\n+++ /dev/null\n@@ -1 +0,0 @@\n";
        Assert.Equal([new ChangedLines("x.cs", 4, 5), new ChangedLines("x.cs", 12, 12), new ChangedLines("x.cs", 23, 23)], DiffMapper.Parse(diff));
    }

    [Fact]
    public void Doc_ids_give_the_containing_and_constructed_types()
    {
        Assert.Equal("A|T:Ns.T", DocIds.ContainingType("A|M:Ns.T.Run(System.Int32)"));
        Assert.Equal("A|T:Ns.Outer.Inner", DocIds.ConstructedType("A|M:Ns.Outer.Inner.#ctor"));
        Assert.Null(DocIds.ConstructedType("A|M:Ns.T.Run"));
        Assert.Null(DocIds.ContainingType("A|T:Ns.T"));
    }
}

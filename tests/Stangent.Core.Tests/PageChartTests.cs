using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Stangent.Core;
using Stangent.Flows;
using Stangent.Graph;

namespace Stangent.Core.Tests;

/// <summary>
/// Layer 4's developer page (D278–D285) on hand-made flow files: which flows get charts, step numbers on every box and row (D284),
/// the framework share and its floor (D281), the chart cap (D282), the note (D283), full labels, field checks only in the table,
/// gate 1 (D280) and byte-for-byte rebuilds. Fakes only; no AI.
/// </summary>
public sealed class PageChartTests : IDisposable
{
    const string Area = "Shop";
    readonly string root = Directory.CreateTempSubdirectory("stangent-chart-").FullName;
    readonly List<JsonObject> index = [];

    public void Dispose() => Directory.Delete(root, true);

    static JsonObject Cond(string kind, string at, string code) => new() { ["kind"] = kind, ["at"] = at, ["code"] = code };

    static JsonObject Step(int n, int? from, string kind, string what, string at, string code, params JsonObject[] conds)
    {
        var o = new JsonObject { ["n"] = n, ["kind"] = kind, ["what"] = what, ["label"] = "certain", ["at"] = at, ["code"] = code, ["conditions"] = new JsonArray([.. conds.Select(c => (JsonNode)c.DeepClone())]) };
        if (from is not null) o["from"] = from;
        return o;
    }

    static JsonObject Exit(string what, string at, int step, params JsonObject[] conds) => Exit("throw", what, at, step, conds);

    static JsonObject Exit(string kind, string what, string at, int step, params JsonObject[] conds) =>
        new() { ["kind"] = kind, ["what"] = what, ["point"] = "p", ["step"] = step, ["at"] = at, ["code"] = "throw …", ["conditions"] = new JsonArray([.. conds.Select(c => (JsonNode)c.DeepClone())]) };

    static JsonObject Check(string what, string property, string at, string rule = "NotEmpty") =>
        new() { ["what"] = what, ["rule"] = rule, ["property"] = property, ["message"] = "", ["onlyWarns"] = false, ["at"] = at, ["code"] = $".{rule}()" };

    static JsonObject Message(int step, string flow) =>
        new() { ["kind"] = "message", ["to"] = flow, ["flow"] = flow, ["step"] = step, ["label"] = "certain", ["sentence"] = "Sends", ["at"] = "src/OrderService.cs:31", ["code"] = "Publish" };

    void Flow(string id, string kind, string name, JsonArray steps, JsonArray? links = null, JsonArray? exits = null, JsonArray? checks = null, bool placed = true,
        JsonArray? unknowns = null, JsonArray? guesses = null, JsonArray? handled = null, string? point = null, string? startAt = null)
    {
        var flow = new JsonObject
        {
            ["contract"] = "flows", ["version"] = 1, ["commit"] = "abc", ["id"] = id, ["name"] = name, ["kind"] = kind, ["home"] = "",
            ["start"] = new JsonObject { ["what"] = kind, ["point"] = point ?? $"ts|web/{id}", ["at"] = startAt ?? $"web/{id}.tsx:1", ["code"] = "start()", ["triggers"] = new JsonArray() },
            ["actor"] = new JsonObject { ["who"] = "signed-in user", ["why"] = "", ["label"] = "certain" },
            ["inputs"] = new JsonArray(), ["checks"] = checks ?? [], ["steps"] = steps, ["exits"] = exits ?? [], ["scenarios"] = new JsonArray(),
            ["links"] = links ?? [], ["guesses"] = guesses ?? [], ["unknowns"] = unknowns ?? [], ["displayOnly"] = new JsonArray(),
        };
        if (handled is not null) flow["handled"] = handled;
        var file = $"specs/areas/shop/{id}.json";
        Directory.CreateDirectory(Path.Combine(root, "specs/areas/shop"));
        File.WriteAllText(Path.Combine(root, file), CanonicalJson.Serialize(flow));
        index.Add(new JsonObject { ["id"] = id, ["kind"] = kind, ["point"] = point ?? $"ts|web/{id}", ["start"] = $"web/{id}.tsx:1", ["name"] = name, ["file"] = file, ["links"] = new JsonArray(), ["placed"] = placed });
    }

    const string LogLine = "src/Shared/Log.cs:5";
    const string LongWhat = "calls outside the app: outside HTTP GET through IHttpClient: URL built at runtime from the series' title and its language";

    Areas? loaded;
    Placement? lastPlacement;

    Placement Placed() => lastPlacement!;

    /// <summary>
    /// The shop: a click that only reads; one whose only change is a log line many flows write; "Place order", which has every kind of
    /// change and stop; a handler; and <paramref name="others"/> flows outside the area that pass the same guard and log line.
    /// The hub threshold is 3. With 12 others, the log line is in 14 of 16 start flows.
    /// </summary>
    AreaCharts Charts(int others = 12, PageSettings? settings = null, Action? extra = null)
    {
        var http = Step(1, null, "http", "calls POST /api/orders", "web/useOrder.ts:3", "post()", Cond("mutation", "web/useOrder.ts:2", "mutate(…)"));
        var total = Cond("if", "src/OrderService.cs:29", "dto.Total > 0");
        Flow("click-sort", "click", "Sort", [Step(1, null, "database", "reads Order", "src/OrderRepo.cs:4", "_db.All()")]);
        Flow("click-cleanup", "click", "Clean up", [Step(1, null, "database", "saves AuditLog", LogLine, "_db.Insert(log)")]);
        var guessedStep = Step(12, 2, "message", "sends OrderAudited", "src/OrderService.cs:60", "Publish(new OrderAudited())");
        guessedStep["label"] = "guessed";
        Flow("click-place-order", "click", "Place order", [
            http,
            Step(2, 1, "call", "runs OrderController.Place", "src/OrderController.cs:10", "public IActionResult Place([FromBody] OrderDto dto, CancellationToken ct)"),
            Step(3, 2, "call", "runs LookupProxy.Get", "src/LookupProxy.cs:5", "Get(id)"),
            Step(4, 3, "outside-call", LongWhat, "src/LookupProxy.cs:8", "_http.Get(url)"),
            Step(5, 3, "outside-call", "calls outside the app: outside HTTP POST: URL built at runtime", "src/LookupProxy.cs:20", "_http.Post(url)"),
            Step(6, 2, "database", "reads Settings", "src/SettingsService.cs:20", "var config = _repo.SingleOrDefault();"),
            Step(7, 2, "database", "saves Settings", "src/SettingsService.cs:22", "_repo.Insert(Settings.Default);", Cond("if", "src/SettingsService.cs:21", "config == null")),
            Step(8, 2, "database", "saves Order", "src/OrderService.cs:30", "_repo.Insert(order);", total),
            Step(9, 2, "message", "sends OrderPlaced", "src/OrderService.cs:31", "Publish(new OrderPlaced())", total),
            Step(10, 2, "database", "saves Order through IOrderService.Update", "src/OrderService.cs:50", "_orders.Update(order);"),
            Step(11, 2, "database", "saves Coupon", "src/OrderService.cs:45", "_repo.Insert(coupon);", Cond("if", "src/OrderService.cs:44", "coupon == null")),
            guessedStep,
            Step(13, 2, "database", "saves AuditLog", LogLine, "_db.Insert(log)"),
        ],
        links: [Message(9, "handler-mail"), Message(12, "handler-a"), Message(12, "handler-b")],
        exits: [
            Exit("throws LookupFailed", "src/LookupProxy.cs:11", 3, Cond("catch", "src/LookupProxy.cs:9", "(HttpException ex)")),
            Exit("throws NotFound", "src/LookupProxy.cs:13", 3, Cond("if", "src/LookupProxy.cs:12", "response.StatusCode == 404")),
            Exit("throws BadRequest", "src/Rest.cs:5", 2, Cond("if", "src/Rest.cs:4", "body == null")),
            Exit("throws OutOfStock", "src/OrderController.cs:12", 2, Cond("if", "src/OrderController.cs:11", "stock < qty")),
            Exit("throws TooMany", "src/OrderController.cs:14", 2, Cond("if", "src/OrderController.cs:13", "dto.Qty > 100")),
            Exit("Redirect", "sends the user elsewhere ('/cart')", "src/OrderController.cs:16", 2, Cond("if", "src/OrderController.cs:15", "cartEmpty")),
        ],
        checks: [
            Check("OrderDto.Qty: GreaterThan(0)", "Api|P:Api.OrderDto.Qty", "src/OrderValidator.cs:5", "GreaterThan"),
            Check("OrderDto.Email: NotEmpty", "Api|P:Api.OrderDto.Email", "src/OrderValidator.cs:6"),
            Check("Api|M:Api.Rest.ValidateId(System.Int32): guard", "Api|M:Api.Rest.ValidateId(System.Int32)", "src/Rest.cs:3", "guard"),
        ],
        unknowns: [new JsonObject { ["at"] = "src/LookupProxy.cs:8", ["code"] = "_http.Get(url)", ["point"] = "p", ["step"] = 4, ["why"] = "outside HTTP GET: URL built at runtime" }],
        guesses: [new JsonObject { ["step"] = 12, ["what"] = "sends OrderAudited", ["reason"] = "handlers found by the event's type" }]);
        Flow("handler-mail", "handler", "MailHandler.Handle on OrderPlaced", [Step(1, null, "outside-call", "calls outside the app: outside SMTP send", "src/MailHandler.cs:4", "smtp.Send(m)")]);
        for (var i = 1; i <= others; i++)
            Flow($"click-other-{i:00}", "click", $"other {i}", [Step(1, null, "call", "runs Rest.Validate", "src/Rest.cs:1", "Validate()"), Step(2, null, "database", "saves AuditLog", LogLine, "_db.Insert(log)")],
                exits: [Exit("throws BadRequest", "src/Rest.cs:5", 1, Cond("if", "src/Rest.cs:4", "body == null"))], placed: false);
        extra?.Invoke();

        Directory.CreateDirectory(Path.Combine(root, FlowFiles.Folder));
        File.WriteAllText(Path.Combine(root, FlowFiles.IndexFile), new JsonObject
        {
            ["commit"] = "abc", ["settings"] = new JsonObject { ["hubCallers"] = 3, ["depth"] = 8 },
            ["flows"] = new JsonArray([.. index.Select(f => { var c = (JsonObject)f.DeepClone(); c.Remove("placed"); return (JsonNode)c; })]),
        }.ToJsonString());
        loaded = Areas.Load(new CodeGraph { Commit = "abc" }, root);
        var placement = lastPlacement = new Placement();
        foreach (var f in index.Where(f => (bool)f["placed"]!)) placement.AreaOf[(string)f["id"]!] = Area;
        index.Clear();
        return PageChart.Build(root, loaded, placement, Area, "shop", settings);
    }

    static FlowChart Order(AreaCharts c) => c.Charts.Single(x => x.Flow == "click-place-order");

    static string Page(AreaCharts c) => PageChart.Note(c) + PageChart.Body(c);

    PageGate.Result Gate(AreaCharts c, string text) => PageGate.Check(new FlowReader(root, loaded!), c, text);

    [Fact]
    public void A_change_in_more_than_the_share_of_flows_and_at_least_ten_is_listed_under_shared_code_not_drawn()
    {
        var c = Charts();
        Assert.Equal(16, c.AppFlows);
        Assert.Equal(["click-place-order", "handler-mail"], c.Charts.Select(x => x.Flow));
        Assert.Equal([("click-cleanup", "only changes made in shared code"), ("click-sort", "only reads or shows")], c.DisplayOnly.Select(d => (d.Flow, d.Why)));
        var order = Order(c);
        var log = Assert.Single(order.SharedChanges);
        Assert.Equal(("saves AuditLog", LogLine, 14), (log.Text, log.At, log.Flows));
        Assert.Equal([13], log.Steps);
        Assert.DoesNotContain(order.Chart.Nodes, n => n.Label.Contains("AuditLog", StringComparison.Ordinal));
        var md = Page(c);
        Assert.Contains($"- saves AuditLog (step 13) `{LogLine}`, in 14 of 16 flows\n", md);
        Assert.Contains($"- `click-cleanup` (click) Clean up: only changes made in shared code: saves AuditLog (step 1) `{LogLine}`, in 14 of 16 flows", md);
    }

    [Fact]
    public void Below_ten_flows_a_change_is_drawn_whatever_its_share_and_the_share_is_a_setting()
    {
        // 9 flows of 11 write the log line: 82%, but under the floor of 10.
        var c = Charts(others: 7);
        Assert.Contains(c.Charts, x => x.Flow == "click-cleanup");
        Assert.Contains(Order(c).Chart.Nodes, n => n.Label == "saves AuditLog (step 13)");
        Assert.Empty(Order(c).SharedChanges);
        // 14 of 16 is 87.5%, not more than 90%.
        var high = Charts(settings: new PageSettings(FrameworkShare: 90));
        Assert.Contains(Order(high).Chart.Nodes, n => n.Label == "saves AuditLog (step 13)");
    }

    [Fact]
    public void Every_box_carries_its_step_numbers()
    {
        var g = Order(Charts()).Chart;
        Assert.Equal([
            $"LookupProxy {LongWhat} ❓ (step 4)",
            "LookupProxy calls outside the app: outside HTTP POST: URL built at runtime (step 5)",
            "saves Order (step 8)", "sends OrderPlaced (step 9)", "saves Order through IOrderService.Update (step 10)", "saves Coupon (step 11)", "sends OrderAudited 🟡 (step 12)",
        ], g.Nodes.Where(n => n.Shape == "change").Select(n => n.Label));
        Assert.Contains(g.Nodes, n => n.Shape == "diamond" && n.Label == "dto.Total > 0? (step 8)");
        Assert.Contains(g.Nodes, n => n.Shape == "diamond" && n.Label == "stock < qty? (step 2)");
        Assert.Equal("sends the user to '/cart' (step 2)", Assert.Single(g.Nodes, n => n.Shape == "redirect").Label);
        Assert.Equal("then, on its own: 2 flows (step 12)", g.Nodes.Single(n => n.Id == g.Edges.Single(e => e.Label == "then" && g.Nodes.Single(x => x.Id == e.From).Label.StartsWith("sends OrderAudited", StringComparison.Ordinal)).To).Label);
        Assert.Single(g.Nodes, n => n.Shape == "refused");
        Assert.DoesNotContain(g.Nodes, n => n.Label == "Done");
    }

    [Fact]
    public void Scenario_rows_name_the_steps_they_pass_and_where_they_stop()
    {
        var order = Order(Charts());
        var rows = order.Scenarios;
        // D288: a condition's row has its diamond's id on the chart; the main path names the steps that can run.
        Assert.Equal(["main", "D5", "D6", "K1", "K2", "S1", "X1", "X2", "X3", "U1"], rows.Select(r => r.Id));
        Assert.Equal("dto.Total > 0? (step 8)", order.Chart.Nodes.Single(n => n.Id == "D5").Label);
        Assert.Equal("no coupon? (step 11)", order.Chart.Nodes.Single(n => n.Id == "D6").Label);
        Assert.Equal(("can pass steps 4, 5, 8, 9, 10, 11, 12", "ends after step 12"), (rows[0].Steps, rows[0].Ends));
        Assert.Equal(("When `dto.Total > 0` is false", "passes steps 4, 5, 10, 11, 12; skips steps 8, 9"), (rows[1].Scenario, rows[1].Steps));
        Assert.Equal("When has coupon", rows[2].Scenario);
        Assert.Equal("Refused: throws OutOfStock, stops at step 2 (`src/OrderController.cs:12`)", rows[6].Ends);
        Assert.Equal("passes no change", rows[6].Steps);
        Assert.Equal("sends the user to '/cart', stops at step 2 (`src/OrderController.cs:16`)", rows[8].Ends);
        Assert.Equal(("Setup: saves Settings", "also passes step 7"), (rows[9].Scenario, rows[9].Steps));
        Assert.Contains("nothing saved yet: config read from Settings", rows[9].Decides, StringComparison.Ordinal);
        Assert.Equal(("LookupProxy fails", "Refused"), (rows[5].Scenario, rows[5].Ends));
    }

    [Fact]
    public void Field_checks_are_listed_only_in_the_table_and_the_chart_keeps_one_diamond()
    {
        var c = Charts();
        var md = PageChart.FlowMarkdown(Order(c), c);
        Assert.Single(Order(c).Chart.Nodes, n => n.Label == "All checks pass?");
        Assert.Contains("| K1 | OrderDto.Qty fails a check | OrderDto.Qty: GreaterThan(0) (`src/OrderValidator.cs:5`) |", md);
        Assert.Equal(1, Regex.Count(md, Regex.Escape("GreaterThan(0)")));
        Assert.DoesNotContain("**All checks pass?**", md);
        Assert.DoesNotContain("<details>", md);
    }

    [Fact]
    public void The_lists_carry_stops_and_flow_counts_in_the_set_order()
    {
        var c = Charts();
        var md = PageChart.FlowMarkdown(Order(c), c);
        Assert.Contains("- LookupProxy: throws LookupFailed when on error (HttpException ex), stops at step 3 (`src/LookupProxy.cs:11`)\n", md);
        Assert.Contains("- throws BadRequest when no body (`body == null`), stops at step 2 (`src/Rest.cs:5`), in 13 of 16 flows\n", md);
        Assert.Contains("- after sends OrderAudited (step 12): `handler-a`, `handler-b`\n", md);
        var order = new[] { "```mermaid", "**Scenarios**", "**Outside services failing**", "**What runs afterwards on its own**", "**Guards shared with other flows**", "**Changes made in shared code**" };
        Assert.Equal(order, order.OrderBy(h => md.IndexOf(h, StringComparison.Ordinal)));
    }

    [Fact]
    public void Labels_are_full_and_escaped_for_Mermaid()
    {
        Assert.Equal("a #quot;b#quot; #91;c#93; #96;d#96; #124; #lt;e#gt; #35;1 #123;f#125; #40;g#41; #59;", PageChart.Escape("a \"b\" [c] `d` | <e> #1 {f} (g) ;"));
        Assert.Equal("x y", PageChart.Escape("x\n   y"));
        Assert.Equal(new string('a', 200), PageChart.Escape(new string('a', 200)));
        var md = Page(Charts());
        Assert.Contains("outside HTTP GET through IHttpClient: URL built at runtime from the series' title and its language ❓ #40;step 4#41;", md);
        Assert.DoesNotContain("…", md);
    }

    [Fact]
    public void Above_the_cap_a_click_has_no_chart_only_its_node_count_and_its_table()
    {
        var c = Charts(settings: new PageSettings(ChartCap: 5));
        var order = Order(c);
        Assert.False(order.Drawn);
        var md = PageChart.FlowMarkdown(order, c);
        Assert.DoesNotContain("```mermaid", md);
        Assert.Contains($"No chart: it would have {order.Chart.Nodes.Count} nodes, over the chart cap of 5 (`pages.chartCap`).", md);
        Assert.Contains("| main | Main path |", md);
        Assert.True(Charts().Charts.All(x => x.Drawn));
        Assert.Contains("tooLargeToDraw: 1\n", PageChart.Note(c));
    }

    [Fact]
    public void The_note_opens_the_page_with_counts_and_no_date()
    {
        var c = Charts();
        var note = PageChart.Note(c);
        Assert.Equal("""
            ---
            area: "Shop"
            madeBy: code, no AI
            commit: "abc"
            settings:
              hubCallers: 3
              callDepth: 8
              frameworkSharePercent: 10
              chartCap: 60
            clicks:
              charted: 2
              oneLine: 2
              tooLargeToDraw: 0
            steps:
              certain: 14
              guessed: 1
              cantTell: 1
            ---

            """, note);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", Page(c));
        Assert.Contains("`click-place-order` · click · 13 steps: 🟡 1 guessed, ❓ 1 can't tell", Page(c));
        Assert.Contains("- ❓ `click-place-order` step 4: outside HTTP GET: URL built at runtime (`src/LookupProxy.cs:8`)", Page(c));
    }

    [Fact]
    public void Gate_one_passes_on_a_full_page_and_catches_a_missing_change_and_a_missing_stop()
    {
        var c = Charts();
        var page = Page(c);
        var ok = Gate(c, page);
        Assert.Empty(ok.Missing);
        Assert.Equal(6, ok.Stops);
        Assert.True(ok.Changes >= 11);
        var noChange = Gate(c, page.Replace("saves Order #40;step 8#41;", "saves Order", StringComparison.Ordinal));
        var miss = Assert.Single(noChange.Missing);
        Assert.Equal(("change", 8), ((string)miss["what"]!, (int)miss["step"]!));
        var noStop = Gate(c, page.Replace("stops at step 2 (`src/OrderController.cs:12`)", "", StringComparison.Ordinal));
        var stop = Assert.Single(noStop.Missing);
        Assert.Equal(("stop", "src/OrderController.cs:12"), ((string)stop["what"]!, (string)stop["at"]!));
        // Above the cap, the table holds every change.
        var big = Charts(settings: new PageSettings(ChartCap: 5));
        Assert.Empty(Gate(big, Page(big)).Missing);
    }

    /// <summary>D313(1): a click whose request the graph matched, one it couldn't, one calling only another host, and a handler.</summary>
    void StartFlows()
    {
        var http = Step(1, null, "http", "calls POST {base}/api/pay", "web/usePay.ts:3", "post()");
        http["point"] = "endpoint:Api|POST /api/Pay";
        Flow("click-pay", "click", "Pay", [
            http,
            Step(2, 1, "call", "runs PayController.Pay", "src/PayController.cs:12", "public IActionResult Pay()"),
            Step(3, 2, "database", "saves Payment", "src/PayService.cs:20", "_repo.Insert(p);"),
            Step(4, 2, "database", "saves Receipt", "src/PayService.cs:21", "_repo.Insert(r);"),
            Step(5, 2, "database", "saves Ledger", "src/PayService.cs:22", "_repo.Insert(l);"),
            Step(6, 2, "database", "saves Wallet", "src/PayService.cs:23", "_repo.Insert(w);"),
            Step(7, 2, "message", "sends PaymentMade", "src/PayService.cs:24", "Publish(new PaymentMade())"),
        ], startAt: "web/PayButton.tsx:40");
        var vague = Step(1, null, "http", "can't be followed: HTTP POST http://localhost:5000/api/{*}: its only fixed part is the API root", "web/useFilter.ts:3", "post()");
        vague["end"] = new JsonObject { ["kind"] = "unknown", ["detail"] = "HTTP POST http://localhost:5000/api/{*}: its only fixed part is the API root (/api), so which endpoint it calls is built at runtime" };
        Flow("click-filter", "click", "Filter", [vague, Step(2, null, "database", "saves Filter", "src/FilterRepo.cs:3", "_db.Insert(f)")]);
        var github = Step(1, null, "http", "can't be followed: HTTP GET to 'api.github.com': no service with that name in this repo", "web/useUpdate.ts:3", "get()");
        github["end"] = new JsonObject { ["kind"] = "unknown", ["detail"] = "HTTP GET to 'api.github.com': no service with that name in this repo" };
        Flow("click-update", "click", "Update", [github, Step(2, null, "database", "saves Version", "src/VersionRepo.cs:3", "_db.Insert(v)")]);
        Flow("screen-opens-admin-pay", "screen-opens", "ANY /Admin/Pay/List/{id?}", [Step(1, null, "database", "saves Setting", "src/SettingRepo.cs:3", "_db.Insert(s)")],
            point: "Api|M:Api.PayController.List", startAt: "src/PayController.cs:30");
        Flow("screen-opens-paycontroller-edit", "screen-opens", "PayController.Edit", [Step(1, null, "database", "saves Setting", "src/SettingRepo.cs:3", "_db.Insert(s)")],
            point: "Api|M:Api.PayController.Edit", startAt: "src/PayController.cs:40");
        Flow("handler-ledger", "handler", "LedgerHandler.Handle on PaymentMade", [Step(1, null, "database", "saves Ledger", "src/LedgerHandler.cs:9", "_db.Insert(l)")],
            point: "Api|M:Api.LedgerHandler.Handle(Api.PaymentMade)", startAt: "src/LedgerHandler.cs:7");
    }

    [Fact]
    public void Each_index_line_says_where_to_start_what_it_changes_and_what_it_sends()
    {
        var c = Charts(extra: StartFlows);
        var page = Page(c);
        Assert.Contains("- starts with the click \"Pay\" ([`click-pay`](click-pay.md)): chart, ", page, StringComparison.Ordinal);
        Assert.Contains("  - start here: `web/PayButton.tsx:40` → `POST /api/Pay` → `src/PayController.cs:12`\n", page, StringComparison.Ordinal);
        Assert.Contains("  - changes: saves Payment, saves Receipt, saves Ledger, +1 more · sends `PaymentMade`\n", page, StringComparison.Ordinal);
        // A request to this app the graph couldn't match is ❓ with what it asked for; one to another host is no request of the app's.
        Assert.Contains("  - start here: `web/click-filter.tsx:1` → ❓ `POST /api/{*}`\n", page, StringComparison.Ordinal);
        Assert.Contains("  - start here: `web/click-update.tsx:1` → ❓ request\n", page, StringComparison.Ordinal);
        // A screen the server makes opens at the route its name gives, landing on its action; with no route in its name, ❓.
        Assert.Contains("  - start here: `ANY /Admin/Pay/List/{id?}` → `PayController.List` at `src/PayController.cs:30`\n", page, StringComparison.Ordinal);
        Assert.Contains("  - start here: ❓ request → `PayController.Edit` at `src/PayController.cs:40`\n", page, StringComparison.Ordinal);
        // A handler starts in its C# method; it sends no request, so none is named.
        Assert.Contains("  - start here: `LedgerHandler.Handle` at `src/LedgerHandler.cs:7`\n  - changes: saves Ledger\n", page, StringComparison.Ordinal);
        // An outside call is named by its service; the flow parts and gate 1 are unchanged by the lines.
        Assert.Contains("calls outside: `LookupProxy`", page, StringComparison.Ordinal);
        Assert.Empty(Gate(c, page).Missing);
    }

    [Fact]
    public void Start_flows_are_ordered_by_kind_then_small_to_large_with_too_large_ones_last_in_the_index_and_the_body()
    {
        var c = Charts(extra: StartFlows, settings: new PageSettings(ChartCap: 6));
        var order = c.Charts.Select(x => (PageChart.KindRank(x.Kind), x.Drawn ? 0 : 1, x.Chart.Nodes.Count)).ToList();
        Assert.Equal(order.Order().ToList(), order);
        Assert.Contains(c.Charts, x => !x.Drawn);
        Assert.Contains(c.Charts, x => x.Kind == "handler");
        var page = Page(c);
        var index = Regex.Matches(page, @"^- .*\(\[`(?<id>[^`]+)`\]", RegexOptions.Multiline).Select(m => m.Groups["id"].Value).ToList();
        var body = Regex.Matches(page, @"^<!-- flow (?<id>\S+) -->$", RegexOptions.Multiline).Select(m => m.Groups["id"].Value).ToList();
        Assert.Equal(c.Charts.Select(x => x.Flow), index);
        Assert.Equal(index, body);
        // What code couldn't see follows the same order.
        var seen = c.CantSee.Select(x => x.Flow).Distinct().ToList();
        Assert.Equal(seen, index.Concat(c.DisplayOnly.Select(d => d.Flow)).Where(seen.Contains));
    }

    [Fact]
    public void One_legend_line_near_the_top_says_what_the_marks_and_step_numbers_mean()
    {
        var body = PageChart.Body(Charts());
        Assert.Single(Regex.Matches(body, Regex.Escape(PageChart.Legend)));
        Assert.StartsWith(PageChart.BodyMarker + "\n\n" + PageChart.Legend + "\n\n## Clicks that change something", body, StringComparison.Ordinal);
        Assert.Contains("🟡 guessed", PageChart.Legend, StringComparison.Ordinal);
        Assert.Contains("❓ can't tell", PageChart.Legend, StringComparison.Ordinal);
        Assert.Contains("\"step N\"", PageChart.Legend, StringComparison.Ordinal);
        Assert.Contains("`<flow id>.md`", PageChart.Legend, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_flows_give_the_same_bytes()
    {
        var first = Charts();
        var second = Charts();
        Assert.Equal(CanonicalJson.Serialize(PageChart.ToJson(first)), CanonicalJson.Serialize(PageChart.ToJson(second)));
        Assert.Equal(Page(first), Page(second));
        Assert.Equal(PageChart.Counts(first), PageChart.Counts(second));
    }

    [Fact]
    public void A_loop_diamond_reads_each_and_after_the_loop()
    {
        var c = Charts(extra: () => Flow("click-import", "click", "Import", [
            Step(1, null, "call", "runs Import", "src/Import.cs:3", "Import(list)"),
            Step(2, 1, "outside-call", "calls outside the app: outside HTTP GET", "src/Proxy.cs:4", "_http.Get()", Cond("loop", "src/Import.cs:5", "foreach (var s in list)")),
            Step(3, 1, "database", "saves Series", "src/Import.cs:9", "_repo.InsertMany(list)"),
        ]));
        var g = c.Charts.Single(x => x.Flow == "click-import").Chart;
        var d = g.Nodes.Single(n => n.Label == "for each: foreach (var s in list)? (step 2)");
        Assert.Equal(["each", "after the loop"], g.Edges.Where(e => e.From == d.Id).Select(e => e.Label));
    }

    [Fact]
    public void The_page_settings_default_and_are_checked_when_stangent_yaml_is_read()
    {
        File.WriteAllText(Path.Combine(root, StangentRepo.ConfigFile), "version: 1\n");
        Assert.Equal(new PageSettings(10, 60), PageSettings.From(StangentRepo.Discover(root).Config));
        File.WriteAllText(Path.Combine(root, StangentRepo.ConfigFile), "version: 1\npages:\n  frameworkShare: 15\n  chartCap: 45\n");
        Assert.Equal(new PageSettings(15, 45), PageSettings.From(StangentRepo.Discover(root).Config));
        File.WriteAllText(Path.Combine(root, StangentRepo.ConfigFile), "version: 1\npages:\n  chartCap: 0\n");
        Assert.Equal("invalid_config", Assert.Throws<StangentException>(() => StangentRepo.Discover(root)).Code);
        File.WriteAllText(Path.Combine(root, StangentRepo.ConfigFile), "version: 1\npages:\n  frameworkShare: 101\n");
        Assert.Equal("invalid_config", Assert.Throws<StangentException>(() => StangentRepo.Discover(root)).Code);
    }

    [Fact]
    public void A_page_never_depends_on_which_areas_were_built_before_it_through_one_cache()
    {
        // Two shared flows continue into each other; the walk cuts the loop where it started, so asking for sh-a first used to
        // cache sh-b without sh-a's save, and the other area's page then changed with the build order.
        static JsonObject Continues(int step, string flow) => new() { ["kind"] = "continues", ["flow"] = flow, ["step"] = step, ["at"] = $"src/C.cs:{step}" };
        Flow("sh-a", "shared", "A.Save", [Step(1, null, "database", "saves Alpha", "src/A.cs:3", "_db.Insert(a)")], links: [Continues(1, "sh-b")], placed: false);
        Flow("sh-b", "shared", "B.Save", [Step(1, null, "database", "saves Beta", "src/B.cs:3", "_db.Insert(b)")], links: [Continues(1, "sh-a")], placed: false);
        Flow("click-one", "click", "One", [Step(1, null, "http", "calls POST /one", "web/one.ts:1", "post()"), Step(2, 1, "call", "runs B", "src/One.cs:2", "B()")],
            links: [Continues(2, "sh-b")], placed: false);
        Flow("click-two", "click", "Two", [Step(1, null, "http", "calls POST /two", "web/two.ts:1", "post()"), Step(2, 1, "call", "runs A", "src/Two.cs:2", "A()"), Step(3, 1, "call", "runs B", "src/Two.cs:3", "B()")],
            links: [Continues(2, "sh-a"), Continues(3, "sh-b")], placed: false);
        Directory.CreateDirectory(Path.Combine(root, FlowFiles.Folder));
        File.WriteAllText(Path.Combine(root, FlowFiles.IndexFile), new JsonObject
        {
            ["commit"] = "abc", ["settings"] = new JsonObject { ["hubCallers"] = 3, ["depth"] = 8 },
            ["flows"] = new JsonArray([.. index.Select(f => { var c = (JsonObject)f.DeepClone(); c.Remove("placed"); return (JsonNode)c; })]),
        }.ToJsonString());
        var a = Areas.Load(new CodeGraph { Commit = "abc" }, root);
        var placement = new Placement();
        placement.AreaOf["click-one"] = "One";
        placement.AreaOf["click-two"] = "Two";
        string Built(string area, PageCache cache) => Page(PageChart.Build(root, a, placement, area, area.ToLowerInvariant(), cache: cache));
        var fresh = new[] { "One", "Two" }.ToDictionary(x => x, x => Built(x, new PageCache(root, a)));
        Assert.Contains("saves Alpha #40;in shared code A.Save#41;", fresh["One"], StringComparison.Ordinal);
        foreach (var order in new[] { new[] { "One", "Two" }, new[] { "Two", "One" } })
        {
            var cache = new PageCache(root, a);
            foreach (var area in order) Assert.Equal(fresh[area], Built(area, cache));
        }
    }

    [Fact]
    public void Elsewhere_lists_the_landed_controllers_other_endpoints_with_the_start_flows_that_call_them()
    {
        // D316: Kavita's Reading Lists page lands on ReadingListController; its create and delete live on other areas' pages.
        const string place = "endpoint:Api|POST /api/orders", cancel = "endpoint:Api|POST /api/orders/cancel", drop = "endpoint:Api|DELETE /api/orders";
        static JsonObject Request(string what, string at, string endpoint) { var st = Step(1, null, "http", what, at, "post()"); st["point"] = endpoint; return st; }
        Flow("click-place", "click", "Place order", [Request("calls POST /api/orders", "web/order.ts:1", place), Step(2, 1, "call", "runs OrderController.Place", "src/OrderController.cs:10", "Place()")]);
        Flow("click-cancel", "click", "Cancel", [Request("calls POST /api/orders/cancel", "web/cancel.ts:1", cancel)], placed: false);
        Directory.CreateDirectory(Path.Combine(root, FlowFiles.Folder));
        File.WriteAllText(Path.Combine(root, FlowFiles.IndexFile), new JsonObject
        {
            ["commit"] = "abc", ["settings"] = new JsonObject { ["hubCallers"] = 3, ["depth"] = 8 },
            ["flows"] = new JsonArray([.. index.Select(f => { var c = (JsonObject)f.DeepClone(); c.Remove("placed"); return (JsonNode)c; })]),
        }.ToJsonString());
        var graph = new CodeGraph { Commit = "abc" };
        var line = 10;
        foreach (var (endpoint, action) in new[] { (place, "Place"), (cancel, "Cancel"), (drop, "Drop") })
        {
            var id = $"Api|M:Api.OrderController.{action}";
            graph.LinkedNodes.Add(new Node(endpoint, NodeKind.Endpoint, endpoint, "Api", null));
            graph.LinkedNodes.Add(new Node(id, NodeKind.Method, $"OrderController.{action}", "Api", "src/OrderController.cs", line, line + 5));
            graph.LinkedEdges.Add(new Edge(endpoint, id, EdgeKind.RoutesTo, Label.Certain));
            line += 10;
        }
        var a = Areas.Load(graph, root);
        var placement = new Placement();
        placement.AreaOf["click-place"] = Area;
        placement.AreaOf["click-cancel"] = "Orders";
        var c = PageChart.Build(root, a, placement, Area, "shop");
        var body = PageChart.Body(c);
        Assert.Contains(PageElsewhere.Heading + "\n", body, StringComparison.Ordinal);
        Assert.Contains("- `OrderController` (`src/OrderController.cs`): 2 of its 3 endpoints not called here\n"
            + "  - `POST /api/orders/cancel` (`OrderController.cs:20`): called by Orders: \"Cancel\"\n"
            + "  - no start flow calls: `DELETE /api/orders` (`:30`)\n", body, StringComparison.Ordinal);
        Assert.DoesNotContain("`POST /api/orders` (", body[body.IndexOf(PageElsewhere.Heading, StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void A_one_line_flows_own_stops_are_listed_under_it_and_its_shared_guards_only_counted()
    {
        var c = Charts(extra: () => Flow("click-view", "click", "View", [
            Step(1, null, "database", "reads Order", "src/View.cs:4", "_db.Find(id)"), Step(2, null, "call", "runs Rest.Validate", "src/Rest.cs:1", "Validate()")],
            exits: [
                Exit("throws NotFound", "src/View.cs:9", 1, Cond("if", "src/View.cs:8", "item == null")),
                Exit("throws NotFound", "src/View.cs:20", 2, Cond("if", "src/View.cs:8", "item == null")),
                Exit("throws BadRequest", "src/Rest.cs:5", 2, Cond("if", "src/Rest.cs:4", "body == null")),
            ]));
        var d = c.DisplayOnly.Single(x => x.Flow == "click-view");
        Assert.Equal("only reads or shows", d.Why);
        var page = Page(c);
        Assert.Contains("- `click-view` (click) View: only reads or shows · 2 steps: 🟡 0 guessed, ❓ 0 can't tell\n"
            + "  - stops at step 1 (`src/View.cs:9`), stops at step 2 (`src/View.cs:20`): throws NotFound when no item (`item == null`)\n"
            + "  - + 1 shared guard\n", page);
        Assert.DoesNotContain("stops at step 2 (`src/Rest.cs:5`): ", page);
        var ok = Gate(c, page);
        Assert.Empty(ok.Missing);
        Assert.Equal(1, ok.OneLineGuards);
        var gone = Gate(c, page.Replace("stops at step 2 (`src/View.cs:20`)", "", StringComparison.Ordinal));
        var miss = Assert.Single(gone.Missing);
        Assert.Equal(("click-view", "stop", "src/View.cs:20"), ((string)miss["flow"]!, (string)miss["what"]!, (string)miss["at"]!));
    }
    static JsonObject Pointed(JsonObject step, string point) { step["point"] = point; return step; }

    static JsonObject Continues(int step, string flow) => new() { ["kind"] = "continues", ["flow"] = flow, ["step"] = step, ["at"] = $"src/C.cs:{step}" };

    /// <summary>A loop holding an outside call and two saves under an inner <c>if</c>, as Sonarr's scene-mapping handler; then a save after the loop.</summary>
    void Sync() => Flow("click-sync", "click", "Sync", [
        Step(1, null, "call", "runs Sync", "src/Sync.cs:3", "Sync()"),
        Step(2, 1, "outside-call", "calls outside the app: outside HTTP GET", "src/Proxy.cs:4", "_http.Get()", Cond("loop", "src/Sync.cs:5", "foreach (var m in list)")),
        // A flow file lists a step's own conditions innermost first.
        Step(3, 1, "database", "saves Mapping", "src/Sync.cs:9", "_repo.DeleteMany(old)", Cond("if", "src/Sync.cs:8", "mappings.Any()"), Cond("loop", "src/Sync.cs:5", "foreach (var m in list)")),
        Step(4, 1, "database", "saves Mapping", "src/Sync.cs:10", "_repo.InsertMany(m)", Cond("if", "src/Sync.cs:8", "mappings.Any()"), Cond("loop", "src/Sync.cs:5", "foreach (var m in list)")),
        Step(5, 1, "database", "saves SyncLog", "src/Sync.cs:20", "_repo.Insert(log)"),
    ],
    exits: [Exit("throws Locked", "src/Sync.cs:13", 1, Cond("if", "src/Sync.cs:12", "result.IsLockedOut"), Cond("if", "src/Sync.cs:11", "string.IsNullOrEmpty(key)"))]);

    [Fact]
    public void A_loop_is_drawn_once_with_everything_inside_it_and_its_no_items_row_passes_none_of_them()
    {
        var chart = Charts(extra: Sync).Charts.Single(x => x.Flow == "click-sync");
        var g = chart.Chart;
        var loop = Assert.Single(g.Nodes, n => n.Label.StartsWith("for each: foreach (var m in list)?", StringComparison.Ordinal));
        var any = g.Nodes.Single(n => n.Label.StartsWith("mappings has items?", StringComparison.Ordinal));
        var call = g.Nodes.Single(n => n.Label.StartsWith("Proxy calls outside", StringComparison.Ordinal));
        var log = g.Nodes.Single(n => n.Label == "saves SyncLog (step 5)");
        Assert.Contains(g.Edges, e => e.From == loop.Id && e.To == call.Id && e.Label == "each");
        Assert.Contains(g.Edges, e => e.From == call.Id && e.To == any.Id);
        // D305(2): the lockout check (Sync.cs:11–13) sits after the loop in the code, so it comes between the loop and the log.
        var locked = g.Nodes.Single(n => n.Label == "key is empty and IsLockedOut? (step 1)");
        Assert.Equal(locked.Id, g.Edges.Single(e => e.From == loop.Id && e.Label == "after the loop").To);
        Assert.Contains(g.Edges, e => e.From == locked.Id && e.To == log.Id);
        // Same words, different lines: two boxes (D288).
        Assert.Equal(["saves Mapping (step 3)", "saves Mapping (step 4)"], g.Nodes.Where(n => n.Label.StartsWith("saves Mapping", StringComparison.Ordinal)).Select(n => n.Label));
        var row = chart.Scenarios.Single(r => r.Scenario.EndsWith("has no items", StringComparison.Ordinal));
        Assert.Equal(loop.Id, row.Id);
        Assert.Equal("passes step 5; skips steps 2, 3, 4", row.Steps);
        // Conditions print outer first, as the code nests them.
        Assert.Equal("key is empty: `string.IsNullOrEmpty(key)` (`src/Sync.cs:11`); IsLockedOut: `result.IsLockedOut` (`src/Sync.cs:12`)", chart.Scenarios.Single(r => r.Id == "X1").Decides);
        Assert.Contains(g.Nodes, n => n.Label == "key is empty and IsLockedOut? (step 1)");
    }

    [Fact]
    public void Elements_under_one_condition_share_its_diamond_even_when_others_come_between()
    {
        var c = Charts(extra: () => Flow("click-mix", "click", "Mix", [
            Step(1, null, "call", "runs Mix", "src/Mix.cs:3", "Mix()"),
            Step(2, 1, "database", "saves Alpha", "src/Mix.cs:6", "a()", Cond("loop", "src/Mix.cs:5", "foreach (var x in xs)")),
            Step(3, 1, "database", "saves Beta", "src/Mix.cs:9", "b()", Cond("if", "src/Mix.cs:8", "flag")),
            Step(4, 1, "database", "saves Gamma", "src/Mix.cs:7", "c()", Cond("loop", "src/Mix.cs:5", "foreach (var x in xs)")),
        ]));
        var chart = c.Charts.Single(x => x.Flow == "click-mix");
        var loop = Assert.Single(chart.Chart.Nodes, n => n.Label.StartsWith("for each:", StringComparison.Ordinal));
        var row = chart.Scenarios.Single(r => r.Id == loop.Id);
        Assert.Equal("passes step 3; skips steps 2, 4", row.Steps);
    }

    /// <summary>
    /// A click whose validator runs mid-flow (step 3), a redirect stop with no step on a step's line, a queued job with an empty "()",
    /// an <c>unless !x</c>, and a save in shared code.
    /// </summary>
    void Add()
    {
        Flow("sh-store", "shared", "Store.Put", [Step(1, null, "database", "saves Blob", "src/Store.cs:7", "_db.Insert(b)")], placed: false);
        var redirect = Exit("Redirect", "sends the user elsewhere (Done)", "src/ItemController.cs:40", 0);
        redirect.Remove("step");
        Flow("click-add", "click", "Add", [
            Step(1, null, "http", "calls POST /api/items", "web/add.ts:3", "post()"),
            Step(2, 1, "database", "saves Item", "src/ItemService.cs:12", "_repo.Insert(item)", Cond("unless", "src/ItemService.cs:11", "!hasLicense")),
            Pointed(Step(3, 1, "call", "calls IAddValidator.Validate", "src/ItemService.cs:10", "_validator.Validate(item)"), "Api|M:Api.IAddValidator.Validate(Api.Item)"),
            Step(4, 1, "call", "calls Done", "src/ItemController.cs:40", "return RedirectToAction(\"Done\");"),
            Step(5, 1, "queue", "queues Job through RecurringJob.AddOrUpdate, which runs later, in the background ()", "src/Jobs.cs:4", "RecurringJob.AddOrUpdate(…)"),
            Step(7, 1, "call", "runs Store.Put", "src/ItemService.cs:20", "_store.Put(b)"),
        ],
        links: [Continues(7, "sh-store")],
        exits: [redirect],
        checks: [Check("Item.Path: NotEmpty", "Api|P:Api.Item.Path", "src/AddValidator.cs:20"), Check("ItemDto.Name: NotEmpty", "Api|P:Api.ItemDto.Name", "src/ItemController.cs:30")]);
    }

    [Fact]
    public void A_check_run_mid_flow_says_its_step_and_a_stop_with_no_step_takes_the_step_on_its_line()
    {
        var c = Charts(extra: Add);
        var chart = c.Charts.Single(x => x.Flow == "click-add");
        Assert.Equal(["checked at step 3", "checked before the steps (no step number)"], chart.Scenarios.Where(r => r.Id.StartsWith('K')).Select(r => r.Steps));
        Assert.Contains(chart.Chart.Nodes, n => n.Shape == "redirect" && n.Label == "sends the user to Done (step 4)");
        Assert.Contains("stops at step 4 (`src/ItemController.cs:40`)", Page(c), StringComparison.Ordinal);
        Assert.Empty(Gate(c, Page(c)).Missing);
    }

    [Fact]
    public void No_empty_brackets_and_no_not_of_a_negation()
    {
        var g = Charts(extra: Add).Charts.Single(x => x.Flow == "click-add").Chart;
        Assert.Contains(g.Nodes, n => n.Label == "queues Job through RecurringJob.AddOrUpdate, which runs later, in the background (step 5)");
        Assert.Contains(g.Nodes, n => n.Label == "hasLicense? (step 2)");
        Assert.DoesNotContain(g.Nodes, n => n.Label.Contains("()", StringComparison.Ordinal) || n.Label.Contains("not: !", StringComparison.Ordinal) || n.Label.Contains("unless", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("!hasLicense", "hasLicense")]
    [InlineData("!_licenseService.HasActiveLicense(false, ct)", "_licenseService.HasActiveLicense(false, ct)")]
    [InlineData("!user?.token", "user?.token")]
    [InlineData("!(a && b)", "a && b")]
    [InlineData("!condition || (comparison != 1 && tags.Count == 0)", "not: (!condition || (comparison != 1 && tags.Count == 0))")]
    [InlineData("!(a) || (b)", "not: (!(a) || (b))")]
    [InlineData("!await svc.Has()", "not: (!await svc.Has())")]
    [InlineData("response == null", "not: response == null")]
    public void A_negated_simple_condition_reads_plainly_and_a_combined_one_keeps_not_around_it(string code, string other) =>
        Assert.Equal(other, PageChart.Not(code));

    [Fact]
    public void A_negated_if_reads_plainly_on_its_scenario_row()
    {
        var c = Charts(extra: () => Flow("click-boot", "click", "Boot", [
            Step(1, null, "call", "runs Boot", "src/Boot.cs:3", "Boot()"),
            Step(2, 1, "database", "saves Mapping", "src/Boot.cs:6", "m()", Cond("if", "src/Boot.cs:5", "!_updatedAfterStartup")),
        ]));
        Assert.Contains(c.Charts.Single(x => x.Flow == "click-boot").Scenarios, r => r.Scenario == "When `_updatedAfterStartup` is true");
    }

    [Fact]
    public void Gate_one_counts_a_save_in_shared_code_once_per_area_however_many_flows_reach_it()
    {
        var c = Charts(extra: () =>
        {
            Add();
            Flow("click-add-two", "click", "Add two", [Step(1, null, "http", "calls POST /api/two", "web/two.ts:3", "post()"), Step(2, 1, "call", "runs Store.Put", "src/Two.cs:4", "_store.Put(b)")],
                links: [Continues(2, "sh-store")]);
        });
        var page = Page(c);
        var both = Gate(c, page);
        Assert.Empty(both.Missing);
        // Each flow still has its own box to check: dropping one flow's box is caught.
        var two = c.Charts.Single(x => x.Flow == "click-add-two");
        Assert.Contains(two.Chart.Nodes, n => n.Label == "saves Blob (in shared code Store.Put) (step 2)");
        var miss = Assert.Single(Gate(c, page.Replace("saves Blob #40;in shared code Store.Put#41; #40;step 2#41;", "saves Blob", StringComparison.Ordinal)).Missing);
        Assert.Equal("click-add-two", (string)miss["flow"]!);
        // Counted once: the second flow changes nothing of its own, so the area counts as many changes as without it.
        var one = Charts(extra: Add);
        Assert.Equal(Gate(one, Page(one)).Changes, both.Changes);
    }

    [Fact]
    public void Gate_one_checks_saves_in_shared_code_and_only_change_boxes_count()
    {
        var c = Charts(extra: Add);
        var page = Page(c);
        Assert.Contains("saves Blob #40;in shared code Store.Put#41; #40;step 7#41;", page, StringComparison.Ordinal);
        var miss = Assert.Single(Gate(c, page.Replace("saves Blob #40;in shared code Store.Put#41; #40;step 7#41;", "saves Blob", StringComparison.Ordinal)).Missing);
        Assert.Equal(("click-add", 7, "src/Store.cs:7"), ((string)miss["flow"]!, (int)miss["step"]!, (string)miss["at"]!));
        // Above the cap, a change counts only from the list of changes: "skips steps 8, 9" in a row doesn't stand for it.
        var big = Charts(settings: new PageSettings(ChartCap: 5));
        var bigPage = Page(big);
        Assert.Contains("**Changes**\n\n- LookupProxy", bigPage, StringComparison.Ordinal);
        Assert.Contains("skips steps 8, 9", bigPage, StringComparison.Ordinal);
        var gone = Assert.Single(Gate(big, bigPage.Replace("- saves Order (step 8)\n", "", StringComparison.Ordinal)).Missing);
        Assert.Equal(("change", 8), ((string)gone["what"]!, (int)gone["step"]!));
    }

    [Fact]
    public void Gate_one_fails_a_start_flow_with_no_file_and_a_change_with_neither_step_nor_line()
    {
        var c = Charts(extra: () => Flow("click-blind", "click", "Blind", [Step(1, null, "http", "calls POST /x", "web/x.ts:1", "post()")],
            unknowns: [new JsonObject { ["at"] = "", ["code"] = "", ["point"] = "p", ["why"] = "dynamic dispatch" }]));
        var blind = Gate(c, Page(c)).Missing.Single();
        Assert.Equal(("click-blind", "change"), ((string)blind["flow"]!, (string)blind["what"]!));
        // A placed start flow whose file is gone: never drawn, and the gate says so.
        var gone = Charts(extra: () => Flow("click-gone", "click", "Gone", [Step(1, null, "database", "saves Gone", "src/G.cs:1", "g()")]));
        Assert.Empty(Gate(gone, Page(gone)).Missing);
        File.Delete(Path.Combine(root, "specs/areas/shop/click-gone.json"));
        var again = PageChart.Build(root, loaded!, Placed(), Area, "shop");
        Assert.Contains("click-gone", again.Missing);
        var miss = Assert.Single(Gate(again, Page(again)).Missing);
        Assert.Equal(("click-gone", "flow file"), ((string)miss["flow"]!, (string)miss["what"]!));
    }

    static JsonObject SameAs(JsonObject step, int n) { step["sameAs"] = n; return step; }

    static JsonObject Labelled(JsonObject step, string label) { step["label"] = label; return step; }

    [Fact]
    public void Pushes_file_writes_and_a_step_that_may_save_are_changes_on_the_page_and_gate_one_checks_them()
    {
        // D291, D292, D294: the page dropped every store step beginning "reads", and pushes and file writes were plain calls.
        var c = Charts(extra: () => Flow("click-login", "click", "Log in", [
            Labelled(Step(1, null, "database", "reads or saves the Identity user store through UserManager.AccessFailedAsync", "src/Token.cs:70", "a()"), "guessed"),
            Labelled(Step(2, null, "database", "saves the Identity user store through UserManager.RemoveAuthenticationTokenAsync", "src/Token.cs:71", "b()"), "guessed"),
            Step(3, null, "push", "pushes ReadingListUpdated to the browser", "src/List.cs:28", "c()"),
            Step(4, null, "file", "writes a file through Image.WriteToFile", "src/Image.cs:150", "d()"),
            Step(5, null, "database", "reads the Identity user store through UserManager.FindByNameAsync", "src/Token.cs:72", "e()"),
        ]));
        var boxes = c.Charts.Single(x => x.Flow == "click-login").Chart.Nodes.Where(n => n.Shape == "change").Select(n => n.Label).ToList();
        Assert.Equal([
            "may save the Identity user store through UserManager.AccessFailedAsync 🟡 (step 1)",
            "saves the Identity user store through UserManager.RemoveAuthenticationTokenAsync 🟡 (step 2)",
            "pushes ReadingListUpdated to the browser (step 3)",
            "writes a file through Image.WriteToFile (step 4)",
        ], boxes);
        var page = Page(c);
        Assert.Empty(Gate(c, page).Missing);
        var miss = Assert.Single(Gate(c, page.Replace("pushes ReadingListUpdated to the browser #40;step 3#41;", "pushes", StringComparison.Ordinal)).Missing);
        Assert.Equal(("change", 3), ((string)miss["what"]!, (int)miss["step"]!));
    }

    [Fact]
    public void Handled_errors_are_a_short_list_of_their_own_not_stops_and_gate_one_checks_them()
    {
        // D300: Sonarr's parser errors, caught per indexer in HttpIndexerBase.cs, were listed as stops.
        static JsonObject Handled(string what, string at, int step, string caught) =>
            new() { ["kind"] = "throw", ["what"] = what, ["point"] = "p", ["step"] = step, ["at"] = at, ["code"] = "throw", ["conditions"] = new JsonArray(), ["caughtBy"] = caught, ["caughtCode"] = "catch" };
        var c = Charts(extra: () => Flow("job-search", "job", "Search", [Step(1, null, "database", "saves History", "src/History.cs:3", "h()"), Step(2, null, "call", "runs Parser.Parse", "src/Parser.cs:20", "p()")],
            handled: [Handled("throws IndexerException", "src/Parser.cs:26", 2, "src/HttpIndexerBase.cs:252"), Handled("throws IndexerException", "src/Parser.cs:30", 2, "src/HttpIndexerBase.cs:252")]));
        var chart = c.Charts.Single(x => x.Flow == "job-search");
        var row = Assert.Single(chart.Handled);
        Assert.Equal("handled: IndexerException at `src/Parser.cs:26` (step 2), `src/Parser.cs:30` (step 2), caught by `src/HttpIndexerBase.cs:252`", row.Text);
        Assert.DoesNotContain(chart.Scenarios, r => r.Scenario.Contains("IndexerException", StringComparison.Ordinal));
        var page = Page(c);
        Assert.Contains("**Errors handled on the way (not stops)**", page, StringComparison.Ordinal);
        Assert.Empty(Gate(c, page).Missing);
        var miss = Gate(c, page.Replace(row.Text, "handled elsewhere", StringComparison.Ordinal)).Missing;
        Assert.Equal(2, miss.Count(m => (string)m!["what"]! == "handled"));
    }

    [Fact]
    public void A_repeated_call_carries_its_first_calls_changes_under_its_own_conditions()
    {
        // D296: Kavita's reading list saves under !dto.CoverImageLocked (steps 30–31) and again unconditionally (step 32, as in step 30).
        var locked = Cond("if", "src/ReadingListService.cs:121", "!dto.CoverImageLocked");
        var c = Charts(extra: () => Flow("click-list-save", "click", "Save list", [
            Step(1, null, "call", "calls ReadingListRepository.Update", "src/ReadingListService.cs:122", "Update(l)", locked),
            Step(2, 1, "database", "saves ReadingList", "src/ReadingListRepository.cs:40", "_db.Update(l)"),
            SameAs(Step(3, null, "call", "calls ReadingListRepository.Update", "src/ReadingListService.cs:125", "Update(l)"), 1),
        ]));
        c.Charts.Single(x => x.Flow == "click-list-save");
        var flow = c.Charts.Single(x => x.Flow == "click-list-save");
        var boxes = flow.Chart.Nodes.Where(n => n.Shape == "change").Select(n => n.Label).ToList();
        Assert.Equal(["saves ReadingList (step 2)", "saves ReadingList (step 3, as in step 2)"], boxes);
        // D297: its condition's row says it positively and when what it skips runs.
        var row = Assert.Single(flow.Scenarios, r => r.Id.StartsWith('D'));
        Assert.Equal("When `dto.CoverImageLocked` is true", row.Scenario);
        Assert.Equal("skips saves ReadingList (step 2), which run only when it is true", row.Ends);
        var page = Page(c);
        Assert.Empty(Gate(c, page).Missing);
        var miss = Assert.Single(Gate(c, page.Replace(" #40;step 3, as in step 2#41;", "", StringComparison.Ordinal)).Missing);
        Assert.Equal(("change", 3), ((string)miss["what"]!, (int)miss["step"]!));
        // The same call under the same conditions adds its step to the first box: nothing is lost, nothing grows.
        var same = Charts(extra: () => Flow("click-twice", "click", "Twice", [
            Step(1, null, "call", "calls Repo.Update", "src/S.cs:10", "Update()"), Step(2, 1, "database", "saves Thing", "src/Repo.cs:4", "_db.Update()"),
            SameAs(Step(3, null, "call", "calls Repo.Update", "src/S.cs:12", "Update()"), 1),
        ]));
        Assert.Equal(["saves Thing (steps 2, 3)"], same.Charts.Single(x => x.Flow == "click-twice").Chart.Nodes.Where(n => n.Shape == "change").Select(n => n.Label).ToList());
    }

    [Fact]
    public void Boxes_that_would_read_the_same_each_add_their_code_line()
    {
        // D297: nopCommerce's payment info chart had three boxes reading "saves GenericAttribute (steps 98, 105)".
        var c = Charts(extra: () =>
        {
            Flow("shared-ga", "shared", "GenericAttributeService.SaveAttributeAsync", [
                Step(1, null, "database", "saves GenericAttribute", "src/GenericAttributeService.cs:140", "Insert()"),
                Step(2, null, "database", "saves GenericAttribute", "src/GenericAttributeService.cs:150", "Update()"),
            ], placed: false);
            Flow("click-pay", "click", "Pay", [Step(1, null, "call", "calls GenericAttributeService.SaveAttributeAsync", "src/Checkout.cs:1189", "Save()")],
                links: [new JsonObject { ["kind"] = "continues", ["to"] = "x", ["flow"] = "shared-ga", ["step"] = 1, ["label"] = "certain", ["sentence"] = "Continues", ["at"] = "src/Checkout.cs:1189", ["code"] = "Save()" }]);
        });
        var boxes = c.Charts.Single(x => x.Flow == "click-pay").Chart.Nodes.Where(n => n.Shape == "change").Select(n => n.Label).ToList();
        Assert.Equal([
            "saves GenericAttribute (in shared code GenericAttributeService.SaveAttributeAsync) (step 1, `GenericAttributeService.cs:140`)",
            "saves GenericAttribute (in shared code GenericAttributeService.SaveAttributeAsync) (step 1, `GenericAttributeService.cs:150`)",
        ], boxes);
        Assert.Empty(Gate(c, Page(c)).Missing);
    }

    [Fact]
    public void Receivers_of_a_message_sent_in_shared_code_are_listed_and_gate_one_checks_every_receiver()
    {
        // D293: Sonarr's search job sends ProviderStatusChangedEvent in code 58 of 392 flows pass; its receivers never reached the page.
        var c = Charts(extra: () =>
        {
            Flow("job-search", "job", "Search", [Step(1, null, "database", "saves History", "src/History.cs:3", "h()"), Step(2, null, "message", "sends AuditLogged", LogLine, "Publish()")],
                links: [new JsonObject { ["kind"] = "message", ["to"] = "handler-health", ["flow"] = "handler-health", ["step"] = 2, ["label"] = "guessed", ["sentence"] = "Sends AuditLogged, handled by Health (x)", ["at"] = LogLine, ["code"] = "Publish()" }]);
            Flow("handler-health", "handler", "Health.Handle on AuditLogged", [Step(1, null, "call", "runs Check", "src/Health.cs:1", "c()")]);
        });
        var chart = c.Charts.Single(x => x.Flow == "job-search");
        Assert.Contains(chart.SharedChanges, r => r.Text == "sends AuditLogged");
        Assert.Contains(chart.FollowOns, r => r.Text.Contains("`handler-health`", StringComparison.Ordinal));
        var page = Page(c);
        Assert.Empty(Gate(c, page).Missing);
        var miss = Gate(c, page.Replace("`handler-health`", "a handler", StringComparison.Ordinal)).Missing;
        Assert.Contains(miss, m => (string)m!["what"]! == "receiver" && (string)m["text"]! == "handler-health");
    }

    [Fact]
    public void Gate_one_doesnt_count_as_in_step_n_and_matches_a_change_by_its_step_and_kind_word()
    {
        // D304(4): Kavita's "saves ReadingList (step 32, as in step 31)" must not stand for step 31's own box.
        const string box = "saves ReadingList #40;step 32, as in step 31#41;";
        Assert.True(PageGate.Found(32, "", "save", box));
        Assert.False(PageGate.Found(31, "", "save", box));
        Assert.False(PageGate.Found(32, "", "push", box));
        Assert.True(PageGate.Found(31, "", "save", box + "\nsaves ReadingList #40;step 31#41;"));
        Assert.True(PageGate.Found(7, "", null, "can't be followed #40;step 7#41;"));
    }

    [Fact]
    public void Receivers_of_a_message_sent_in_a_shared_flow_the_click_continues_into_are_listed_per_send()
    {
        // D304(6): Sonarr's grab click continues into ProviderStatusServiceBase.RecordSuccess, which sends ProviderStatusChangedEvent.
        var c = Charts(extra: () =>
        {
            Flow("click-grab", "click", "Grab", [Step(1, null, "database", "saves Grab", "src/Grab.cs:3", "g()"), Step(2, null, "call", "runs Status.RecordSuccess", "src/Grab.cs:4", "s()")],
                links: [new JsonObject { ["kind"] = "continues", ["to"] = "Status.RecordSuccess", ["flow"] = "shared-status", ["step"] = 2, ["label"] = "certain", ["sentence"] = "Continues", ["at"] = "src/Grab.cs:4", ["code"] = "s()" }]);
            Flow("shared-status", "shared", "Status.RecordSuccess", [Step(1, null, "message", "sends StatusChanged", "src/Status.cs:9", "Publish()")],
                links: [new JsonObject { ["kind"] = "message", ["to"] = "handler-health", ["flow"] = "handler-health", ["step"] = 1, ["label"] = "certain", ["sentence"] = "Sends StatusChanged, handled by Health (x)", ["at"] = "src/Status.cs:9", ["code"] = "Publish()" }],
                placed: false);
            Flow("handler-health", "handler", "Health.Handle on StatusChanged", [Step(1, null, "call", "runs Check", "src/Health.cs:1", "c()")], placed: false);
        });
        var chart = c.Charts.Single(x => x.Flow == "click-grab");
        var row = Assert.Single(chart.FollowOns);
        Assert.Equal("after sends StatusChanged (in shared code Status.RecordSuccess) (step 2): `handler-health`", row.Text);
        var page = Page(c);
        Assert.Empty(Gate(c, page).Missing);
        // D314(2): the page names the receiver by its name (no C# point here) with its id.
        Assert.Contains("(step 2): `Health.Handle on StatusChanged` (`handler-health`)\n", page, StringComparison.Ordinal);
        var miss = Gate(c, page.Replace("(step 2): `Health.Handle", "(step 1): `Health.Handle", StringComparison.Ordinal)).Missing;
        Assert.Contains(miss, m => (string)m!["what"]! == "receiver" && (string)m["text"]! == "handler-health" && (string)m["flow"]! == "click-grab");
    }

    [Fact]
    public void A_handled_error_says_only_what_is_known_never_carries_on()
    {
        // D304(7): "caught by X", then what the catch returns when the flow file knows it.
        JsonObject Handled(string at, string? returns)
        {
            var o = new JsonObject { ["kind"] = "throw", ["what"] = "throws X", ["point"] = "p", ["step"] = 1, ["at"] = at, ["code"] = "throw", ["conditions"] = new JsonArray(), ["caughtBy"] = "src/C.cs:9", ["caughtCode"] = "catch" };
            if (returns is not null) o["catchReturns"] = returns;
            return o;
        }
        var c = Charts(extra: () => Flow("click-try", "click", "Try", [Step(1, null, "database", "saves T", "src/T.cs:3", "t()")],
            handled: [Handled("src/T.cs:5", "View(…)")]));
        var row = Assert.Single(c.Charts.Single(x => x.Flow == "click-try").Handled);
        Assert.EndsWith("caught by `src/C.cs:9`, which then returns View(…)", row.Text, StringComparison.Ordinal);
        var plain = Charts(extra: () => Flow("click-try", "click", "Try", [Step(1, null, "database", "saves T", "src/T.cs:3", "t()")], handled: [Handled("src/T.cs:5", null)]));
        var text = Assert.Single(plain.Charts.Single(x => x.Flow == "click-try").Handled).Text;
        Assert.EndsWith("caught by `src/C.cs:9`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("carries on", Page(plain), StringComparison.Ordinal);
    }

    [Fact]
    public void A_redirect_after_the_saves_on_its_path_reads_as_the_ending_and_an_early_one_as_sent_elsewhere()
    {
        // D305(2): nopCommerce edit product's redirect to List when `ModelState.IsValid` and `!continueEditing` (ProductController.cs:1419)
        // sits after the saves in the code, though its step (the action's own, 2) comes before them.
        const string F = "src/EditController.cs";
        var valid = Cond("if", $"{F}:15", "ModelState.IsValid");
        var c = Charts(extra: () => Flow("click-edit", "click", "Edit", [
            Step(1, null, "http", "calls POST /edit", "web/edit.ts:3", "post()"),
            Step(2, 1, "call", "runs EditController.Edit", $"{F}:10", "public IActionResult Edit(Model model, bool continueEditing)"),
            Step(3, 2, "database", "reads Product", $"{F}:11", "_repo.Get(id)"),
            Step(4, 2, "database", "saves Product", $"{F}:20", "_repo.Update(product)", valid),
            Step(5, 2, "call", "calls EditController.SavePrices", $"{F}:22", "SavePrices(product)", valid),
            Step(6, 5, "database", "saves Price", $"{F}:90", "_repo.Insert(price)"),
            Step(7, 2, "database", "saves Draft", $"{F}:40", "_repo.Insert(draft)"),
        ], exits: [
            Exit("Redirect", "sends the user elsewhere (List)", $"{F}:12", 2, Cond("if", $"{F}:11", "product == null")),
            Exit("Redirect", "sends the user elsewhere (List)", $"{F}:30", 2, Cond("if", $"{F}:29", "!continueEditing"), valid),
        ]));
        var rows = c.Charts.Single(x => x.Flow == "click-edit").Scenarios.Where(r => r.Id.StartsWith('X')).ToList();
        Assert.Equal(("Sent elsewhere", "passes no change"), (rows[0].Scenario, rows[0].Steps));
        Assert.Equal(("Ends after the work: sends the user to List", "passes steps 4, 6"), (rows[1].Scenario, rows[1].Steps));
        Assert.Equal($"ends by sending the user to List, after the saves (steps 4, 6); stops at step 2 (`{F}:30`)", rows[1].Ends);
    }

    static JsonObject Into(int step, string flow, string at) =>
        new() { ["kind"] = "continues", ["to"] = flow, ["flow"] = flow, ["step"] = step, ["label"] = "certain", ["sentence"] = "Continues", ["at"] = at, ["code"] = "run()" };

    /// <summary>
    /// Three clicks of the area that each run Series.Refresh, a shared flow with two saves, an outside call and a stop, padded past
    /// D309's 30 steps with plain calls (<paramref name="pad"/> 0: a small helper of 3 steps).
    /// </summary>
    void Refresh() => Refresh(30);

    void Refresh(int pad)
    {
        Flow("shared-refresh", "shared", "SeriesService.Refresh", [
            Step(1, null, "database", "saves Series", "src/Refresh.cs:10", "_repo.Update(s)"),
            Step(2, null, "database", "saves Episode", "src/Refresh.cs:12", "_repo.Insert(e)"),
            Step(3, null, "outside-call", "calls outside the app: outside HTTP GET through IHttpClient: URL built at runtime", "src/Refresh.cs:14", "_http.Get(url)"),
            .. Enumerable.Range(4, pad).Select(i => (JsonNode)Step(i, null, "call", $"runs Helper{i}", $"src/Refresh.cs:{20 + i}", "h()")),
        ], exits: [Exit("throws SeriesNotFound", "src/Refresh.cs:5", 1, Cond("if", "src/Refresh.cs:4", "series == null"))], placed: false);
        foreach (var n in new[] { "a", "b", "c" })
            Flow($"click-refresh-{n}", "click", $"Refresh {n}", [
                Step(1, null, "database", $"saves Note{n}", $"src/{n}.cs:3", "_db.Insert(note)"),
                Step(2, null, "call", "runs SeriesService.Refresh", $"src/{n}.cs:5", "Refresh()"),
            ], links: [Into(2, "shared-refresh", $"src/{n}.cs:5")]);
    }

    [Fact]
    public void Shared_work_three_flows_run_is_shown_once_and_each_flow_has_one_line_into_it()
    {
        // D306: Sonarr's RefreshSeries and Kavita's bulk menu filled every click that reached them; now they move to one part.
        var c = Charts(extra: Refresh);
        var part = Assert.Single(c.Shared);
        Assert.Equal(("shared-refresh", 3, 2, 1), (part.Flow, part.Reach, part.Changes, part.Stops));
        const string box = "runs SeriesService.Refresh (see its part): 2 changes (saves Series, saves Episode), 1 stop";
        foreach (var n in new[] { "a", "b", "c" })
        {
            var labels = c.Charts.Single(x => x.Flow == $"click-refresh-{n}").Chart.Nodes.Where(x => x.Shape == "change").Select(x => x.Label).ToList();
            Assert.Equal([$"saves Note{n} (step 1)", $"{box} (step 2)"], labels);
        }
        var page = Page(c);
        Assert.Contains(PageChart.SharedHeading + "\n", page);
        Assert.Contains(PageChart.SharedMarker("shared-refresh") + "\n## Shared work: SeriesService.Refresh\n", page);
        Assert.Single(Regex.Matches(page, "<!-- shared shared-refresh -->"));
        Assert.DoesNotContain("in shared code SeriesService.Refresh", page);
        // The part has its own chart: its saves, its outside call and its stop, which no click page showed before.
        Assert.Contains("stops at step 1 (`src/Refresh.cs:5`)", page);
        var gate = Gate(c, page);
        Assert.Empty(gate.Missing);
        // Gate 1 checks the part's changes and stops, and each flow's line into it.
        var noStop = Gate(c, page.Replace("stops at step 1 (`src/Refresh.cs:5`)", "stops", StringComparison.Ordinal)).Missing;
        Assert.Contains(noStop, m => (string)m!["flow"]! == "shared-refresh" && (string)m["what"]! == "stop");
        var noPart = Gate(c, page.Replace(PageChart.SharedMarker("shared-refresh"), "<!-- gone -->", StringComparison.Ordinal)).Missing;
        Assert.Contains(noPart, m => (string)m!["flow"]! == "click-refresh-a" && (string)m["what"]! == "shared part");
        var noLine = Gate(c, page.Replace("see its part", "see below", StringComparison.Ordinal)).Missing;
        Assert.Contains(noLine, m => (string)m!["flow"]! == "click-refresh-b" && (string)m["what"]! == "change" && (int?)m["step"] == 2);
        Assert.Equal(page, Page(Charts(extra: Refresh)));
    }

    [Fact]
    public void Shared_work_two_flows_run_stays_inline_unless_it_is_long_and_stops_alone_move_nothing()
    {
        var two = Charts(extra: () =>
        {
            Refresh();
            index.RemoveAll(f => (string)f["id"]! == "click-refresh-c");
        });
        Assert.Empty(two.Shared);
        Assert.Contains(two.Charts.Single(x => x.Flow == "click-refresh-a").Chart.Nodes, n => n.Label == "saves Series (in shared code SeriesService.Refresh) (step 2)");
        // Over 150 steps: a part of its own however few flows run it.
        var lng = Charts(extra: () =>
        {
            Flow("shared-scan", "shared", "DiskScan.Scan", [.. Enumerable.Range(1, 151).Select(i => (JsonNode)Step(i, null, i == 151 ? "database" : "call", i == 151 ? "saves EpisodeFile" : $"runs Step{i}", $"src/Scan.cs:{i}", "x()"))], placed: false);
            Flow("click-scan", "click", "Scan", [Step(1, null, "call", "runs DiskScan.Scan", "src/S.cs:2", "Scan()")], links: [Into(1, "shared-scan", "src/S.cs:2")]);
        });
        Assert.Equal(("shared-scan", 1, 151), lng.Shared.Select(p => (p.Flow, p.Reach, p.StepCount)).Single());
        // D309: a click whose only work is a part is still charted, with one box for the part.
        Assert.Contains(lng.Charts.Single(x => x.Flow == "click-scan").Chart.Nodes, n => n.Label == "runs DiskScan.Scan (see its part): 1 change (saves EpisodeFile), 0 stops (step 1)");
        Assert.Empty(Gate(lng, Page(lng)).Missing);
        // A shared flow three flows run that only stops is not a part: no flow showed its stops, so nothing moves.
        var stops = Charts(extra: () =>
        {
            Flow("shared-guard", "shared", "Ensure.That", [Step(1, null, "call", "runs Check", "src/G.cs:1", "c()")], exits: [Exit("throws Bad", "src/G.cs:2", 1)], placed: false);
            foreach (var n in new[] { "a", "b", "c" })
                Flow($"click-g-{n}", "click", $"G {n}", [Step(1, null, "database", "saves X", $"src/g{n}.cs:1", "x()"), Step(2, null, "call", "runs Ensure.That", $"src/g{n}.cs:2", "e()")], links: [Into(2, "shared-guard", $"src/g{n}.cs:2")]);
        });
        Assert.Empty(stops.Shared);
        // D309: a small helper (30 steps or fewer) three flows run stays inline, under each click's own conditions.
        var small = Charts(extra: () => Refresh(0));
        Assert.Empty(small.Shared);
        Assert.Contains(small.Charts.Single(x => x.Flow == "click-refresh-a").Chart.Nodes, n => n.Label == "saves Series (in shared code SeriesService.Refresh) (step 2)");
    }

    [Fact]
    public void Start_flows_with_the_same_title_add_their_handler_then_their_line()
    {
        // D306(3): Sonarr's three "runs when SeriesAddedEvent is sent" handlers, told apart only by their ids.
        var c = Charts(extra: () =>
        {
            foreach (var (id, type, line) in new[] { ("handler-scene", "SceneMappingService", 319), ("handler-added", "SeriesAddedHandler", 20), ("handler-tracked", "TrackedDownloadService", 40) })
                Flow(id, "handler", $"{type}.Handle on SeriesAddedEvent", [Step(1, null, "database", "saves Thing", $"src/{type}.cs:{line + 2}", "x()")],
                    point: $"Core|M:App.{type}.Handle(App.SeriesAddedEvent)", startAt: $"src/{type}.cs:{line}");
            Flow("job-search-1", "job", "Search", [Step(1, null, "database", "saves A", "src/Search.cs:20", "x()")], point: "Core|M:App.SearchService.Execute(App.MissingCommand)", startAt: "src/Search.cs:17");
            Flow("job-search-2", "job", "Search", [Step(1, null, "database", "saves A", "src/Search.cs:20", "x()")], point: "Core|M:App.SearchService.Execute(App.CutoffCommand)", startAt: "src/Search.cs:17");
        });
        var titles = c.Charts.Select(x => x.Title).ToList();
        Assert.Contains("runs when handler is sent (SceneMappingService.Handle)", titles);
        Assert.Contains("runs when handler is sent (TrackedDownloadService.Handle)", titles);
        Assert.Contains("runs as a job (SearchService.Execute) `Search.cs:17` (MissingCommand)", titles);
        Assert.Contains("runs as a job (SearchService.Execute) `Search.cs:17` (CutoffCommand)", titles);
        Assert.Equal(titles.Count, titles.Distinct().Count());
        // The start box reads the same as the heading; a title that is already alone is left as it was.
        Assert.Contains(c.Charts.Single(x => x.Flow == "handler-scene").Chart.Nodes, n => n.Shape == "start" && n.Label == "runs when handler is sent (SceneMappingService.Handle)");
        Assert.Equal("starts with the click \"Place order\"", Order(c).Title);
        Assert.Equal("SceneMappingService.Handle", PageChart.Member("Core|M:App.Scene.SceneMappingService.Handle(App.SeriesAddedEvent)"));
        Assert.Equal("AddNewSeriesModalContent.handleAddSeriesPress", PageChart.Member("ts|web/AddNew.tsx#AddNewSeriesModalContent.handleAddSeriesPress"));
        Assert.Equal("List<Int32>, String", PageChart.Parameters("A|M:App.S.Run(System.Collections.Generic.List{System.Int32},System.String)"));
        Assert.Empty(Gate(c, Page(c)).Missing);
    }

    [Fact]
    public void The_same_change_at_the_same_line_made_directly_and_in_shared_code_is_one_line_with_all_its_steps()
    {
        // D306(4): Sonarr's Add listed "saves NamingConfig (step 70)" and "saves NamingConfig (in shared code RestController.TypedCreated) (step 137)".
        var c = Charts(extra: () =>
        {
            Flow("shared-created", "shared", "RestController.TypedCreated", [Step(1, null, "database", "saves AuditLog", LogLine, "_db.Insert(log)")], placed: false);
            Flow("click-add", "click", "Add", [
                Step(1, null, "database", "saves Series", "src/Add.cs:3", "x()"),
                Step(2, null, "database", "saves AuditLog", LogLine, "_db.Insert(log)"),
                Step(3, null, "call", "runs RestController.TypedCreated", "src/Add.cs:9", "Created()"),
            ], links: [Into(3, "shared-created", "src/Add.cs:9")]);
        });
        var row = Assert.Single(c.Charts.Single(x => x.Flow == "click-add").SharedChanges);
        Assert.Equal(("saves AuditLog", LogLine), (row.Text, row.At));
        Assert.Equal([2, 3], row.Steps);
        Assert.Contains($"- saves AuditLog (steps 2, 3) `{LogLine}`", Page(c));
        Assert.Empty(Gate(c, Page(c)).Missing);
    }

    [Fact]
    public void A_long_flows_rows_name_the_shorter_step_list_with_ranges_and_short_flows_keep_full_lists()
    {
        // D308: past 20 change steps on the main path, a row names whichever is shorter, passed or skipped, with ranges.
        var main = Enumerable.Range(1, 30).ToList();
        Assert.Equal("steps 2, 5–7, 9", PageChart.Ranges([9, 2, 5, 6, 7]));
        Assert.Equal("passes steps 1, 2", PageChart.PassesShort([1, 2], main));
        Assert.Equal("passes every main-path step except steps 29, 30", PageChart.PassesShort([.. main.Take(28)], main));
        Assert.Equal("passes every main-path step", PageChart.PassesShort(main, main));
        var small = Enumerable.Range(1, 10).ToList();
        Assert.Equal("passes steps 1, 2, 3, 4, 5, 6, 7, 8", PageChart.PassesShort([.. small.Take(8)], small));
    }

    /// <summary>
    /// D310 items 2 and 3, from Kavita's reading-list save: guards that throw before the saves. The title guard's stop row carries its
    /// condition; the name guard's doesn't; the cover guard's other side saves something.
    /// </summary>
    AreaCharts Guarded() => Charts(extra: () => Flow("click-save", "click", "Save list", [
        Step(1, null, "call", "runs Update", "src/ListService.cs:70", "Update(dto)"),
        Step(2, 1, "database", "saves ReadingList", "src/ListService.cs:95", "_db.Update(list)",
            Cond("unless", "src/ListService.cs:79", "string.IsNullOrEmpty(dto.Title)"), Cond("unless", "src/ListService.cs:81", "await Exists(dto.Title) && !same")),
        Step(3, 1, "database", "saves Cover", "src/ListService.cs:84", "_db.Update(cover)", Cond("if", "src/ListService.cs:83", "dto.Cover is null || dto.Locked")),
        Step(4, 1, "database", "saves Tag", "src/ListService.cs:99", "_db.Update(tag)", Cond("unless", "src/ListService.cs:83", "dto.Cover is null || dto.Locked")),
    ],
    exits: [Exit("throws ArgumentException", "src/ListService.cs:92", 1,
        Cond("unless", "src/ListService.cs:79", "string.IsNullOrEmpty(dto.Title)"), Cond("if", "src/ListService.cs:90", "list.Tags == null")),
        Exit("throws InvalidOperationException", "src/ListService.cs:85", 1, Cond("unless", "src/ListService.cs:83", "dto.Cover is null || dto.Locked"))]));

    [Fact]
    public void A_scenario_row_whose_other_side_is_only_a_stop_its_row_already_carries_is_dropped()
    {
        var chart = Guarded().Charts.Single(x => x.Flow == "click-save");
        var decides = chart.Scenarios.Where(r => r.Id.StartsWith('D')).Select(r => r.Decides).ToList();
        // The title guard: its other side throws, and the stop row "throws ArgumentException" lists it.
        Assert.DoesNotContain(decides, d => d.Contains("string.IsNullOrEmpty(dto.Title)", StringComparison.Ordinal));
        Assert.Contains(chart.Scenarios, r => r.Id.StartsWith('X') && r.Decides.Contains("string.IsNullOrEmpty(dto.Title)", StringComparison.Ordinal));
        // The name guard: no stop row carries it, so its row stays.
        Assert.Contains(chart.Scenarios, r => r.Scenario == "When `await Exists(dto.Title) && !same` is true");
        // The cover guard: a stop carries it, but its other side saves Cover, so its row stays.
        Assert.Contains(decides, d => d.StartsWith("`dto.Cover is null || dto.Locked`", StringComparison.Ordinal));
    }

    [Fact]
    public void A_diamond_reads_positively_with_its_lines_swapped_never_with_not()
    {
        var chart = Guarded().Charts.Single(x => x.Flow == "click-save");
        var g = chart.Chart;
        Assert.DoesNotContain(g.Nodes, n => n.Label.Contains("not:", StringComparison.Ordinal));
        // The name guard's diamond is its condition; the save is on its "no" line, and "yes" goes past it.
        var name = Assert.Single(g.Nodes, n => n.Shape == "diamond" && n.Label.StartsWith("await Exists(dto.Title) && !same?", StringComparison.Ordinal));
        var save = Assert.Single(g.Nodes, n => n.Label.StartsWith("saves ReadingList", StringComparison.Ordinal));
        bool Reaches(string from, string to, HashSet<string> seen) =>
            from == to || seen.Add(from) && g.Edges.Where(e => e.From == from).Any(e => Reaches(e.To, to, seen));
        var no = Assert.Single(g.Edges, e => e.From == name.Id && e.Label == "no");
        Assert.True(Reaches(no.To, save.Id, []));
        var yes = Assert.Single(g.Edges, e => e.From == name.Id && e.Label == "yes");
        Assert.False(Reaches(yes.To, save.Id, []));
        // A stop's diamond with several conditions says the guard's side as "is false".
        Assert.Contains(g.Nodes, n => n.Shape == "diamond" && n.Label.Contains("Title is not empty", StringComparison.Ordinal));
    }

    // ---- D311 ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_list_line_repeated_word_for_word_is_shown_once_with_every_step()
    {
        // Sonarr Series: the same part run at many steps, each a box of its own on the chart, one line in a list.
        const string Run = "runs Parse (see its part): 2 changes (saves NamingConfig, saves EpisodeFiles), 4 stops";
        var lines = PageChart.Once([
            (Run + " (steps 712, 759)", new BoxInfo("shared|" + Run, Run, [712, 759], "src/A.cs:1", false)),
            ("saves Series (step 730)", new BoxInfo("save|saves Series|src/S.cs:3", "saves Series", [730], "src/S.cs:3", false)),
            (Run + " (step 720, as in step 712)", new BoxInfo("shared|" + Run, Run, [720], "src/B.cs:9", false)),
            (Run + " (step 810, as in step 712)", new BoxInfo("shared|" + Run, Run, [810], "src/B.cs:9", false)),
            // The same words on another line stay apart (D288).
            ("saves Series (step 731)", new BoxInfo("save|saves Series|src/S.cs:9", "saves Series", [731], "src/S.cs:9", false)),
        ]);
        Assert.Equal([Run + " (steps 712, 720, 759, 810)", "saves Series (step 730)", "saves Series (step 731)"], lines);
        // Gate 1 still finds every step on the merged line with its kind word.
        Assert.True(PageGate.Found(810, "src/B.cs:9", "shared", lines[0]));
        Assert.True(PageGate.Found(720, "src/B.cs:9", "shared", lines[0]));
    }

    void SharedFlows()
    {
        void Linked(params string[] to) => index[^1]["links"] = new JsonArray([.. to.Select(t => (JsonNode)t)]);
        Flow("sh-where", "shared", "SqlBuilder.Where", [Step(1, null, "call", "runs SqlBuilder.AddClause", "src/Sql.cs:4", "AddClause()")], links: [Into(1, "sh-clause", "src/Sql.cs:4")], placed: false);
        Linked("sh-clause");
        Flow("sh-clause", "shared", "SqlBuilder.AddClause", [], placed: false);
        Flow("sh-join", "shared", "StringExtensions.Join", [Step(1, null, "package", "runs string.Join", "src/Str.cs:2", "string.Join()")], placed: false);
        Flow("sh-get", "shared", "SeriesService.GetSeries", [Step(1, null, "database", "reads Series", "src/Series.cs:5", "_db.Get()")], placed: false);
        Flow("sh-ensure", "shared", "Ensure.NotNull", [Step(1, null, "call", "runs Check", "src/Ensure.cs:1", "c()")], exits: [Exit("throws ArgumentNullException", "src/Ensure.cs:2", 1)], placed: false);
        Flow("sh-wrap", "shared", "Mapper.ToResource", [Step(1, null, "call", "runs SeriesService.GetSeries", "src/Map.cs:3", "Get()")], links: [Into(1, "sh-get", "src/Map.cs:3")], placed: false);
        Linked("sh-get");
        Flow("click-list", "click", "List", [Step(1, null, "database", "saves View", "src/List.cs:4", "_db.Insert(v)")]);
        Linked("sh-where", "sh-join", "sh-ensure", "sh-wrap");
    }

    [Fact]
    public void Shared_flows_that_change_stop_and_call_nothing_outside_are_counted_not_listed()
    {
        var c = Charts(extra: SharedFlows);
        Assert.Equal(["sh-clause", "sh-ensure", "sh-get", "sh-join", "sh-where", "sh-wrap"], c.Reached);
        // A read (and what runs one) or a stop keeps a shared flow listed; plain calls and package code all the way down don't.
        Assert.Equal(["sh-clause", "sh-join", "sh-where"], c.Quiet.Order(StringComparer.Ordinal));
        var flows = c.Reached.Select(id => loaded!.Flows[id]).ToList();
        var list = AreaPages.SharedList(flows, c.Quiet, f => $"- [{f.Name}](x.md) `{f.Id}`\n");
        Assert.StartsWith("\n## Shared flows (3 of 6)\n\nLeft out: 3 that change nothing, stop nothing and call nothing outside (for example `SqlBuilder.AddClause`, `StringExtensions.Join`).\n\n- [Ensure.NotNull]", list, StringComparison.Ordinal);
        Assert.DoesNotContain("`sh-where`", list, StringComparison.Ordinal);
        Assert.Equal(6, AreaPages.SharedList(flows, null, f => $"- [{f.Name}](x.md) `{f.Id}`\n").Count(ch => ch == '`') / 2);
        // Gate 1 accounts for each: listed, or quiet and counted.
        var page = Page(c) + list;
        var ok = Gate(c, page);
        Assert.Empty(ok.Missing);
        Assert.Equal(3, ok.SharedLeftOut);
        var dropped = Gate(c, page.Replace("- [Ensure.NotNull](x.md) `sh-ensure`\n", "", StringComparison.Ordinal));
        Assert.Contains(dropped.Missing, m => (string)m["flow"]! == "sh-ensure" && (string)m["what"]! == "shared flow");
        var miscounted = Gate(c, page.Replace("Left out: 3 that", "Left out: 2 that", StringComparison.Ordinal));
        Assert.Contains(miscounted.Missing, m => (string)m["what"]! == "shared flow");
    }

    static JsonObject At(JsonObject exit, string point) { exit["point"] = point; return exit; }

    static JsonObject[] FilterStops(int step) =>
    [
        At(Exit("throws ArgumentOutOfRangeException", "src/ListFilter.cs:28", step, new JsonObject { ["kind"] = "case", ["case"] = "_", ["at"] = "src/ListFilter.cs:28", ["code"] = "comparison" },
            Cond("unless", "src/ListFilter.cs:18", "string.IsNullOrEmpty(query) || !condition")), "Db|M:App.ListFilter.<G>$1.HasTitle(System.Boolean)"),
        At(Exit("throws ArgumentOutOfRangeException", "src/ListFilter.cs:53", step, new JsonObject { ["kind"] = "case", ["case"] = "_", ["at"] = "src/ListFilter.cs:53", ["code"] = "comparison" },
            Cond("if", "src/ListFilter.cs:34", "condition")), "Db|M:App.ListFilter.<G>$1.HasYear(System.Boolean)"),
        At(Exit("throws ArgumentOutOfRangeException", "src/ListFilter.cs:71", step, new JsonObject { ["kind"] = "case", ["case"] = "_", ["at"] = "src/ListFilter.cs:71", ["code"] = "comparison" },
            Cond("if", "src/ListFilter.cs:59", "condition")), "Db|M:App.ListFilter.<G>$1.HasCount(System.Boolean)"),
        At(Exit("throws ArgumentOutOfRangeException", "src/Repo.cs:602", step, new JsonObject { ["kind"] = "case", ["case"] = "_", ["at"] = "src/Repo.cs:602", ["code"] = "field" }), "Db|M:App.Repo.Build(System.Int32)"),
    ];

    [Fact]
    public void Stops_of_one_exception_kind_in_one_class_are_one_row_listing_their_lines()
    {
        var c = Charts(extra: () =>
        {
            Flow("click-filter", "click", "Filter", [Step(1, null, "database", "saves Filter", "src/F.cs:3", "_db.Insert(f)"), Step(2, 1, "call", "runs Repo.Build", "src/F.cs:5", "Build()")], exits: [.. FilterStops(2)]);
            Flow("click-browse", "click", "Browse", [Step(1, null, "database", "reads List", "src/B.cs:3", "_db.All()"), Step(2, 1, "call", "runs Repo.Build", "src/B.cs:5", "Build()")], exits: [.. FilterStops(2)]);
        });
        var chart = c.Charts.Single(x => x.Flow == "click-filter");
        var stops = chart.Scenarios.Where(r => r.Id.StartsWith('X')).ToList();
        Assert.Equal(2, stops.Count);   // the three in ListFilter are one row; Repo's stays its own
        var merged = stops.Single(r => r.Scenario.Contains("3 places in ListFilter", StringComparison.Ordinal));
        Assert.Equal("when `comparison` matches no case, at `ListFilter.cs:28` (when not: string.IsNullOrEmpty(query) || !condition); `:53`, `:71` (when condition)",
            merged.Decides);
        Assert.Contains("stops at step 2 (`src/ListFilter.cs:28`), stops at step 2 (`src/ListFilter.cs:53`), stops at step 2 (`src/ListFilter.cs:71`)", merged.Ends, StringComparison.Ordinal);
        // The one-line flow's sub-bullets merge the same way.
        var d = c.DisplayOnly.Single(x => x.Flow == "click-browse");
        Assert.Equal(2, d.Stops.Count);
        Assert.StartsWith("throws ArgumentOutOfRangeException (3 places in ListFilter): when `comparison` matches no case, at `ListFilter.cs:28`", d.Stops.Single(r => r.Stops.Count == 3).Text, StringComparison.Ordinal);
        Assert.Empty(Gate(c, Page(c)).Missing);
    }

    [Theory]
    [InlineData("string.IsNullOrEmpty(dto.Title)", false, "Title is empty")]
    [InlineData("string.IsNullOrWhiteSpace(name)", false, "name is empty")]
    [InlineData("!string.IsNullOrEmpty(dto.Title)", false, "Title is not empty")]
    [InlineData("string.IsNullOrEmpty(dto.Title)", true, "Title is not empty")]
    [InlineData("series == null", false, "no series")]
    [InlineData("series is null", true, "has series")]
    [InlineData("data.filterV2 === undefined", false, "no filterV2")]
    [InlineData("dto.Cover != null", false, "has Cover")]
    [InlineData("!items.Any()", false, "items is empty")]
    [InlineData("items.Count == 0", false, "items is empty")]
    [InlineData("list.length === 0", true, "list has items")]
    [InlineData("importDecisions.Any()", false, "importDecisions has items")]
    [InlineData("files.Count > 0", false, "files has items")]
    [InlineData("readingList.IsLocked", false, "IsLocked")]
    [InlineData("!readingList.IsLocked", false, "not IsLocked")]
    [InlineData("(series == null)", false, "no series")]
    public void Common_condition_shapes_are_phrased(string code, bool negate, string said) => Assert.Equal(said, PageChart.Phrase(code, negate));

    [Theory]
    [InlineData("a == b")]
    [InlineData("files.Count > 100")]
    [InlineData("isLocked")]
    [InlineData("x == null || y")]
    [InlineData("GetUser() == null")]
    [InlineData("_disk.FolderExists(path)")]
    [InlineData("dto.Locked")]
    public void Other_condition_shapes_stay_raw(string code) => Assert.Null(PageChart.Phrase(code));

    [Fact]
    public void A_phrased_condition_keeps_its_raw_code_beside_it_and_its_diamond_stays_positive()
    {
        var c = Charts();
        var order = Order(c);
        // The diamond is the phrase; its row keeps the raw code and line, and says the other side in words.
        var row = order.Scenarios.Single(r => r.Id == "D6");
        Assert.Equal(("When has coupon", "`coupon == null` (`src/OrderService.cs:44`)"), (row.Scenario, row.Decides));
        Assert.EndsWith("which run only when no coupon", row.Ends, StringComparison.Ordinal);
        Assert.Contains("throws BadRequest when no body (`body == null`)", PageChart.FlowMarkdown(order, c), StringComparison.Ordinal);
        Assert.Contains("D6{\"no coupon? #40;step 11#41;\"}", PageChart.Mermaid(order.Chart), StringComparison.Ordinal);
    }
    [Fact]
    public void A_change_made_only_under_a_condition_says_so_on_the_start_here_line()
    {
        // D314(3): Sonarr's Monitored toggle "queues MoveSeriesCommand" only when `queueMove` (`SeriesService.cs:336`).
        var c = Charts(extra: () =>
        {
            var move = Cond("if", "src/SeriesService.cs:336", "queueMove");
            Flow("click-monitor", "click", "Monitored", [
                Step(1, null, "database", "saves Series", "src/SeriesService.cs:330", "_repo.Update(s)"),
                Step(2, null, "queue", "queues MoveSeriesCommand", "src/SeriesService.cs:337", "Push(new MoveSeriesCommand())", move),
                // A flow file lists a step's conditions innermost first: `edited` is the nearest.
                Step(3, null, "message", "sends SeriesEditedEvent", "src/SeriesService.cs:340", "Publish(e)", Cond("if", "src/SeriesService.cs:339", "edited"), Cond("if", "src/SeriesService.cs:338", "series.IsDirty")),
            ]);
        });
        var page = Page(c);
        Assert.Contains("  - changes: saves Series, queues MoveSeriesCommand (only when `queueMove`, `SeriesService.cs:336`) · sends `SeriesEditedEvent` (only when `edited`, `SeriesService.cs:339`, …)\n", page, StringComparison.Ordinal);
        // Place order: a save under a phrased condition; the unconditional one stays plain.
        Assert.Contains("saves Order (only when `dto.Total > 0`, `OrderService.cs:29`)", page, StringComparison.Ordinal);
        Assert.Contains("saves Order through IOrderService.Update,", page, StringComparison.Ordinal);
        Assert.Empty(Gate(c, page).Missing);
        Assert.Equal("`ready` is false", PageChart.OnlyIf(new SummaryCondition("a:1", "!ready", "if", null, "")));
        Assert.Equal("`ready`", PageChart.OnlyIf(new SummaryCondition("a:1", "!ready", "unless", null, "")));
        Assert.Equal("no series", PageChart.OnlyIf(new SummaryCondition("a:1", "series == null", "if", null, "")));
    }

    [Fact]
    public void Every_receiver_is_named_by_class_and_method_with_a_link_and_a_long_list_goes_on_sub_bullets()
    {
        // D314(2): Sonarr's delete sends SeriesDeletedEvent to 18 handlers; each is named, none cut.
        var receivers = Enumerable.Range(1, 14).Select(i => $"handler-h{i:00}").ToList();
        var c = Charts(extra: () =>
        {
            Flow("click-delete", "click", "Delete", [Step(1, null, "database", "saves Series", "src/SeriesService.cs:10", "_repo.Delete(s)"), Step(2, null, "message", "sends SeriesDeletedEvent", "src/SeriesService.cs:11", "Publish(e)")],
                links: [.. receivers.Select(r => (JsonNode)new JsonObject { ["kind"] = "message", ["to"] = r, ["flow"] = r, ["step"] = 2, ["label"] = "certain", ["sentence"] = "Sends SeriesDeletedEvent, handled by x", ["at"] = "src/SeriesService.cs:11", ["code"] = "Publish(e)" })]);
            foreach (var r in receivers)
                Flow(r, "handler", $"H{r[^2..]}Handler.Handle on Ns.Events.SeriesDeletedEvent", [Step(1, null, "call", "runs x", "src/H.cs:1", "x()")], placed: false,
                    point: $"Api|M:Api.Tv.H{r[^2..]}Handler.Handle(Api.SeriesDeletedEvent)");
        });
        var page = Page(c);
        var part = page[page.IndexOf(PageChart.FlowMarker("click-delete"), StringComparison.Ordinal)..];
        Assert.Contains("- after sends SeriesDeletedEvent (step 2), 14 flows:\n  - `H01Handler.Handle` (`handler-h01`)\n", part, StringComparison.Ordinal);
        foreach (var r in receivers) Assert.Contains($"  - `H{r[^2..]}Handler.Handle` (`{r}`)\n", part, StringComparison.Ordinal);
        Assert.DoesNotContain("…", part[..part.IndexOf("\n\n**", part.IndexOf("**What runs afterwards", StringComparison.Ordinal), StringComparison.Ordinal)], StringComparison.Ordinal);
        // Gate 1 reads a sub-bullet's step from the line it sits under: moved, every receiver is missing.
        Assert.Empty(Gate(c, page).Missing);
        var moved = Gate(c, page.Replace("- after sends SeriesDeletedEvent (step 2), 14 flows:", "- after sends SeriesDeletedEvent (step 1), 14 flows:", StringComparison.Ordinal)).Missing;
        Assert.Equal(14, moved.Count(m => (string)m!["what"]! == "receiver"));
        // A short list stays on its line, each named.
        Assert.Contains("- after sends OrderPlaced (step 9): `MailHandler.Handle on OrderPlaced` (`handler-mail`)\n", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_field_checks_in_several_flows_are_written_once_under_the_first()
    {
        // D314(4): Sonarr's K1–K8 validator rows repeated in every series flow.
        JsonArray Checks() => [Check("OrderDto.Qty: GreaterThan(0)", "Api|P:Api.OrderDto.Qty", "src/OrderValidator.cs:5", "GreaterThan"), Check("OrderDto.Email: NotEmpty", "Api|P:Api.OrderDto.Email", "src/OrderValidator.cs:6")];
        var c = Charts(extra: () =>
        {
            Flow("click-reorder", "click", "Reorder", [Step(1, null, "database", "saves Order", "src/OrderService.cs:80", "_repo.Insert(o)")], checks: Checks());
            Flow("click-quick", "click", "Quick order", [Step(1, null, "database", "saves Order", "src/OrderService.cs:90", "_repo.Insert(o)")], checks: Checks());
            Flow("click-odd", "click", "Odd order", [Step(1, null, "database", "saves Order", "src/OrderService.cs:95", "_repo.Insert(o)")], checks: [Check("OrderDto.Qty: GreaterThan(0)", "Api|P:Api.OrderDto.Qty", "src/OrderValidator.cs:5", "GreaterThan")]);
        });
        var page = Page(c);
        var first = c.Charts.First(x => x.Flow is "click-reorder" or "click-quick" or "click-place-order");
        var firstPart = PageChart.FlowMarkdown(first, c);
        Assert.Contains("| K1 | OrderDto.Qty fails a check |", firstPart, StringComparison.Ordinal);
        Assert.Null(first.SameChecksAs);
        foreach (var later in c.Charts.Where(x => x.Flow is "click-reorder" or "click-quick" or "click-place-order" && x != first))
        {
            Assert.Equal((first.Title, first.Flow), later.SameChecksAs);
            var md = PageChart.FlowMarkdown(later, c);
            Assert.DoesNotContain("| K2 |", md, StringComparison.Ordinal);
            Assert.Contains($"| K1–K2 | same checks as [{first.Title.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal)}](#{PageChart.Anchor(first.Title)}) (K1–K2), `{first.Flow}` | 2 fields, as written there |", md, StringComparison.Ordinal);
        }
        // A different set is written out.
        var odd = c.Charts.Single(x => x.Flow == "click-odd");
        Assert.Null(odd.SameChecksAs);
        Assert.Contains("| K1 | OrderDto.Qty fails a check |", PageChart.FlowMarkdown(odd, c), StringComparison.Ordinal);
        Assert.Equal("starts-with-the-click-place-order", PageChart.Anchor("starts with the click \"Place order\""));
        Assert.Empty(Gate(c, page).Missing);
        // One check row alone stays written out, even when an earlier flow has the same one.
        var single = Charts(extra: () =>
        {
            JsonArray One() => [Check("OrderDto.Note: NotEmpty", "Api|P:Api.OrderDto.Note", "src/OrderValidator.cs:9")];
            Flow("click-n1", "click", "Note one", [Step(1, null, "database", "saves Note", "src/NoteService.cs:3", "_repo.Insert(n)")], checks: One());
            Flow("click-n2", "click", "Note two", [Step(1, null, "database", "saves Note", "src/NoteService.cs:4", "_repo.Insert(n)")], checks: One());
        });
        Assert.All(single.Charts.Where(x => x.Flow is "click-n1" or "click-n2"), x => Assert.Null(x.SameChecksAs));
    }
}

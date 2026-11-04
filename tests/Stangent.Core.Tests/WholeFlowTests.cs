using System.Diagnostics;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Spec.Fsd;
using Stangent.Spec.Trace;

namespace Stangent.Core.Tests;

/// <summary>
/// One run of the whole flow (S.0), so a seam between phases can't pass every phase's own tests and still break. It runs the
/// built <c>stangent</c> on a real git repo holding the shop fixture, with the real graph builder. Every AI step's answer is a
/// hand-written file playing that step's agent, and the code checks still run on it. The run happens once; each stage's facts
/// read what was seen at that stage. No test here reaches an AI.
/// </summary>
public sealed class WholeFlow : IDisposable
{
    public const string Post = "endpoint:Shop.Web|POST /api/orders";
    public const string Checkout = "endpoint:Shop.Web|ANY /checkout";
    public const string Legacy = "endpoint:Shop.Web|POST /api/legacy/place";
    public const string Orders = "page:/orders";
    public const string Stock = "page:/stock/:id";
    public const string CreateOrder = "Shop.Web|M:Shop.Web.Endpoints.CreateOrder(Shop.Core.IOrderService,Microsoft.Extensions.Configuration.IConfiguration)";
    public const string PlaceOrder = "Shop.Core|M:Shop.Core.OrderService.PlaceOrder(System.String,System.Decimal)";
    public const string Publish = "Shop.Core|M:Shop.Core.OrderService.Publish(System.Object)";
    public const string Handler = "Shop.Core|M:Shop.Core.SendReceiptOnOrderPlaced.HandleAsync(Shop.Core.OrderPlaced)";
    public const string Submit = "ts|Shop.Web/ClientApp/src/app/order-form.component.ts#OrderFormComponent.submit";
    public const string StockPage = "ts|Shop.Web/react-app/src/StockPage.tsx#StockPage";
    public const string CheckoutIndex = "Shop.Web|M:Shop.Web.Controllers.CheckoutController.Index";
    public const string LegacyPlace = "Shop.Web|M:Shop.Web.Controllers.LegacyOrdersController.Place";

    public const string Person = "Pat Person";
    public const string FsdAgree = "The orders screen has a Place button.";
    public const string FsdConflict = "Orders over 50 need approval.";
    public const string FsdGap = "Every order sends a receipt by post.";
    public const string FsdReceipt = "Placing an order sends a receipt.";
    public const string FsdBack = "The customer gets the order back.";
    public const string FsdStock = "The stock page shows the stock count for one item.";
    public const string FsdCheckout = "Checkout goes through the checkout screen.";
    public const string FsdLegacy = "Old clients place orders through the legacy route.";

    public const string Rule = "Every order is placed with a total of 1.";
    public const string Effect = "Placing an order publishes OrderPlaced, which sends a receipt.";
    public const string Rephrased = "Placing an order publishes OrderPlaced, which sends a receipt to the customer.";
    public const string Returned = "The endpoint returns the placed order.";

    readonly string archive = Directory.CreateTempSubdirectory("stangent-flow-archive-").FullName;

    public string Root { get; } = ShopCopy.Create();
    public string? Broke { get; private set; }
    public int BrokeAt { get; private set; } = int.MaxValue;
    public List<string> SchemaErrors { get; } = [];
    public Dictionary<string, JsonNode?> Saw { get; } = [];

    readonly string? extraConfig;
    readonly bool allOn;

    public WholeFlow() : this(null) { }

    /// <summary>The flow with these settings added, as far as what's left on can take it (D121): the rest of the stages need findings to act on.</summary>
    public static WholeFlow With(string extraConfig) => new(extraConfig);

    public const string SwitchedOn = "components: { table-facts: true, judge-finer-verdicts: true }";

    /// <summary>The flow with what's built switched off turned on (D124): the fake AI answers each variant, and nothing real runs.</summary>
    public static WholeFlow AllSwitchedOn() => new(SwitchedOn, allOn: true);

    /// <summary>A command as a person runs it, envelope and all.</summary>
    public JsonNode Cli(params string[] args) => Run(args).Envelope;

    WholeFlow(string? extraConfig, bool allOn = false)
    {
        this.extraConfig = extraConfig;
        this.allOn = allOn;
        Action[] stages = allOn ? [SetUp, ReferencePages, Import, ComparedTables, LinksOn] : extraConfig is null
            ? [SetUp, ReferencePages, Import, LinksAndFindings, Disprove, PersonDecides, Impact, NewVersion, Regenerate, WholeResult, ExcelAndEmbeddedFiles]
            : [SetUp, ReferencePages, Import, LinksAndFindings, Impact];
        for (var i = 0; i < stages.Length; i++)
        {
            try
            {
                stages[i]();
                Sweep(i + 1);
            }
            catch (Exception e)
            {
                (BrokeAt, Broke) = (i + 1, $"stage {i + 1} ({stages[i].Method.Name}): {e.Message}");
                return;
            }
        }
    }

    public void Dispose()
    {
        Directory.Delete(Root, true);
        Directory.Delete(archive, true);
    }

    /// <summary>A stage's facts need the run to have got through it.</summary>
    public void Reached(int stage) => Assert.True(stage < BrokeAt, Broke);

    public JsonNode Seen(string key) => Saw[key] ?? throw new InvalidOperationException($"nothing saved as {key}");

    /// <summary>The entry that quotes these FSD words, in the trace file saved under <paramref name="key"/>.</summary>
    public JsonObject EntryIn(string key, string fsdQuote) => Find(Seen(key), fsdQuote)
        ?? throw new InvalidOperationException($"no entry quoting '{fsdQuote}' in {key}");

    static JsonObject? Find(JsonNode trace, string fsdQuote) =>
        trace["entries"]!.AsArray().OfType<JsonObject>().FirstOrDefault(e => (string?)e["fsd"]?["quote"] == fsdQuote);

    public JsonNode? Listing(string key, string finding) => Seen(key)["findings"]!.AsArray().FirstOrDefault(f => (string?)f!["id"] == finding);

    public static string TypedHeadings(params string[] titles) => string.Concat(titles.Select(t => H(t) + P("Some words in this section.")));

    public static IEnumerable<string> ProblemLines(JsonNode? report) => report?["problems"]?.AsArray().Select(p => (string)p!) ?? [];

    // ---- the stages ----

    void SetUp()
    {
        Append(".gitignore", "bin/\nobj/\n");   // as a real repo has; the fixture's graph build restores packages
        var yaml = File.ReadAllText(Path.Combine(Root, "stangent.yaml")).TrimEnd() + $"\n{FsdConfig(disprove: false)}\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n  aiPoints: false\n";
        if (extraConfig is not null) yaml = yaml.TrimEnd() + $"\n{extraConfig}\n";
        File.WriteAllText(Path.Combine(Root, "stangent.yaml"), yaml);
        if (allOn)
        {
            File.WriteAllText(Path.Combine(Root, "Shop.Core", "Status.cs"), "namespace Shop.Core;\n\npublic enum OrderStatus\n{\n    Open,\n    Closed = 5,\n}\n");
            File.WriteAllText(Path.Combine(Root, "Shop.Web", "ClientApp", "src", "app", "status.ts"), "export enum ScreenStatus {\n  Shown,\n  Hidden = 7,\n}\n");
        }
        Saw["init"] = Ok("init");
        Git("config", "user.name", Person);
        Git("config", "user.email", "pat@example.invalid");
        Commit("set up");
        Saw["graph"] = Ok("graph", "build");
    }

    string FsdConfig(bool disprove) => $"fsd: {{ archive: {{ name: docs, path: '{archive}' }}, findings: true, disprove: {(disprove ? "true" : "false")} }}";

    void ReferencePages()
    {
        Saw["post.prepare"] = Ok("spec", "prepare", Post);
        var post = Record(Post, WriterPost(Returned, Effect, null));
        Saw["post.recorded"] = post;
        Record(Orders, Writer("Orders", actions: [S("The Place button submits the order.", Submit)]));
        Record(Stock, Writer("Stock", shows: [S("The screen shows a Stock button.", StockPage)]));
        Record(Legacy, Writer("Legacy place", actions: [S("A caller places an order through the legacy route.", LegacyPlace)]));
        // The verifier doubts one statement; the writer's fix round rewords it, and it keeps its id.
        Answer(Ok("spec", "prepare", Checkout), Writer("Checkout", actions: [S("The checkout route places an order for a shop with a total of 2.", CheckoutIndex)]));
        Answer(Ok("spec", "check", Checkout), new JsonObject { ["findings"] = new JsonArray(new JsonObject { ["statement"] = "A1", ["kind"] = "unsupported", ["explanation"] = "The code says web, not a shop." }) });
        var fix = Ok("spec", "check", Checkout);
        Saw["checkout.fix"] = fix;
        var fixedPage = Writer("Checkout", actions: [S("The checkout route places an order for web with a total of 2.", CheckoutIndex)]);
        fixedPage["resolutions"] = new JsonArray(new JsonObject { ["finding"] = "F1", ["outcome"] = "fixed", ["reason"] = "It says web now." });
        Answer(fix, fixedPage);
        Saw["checkout.recorded"] = Ok("spec", "check", Checkout);
        Saw["post.page"] = PageJson(Post);
        Saw["post.lines"] = PageFile(Post, ".lines.json");
        Saw["post.ids"] = PageFile(Post, ".ids.json");
        Saw["checkout.page"] = PageJson(Checkout);
    }

    void Import()
    {
        var file = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(file, Version1());
        Saw["import"] = Ok("fsd", "import", file, "--id", "shop");
        Saw["document"] = Document("shop");

        using var pdf = new PdfFixture();
        pdf.Page("1 Refunds", "Refunds are paid within five days.");
        pdf.Page("2 Returns", "Returns need a receipt.");
        var pdfFile = Path.Combine(Root, "terms.pdf");
        File.WriteAllBytes(pdfFile, pdf.Build(PdfFixture.Mark("1 Refunds", 0, 1), PdfFixture.Mark("2 Returns", 0, 2)));
        Saw["import.pdf"] = Ok("fsd", "import", pdfFile, "--id", "terms");
        Saw["document.pdf"] = Document("terms");
        // Originals go to the archive, as they do for real: they don't stay in the repo.
        File.Delete(file);
        File.Delete(pdfFile);
    }

    void LinksAndFindings()
    {
        Saw["candidates"] = Ok("fsd", "candidates");

        var step = Ok("fsd", "prepare", "shop", "1");
        Saw["judge.prepare"] = step;
        Saw["judge.bundle"] = File.ReadAllText(Path.Combine(Root, Path.GetDirectoryName((string)step["next"]!["output"]!)!, "judge.md"));
        // First a made-up quote: code refuses it and gives one retry.
        Answer(step, Links(
            Link("conflict", "Orders over 75 need approval.", Post, "R1", "Every order is placed", "FSD 75, code 1")));
        var retry = Run("fsd", "check", "shop", "1");
        Saw["judge.retry"] = retry.Envelope;
        Answer(retry.Envelope["result"]!, Links(
            Link("agree", FsdAgree, Orders, "A1", "The Place button submits the order."),
            Link("agree", FsdReceipt, Post, "E1", "which sends a receipt"),
            Link("agree", FsdBack, Post, "S1", "returns the placed order"),
            Link("conflict", FsdConflict, Post, "R1", "Every order is placed with a total of 1.", "FSD says over 50 need approval; the code has no such rule."),
            Link("gap", FsdGap, Post, null, null, "No statement says how a receipt is delivered.")));
        step = Ok("fsd", "check", "shop", "1");
        Saw["judge.confirm"] = step;
        Answer(step, new JsonObject { ["checks"] = new JsonArray([.. new[] { "K1", "K2", "K3" }.Select(k => (JsonNode)new JsonObject { ["link"] = k, ["verdict"] = "confirm", ["note"] = "same" })]) });
        Saw["judge.recorded"] = Ok("fsd", "check", "shop", "1");

        Judge("2", Links(Link("conflict", FsdStock, Stock, "S1", "The screen shows a Stock button.", "A button, not a count.")));
        Judge("3", Links(Link("conflict", FsdCheckout, Checkout, "A1", "The checkout route places an order", "No screen, a route.")));
        step = Judge("4", Links(Link("agree", FsdLegacy, Legacy, "A1", "A caller places an order through the legacy route.")));
        Answer(step, new JsonObject { ["checks"] = new JsonArray(new JsonObject { ["link"] = "K1", ["verdict"] = "confirm", ["note"] = "same" }) });
        Ok("fsd", "check", "shop", "4");

        Saw["check.links"] = Check();
        Saw["trace.links"] = Trace();
    }

    /// <summary>The judge's first answer for a section; returns the check's next step.</summary>
    JsonNode Judge(string section, JsonObject answer)
    {
        Answer(Ok("fsd", "prepare", "shop", section), answer);
        return Ok("fsd", "check", "shop", section);
    }

    void Disprove()
    {
        Saw["runs.before-disprove"] = Runs();
        File.WriteAllText(Path.Combine(Root, "stangent.yaml"), File.ReadAllText(Path.Combine(Root, "stangent.yaml")).Replace(FsdConfig(false), FsdConfig(true)));
        Commit("turn the disprove step on");

        Saw["rejudge"] = Ok("fsd", "prepare", "shop", "3");     // in no link's input hash: nothing is judged again

        const string created = "public Order PlaceOrder(string customer, decimal total)";
        var conflict = Quoted("stands", "Shop.Core/Orders.cs", 28, created);
        var gap = Quoted("page-gap", "Shop.Core/Orders.cs", 31, "Publish(new OrderPlaced(order.Id));");
        Saw["disprove.bundle"] = AnswerFindings("1", gap, conflict);
        AnswerFindings("2", Quoted("refuted", "Shop.Web/react-app/src/StockPage.tsx", 3, "return <button onClick={load}>Stock</button>;"));
        AnswerFindings("3", Quoted("refuted", "Shop.Web/Controllers/CheckoutController.cs", 8, "public IActionResult Index() => Content(orders.PlaceOrder(\"web\", 2m).Customer);"));
        Saw["disprove.none"] = Ok("fsd", "disprove", "prepare", "shop", "1");

        Saw["check.disprove"] = Check();
        Saw["trace.disprove"] = Trace();

        var refuted = Find(Trace(), FsdStock)!;
        var sentBack = Find(Trace(), FsdCheckout)!;
        Saw["confirm"] = Ok("trace", "confirm", (string)refuted["finding"]!, "--why", "The button line is what the FSD asks for.");
        Saw["send-back"] = Ok("trace", "send-back", (string)sentBack["finding"]!, "--why", "The route isn't a screen; the BA should read this.");
        Saw["check.reviewed"] = Check();
        Saw["trace.reviewed"] = Trace();
        Saw["history.confirm"] = Ok("state", "history", "finding-review", (string)refuted["id"]!);
        Saw["history.send-back"] = Ok("state", "history", "finding-review", (string)sentBack["id"]!);
        Saw["history.verdict"] = Ok("state", "history", "finding-verdict", (string)refuted["id"]!);
    }

    /// <summary>Answers a section's findings, in the order disprove asks, by which FSD sentence each bundle holds; returns the first bundle.</summary>
    string AnswerFindings(string section, params JsonObject[] answers)
    {
        string? first = null;
        for (var i = 0; i < answers.Length; i++)
        {
            var step = Ok("fsd", "disprove", "prepare", "shop", section);
            if (step["next"] is null) throw new InvalidOperationException($"section {section}, answer {i}: {step.ToJsonString()}");
            first ??= File.ReadAllText(Path.Combine(Root, Path.GetDirectoryName((string)step["next"]!["output"]!)!, "bundle.md"));
            Answer(step, answers[i]);
            Ok("fsd", "disprove", "check", "shop", section);
        }
        return first!;
    }

    void PersonDecides()
    {
        Commit("the links and findings");
        Saw["git.tracked"] = Git("ls-files", ".stangent");
        var before = Git("rev-parse", "HEAD");
        Saw["approve.1"] = Ok("trace", "approve", "shop", "1");
        Saw["approve.4"] = Ok("trace", "approve", "shop", "4");
        // A person edits the file in a PR, only the fields they own.
        Edit(trace =>
        {
            Find(trace, FsdConflict)!["decision"] = "code-wrong";
            Find(trace, FsdConflict)!["severity"] = "major";
            Find(trace, FsdGap)!["decision"] = "page-wrong";
            Find(trace, FsdGap)!["severity"] = "minor";
        });
        Commit("the BA's decisions");
        Saw["check.in-pr"] = Check("--base", before);
        Saw["attribute"] = Ok("trace", "attribute", "--base", before, "--by", Git("config", "user.name"));
        Commit("after the merge");
        Saw["check.decided"] = Check();
        Saw["trace.decided"] = Trace();
        Saw["stale.decided"] = Ok("spec", "stale");
        Saw["history.page-wrong"] = Ok("state", "history", "trace-decision", (string)Find(Trace(), FsdGap)!["id"]!);
    }

    void Impact()
    {
        Saw["impact"] = Ok("graph", "impact", "--symbol", PlaceOrder, "--expand", "fsdSections");
        Saw["impact.closed"] = Ok("graph", "impact", "--symbol", StockPage, "--expand", "fsdSections");

        TicketFixtures.Make(Path.Combine(Root, "replay"), "SHOP-1", "Bug", "Orders over 50 aren't stopped", "An order over 50 went through with no approval.");
        Saw["ticket.pull"] = Ok("ticket", "pull", "SHOP-1");
        Saw["ticket.points"] = Ok("ticket", "points", "SHOP-1", "--add", Post);
        Saw["ticket.bundle"] = Ok("ticket", "bundle", "SHOP-1");
        Validate("ticket-bundle.v2.json", Seen("ticket.bundle"), "ticket bundle");
        Directory.Delete(Path.Combine(Root, "replay"), true);
    }

    void NewVersion()
    {
        Commit("before the new version");
        var beforeImport = Git("rev-parse", "HEAD");
        Saw["runs.before-promote"] = Runs();
        Saw["ids.before"] = Trace();
        var file = Path.Combine(Root, "shop.docx");
        File.WriteAllBytes(file, Version2());
        Saw["import.v2"] = Ok("fsd", "import", file, "--id", "shop");
        File.Delete(file);
        Saw["document.candidate"] = Document("shop");
        var diff = Ok("fsd", "diff", "shop");
        Saw["diff"] = diff;
        Saw["promote.refused"] = Run("fsd", "promote", "shop").Envelope;

        var delivery = diff["sections"]!.AsArray().Single(e => (string?)e!["state"] == "doubtful" && (string?)e["to"] == "4")!;
        var old = ((JsonArray)Document("shop")["sections"]!).Single(s => (string?)s!["id"] == "5")!;
        Saw["resolve"] = Ok("fsd", "resolve", "shop", (string)delivery["to"]!, "--same-as", (string)old["permanentId"]!);
        Saw["promote"] = Ok("fsd", "promote", "shop");
        Saw["document.v2"] = Document("shop");
        Saw["trace.promoted"] = Trace();
        Saw["runs.promoted"] = Runs();
        Saw["history.dropped"] = Ok("state", "history", "trace-decision", (string)Find(Trace(), FsdConflict)!["id"]!);
        Saw["check.promoted"] = Check();
        Saw["rejudge.moved"] = Ok("fsd", "prepare", "shop", "2.1");
        Saw["rejudge.unchanged"] = Ok("fsd", "prepare", "shop", "3");
        var stock = Find(Trace(), FsdStock)!;
        Saw["history.moved"] = Ok("state", "history", "finding-review", (string)stock["id"]!);
        Saw["history.moved.finding"] = Ok("state", "history", "finding-review", (string)stock["finding"]!);
        Commit("the new version");
        // CI on that PR: a promotion mustn't read as typed decisions.
        Saw["check.promoted.base"] = Check("--base", beforeImport);
        Saw["attribute.promoted"] = Ok("trace", "attribute", "--base", beforeImport, "--by", Person);
        Saw["prepare.number-used-again"] = Ok("fsd", "prepare", "shop", "4");
        Commit("after the merge");
    }

    void Regenerate()
    {
        // The code moves first, so a line citation has somewhere new to be.
        var orders = Path.Combine(Root, "Shop.Core", "Orders.cs");
        var code = File.ReadAllText(orders);
        File.WriteAllText(orders, code.Replace("public class OrderService : IOrderService", "// Places orders.\n// Publishes what happened.\npublic class OrderService : IOrderService"));
        Commit("code moves");
        Saw["graph.2"] = Ok("graph", "build");
        Saw["stale.moved"] = Ok("spec", "stale");
        Saw["runs.before-regenerate"] = Runs();

        var step = Ok("spec", "prepare", Post);
        Saw["regenerate.prepare"] = step;
        Saw["regenerate.bundle"] = File.ReadAllText(Path.Combine(Root, Path.GetDirectoryName((string)step["next"]!["output"]!)!, "bundle.md"));
        Answer(step, WriterPost(null, Rephrased, S("The endpoint reads the Shop:Region setting.", CreateOrder)));
        Answer(Ok("spec", "check", Post), new JsonObject { ["findings"] = new JsonArray() });
        Saw["regenerate.recorded"] = Ok("spec", "check", Post);
        Saw["post.page.2"] = PageJson(Post);
        Saw["post.lines.2"] = PageFile(Post, ".lines.json");
        Saw["post.ids.2"] = PageFile(Post, ".ids.json");
        Saw["trace.regenerated"] = Trace();
        Saw["check.regenerated"] = Check();
        Saw["check.fixed"] = Check("--fix");
        Saw["trace.fixed"] = Trace();
        Saw["runs.regenerated"] = Runs();
        Saw["cite"] = Ok("spec", "cite");
        Saw["checkout.lines"] = PageFile(Checkout, ".lines.json");
    }

    void WholeResult()
    {
        Commit("the new page");
        var before = Git("rev-parse", "HEAD");
        // A person deals with what the new version and the new page left: the link on the removed section goes, section 1 is judged again.
        Edit(trace =>
        {
            var removed = trace["entries"]!.AsArray().First(e => ((string?)e!["fsd"]?["section"])?.StartsWith("(removed", StringComparison.Ordinal) == true);
            trace["entries"]!.AsArray().Remove(removed);
        });
        var step = Ok("fsd", "prepare", "shop", "1");
        Saw["rejudge.1"] = step;
        Answer(step, Links(
            Link("agree", FsdAgree, Orders, "A1", "The Place button submits the order."),
            Link("agree", FsdReceipt, Post, "E1", "which sends a receipt"),
            Link("conflict", FsdConflict.Replace("50", "75"), Post, "R1", "Every order is placed with a total of 1.", "FSD says over 75 need approval; the code has no such rule."),
            Link("gap", FsdGap, Post, null, null, "No statement says how a receipt is delivered."),
            Link("gap", FsdBack, Post, null, null, "The page no longer says what comes back.")));
        step = Ok("fsd", "check", "shop", "1");
        Answer(step, new JsonObject { ["checks"] = new JsonArray([.. new[] { "K1", "K2" }.Select(k => (JsonNode)new JsonObject { ["link"] = k, ["verdict"] = "confirm", ["note"] = "same" })]) });
        Saw["rejudge.recorded"] = Ok("fsd", "check", "shop", "1");
        Saw["check.final"] = Check();
        Commit("section 1 judged again");
        Saw["check.final.base"] = Check("--base", before);
        Saw["trace.final"] = Trace();
        Saw["runs.final"] = Runs();
        Saw["phases"] = RunIn(RepoRoot.Find(), "phases", "check").Envelope;

        // Last, because each changes what's on file: what the disprove step does next, on a real run. It runs after every section.
        Saw["disprove.moved"] = Ok("fsd", "disprove", "prepare", "shop", "2.1");
        Saw["trace.disprove-moved"] = Trace();
        // Regenerate pushed this code two lines down, words unchanged: section 3's verdict rests on the words.
        Saw["disprove.pushed-down"] = Ok("fsd", "disprove", "prepare", "shop", "3");
        Append("README.md", "A line about nothing.\n");
        Commit("no code changes");
        Ok("graph", "build");
        Saw["disprove.new-commit"] = Ok("fsd", "disprove", "prepare", "shop", "3");
        Saw["trace.disprove-new-commit"] = Trace();
    }

    // ---- the fake agents' answers ----

    public static JsonObject S(string text, params string[] cites) => new() { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) };

    static JsonObject Writer(string title, JsonObject[]? shows = null, JsonObject[]? actions = null, JsonObject[]? rules = null, JsonObject[]? effects = null) => new()
    {
        ["title"] = title,
        ["shows"] = new JsonArray([.. (shows ?? [])]),
        ["actions"] = new JsonArray([.. (actions ?? [])]),
        ["rules"] = new JsonArray([.. (rules ?? [])]),
        ["sideEffects"] = new JsonArray([.. (effects ?? [])]),
    };

    static JsonObject WriterPost(string? returned, string effect, JsonObject? added) => Writer("Place an order",
        shows: [.. returned is null ? [] : new[] { S(returned, PlaceOrder) }, .. added is null ? [] : new[] { added }],
        rules: [S(Rule, CreateOrder, PlaceOrder)],
        effects: [S(effect, PlaceOrder, Handler)]);

    static JsonObject Link(string state, string? fsd, string? page, string? statement, string? pageQuote, string note = "n")
    {
        var link = new JsonObject { ["state"] = state, ["note"] = note };
        if (fsd is not null) link["fsdQuote"] = fsd;
        if (page is not null) link["page"] = page;
        if (statement is not null) link["statement"] = statement;
        if (pageQuote is not null) link["pageQuote"] = pageQuote;
        return link;
    }

    static JsonObject Links(params JsonObject[] links) => new() { ["links"] = new JsonArray(links) };

    static JsonObject Quoted(string verdict, string path, int line, string quote) => new()
    {
        ["verdict"] = verdict, ["why"] = "From the code.", ["path"] = path, ["start"] = line, ["end"] = line, ["quote"] = quote,
    };

    // ---- the FSD, by hand ----

    const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";

    static string H(string text) => $"""<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>""";
    static string H2(string text) => $"""<w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>""";
    static string P(string text) => $"""<w:p><w:r><w:t xml:space="preserve">{text}</w:t></w:r></w:p>""";
    static string Commented(string text) => $"""<w:p><w:commentRangeStart w:id="7"/><w:r><w:t>{text}</w:t></w:r><w:commentRangeEnd w:id="7"/><w:r><w:commentReference w:id="7"/></w:r></w:p>""";
    static string Cell(string text) => $"<w:tc><w:tcPr/>{P(text)}</w:tc>";
    const string Table = "<w:tbl><w:tr>{0}{1}</w:tr><w:tr>{2}{3}</w:tr></w:tbl>";
    static readonly string Fields = string.Format(System.Globalization.CultureInfo.InvariantCulture, Table, Cell("Field"), Cell("Rule"), Cell("Customer"), Cell("Required"));
    const string Sheet = """<w:p><w:r><w:object><v:shape id="s1"/><o:OLEObject ProgID="Excel.Sheet.12" ShapeID="s1" DrawAspect="Icon"/></w:object></w:r></w:p>""";

    static string Section1(string conflict) => H("1 Orders") + P(FsdAgree) + conflict + P(FsdGap) + P(FsdReceipt) + P(FsdBack) + Fields;
    static string Section2Body => P(FsdStock);
    static string Section3 => H("3 Checkout") + P(FsdCheckout);

    /// <summary>A workbook as the main document, and a workbook inside a Word file (D123). Last, so what's before it runs on what it always did; with the switch off nothing it adds reaches a check.</summary>
    void ExcelAndEmbeddedFiles()
    {
        var checkBefore = Check();
        var book = Path.Combine(Root, "rates.xlsx");
        File.WriteAllBytes(book, XlsxFixture.Build([
            new("Rates", XlsxFixture.Row(1, XlsxFixture.Str("A1", "Tier"), XlsxFixture.Str("B1", "Fee")) + XlsxFixture.Row(2, XlsxFixture.Str("A2", "Gold"), XlsxFixture.Num("B2", "12"))),
            new("Back", XlsxFixture.Row(1, XlsxFixture.Str("A1", "secret")), State: "veryHidden"),
        ]));
        Saw["excel.import"] = Ok("fsd", "import", book, "--id", "rates");
        Saw["excel.document"] = Document("rates");
        Saw["excel.show"] = Ok("fsd", "show", "rates", "Rates");
        File.Delete(book);

        var word = Path.Combine(Root, "attached.docx");
        File.WriteAllBytes(word, EmbeddedFilesTests.WordWith("rates.xlsx", XlsxFixture.Rates()));
        Saw["embedded.import"] = Ok("fsd", "import", word, "--id", "attached");
        Saw["embedded.document"] = Document("attached");
        Saw["embedded.show"] = Ok("fsd", "show", "attached", "Rules");
        Saw["embedded.csv"] = JsonValue.Create(File.ReadAllText(Path.Combine(Root, "specs", "fsd", "attached", "attachments", "A-0001", "rates.csv")));
        File.Delete(word);
        Saw["embedded.check.before"] = checkBefore;
        Saw["embedded.check.after"] = Check();
        Saw["components"] = Ok("components");
    }

    /// <summary>A person's mapping file and a real graph, with enums read from C# and TypeScript (C.2). The graph's export is the same bytes with the switch on or off.</summary>
    void ComparedTables()
    {
        var file = Path.Combine(Root, "facts.docx");
        File.WriteAllBytes(file, DocxWriter.Write("# Order fields\n\n| Field |\n| total |\n| customer |\n| weight |\n\n# Statuses\n\n| Status | Code |\n| Open | 0 |\n| Closed | 1 |\n\n# Screens\n\n| Status |\n| Shown |\n| Hidden |\n\n# Other\n\n| Thing |\n| One |\n"));
        Ok("fsd", "import", file, "--id", "facts");
        File.Delete(file);
        var tables = Ok("fsd", "tables", "facts");
        Saw["tables"] = tables;
        string Id(int i) => (string)tables["tables"]![i]!["id"]!;
        File.WriteAllText(Path.Combine(Root, "specs", "fsd", "facts", "table-map.yaml"),
            $"version: 1\ntables:\n  - {{ table: {Id(0)}, column: Field, kind: api-field, target: POST /api/orders response }}\n"
            + $"  - {{ table: {Id(1)}, column: Status, kind: enum-member, target: OrderStatus, valueColumn: Code }}\n"
            + $"  - {{ table: {Id(2)}, column: Status, kind: enum-member, target: ScreenStatus }}\n");
        Saw["compared"] = Ok("fsd", "compare-tables", "facts");
        Saw["compared.check"] = Check();
        Saw["compared.trace"] = Trace();

        string Export(string name)
        {
            var path = Path.Combine(archive, name);
            Ok("graph", "export", path);
            return Convert.ToBase64String(File.ReadAllBytes(path));
        }
        var yaml = Path.Combine(Root, "stangent.yaml");
        var on = File.ReadAllText(yaml);
        var enumsOn = File.ReadAllText(CodeEnums.PathFor(Root));
        var exportOn = Export("on.gz");
        File.WriteAllText(yaml, on.Replace("table-facts: true", "table-facts: false", StringComparison.Ordinal));
        Ok("graph", "build");
        Saw["enums.removed"] = JsonValue.Create(!File.Exists(CodeEnums.PathFor(Root)));
        Saw["compare.off"] = Ok("fsd", "compare-tables", "facts");
        Saw["export.same"] = JsonValue.Create(Export("off.gz") == exportOn);
        File.WriteAllText(yaml, on);
        Ok("graph", "build");
        Saw["enums.back"] = JsonValue.Create(File.ReadAllText(CodeEnums.PathFor(Root)) == enumsOn);
    }

    /// <summary>The link judge's variants, answered by the fake AI, with the code checks running on each.</summary>
    void LinksOn()
    {
        var step = Ok("fsd", "prepare", "shop", "1");
        Saw["on.judge"] = step;
        var partial = Link("partial", FsdConflict, Post, "R1", "Every order is placed with a total of 1.", "Part of it.");
        partial["covered"] = "a total";
        partial["missing"] = "who approves";
        var unclear = Link("unclear", FsdGap, null, null, null, "It doesn't say which post.");
        foreach (var k in unclear.Where(p => p.Value is null).Select(p => p.Key).ToList()) unclear.Remove(k);
        Answer(step, Links(partial, unclear));
        Saw["on.recorded"] = Ok("fsd", "check", "shop", "1");
        Saw["on.check"] = Check();
        Saw["on.status"] = Ok("fsd", "status", "shop");
    }

    static byte[] Version1() => FsdDocxTests.Docx(
        Section1(Commented(FsdConflict)) + Sheet
        + H("2 Stock") + Section2Body
        + Section3
        + H("4 Legacy place") + P(FsdLegacy)
        + H("5 Delivery") + P("Delivery takes three working days.") + P("Couriers collect parcels at noon."),
        m => m.AddNewPart<WordprocessingCommentsPart>().Comments = new Comments(
            $"""<w:comments {W}><w:comment w:id="7" w:author="Jane"><w:p><w:r><w:t>Is it 50 or 75?</w:t></w:r></w:p></w:comment></w:comments>"""));

    static byte[] Version2() => FsdDocxTests.Docx(
        Section1(P(FsdConflict.Replace("50", "75"))) + Sheet
        + H("2 Warehouse") + P("The warehouse holds the stock.")
        + H2("2.1 Stock") + Section2Body
        + Section3
        + H("4 Shipping") + P("Delivery takes three working days.") + P("Couriers collect parcels at ten."));

    // ---- the run's tools ----

    public readonly record struct CliRun(int Exit, JsonObject Envelope, string Err);

    CliRun Run(params string[] args) => RunIn(Root, args);

    /// <summary>The built tool, as a person or a driver runs it.</summary>
    static CliRun RunIn(string cwd, params string[] args)
    {
        var dll = StangentDll();
        var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        psi.ArgumentList.Add(dll);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment.Remove("ANTHROPIC_API_KEY");
        psi.Environment.Remove("STANGENT_TS_EXTRACTOR");
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        var envelope = JsonNode.Parse(output) as JsonObject ?? throw new InvalidOperationException($"stangent {string.Join(' ', args)} printed no envelope: {output}");
        return new CliRun(p.ExitCode, envelope, err.Result);
    }

    static string StangentDll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        var (framework, config) = (dir.Name, dir.Parent!.Name);
        var dll = Path.Combine(RepoRoot.Find(), "src", "Stangent.Cli", "bin", config, framework, "stangent.dll");
        return File.Exists(dll) ? dll : throw new InvalidOperationException($"{dll} isn't built");
    }

    JsonNode Ok(params string[] args)
    {
        var run = Run(args);
        if (run.Exit != 0) throw new InvalidOperationException($"stangent {string.Join(' ', args)} exit {run.Exit}: {run.Envelope.ToJsonString()}");
        return run.Envelope["result"]!;
    }

    /// <summary><c>trace check</c>'s report, whether it passed or found problems.</summary>
    JsonNode Check(params string[] args) => Run(["trace", "check", .. args]).Envelope["result"]!;

    JsonNode Record(string page, JsonObject writer, JsonObject? verifier = null)
    {
        Answer(Ok("spec", "prepare", page), writer);
        Answer(Ok("spec", "check", page), verifier ?? new JsonObject { ["findings"] = new JsonArray() });
        return Ok("spec", "check", page);
    }

    /// <summary>The fake agent writes its answer where the step said, in the shape the step said.</summary>
    void Answer(JsonNode step, JsonObject answer)
    {
        var next = step["next"] ?? throw new InvalidOperationException($"the step names nothing to run: {step.ToJsonString()}");
        Validate((string)next["schema"]!, answer, (string)next["agent"]!);
        File.WriteAllText(Path.Combine(Root, (string)next["output"]!), answer.ToJsonString());
    }

    void Validate(string schema, JsonNode? json, string what)
    {
        foreach (var e in SchemaRegistry.Embedded.Validate(schema, json!.ToJsonString())) SchemaErrors.Add($"{what}: {e}");
    }

    /// <summary>After each stage, whatever the stage left on disk must still fit its contract.</summary>
    void Sweep(int stage)
    {
        var reference = Path.Combine(Root, "specs", "reference");
        if (Directory.Exists(reference))
            foreach (var file in Directory.EnumerateFiles(reference, "*.json", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name == "failed.json" || name.EndsWith(".ids.json", StringComparison.Ordinal)) continue;
                Validate(name.EndsWith(".lines.json", StringComparison.Ordinal) ? "reference-lines.v1.json" : "reference-page.v1.json", JsonNode.Parse(File.ReadAllText(file)), $"stage {stage} {name}");
            }
        var fsd = Path.Combine(Root, "specs", "fsd");
        if (Directory.Exists(fsd))
            foreach (var dir in Directory.EnumerateDirectories(fsd))
                Validate("source-document.v3.json", JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "document.json"))), $"stage {stage} {Path.GetFileName(dir)}");
        if (File.Exists(TraceFile.PathIn(Root))) Validate("trace-links.v4.json", Trace(), $"stage {stage} trace.yaml");
    }

    JsonObject Trace() => TraceFile.Load(Root);

    /// <summary>A person's edit of <c>specs/trace.yaml</c>: the same file, the same fixed form.</summary>
    void Edit(Action<JsonObject> edit)
    {
        var trace = Trace();
        edit(trace);
        TraceFile.Save(Root, trace);
    }

    JsonNode Runs() => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "specs", "trace-runs.json")))!;
    JsonNode Document(string doc) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "specs", "fsd", doc, "document.json")))!;
    JsonNode PageJson(string page) => PageFile(page, ".json");

    JsonNode PageFile(string page, string extension)
    {
        var path = (string)Ok("spec", "pages")["pages"]!.AsArray().Single(p => (string?)p!["id"] == page)!["path"]!;
        return JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "specs", "reference", path + extension)))!;
    }

    void Append(string file, string text) => File.AppendAllText(Path.Combine(Root, file), text);

    void Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "--allow-empty", "-m", message);
    }

    string Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)}: {err.Result}");
        return output;
    }
}

public class WholeFlowTests(WholeFlow flow) : IClassFixture<WholeFlow>
{
    [Fact]
    public void Stage_1_init_and_the_real_graph_build_set_the_repo_up()
    {
        flow.Reached(1);
        Assert.True(File.Exists(Path.Combine(flow.Root, ".stangent", "graph.sqlite")));
        var graph = flow.Seen("graph");
        Assert.Empty(graph["loadErrors"]!.AsArray());
        Assert.Equal(0, (int?)graph["dirtyFiles"]);
        Assert.Equal(2, (int?)graph["frontends"]);
    }

    [Fact]
    public void Stage_2_a_page_goes_writer_check_verifier_check_and_statements_get_ids_and_line_citations()
    {
        flow.Reached(2);
        Assert.Equal(("write", "stangent-page-writer"), ((string?)flow.Seen("post.prepare")["next"]!["action"], (string?)flow.Seen("post.prepare")["next"]!["agent"]));
        Assert.Equal("recorded", (string?)flow.Seen("post.recorded")["status"]);

        var sections = flow.Seen("post.page")["sections"]!;
        Assert.Equal(WholeFlow.Returned, (string?)sections["shows"]![0]!["text"]);
        var lines = flow.Seen("post.lines");
        Assert.Equal(("Shop.Core/Orders.cs", 28, 33), ((string?)lines["symbols"]![WholeFlow.PlaceOrder]!["file"], (int?)lines["symbols"]![WholeFlow.PlaceOrder]!["start"], (int?)lines["symbols"]![WholeFlow.PlaceOrder]!["end"]));
        Assert.Equal((string?)flow.Seen("graph")["commit"], (string?)lines["commit"]);
        var ids = flow.Seen("post.ids")["scopes"]!;
        Assert.Equal((1, 1, 1), ((int?)ids["S"]!["last"], (int?)ids["R"]!["last"], (int?)ids["E"]!["last"]));

        // The verifier's finding sent the page to the fix round, and stays recorded on its statement.
        Assert.Equal("fix", (string?)flow.Seen("checkout.fix")["status"]);
        Assert.Equal("recorded", (string?)flow.Seen("checkout.recorded")["status"]);
        var finding = Assert.Single(flow.Seen("checkout.page")["verification"]!["findings"]!.AsArray())!;
        Assert.Equal(("A1", "unsupported"), ((string?)finding["statement"], (string?)finding["kind"]));
    }

    [Fact]
    public void Stage_3_a_docx_imports_with_permanent_ids_and_lists_what_it_cant_bring_in()
    {
        flow.Reached(3);
        Assert.Equal("imported", (string?)flow.Seen("import")["status"]);
        var sections = flow.Seen("document")["sections"]!.AsArray();
        Assert.Equal(["1", "2", "3", "4", "5"], sections.Select(s => (string?)s!["id"]));
        Assert.Equal(["S-0001", "S-0002", "S-0003", "S-0004", "S-0005"], sections.Select(s => (string?)s!["permanentId"]));
        var orders = (string)sections[0]!["text"]!;
        Assert.Contains(WholeFlow.FsdConflict, orders);
        Assert.Contains("| Customer | Required |", orders);
        Assert.DoesNotContain("Is it 50 or 75?", orders);

        var structured = flow.Seen("import")["problems"]!.AsArray();
        var problems = structured.Select(p => (string)p!["detail"]!).ToList();
        Assert.Contains("section 1: an embedded file not read (Excel.Sheet.12)", problems);
        Assert.Contains("comment: section \"1\" by \"Jane\" open: \"Is it 50 or 75?\"", problems);
        Assert.Equal(2, problems.Count);
        Assert.Equal([("element", "S-0001"), ("comment", "S-0001")], structured.Select(p => ((string)p!["kind"]!, (string)p["section"]!)));
        Assert.Equal(("Jane", "open", "Is it 50 or 75?"), ((string)structured[1]!["author"]!, (string)structured[1]!["state"]!, (string)structured[1]!["text"]!));
    }

    [Fact]
    public void Stage_3_a_pdf_comes_through_the_same_import()
    {
        flow.Reached(3);
        Assert.Equal("imported", (string?)flow.Seen("import.pdf")["status"]);
        var sections = flow.Seen("document.pdf")["sections"]!.AsArray();
        Assert.Equal([("1", "Refunds", "S-0001"), ("2", "Returns", "S-0002")], sections.Select(s => ((string?)s!["id"], (string?)s["title"], (string?)s["permanentId"])));
        Assert.Equal("Refunds are paid within five days.", (string?)sections[0]!["text"]);
        Assert.Empty(flow.Seen("import.pdf")["problems"]!.AsArray());
    }

    [Fact]
    public void Stage_4_code_finds_candidates_checks_quotes_issues_ids_and_fills_evidence()
    {
        flow.Reached(4);
        var orders = flow.Seen("candidates")["result"]!.AsArray().Single(r => (string?)r!["doc"] == "shop" && (string?)r["section"] == "1")!;
        Assert.Contains(orders["candidates"]!.AsArray(), c => (string?)c!["page"] == WholeFlow.Orders && (string?)c["why"] == "name");
        Assert.Contains(orders["candidates"]!.AsArray(), c => (string?)c!["page"] == WholeFlow.Post && (string?)c["why"] == "endpoint");
        Assert.Contains(flow.Seen("judge.bundle").GetValue<string>().Split('\n'), l => l.Contains("**R1**") && l.Contains(WholeFlow.Rule));

        // The made-up quote was refused by code, with one retry.
        var retry = flow.Seen("judge.retry");
        Assert.Equal("check_failed", (string?)retry["error"]!["code"]);
        Assert.Contains("Orders over 75 need approval.", (string?)retry["result"]!["errors"]![0]);
        Assert.Equal("recorded", (string?)flow.Seen("judge.recorded")["status"]);

        var report = flow.Seen("check.links");
        Assert.True((bool?)report["ok"], string.Join("\n", WholeFlow.ProblemLines(report)));
        Assert.Equal((4, 3, 1), ((int?)report["counts"]!["byState"]!["agree"], (int?)report["counts"]!["byState"]!["conflict"], (int?)report["counts"]!["byState"]!["gap"]));
        Assert.Equal(4, (int?)report["counts"]!["bySeverity"]!["ungraded"]);

        Assert.Equal("confirmed", (string?)flow.EntryIn("trace.links", WholeFlow.FsdAgree)["check"]);
        Assert.Null(flow.EntryIn("trace.links", WholeFlow.FsdAgree)["finding"]);
        var conflict = flow.EntryIn("trace.links", WholeFlow.FsdConflict);
        var id = (string)conflict["finding"]!;
        Assert.StartsWith("X-", id);
        Assert.Equal("ungraded", (string?)conflict["severity"]);
        var evidence = conflict["evidence"]!.AsArray();
        Assert.Contains(evidence, r => (string?)r!["id"] == $"{id}.fsd" && (string?)r["citation"]!["quote"] == WholeFlow.FsdConflict && (string?)r["strength"] == "stated");
        Assert.Contains(evidence, r => (string?)r!["id"] == $"{id}.page" && (string?)r["citation"]!["symbol"] == "R1");
        var code = evidence.First(r => (string?)r!["id"] == $"{id}.code.2")!;
        Assert.Equal((28, 33, "read"), ((int?)code["citation"]!["lines"]!["start"], (int?)code["citation"]!["lines"]!["end"], (string?)code["strength"]));
        Assert.EndsWith("Shop.Core/Orders.cs", (string?)code["citation"]!["ref"]);
    }

    [Fact]
    public void Stage_5_disprove_answers_go_to_the_right_queues_and_a_person_closes_or_sends_back()
    {
        flow.Reached(5);
        Assert.Equal(("current", "none"), ((string?)flow.Seen("rejudge")["status"], (string?)flow.Seen("rejudge")["next"]!["action"]));     // turning it on re-judges nothing
        var bundle = flow.Seen("disprove.bundle").GetValue<string>();
        Assert.Contains("# The code", bundle);
        Assert.DoesNotContain("No statement says how a receipt is delivered.", bundle);     // never who found it, or why (D61)

        var report = flow.Seen("check.disprove");
        Assert.True((bool?)report["ok"], string.Join("\n", WholeFlow.ProblemLines(report)));
        string Id(string quote) => (string)flow.EntryIn("trace.disprove", quote)["finding"]!;
        JsonNode Goes(string quote) => flow.Listing("check.disprove", Id(quote))!;
        Assert.Equal(("stands", "ba"), ((string?)Goes(WholeFlow.FsdConflict)["verdict"], (string?)Goes(WholeFlow.FsdConflict)["goesTo"]));
        Assert.Equal(("page-gap", "developer"), ((string?)Goes(WholeFlow.FsdGap)["verdict"], (string?)Goes(WholeFlow.FsdGap)["goesTo"]));
        Assert.Equal(("refuted", "developer-confirm"), ((string?)Goes(WholeFlow.FsdStock)["verdict"], (string?)Goes(WholeFlow.FsdStock)["goesTo"]));
        Assert.Contains(report["queues"]!["developer-confirm"]!.AsArray(), f => (string?)f == Id(WholeFlow.FsdCheckout));
        Assert.Equal("Shop.Web/react-app/src/StockPage.tsx:3-3", (string?)Goes(WholeFlow.FsdStock)["at"]);
        Assert.Contains(flow.EntryIn("trace.disprove", WholeFlow.FsdStock)["evidence"]!.AsArray(),
            r => (string?)r!["id"] == $"{Id(WholeFlow.FsdStock)}.disprove" && (string?)r["strength"] == "read");
        Assert.Equal(("current", "none"), ((string?)flow.Seen("disprove.none")["status"], (string?)flow.Seen("disprove.none")["next"]!["action"]));

        Assert.Equal(("confirmed", "closed", WholeFlow.Person), ((string?)flow.Seen("confirm")["review"], (string?)flow.Seen("confirm")["goesTo"], (string?)flow.Seen("confirm")["by"]));
        Assert.Equal(("sent-back", "ba"), ((string?)flow.Seen("send-back")["review"], (string?)flow.Seen("send-back")["goesTo"]));
        var reviewed = flow.Seen("check.reviewed");
        Assert.True((bool?)reviewed["ok"], string.Join("\n", WholeFlow.ProblemLines(reviewed)));
        Assert.Contains(reviewed["queues"]!["closed"]!.AsArray(), f => (string?)f == Id(WholeFlow.FsdStock));
        Assert.Contains(reviewed["queues"]!["ba"]!.AsArray(), f => (string?)f == Id(WholeFlow.FsdCheckout));

        var confirm = Assert.Single(flow.Seen("history.confirm")["moves"]!.AsArray())!;
        Assert.Equal(("confirmed", WholeFlow.Person), ((string?)confirm["to"], (string?)confirm["who"]!["name"]));
        Assert.Equal("sent-back", (string?)Assert.Single(flow.Seen("history.send-back")["moves"]!.AsArray())!["to"]);
        Assert.Equal("refuted", (string?)Assert.Single(flow.Seen("history.verdict")["moves"]!.AsArray())!["to"]);
    }

    [Fact]
    public void Stage_5_what_the_run_leaves_in_the_store_folder_isnt_committed()
    {
        flow.Reached(6);
        Assert.Empty(flow.Seen("git.tracked").GetValue<string>());     // the disprove bundles hold FSD text and source
    }

    [Fact]
    public void Stage_6_a_persons_decisions_pass_the_checks_and_page_wrong_makes_the_page_stale()
    {
        flow.Reached(6);
        Assert.Equal(3, (int?)flow.Seen("approve.1")["approved"]);
        Assert.Equal(1, (int?)flow.Seen("approve.4")["approved"]);
        var pr = flow.Seen("check.in-pr");
        Assert.True((bool?)pr["ok"], string.Join("\n", WholeFlow.ProblemLines(pr)));
        Assert.True((int?)flow.Seen("attribute")["attributed"] > 0);
        var decided = flow.Seen("check.decided");
        Assert.True((bool?)decided["ok"], string.Join("\n", WholeFlow.ProblemLines(decided)));

        var conflict = flow.EntryIn("trace.decided", WholeFlow.FsdConflict);
        Assert.Equal(("code-wrong", WholeFlow.Person, "major", WholeFlow.Person), ((string?)conflict["decision"], (string?)conflict["decided_by"], (string?)conflict["severity"], (string?)conflict["severity_by"]));
        Assert.NotNull(conflict["decided_on"]);
        var gap = flow.EntryIn("trace.decided", WholeFlow.FsdGap);
        Assert.Equal(("page-wrong", WholeFlow.Person, "minor"), ((string?)gap["decision"], (string?)gap["decided_by"], (string?)gap["severity"]));
        Assert.Equal("page-owner", (string?)flow.Listing("check.decided", (string)gap["finding"]!)!["goesTo"]);
        Assert.Equal(("agree", WholeFlow.Person), ((string?)flow.EntryIn("trace.decided", WholeFlow.FsdLegacy)["decision"], (string?)flow.EntryIn("trace.decided", WholeFlow.FsdLegacy)["decided_by"]));

        var stale = flow.Seen("stale.decided")["stale"]!.AsArray().Single(p => (string?)p!["id"] == WholeFlow.Post)!;
        Assert.Equal(WholeFlow.Person, (string?)stale["reportedWrong"]![0]!["by"]);
        Assert.Equal("page-wrong", (string?)flow.Seen("history.page-wrong")["moves"]!.AsArray().Last()!["to"]);
    }

    [Fact]
    public void Stage_7_an_impact_answer_on_the_code_a_link_reaches_names_the_FSD_section()
    {
        flow.Reached(7);
        var items = flow.Seen("impact")["fsdSections"]!["items"]!.AsArray();
        var section = items.Single(i => (string?)i!["section"] == "1")!;
        Assert.Equal(("shop", "Orders", false), ((string?)section["doc"], (string?)section["title"], (bool?)section["verified"]));     // page-wrong isn't verified
        Assert.Contains(section["pages"]!.AsArray(), p => (string?)p == WholeFlow.Post);
        Assert.Equal(["agree", "conflict", "gap"], section["states"]!.AsArray().Select(s => (string?)s));
        Assert.Contains(items, i => (string?)i!["section"] == "4" && (bool?)i["verified"] == true);
        // Only a closed finding leaves the answer.
        Assert.Empty(flow.Seen("impact.closed")["fsdSections"]!["items"]!.AsArray());
    }

    [Fact]
    public void Stage_7_a_ticket_bundle_on_that_code_names_the_section_too()
    {
        flow.Reached(7);
        var bundle = flow.Seen("ticket.bundle");
        var section = bundle["sections"]!.AsArray().Single(s => (string?)s!["id"] == "1")!;
        Assert.Equal("shop", (string?)section["doc"]);
        Assert.Contains(section["links"]!.AsArray(), l => (string?)l!["state"] == "conflict" && (string?)l["statement"] == "R1" && (string?)l["decision"] == "code-wrong");
        Assert.Contains(bundle["impact"]!["fsdSections"]!["items"]!.AsArray(), i => (string?)i!["section"] == "1" && (string?)i["title"] == "Orders");
    }

    [Fact]
    public void Stage_8_a_new_version_waits_as_a_candidate_until_doubtful_sections_are_settled()
    {
        flow.Reached(8);
        Assert.Equal("candidate", (string?)flow.Seen("import.v2")["status"]);
        Assert.Equal("S-0001", (string?)flow.Seen("document.candidate")["sections"]![0]!["permanentId"]);     // the current version still serves
        Assert.Equal("5", (string?)flow.Seen("document.candidate")["sections"]![4]!["id"]);
        var counts = flow.Seen("diff")["counts"]!;
        Assert.Equal((1, 1, 1, 1, 1, 2), ((int?)counts["modified"], (int?)counts["moved"], (int?)counts["removed"], (int?)counts["unchanged"], (int?)counts["added"], (int?)counts["doubtful"]));
        Assert.Equal("doubtful_sections_remain", (string?)flow.Seen("promote.refused")["error"]!["code"]);
        var after = flow.Seen("document.v2")["sections"]!.AsArray();
        Assert.Equal([("1", "S-0001"), ("2", "S-0006"), ("2.1", "S-0002"), ("3", "S-0003"), ("4", "S-0005")], after.Select(s => ((string?)s!["id"], (string?)s["permanentId"])));
    }

    [Fact]
    public void Stage_8_references_follow_a_moved_section_and_a_modified_quote_goes_back_to_the_BA()
    {
        flow.Reached(8);
        var was = flow.EntryIn("ids.before", WholeFlow.FsdStock);
        var moved = flow.EntryIn("trace.promoted", WholeFlow.FsdStock);
        Assert.Equal(("2.1", "Stock"), ((string?)moved["fsd"]!["section"], (string?)moved["fsd"]!["title"]));
        Assert.Equal("S-0002", (string?)moved["fsd"]!["sectionId"]);     // the number moved; the permanent id didn't
        Assert.Equal((string?)was["finding"], (string?)moved["finding"]);
        Assert.Contains(flow.Seen("promote")["carried"]!["traceIds"]!.AsArray(), m => (string?)m!["from"] == (string?)was["id"] && (string?)m["to"] == (string?)moved["id"]);
        Assert.Equal("closed", (string?)flow.Listing("check.promoted", (string)moved["finding"]!)!["goesTo"]);
        Assert.Equal(("current", "none"), ((string?)flow.Seen("rejudge.moved")["status"], (string?)flow.Seen("rejudge.moved")["next"]!["action"]));     // only its number changed: no paid run

        var modified = flow.EntryIn("trace.promoted", WholeFlow.FsdConflict);
        Assert.Contains(flow.Seen("promote")["staleQuotes"]!.AsArray(), id => (string?)id == (string?)modified["id"]);
        Assert.Empty(flow.Seen("promote")["carried"]!["sectionIdsDisagree"]!.AsArray());
        Assert.Equal(["1"], flow.Seen("promote")["carried"]!["runAgain"]!.AsArray().Select(s => (string?)s));
        Assert.Null(modified["decision"]);
        Assert.Null(modified["decided_by"]);
        Assert.Equal(("major", WholeFlow.Person), ((string?)modified["severity"], (string?)modified["severity_by"]));     // a grade stays
        var listed = flow.Listing("check.promoted", (string)modified["finding"]!)!;
        Assert.Equal("ba", (string?)listed["goesTo"]);
        Assert.Contains("a quote no longer matches", (string?)listed["label"]);
        var drop = flow.Seen("history.dropped")["moves"]!.AsArray().Last()!;
        Assert.Equal(("code-wrong", "undecided"), ((string?)drop["from"], (string?)drop["to"]));
    }

    [Fact]
    public void Stage_8_a_removed_sections_link_keeps_its_decision_and_a_marker_and_its_number_can_be_used_again()
    {
        flow.Reached(8);
        var removed = flow.EntryIn("trace.promoted", WholeFlow.FsdLegacy);
        Assert.Equal(("(removed S-0004) 4", "Legacy place"), ((string?)removed["fsd"]!["section"], (string?)removed["fsd"]!["title"]));
        Assert.Equal(("agree", WholeFlow.Person), ((string?)removed["decision"], (string?)removed["decided_by"]));
        var carried = Assert.Single(flow.Seen("promote")["carried"]!["removed"]!.AsArray())!;
        Assert.Equal(("S-0004", true), ((string?)carried["permanentId"], (bool?)carried["numberUsedAgain"]));
        Assert.Equal((string?)removed["id"], (string?)carried["decisions"]![0]!["id"]);
        Assert.Contains(WholeFlow.ProblemLines(flow.Seen("check.promoted")), p => p.Contains("was removed"));
        // The new section 4 isn't taken for the old one: it starts over.
        Assert.NotEqual("current", (string?)flow.Seen("prepare.number-used-again")["status"]);
    }

    [Fact]
    public void Stage_8_a_promotion_doesnt_read_as_typed_decisions_in_CI()
    {
        flow.Reached(8);
        Assert.DoesNotContain(WholeFlow.ProblemLines(flow.Seen("check.promoted.base")), p => !p.Contains("was removed"));
        Assert.Equal(0, (int?)flow.Seen("attribute.promoted")["attributed"]);
    }

    [Fact]
    public void Stage_9_a_regenerated_page_keeps_or_retires_ids_and_decisions_on_changed_words_drop_back()
    {
        flow.Reached(9);
        Assert.Contains("## A person said this page is wrong", flow.Seen("regenerate.bundle").GetValue<string>());
        Assert.Contains(WholeFlow.FsdGap, flow.Seen("regenerate.bundle").GetValue<string>());
        Assert.Equal("recorded", (string?)flow.Seen("regenerate.recorded")["status"]);

        var page = flow.Seen("post.page.2")["sections"]!;
        var e1 = page["sideEffects"]![0]!;
        Assert.Equal(("E1", WholeFlow.Rephrased), ((string?)e1["id"], (string?)e1["text"]));     // other words, the same symbols: kept (D118)
        Assert.Equal("R1", (string?)page["rules"]![0]!["id"]);
        var added = Assert.Single(page["shows"]!.AsArray())!;
        Assert.Equal("S2", (string?)added["id"]);     // the retired S1 never comes back
        var ids = flow.Seen("post.ids.2")["scopes"]!["S"]!;
        Assert.Equal((2, "S1"), ((int?)ids["last"], (string?)Assert.Single(ids["removed"]!.AsArray())));

        var report = flow.Seen("check.regenerated");
        Assert.Contains(report["changedStatements"]!.AsArray(), c => (string?)c!["statement"] == "E1" && (string?)c["was"] == "agree");
        Assert.Contains(report["retiredStatements"]!.AsArray(), c => (string?)c!["statement"] == "S1");

        Assert.Equal("agree", (string?)flow.EntryIn("trace.regenerated", WholeFlow.FsdReceipt)["decision"]);
        Assert.Null(flow.EntryIn("trace.fixed", WholeFlow.FsdReceipt)["decision"]);     // the rephrased statement's
        Assert.Equal("E1", (string?)flow.EntryIn("trace.fixed", WholeFlow.FsdReceipt)["page"]!["statement"]);
        Assert.Null(flow.EntryIn("trace.fixed", WholeFlow.FsdBack)["decision"]);        // the retired statement's
        var gap = flow.EntryIn("trace.regenerated", WholeFlow.FsdGap);
        Assert.Null(gap["decision"]);                                                // writing the page again clears page-wrong
        Assert.Contains("page was regenerated", (string?)flow.Listing("check.regenerated", (string)gap["finding"]!)!["label"]);
        Assert.NotNull(flow.Seen("stale.moved")["stale"]!.AsArray().Single(p => (string?)p!["id"] == WholeFlow.Post)!["reportedWrong"]);
    }

    [Fact]
    public void Stage_9_line_citations_and_evidence_follow_the_code()
    {
        flow.Reached(9);
        var graph = flow.Seen("graph.2");
        var lines = flow.Seen("post.lines.2");
        Assert.Equal((string?)graph["commit"], (string?)lines["commit"]);
        Assert.Equal((30, 35), ((int?)lines["symbols"]![WholeFlow.PlaceOrder]!["start"], (int?)lines["symbols"]![WholeFlow.PlaceOrder]!["end"]));
        Assert.Equal(0, (int?)flow.Seen("cite")["citations"]!["withoutLines"]);
        Assert.Equal((string?)graph["commit"], (string?)flow.Seen("checkout.lines")["commit"]);
        var entry = flow.EntryIn("trace.fixed", WholeFlow.FsdConflict);
        var code = entry["evidence"]!.AsArray().First(r => ((string?)r!["id"])!.EndsWith(".code.2", StringComparison.Ordinal))!;
        Assert.Equal((30, 35), ((int?)code["citation"]!["lines"]!["start"], (int?)code["citation"]!["lines"]!["end"]));
    }

    [Fact]
    public void Stage_10_the_end_state_passes_every_check_and_nothing_broke_its_contract()
    {
        flow.Reached(11);
        Assert.Equal([], flow.SchemaErrors);
        var final = flow.Seen("check.final");
        Assert.True((bool?)final["ok"], string.Join("\n", WholeFlow.ProblemLines(final)));
        var ci = flow.Seen("check.final.base");
        Assert.True((bool?)ci["ok"], string.Join("\n", WholeFlow.ProblemLines(ci)));
        Assert.True((bool?)flow.Seen("phases")["ok"], flow.Seen("phases").ToJsonString());
        Assert.Equal("recorded", (string?)flow.Seen("rejudge.recorded")["status"]);

        // A re-run that gives the same link keeps its X- id; a new link gets a new one; a retired one isn't used again.
        var gone = (string)flow.EntryIn("trace.promoted", WholeFlow.FsdConflict)["finding"]!;
        var now = flow.Seen("check.final")["findings"]!.AsArray().Select(f => (string)f!["id"]!).ToList();
        Assert.DoesNotContain(gone, now);
        Assert.Contains((string)flow.EntryIn("trace.promoted", WholeFlow.FsdGap)["finding"]!, now);
        Assert.Contains((string)flow.EntryIn("trace.final", WholeFlow.FsdConflict.Replace("50", "75"))["finding"]!, now);
        Assert.NotEqual(gone, (string)flow.EntryIn("trace.final", WholeFlow.FsdConflict.Replace("50", "75"))["finding"]!);
    }

    [Fact]
    public void Stage_10_input_hashes_of_unchanged_sections_stay_the_same_through_the_new_version_and_the_new_page()
    {
        flow.Reached(9);
        static string? Inputs(JsonNode runs, string key) => (string?)runs[key]!["inputs"];
        var before = flow.Seen("runs.before-disprove");
        foreach (var later in new[] { "runs.before-promote", "runs.promoted", "runs.before-regenerate", "runs.regenerated" })
            Assert.Equal(Inputs(before, "shop#3"), Inputs(flow.Seen(later), "shop#3"));
        Assert.Null(flow.Seen("runs.promoted")["shop#2"]);
        Assert.Equal(Inputs(flow.Seen("runs.promoted"), "shop#2.1"), Inputs(flow.Seen("runs.regenerated"), "shop#2.1"));
        Assert.Equal(("current", "none"), ((string?)flow.Seen("rejudge.unchanged")["status"], (string?)flow.Seen("rejudge.unchanged")["next"]!["action"]));
    }

    // ---- what the run found, fixed (phases/0-foundation.md) ----

    [Fact]
    public void Disprove_a_section_that_only_moved_keeps_its_verdict_and_a_persons_confirmation()
    {
        flow.Reached(11);
        Assert.Equal(("current", "none"), ((string?)flow.Seen("disprove.moved")["status"], (string?)flow.Seen("disprove.moved")["next"]!["action"]));
        var stock = flow.EntryIn("trace.disprove-moved", WholeFlow.FsdStock);
        Assert.Equal(("refuted", "confirmed"), ((string?)stock["verdict"], (string?)stock["review"]));
    }

    [Fact]
    public void Disprove_a_new_commit_that_touches_no_code_asks_for_no_new_verdict()
    {
        flow.Reached(11);
        Assert.Equal(("current", "none"), ((string?)flow.Seen("disprove.new-commit")["status"], (string?)flow.Seen("disprove.new-commit")["next"]!["action"]));
        Assert.Equal("refuted", (string?)flow.EntryIn("trace.disprove-new-commit", WholeFlow.FsdCheckout)["verdict"]);
    }

    [Fact]
    public void Disprove_code_pushed_down_with_the_same_words_asks_for_no_new_verdict()
    {
        flow.Reached(11);
        Assert.Equal(("current", "none"), ((string?)flow.Seen("disprove.pushed-down")["status"], (string?)flow.Seen("disprove.pushed-down")["next"]!["action"]));
    }

    [Fact]
    public void A_moved_findings_history_can_be_read_under_its_current_id()
    {
        flow.Reached(8);
        Assert.NotEmpty(flow.Seen("history.moved")["moves"]!.AsArray());
        Assert.Contains(flow.Seen("history.moved")["moves"]!.AsArray(), m => (string?)m!["kind"] == "finding-review" && (string?)m["to"] == "confirmed");
        Assert.Contains(flow.Seen("history.moved")["moves"]!.AsArray(), m => (string?)m!["kind"] == "id-moved");
        Assert.Equal(flow.Seen("history.moved")["moves"]!.ToJsonString(), flow.Seen("history.moved.finding")["moves"]!.ToJsonString());
    }

    [Fact]
    public void A_workbook_imports_as_a_document_with_its_hidden_sheet_left_out_and_listed()
    {
        flow.Reached(12);
        Assert.Equal(("imported", "ok-with-warnings"), ((string?)flow.Seen("excel.import")["status"], (string?)flow.Seen("excel.import")["verdict"]));
        var document = flow.Seen("excel.document");
        Assert.Equal(["Rates"], document["sections"]!.AsArray().Select(s => (string?)s!["id"]));
        Assert.DoesNotContain("secret", document["sections"]![0]!["text"]!.GetValue<string>());
        Assert.Contains(document["problems"]!.AsArray(), p => (string?)p!["kind"] == "hidden" && ((string)p["detail"]!).Contains("veryHidden", StringComparison.Ordinal));
        Assert.Contains("| Gold | 12 |", flow.Seen("excel.show")["text"]!.GetValue<string>());
    }

    [Fact]
    public void A_workbook_inside_a_word_file_is_saved_and_linked_and_changes_nothing_a_check_reads()
    {
        flow.Reached(12);
        var document = flow.Seen("embedded.document");
        Assert.Equal(("A-0001", "S-0001"), ((string?)document["attachments"]![0]!["id"], (string?)document["attachments"]![0]!["section"]));
        Assert.DoesNotContain("Gold", document["sections"]![0]!["text"]!.GetValue<string>());
        Assert.Contains("A-0001", flow.Seen("embedded.show")["attachments"]!.ToJsonString());
        Assert.Contains("Gold,12", flow.Seen("embedded.csv").GetValue<string>());
        Assert.Equal(flow.Seen("embedded.check.before")["staleQuotes"]!.ToJsonString(), flow.Seen("embedded.check.after")["staleQuotes"]!.ToJsonString());
        var attachments = flow.Seen("components")["components"]!.AsArray().Single(c => (string?)c!["name"] == "attachments-in-checks")!;
        Assert.False((bool)attachments["on"]!);
    }

    [Fact]
    public void A_chapter_taken_out_leaves_later_typed_chapters_their_numbers()
    {
        var doc = Stangent.Spec.Fsd.DocxConverter.Convert(FsdDocxTests.Docx(WholeFlow.TypedHeadings("1 Orders", "3 Checkout")));
        Assert.Equal([("1", "Orders"), ("3", "Checkout")], doc.Sections.Select(s => (s.Id, s.Title)));
    }

    [Fact]
    public void The_page_wrong_note_doesnt_show_an_empty_statement_for_a_gap()
    {
        flow.Reached(9);
        Assert.DoesNotContain("statement (none) \"\"", flow.Seen("regenerate.bundle").GetValue<string>());
    }
}

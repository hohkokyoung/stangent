using Stangent.Spec.Trace;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;
using Stangent.Tickets;
using Stangent.Tickets.Secrets;

namespace Stangent.Core.Tests;

/// <summary>Phase 3 step 4: the bundle (D71) on the shop app, with hand-written reference pages and an FSD link.</summary>
public class TicketBundleTests
{
    const string Orders = "page:/orders";
    const string Cart = "page:/cart";

    static SpecApp App(string extra = "") => new($"version: 1\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n  aiPoints: false\n{extra}");

    /// <summary>Pull and points for a made-up ticket.</summary>
    static void Ticket(SpecApp app, string key, string description, string[]? add = null)
    {
        TicketFixtures.Make(Path.Combine(app.Root, "replay"), key, "Bug", "Orders are wrong", description);
        var tracker = Trackers.Open(app.Repo, new MemorySecretStore(), TextWriter.Null, null);
        TicketPull.RunAsync(app.Repo, tracker, key).GetAwaiter().GetResult();
        TicketPoints.Update(app.Repo, TicketContext.Load(app.Repo), key, add ?? [], [], "Pat Developer");
    }

    /// <summary>A written reference page, as Phase 2 records it (statements with citations).</summary>
    static void Page(SpecApp app, string id, bool stale = false, params (string Section, string Text, string[] Cites)[] statements)
    {
        var set = app.Pages();
        var def = set.Find(id)!;
        var sections = new JsonObject { ["shows"] = new JsonArray(), ["actions"] = new JsonArray(), ["rules"] = new JsonArray(), ["sideEffects"] = new JsonArray() };
        foreach (var (section, text, cites) in statements)
            sections[section]!.AsArray().Add(new JsonObject { ["text"] = text, ["cites"] = new JsonArray([.. cites.Select(c => (JsonNode)c)]) });
        var page = new JsonObject
        {
            ["schemaVersion"] = 1, ["id"] = def.Id, ["kind"] = def.Kind, ["entry"] = def.Entry, ["codeTitle"] = def.Title,
            ["title"] = id == Orders ? "Orders" : def.Title, ["group"] = def.Group, ["sections"] = sections, ["unknowns"] = new JsonArray(),
            ["verification"] = new JsonObject { ["findings"] = new JsonArray(), ["dropped"] = 0 },
            ["generated"] = new JsonObject
            {
                ["commit"] = "c0ffee", ["bundleHash"] = stale ? new string('0', 64) : Bundles.Build(set, def, null).InputHash,
                ["slice"] = new JsonObject { ["depth"] = 6, ["points"] = 1, ["chars"] = 1, ["cutOff"] = 0 },
                ["writer"] = new JsonObject { ["calls"] = 1 }, ["verifier"] = new JsonObject { ["calls"] = 1 }, ["checkRetries"] = 0,
            },
        };
        var paths = new SpecPaths(app.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PageJson(def))!);
        File.WriteAllText(paths.PageJson(def), CanonicalJson.Serialize(page));
    }

    static void OrdersPage(SpecApp app, bool stale = false) => Page(app, Orders, stale,
        ("shows", "The orders screen shows a Place order button.", [SpecApp.OrdersPage]),
        ("actions", "Pressing Place order posts the order to the orders endpoint.", [SpecApp.Save]));

    /// <summary>An FSD section linked to the orders page: one agree link, and one conflict (a finding).</summary>
    static void Fsd(SpecApp app)
    {
        var dir = Path.Combine(app.Root, "specs", "fsd", "shop");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "document.json"), new JsonObject
        {
            ["schemaVersion"] = 1, ["id"] = "shop", ["title"] = "Shop FSD",
            ["sections"] = new JsonArray(new JsonObject { ["id"] = "4.1", ["title"] = "Placing an order", ["text"] = "The user places an order with the Place order button. Orders over 50 need approval.", ["parent"] = null }),
        }.ToJsonString());
        var section = new JsonObject { ["id"] = "4.1", ["title"] = "Placing an order", ["text"] = "" };
        LinkRun.ApplyEntries(app.Root, "shop", "4.1",
        [
            LinkRun.Entry("agree", "shop", section, "The user places an order with the Place order button.", Orders, "S1", "The orders screen shows a Place order button.", "judge", "confirmed", null),
            LinkRun.Entry("conflict", "shop", section, "Orders over 50 need approval.", Orders, "A1", "Pressing Place order posts the order to the orders endpoint.", "judge", null, "50 vs 100"),
        ]);
    }

    [Fact]
    public async Task An_attachment_that_cannot_be_converted_is_listed_in_a_valid_bundle()
    {
        using var app = App();
        TicketFixtures.Make(Path.Combine(app.Root, "replay"), "SHOP-20", "Bug", "Orders are wrong", "See broken.docx", files:
        [
            new("1", "broken.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", [1, 2, 3]),
        ]);
        await using var tracker = Trackers.Open(app.Repo, new MemorySecretStore(), TextWriter.Null, null);
        await TicketPull.RunAsync(app.Repo, tracker, "SHOP-20", TestContext.Current.CancellationToken);
        TicketPoints.Update(app.Repo, TicketContext.Load(app.Repo), "SHOP-20", [], [], "Pat Developer");
        var b = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-20");
        Assert.Empty(SchemaRegistry.Embedded.Validate("ticket-bundle.v2.json", b.ToJsonString()));
        Assert.Equal("broken.docx", (string)b["ticket"]!["attachments"]![0]!["fileName"]!);
    }

    [Fact]
    public void The_bundle_carries_pages_sections_impact_and_pointers_but_no_source_code()
    {
        using var app = App();
        OrdersPage(app);
        Fsd(app);
        Ticket(app, "SHOP-1", "The total on /orders is wrong when I press Place order.");
        var b = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-1");

        var page = b["pages"]!.AsArray().Single()!;
        Assert.Equal(Orders, (string?)page["id"]);
        Assert.Equal("included", (string?)page["state"]);
        Assert.Equal(["S1", "A1"], page["statements"]!.AsArray().Select(s => (string)s!["id"]!));

        var section = b["sections"]!.AsArray().Single()!;
        Assert.Equal("4.1", (string?)section["id"]);
        var link = section["links"]!.AsArray().Single()!; // findings are off by default (D68): only the agree link
        Assert.Equal("agree", (string?)link["state"]);
        Assert.False((bool)link["decided"]!);

        Assert.Equal("starting-points", (string?)b["impact"]!["change"]!["kind"]);
        Assert.Equal(2, (int)b["impact"]!["maxDepth"]!);
        Assert.Contains(b["pointers"]!.AsArray(), p => (string?)p!["id"] == SpecApp.Save && (string?)p["file"] == "web/src/Orders.tsx");

        var text = CanonicalJson.Serialize(b);
        Assert.DoesNotContain("fetch('/api/orders'", text);
        Assert.DoesNotContain("throw new ArgumentException", text);
        Assert.True(File.Exists(new TicketPaths(app.Root, "SHOP-1").BundleMarkdown));
    }

    [Fact]
    public void Missing_and_stale_pages_are_flagged_and_never_generated()
    {
        using var app = App();
        OrdersPage(app, stale: true);
        Ticket(app, "SHOP-2", "Both /orders and /cart show the wrong total.");
        var b = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-2");
        Assert.Equal([Cart], b["missingPages"]!.AsArray().Select(x => (string)x!));
        Assert.Equal([Orders], b["stalePages"]!.AsArray().Select(x => (string)x!));
        Assert.Equal("missing", (string?)b["pages"]!.AsArray().Single(p => (string?)p!["id"] == Cart)!["state"]);
        Assert.False(File.Exists(new SpecPaths(app.Root).PageJson(app.Pages().Find(Cart)!)));
        Assert.Contains("/stangent:spec-generate", File.ReadAllText(new TicketPaths(app.Root, "SHOP-2").BundleMarkdown));
    }

    [Fact]
    public void Over_the_cap_the_lowest_ranked_page_becomes_a_pointer()
    {
        using var app = App("  bundleChars: 3000\n");
        OrdersPage(app);
        Page(app, Cart, false, ("shows", "The cart screen lists what the user is about to buy, with a total.", ["ts|web/src/Other.tsx#CartPage"]));
        Ticket(app, "SHOP-3", "On /orders and on /cart the total is wrong.");
        var b = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-3");
        var full = (int)b["cap"]!["chars"]!;
        Assert.True(full <= 3000 || (bool)b["cap"]!["over"]!);
        Assert.Equal(Cart, (string?)b["cap"]!["cut"]!.AsArray().FirstOrDefault()); // found second, so cut first
        Assert.Equal("cut", (string?)b["pages"]!.AsArray().Single(p => (string?)p!["id"] == Cart)!["state"]);

        using var tiny = App("  bundleChars: 100\n");
        OrdersPage(tiny);
        Ticket(tiny, "SHOP-4", "On /orders the total is wrong.");
        var t = TicketBundle.Build(tiny.Repo, TicketContext.Load(tiny.Repo), "SHOP-4");
        Assert.True((bool)t["cap"]!["over"]!);
        Assert.Equal([Orders], t["cap"]!["cut"]!.AsArray().Select(x => (string)x!));
    }

    [Fact]
    public void A_cut_page_loses_its_statements_but_keeps_the_files_it_cites()
    {
        // On Sonarr, 5 bugs had the fix file among the starting files but lost it when its page was cut (2026-11-02).
        using var app = App("  bundleChars: 100\n");
        OrdersPage(app);
        Ticket(app, "SHOP-9", "On /orders the total is wrong.");
        var b = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-9");

        var page = b["pages"]!.AsArray().Single()!;
        Assert.Equal("cut", (string?)page["state"]);
        Assert.Null(page["statements"]);
        var pointers = b["pointers"]!.AsArray();
        Assert.Contains(pointers, p => (string?)p!["id"] == SpecApp.Save && (string?)p["file"] == "web/src/Orders.tsx" && (string?)p["from"] == Orders);
        Assert.Contains(pointers, p => (string?)p!["id"] == SpecApp.OrdersPage && (string?)p["from"] == Orders);
    }

    /// <summary>A ticket whose starting point is one file the finder named (D101), checked like a real run.</summary>
    static SpecApp Found(string key, string extra = "")
    {
        var app = new SpecApp($"version: 1\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n{extra}");
        Ticket(app, key, "When I press Place order nothing is saved and the orders page just shows Orders.");
        var ctx = TicketContext.Load(app.Repo);
        TicketPoints.Prepare(app.Repo, ctx, key);
        File.WriteAllText(Path.Combine(app.Root, ".stangent", "tickets", key, "points", "output.json"), new JsonObject
        {
            ["files"] = new JsonArray(new JsonObject
            {
                ["path"] = "web/src/Orders.tsx", ["quote"] = "When I press Place order nothing is saved",
                ["line"] = "const save = () => fetch('/api/orders', { method: 'POST' });", ["why"] = "The Place order button's save call.",
            }),
        }.ToJsonString());
        Assert.Equal("recorded", TicketPoints.Check(app.Repo, ctx, key, null, "Pat Developer").Status);
        return app;
    }

    [Fact]
    public void A_found_file_comes_first_and_the_pages_that_cite_it_explain_it()
    {
        using var app = Found("SHOP-20");
        OrdersPage(app);
        Page(app, Cart, false, ("shows", "The cart screen lists what the user is about to buy, with a total.", ["ts|web/src/Other.tsx#CartPage"]));
        var b = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-20");

        Assert.Equal(2, (int)b["schemaVersion"]!);
        var point = Assert.Single(b["points"]!.AsArray())!;
        Assert.Equal(("file", "web/src/Orders.tsx"), ((string)point["kind"]!, (string)point["id"]!));
        var first = b["pointers"]![0]!;
        Assert.Equal(("file", "web/src/Orders.tsx", "point"), ((string)first["kind"]!, (string)first["file"]!, (string)first["from"]!));

        // The orders page cites code in the file, so it explains it; the cart page doesn't, so it isn't in the bundle.
        var page = Assert.Single(b["pages"]!.AsArray())!;
        Assert.Equal(Orders, (string?)page["id"]);
        Assert.Equal(("web/src/Orders.tsx", "cites"), ((string)page["explains"]![0]!["file"]!, (string)page["explains"]![0]!["how"]!));

        // The impact answer starts from the file's own code, not from everything the explaining page cites.
        Assert.Equal("starting-points", (string?)b["impact"]!["change"]!["kind"]);
        var md = File.ReadAllText(new TicketPaths(app.Root, "SHOP-20").BundleMarkdown);
        // D107: the finder's reason is a guess from reading the code. People see it labelled; the reproducer doesn't get it.
        Assert.Contains("| file | `web/src/Orders.tsx` | The finder's guess, not checked: The Place order button's save call. The ticket: \"When I press Place order nothing is saved\"", md);
        var forProof = TicketBundle.Markdown(TicketBundle.LoadOrFail(app.Root, "SHOP-20"), forProof: true);
        Assert.Contains("| file | `web/src/Orders.tsx` | The ticket: \"When I press Place order nothing is saved\"", forProof);
        Assert.DoesNotContain("The Place order button's save call", forProof);
        Assert.Contains("Explains `web/src/Orders.tsx` (its statements cite it).", md);
    }

    [Fact]
    public void Over_the_cap_the_impact_lists_go_first_then_pages_and_the_found_file_always_stays()
    {
        using var app = Found("SHOP-21", "  bundleChars: 100\n");
        OrdersPage(app);
        var b = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-21");
        Assert.True((bool)b["cap"]!["impactTrimmed"]!);
        // The orders page only explains the file: under the cap it's left out and counted, not listed as cut.
        Assert.Empty(b["cap"]!["cut"]!.AsArray());
        Assert.Equal(1, (int)b["cap"]!["dropped"]!);
        Assert.Empty(b["pages"]!.AsArray());
        Assert.True((bool)b["cap"]!["over"]!);
        Assert.Contains(b["pointers"]!.AsArray(), p => (string?)p!["file"] == "web/src/Orders.tsx" && (string?)p["from"] == "point");
        Assert.All(b["impact"]!["groups"]!.AsArray(), g => Assert.Empty(g!["items"]!.AsArray()));
        // A cut page that only explains the file takes its citations with it: the file is a pointer of its own.
        Assert.DoesNotContain(b["pointers"]!.AsArray(), p => (string?)p!["from"] == Orders);
        var md21 = File.ReadAllText(new TicketPaths(app.Root, "SHOP-21").BundleMarkdown);
        Assert.Contains("The impact answer's lists were left out", md21);
        Assert.Contains("1 more page(s) that explain the found files were left out", md21);

        using var roomy = Found("SHOP-22", "  bundleChars: 400000\n");
        OrdersPage(roomy);
        var r = TicketBundle.Build(roomy.Repo, TicketContext.Load(roomy.Repo), "SHOP-22");
        Assert.False((bool)r["cap"]!["impactTrimmed"]!);
        Assert.Empty(r["cap"]!["cut"]!.AsArray());
    }

    [Fact]
    public void A_page_the_developer_adds_ranks_first_and_is_cut_last()
    {
        using var app = App("  bundleChars: 3000\n");
        OrdersPage(app);
        Page(app, Cart, false, ("shows", "The cart screen lists what the user is about to buy, with a total.", ["ts|web/src/Other.tsx#CartPage"]));
        Ticket(app, "SHOP-5", "On /orders the total is wrong.", add: [Cart]);
        var b = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-5");
        Assert.Equal(Cart, (string?)b["pages"]![0]!["id"]);
        var cut = b["cap"]!["cut"]!.AsArray().Select(x => (string)x!).ToList();
        Assert.Equal(Orders, cut[0]);
        Assert.True(cut.Count == 1 || cut[1] == Cart);
    }

    [Fact]
    public void Findings_appear_only_when_fsd_findings_is_on_and_are_marked_unconfirmed()
    {
        using var app = App("fsd:\n  findings: true\n");
        OrdersPage(app);
        Fsd(app);
        Ticket(app, "SHOP-6", "On /orders the total is wrong.");
        var b = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-6");
        var conflict = b["sections"]![0]!["links"]!.AsArray().Single(l => (string?)l!["state"] == "conflict")!;
        Assert.True((bool)conflict["unconfirmed"]!);
        Assert.True((bool)b["findings"]!);
    }

    // ---- a security flag (D148) ----

    /// <summary>The conflict with a flag code wrote, and with what else it takes.</summary>
    static void FlagTheConflict(SpecApp app, Action<JsonObject>? more = null)
    {
        var trace = TraceFile.Load(app.Root);
        var e = trace["entries"]!.AsArray().OfType<JsonObject>().Single(x => (string?)x["state"] == "conflict");
        var flag = new JsonObject
        {
            ["why"] = "The server trusts an id sent in the request.", ["path"] = "web/src/Orders.tsx", ["start"] = 3, ["end"] = 3, ["quote"] = "post(order)",
            ["symbol"] = SpecApp.Save, ["commit"] = new string('a', 40), ["inputs"] = new string('b', 64),
        };
        e["security"] = flag;
        e["security_hash"] = TraceFile.SecurityHash(flag);
        e["security_state"] = "unanswered";
        more?.Invoke(e);
        TraceFile.Save(app.Root, trace);
    }

    static JsonNode? Conflict(JsonObject b) => b["sections"]![0]?["links"]!.AsArray().SingleOrDefault(l => (string?)l!["state"] == "conflict");

    static string Md(SpecApp app, string key) => File.ReadAllText(new TicketPaths(app.Root, key).BundleMarkdown);

    [Fact]
    public void An_unanswered_flag_is_marked_plainly_in_the_bundle()
    {
        using var app = App("fsd:\n  findings: true\n");
        OrdersPage(app);
        Fsd(app);
        FlagTheConflict(app);
        Ticket(app, "SHOP-30", "On /orders the total is wrong.");
        var b = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-30");
        var flag = Conflict(b)!["security"]!;
        Assert.Equal("unanswered", (string?)flag["answer"]);
        Assert.Equal("The server trusts an id sent in the request.", (string?)flag["why"]);
        Assert.Contains("**SECURITY FLAG, UNANSWERED:** The server trusts an id sent in the request. (`web/src/Orders.tsx:3-3`", Md(app, "SHOP-30"));
    }

    [Fact]
    public void An_answered_flag_says_who_and_no_flag_says_nothing()
    {
        using var app = App("fsd:\n  findings: true\n");
        OrdersPage(app);
        Fsd(app);
        Ticket(app, "SHOP-31", "On /orders the total is wrong.");
        var plain = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-31");
        Assert.DoesNotContain("security", plain.ToJsonString());
        Assert.DoesNotContain("SECURITY", Md(app, "SHOP-31"));

        FlagTheConflict(app);
        var id = (string)TraceFile.Load(app.Root)["entries"]!.AsArray().OfType<JsonObject>().Single(x => (string?)x["state"] == "conflict")["id"]!;
        TraceFindings.AnswerSecurity(app.Root, id, "not-security", "pat", "wrong line");
        var answered = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-31");
        Assert.Equal("not-security", (string?)Conflict(answered)!["security"]!["answer"]);
        Assert.Contains("security flag not-security by pat", Md(app, "SHOP-31"));
        Assert.DoesNotContain("UNANSWERED", Md(app, "SHOP-31"));
    }

    [Fact]
    public void A_closed_finding_leaves_the_bundle_unless_its_flag_is_unanswered()
    {
        static void Close(JsonObject e)
        {
            var disproof = new JsonObject { ["inputs"] = new string('b', 64), ["why"] = "w" };
            e["verdict"] = "refuted";
            e["disproof"] = disproof;
            e["disproof_hash"] = TraceFile.DisproofHash("refuted", disproof);
            var note = new JsonObject { ["by"] = "pat", ["why"] = "w" };
            e["review"] = "confirmed";
            e["review_note"] = note;
            e["review_hash"] = TraceFile.ReviewHash("confirmed", note, (string?)e["disproof_hash"]);
        }

        using var app = App("fsd:\n  findings: true\n");
        OrdersPage(app);
        Fsd(app);
        FlagTheConflict(app, Close);
        Ticket(app, "SHOP-32", "On /orders the total is wrong.");
        var open = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-32");
        Assert.Equal("unanswered", (string?)Conflict(open)!["security"]!["answer"]);

        var id = (string)TraceFile.Load(app.Root)["entries"]!.AsArray().OfType<JsonObject>().Single(x => (string?)x["state"] == "conflict")["id"]!;
        TraceFindings.AnswerSecurity(app.Root, id, "not-security", "pat", null);
        Assert.Null(Conflict(TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-32")));
    }

    [Fact]
    public void With_findings_switched_off_an_unanswered_flag_still_shows()
    {
        using var app = App();
        OrdersPage(app);
        Fsd(app);
        FlagTheConflict(app);
        Ticket(app, "SHOP-33", "On /orders the total is wrong.");
        var b = TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-33");
        Assert.Equal("unanswered", (string?)Conflict(b)!["security"]!["answer"]);
    }

    [Fact]
    public void Same_inputs_give_the_same_bundle_bytes_even_after_pulling_again()
    {
        using var app = App();
        OrdersPage(app);
        Fsd(app);
        Ticket(app, "SHOP-7", "On /orders `OrderService.Place` gives the wrong total.");
        TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-7");
        var paths = new TicketPaths(app.Root, "SHOP-7");
        var (json, md) = (File.ReadAllText(paths.BundleJson), File.ReadAllText(paths.BundleMarkdown));

        Ticket(app, "SHOP-7", "On /orders `OrderService.Place` gives the wrong total.");
        TicketBundle.Build(app.Repo, TicketContext.Load(app.Repo), "SHOP-7");
        Assert.Equal(json, File.ReadAllText(paths.BundleJson));
        Assert.Equal(md, File.ReadAllText(paths.BundleMarkdown));
    }
}

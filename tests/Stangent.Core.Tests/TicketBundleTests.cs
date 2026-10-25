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

    static SpecApp App(string extra = "") => new($"version: 1\ntickets:\n  site: acme.atlassian.net\n  replay: replay\n{extra}");

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

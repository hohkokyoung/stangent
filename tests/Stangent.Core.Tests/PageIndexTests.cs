using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Tickets;

namespace Stangent.Core.Tests;

/// <summary>The page index (D87) on the shop app: what it reads, how it ranks, when code picks a clear winner. No AI.</summary>
public class PageIndexTests
{
    const string OrdersRoute = "page:/orders";
    const string CartRoute = "page:/cart";
    const string HomeRoute = "page:/home";

    static TicketText Ticket(string summary, string description = "", params string[] comments) =>
        new([("summary", summary), ("description", description), .. comments.Select((c, i) => ($"comment {i + 1}", c))], []);

    static PageIndex Index(SpecApp app) => PageIndex.Build(TicketContext.Load(app.Repo), app.Root, app.Repo.Config);

    [Fact]
    public void Terms_split_names_fold_plurals_and_drop_stop_words_and_numbers()
    {
        Assert.Equal(["quality", "profile", "item", "size"], Terms.Of("the QualityProfileItemSizes"));
        Assert.Equal(["order", "approval"], Terms.Of("Orders over 100 need approval").Except(["over", "need"]));
        Assert.Equal(["import", "list", "sync", "service"], Terms.Of("import_list ImportListSyncService").Distinct());
        // A word and its plural meet, also when it ends in -ing.
        foreach (var (one, many) in new[] { ("setting", "settings"), ("mapping", "mappings"), ("string", "strings"), ("box", "boxes"),
                     ("process", "processes"), ("status", "status"), ("category", "categories") })
            Assert.Equal(Terms.Of(one), Terms.Of(many));
        // Punctuation splits phrases: "Docker: Yes" is two segments, "Emby.MediaBrowser" one.
        var docker = Terms.Tokens("Docker: Yes");
        Assert.NotEqual(docker[0].Segment, docker[1].Segment);
        var emby = Terms.Tokens("Emby.MediaBrowser");
        Assert.All(emby, t => Assert.Equal(emby[0].Segment, t.Segment));
    }

    [Fact]
    public void Every_page_is_ranked_and_the_order_is_stable()
    {
        using var app = new SpecApp();
        var index = Index(app);
        var ticket = Ticket("Something is off", "Nothing here names a page.");
        var a = index.Rank(ticket);
        var b = index.Rank(ticket);
        Assert.Equal(index.Pages.Count, a.Pages.Count);
        Assert.Equal(a.Pages.Select(p => p.Page.Id), b.Pages.Select(p => p.Page.Id));
        Assert.Equal(a.Pages.Select(p => p.Page.Id).Order(StringComparer.Ordinal), a.Pages.Where(p => p.Score == 0).Select(p => p.Page.Id));
        Assert.Null(a.Winner);
    }

    [Fact]
    public void A_message_in_the_code_makes_its_page_a_clear_winner_with_the_tickets_words_as_evidence()
    {
        using var app = new SpecApp();
        var ranking = Index(app).Rank(Ticket("Checkout fails", "When I check out I get: orders over 100 NEED approval. Then nothing."));
        Assert.Equal(SpecApp.Post, ranking.Winner?.Page.Id);
        Assert.Equal("orders over 100 NEED approval", ranking.Winner!.Phrase!.Quote);   // the ticket's own words, as written
        Assert.Equal(["order", "need", "approval"], ranking.Winner.Phrase.Terms);        // the whole run, not a piece of it
        Assert.Equal(IndexSource.Text, ranking.Winner.Phrase.Unit.Source);
        Assert.Contains("Orders over 100 need approval", ranking.Winner.Phrase.Unit.Text);
        Assert.True(ranking.AlsoPossibly.Count() <= PageIndex.AlsoPossible);
        Assert.DoesNotContain(ranking.AlsoPossibly, p => p.Page.Id == SpecApp.Post);
    }

    [Fact]
    public void Markup_text_finds_the_screen()
    {
        using var app = new SpecApp();
        var ranking = Index(app).Rank(Ticket("The place order button does nothing"));
        Assert.Equal(OrdersRoute, ranking.Pages[0].Page.Id);
        Assert.Equal("place order", string.Join(' ', ranking.Pages[0].Phrase!.Terms));
    }

    [Fact]
    public void A_key_into_the_projects_text_file_reads_as_its_text()
    {
        using var app = new SpecApp("version: 1\ntickets:\n  textFiles: [web/en.json]\n");
        app.Write("web/en.json", """{ "CartTitle": "Your shopping basket", "nested": { "Help": "Ask a florist" } }""");
        app.Write("web/src/Other.tsx", """
            export function CartPage() { return <Button label={translate('CartTitle')} hint={t("nested.Help")} />; }
            export function AccountPage() { return <Button label="Account" />; }
            """);
        var index = Index(app);
        Assert.Equal(CartRoute, index.Rank(Ticket("Your shopping basket heading overlaps")).Winner?.Page.Id);
        Assert.Equal(CartRoute, index.Rank(Ticket("ask a florist link is broken")).Pages[0].Page.Id);
    }

    [Fact]
    public void A_missing_text_file_is_a_config_error()
    {
        using var app = new SpecApp("version: 1\ntickets:\n  textFiles: [web/missing.json]\n");
        var e = Assert.Throws<StangentException>(() => Index(app));
        Assert.Equal("bad_config", e.Code);
    }

    [Fact]
    public void Resx_values_the_code_names_and_a_razor_view_are_read()
    {
        using var app = new SpecApp();
        app.Write("Core/Resources.resx", """
            <?xml version="1.0" encoding="utf-8"?>
            <root><data name="Declined" xml:space="preserve"><value>The card issuer declined payment</value></data></root>
            """);
        app.Write("Core/OrderService.cs", """
            public class OrderService
            {
                public Order Place(decimal total)
                {
                    if (total > 100) throw new ArgumentException(Resources.Declined);
                    events.Publish(new OrderPlaced());
                    return new Order { Total = total };
                }
            }
            """);
        app.Write("Api/Views/Home/Index.cshtml", "<h2>Welcome back to the flower shop</h2>\n<p>@Model.Name</p>");
        var index = Index(app);
        Assert.Equal(SpecApp.Post, index.Rank(Ticket("Card issuer declined payment on a small order")).Pages[0].Page.Id);
        Assert.Equal(HomeRoute, index.Rank(Ticket("welcome back to the flower shop shows twice")).Winner?.Page.Id);
    }

    [Fact]
    public void Written_titles_statements_and_fsd_sections_are_indexed()
    {
        using var app = new SpecApp();
        var before = Index(app);
        Assert.All(before.Rank(Ticket("thank-you note never arrives")).Pages, p => Assert.Equal(0, p.Score));
        Assert.All(before.Rank(Ticket("the postcard goes out by courier twice")).Pages, p => Assert.Equal(0, p.Score));

        var page = new JsonObject
        {
            ["id"] = SpecApp.Receipt, ["title"] = "Receipt emails",
            ["sections"] = new JsonObject { ["shows"] = new JsonArray(), ["actions"] = new JsonArray(), ["rules"] = new JsonArray(),
                ["sideEffects"] = new JsonArray(new JsonObject { ["text"] = "Sends a thank-you note once an order is placed.", ["cites"] = new JsonArray() }) },
        };
        app.Write("specs/reference/backend/receipt.json", page.ToJsonString());
        app.Write("specs/fsd/shop-fsd/document.json", """
            {"id":"shop-fsd","sections":[{"id":"2","title":"After the sale","text":"A postcard goes out by courier."}]}
            """);
        app.Write("specs/trace.yaml", $"""
            version: 1
            entries:
              - id: "L1"
                state: "agree"
                fsd: {"{"} doc: "shop-fsd", section: "2", title: "After the sale", quote: "A postcard goes out by courier." {"}"}
                page: {"{"} id: "{SpecApp.Receipt}", statement: "E1", quote: "Sends a thank-you note" {"}"}
            """);
        var index = Index(app);
        Assert.Contains(index.UnitsOf(SpecApp.Receipt), u => u.Unit.Source == IndexSource.Title && u.Unit.Text == "Receipt emails");
        var note = index.Rank(Ticket("thank-you note never arrives"));
        Assert.Equal(SpecApp.Receipt, note.Pages[0].Page.Id);
        Assert.Equal(IndexSource.Statement, note.Pages[0].Phrase!.Unit.Source);
        var postcard = index.Rank(Ticket("the postcard goes out by courier twice"));
        Assert.Equal(SpecApp.Receipt, postcard.Pages[0].Page.Id);
        Assert.Equal(IndexSource.Fsd, postcard.Pages[0].Phrase!.Unit.Source);
    }

    [Fact]
    public void A_text_file_that_is_not_valid_json_is_a_config_error()
    {
        using var app = new SpecApp("version: 1\ntickets:\n  textFiles: [web/en.json]\n");
        app.Write("web/en.json", "{ not json");
        Assert.Equal("bad_config", Assert.Throws<StangentException>(() => Index(app)).Code);
    }

    [Fact]
    public void An_english_resx_counts_when_there_is_no_neutral_one_and_its_keys_read_by_string()
    {
        using var app = new SpecApp();
        app.Write("Core/SharedResources.en.resx", """
            <?xml version="1.0" encoding="utf-8"?>
            <root><data name="TooBig" xml:space="preserve"><value>Bouquets above a hundred stems need a florist</value></data></root>
            """);
        app.Write("Core/SharedResources.fr.resx", """
            <?xml version="1.0" encoding="utf-8"?>
            <root><data name="TooBig" xml:space="preserve"><value>Les bouquets de plus de cent tiges</value></data></root>
            """);
        app.Write("Core/OrderService.cs", """
            public class OrderService
            {
                public Order Place(decimal total)
                {
                    if (total > 100) throw new ArgumentException(localizer["TooBig"]);
                    events.Publish(new OrderPlaced());
                    return new Order { Total = total };
                }
            }
            """);
        var index = Index(app);
        Assert.Equal(SpecApp.Post, index.Rank(Ticket("bouquets above a hundred stems are refused")).Pages[0].Page.Id);
        Assert.All(index.Rank(Ticket("tiges")).Pages, p => Assert.Equal(0, p.Score));
    }

    [Fact]
    public void Template_lines_and_form_field_keys_are_not_the_reporters_words()
    {
        using var app = new SpecApp();
        var index = Index(app);
        var ranking = index.Rank(Ticket("Crash", """
            ### Place order
            - [x] Place order
            - Place order: no
            """));
        Assert.All(ranking.Pages, p => Assert.Equal(0, p.Score));
    }

    [Fact]
    public void Two_pages_that_match_alike_give_no_clear_winner()
    {
        using var app = new SpecApp();
        app.Write("web/src/Other.tsx", """
            export function CartPage() { return <Button label="Gift wrap" />; }
            export function AccountPage() { return <Button label="Gift wrap" />; }
            """);
        var ranking = Index(app).Rank(Ticket("Gift wrap is missing"));
        Assert.Null(ranking.Winner);
        Assert.Equal(["page:/account", CartRoute], ranking.Pages.Take(2).Select(p => p.Page.Id));
    }
}

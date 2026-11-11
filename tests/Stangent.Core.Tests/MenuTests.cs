using Stangent.Graph;

namespace Stangent.Core.Tests;

/// <summary>D221: the app's menus as <c>menu-item</c> facts, from C# data, Razor links, React data and Angular markup.</summary>
public class MenuTests(ShopGraph shop) : IClassFixture<ShopGraph>
{
    List<string> Menu(string file) =>
        [.. shop.Graph.AllFacts.Where(f => f.Kind == "menu-item" && f.File == file)
            .Select(f => $"{f["path"]} {f["name"]}|{f["key"]} → {f["address"]} ({f["via"]})")];

    [Fact]
    public void A_menu_built_in_csharp_reads_names_keys_and_urls_through_the_apps_own_helper()
    {
        // The root (one item, not a list) isn't the menu; its children are. A URL made at runtime makes no item.
        Assert.Equal(
        [
            "1 Stock|Admin.Stock →  (data)",
            "1.1 Clear stock|Admin.Stock.Clear → mvc:Admin/StockAdmin/Clear (data)",
            "1.2 Shelves| → /shelves (data)",
            "2 Orders| → mvc:Admin/Orders/List (data)",
            "3 Help| → https://example.com/help (data)",
            // A list built by Add calls: route names, a key by its property's name (D229).
            "1 |Account.Checkout → route:checkout (data)",
            "2 |Account.Register → route:Register (data)",
            "3 Orders| → /orders (data)",
        ], Menu("Shop.Web/Menu/AdminMenu.cs"));
        var clear = shop.Graph.AllFacts.Single(f => f.Kind == "menu-item" && f["path"] == "1.1" && f.File == "Shop.Web/Menu/AdminMenu.cs");
        Assert.Equal(("StockAdmin", "Clear", "Admin", "menu"), (clear["controller"], clear["action"], clear["area"], clear.Detector));
    }

    [Fact]
    public void Razor_links_side_by_side_are_a_list_and_a_list_right_after_a_link_is_its_children()
    {
        Assert.Equal(
        [
            "1 Register| → mvc:/Account/Register (markup)",
            "2 |Nav.Checkout → /checkout/ (markup)",
            "3 Settings| → mvc:/Account/Settings (markup)",
            "3.1 Clear stock| → mvc:Admin/StockAdmin/Clear (markup)",
            "3.2 Checkout again| → route:checkout (markup)",
        ], Menu("Shop.Web/Views/Shared/_Nav.cshtml"));
        Assert.All(shop.Graph.AllFacts.Where(f => f.Kind == "menu-item" && f.File == "Shop.Web/Views/Shared/_Nav.cshtml"),
            f => Assert.Equal(("view:Shop.Web/Views/Shared/_Nav.cshtml", "razor-views"), (f.Owner, f.Detector)));
    }

    [Fact]
    public void Frontend_menus_come_from_the_typescript_extractor_as_data_and_markup()
    {
        Assert.Equal(
        [
            "1 Stock| →  (data)",
            "1.1 Shelves| → /stock/1 (data)",
            "1.1.1 Prices| → /prices (data)",
            "1.2 New order| → /orders/new (data)",
            "2 Orders| → /orders/new (data)",
            "3 Help| → https://example.com/help (data)",
        ], Menu("Shop.Web/react-app/src/Sidebar.tsx"));
        Assert.Equal(
        [
            "1 Orders| → /orders (markup)",
            "2 New order| → /orders/new (markup)",
            "3 Stock| → /stock (markup)",
        ], Menu("Shop.Web/ClientApp/src/app/nav.component.ts"));
    }
}

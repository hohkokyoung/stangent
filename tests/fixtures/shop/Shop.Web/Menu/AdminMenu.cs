namespace Shop.Web.Menu;

public sealed class MenuItem
{
    public string SystemName { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public List<MenuItem> ChildNodes { get; set; } = [];
}

/// <summary>D221: a menu built in C#, the way nopCommerce's AdminMenu is: a root whose children are the menu, URLs through the app's own helper.</summary>
public sealed class AdminMenu(LinkGenerator links, IHttpContextAccessor http)
{
    static string Text(string key) => key;

    public MenuItem Build() => new()
    {
        SystemName = "Home",
        Title = Text("Admin.Home"),
        Url = GetMenuItemUrl("Home", "Index"),
        ChildNodes = new List<MenuItem>
        {
            new() { SystemName = "Stock", Title = Text("Admin.Stock"), ChildNodes = new List<MenuItem>
            {
                new() { SystemName = "Clear stock", Title = Text("Admin.Stock.Clear"), Url = GetMenuItemUrl("StockAdmin", "Clear") },
                new() { SystemName = "Shelves", Url = "/shelves" },
            } },
            new() { SystemName = "Orders", Url = GetMenuItemUrl("Orders", "List") },
            new() { SystemName = "Help", Url = "https://example.com/help" },
            new() { SystemName = "Dynamic", Url = GetMenuItemUrl("Orders", Guid.NewGuid().ToString()) },
        },
    };

    public string? GetMenuItemUrl(string controllerName, string actionName)
    {
        if (http.HttpContext is not { } context) return null;
        return links.GetPathByAction(context, actionName, controllerName, new { area = "Admin" });
    }
}

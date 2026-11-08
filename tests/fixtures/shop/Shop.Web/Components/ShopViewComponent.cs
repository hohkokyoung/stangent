using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;

namespace Shop.Web.Components;

// T4: a base helper that forwards to View(…), the way nopCommerce's NopViewComponent.ViewAsync does.
public abstract class ShopViewComponent : ViewComponent
{
    public Task<ViewViewComponentResult> ViewAsync<TModel>(TModel model) => Task.FromResult(View(model));

    public Task<ViewViewComponentResult> ViewAsync(string viewName) => Task.FromResult(View(viewName));
}

public class CartViewComponent : ShopViewComponent
{
    public Task<ViewViewComponentResult> InvokeAsync() => ViewAsync(new { Count = 1 });
}

public class BadgeViewComponent : ShopViewComponent
{
    public Task<ViewViewComponentResult> InvokeAsync() => ViewAsync("Small");
}

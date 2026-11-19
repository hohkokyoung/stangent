using Microsoft.AspNetCore.Mvc;

namespace Shop.Web.Components;

public class NewsletterViewComponent : ViewComponent
{
    public IViewComponentResult Invoke() => View();
}

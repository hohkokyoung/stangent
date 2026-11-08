using Microsoft.AspNetCore.Mvc;

namespace Shop.Web.Components;

// V1: MVC looks a component's view up in the folder of the page's controller first (Views/Account/Components/Tips/…).
public class TipsViewComponent : ViewComponent
{
    public IViewComponentResult Invoke() => View();
}

public class SummaryViewComponent : ViewComponent
{
    public IViewComponentResult Invoke() => View();
}

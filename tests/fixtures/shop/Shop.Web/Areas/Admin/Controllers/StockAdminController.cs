using Microsoft.AspNetCore.Mvc;

namespace Shop.Web.Areas.Admin.Controllers;

// [Area] on a base controller holds for those derived from it, as ASP.NET reads it.
[Area("Admin")]
public abstract class AdminController : Controller;

public class StockAdminController : AdminController
{
    [HttpPost]
    public IActionResult Clear() => View();
}

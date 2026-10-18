using Microsoft.AspNetCore.Mvc;
using Shop.Core;

namespace Shop.Web.Controllers;

public class CheckoutController(IOrderService orders) : Controller
{
    public IActionResult Index() => Content(orders.PlaceOrder("web", 2m).Customer);
}

[ApiController]
[Route("api/legacy")]
public class LegacyOrdersController(IOrderService orders) : ControllerBase
{
    [HttpPost("place")]
    public Order Place() => orders.PlaceOrder("legacy", 3m);
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Shop.Web.Controllers;

// Verb attributes of an app's own, the way Sonarr writes them (G1).
public class PostShelfAttribute : HttpPostAttribute;

public class DeleteShelfAttribute : HttpDeleteAttribute
{
    public DeleteShelfAttribute() : base("{id:int}") { }
}

public class GetShelfAttribute : System.Attribute, IActionHttpMethodProvider, IRouteTemplateProvider
{
    public IEnumerable<string> HttpMethods => new[] { "GET" };
    public string Template => "{id:int}";
    public int? Order => 0;
    public string? Name { get; }
}

[Microsoft.AspNetCore.Authorization.Authorize(Policy = "Staff")]
public abstract class StaffController : Controller
{
    [HttpPut("{id:int?}")]
    public virtual int Save(int id) => id;

    [GetShelf]
    public virtual int Find(int id) => id;
}

[ApiController]
[Route("api/shelf")]
public class ShelfController : StaffController
{
    // Controller.OnActionExecuting is [NonAction]; so is this override.
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context) { }

    [PostShelf]
    public int Add() => 1;

    [DeleteShelf]
    public void Remove(int id) { }

    [GetShelf, Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public int Get(int id) => id;

    // F1: inherits the base's [HttpPut]; the base method isn't routed apart.
    public override int Save(int id) => id + 1;

    // F1: [NonAction] on an override takes the base method out too.
    [NonAction]
    public override int Find(int id) => id;
}

// A route attribute whose Template reads a property set just before it (Sonarr's feed controllers).
public class FeedAttribute : System.Attribute, IRouteTemplateProvider
{
    public FeedAttribute(int version, string resource)
    {
        Version = version;
        Template = $"feed/v{Version}/{resource}";
    }

    public string? Template { get; private set; }
    public int? Order => 0;
    public string? Name { get; set; }
    public int Version { get; private set; }
}

[Feed(2, "calendar")]
public class CalendarFeedController : Controller
{
    [HttpGet("shop.ics")]
    public string Feed() => "";
}

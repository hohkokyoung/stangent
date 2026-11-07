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
public abstract class StaffController : Controller;

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
}

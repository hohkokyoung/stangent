using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Shop.Web.Models;

namespace Shop.Web.Controllers;

// A server-rendered MVC controller, the way nopCommerce writes one: its own access filters (N3), a named conventional
// route (Program.cs), and views found by MVC's lookup rules (N1, N2).

public static class RouteNames
{
    public const string Register = "Register";
}

public static class Permissions
{
    public const string ManageAccounts = "Accounts.Manage";
}

public enum DenyAs { Default, Json }

/// <summary>Runs at the authorisation stage, so it's an access rule whatever its name.</summary>
public sealed class CheckPermissionAttribute : TypeFilterAttribute
{
    public CheckPermissionAttribute(string permission, DenyAs deny = DenyAs.Default) : base(typeof(CheckPermissionFilter)) => Arguments = [permission, deny];

    sealed class CheckPermissionFilter(string permission, DenyAs deny) : IAsyncAuthorizationFilter
    {
        public Task OnAuthorizationAsync(AuthorizationFilterContext context) => Task.CompletedTask;
        public override string ToString() => permission + deny;
    }
}

/// <summary>An action filter, but its name says it controls access.</summary>
public sealed class CheckAccessStoreAttribute : TypeFilterAttribute
{
    public CheckAccessStoreAttribute(bool ignore = false) : base(typeof(CheckAccessStoreFilter)) => Arguments = [ignore];
}

public sealed class CheckAccessStoreFilter(bool ignore) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context) { if (ignore) return; }
    public void OnActionExecuted(ActionExecutedContext context) { }
}

/// <summary>Neither: not an access rule.</summary>
public sealed class SaveLastPageAttribute : TypeFilterAttribute
{
    public SaveLastPageAttribute() : base(typeof(SaveLastPageFilter)) { }
}

public sealed class SaveLastPageFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context) { }
    public void OnActionExecuted(ActionExecutedContext context) { }
}

/// <summary>T5: an action filter whose name says nothing, but whose code refuses the request.</summary>
public sealed class ValidateIpAttribute : TypeFilterAttribute
{
    public ValidateIpAttribute() : base(typeof(ValidateIpFilter)) { }

    sealed class ValidateIpFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context) => context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}

/// <summary>T6: runs at the authorisation stage but only redirects to HTTPS: 🟡, not proof of an access rule.</summary>
public sealed class HttpsOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context) => context.Result = new RedirectResult("https://shop.example/");
}

/// <summary>T5: an action filter that ends the request with a result of its own: 🟡.</summary>
public sealed class MaintenanceAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context) => context.Result = new ContentResult { Content = "Back soon" };
}

/// <summary>V4: refuses by "not found" (hiding what exists), in a base class: still a refusal at the authorisation stage.</summary>
public abstract class OwnerGuardAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context) => context.Result = new NotFoundResult();
}

public sealed class OwnedOnlyAttribute : OwnerGuardAttribute;

public sealed class SignedInFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context) { }
}

[CheckAccessStore]
public class AccountController : Controller
{
    [HttpGet, SaveLastPage]
    public IActionResult Register() => View(new RegisterModel());

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Register(RegisterModel model)
    {
        if (!ModelState.IsValid) return View(model);
        return RedirectToAction(nameof(Welcome));
    }

    public IActionResult Welcome() => View("Done");

    // Attribute-routed pages for the form cases (T1, T3, T10).
    [HttpGet("account/settings"), ValidateIp, HttpsOnly, Maintenance, SaveLastPage, OwnedOnly]
    public IActionResult Settings() => View();

    [HttpPost("account/settings")]
    public IActionResult Settings(RegisterModel model) => View();

    [Route("account/notes")]
    public IActionResult Notes() => Content("");

    [HttpPost("account/notes")]
    public IActionResult Notes(RegisterModel model) => Content("");

    [HttpPost("account/archive")]
    public IActionResult Archive() => Content("");

    [CheckPermission(Permissions.ManageAccounts, DenyAs.Json), TypeFilter(typeof(SignedInFilter)), Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public IActionResult Close() => PartialView("_Closed");

    // Y1: a view picked at runtime (a theme or template page) still opens a page.
    [HttpGet("account/themed")]
    public IActionResult Themed(string template) => View(template);
}

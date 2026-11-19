using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Shop.Web;

// D186 (ruling R4): an app's own helper that ends the request is an exit where it's returned; one that doesn't isn't.
public class PageHelpers
{
    static IActionResult Gone(HttpResponse response)
    {
        response.StatusCode = 404;
        return new EmptyResult();
    }

    static IActionResult Refuse(bool hard) => hard ? new ForbidResult() : new ForbidResult();

    static IActionResult Plain() => new EmptyResult();

    public IActionResult Show(HttpResponse response, int id)
    {
        if (id < 0) return Gone(response);
        if (id == 0) return Refuse(true);
        return Plain();
    }
}

// D187 (ruling R5): a filter setting context.Result to a refusal stops the request: an exit; one setting anything else isn't.
public class OpenStoreFilter : Microsoft.AspNetCore.Mvc.Filters.IAuthorizationFilter
{
    public void OnAuthorization(Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            context.Result = new ChallengeResult();
        else
            context.Result = new EmptyResult();
    }
}

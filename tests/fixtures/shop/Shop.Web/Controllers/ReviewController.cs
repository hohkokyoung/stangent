using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Shop.Web.Controllers;

// D207: request filters that check the request before the action: a captcha that hands the action its verdict, and the
// framework's automatic antiforgery check on the controller (not on GET, and not where an action ignores it).

public sealed class CheckCaptchaAttribute : TypeFilterAttribute
{
    public CheckCaptchaAttribute() : base(typeof(CheckCaptchaFilter)) { }

    sealed class CheckCaptchaFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context) => context.ActionArguments["captchaValid"] = context.HttpContext.Request.Form.ContainsKey("captcha");
        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}

[AutoValidateAntiforgeryToken]
public class ReviewController : Controller
{
    [HttpGet("reviews/write")]
    public IActionResult Write() => View();

    [HttpPost("reviews/write"), CheckCaptcha]
    public IActionResult Write(string text, bool captchaValid) => captchaValid ? Content(text) : BadRequest();

    [HttpPost("reviews/preview"), IgnoreAntiforgeryToken]
    public IActionResult Preview(string text) => Content(text);
}

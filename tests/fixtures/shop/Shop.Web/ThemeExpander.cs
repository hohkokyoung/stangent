using Microsoft.AspNetCore.Mvc.Razor;

namespace Shop.Web;

// T8: an app's own view location rule (nopCommerce's themes): Themes/{theme}/Views/… first.
public sealed class ThemeExpander : IViewLocationExpander
{
    public void PopulateValues(ViewLocationExpanderContext context) => context.Values["theme"] = "Dark";

    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations) =>
        viewLocations.Select(l => l.Replace("/Views/", "/Themes/Dark/Views/")).Concat(viewLocations);
}

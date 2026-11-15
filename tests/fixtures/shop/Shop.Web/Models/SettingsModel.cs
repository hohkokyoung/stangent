using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Shop.Web.Models;

/// <summary>D329: an app's own display-name attribute (named <c>…DisplayNameAttribute</c>, deriving from nothing special).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ShopResourceDisplayNameAttribute(string resourceKey) : Attribute
{
    public string ResourceKey { get; } = resourceKey;
}

public enum Theme { Light, Dark }

public class SettingsModel
{
    [ShopResourceDisplayName("Shop.Settings.Fields.Name")]
    public string Name { get; set; } = "";

    [Display(Name = "Shop.Settings.Fields.Theme", Description = "Shop.Settings.Fields.Theme.Hint")]
    public Theme Theme { get; set; }

    // No attribute names it: not a server-defined field.
    public string Notes { get; set; } = "";

    public List<SelectListItem> AvailableSizes { get; set; } = [];
}

/// <summary>D329: a helper given markup as a Razor template, as nopCommerce's localized editors are.</summary>
public static class TemplateHelpers
{
    public static Microsoft.AspNetCore.Html.IHtmlContent Localized(this IHtmlHelper html, Func<int, Microsoft.AspNetCore.Mvc.Razor.HelperResult> template) => template(0);
}

/// <summary>D329: an app's own editor; a field because it is bound with asp-for, never by its name.</summary>
[HtmlTargetElement("shop-editor", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ShopEditorTagHelper : TagHelper
{
    [HtmlAttributeName("asp-for")]
    public ModelExpression For { get; set; } = null!;
}

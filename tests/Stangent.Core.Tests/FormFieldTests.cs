using Stangent.Graph;

namespace Stangent.Core.Tests;

/// <summary>D329: <c>form-field</c> facts in C#: fields the server defines (attributes), their choices, and Razor's fields.</summary>
public class FormFieldTests(ShopGraph shop) : IClassFixture<ShopGraph>
{
    List<string> Fields(string file) =>
        [.. shop.Graph.AllFacts.Where(f => f.Kind == "form-field" && f.File == file).OrderBy(f => f.Line)
            .Select(f => $"{f.Line} {f["element"]} {f["bind"]}={f["to"]} {f["type"]} {f["via"]} {f["label"]}{f["labelText"]}|{f["help"]} {f["choices"]}")];

    [Fact]
    public void A_property_named_by_a_label_argument_is_a_server_defined_field_with_help_and_choices()
    {
        Assert.Equal(
        [
            "33 property Url=Shop.Web.Providers.FeedSettings string server FeedUrl|FeedUrlHelp ",
            "36 property Mode=Shop.Web.Providers.FeedSettings int server FeedMode| enum:FeedMode",
            "39 property Tags=Shop.Web.Providers.FeedSettings IEnumerable<int> server FeedTags| server:feedTags",
        ], Fields("Shop.Web/Providers/FeedSettings.cs"));
        var url = shop.Graph.AllFacts.Single(f => f.Kind == "form-field" && f["bind"] == "Url");
        Assert.Equal(("form-fields", "FieldDef"), (url.Detector, url["attribute"]));
        Assert.Contains("Label = \"FeedUrl\"", url["args"]);
    }

    [Fact]
    public void A_display_name_attribute_names_a_model_property_and_an_enum_members_label_makes_it_a_choice()
    {
        Assert.Equal(
        [
            "19 property Name=Shop.Web.Models.SettingsModel string server Shop.Settings.Fields.Name| ",
            "22 property Theme=Shop.Web.Models.SettingsModel Theme server Shop.Settings.Fields.Theme|Shop.Settings.Fields.Theme.Hint enum:Theme",
        ], Fields("Shop.Web/Models/SettingsModel.cs"));
        Assert.Equal(["Fast FeedModeFast|", "Slow |slow but sure"],
            shop.Graph.AllFacts.Where(f => f.Kind == "field-choice").Select(f => $"{f["member"]} {f["label"]}|{f["help"]}"));
    }

    [Fact]
    public void Razor_fields_bound_with_asp_for_or_native_with_their_labels_and_choices()
    {
        Assert.Equal(
        [
            "5 input asp-for=Name text binding | ",
            "6 select asp-for=Theme select binding | enum:Theme",
            "8 textarea name=notes textarea native Shop.Settings.Notes| ",
            "10 shop-editor asp-for=Notes shop-editor binding | ",
            "11 select name=size select native | list:Shop.Size.Small|Large",
            "12 select asp-for=Name select binding | runtime:Model.AvailableSizes",
            // Markup handed to a helper as a Razor template is the view's own (nopCommerce's localized editors).
            "14 input asp-for=Name text binding | ",
        ], Fields("Shop.Web/Views/Settings/Edit.cshtml"));
        var name = shop.Graph.AllFacts.First(f => f.Kind == "form-field" && f.File == "Shop.Web/Views/Settings/Edit.cshtml");
        Assert.Equal(("view:Shop.Web/Views/Settings/Edit.cshtml", "razor-views", "Shop.Web.Models.SettingsModel"), (name.Owner, name.Detector, name["model"]));
    }
}

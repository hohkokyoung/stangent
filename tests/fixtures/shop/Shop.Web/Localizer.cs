namespace Shop.Web;

/// <summary>A view's text by key, the way nopCommerce's views call it: <c>@T("Account.Register.Button")</c> (N1).</summary>
public delegate Microsoft.AspNetCore.Html.HtmlString Localizer(string key);

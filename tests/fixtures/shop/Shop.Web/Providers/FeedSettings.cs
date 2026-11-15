namespace Shop.Web.Providers;

/// <summary>D329: fields a generic form asks the server for, defined by an attribute with a Label argument.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FieldDefAttribute(int order) : Attribute
{
    public int Order { get; } = order;
    public string Label { get; set; } = "";
    public string HelpText { get; set; } = "";
    public Type? SelectOptions { get; set; }
    public string OptionsProvider { get; set; } = "";
    public bool Advanced { get; set; }
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class FieldChoiceAttribute(string? label = null) : Attribute
{
    public string? Label { get; set; } = label;
    public string Hint { get; set; } = "";
}

public enum FeedMode
{
    [FieldChoice("FeedModeFast")]
    Fast,
    [FieldChoice(Hint = "slow but sure")]
    Slow,
    Off,
}

public class FeedSettings
{
    [FieldDef(0, Label = "FeedUrl", HelpText = "FeedUrlHelp")]
    public string Url { get; set; } = "";

    [FieldDef(1, Label = "FeedMode", SelectOptions = typeof(FeedMode), Advanced = true)]
    public int Mode { get; set; }

    [FieldDef(2, Label = "FeedTags", OptionsProvider = "feedTags")]
    public IEnumerable<int> Tags { get; set; } = [];

    [FieldDef(3)]
    public FeedMode Fallback { get; set; }
}

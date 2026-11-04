using System.Text.Json.Nodes;
using Stangent.Core;

/// <summary><c>stangent components</c> (D121).</summary>
static class ComponentReport
{
    public static JsonObject Build(StangentConfig config) => new()
    {
        ["components"] = new JsonArray([.. StangentComponents.All.Select(c =>
        {
            var o = new JsonObject
            {
                ["name"] = c.Name, ["label"] = c.Label, ["on"] = StangentComponents.IsOn(config, c.Name), ["default"] = c.OnByDefault,
                ["setting"] = c.Setting, ["switch"] = $"components.{c.Name}",
                ["needs"] = new JsonArray([.. c.Needs.Select(n => (JsonNode)n)]),
                ["neededBy"] = new JsonArray([.. StangentComponents.All.Where(d => d.Needs.Contains(c.Name)).Select(d => (JsonNode)d.Name)]),
            };
            if (StangentComponents.MissingNeed(config, c.Name) is { } missing) o["blocked"] = missing;
            return (JsonNode)o;
        })]),
        ["noSwitch"] = new JsonArray([.. StangentComponents.NoSwitches.Select(n => (JsonNode)new JsonObject { ["name"] = n.Name, ["why"] = n.Why })]),
    };
}

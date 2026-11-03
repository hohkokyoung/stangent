using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stangent.Core;
using YamlDotNet.RepresentationModel;

namespace Stangent.Spec.Requirements;

/// <summary>
/// The requirement files' YAML (D99): written by code in one fixed form, keys in the order given and every string
/// double-quoted, so the same content gives the same bytes. Read back as the JSON the schemas check; every scalar stays
/// a string except null and <c>version</c>.
/// </summary>
public static class YamlForm
{
    static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Write(JsonObject root, string? header = null)
    {
        var sb = new StringBuilder();
        if (header is not null) sb.Append(string.Join('\n', header.Split('\n').Select(l => "# " + l))).Append('\n');
        Mapping(sb, root, 0);
        return sb.ToString();
    }

    /// <summary>One item per line: a JSON object is a valid YAML flow mapping, so index files stay one line per item.</summary>
    public static string Line(JsonObject item) =>
        "{" + string.Join(", ", item.Select(p => $"{JsonSerializer.Serialize(p.Key, Json)}: {Scalar(p.Value)}")) + "}";

    static void Mapping(StringBuilder sb, JsonObject o, int indent)
    {
        var pad = new string(' ', indent);
        foreach (var (key, value) in o)
        {
            sb.Append(pad).Append(key).Append(':');
            Value(sb, value, indent);
        }
    }

    static void Value(StringBuilder sb, JsonNode? value, int indent)
    {
        switch (value)
        {
            case JsonObject m when m.Count == 0: sb.Append(" {}\n"); break;
            case JsonObject m: sb.Append('\n'); Mapping(sb, m, indent + 2); break;
            case JsonArray a when a.Count == 0: sb.Append(" []\n"); break;
            case JsonArray a:
                sb.Append('\n');
                foreach (var item in a) Item(sb, item, indent + 2);
                break;
            default: sb.Append(' ').Append(Scalar(value)).Append('\n'); break;
        }
    }

    static void Item(StringBuilder sb, JsonNode? item, int indent)
    {
        var pad = new string(' ', indent);
        if (item is not JsonObject { Count: > 0 } o)
        {
            sb.Append(pad).Append("- ");
            var inner = new StringBuilder();
            Value(inner, item, indent);
            sb.Append(inner.ToString().TrimStart(' '));
            return;
        }
        var first = true;
        foreach (var (key, value) in o)
        {
            sb.Append(pad).Append(first ? "- " : "  ").Append(key).Append(':');
            Value(sb, value, indent + 2);
            first = false;
        }
    }

    static string Scalar(JsonNode? n) => n is null ? "null"
        : n is JsonValue v && v.TryGetValue<string>(out var s) ? JsonSerializer.Serialize(s, Json) : n.ToJsonString();

    public static JsonObject Parse(string yaml, string what)
    {
        var stream = new YamlStream();
        try { stream.Load(new StringReader(yaml)); }
        catch (YamlDotNet.Core.YamlException e) { throw new StangentException("invalid_requirements", $"{what} isn't valid YAML: {e.Message}"); }
        if (stream.Documents.Count == 0) throw new StangentException("invalid_requirements", $"{what} is empty.");
        return ToJson(stream.Documents[0].RootNode, "") as JsonObject ?? throw new StangentException("invalid_requirements", $"{what} must be a mapping.");
    }

    static JsonNode? ToJson(YamlNode node, string key) => node switch
    {
        YamlMappingNode m => new JsonObject(m.Children.Select(c => KeyValuePair.Create(((YamlScalarNode)c.Key).Value!, ToJson(c.Value, ((YamlScalarNode)c.Key).Value!)))),
        YamlSequenceNode s => new JsonArray([.. s.Children.Select(c => ToJson(c, ""))]),
        YamlScalarNode { Value: null or "" or "~" or "null" } sc when sc.Style == YamlDotNet.Core.ScalarStyle.Plain => null,
        YamlScalarNode sc when key == "version" && int.TryParse(sc.Value, out var v) => v,
        YamlScalarNode sc => sc.Value,
        _ => null,
    };
}

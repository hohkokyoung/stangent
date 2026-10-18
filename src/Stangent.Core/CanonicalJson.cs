using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Stangent.Core;

/// <summary>
/// One byte-exact form per JSON value: keys sorted ordinally, 2-space indent, LF, trailing
/// newline. Artifact files are written this way so the same content always gives the same
/// file and hash, whoever wrote it.
/// </summary>
public static class CanonicalJson
{
    static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray arr => new JsonArray([.. arr.Select(Sort)]),
        null => null,
        _ => node.DeepClone(),
    };

    public static string Serialize(JsonNode node) => Sort(node)!.ToJsonString(Indented) + "\n";

    public static string SerializeCompact(JsonNode? node) => node is null ? "null" : Sort(node)!.ToJsonString(Compact);

    public static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

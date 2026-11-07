using System.Text.Json.Nodes;

namespace Stangent.Core;

/// <summary>
/// Compares several answers to one identical AI call (D161's repeat check), by code. A field is free text, and dropped, only
/// when its name is on <see cref="FreeText"/> and the call's own output schema leaves it a plain string everywhere it
/// appears: an id with a pattern, an enum or a constant is always a decision, whatever it's called. Lists count as sets,
/// and each top-level field, or each element of a top-level list, is one item. A step is steady only when every run gave
/// an answer and every run gives every item.
/// </summary>
public static class AiCompare
{
    /// <summary>Names that hold an AI's own words in the output schemas. Quotes and words code checks (<c>extra</c>) stay.</summary>
    public static readonly IReadOnlyList<string> FreeText =
        ["note", "notes", "reason", "why", "explanation", "text", "title", "statement", "covered", "missing", "summary", "rationale"];

    public static JsonObject Compare(IReadOnlyList<JsonNode?> answers, JsonNode schema)
    {
        if (answers.Count < 2) throw new StangentException("too_few_answers", "Comparing needs two answers or more.", ExitCodes.Usage);
        var dropped = Droppable(schema);
        var missing = answers.Select((a, i) => (a, i + 1)).Where(x => x.a is null).Select(x => x.Item2).ToList();
        var sets = answers.Select(a => Items(a, dropped)).ToList();
        var all = sets.SelectMany(s => s).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var agreed = all.Where(d => sets.All(s => s.Contains(d))).ToList();
        var differ = all.Where(d => !sets.All(s => s.Contains(d)))
            .Select(d => (JsonNode)new JsonObject
            {
                ["item"] = d,
                ["inRuns"] = new JsonArray([.. sets.Select((s, i) => (s.Contains(d), i + 1)).Where(x => x.Item1).Select(x => (JsonNode)x.Item2)]),
            }).ToList();
        return new JsonObject
        {
            ["runs"] = answers.Count,
            ["steady"] = missing.Count == 0 && differ.Count == 0,
            ["missing"] = new JsonArray([.. missing.Select(m => (JsonNode)m)]),
            ["decisions"] = all.Count,
            ["agreed"] = agreed.Count,
            ["differ"] = new JsonArray([.. differ]),
            ["ignored"] = new JsonArray([.. dropped.Order(StringComparer.Ordinal).Select(f => (JsonNode)f)]),
        };
    }

    /// <summary>The <see cref="FreeText"/> names this schema leaves a plain string at every place they appear.</summary>
    public static HashSet<string> Droppable(JsonNode schema)
    {
        var plain = new HashSet<string>(StringComparer.Ordinal);
        var constrained = new HashSet<string>(StringComparer.Ordinal);
        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o["properties"] is JsonObject props)
                        foreach (var (name, p) in props)
                            if (FreeText.Contains(name))
                                (IsPlainString(p) ? plain : constrained).Add(name);
                    foreach (var (_, v) in o) Walk(v);
                    break;
                case JsonArray a:
                    foreach (var v in a) Walk(v);
                    break;
            }
        }
        Walk(schema);
        plain.ExceptWith(constrained);
        return plain;
    }

    static bool IsPlainString(JsonNode? p) =>
        p is JsonObject o && (string?)o["type"] == "string" && o["enum"] is null && o["pattern"] is null && o["const"] is null && o["format"] is null;

    /// <summary>One answer's items, as canonical strings. A missing answer has one item of its own; one that isn't an object is itself.</summary>
    public static HashSet<string> Items(JsonNode? answer, IReadOnlySet<string> dropped)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (answer is null) { set.Add("no answer"); return set; }
        if (Strip(answer, dropped) is not JsonObject o) { set.Add("answer: " + CanonicalJson.SerializeCompact(Strip(answer, dropped))); return set; }
        foreach (var (key, value) in o)
            if (value is JsonArray list)
            {
                if (list.Count == 0) set.Add($"{key}: []");
                foreach (var item in list) set.Add($"{key}[]: {CanonicalJson.SerializeCompact(item)}");
            }
            else set.Add($"{key}: {CanonicalJson.SerializeCompact(value)}");
        return set;
    }

    /// <summary>The answer with free-text fields removed and every list sorted, so order and wording don't count.</summary>
    static JsonNode? Strip(JsonNode? node, IReadOnlySet<string> dropped) => node switch
    {
        JsonObject o => new JsonObject(o.Where(p => !dropped.Contains(p.Key)).Select(p => KeyValuePair.Create(p.Key, Strip(p.Value, dropped)))),
        JsonArray a => new JsonArray([.. a.Select(x => Strip(x, dropped)).OrderBy(CanonicalJson.SerializeCompact, StringComparer.Ordinal)]),
        _ => node?.DeepClone(),
    };
}

using Microsoft.CodeAnalysis;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// What an endpoint sends and receives (D26, D31): its response and request-body types, and each
/// type's JSON field names, so frontend types can be matched field by field (`maps-field`).
/// </summary>
static class ApiShapes
{
    const int MaxDepth = 3;

    static readonly HashSet<string> Wrappers =
    [
        "Task", "ValueTask", "ActionResult", "Ok", "Created", "CreatedAtRoute", "CreatedAtAction", "Accepted", "AcceptedAtRoute",
        "IEnumerable", "List", "IList", "ICollection", "IReadOnlyList", "IReadOnlyCollection", "IQueryable", "IAsyncEnumerable",
        "HashSet", "ISet", "Collection", "ReadOnlyCollection", "Nullable",
    ];

    static readonly HashSet<string> BindingAttributes =
        ["FromQueryAttribute", "FromRouteAttribute", "FromServicesAttribute", "FromHeaderAttribute", "FromFormAttribute", "FromKeyedServicesAttribute", "AsParametersAttribute"];

    /// <summary>Records the endpoint's types and, once per project, each type's JSON shape.</summary>
    public static void Record(ExtractContext ctx, string detector, IMethodSymbol method, string targetId, HashSet<string> seen)
    {
        var response = Unwrap(method.ReturnType);
        var body = BodyType(ctx, method);
        ctx.AddFact(detector, "endpoint-types", targetId, null,
            ("response", Id(ctx, response) ?? ""), ("body", Id(ctx, body) ?? ""));
        Shape(ctx, detector, response, seen, 0);
        Shape(ctx, detector, body, seen, 0);
    }

    /// <summary>Task&lt;ActionResult&lt;IEnumerable&lt;SeriesDto&gt;&gt;&gt; → SeriesDto; Results&lt;Ok&lt;T&gt;, NotFound&gt; → T; IActionResult → null.</summary>
    public static ITypeSymbol? Unwrap(ITypeSymbol? type, int depth = 0)
    {
        if (type is null || depth > 6) return null;
        if (type is IArrayTypeSymbol array) return Unwrap(array.ElementType, depth + 1);
        if (type is not INamedTypeSymbol named) return null;
        if (named.IsGenericType && named.Name == "Results")
            return named.TypeArguments.Select(a => Unwrap(a, depth + 1)).FirstOrDefault(t => t is not null);
        if (named.IsGenericType && Wrappers.Contains(named.Name)) return Unwrap(named.TypeArguments[0], depth + 1);
        if (named.SpecialType != SpecialType.None || named.TypeKind is TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate) return null;
        // A class that is a collection (Kavita's PagedList<T> : List<T>) is written as a JSON array of T.
        if (named.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T) is { } enumerable
            && !named.AllInterfaces.Any(i => i.Name is "IDictionary" or "IReadOnlyDictionary"))
            return Unwrap(enumerable.TypeArguments[0], depth + 1);
        return named.Locations.Any(l => l.IsInSource) ? named : null;
    }

    static ITypeSymbol? BodyType(ExtractContext ctx, IMethodSymbol method)
    {
        foreach (var p in method.Parameters)
            if (p.GetAttributes().Any(a => a.AttributeClass?.Name == "FromBodyAttribute")) return Unwrap(p.Type);
        foreach (var p in method.Parameters)
        {
            if (p.GetAttributes().Any(a => a.AttributeClass is { } c && BindingAttributes.Contains(c.Name))) continue;
            if (Unwrap(p.Type) is { } t && ctx.NodeId(t) is not null && t.TypeKind is TypeKind.Class or TypeKind.Struct) return t;
        }
        return null;
    }

    static string? Id(ExtractContext ctx, ITypeSymbol? t) => t is null ? null : ctx.NodeId(t);

    static void Shape(ExtractContext ctx, string detector, ITypeSymbol? type, HashSet<string> seen, int depth)
    {
        if (type is not INamedTypeSymbol named || depth > MaxDepth || ctx.NodeId(named) is not { } typeId || !seen.Add(typeId)) return;
        var props = new List<Dictionary<string, string>>();
        var nested = new List<ITypeSymbol>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var t = named; t is not null && t.SpecialType == SpecialType.None && t.Locations.Any(l => l.IsInSource); t = t.BaseType)
            foreach (var prop in t.GetMembers().OfType<IPropertySymbol>())
            {
                if (prop.IsStatic || prop.IsIndexer || prop.GetMethod is null || prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.GetAttributes().Any(a => a.AttributeClass?.Name is "JsonIgnoreAttribute")) continue;
                var json = JsonName(prop);
                if (!names.Add(json) || ctx.NodeId(prop) is not { } propId) continue;
                var propType = Unwrap(prop.Type);
                props.Add(new() { ["json"] = json, ["id"] = propId, ["type"] = Id(ctx, propType) ?? "" });
                if (propType is not null) nested.Add(propType);
            }
        if (props.Count > 0)
            ctx.AddFact(detector, "json-shape", typeId, null, ("props", System.Text.Json.JsonSerializer.Serialize(props)));
        foreach (var n in nested) Shape(ctx, detector, n, seen, depth + 1);
    }

    /// <summary>[JsonPropertyName], Newtonsoft [JsonProperty], [DataMember(Name)], else ASP.NET Core's default camelCase.</summary>
    public static string JsonName(IPropertySymbol prop)
    {
        foreach (var a in prop.GetAttributes())
        {
            var explicitName = a.AttributeClass?.Name switch
            {
                "JsonPropertyNameAttribute" => a.ConstructorArguments.FirstOrDefault().Value as string,
                "JsonPropertyAttribute" => a.ConstructorArguments.FirstOrDefault().Value as string
                    ?? a.NamedArguments.FirstOrDefault(n => n.Key == "PropertyName").Value.Value as string,
                "DataMemberAttribute" => a.NamedArguments.FirstOrDefault(n => n.Key == "Name").Value.Value as string,
                _ => null,
            };
            if (!string.IsNullOrEmpty(explicitName)) return explicitName;
        }
        return CamelCase(prop.Name);
    }

    /// <summary>Same rule as System.Text.Json's camelCase policy: "Name" → "name", "IOStream" → "ioStream", "ID" → "id".</summary>
    public static string CamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0])) return name;
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i])) break;
            var hasNext = i + 1 < chars.Length;
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
            {
                if (chars[i + 1] == ' ') chars[i] = char.ToLowerInvariant(chars[i]);
                break;
            }
            chars[i] = char.ToLowerInvariant(chars[i]);
        }
        return new string(chars);
    }
}

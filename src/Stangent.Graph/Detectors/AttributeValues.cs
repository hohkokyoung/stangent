using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Stangent.Graph.Detectors;

/// <summary>An attribute's arguments as text: strings quoted, enums by member name (N4), arrays in brackets.</summary>
static class AttributeValues
{
    public static string Format(TypedConstant c)
    {
        if (c.IsNull) return "null";
        if (c.Kind == TypedConstantKind.Array) return "[" + string.Join(", ", c.Values.Select(Format)) + "]";
        if (c.Kind == TypedConstantKind.Type) return c.Value is ITypeSymbol t ? $"typeof({t.Name})" : "typeof(?)";
        if (c.Kind == TypedConstantKind.Enum && c.Type is INamedTypeSymbol e && EnumMember(e, c.Value) is { } member) return member;
        return c.Value switch
        {
            string s => $"\"{s}\"",
            bool b => b ? "true" : "false",
            var v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "null",
        };
    }

    /// <summary><c>DataType(10)</c> → <c>EmailAddress</c>; a value no single member has (flags) stays a number.</summary>
    public static string? EnumMember(INamedTypeSymbol type, object? value) =>
        value is null ? null : type.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value))?.Name;

    /// <summary>Constructor arguments by their parameter's name, then named arguments: <c>permission = "x", deny = Json</c>.</summary>
    public static string Named(AttributeData a, Func<string, bool>? skip = null)
    {
        var parameters = a.AttributeConstructor?.Parameters ?? [];
        var positional = a.ConstructorArguments.Select((c, i) => i < parameters.Length ? $"{parameters[i].Name} = {Format(c)}" : Format(c));
        var named = a.NamedArguments.Where(n => skip?.Invoke(n.Key) != true).Select(n => $"{n.Key} = {Format(n.Value)}");
        return string.Join(", ", positional.Concat(named));
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// D329: fields the server defines, as <c>form-field</c> facts on the property (<c>via</c> <c>server</c>), and their choices as
/// <c>field-choice</c> facts on the enum. A property is one when an attribute on it names it: a <c>Label</c> named argument or a
/// constructor parameter called <c>label</c> (<c>[FieldDefinition(0, Label = "ApiKey")]</c>), a display-name attribute (one
/// deriving from <c>System.ComponentModel.DisplayNameAttribute</c> or named <c>…DisplayNameAttribute</c>, e.g.
/// <c>[NopResourceDisplayName("Admin.Catalog.Products.Fields.Name")]</c>), or DataAnnotations' <c>[Display(Name = …)]</c>. The same
/// attribute on an enum member makes that member one of a field's choices. Help comes from <c>HelpText</c>, <c>Description</c> or
/// <c>Hint</c>; choices from a named argument about options (<c>SelectOptions = typeof(E)</c> → <c>enum:E</c>,
/// <c>SelectOptionsProviderAction = "x"</c> → <c>server:x</c>), else the property's own enum type.
/// </summary>
public sealed class FormFieldsDetector : IDetector
{
    public string Name => "form-fields";

    static readonly string[] HelpArgs = ["HelpText", "Description", "Hint"];

    public void Extract(ExtractContext ctx)
    {
        foreach (var file in ctx.Files)
        {
            var root = file.Tree.GetRoot();
            foreach (var prop in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                if (file.Model.GetDeclaredSymbol(prop) is not IPropertySymbol symbol || ctx.NodeId(symbol) is not { } propId) continue;
                foreach (var a in symbol.GetAttributes())
                {
                    if (LabelOf(a) is not { } label) continue;
                    var at = a.ApplicationSyntaxReference?.GetSyntax() ?? prop;
                    ctx.AddFact(Name, "form-field", propId, at, ("element", "property"), ("bind", symbol.Name), ("to", symbol.ContainingType.ToDisplayString()),
                        ("type", Named(a, "Type") ?? TypeName(symbol.Type)), ("via", "server"), ("id", ""), ("label", label), ("labelText", ""),
                        ("help", HelpArgs.Select(h => Named(a, h)).FirstOrDefault(h => h is not null)?.Trim('"') ?? ""), ("helpText", ""),
                        ("choices", Choices(a, symbol.Type)), ("inside", ""), ("attribute", Short(a.AttributeClass!.Name)), ("args", AttributeValues.Named(a)));
                    break;
                }
            }
            foreach (var member in root.DescendantNodes().OfType<EnumMemberDeclarationSyntax>())
            {
                if (file.Model.GetDeclaredSymbol(member) is not IFieldSymbol symbol || ctx.NodeId(symbol.ContainingType) is not { } enumId) continue;
                foreach (var a in symbol.GetAttributes())
                {
                    var label = LabelOf(a);
                    var hint = HelpArgs.Select(h => Named(a, h)).FirstOrDefault(h => h is not null)?.Trim('"');
                    if (label is null && hint is null && !IsChoiceAttribute(a)) continue;
                    ctx.AddFact(Name, "field-choice", enumId, a.ApplicationSyntaxReference?.GetSyntax() ?? member, ("enum", symbol.ContainingType.ToDisplayString()),
                        ("member", symbol.Name), ("value", symbol.HasConstantValue ? Convert.ToString(symbol.ConstantValue, System.Globalization.CultureInfo.InvariantCulture) ?? "" : ""),
                        ("label", label ?? ""), ("help", hint ?? ""), ("attribute", Short(a.AttributeClass!.Name)));
                    break;
                }
            }
        }
    }

    public void Link(LinkContext ctx) { }

    /// <summary>The words or key an attribute names its target with, or null when it doesn't name it.</summary>
    static string? LabelOf(AttributeData a)
    {
        if (a.AttributeClass is not { } cls) return null;
        if (a.NamedArguments.FirstOrDefault(n => n.Key == "Label").Value is { Value: string named }) return named;
        var parameters = a.AttributeConstructor?.Parameters ?? [];
        for (var i = 0; i < a.ConstructorArguments.Length && i < parameters.Length; i++)
            if (parameters[i].Name.Equals("label", StringComparison.OrdinalIgnoreCase) && a.ConstructorArguments[i].Value is string byCtor) return byCtor;
        if (ExtractContext.IsOrDerives(cls, "System.ComponentModel.DisplayNameAttribute") || cls.Name.EndsWith("DisplayNameAttribute", StringComparison.Ordinal))
            return a.ConstructorArguments.Select(c => c.Value).OfType<string>().FirstOrDefault()
                ?? a.NamedArguments.Select(n => n.Value.Value).OfType<string>().FirstOrDefault();
        if (ExtractContext.IsOrDerives(cls, "System.ComponentModel.DataAnnotations.DisplayAttribute"))
            return a.NamedArguments.FirstOrDefault(n => n.Key == "Name").Value.Value as string;
        return null;
    }

    /// <summary>An attribute with a <c>Label</c> property or <c>label</c> parameter left unset still marks a choice (<c>[FieldOption(Hint = …)]</c>).</summary>
    static bool IsChoiceAttribute(AttributeData a) =>
        a.AttributeClass?.GetMembers("Label").OfType<IPropertySymbol>().Any() == true
        || a.AttributeConstructor?.Parameters.Any(p => p.Name.Equals("label", StringComparison.OrdinalIgnoreCase)) == true;

    static string? Named(AttributeData a, string name) =>
        a.NamedArguments.FirstOrDefault(n => n.Key == name) is { Key: not null } n ? AttributeValues.Format(n.Value) : null;

    static string Choices(AttributeData a, ITypeSymbol type)
    {
        foreach (var (key, value) in a.NamedArguments)
        {
            if (!key.Contains("Options", StringComparison.Ordinal) && !key.Contains("Items", StringComparison.Ordinal)) continue;
            if (value.Kind == TypedConstantKind.Type && value.Value is ITypeSymbol t) return t.TypeKind == TypeKind.Enum ? $"enum:{t.Name}" : $"type:{t.Name}";
            if (value.Value is string s) return $"server:{s}";
        }
        return EnumOf(type) is { } e ? $"enum:{e.Name}" : "";
    }

    /// <summary>The enum a property holds: itself, nullable, or a list of it.</summary>
    static ITypeSymbol? EnumOf(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum) return type;
        if (type is INamedTypeSymbol { IsGenericType: true } g && g.TypeArguments is [var inner] && inner.TypeKind == TypeKind.Enum) return inner;
        if (type is IArrayTypeSymbol { ElementType.TypeKind: TypeKind.Enum } arr) return arr.ElementType;
        return null;
    }

    static string TypeName(ITypeSymbol t) => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    static string Short(string name) => name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
}

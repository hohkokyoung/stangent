using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// G7: validation rules as facts on the property they check (<c>rule</c>), for flows to state as code's facts.
/// DataAnnotations: any attribute derived from <c>ValidationAttribute</c> (<c>[Required]</c>, <c>[Range(1, 10)]</c>).
/// FluentValidation: each call chained after <c>RuleFor(x =&gt; x.P)</c> (<c>.NotEmpty()</c>, <c>.GreaterThan(0)</c>,
/// <c>.SetValidator(v)</c>), with the validator set it was added to, its message and whether a condition guards it.
/// </summary>
public sealed class ValidationDetector : IDetector
{
    public string Name => "validation";

    /// <summary>
    /// Chained calls that change the rule before them rather than add one. N4: by prefix, as FluentValidation names them, so an
    /// app's own (nopCommerce's <c>WithMessageAwait</c>) is one too: <c>With…</c>, <c>When…</c>, <c>Unless…</c>.
    /// </summary>
    static readonly HashSet<string> Modifiers = ["Cascade", "OverridePropertyName", "DependentRules", "Configure"];

    static bool Message(string name) => name.StartsWith("WithMessage", StringComparison.Ordinal);
    static bool Condition(string name) => name.StartsWith("When", StringComparison.Ordinal) || name.StartsWith("Unless", StringComparison.Ordinal);
    static bool Modifier(string name) => name.StartsWith("With", StringComparison.Ordinal) || Condition(name) || Modifiers.Contains(name);

    public void Extract(ExtractContext ctx)
    {
        foreach (var file in ctx.Files)
        {
            var root = file.Tree.GetRoot();
            foreach (var prop in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                if (file.Model.GetDeclaredSymbol(prop) is not IPropertySymbol symbol || ctx.NodeId(symbol) is not { } propId) continue;
                foreach (var a in symbol.GetAttributes())
                    if (ExtractContext.IsOrDerives(a.AttributeClass, "System.ComponentModel.DataAnnotations.ValidationAttribute"))
                        ctx.AddFact(Name, "rule", propId, prop, ("rule", Short(a.AttributeClass!.Name)), ("args", Args(a)), ("via", "attribute"),
                            ("set", ""), ("message", a.NamedArguments.FirstOrDefault(n => n.Key == "ErrorMessage").Value.Value as string ?? ""), ("conditional", ""));
            }
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                if (ExtractContext.InvokedName(call) == "RuleFor") Chain(ctx, file, call);
        }
    }

    void Chain(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax ruleFor)
    {
        if (file.Model.GetSymbolInfo(ruleFor).Symbol is not IMethodSymbol method
            || method.ContainingNamespace?.ToDisplayString().StartsWith("FluentValidation", StringComparison.Ordinal) != true) return;
        var selector = ruleFor.ArgumentList.Arguments.FirstOrDefault()?.Expression as LambdaExpressionSyntax;
        var body = selector?.ExpressionBody;
        if (body is null || file.Model.GetSymbolInfo(body).Symbol is not IPropertySymbol target || ctx.NodeId(target) is not { } propId) return;
        var set = ruleFor.Expression is MemberAccessExpressionSyntax m ? m.Expression.ToString() : "";
        var rules = new List<(string Rule, string Args, SyntaxNode At, string Message, bool Conditional, string Modifiers, string Severity)>();
        // RuleFor(…).A().B(): each call's parent is a member access whose parent is the next invocation.
        for (SyntaxNode node = ruleFor; node.Parent is MemberAccessExpressionSyntax access && access.Parent is InvocationExpressionSyntax next; node = next)
        {
            var name = access.Name.Identifier.ValueText;
            var args = string.Join(", ", next.ArgumentList.Arguments.Select(a => Value(file.Model, a.Expression)));
            // T12: a modifier is a call on the rule's options (IRuleBuilderOptions); a rule, a call on the rule builder. By name only when unknown.
            var receiver = file.Model.GetSymbolInfo(next).Symbol is IMethodSymbol bound ? (bound.ReducedFrom is { } ext ? ext.Parameters[0].Type : bound.IsExtensionMethod ? bound.Parameters[0].Type : bound.ContainingType)?.Name : null;
            var modifier = Modifiers.Contains(name) || (receiver is null || !receiver.StartsWith("IRuleBuilder", StringComparison.Ordinal) ? Modifier(name) : receiver.StartsWith("IRuleBuilderOptions", StringComparison.Ordinal));
            if (!modifier) rules.Add((name, args, access.Name, "", false, "", "")); // F9: the rule's own line
            else if (Message(name))
            {
                if (rules.Count > 0) rules[^1] = rules[^1] with { Message = MessageText(file.Model, next) };
            }
            // F9: When/Unless guard the rules before them, not those chained after.
            else if (Condition(name))
                for (var i = 0; i < rules.Count; i++) rules[i] = rules[i] with { Conditional = true };
            // V5: any other modifier belongs to the rule before it, by name (Sonarr's AsWarning: it only warns); WithSeverity's value too.
            else if (rules.Count > 0)
                rules[^1] = rules[^1] with
                {
                    Modifiers = rules[^1].Modifiers.Length > 0 ? $"{rules[^1].Modifiers}|{name}" : name,
                    Severity = name == "WithSeverity" && next.ArgumentList.Arguments.Count == 1 ? args : rules[^1].Severity,
                };

        }
        foreach (var r in rules)
            ctx.AddFact(Name, "rule", propId, r.At, ("rule", r.Rule), ("args", r.Args), ("via", "fluentvalidation"), ("set", set),
                ("message", r.Message), ("conditional", r.Conditional ? "yes" : ""), ("modifiers", r.Modifiers), ("severity", r.Severity));
    }

    /// <summary>A constant's value; otherwise the expression's own text with its type (<c>seriesExistsValidator: SeriesExistsValidator</c>).</summary>
    static string Value(SemanticModel model, ExpressionSyntax e)
    {
        var constant = model.GetConstantValue(e);
        if (constant.HasValue && model.GetTypeInfo(e).Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } en
            && AttributeValues.EnumMember(en, constant.Value) is { } member) return member;
        if (constant.HasValue) return constant.Value switch { string s => $"\"{s}\"", null => "null", var v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)! };
        var type = model.GetTypeInfo(e).Type;
        return e is LambdaExpressionSyntax || type is null ? e.ToString() : $"{e}: {type.Name}";
    }

    /// <summary>The message: a constant's text, else the expression that makes it (<c>loc.GetResourceAsync("Account.Email.Required")</c>).</summary>
    static string MessageText(SemanticModel model, InvocationExpressionSyntax call)
    {
        if (call.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } e) return "";
        return model.GetConstantValue(e) is { HasValue: true, Value: string s } ? s : e.ToString();
    }

    static string Args(AttributeData a) => string.Join(", ", a.ConstructorArguments.Select(AttributeValues.Format)
        .Concat(a.NamedArguments.Where(n => n.Key != "ErrorMessage").Select(n => $"{n.Key} = {AttributeValues.Format(n.Value)}")));

    static string Short(string attribute) => attribute.EndsWith("Attribute", StringComparison.Ordinal) ? attribute[..^"Attribute".Length] : attribute;

    public void Link(LinkContext ctx) { }
}

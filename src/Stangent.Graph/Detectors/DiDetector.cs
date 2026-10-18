using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// Microsoft DI registrations: <c>AddScoped&lt;IService, Impl&gt;()</c> and the <c>typeof</c> forms
/// → the service is wired to the implementation. Services registered with several different
/// implementations are flagged, since which one runs depends on registration order.
/// </summary>
public sealed class DiDetector : IDetector
{
    public string Name => "microsoft-di";

    static readonly HashSet<string> Methods =
    [
        "AddScoped", "AddTransient", "AddSingleton", "TryAddScoped", "TryAddTransient", "TryAddSingleton",
        "AddKeyedScoped", "AddKeyedTransient", "AddKeyedSingleton", "TryAddKeyedScoped", "TryAddKeyedTransient", "TryAddKeyedSingleton",
    ];

    public void Extract(ExtractContext ctx)
    {
        foreach (var file in ctx.Files)
            foreach (var call in file.Tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (ExtractContext.InvokedName(call) is not { } name || !Methods.Contains(name)) continue;
                if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method) continue;
                if (!ExtractContext.IsOrDerives(method.ReducedFrom is not null ? method.ReceiverType : method.Parameters.FirstOrDefault()?.Type,
                        "Microsoft.Extensions.DependencyInjection.IServiceCollection")) continue;

                ITypeSymbol? service = null, impl = null;
                if (method.TypeArguments.Length == 2) (service, impl) = (method.TypeArguments[0], method.TypeArguments[1]);
                else
                {
                    var types = call.ArgumentList.Arguments.Select(a => a.Expression).OfType<TypeOfExpressionSyntax>()
                        .Select(t => file.Model.GetTypeInfo(t.Type).Type).ToList();
                    if (types.Count == 2) (service, impl) = (types[0], types[1]);
                }
                if (service is null || impl is null) continue;
                var serviceId = ctx.NodeId(service);
                var implId = ctx.NodeId(impl);
                if (serviceId is null || implId is null) continue;
                ctx.AddEdge(serviceId, implId, EdgeKind.WiredTo, Label.Certain, Name);
                ctx.AddFact(Name, "registration", ctx.OwnerId(call, file.Model) ?? ctx.ProjectId, call, ("service", serviceId), ("impl", implId));
            }
    }

    public void Link(LinkContext ctx)
    {
        foreach (var group in ctx.Facts(Name).Where(f => f.Kind == "registration").GroupBy(f => f["service"]).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var impls = group.Select(f => f["impl"]).Distinct().Order(StringComparer.Ordinal).ToList();
            if (impls.Count < 2) continue;
            foreach (var site in group)
                ctx.AddBlindSpot(site.Owner, site.File, site.Line,
                    $"{Short(group.Key)} is registered with {impls.Count} implementations ({string.Join(", ", impls.Select(Short))}); the last registration at runtime wins",
                    Name);
        }
    }

    static string Short(string id) => id[(id.LastIndexOf('.') + 1)..];
}

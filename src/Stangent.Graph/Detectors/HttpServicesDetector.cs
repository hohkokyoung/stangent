using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// HTTP calls between services (D4's riskiest links). Typed <c>HttpClient</c>s registered with
/// <c>AddHttpClient&lt;T&gt;</c> and a base address give the service a client talks to; service
/// names (<c>https+http://catalog-api</c>) are resolved through an Aspire AppHost when there is one,
/// or through <c>services</c> in this detector's settings;
/// each call's method and path is matched against that service's endpoints (🟡).
/// Anything that can't be followed is ❓.
/// </summary>
public sealed class HttpServicesDetector : IDetector
{
    public string Name => "http-services";

    static readonly Dictionary<string, string> Calls = new()
    {
        ["GetAsync"] = "GET", ["GetFromJsonAsync"] = "GET", ["GetStringAsync"] = "GET", ["GetStreamAsync"] = "GET",
        ["GetByteArrayAsync"] = "GET", ["PostAsync"] = "POST", ["PostAsJsonAsync"] = "POST", ["PutAsync"] = "PUT",
        ["PutAsJsonAsync"] = "PUT", ["PatchAsync"] = "PATCH", ["PatchAsJsonAsync"] = "PATCH", ["DeleteAsync"] = "DELETE",
        ["DeleteFromJsonAsync"] = "DELETE",
    };

    public void Extract(ExtractContext ctx)
    {
        foreach (var file in ctx.Files)
            foreach (var call in file.Tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = ExtractContext.InvokedName(call);
                if (name is null) continue;
                if (name is "AddProject" or "AddDotnetProject" or "AddCSharpApp") Service(ctx, file, call);
                else if (name == "AddHttpClient") Client(ctx, file, call);
                else if (Calls.ContainsKey(name) || name == "SendAsync") Request(ctx, file, call, name);
            }
    }

    void Service(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call)
    {
        if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method
            || !(method.ContainingNamespace?.ToDisplayString().StartsWith("Aspire.Hosting", StringComparison.Ordinal) ?? false)) return;
        var args = call.ArgumentList.Arguments;
        var service = ExtractContext.StringValue(args.FirstOrDefault()?.Expression, file.Model);
        if (service is null) return;
        string project = "", path = "";
        if (method.TypeArguments.FirstOrDefault() is { } projectType) project = projectType.Name;
        else if (args.Count > 1 && ExtractContext.StringValue(args[1].Expression, file.Model) is { } relative)
        {
            var full = Path.GetFullPath(Path.Combine(ctx.ProjectDir, relative));
            var csproj = full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? full
                : Directory.Exists(full) ? Directory.EnumerateFiles(full, "*.csproj").Order(StringComparer.Ordinal).FirstOrDefault() : null;
            if (csproj is not null) path = Path.GetRelativePath(ctx.RepoRoot, csproj).Replace('\\', '/');
        }
        ctx.AddFact(Name, "service", ctx.ProjectId, call, ("service", service), ("projectName", project), ("projectPath", path));
    }

    void Client(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call)
    {
        if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method || method.TypeArguments.Length == 0) return;
        var client = ctx.NodeId(method.TypeArguments[^1]);
        if (client is null) return;
        // The base address may be set in this call's lambda or in a chained ConfigureHttpClient.
        var statement = call.Ancestors().FirstOrDefault(a => a is StatementSyntax) ?? call;
        var address = statement.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(a => a.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "BaseAddress" })
            .Select(a => a.Right switch
            {
                BaseObjectCreationExpressionSyntax creation => ExtractContext.StringValue(creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression, file.Model),
                var other => ExtractContext.StringValue(other, file.Model),
            })
            .FirstOrDefault();
        ctx.AddFact(Name, "client", client, call, ("client", client), ("base", address ?? ""));
    }

    void Request(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call, string name)
    {
        if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method) return;
        var receiver = method.ReducedFrom is not null ? method.ReceiverType : method.ContainingType;
        if (!ExtractContext.IsOrDerives(receiver, "System.Net.Http.HttpClient")) return;
        var owner = ctx.OwnerId(call, file.Model);
        if (owner is null) return;
        if (name == "SendAsync")
        {
            ctx.AddBlindSpot(owner, call, "HTTP call through SendAsync: method and path not followed", Name);
            return;
        }
        var arg = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (arg is BaseObjectCreationExpressionSyntax uri) arg = uri.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        var url = ExtractContext.StringValue(arg, file.Model);
        if (url is null)
        {
            ctx.AddBlindSpot(owner, call, $"HTTP {Calls[name]} to a URL built at runtime", Name);
            return;
        }
        ctx.AddFact(Name, "call", owner, call, ("method", Calls[name]), ("url", url), ("client", DocIds.ContainingType(owner) ?? ""));
    }

    public void Link(LinkContext ctx)
    {
        var facts = ctx.Facts(Name).ToList();
        var projects = ctx.Graph.AllNodes.Where(n => n.Kind == NodeKind.Project).ToList();
        string? Assembly(Fact s)
        {
            var match = s["projectPath"].Length > 0
                ? projects.FirstOrDefault(p => p.Id == "project:" + s["projectPath"])
                : projects.FirstOrDefault(p => Simplify(p.Name) == Simplify(s["projectName"]));
            return match?.Name;
        }
        var services = facts.Where(f => f.Kind == "service")
            .GroupBy(f => f["service"], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Assembly(g.First()), StringComparer.OrdinalIgnoreCase);
        // Without Aspire (e.g. Kubernetes service names), stangent.yaml maps names to projects:
        // detectorSettings: { http-services: { services: ["stock-api=Shop.Stock"] } }
        foreach (var entry in ctx.Settings.GetValueOrDefault("services") ?? [])
            if (entry.Split('=', 2) is [var service, var project])
                services[service.Trim()] = projects.FirstOrDefault(p => Simplify(p.Name) == Simplify(project.Trim()))?.Name;
        var clients = facts.Where(f => f.Kind == "client").GroupBy(f => f["client"])
            .ToDictionary(g => g.Key, g => g.Select(f => f["base"]).FirstOrDefault(b => b.Length > 0) ?? "");
        var endpoints = ctx.Graph.AllNodes.Where(n => n.Kind == NodeKind.Endpoint)
            .Select(n => (Node: n, Assembly: DocIds.Assembly(n.Id["endpoint:".Length..]), Verb: n.Name[..n.Name.IndexOf(' ')], Path: n.Name[(n.Name.IndexOf(' ') + 1)..]))
            .ToList();

        foreach (var call in facts.Where(f => f.Kind == "call"))
        {
            var url = call["url"];
            var baseAddress = clients.GetValueOrDefault(call["client"]) ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute) || absolute.Scheme is not ("http" or "https" or "https+http" or "http+https"))
            {
                if (baseAddress.Length == 0)
                {
                    ctx.AddBlindSpot(call.Owner, call.File, call.Line, $"HTTP {call["method"]} {url}: the client's base address isn't known", Name);
                    continue;
                }
                url = baseAddress.TrimEnd('/') + "/" + url.TrimStart('/');
            }
            var scheme = url.IndexOf("://", StringComparison.Ordinal);
            var rest = scheme < 0 ? url : url[(scheme + 3)..];
            var slash = rest.IndexOf('/');
            var host = (slash < 0 ? rest : rest[..slash]).Split(':')[0];
            var path = slash < 0 ? "/" : rest[slash..];
            path = path.Split('?')[0];

            if (!services.TryGetValue(host, out var targetAssembly) || targetAssembly is null)
            {
                ctx.AddBlindSpot(call.Owner, call.File, call.Line, $"HTTP {call["method"]} to '{host}': no service with that name in this repo", Name);
                continue;
            }
            var scored = endpoints.Where(e => e.Assembly == targetAssembly && (e.Verb == call["method"] || e.Verb == "ANY"))
                .Select(e => (e.Node, Score: Score(e.Path, path))).Where(m => m.Score >= 0).ToList();
            if (scored.Count == 0)
            {
                ctx.AddBlindSpot(call.Owner, call.File, call.Line, $"HTTP {call["method"]} {path} matched no endpoint in {host}", Name);
                continue;
            }
            // Keep only the closest matches: a value known only at runtime fits any segment, but a route parameter fits it best.
            var best = scored.Max(m => m.Score);
            var matches = scored.Where(m => m.Score == best).Select(m => (Node: m.Node, 0)).ToList();
            foreach (var (node, _) in matches) ctx.AddEdge(call.Owner, node.Id, EdgeKind.CallsHttp, Label.Guessed, Name);
        }
    }

    static string Simplify(string name) => name.Replace('.', '_').Replace('-', '_').ToLowerInvariant();

    /// <summary>
    /// How well a call path fits an endpoint template, segment by segment; -1 when it can't.
    /// Same literal: 3. Route parameter vs a runtime value (<c>{*}</c>): 3. Route parameter vs a
    /// literal: 2. Runtime value vs a literal segment: 1 (possible, but a weak fit).
    /// </summary>
    public static int Score(string template, string path)
    {
        var t = template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var p = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var required = t.Count(s => !(s.StartsWith('{') && s.EndsWith("?}", StringComparison.Ordinal)));
        var catchAll = t.Length > 0 && t[^1].StartsWith("{*", StringComparison.Ordinal);
        if (p.Length < required - (catchAll ? 1 : 0) || (p.Length > t.Length && !catchAll)) return -1;
        var score = 0;
        for (var i = 0; i < Math.Min(p.Length, t.Length); i++)
        {
            var param = t[i].StartsWith('{');
            var runtime = p[i].Contains("{*}", StringComparison.Ordinal);
            if (param) score += runtime ? 3 : 2;
            else if (runtime) score += 1;
            else if (string.Equals(t[i], p[i], StringComparison.OrdinalIgnoreCase)) score += 3;
            else return -1;
        }
        return score;
    }

    public static bool Matches(string template, string path) => Score(template, path) >= 0;
}

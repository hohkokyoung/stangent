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
                else if (!((Calls.ContainsKey(name) || name == "SendAsync") && Request(ctx, file, call, name)) && !Mail(ctx, file, call)) OtherClient(ctx, file, call);
            }
    }

    /// <summary>
    /// Outside calls through a client other than System.Net.Http's (G4): Flurl's string and Url extensions, and an
    /// app's own client type (<c>IHttpClient</c>, <c>…HttpClient</c>; Sonarr's). A literal URL becomes a <c>call</c>
    /// fact; anything else is ❓, so a flow never looks complete when it reaches outside.
    /// </summary>
    void OtherClient(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call)
    {
        if (call.Expression is not MemberAccessExpressionSyntax access || access.Expression is ThisExpressionSyntax or BaseExpressionSyntax) return;
        if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method) return;
        var verb = VerbOf(method.Name);
        if (verb is null) return;
        var flurl = method.ContainingNamespace?.ToDisplayString().StartsWith("Flurl", StringComparison.Ordinal) == true
            && method.Name.EndsWith("Async", StringComparison.Ordinal);
        var receiver = method.ReducedFrom is not null ? method.ReceiverType : method.ContainingType;
        if (!flurl && !(receiver is INamedTypeSymbol type && IsClient(type) && !ExtractContext.IsOrDerives(type, "System.Net.Http.HttpClient"))) return;
        var owner = ctx.OwnerId(call, file.Model);
        if (owner is null) return;
        string? url = null, start = null;
        if (flurl)
        {
            // "https://x".AppendPathSegment(id).WithHeader(…).PostJsonAsync(…): the URL starts at the chain's first receiver.
            var root = access.Expression;
            var changed = false;
            while (root is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax inner } link
                   && file.Model.GetSymbolInfo(link).Symbol is IMethodSymbol m && m.ContainingNamespace?.ToDisplayString().StartsWith("Flurl", StringComparison.Ordinal) == true)
            {
                // F6: only path, query and fragment calls change the URL; WithHeader, WithTimeout, AllowHttpStatus keep it.
                if (UrlChanging.Any(w => m.Name.StartsWith(w, StringComparison.Ordinal))) changed = true;
                root = inner.Expression;
            }
            start = ExtractContext.StringValue(root, file.Model);
            if (!changed && start is not null && !start.Contains("{*}", StringComparison.Ordinal)) url = start;
        }
        var via = flurl ? "Flurl" : receiver!.Name;
        if (url is null)
        {
            ctx.AddBlindSpot(owner, call, start is null
                ? $"outside HTTP {verb} through {via}: URL built at runtime"
                : $"outside HTTP {verb} through {via} to {start}…: rest of the URL built at runtime", Name);
            return;
        }
        ctx.AddFact(Name, "call", owner, call, ("method", verb), ("url", url), ("client", ""));
    }

    /// <summary>
    /// D199: a mail client's connect, sign-in or send (MailKit's or .NET's <c>SmtpClient</c>, a short list like G4's HTTP
    /// clients) talks to a mail server outside the app, whose address is read at runtime: ❓ "outside SMTP …", which a flow
    /// shows as an outside call.
    /// </summary>
    bool Mail(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call)
    {
        if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method) return false;
        // The client the call is made on (MailKit's SendAsync is declared on a base class of SmtpClient).
        var on = call.Expression is MemberAccessExpressionSyntax access ? file.Model.GetTypeInfo(access.Expression).Type : method.ContainingType;
        INamedTypeSymbol? client = null;
        for (var t = on as INamedTypeSymbol; t is not null && client is null; t = t.BaseType)
            if (t.Name == "SmtpClient" && t.ContainingNamespace?.ToDisplayString() is "MailKit.Net.Smtp" or "System.Net.Mail") client = t;
        if (client is null) return false;
        var what = method.Name.StartsWith("Connect", StringComparison.Ordinal) ? "connect" : method.Name.StartsWith("Authenticate", StringComparison.Ordinal) ? "sign-in"
            : method.Name.StartsWith("Send", StringComparison.Ordinal) ? "send" : null;
        if (what is null || ctx.OwnerId(call, file.Model) is not { } owner) return true;
        ctx.AddBlindSpot(owner, call, $"outside SMTP {what} through {client.ToDisplayString()}: the mail server is set at runtime", Name);
        return true;
    }

    static readonly string[] UrlChanging = ["AppendPathSegment", "AppendQueryParam", "SetQueryParam", "RemoveQueryParam", "SetFragment", "RemoveFragment", "ResetToRoot", "RemovePath", "AppendPath"];

    static bool IsClient(INamedTypeSymbol type) =>
        type.Name == "IHttpClient" || type.Name.EndsWith("HttpClient", StringComparison.Ordinal) || type.AllInterfaces.Any(i => i.Name == "IHttpClient");

    /// <summary>The verb a client method's name says: Get…/Post…/Put…/Patch…/Delete…/Head…/Download…; Execute/Send/Request → ANY.</summary>
    static string? VerbOf(string name)
    {
        foreach (var (prefix, verb) in new[] { ("Get", "GET"), ("Download", "GET"), ("Post", "POST"), ("Put", "PUT"), ("Patch", "PATCH"), ("Delete", "DELETE"), ("Head", "HEAD") })
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return verb;
        return name is "Execute" or "ExecuteAsync" or "Send" or "SendAsync" or "Request" or "RequestAsync" ? "ANY" : null;
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

    /// <summary>A System.Net.Http.HttpClient call; false when it isn't one, so other clients get a look.</summary>
    bool Request(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call, string name)
    {
        if (file.Model.GetSymbolInfo(call).Symbol is not IMethodSymbol method) return false;
        var receiver = method.ReducedFrom is not null ? method.ReceiverType : method.ContainingType;
        if (!ExtractContext.IsOrDerives(receiver, "System.Net.Http.HttpClient")) return false;
        var owner = ctx.OwnerId(call, file.Model);
        if (owner is null) return true;
        if (name == "SendAsync")
        {
            ctx.AddBlindSpot(owner, call, "HTTP call through SendAsync: method and path not followed", Name);
            return true;
        }
        var arg = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (arg is BaseObjectCreationExpressionSyntax uri) arg = uri.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        var url = ExtractContext.StringValue(arg, file.Model);
        if (url is null)
        {
            ctx.AddBlindSpot(owner, call, $"HTTP {Calls[name]} to a URL built at runtime", Name);
            return true;
        }
        ctx.AddFact(Name, "call", owner, call, ("method", Calls[name]), ("url", url), ("client", DocIds.ContainingType(owner) ?? ""));
        return true;
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

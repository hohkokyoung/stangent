using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// Configuration keys (D26): keys defined in a project's <c>appsettings*.json</c>, code that reads
/// them (<c>config["A:B"]</c>, <c>GetValue</c>, <c>GetSection</c>, <c>GetConnectionString</c>), and
/// settings classes bound to a section (<c>Configure&lt;T&gt;(GetSection("X"))</c>, <c>Bind</c>,
/// <c>BindConfiguration</c>). A key that is read but defined in no appsettings file is ❓: it may
/// come from an environment variable or a secret store.
/// </summary>
public sealed class ConfigKeysDetector : IDetector
{
    public string Name => "config-keys";
    const string Configuration = "Microsoft.Extensions.Configuration.IConfiguration";

    public static string KeyId(string assembly, string key) => $"config:{assembly}|{key}";

    public void Extract(ExtractContext ctx)
    {
        // Anywhere in the project's own folders (nopCommerce keeps it in App_Data/), never bin/obj.
        foreach (var json in Loading.ProjectScanner.Walk(ctx.ProjectDir, d => !File.Exists(Path.Combine(d, Path.GetFileName(d) + ".csproj")))
                     .Where(f => Path.GetFileName(f).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal))
            foreach (var (key, line) in Flatten(File.ReadAllBytes(json)))
                ctx.AddFact(Name, "define", ctx.ProjectId, null, ("key", key), ("file", Path.GetRelativePath(ctx.RepoRoot, json).Replace('\\', '/')),
                    ("line", line.ToString()), ("assembly", ctx.AssemblyName), ("project", ctx.ProjectId));

        foreach (var file in ctx.Files)
            foreach (var node in file.Tree.GetRoot().DescendantNodes())
            {
                if (node is ElementAccessExpressionSyntax access && IsConfig(access.Expression, file.Model))
                {
                    var prefix = Prefix(access.Expression, file.Model);
                    var key = ExtractContext.StringValue(access.ArgumentList.Arguments.FirstOrDefault()?.Expression, file.Model);
                    Read(ctx, file, access, prefix is null || key is null ? null : prefix + key);
                }
                else if (node is InvocationExpressionSyntax call && call.Expression is MemberAccessExpressionSyntax m)
                    Invocation(ctx, file, call, m);
            }
    }

    void Invocation(ExtractContext ctx, SourceFile file, InvocationExpressionSyntax call, MemberAccessExpressionSyntax m)
    {
        var name = m.Name.Identifier.ValueText;
        var arg = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        switch (name)
        {
            case "GetValue" or "GetConnectionString" when IsConfig(m.Expression, file.Model):
                var prefix = Prefix(m.Expression, file.Model);
                var key = ExtractContext.StringValue(arg, file.Model);
                if (name == "GetConnectionString" && key is not null) key = "ConnectionStrings:" + key;
                Read(ctx, file, call, prefix is null || key is null ? null : prefix + key);
                break;
            case "Configure" when file.Model.GetSymbolInfo(call).Symbol is IMethodSymbol { TypeArguments: [var options] } && arg is not null && IsConfig(arg, file.Model):
                Bind(ctx, file, call, options, SectionOf(arg, file.Model));
                break;
            case "Bind" when arg is not null && IsConfig(arg, file.Model) && OptionsType(m.Expression, file.Model) is { } bound:
                Bind(ctx, file, call, bound, SectionOf(arg, file.Model));
                break;
            case "BindConfiguration" when OptionsType(m.Expression, file.Model) is { } bound:
                Bind(ctx, file, call, bound, ExtractContext.StringValue(arg, file.Model));
                break;
            case "Get" or "Bind" when IsConfig(m.Expression, file.Model):
                var type = name == "Get"
                    ? (file.Model.GetSymbolInfo(call).Symbol as IMethodSymbol)?.TypeArguments.FirstOrDefault()
                    : arg is null ? null : file.Model.GetTypeInfo(arg).Type;
                if (type is not null) Bind(ctx, file, call, type, SectionOf(m.Expression, file.Model));
                break;
        }
    }

    void Read(ExtractContext ctx, SourceFile file, SyntaxNode at, string? key)
    {
        var owner = ctx.OwnerId(at, file.Model);
        if (owner is null) return;
        if (key is null || key.Contains("{*}", StringComparison.Ordinal))
            ctx.AddBlindSpot(owner, at, "configuration key built at runtime", Name);
        else
            ctx.AddFact(Name, "read", owner, at, ("key", key), ("assembly", ctx.AssemblyName), ("project", ctx.ProjectId));
    }

    void Bind(ExtractContext ctx, SourceFile file, SyntaxNode at, ITypeSymbol type, string? section)
    {
        var owner = ctx.OwnerId(at, file.Model) ?? ctx.ProjectId;
        if (section is null)
        {
            ctx.AddBlindSpot(owner, at, $"settings class {type.Name} bound to a section built at runtime", Name);
            return;
        }
        foreach (var (prop, key) in Properties(type, section, 0))
            if (ctx.NodeId(prop) is { } propId)
                ctx.AddFact(Name, "bind", propId, at, ("key", key), ("section", section), ("assembly", ctx.AssemblyName), ("project", ctx.ProjectId));
    }

    static IEnumerable<(IPropertySymbol, string)> Properties(ITypeSymbol type, string prefix, int depth)
    {
        if (depth > 3) yield break;
        foreach (var prop in type.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && p.DeclaredAccessibility == Accessibility.Public))
        {
            var key = $"{prefix}:{prop.Name}";
            yield return (prop, key);
            if (prop.Type is INamedTypeSymbol { TypeKind: TypeKind.Class, SpecialType: SpecialType.None } nested
                && nested.Locations.Any(l => l.IsInSource) && nested.Name != type.Name)
                foreach (var inner in Properties(nested, key, depth + 1)) yield return inner;
        }
    }

    static bool IsConfig(ExpressionSyntax expr, SemanticModel model) =>
        ExtractContext.IsOrDerives(model.GetTypeInfo(expr).Type, Configuration);

    /// <summary>"A:B:" for <c>config.GetSection("A").GetSection("B")</c>; "" for the root; null when a section name isn't constant.</summary>
    static string? Prefix(ExpressionSyntax expr, SemanticModel model)
    {
        var section = SectionOf(expr, model);
        return section is null ? null : section.Length == 0 ? "" : section + ":";
    }

    static string? SectionOf(ExpressionSyntax expr, SemanticModel model)
    {
        if (expr is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax m } inv
            && m.Name.Identifier.ValueText is "GetSection" or "GetRequiredSection")
        {
            var outer = SectionOf(m.Expression, model);
            var own = ExtractContext.StringValue(inv.ArgumentList.Arguments.FirstOrDefault()?.Expression, model);
            return outer is null || own is null ? null : outer.Length == 0 ? own : $"{outer}:{own}";
        }
        if (expr is ElementAccessExpressionSyntax) return null;
        return IsConfig(expr, model) ? "" : null;
    }

    /// <summary>The T of an <c>OptionsBuilder&lt;T&gt;</c> expression (from <c>AddOptions&lt;T&gt;()</c>).</summary>
    static ITypeSymbol? OptionsType(ExpressionSyntax expr, SemanticModel model) =>
        model.GetTypeInfo(expr).Type is INamedTypeSymbol { TypeArguments: [var t] } builder
        && ExtractContext.FullName(builder.OriginalDefinition) == "Microsoft.Extensions.Options.OptionsBuilder`1" ? t : null;

    /// <summary>Every leaf key in an appsettings file as "A:B:C", with its line number.</summary>
    public static List<(string Key, int Line)> Flatten(byte[] json)
    {
        var result = new List<(string, int)>();
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < json.Length; i++) if (json[i] == '\n') lineStarts.Add(i + 1);
        int LineAt(long offset) { var idx = lineStarts.BinarySearch((int)offset); return (idx >= 0 ? idx : ~idx - 1) + 1; }

        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        // One frame per open object/array: its path segment (null for the root) and, for arrays, the next index.
        var frames = new List<(string? Segment, bool IsArray, int Index)>();
        string? pending = null;

        string? NextSegment()
        {
            if (pending is not null) { var name = pending; pending = null; return name; }
            if (frames.Count == 0 || !frames[^1].IsArray) return null;
            var f = frames[^1];
            frames[^1] = f with { Index = f.Index + 1 };
            return f.Index.ToString();
        }

        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        pending = reader.GetString();
                        break;
                    case JsonTokenType.StartObject or JsonTokenType.StartArray:
                        frames.Add((NextSegment(), reader.TokenType == JsonTokenType.StartArray, 0));
                        break;
                    case JsonTokenType.EndObject or JsonTokenType.EndArray:
                        frames.RemoveAt(frames.Count - 1);
                        break;
                    default:
                        var leaf = NextSegment();
                        if (leaf is not null)
                            result.Add((string.Join(":", frames.Select(f => f.Segment).OfType<string>().Append(leaf)), LineAt(reader.TokenStartIndex)));
                        break;
                }
            }
        }
        catch (JsonException) { }
        return result;
    }

    public void Link(LinkContext ctx)
    {
        var facts = ctx.Facts(Name).ToList();
        var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in facts.Where(f => f.Kind == "define").OrderBy(f => f["key"], StringComparer.Ordinal))
        {
            var id = KeyId(d["assembly"], d["key"]);
            if (defined.Add(id))
                ctx.AddNode(new Node(id, NodeKind.Config, d["key"], d["project"], d["file"], int.Parse(d["line"]), int.Parse(d["line"])));
        }

        var missing = new SortedDictionary<string, (Fact Fact, string Key)>(StringComparer.Ordinal);
        foreach (var r in facts.Where(f => f.Kind is "read" or "bind"))
        {
            var key = r["key"];
            var id = Canonical(ctx, r["assembly"], key, defined);
            if (!defined.Contains(id))
            {
                ctx.AddNode(new Node(id, NodeKind.Config, key, r["project"]));
                // Report each undefined read once, and each undefined bound section once.
                var reportKey = r.Kind == "bind" ? $"{r["assembly"]}|{r["section"]}" : id;
                missing.TryAdd(reportKey, (r, r.Kind == "bind" ? r["section"] : key));
            }
            ctx.AddEdge(r.Owner, id, r.Kind == "read" ? EdgeKind.ReadsConfig : EdgeKind.Binds, r.Kind == "read" ? Label.Certain : Label.Guessed, Name);
        }
        foreach (var (fact, key) in missing.Values)
        {
            if (fact.Kind == "bind" && defined.Any(d => d.StartsWith(KeyId(fact["assembly"], fact["section"] + ":"), StringComparison.OrdinalIgnoreCase))) continue;
            ctx.AddBlindSpot(fact.Owner, fact.File, fact.Line,
                $"configuration {(fact.Kind == "bind" ? "section" : "key")} '{key}' is in no appsettings file of this project (environment variable or secret?)", Name);
        }
    }

    /// <summary>Configuration keys are case-insensitive: reuse the defined spelling when there is one.</summary>
    static string Canonical(LinkContext ctx, string assembly, string key, HashSet<string> defined)
    {
        var id = KeyId(assembly, key);
        return defined.TryGetValue(id, out var existing) ? existing : id;
    }
}

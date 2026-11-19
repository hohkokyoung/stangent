using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// Which code sends which template file (code-graph v7, D362 (5)): a class holding a string constant that is a file-name
/// pattern (<c>"{0}.html"</c>, <c>"Templates/{0}.liquid"</c>) and string constants whose values fill it
/// (<c>EmailConfirmTemplate = "EmailConfirm"</c>) gets a 🟡 <c>sends-template</c> line from each such constant to every
/// project file the filled pattern names (<c>Kavita.Server/EmailTemplates/EmailConfirm.html</c>), a <c>template-file</c>
/// point, in a template folder (two or more of the class's names find a file there, or the pattern names it); a whole string literal in one of the class's members that fills it links from that member
/// (<c>GetEmailBody("base")</c>). A matching rule, not an app's names. A file in the same folder with the pattern's extension that no constant of
/// the class names is a blind spot: it may be sent with a name built at runtime. Walked by impact: a template's change
/// reaches the constant and the code that uses it.
/// </summary>
public sealed partial class TemplateFilesDetector : IDetector
{
    public string Name => "template-files";

    /// <summary>A pattern: an optional folder, one <c>{0}</c> as the whole base name, an extension.</summary>
    [GeneratedRegex(@"^(?<dir>[^{}*?<>|""]*/)?\{0\}(?<ext>\.[A-Za-z0-9]{1,10})$")]
    private static partial Regex Pattern();

    /// <summary>A value that can be a base name: one word-ish token, no folder, no placeholder.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,99}$")]
    private static partial Regex BaseName();

    static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", ".git", ".stangent", "dist"];

    public void Extract(ExtractContext ctx)
    {
        foreach (var file in ctx.Files)
            foreach (var declarator in file.Tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.Parent?.Parent is not FieldDeclarationSyntax) continue;
                if (file.Model.GetDeclaredSymbol(declarator) is not IFieldSymbol { Type.SpecialType: SpecialType.System_String } field) continue;
                if (ctx.NodeId(field) is not { } fieldId || ctx.NodeId(field.ContainingType) is not { } typeId) continue;
                string? value = null;
                if (field.IsConst && field.HasConstantValue) value = field.ConstantValue as string;
                else if (field.IsStatic && field.IsReadOnly && declarator.Initializer?.Value is { } init && file.Model.GetConstantValue(init) is { HasValue: true } c) value = c.Value as string;
                if (value is null) continue;
                var v = value.Replace('\\', '/');
                if (Pattern().IsMatch(v)) ctx.AddFact(Name, "pattern", fieldId, declarator, ("value", v), ("type", typeId));
                else if (BaseName().IsMatch(v)) ctx.AddFact(Name, "name", fieldId, declarator, ("value", v), ("type", typeId));
            }
        // A whole string literal in a member's code names a template too (Kavita's GetEmailBody("base")): the line is from
        // the member that holds it. Literals that initialise a field were read above.
        foreach (var file in ctx.Files)
            foreach (var literal in file.Tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>())
            {
                if (!literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression) || literal.Token.ValueText is not { } value) continue;
                if (literal.Ancestors().Any(a => a is FieldDeclarationSyntax or AttributeSyntax) || !BaseName().IsMatch(value)) continue;
                if (literal.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is not { } typeSyntax) continue;
                if (file.Model.GetDeclaredSymbol(typeSyntax) is not INamedTypeSymbol type || ctx.NodeId(type) is not { } typeId) continue;
                if (ctx.OwnerId(literal, file.Model) is not { } owner || owner == typeId) continue;
                ctx.AddFact(Name, "name", owner, literal, ("value", value), ("type", typeId));
            }
    }

    public void Link(LinkContext ctx)
    {
        var facts = ctx.Facts(Name).ToList();
        var patterns = facts.Where(f => f.Kind == "pattern").GroupBy(f => f["type"]).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        if (patterns.Count == 0 || ctx.Root.Length == 0) return;
        var names = facts.Where(f => f.Kind == "name").GroupBy(f => f["type"]).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        // The repo's files by file name, once: a filled pattern is looked up by its last part, not matched against every file.
        var byName = RepoFiles(ctx.Root).GroupBy(p => p[(p.LastIndexOf('/') + 1)..], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var (type, list) in patterns.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var mine = (names.GetValueOrDefault(type) ?? []).Where(n => ctx.HasNode(n.Owner)).OrderBy(n => n.Owner, StringComparer.Ordinal).ThenBy(n => n.Line).ToList();
            var blind = new SortedDictionary<string, Fact>(StringComparer.Ordinal);
            var namedByAny = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pattern in list.OrderBy(p => p.Owner, StringComparer.Ordinal))
            {
                var m = Pattern().Match(pattern["value"]);
                var dir = m.Groups["dir"].Value;
                var ext = m.Groups["ext"].Value;
                var hits = new List<(Fact Name, string Path)>();
                foreach (var name in mine)
                {
                    var target = dir + name["value"] + ext;
                    foreach (var path in byName.GetValueOrDefault(target[(target.LastIndexOf('/') + 1)..]) ?? [])
                        if (path == target || path.EndsWith("/" + target, StringComparison.Ordinal)) hits.Add((name, path));
                }
                // A template folder: one where two or more of the class's names find a file, or the one the pattern itself
                // names; never the repo's root unless the pattern names it. A lone name matching a file somewhere ("package" → every
                // package.json) is no template (D370).
                var folders = hits.GroupBy(h => Folder(h.Path), StringComparer.Ordinal)
                    .Where(g => dir.Length > 0 || (g.Key.Length > 0 && g.Select(h => h.Name["value"]).Distinct(StringComparer.Ordinal).Count() >= 2))
                    .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
                var named = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (name, path) in hits.Where(h => folders.Contains(Folder(h.Path))))
                {
                    var id = "template-file:" + path;
                    if (!ctx.HasNode(id)) ctx.AddNode(new Node(id, NodeKind.TemplateFile, Path.GetFileName(path), null, path, 1, Math.Max(1, Lines(ctx.Root, path))));
                    ctx.AddEdge(name.Owner, id, EdgeKind.SendsTemplate, Label.Guessed, Name);
                    ctx.AddFact(Name, "sends", name.Owner, name.File, name.Line, ("template", path), ("pattern", pattern.Owner));
                    named.Add(path);
                    namedByAny.Add(path);
                }
                // What sits beside them that no name names: maybe a name made at runtime. Once per file, on the first pattern.
                foreach (var folder in folders)
                    foreach (var path in byName.Values.SelectMany(v => v).Where(p => Folder(p) == folder && p.EndsWith(ext, StringComparison.Ordinal) && !named.Contains(p)))
                        blind.TryAdd(path, pattern);
            }
            foreach (var (path, pattern) in blind)
                if (!namedByAny.Contains(path))
                    ctx.AddBlindSpot(pattern.Owner, pattern.File, pattern.Line,
                        $"{path} sits with the templates {Short(type)} names through \"{pattern["value"]}\", but nothing there names it: it may be sent with a name built at runtime", Name);
        }
    }

    static string Folder(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";

    static string Short(string id) => id[(id.LastIndexOf('.') + 1)..];

    /// <summary>The repo's files, relative and sorted: git's tracked and untracked-but-not-ignored ones, else a folder walk
    /// that skips build output and packages.</summary>
    static List<string> RepoFiles(string root)
    {
        if (Git.Run(root, "ls-files -co --exclude-standard -z") is { } listed)
            return [.. listed.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Replace('\\', '/').Normalize())
                .Where(p => !p.Split('/').Any(SkippedFolders.Contains)).Order(StringComparer.Ordinal)];
        var result = new List<string>();
        var stack = new Stack<string>([root]);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (!SkippedFolders.Contains(Path.GetFileName(sub))) stack.Push(sub);
            foreach (var f in Directory.EnumerateFiles(dir)) result.Add(Path.GetRelativePath(root, f).Replace('\\', '/').Normalize());
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    static int Lines(string root, string path)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(root, path));
            return text.Length == 0 ? 0 : text.Split('\n').Length - (text.EndsWith('\n') ? 1 : 0);
        }
        catch (IOException) { return 1; }
        catch (UnauthorizedAccessException) { return 1; }
    }
}

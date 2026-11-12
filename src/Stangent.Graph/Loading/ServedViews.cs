using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Stangent.Graph.Loading;

/// <summary>
/// Step 6 (D236): views a plain-SDK project ships for a web app to compile at runtime (nopCommerce's plugins: <c>Content</c>
/// items copied into <c>Nop.Web/Plugins/X</c>, served as <c>~/Plugins/X/Views/Y.cshtml</c>). The build's copy says they are
/// served; the Razor compiler the web projects already use is run on them inside the loaded compilation, so they become
/// <c>view</c> points like any other (N1) instead of ❓ "not found by MVC's lookup rules". Each view's path is its path in the
/// project, which the lookup's "longest tail" rule finds from the deployed path.
/// </summary>
public static partial class ServedViews
{
    /// <summary>The <c>.cshtml</c> files a project's file copies to its build output (<c>Content</c> or <c>None</c> items with
    /// <c>CopyToOutputDirectory</c> other than <c>Never</c>), as paths relative to the project's folder.</summary>
    public static List<string> Copied(string projectFile)
    {
        XDocument doc;
        try { doc = XDocument.Load(projectFile); }
        catch (Exception e) when (e is IOException or System.Xml.XmlException or UnauthorizedAccessException) { return []; }
        var dir = Path.GetDirectoryName(projectFile)!;
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var item in doc.Descendants().Where(e => e.Name.LocalName is "Content" or "None"))
        {
            var copy = item.Elements().FirstOrDefault(e => e.Name.LocalName == "CopyToOutputDirectory")?.Value ?? item.Attribute("CopyToOutputDirectory")?.Value;
            if (copy is null || copy.Trim().Equals("Never", StringComparison.OrdinalIgnoreCase)) continue;
            var pattern = item.Attribute("Include")?.Value ?? item.Attribute("Update")?.Value;
            if (pattern is null) continue;
            foreach (var part in pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => p.Replace('\\', '/')))
            {
                if (!part.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) || part.Contains("$(", StringComparison.Ordinal)) continue;
                if (!part.Contains('*')) { if (File.Exists(Path.Combine(dir, part))) result.Add(part); continue; }
                var rx = new Regex("^" + Regex.Escape(part).Replace(@"\*\*/", "(.*/)?", StringComparison.Ordinal).Replace(@"\*", "[^/]*", StringComparison.Ordinal) + "$", RegexOptions.IgnoreCase);
                foreach (var file in Directory.EnumerateFiles(dir, "*.cshtml", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
                    if (!rel.StartsWith("bin/", StringComparison.Ordinal) && !rel.StartsWith("obj/", StringComparison.Ordinal) && rx.IsMatch(rel)) result.Add(rel);
                }
            }
        }
        return [.. result];
    }

    /// <summary>The Razor SDK's source generator and the files it loads with it (its <c>source-generators</c> folder).</summary>
    static bool IsRazor(AnalyzerReference a) =>
        (a.FullPath ?? "").Replace('\\', '/').Contains("Microsoft.NET.Sdk.Razor/source-generators/", StringComparison.Ordinal);

    /// <summary>
    /// The solution with the Razor compiler run on each listed project's served views: the generator taken from a project that
    /// already has it (else the SDK's own), the views added as additional files, and a global config naming each one's path.
    /// </summary>
    public static Solution Add(Solution solution, IEnumerable<(ProjectId Id, string Dir, List<string> Views)> projects, List<string> warnings)
    {
        var host = solution.Projects.FirstOrDefault(p => p.AnalyzerReferences.Any(IsRazor));
        List<AnalyzerReference> razor = host is not null ? [.. host.AnalyzerReferences.Where(IsRazor)] : SdkGenerator();
        if (razor.Count == 0)
        {
            warnings.Add("served views: no Razor compiler found (no web project loaded and none in the SDK); views compiled only at runtime stay ❓");
            return solution;
        }
        var options = host is null ? [] : HostOptions(host);
        foreach (var (id, dir, listed) in projects)
        {
            var project = solution.GetProject(id);
            if (project is null) continue;
            // A view the project already hands the compiler (a Razor SDK project) is compiled already.
            var known = project.AdditionalDocuments.Select(d => d.FilePath).OfType<string>().ToHashSet(StringComparer.Ordinal);
            List<string> views = [.. listed.Where(v => !known.Contains(Path.Combine(dir, v)))];
            if (views.Count == 0) continue;
            if (!project.AnalyzerReferences.Any(IsRazor)) solution = solution.AddAnalyzerReferences(id, razor);
            // Two global configs setting one key cancel it out: only keys the project's own build config doesn't set.
            var own = GlobalKeys(project);
            var config = new StringBuilder("is_global = true\n");
            void Set(string key, string value) { if (own.Add(key)) config.Append($"{key} = {value}\n"); }
            foreach (var line in options) Set(line.Split('=')[0].Trim(), line[(line.IndexOf('=') + 1)..].Trim());
            Set("build_property.RootNamespace", project.DefaultNamespace ?? project.AssemblyName);
            Set("build_property.MSBuildProjectDirectory", dir);
            Set("build_property.RazorLangVersion", "latest");
            foreach (var view in views)
            {
                var full = Path.Combine(dir, view);
                solution = solution.AddAdditionalDocument(DocumentId.CreateNewId(id), Path.GetFileName(view), SourceText.From(File.ReadAllText(full), Encoding.UTF8), filePath: full);
                config.Append($"\n[{full.Replace('\\', '/')}]\n");
                config.Append($"build_metadata.AdditionalFiles.TargetPath = {Convert.ToBase64String(Encoding.UTF8.GetBytes(view))}\n");
                config.Append("build_metadata.AdditionalFiles.CssScope = \n");
            }
            solution = solution.AddAnalyzerConfigDocument(DocumentId.CreateNewId(id), "stangent-served-views.globalconfig",
                SourceText.From(config.ToString(), Encoding.UTF8), filePath: Path.Combine(dir, "obj", "stangent-served-views.globalconfig"));
        }
        return solution;
    }

    /// <summary>The keys a project's own global build configs set.</summary>
    static HashSet<string> GlobalKeys(Project project)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var doc in project.AnalyzerConfigDocuments)
            if (doc.GetTextAsync().GetAwaiter().GetResult() is { } text && text.Lines.Any(l => l.ToString().Replace(" ", "", StringComparison.Ordinal) == "is_global=true"))
                foreach (var l in text.Lines.Select(l => l.ToString().Trim()).TakeWhile(l => !l.StartsWith('[')).Where(l => l.Contains('=') && !l.StartsWith("is_global", StringComparison.Ordinal)))
                    keys.Add(l.Split('=')[0].Trim());
        return keys;
    }

    /// <summary>The Razor settings a web project builds with (<c>build_property.Razor…</c> and the few others the compiler reads).</summary>
    static List<string> HostOptions(Project host)
    {
        foreach (var doc in host.AnalyzerConfigDocuments.Where(d => d.FilePath?.EndsWith(".GeneratedMSBuildEditorConfig.editorconfig", StringComparison.Ordinal) == true))
            if (doc.TryGetText(out var text) || (text = doc.GetTextAsync().GetAwaiter().GetResult()) is not null)
                return [.. text.Lines.Select(l => l.ToString().Trim()).TakeWhile(l => !l.StartsWith('['))
                    .Where(l => l.StartsWith("build_property.Razor", StringComparison.Ordinal) || l.StartsWith("build_property.GenerateRazor", StringComparison.Ordinal)
                        || l.StartsWith("build_property.SupportLocalizedComponentNames", StringComparison.Ordinal) || l.StartsWith("build_property.TargetFramework", StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)];
        return [];
    }

    /// <summary>The SDK's Razor source generator folder, when no loaded project carries it.</summary>
    static List<AnalyzerReference> SdkGenerator()
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var sdkRoot = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..", "sdk"));
        if (!Directory.Exists(sdkRoot)) return [];
        var dir = Directory.EnumerateDirectories(sdkRoot).OrderByDescending(d => d, StringComparer.Ordinal)
            .Select(d => Path.Combine(d, "Sdks", "Microsoft.NET.Sdk.Razor", "source-generators"))
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "Microsoft.CodeAnalysis.Razor.Compiler.dll")));
        if (dir is null) return [];
        var loader = new Loader();
        return [.. Directory.EnumerateFiles(dir, "*.dll").Order(StringComparer.Ordinal).Select(f => (AnalyzerReference)new AnalyzerFileReference(f, loader))];
    }

    sealed class Loader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath) { }
        public Assembly LoadFromPath(string fullPath) => AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }
}

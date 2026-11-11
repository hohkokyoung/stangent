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

    static bool IsRazor(AnalyzerReference a) =>
        (a.Display ?? "").Contains("Razor", StringComparison.Ordinal) && (a.FullPath ?? "").Contains("source-generators", StringComparison.Ordinal);

    /// <summary>
    /// The solution with the Razor compiler run on each listed project's served views: the generator taken from a project that
    /// already has it (else the SDK's own), the views added as additional files, and a global config naming each one's path.
    /// </summary>
    public static Solution Add(Solution solution, IEnumerable<(ProjectId Id, string Dir, List<string> Views)> projects, List<string> warnings)
    {
        var host = solution.Projects.FirstOrDefault(p => p.AnalyzerReferences.Any(IsRazor));
        var razor = host?.AnalyzerReferences.First(IsRazor) ?? SdkGenerator();
        if (razor is null)
        {
            warnings.Add("served views: no Razor compiler found (no web project loaded and none in the SDK); views compiled only at runtime stay ❓");
            return solution;
        }
        var options = host is null ? [] : HostOptions(host);
        foreach (var (id, dir, views) in projects)
        {
            var project = solution.GetProject(id);
            if (project is null || views.Count == 0 || project.AnalyzerReferences.Any(IsRazor)) continue;
            solution = solution.AddAnalyzerReference(id, razor);
            var config = new StringBuilder("is_global = true\n");
            foreach (var line in options) config.Append(line).Append('\n');
            config.Append($"build_property.RootNamespace = {project.DefaultNamespace ?? project.AssemblyName}\n");
            config.Append($"build_property.MSBuildProjectDirectory = {dir}\n");
            if (!options.Any(o => o.StartsWith("build_property.RazorLangVersion", StringComparison.Ordinal))) config.Append("build_property.RazorLangVersion = latest\n");
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

    /// <summary>The SDK's Razor source generator, when no loaded project carries it.</summary>
    static AnalyzerReference? SdkGenerator()
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var sdkRoot = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..", "sdk"));
        if (!Directory.Exists(sdkRoot)) return null;
        var dll = Directory.EnumerateDirectories(sdkRoot).OrderByDescending(d => d, StringComparer.Ordinal)
            .Select(d => Path.Combine(d, "Sdks", "Microsoft.NET.Sdk.Razor", "source-generators", "Microsoft.CodeAnalysis.Razor.Compiler.dll"))
            .FirstOrDefault(File.Exists);
        return dll is null ? null : new AnalyzerFileReference(dll, new Loader());
    }

    sealed class Loader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath) { }
        public Assembly LoadFromPath(string fullPath) => AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }
}

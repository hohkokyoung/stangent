using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stangent.Core;
using Stangent.Graph;
using Stangent.Graph.Impact;
using Stangent.Graph.Loading;

/// <summary>
/// Test 4 — compile-break ground truth. For a sample of methods: rename the method in memory,
/// recompile every project that depends on it, and record which other files now fail to compile.
/// That is the compiler's own answer to "what breaks if this method's signature changes", with no
/// history and no AI. It checks the ✅ part of the graph directly: every direct reference should
/// be found. Only compile breakage counts, so 🟡 links (routes, events, HTTP) are reported as
/// going beyond what this test can see, not as wrong.
/// </summary>
static class CompileBreak
{
    const string Suffix = "__StangentBroken";

    public static async Task<JsonObject> Run(StangentRepo repo, CodeGraph graph, ImpactAnalyzer analyzer, int count,
        Func<List<Node>, HashSet<string>, int?, Label, Dictionary<string, Label>> graphPredict,
        Func<IEnumerable<string>, IReadOnlySet<string>, Dictionary<string, Label>> sameFolder,
        Func<IEnumerable<string>, IReadOnlySet<string>, Dictionary<string, Label>> sameProject)
    {
        var projects = ProjectScanner.Find(repo.Root);
        using var workspace = GraphBuilder.CreateWorkspace(repo.Root, projects);
        foreach (var p in projects)
        {
            if (workspace.CurrentSolution.Projects.Any(x => x.FilePath == p.FullPath)) continue;
            try { await workspace.OpenProjectAsync(p.FullPath); }
            catch (Exception e) when (e is not OperationCanceledException) { Console.Error.WriteLine($"compile-break: {p.RelativePath} didn't load"); }
        }
        var solution = workspace.CurrentSolution;
        // One Roslyn project per .csproj (multi-targeted projects load once per target framework).
        var chosen = solution.Projects.Where(x => x.FilePath is not null).GroupBy(x => x.FilePath!)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Name, StringComparer.Ordinal).First().Id);
        var chosenIds = chosen.Values.ToHashSet();

        Console.Error.WriteLine("compile-break: compiling the baseline");
        var baseline = new Dictionary<ProjectId, HashSet<string>>();
        foreach (var id in chosenIds) baseline[id] = await Errors(solution.GetProject(id)!, repo.Root);

        // Candidates: non-test public/internal/protected ordinary methods declared in a loaded project.
        var candidates = graph.AllNodes
            .Where(n => n.Kind == NodeKind.Method && !n.IsTest && n.File is not null && !n.Name.EndsWith(".ctor", StringComparison.Ordinal)
                && !n.Name.Contains('<') && n.Project is not null && !n.Project.Contains("Test", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(n => n.Id).OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        var deps = solution.GetProjectDependencyGraph();
        var cases = new List<(Node Node, HashSet<string> Broken)>();
        var tried = 0;
        foreach (var node in Spread(candidates))
        {
            if (cases.Count >= count) break;
            tried++;
            var mutated = Mutate(solution, repo.Root, node);
            if (mutated is null) continue;
            var (newSolution, mutatedProjects) = mutated.Value;
            var affected = mutatedProjects.SelectMany(p => deps.GetProjectsThatTransitivelyDependOnThisProject(p).Append(p))
                .Where(chosenIds.Contains).Distinct().ToList();
            var broken = new HashSet<string>(StringComparer.Ordinal);
            var errorSets = await Task.WhenAll(affected.Select(p => Errors(newSolution.GetProject(p)!, repo.Root)));
            for (var i = 0; i < affected.Count; i++)
                foreach (var e in errorSets[i].Except(baseline[affected[i]])) broken.Add(e.Split('\t')[0]);
            broken.Remove(node.File!);
            cases.Add((node, broken));
            Console.Error.WriteLine($"compile-break: {cases.Count}/{count} {node.Name}: {broken.Count} file(s) break");
        }

        JsonObject Score(Func<(Node Node, HashSet<string> Broken), Dictionary<string, Label>> predict)
        {
            int hits = 0, actual = 0, predicted = 0;
            var sizes = new List<int>();
            foreach (var c in cases)
            {
                var p = predict(c).Keys.ToHashSet(StringComparer.Ordinal);
                hits += c.Broken.Count(p.Contains); actual += c.Broken.Count; predicted += p.Count; sizes.Add(p.Count);
            }
            sizes.Sort();
            return new JsonObject
            {
                ["recall"] = actual == 0 ? 0 : Math.Round((double)hits / actual, 3),
                ["precision"] = predicted == 0 ? 0 : Math.Round((double)hits / predicted, 3),
                ["found"] = hits, ["of"] = actual,
                ["medianPredictedFiles"] = sizes.Count == 0 ? 0 : sizes[sizes.Count / 2],
            };
        }

        HashSet<string> Exclude((Node Node, HashSet<string>) c) => [c.Node.File!];
        // A file compiled into several projects declares the method once per project: seed every copy,
        // as a diff of that file would (the rename breaks callers of all of them).
        var copies = graph.AllNodes.Where(n => n.Kind == NodeKind.Method && n.File is not null)
            .ToLookup(n => (n.File!, n.Id[(n.Id.IndexOf('|') + 1)..]));
        List<Node> Seeds(Node n) => [.. copies[(n.File!, n.Id[(n.Id.IndexOf('|') + 1)..])].DistinctBy(x => x.Id)];
        var misses = new JsonArray();
        foreach (var c in cases)
        {
            var predicted = graphPredict(Seeds(c.Node), Exclude(c), 1, Label.Certain);
            foreach (var f in c.Broken.Where(f => !predicted.ContainsKey(f)).Order(StringComparer.Ordinal).Take(3))
                if (misses.Count < 25) misses.Add($"{c.Node.Name} → {f}");
        }

        return new JsonObject
        {
            ["methods"] = cases.Count,
            ["tried"] = tried,
            ["methodsWithBreakage"] = cases.Count(c => c.Broken.Count > 0),
            ["brokenFiles"] = cases.Sum(c => c.Broken.Count),
            ["note"] = "Ground truth = files with new compile errors after renaming the method, its own file excluded. Only ✅ links are expected to match; 🟡 links (routes, events, HTTP) break things the compiler can't see.",
            ["graph ✅ depth ≤ 1"] = Score(c => graphPredict(Seeds(c.Node), Exclude(c), 1, Label.Certain)),
            ["graph ✅ depth ≤ 2"] = Score(c => graphPredict(Seeds(c.Node), Exclude(c), 2, Label.Certain)),
            ["graph ✅ unlimited"] = Score(c => graphPredict(Seeds(c.Node), Exclude(c), null, Label.Certain)),
            ["graph ✅+🟡 unlimited"] = Score(c => graphPredict(Seeds(c.Node), Exclude(c), null, Label.Guessed)),
            ["same folder"] = Score(c => sameFolder([c.Node.File!], Exclude(c))),
            ["same project"] = Score(c => sameProject([c.Node.File!], Exclude(c))),
            ["graphDirectMisses"] = misses,
        };
    }

    /// <summary>Every k-th candidate first, then the rest, so skipped mutations don't bias the sample.</summary>
    static IEnumerable<Node> Spread(List<Node> items)
    {
        var step = Math.Max(1, items.Count / 200);
        for (var offset = 0; offset < step; offset++)
            for (var i = offset; i < items.Count; i += step) yield return items[i];
    }

    /// <summary>Renames the method in every project that compiles its file. Null when it isn't a plain method declaration.</summary>
    static (Solution, List<ProjectId>)? Mutate(Solution solution, string root, Node node)
    {
        var path = Path.Combine(root, node.File!);
        var name = node.Name[(node.Name.LastIndexOf('.') + 1)..];
        var newSolution = solution;
        var projects = new List<ProjectId>();
        foreach (var docId in solution.GetDocumentIdsWithFilePath(path))
        {
            var doc = solution.GetDocument(docId)!;
            var tree = doc.GetSyntaxTreeAsync().GetAwaiter().GetResult()!;
            var text = tree.GetText();
            var decl = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(m =>
                m.Identifier.ValueText == name && text.Lines.GetLineFromPosition(m.SpanStart).LineNumber + 1 == node.Line);
            if (decl is null || decl.ExplicitInterfaceSpecifier is not null
                || decl.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword) || m.IsKind(SyntaxKind.PartialKeyword) || m.IsKind(SyntaxKind.ExternKeyword) || m.IsKind(SyntaxKind.PrivateKeyword))
                || (!decl.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword))
                    && decl.Parent is not InterfaceDeclarationSyntax))
                return null;
            var root2 = tree.GetRoot().ReplaceToken(decl.Identifier, SyntaxFactory.Identifier(name + Suffix).WithTriviaFrom(decl.Identifier));
            newSolution = newSolution.WithDocumentSyntaxRoot(docId, root2);
            projects.Add(docId.ProjectId);
        }
        return projects.Count == 0 ? null : (newSolution, projects);
    }

    /// <summary>A project's compile errors in repo source files, as "file\tid\tline\tmessage".</summary>
    static async Task<HashSet<string>> Errors(Project project, string root)
    {
        var compilation = await project.GetCompilationAsync();
        if (compilation is null) return [];
        return [.. compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource && d.Location.SourceTree?.FilePath is { } f
                && f.StartsWith(root, StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(d => $"{Path.GetRelativePath(root, d.Location.SourceTree!.FilePath).Replace('\\', '/')}\t{d.Id}\t{d.Location.GetLineSpan().StartLinePosition.Line}\t{d.GetMessage()}")];
    }
}

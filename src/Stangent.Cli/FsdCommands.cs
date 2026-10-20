using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;

/// <summary><c>stangent fsd …</c>: the agreed FSD, imported by code and linked to the reference pages (Phase 2b, D52–D53).</summary>
static class FsdCommands
{
    public static void Add(RootCommand root)
    {
        var fsd = new Command("fsd", "The agreed FSD: import it (code only) and link it to the reference pages (D52, D53).");
        root.Subcommands.Add(fsd);

        var fileArg = new Argument<string>("file") { Description = "The .docx to import." };
        var idOption = new Option<string?>("--id") { Description = "The document's id (default: from its file name)." };
        var import = new Command("import", "Convert a .docx to Markdown by code, archive the original (D16) and record its sections (source-document v1).") { fileArg, idOption };
        import.SetAction(pr => Cli.Run(pr, "fsd import", ctx =>
        {
            var result = FsdImport.Import(ctx.Repo, pr.GetValue(fileArg)!, pr.GetValue(idOption));
            ctx.Span.Attributes["stangent.artifact.count"] = (int?)result["sections"] ?? 0;
            return result;
        }));
        fsd.Subcommands.Add(import);
    }
}

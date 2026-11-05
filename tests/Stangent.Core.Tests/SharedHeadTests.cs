using System.Text.Json.Nodes;
using Stangent.Spec.Fsd;
using Stangent.Spec.Pages;

namespace Stangent.Core.Tests;

/// <summary>D130: a bundle on disk holds what's unique and a pointer; the full text comes back byte for byte, and the saved hashes are taken on that.</summary>
public class SharedHeadTests
{
    static string Saved(string label) => SavedWorkTests.Saved.Single(l => l.StartsWith(label + " ", StringComparison.Ordinal)).Split(' ').Last();

    static string Work(SpecApp app, RunStep step, string name) => Path.Combine(app.Root, Path.GetDirectoryName(step.Output!)!, name);

    [Fact]
    public void The_writers_bundle_on_disk_holds_no_instructions_and_assembles_to_the_hashed_text()
    {
        using var app = new SpecApp();
        var page = app.Pages().Find(SpecApp.Post)!;
        var step = PageRun.Prepare(app.Pages(), page, app.Repo);
        var stored = File.ReadAllText(Work(app, step, "bundle.md"));
        Assert.DoesNotContain(Bundles.Instructions("page-writer.md"), stored);
        Assert.DoesNotContain("Output schema", stored);
        Assert.StartsWith("⟦stangent shared page instructions=page-writer.md schema=page-writer-output.v1.json⟧\n", stored);

        var built = Bundles.Build(app.Pages(), page, null);
        Assert.Equal(built.Text, SharedHead.Assemble(stored));
        // The pin itself: the same bytes the saved hash was taken on.
        Assert.Equal(Saved("writer"), built.InputHash);
        Assert.Equal(built.InputHash, CanonicalJson.Hash(SharedHead.Assemble(stored)[..built.Chars]));
    }

    [Fact]
    public void The_verifiers_and_judges_files_assemble_to_what_the_saved_hashes_cover()
    {
        using var app = SavedWorkTests.App(out var verifierInput);
        Assert.Equal(Saved("verifier"), SavedWorkTests.Sha(verifierInput));

        var step = LinkRun.Prepare(app.Pages(), app.Repo, "shop", "1");
        var stored = File.ReadAllText(Work(app, step, "judge.md"));
        Assert.DoesNotContain(Bundles.Instructions("link-judge.md"), stored);
        var judge = SharedHead.Assemble(stored);
        Assert.Contains(Bundles.Instructions("link-judge.md"), judge);
        // What JudgeInputs hashes: the judge's whole input, then the checker's instructions and schema.
        var inputs = CanonicalJson.Hash(judge + Bundles.Instructions("agree-checker.md") + SchemaRegistry.EmbeddedText("agree-checker-output.v1.json"));
        Assert.Equal(Saved("judge"), inputs);
    }

    [Fact]
    public void Schema_once_leaves_out_only_the_schema()
    {
        using var app = new SpecApp();
        var step = PageRun.Prepare(app.Pages(), app.Pages().Find(SpecApp.Post)!, app.Repo);
        var stored = File.ReadAllText(Work(app, step, "bundle.md"));
        var whole = SharedHead.Assemble(stored);
        var once = SharedHead.Assemble(stored, schemaOnce: true);
        Assert.Contains(Bundles.Instructions("page-writer.md"), once);
        Assert.DoesNotContain("## Output schema", once);
        Assert.Equal(whole.Replace("## Output schema\n\n```json\n" + SchemaRegistry.EmbeddedText(Bundles.WriterSchema).TrimEnd('\n') + "\n```\n\n", ""), once);
        Assert.False(StangentComponents.IsOn(new StangentConfig(), "schema-once"));
    }

    [Fact]
    public void Only_the_first_line_is_a_pointer_and_an_unknown_one_is_refused()
    {
        const string plain = "some source\n⟦stangent shared step instructions=link-judge.md schema=link-judge-output.v1.json⟧\n";
        Assert.Same(plain, SharedHead.Assemble(plain));
        Assert.Same(plain, SharedHead.Store(plain));
        Assert.Throws<StangentException>(() => SharedHead.Assemble("⟦stangent shared step instructions=other.md schema=x.json⟧\nrest"));
        // A pointer-shaped line in the unique part stays in the unique part.
        var full = SharedHead.Step("agree-checker.md", "agree-checker-output.v1.json") + "⟦stangent shared step instructions=link-judge.md schema=link-judge-output.v1.json⟧\n";
        Assert.Equal(full, SharedHead.Assemble(SharedHead.Store(full)));
    }
}

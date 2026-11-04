using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Fsd;
using Stangent.Spec.Trace;

namespace Stangent.Spec.Pages;

/// <summary>
/// "How does the app do X today?", answered from the reference pages with citations (E.2, D124). Off by default. Code picks the pages by the question's words, a one-turn step answers
/// with statement ids and no tools, and code checks every id and adds the code's lines from <c>reference-lines</c>. An answer with nothing cited is refused. Pages are generated, so an answer says so.
/// </summary>
public static class PageAsk
{
    public const string Asker = "stangent-page-asker";
    public const string OutputSchema = "page-asker-output.v1.json";
    public const string NotCovered = "The pages don't cover this.";
    public const string Label = "as the pages describe it";

    public static string Id(string question) => "ask-" + CanonicalJson.Hash(Quotes.Normalize(question))[..16];

    static string Work(string root, string question) => Path.Combine(root, ".stangent", "spec-ask", Id(question));

    /// <summary>A page's words in its title, its code's title and its route. Scored apart from its statements, as a title says what a page is about.</summary>
    static HashSet<string> TitleWords(string id, JsonObject page)
    {
        var route = id.StartsWith("page:", StringComparison.Ordinal) ? id["page:".Length..] : id.StartsWith("endpoint:", StringComparison.Ordinal) ? id[(id.IndexOf('|') + 1)..] : "";
        return Candidates.Words($"{(string?)page["title"]} {(string?)page["codeTitle"]} {route}");
    }

    /// <summary>
    /// Pages by the question's words: most words in a title or route, then most in its statements, then most statements holding one, then by id, so the same question always picks the same pages.
    /// No weights to tune: the order of those is the rule. A page holding none of the words isn't picked. <c>spec.askMaxPages</c> is the cap.
    /// </summary>
    public static List<(string Page, int InTitle, int InStatements, int Statements)> Pick(SpecIndex index, string question, int cap)
    {
        var words = Candidates.Words(question);
        return [.. index.Pages.Select(p =>
            {
                var statements = PageChecks.Statements(p.Value).Select(s => Candidates.Words((string)s.Statement["text"]!)).ToList();
                return (Page: p.Key, InTitle: TitleWords(p.Key, p.Value).Intersect(words).Count(),
                    InStatements: statements.SelectMany(s => s).Distinct().Intersect(words).Count(), Statements: statements.Count(s => s.Overlaps(words)));
            })
            .Where(p => p.InTitle + p.InStatements > 0)
            .OrderByDescending(p => p.InTitle).ThenByDescending(p => p.InStatements).ThenByDescending(p => p.Statements).ThenBy(p => p.Page, StringComparer.Ordinal)
            .Take(cap)];
    }

    /// <summary>What runs next, and the answer if there's already one: none needs a call when no page holds a word of the question or the same pages already answered it.</summary>
    public static (RunStep Step, JsonObject? Answer) Prepare(StangentRepo repo, string question)
    {
        if (!StangentComponents.IsOn(repo.Config, "spec-ask"))
            return (new RunStep("off", "none", Warning: "spec-ask is off (spec.ask), so nothing was asked. That isn't the pages not covering it."), null);
        var index = SpecIndex.Load(repo.Root);
        var picked = Pick(index, question, repo.Config.Spec.AskMaxPages);
        if (picked.Count == 0) return (new RunStep("not-covered", "none"), Refused(new JsonArray(), "no page holds a word of the question, so nothing was asked"));

        var pages = picked.Select(p => p.Page).ToList();
        var bundle = Bundle(question, pages, index);
        var inputs = CanonicalJson.Hash(bundle);
        var work = Work(repo.Root, question);
        if (PageRun.LoadJson(Path.Combine(work, "answer.json")) is JsonObject saved && (string?)saved["inputs"] == inputs)
            return (new RunStep((string)saved["said"]!, "none"), (JsonObject)saved["answer"]!.DeepClone());

        AiPolicy.EnsureAllowed(repo.Config, "spec ask");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "bundle.md"), bundle);
        File.WriteAllText(Path.Combine(work, "state.json"), CanonicalJson.Serialize(new JsonObject
        {
            ["question"] = question, ["inputs"] = inputs, ["retryUsed"] = false, ["schema"] = OutputSchema,
            ["pages"] = new JsonArray([.. pages.Select(p => (JsonNode)p)]),
            ["picked"] = new JsonArray([.. picked.Select(p => (JsonNode)new JsonObject { ["page"] = p.Page, ["inTitle"] = p.InTitle, ["inStatements"] = p.InStatements, ["statements"] = p.Statements })]),
        }));
        return (LinkRun.Step(repo.Root, work, Asker, "bundle.md", "output.json", "Question and pages", OutputSchema), null);
    }

    static string Bundle(string question, List<string> pages, SpecIndex index)
    {
        var sb = new StringBuilder("# Instructions\n\n").Append(Bundles.Instructions("page-asker.md"));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText(OutputSchema).TrimEnd('\n')).Append("\n```\n\n");
        sb.Append("# The question\n\n").Append(question.Trim()).Append('\n');
        sb.Append(LinkRun.PagesBlock(pages, index));
        return sb.ToString();
    }

    /// <summary>Checks the answer by code: every id on a page in the bundle, every sentence citing one. One retry, then it's refused: never an answer with a citation code couldn't find.</summary>
    public static (RunStep Step, JsonObject? Answer) Check(StangentRepo repo, string question, Usage? usage)
    {
        var work = Work(repo.Root, question);
        if (PageRun.LoadJson(Path.Combine(work, "state.json")) is not JsonObject state)
            throw new StangentException("not_prepared", "No question like that is waiting. Run `stangent spec ask` first.", ExitCodes.Usage);
        var index = SpecIndex.Load(repo.Root);
        var pages = state["pages"]!.AsArray().Select(p => (string)p!).ToHashSet(StringComparer.Ordinal);
        var errors = new List<string>();
        var text = File.Exists(Path.Combine(work, "output.json")) ? File.ReadAllText(Path.Combine(work, "output.json")) : "";
        var parsed = PageChecks.Parse(text, errors);
        if (errors.Count == 0) errors.AddRange(SchemaRegistry.Embedded.Validate(OutputSchema, parsed!.ToJsonString()).Select(e => $"Schema: {e}"));
        var sentences = errors.Count == 0 ? parsed!["answer"]!.AsArray().OfType<JsonObject>().ToList() : [];
        if (errors.Count == 0)
            foreach (var (s, i) in sentences.Select((s, i) => (s, i)))
            {
                if (s["cites"]!.AsArray().Count == 0) errors.Add($"answer[{i}] cites nothing: every sentence cites a statement, or the answer is empty");
                foreach (var c in s["cites"]!.AsArray())
                {
                    var (page, statement) = ((string)c!["page"]!, (string)c["statement"]!);
                    if (!pages.Contains(page)) errors.Add($"answer[{i}] cites page `{page}`, which isn't in the bundle");
                    else if (index.Statement(page, statement) is null) errors.Add($"answer[{i}] cites {statement} of `{page}`, which that page doesn't have");
                }
            }
        if (errors.Count > 0 && LinkRun.Retry(repo.Root, work, state, errors, Asker, "bundle.md", "output.json") is { } retry) return (retry, null);
        var (said, answer) = errors.Count > 0 ? ("not-covered", Refused(state["pages"]!.DeepClone(), "the answer failed its check twice: " + string.Join("; ", errors.Take(3))))
            : sentences.Count == 0 ? ("not-covered", Refused(state["pages"]!.DeepClone(), "the answer cited nothing"))
            : ("answered", Answered(repo.Root, sentences, index, state["pages"]!.DeepClone()));
        if (usage is not null) answer["usage"] = usage.ToJson();
        File.WriteAllText(Path.Combine(work, "answer.json"), CanonicalJson.Serialize(new JsonObject { ["inputs"] = (string)state["inputs"]!, ["said"] = said, ["answer"] = answer.DeepClone() }));
        return (new RunStep(said, "none"), answer);
    }

    static JsonObject Refused(JsonNode pages, string why) => new()
    {
        ["text"] = NotCovered, ["sentences"] = new JsonArray(), ["pages"] = pages, ["why"] = why,
    };

    /// <summary>Each sentence with what it cites: the statement's words and the code's lines from <c>reference-lines</c>. A symbol with no lines says why, rather than being left out.</summary>
    static JsonObject Answered(string root, List<JsonObject> sentences, SpecIndex index, JsonNode pages)
    {
        var notCited = new JsonArray();
        var out_ = new JsonArray();
        foreach (var s in sentences)
        {
            var cites = new JsonArray();
            foreach (var c in s["cites"]!.AsArray())
            {
                var (page, statement) = ((string)c!["page"]!, (string)c["statement"]!);
                var found = index.Statement(page, statement)!;
                var read = index.PageFiles.TryGetValue(page, out var file) ? PageLines.Load(file) : null;
                var lines = new JsonArray();
                foreach (var symbol in found["cites"]!.AsArray().Select(x => (string)x!))
                {
                    var at = read?["symbols"]?[symbol] as JsonObject;
                    var line = new JsonObject { ["symbol"] = symbol, ["at"] = PageLines.Cite(at) };
                    if (PageLines.Cite(at) is null) line["why"] = (string?)at?["why"] ?? (string?)read?["note"] ?? "this page has no line citations (`spec cite`, or line-citations is off)";
                    lines.Add((JsonNode)line);
                }
                if (lines.All(l => l!["at"] is null)) notCited.Add((JsonNode)new JsonObject { ["page"] = page, ["statement"] = statement });
                cites.Add((JsonNode)new JsonObject { ["page"] = page, ["statement"] = statement, ["says"] = (string)found["text"]!, ["commit"] = (string?)read?["commit"], ["lines"] = lines });
            }
            out_.Add((JsonNode)new JsonObject { ["text"] = (string)s["text"]!, ["cites"] = cites });
        }
        var answer = new JsonObject
        {
            ["label"] = Label,
            ["text"] = "As the pages describe it: " + string.Join(' ', sentences.Select(s => (string)s["text"]!)),
            ["sentences"] = out_, ["pages"] = pages,
        };
        if (notCited.Count > 0) answer["notCited"] = notCited;
        return answer;
    }
}

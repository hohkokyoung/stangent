using System.Text;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Spec.Pages;

namespace Stangent.Tickets;

/// <summary>One of Phase 3's AI steps (D60): which subagent, its output schema, and its files in the ticket's folder.</summary>
public sealed record StepSpec(string Name, string Agent, string Schema, string Instructions)
{
    public const string PointPicker = "stangent-point-picker";
    public const string BugReproducer = "stangent-bug-reproducer";
    public const string QuestionWriter = "stangent-question-writer";
    public const string SummaryWriter = "stangent-summary-writer";

    public static readonly StepSpec Points = new("points", PointPicker, "point-picker-output.v1.json", "point-picker.md");
    public static readonly StepSpec Prove = new("prove", BugReproducer, "bug-reproducer-output.v1.json", "bug-reproducer.md");
    public static readonly StepSpec Questions = new("questions", QuestionWriter, "draft-output.v1.json", "question-writer.md");
    public static readonly StepSpec Resolution = new("resolution", SummaryWriter, "draft-output.v1.json", "summary-writer.md");

    public static StepSpec Named(string name) => name switch
    {
        "points" => Points, "prove" => Prove, "questions" => Questions, "resolution" => Resolution,
        _ => throw new StangentException("usage", $"Unknown step '{name}'. Steps: points, prove, questions, resolution.", ExitCodes.Usage),
    };

    public string InputFile => "input.md";
    public string OutputFile => "output.json";
}

/// <summary>
/// The shared loop of Phase 3's AI steps, as in Phase 2 (D60): <c>prepare</c> writes the step's input as one file and
/// names the subagent; <c>check</c> reads its JSON, validates the schema and the step's own checks (D18), and gives the
/// subagent one retry with the specific errors. An input hash already recorded means no AI call (D18).
/// </summary>
public static class AiStep
{
    public static string Instructions(string name)
    {
        using var stream = typeof(AiStep).Assembly.GetManifestResourceStream("ticket-instructions/" + name)
            ?? throw new StangentException("missing_instructions", $"No instructions named {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>The input file's header: instructions and output schema, as every bundle starts (D60).</summary>
    public static StringBuilder Header(StepSpec step)
    {
        var sb = new StringBuilder("# Instructions\n\n").Append(Instructions(step.Instructions));
        sb.Append("\n# Output schema\n\n```json\n").Append(SchemaRegistry.EmbeddedText(step.Schema).TrimEnd('\n')).Append("\n```\n\n");
        return sb;
    }

    public static string Work(TicketPaths paths, StepSpec step) => paths.Work(step.Name);

    /// <summary>Starts a fresh run: a clean work folder, the input file, and the state with the input hash.</summary>
    public static RunStep Start(TicketPaths paths, StepSpec step, string input, string inputHash, JsonObject? extra = null)
    {
        var work = Work(paths, step);
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, step.InputFile), input);
        var state = extra?.DeepClone().AsObject() ?? new JsonObject();
        state["step"] = step.Name;
        state["inputs"] = inputHash;
        state["retryUsed"] = false;
        state["retries"] = 0;
        state["usage"] = Usage.None.ToJson();
        SaveState(paths, step, state);
        return Next(paths, step, "prepared");
    }

    public static RunStep Next(TicketPaths paths, StepSpec step, string status)
    {
        var work = Work(paths, step);
        return new RunStep(status, step.Name, step.Agent,
            $"Input: {paths.Relative(Path.Combine(work, step.InputFile))}\nWrite your JSON to: {paths.Relative(Path.Combine(work, step.OutputFile))}",
            paths.Relative(Path.Combine(work, step.OutputFile)));
    }

    public static JsonObject State(TicketPaths paths, StepSpec step) =>
        PageRun.LoadJson(Path.Combine(Work(paths, step), "state.json")) as JsonObject
        ?? throw new StangentException("not_prepared", $"No {step.Name} run for {paths.Key}. Run `stangent ticket prepare {paths.Key} --step {step.Name}` first.", ExitCodes.Usage);

    public static void SaveState(TicketPaths paths, StepSpec step, JsonObject state) =>
        Quotes.WriteAtomic(Path.Combine(Work(paths, step), "state.json"), CanonicalJson.Serialize(state));

    /// <summary>Adds the call's usage to the state and reads the output: parsed and schema-checked, or the errors.</summary>
    public static (JsonObject? Output, List<string> Errors) Read(TicketPaths paths, StepSpec step, JsonObject state, Usage? usage)
    {
        state["usage"] = Usage.FromJson(state["usage"]).Add(usage ?? Usage.None).ToJson();
        var file = Path.Combine(Work(paths, step), step.OutputFile);
        var errors = new List<string>();
        var output = PageChecks.Parse(File.Exists(file) ? File.ReadAllText(file) : "", errors) as JsonObject;
        if (errors.Count == 0 && output is null) errors.Add("The output isn't a JSON object.");
        if (errors.Count == 0) errors.AddRange(SchemaRegistry.Embedded.Validate(step.Schema, output!.ToJsonString()).Select(e => $"Schema: {e}"));
        return (errors.Count == 0 ? output : null, errors);
    }

    /// <summary>The one retry (D60): the errors go to <c>feedback.md</c>, the previous output is kept beside it.
    /// Null when the retry was already used.</summary>
    public static RunStep? Retry(TicketPaths paths, StepSpec step, JsonObject state, IReadOnlyList<string> errors)
    {
        if ((bool?)state["retryUsed"] == true) return null;
        state["retryUsed"] = true;
        state["retries"] = ((int?)state["retries"] ?? 0) + 1;
        SaveState(paths, step, state);
        var work = Work(paths, step);
        var sb = new StringBuilder("# The code check failed\n\nYour output wasn't accepted. Fix every problem below and write the whole output again ");
        sb.Append("(one more failure and the step is recorded as failed).\n\n");
        foreach (var e in errors.Take(40)) sb.Append($"- {e}\n");
        File.WriteAllText(Path.Combine(work, "feedback.md"), sb.ToString());
        var output = Path.Combine(work, step.OutputFile);
        if (File.Exists(output)) File.Move(output, Path.Combine(work, "previous-" + step.OutputFile), overwrite: true);
        else File.WriteAllText(Path.Combine(work, "previous-" + step.OutputFile), "(no output)");
        var next = Next(paths, step, "retry");
        return next with
        {
            Errors = [.. errors],
            Prompt = next.Prompt + $"\nYour previous attempt: {paths.Relative(Path.Combine(work, "previous-" + step.OutputFile))}\nWhat to fix: {paths.Relative(Path.Combine(work, "feedback.md"))}",
        };
    }
}

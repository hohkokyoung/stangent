namespace Stangent.Core;

/// <summary>A part of the flow that can be taken out, with the flow still running (D120, D121).</summary>
/// <param name="Setting">The key that controls it. <c>components.&lt;name&gt;</c> works for every one and wins over this key; old keys keep working.</param>
/// <param name="Commands">The commands that use it, so each run can say which were on.</param>
/// <param name="Needs">What it can't work without.</param>
/// <param name="WaitingFor">Set when it's built and nothing outside its own code uses it yet (S.4).</param>
public sealed record StangentComponent(string Name, string Label, string Setting, bool OnByDefault, IReadOnlyList<string> Commands, IReadOnlyList<string> Needs, string? WaitingFor = null)
{
    public bool Connected => WaitingFor is null;
}

/// <summary>Something that can't be switched off cleanly, and why.</summary>
public sealed record NoSwitch(string Name, string Why);

/// <summary>Built, with no switch to give and nothing using it yet (S.4).</summary>
public sealed record NotConnected(string Name, string Label, string WaitingFor);

/// <summary>
/// Every component and its switch (D121). A switch's state is never part of an input hash: a component that's off changes what a step does,
/// and only what really reaches an AI (a judge's page list, say) can move a hash.
/// </summary>
public static class StangentComponents
{
    const string Graph = "graph build";
    static readonly string[] Links = ["fsd prepare", "fsd check"];
    static readonly string[] Pages = ["spec prepare", "spec check"];
    static readonly string[] AiSteps = ["spec prepare", "fsd prepare", "fsd disprove prepare", "req prepare", "ticket points", "ticket prepare", "ticket match", "ticket prove", "ticket draft"];

    static StangentComponent Detector(string name, string label, params string[] needs) =>
        new($"detector-{name}", label, "detectors", true, [Graph], needs);

    public static IReadOnlyList<StangentComponent> All { get; } =
    [
        new("ai", "Every AI step", "ai.enabled", true, AiSteps, []),
        new("git-history", "What usually changes together, from git history", "history.enabled", true, [Graph, "graph impact"], []),
        Detector("aspnetcore-routes", "ASP.NET Core routes"),
        Detector("microsoft-di", "Microsoft DI registrations"),
        Detector("publish-handle", "Publish and handle links"),
        Detector("entity-tables", "Entity to table links"),
        Detector("config-keys", "Configuration keys"),
        Detector("http-services", "Backend HTTP calls to endpoints", "detector-aspnetcore-routes"),
        Detector("frontend-http", "Frontend calls to endpoints, fields to properties", "detector-aspnetcore-routes"),
        new("detector-playwright-specs", "Playwright lines: existing specs' goto to screens", "detectors", true, [Graph, "graph screen-tests"], [],
            "a step that reads opens-screen lines: only graph code does today"),
        new("page-verifier", "The fresh check of each reference page", "components.page-verifier", true, Pages, ["ai"]),
        new("line-citations", "path:lines beside each page, and code evidence from them", "components.line-citations", true, ["spec check", "spec cite", "trace check"], []),
        new("candidate-finder", "The AI that names pages when code finds none", "components.candidate-finder", true, Links, ["ai"]),
        new("agree-checker", "The fresh check of each agree link", "components.agree-checker", true, Links, ["ai"]),
        new("requirements-links", "Judging by requirement when a section's are built", "components.requirements-links", true, Links, []),
        new("first-import-approval", "A person approves a document's first import before anything reads it", "components.first-import-approval", false, ["fsd import", "fsd approve"], []),
        new("attachments-in-checks", "A section's attachments in the link judge's and disprove bundles, and quotes from them accepted", "fsd.attachmentsInChecks", false, [.. Links, "fsd disprove prepare", "fsd disprove check", "fsd disprove estimate", "fsd promote"], []),
        new("findings", "Gap, conflict and undocumented findings", "fsd.findings", false, [.. Links, "fsd undocumented", "trace check", "ticket bundle", "ticket draft", "fsd promote"], []),
        new("disprove", "A second agent tries to refute each finding", "fsd.disprove", false, ["fsd disprove prepare", "fsd disprove check", "fsd disprove estimate"], ["ai", "findings"]),
        new("file-finder", "The AI that finds a ticket's files", "tickets.aiPoints", true, ["ticket points", "ticket prepare", "ticket check"], ["ai"]),
        new("bug-proof", "A failing test before a bug is fixed", "components.bug-proof", true, ["ticket prove", "ticket prepare", "ticket check", "ticket draft"], ["ai"]),
        new("drafts", "Draft comments for a ticket", "components.drafts", true, ["ticket draft", "ticket prepare", "ticket check", "ticket post"], ["ai"]),
        new("ticket-excel-text", "A ticket's workbooks and files inside its attachments reach ticket.json, and so the AI steps. Off, they're only saved beside the attachment", "components.ticket-excel-text", false, ["ticket pull"], []),
    ];

    /// <summary>What can't come out, so a test run with everything off knows where it stops.</summary>
    public static IReadOnlyList<NoSwitch> NoSwitches { get; } =
    [
        new("document-import", "Everything about a document starts from its text; with no import there's no section to run anything on."),
        new("page-writer", "Links, the disprove bundle and ticket bundles read pages. Without one there's nothing to compare, so a step would report a false gap."),
        new("link-judge", "It is the step that makes a link. `ai.enabled: false` stops it, and with it every other AI step."),
        new("code-checks", "Quote checks, schema checks and `trace check` are what make an AI's answer usable (D18). Taking one out would let a made-up quote through."),
        new("state-module", "Every lifecycle moves through it, and a test holds that to be true."),
    ];

    public static IReadOnlyList<NotConnected> NotConnectedYet { get; } =
    [
        new("ran-evidence", "`ran` evidence: a test or run shows it", "a step that really runs something: nothing fills `ran` today, and Phase 4 records what a failing test ran"),
        new("ticket-evidence", "Tickets on the evidence shape", "Tickets are rebuilt onto `evidence` v2: a bundle and a draft still carry their own shape"),
    ];

    public static StangentComponent Find(string name) => All.First(c => c.Name == name);

    /// <summary>Settles <c>components:</c> onto the keys that already exist, so code reading those keys sees it, and refuses a name nobody knows. Runs when the file is read.</summary>
    public static void Apply(StangentConfig config)
    {
        var unknown = config.Components.Keys.Where(k => All.All(c => c.Name != k)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            throw new StangentException("unknown_component", $"stangent.yaml names unknown component(s): {string.Join(", ", unknown)}. `stangent components` lists them.", ExitCodes.Usage);
        foreach (var (name, on) in config.Components)
            switch (name)
            {
                case "ai": config.Ai.Enabled = on; break;
                case "git-history": config.History.Enabled = on; break;
                case "findings": config.Fsd.Findings = on; break;
                case "disprove": config.Fsd.Disprove = on; break;
                case "attachments-in-checks": config.Fsd.AttachmentsInChecks = on; break;
                case "file-finder": config.Tickets.AiPoints = on; break;
            }
    }

    /// <summary>Whether it's on for this config, read from wherever its setting lives.</summary>
    public static bool IsOn(StangentConfig config, string name) => name switch
    {
        "ai" => config.Ai.Enabled,
        "git-history" => config.History.Enabled,
        "findings" => config.Fsd.Findings,
        "disprove" => config.Fsd.Disprove,
        "attachments-in-checks" => config.Fsd.AttachmentsInChecks,
        "file-finder" => config.Tickets.AiPoints,
        _ when name.StartsWith("detector-", StringComparison.Ordinal) => DetectorOn(config, name["detector-".Length..]),
        _ => config.Components.TryGetValue(name, out var on) ? on : All.FirstOrDefault(c => c.Name == name)?.OnByDefault ?? true,
    };

    /// <summary><c>components.detector-x</c> wins; otherwise the <c>detectors</c> list decides: empty is all, <c>[none]</c> is none (D17).</summary>
    public static bool DetectorOn(StangentConfig config, string detector) =>
        config.Components.TryGetValue($"detector-{detector}", out var on) ? on
        : config.Detectors.Count == 0 || config.Detectors.Contains(detector);

    /// <summary>The components this command uses, on and off. Null when it uses none.</summary>
    public static (IReadOnlyList<string> On, IReadOnlyList<string> Off)? ForCommand(StangentConfig config, string command)
    {
        var used = All.Where(c => c.Commands.Contains(command)).ToList();
        if (used.Count == 0) return null;
        return ([.. used.Where(c => IsOn(config, c.Name)).Select(c => c.Name)], [.. used.Where(c => !IsOn(config, c.Name)).Select(c => c.Name)]);
    }

    /// <summary>What a component's being off means for the one that needs it, or null if everything it needs is on.</summary>
    public static string? MissingNeed(StangentConfig config, string name) =>
        Find(name).Needs.FirstOrDefault(n => !IsOn(config, n)) is { } missing ? $"{name} needs {missing}, and {missing} is off ({Find(missing).Setting})." : null;
}

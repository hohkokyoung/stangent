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
    static readonly string[] Pages = ["spec prepare", "spec check"];
    static readonly string[] AiSteps = ["spec prepare", "fsd review", "ticket points", "ticket prepare", "ticket prove", "ticket draft", "fix prepare"];

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
        Detector("validation", "Validation rules on the properties they check"),
        Detector("form-fields", "Fields the server defines (a property named by a Label argument or a display-name attribute) and their choices (D329)"),
        Detector("constants", "Constants' values"),
        Detector("template-files", "Which code sends which template file: a class's string constants that fill its file-name pattern (\"{0}.html\"), linked to the files they name (D362 (5))"),
        Detector("background-jobs", "Work queued to run later (Hangfire)"),
        Detector("jobs", "System jobs (command handlers, scheduled tasks) and the code that queues them"),
        Detector("conditions", "The conditions around each call, and early exits"),
        Detector("menu", "Menus built in C# as data: items with a name and an address, children nested (D221)"),
        Detector("on-load", "What runs as a server-rendered page opens (MVC GET actions returning a view)", "detector-aspnetcore-routes", "detector-razor-views"),
        Detector("config-keys", "Configuration keys"),
        Detector("http-services", "Backend HTTP calls to endpoints", "detector-aspnetcore-routes"),
        Detector("frontend-http", "Frontend calls to endpoints from any routes detector, fields to properties"),
        Detector("signalr", "SignalR messages sent and listened for"),
        Detector("razor-views", "Server-rendered views (Razor), their forms to endpoints, and the views actions render", "detector-aspnetcore-routes"),
        new("root-screens", "A page for each screen a React app's root shows with no route, read from its JSX. Adds pages to a repo that has them, so it's off by default", "components.root-screens", false, [Graph, "spec pages"], []),
        new("route-render", "A page for each React route written with render or children, read from the JSX it shows. Adds pages to a repo that has them, so it's off by default", "components.route-render", false, [Graph, "spec pages"], []),
        new("python", "Python files on the map, by parsing only: never imported or run. Off, a repo's Python isn't read, nor its plain React apps unless root-screens or route-render is on", "components.python", true, [Graph], []),
        Detector("flask-routes", "Flask routes, blueprints and URL rules", "python"),
        new("detector-playwright-specs", "Playwright lines: existing specs' goto to screens", "detectors", true, [Graph, "graph screen-tests"], [],
            "a step that reads opens-screen lines: only graph code does today"),
        new("schema-once", "Batch runs send a step's output schema once, through Claude Code's schema option, and not also in its input text. No hash moves, but it changes what the AI reads, so it's off by default", "components.schema-once", false, ["spec assemble"], ["ai"]),
        new("page-corrections", "A person's approved correction to a page, put back every time the page is written. A corrected statement is what a ticket bundle then shows, so it's off by default", "components.page-corrections", false, ["spec correct", "spec correct-approve", "spec correct-reject", "spec check", "spec cite"], []),
        new("page-exclusions", "A page a person excluded with a reason isn't listed, written or a candidate. That can change what a ticket bundle shows, so it's off by default; `spec coverage` itself always works", "components.page-exclusions", false, ["spec exclude", "spec include", "spec coverage", "spec pages", "spec stale"], []),
        new("follow-moves", "When a page's code only moved, its citations and saved hash follow it with no AI. It rewrites a saved hash, so it's off by default", "components.follow-moves", false, ["spec prepare", "spec stale", "spec read", "spec follow"], []),
        new("statement-update", "When a page is stale, the writer and verifier handle only the statements citing code that changed, and code puts the others back. It changes what the writer gets, so it's off by default", "components.statement-update", false, Pages, ["ai"]),
        new("page-verifier", "The fresh check of each reference page", "components.page-verifier", true, Pages, ["ai"]),
        new("line-citations", "path:lines beside each page, and code evidence from them", "components.line-citations", true, ["spec check", "spec cite", "trace check"], []),
        new("section-approval", "A person approves or rejects each changed section of a new version, and promote applies only what was approved, with --partial imports and readable section files (D128, D142, D150)", "components.section-approval", false, ["fsd import", "fsd approve", "fsd reject", "fsd diff", "fsd promote"], []),
        new("first-import-approval", "A person approves a document's first import before anything reads it", "components.first-import-approval", false, ["fsd import", "fsd approve"], []),
        new("attachment-home", "A section of its own for files with no place in a document's text, such as those attached to a PDF", "fsd.attachmentHome", false, ["fsd import", "fsd reread"], []),
        new("findings", "Gap, conflict and undocumented findings from the old link route, frozen in specs/trace.yaml (D342), shown in ticket bundles and drafts", "fsd.findings", false, ["trace check", "ticket bundle", "ticket draft", "fsd promote"], []),
        new("file-finder", "The AI that finds a ticket's files", "tickets.aiPoints", true, ["ticket points", "ticket prepare", "ticket check"], ["ai"]),
        new("bug-proof", "A failing test before a bug is fixed", "components.bug-proof", true, ["ticket prove", "ticket prepare", "ticket check", "ticket draft"], ["ai"]),
        new("fix", "Phase 4's fix loop: one fix agent per bug on its own branch and worktree, from the bug proof's failing test, every check run by code (D343, D350); never pushes", "components.fix", true, ["fix start", "fix prepare", "fix check", "fix report"], ["ai", "bug-proof"]),
        new("drafts", "Draft comments for a ticket", "components.drafts", true, ["ticket draft", "ticket prepare", "ticket check", "ticket post"], ["ai"]),
        new("table-facts", "A document's tables compared with the code by code, a second route to code: labelled, pending the owner's answer on D68", "fsd.compareTables", false, ["fsd compare-tables", Graph], []),
        new("pages", "Layer 4: each area's page.md as a developer page by code, no AI: per click that changes something a flowchart, a scenario table and its lists, with flow-file step numbers (D278–D285). Written by code and read by nothing yet, so it's off until a person has read them", "components.pages", false, ["areas check", "pages verify"], []),
        new("ticket-excel-text", "A ticket's workbooks and files inside its attachments reach ticket.json, and so the AI steps. Off, they're only saved beside the attachment", "components.ticket-excel-text", false, ["ticket pull"], []),
    ];

    /// <summary>What can't come out, so a test run with everything off knows where it stops.</summary>
    public static IReadOnlyList<NoSwitch> NoSwitches { get; } =
    [
        new("document-import", "Everything about a document starts from its text; with no import there's no section to run anything on."),
        new("page-writer", "Ticket bundles and starting points read pages (D342). Without one a ticket's bundle has no page to show."),
        new("code-checks", "Quote checks, schema checks and `trace check` are what make an AI's answer usable (D18). Taking one out would let a made-up quote through."),
        new("state-module", "Every lifecycle moves through it, and a test holds that to be true."),
        new("cost-stop", "`--max-cost none` already says no cap, per run. It changes no prompt and no result, only whether a run goes on."),
        new("doubtful-suggestions", "Ranking the closest sections for a doubtful match only gives a person an order to read in. It settles nothing, and no step reads it."),
        new("pages-as-needed", "Which pages a run asks for changes what's written when, never what a page is written from: `--all` and `--new` still make every page."),
        new("page-navigation", "The links and footer on a page's Markdown and the index only help a person read. No step reads them, so they change no prompt and no hash."),
        new("run-checks", "The app-files check, the link and id check, the behind-the-code warning and the read-only hook only report or refuse. They change no prompt and no hash, so they can't move a result to compare."),
    ];

    public static IReadOnlyList<NotConnected> NotConnectedYet { get; } =
    [
        new("ran-evidence", "`ran` evidence: a test or run shows it", "a step that really runs something: nothing fills `ran` today, and Phase 4 records what a failing test ran"),
        new("ticket-evidence", "Tickets on the evidence shape", "Tickets are rebuilt onto `evidence` v2: a bundle and a draft still carry their own shape"),
    ];

    /// <summary>Components cut with the old link route and D156 P8's switches (D342), named so a stangent.yaml that still has one is told why.</summary>
    public static IReadOnlySet<string> Cut { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "disprove-small", "disprove-callers", "page-gap-strict", "security-flag", "shared-through", "fsd-claims", "claim-links", "workflow-files", "test-journeys",
        "candidate-finder", "agree-checker", "requirements-links", "attachments-in-checks", "disprove", "judge-finer-verdicts", "group-sections", "spec-ask",
    };

    public static StangentComponent Find(string name) => All.First(c => c.Name == name);

    /// <summary>Settles <c>components:</c> onto the keys that already exist, so code reading those keys sees it, and refuses a name nobody knows. Runs when the file is read.</summary>
    public static void Apply(StangentConfig config)
    {
        var unknown = config.Components.Keys.Where(k => All.All(c => c.Name != k)).Order(StringComparer.Ordinal).ToList();
        var cut = unknown.Where(Cut.Contains).ToList();
        if (cut.Count > 0)
            throw new StangentException("unknown_component", $"stangent.yaml names component(s) cut with the old link route (D342): {string.Join(", ", cut)}. Take them out of stangent.yaml.", ExitCodes.Usage);
        if (unknown.Count > 0)
            throw new StangentException("unknown_component", $"stangent.yaml names unknown component(s): {string.Join(", ", unknown)}. `stangent components` lists them.", ExitCodes.Usage);
        foreach (var (name, on) in config.Components)
            switch (name)
            {
                case "ai": config.Ai.Enabled = on; break;
                case "git-history": config.History.Enabled = on; break;
                case "findings": config.Fsd.Findings = on; break;
                case "attachment-home": config.Fsd.AttachmentHome = on; break;
                case "table-facts": config.Fsd.CompareTables = on; break;
                case "file-finder": config.Tickets.AiPoints = on; break;
            }
    }

    /// <summary>Whether it's on for this config, read from wherever its setting lives.</summary>
    public static bool IsOn(StangentConfig config, string name) => name switch
    {
        "ai" => config.Ai.Enabled,
        "git-history" => config.History.Enabled,
        "findings" => config.Fsd.Findings,
        "attachment-home" => config.Fsd.AttachmentHome,
        "table-facts" => config.Fsd.CompareTables,
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

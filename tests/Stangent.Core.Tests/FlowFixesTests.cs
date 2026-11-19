using Stangent.Flows;
using Stangent.Graph;
using Stangent.Graph.Extraction;

namespace Stangent.Core.Tests;

/// <summary>
/// Layer 3's fixes from gate 2 (D291–D295, D300–D302, flows v2): each gap reproduced on a small hand-made graph, walked by code,
/// and the C# facts they need read from small sources.
/// </summary>
public sealed class FlowFixesTests
{
    const string P = "App|M:App.";
    readonly ProjectFragment fragment = new() { ProjectId = "App", Fingerprint = "" };

    void Method(string id, string file = "App/Svc.cs", int line = 1) =>
        fragment.Nodes.Add(new Node(P + id, NodeKind.Method, id.Split('(')[0], "App", file, line, line + 50));

    void Fact(string detector, string kind, string owner, int line, params (string, string)[] data) =>
        fragment.Facts.Add(new Fact(detector, kind, P + owner, "App/Svc.cs", line, data.ToDictionary(x => x.Item1, x => x.Item2)));

    void Call(string from, string to, int line, params (string, string)[] extra)
    {
        fragment.Edges.Add(new Edge(P + from, P + to, EdgeKind.Calls, Label.Certain));
        Fact("call-sites", "call-site", from, line, [("target", P + to), ("label", "0"), ("kind", "calls"), .. extra]);
    }

    void Package(string from, int line, string package, string method, string ns) =>
        Fact("call-sites", "call-site", from, line, ("target", ""), ("label", "0"), ("kind", "package"), ("package", package), ("method", method), ("ns", ns));

    Flow Walk(string start, FlowSettings? settings = null)
    {
        var graph = new CodeGraph { Commit = "abc" };
        graph.Fragments["App"] = fragment;
        var g = new FlowGraph(graph, Path.GetTempPath());
        var flow = new Flow
        {
            Id = "job-test", Name = "test", Kind = "job", Start = new StartInfo("job", new At("App/Svc.cs", 1, ""), P + start, []),
            Actor = new Actor("system", "", null, false),
        };
        var w = new Walker(g, settings ?? new FlowSettings(), flow) { ActorFixed = true };
        w.Root(P + start);
        w.Finish();
        return flow;
    }

    [Fact]
    public void A_request_a_mutation_hook_declares_is_sent_only_where_a_call_starts_the_mutation()
    {
        // D316: Sonarr's series screen walked useExecuteCommand's `useApiMutation` (useCommands.ts:45) on load as if it sent
        // POST /api/v5/command; it is sent when a click calls the value the hook hands back.
        Method("Screen()");
        Method("Click()");
        Method("useRun()", "web/useRun.ts");
        Method("RunController.Run()", "App/RunController.cs", 30);
        const string endpoint = "endpoint:App|POST /api/run";
        fragment.Nodes.Add(new Node(endpoint, NodeKind.Endpoint, "POST /api/run", "App", null, 0, 0));
        fragment.Edges.Add(new Edge(endpoint, P + "RunController.Run()", EdgeKind.RoutesTo, Label.Certain));
        Fact("frontend-http", "http-match", "useRun()", 5, ("endpoint", endpoint), ("verb", "POST"), ("url", "{base}/api/run"), ("label", "certain"), ("deferred", "mutation:useMutation"));
        Fact("db", "access", "RunController.Run()", 31, ("entity", "Run"), ("mode", "write"), ("via", "dbset"));
        Call("Screen()", "useRun()", 10);                              // the hook runs on open: it only sets the request up
        Call("Click()", "useRun()", 20, ("fromResult", "1"));          // the click calls what the hook hands back: it sends it
        var opens = Walk("Screen()");
        var declared = Assert.Single(opens.Steps, st => st.End?.Kind == "waits");
        Assert.Equal("declares POST {base}/api/run, sent only when a click (or other code) calls `mutate(…)`, so not followed here", declared.What);
        Assert.DoesNotContain(opens.Steps, st => st.Kind == "http" || st.Point == P + "RunController.Run()");
        var click = Walk("Click()");
        Assert.Contains(click.Steps, st => st.Kind == "http" && st.Point == endpoint);
        Assert.Contains(click.Steps, st => st.Point == P + "RunController.Run()");
        Assert.DoesNotContain(click.Steps, st => st.End?.Kind == "waits");
    }

    [Fact]
    public void Shared_code_entered_where_no_mutation_is_started_gets_a_declared_walk_of_its_own()
    {
        // D316: a mutation hook used in many places is shared code; the screen running it on open continues into a walk that only
        // declares its request (`--declared`), the click starting it into the walk that sends it.
        Method("Screen()", "web/Screen.tsx");
        Method("Click()", "web/Screen.tsx", 20);
        Method("useRun()", "web/useRun.ts");
        Method("RunController.Run()", "App/RunController.cs", 30);
        const string endpoint = "endpoint:App|POST /api/run";
        fragment.Nodes.Add(new Node(endpoint, NodeKind.Endpoint, "POST /api/run", "App", null, 0, 0));
        fragment.Edges.Add(new Edge(endpoint, P + "RunController.Run()", EdgeKind.RoutesTo, Label.Certain));
        fragment.Nodes.Add(new Node("page:/run", NodeKind.Page, "/run", "App", "web/App.tsx", 3, 3));
        fragment.Edges.Add(new Edge("page:/run", P + "Screen()", EdgeKind.RoutesTo, Label.Certain));
        Fact("frontend-http", "http-match", "useRun()", 5, ("endpoint", endpoint), ("verb", "POST"), ("url", "{base}/api/run"), ("label", "certain"), ("deferred", "mutation:useMutation"));
        Fact("frontend-http", "http-match", "Screen()", 11, ("endpoint", "endpoint:App|GET /api/list"), ("verb", "GET"), ("url", "{base}/api/list"), ("label", "certain"));
        Fact("ui", "ui-event", "Screen()", 12, ("handler", P + "Click()"), ("element", "button"), ("event", "click"), ("text", "Run"));
        Call("Screen()", "useRun()", 10);
        Call("Click()", "useRun()", 21, ("fromResult", "1"));
        Fact("frontend-http", "http-match", "Click()", 22, ("endpoint", "endpoint:App|GET /api/list"), ("verb", "GET"), ("url", "{base}/api/list"), ("label", "certain"));   // a click reaching only a hub isn't one (D171)
        var graph = new CodeGraph { Commit = "abc" };
        graph.Fragments["App"] = fragment;
        var set = FlowBuilder.Build(graph, Path.GetTempPath(), new FlowSettings { HubCallers = 2 });
        var shared = set.Flows.Where(f => f.Kind == "shared").ToDictionary(f => f.Id, StringComparer.Ordinal);
        var screen = Assert.Single(set.Flows, f => f.Kind == "screen-opens");
        var click = Assert.Single(set.Flows, f => f.Kind == "click");
        var toDeclared = Assert.Single(screen.Links, l => l.Kind == "continues").ToFlow!;
        var toSent = Assert.Single(click.Links, l => l.Kind == "continues").ToFlow!;
        Assert.EndsWith("--declared", toDeclared, StringComparison.Ordinal);
        Assert.Equal(toDeclared, toSent + "--declared");
        Assert.DoesNotContain(shared[toDeclared].Steps, st => st.Kind == "http");
        Assert.Contains(shared[toDeclared].Steps, st => st.End?.Kind == "waits");
        Assert.Contains(shared[toSent].Steps, st => st.Kind == "http" && st.Point == endpoint);
    }

    [Fact]
    public void An_identity_managers_call_is_a_save_or_a_read_by_its_verb()
    {
        // D291: Kavita login's token writes (TokenService.cs:71–73) were "reads or saves", which the page dropped.
        Method("Login()");
        Package("Login()", 71, "Microsoft.Extensions.Identity.Core", "UserManager.RemoveAuthenticationTokenAsync", "Microsoft.AspNetCore.Identity");
        Package("Login()", 72, "Microsoft.Extensions.Identity.Core", "UserManager.GenerateUserTokenAsync", "Microsoft.AspNetCore.Identity");
        Package("Login()", 73, "Microsoft.Extensions.Identity.Core", "UserManager.SetAuthenticationTokenAsync", "Microsoft.AspNetCore.Identity");
        Package("Login()", 74, "Microsoft.Extensions.Identity.Core", "UserManager.AccessFailedAsync", "Microsoft.AspNetCore.Identity");
        var whats = Walk("Login()").Steps.Select(s => s.What).ToList();
        Assert.Equal([
            "saves the Identity user store through UserManager.RemoveAuthenticationTokenAsync",
            "reads the Identity user store through UserManager.GenerateUserTokenAsync",
            "saves the Identity user store through UserManager.SetAuthenticationTokenAsync",
            "reads or saves the Identity user store through UserManager.AccessFailedAsync",
        ], whats);
        Assert.Null(Walker.IdentityVerb("UserManager.Issue"));          // "Is" must be a whole word
        Assert.Equal("reads", Walker.IdentityVerb("UserManager.IsInRoleAsync"));
    }

    [Fact]
    public void A_signalr_push_is_a_step_of_its_own_and_its_wrapper_is_not_walked()
    {
        // D292: Kavita's reading-list save pushes ReadingListUpdated through IEventHub.SendMessageAsync.
        Method("Save()");
        Method("EventHub.SendMessageAsync(System.String)", "App/EventHub.cs");
        Fact("signalr", "wrapper", "EventHub.SendMessageAsync(System.String)", 9, ("index", "0"));
        Call("Save()", "EventHub.SendMessageAsync(System.String)", 20);
        Fact("signalr", "send-arg", "Save()", 20, ("target", P + "EventHub.SendMessageAsync(System.String)"), ("index", "0"), ("value", "ReadingListUpdated"));
        Call("Save()", "EventHub.SendMessageAsync(System.String)", 22);   // a name made at runtime
        Fact("signalr", "send", "Save()", 24, ("name", "CoverUpdate"));
        var flow = Walk("Save()");
        Assert.Equal(["pushes ReadingListUpdated to the browser", "pushes a message named at runtime to the browser", "pushes CoverUpdate to the browser"],
            flow.Steps.Select(s => s.What).ToList());
        Assert.All(flow.Steps, s => Assert.Equal("push", s.Kind));
        Assert.Contains(flow.Unknowns, u => u.Step == 2 && u.Why.Contains("runtime", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_written_or_deleted_is_a_step_and_an_image_librarys_save_is_one_step_not_two()
    {
        // D294: the cover written through ImageService (`thumbnail.WriteToFile`, ImageService.cs:150) was a plain package call.
        Method("Cover()");
        Fact("conditions", "file-change", "Cover()", 150, ("change", "writes"), ("method", "Image.WriteToFile"));
        Package("Cover()", 150, "NetVips", "Image.WriteToFile", "NetVips");
        Fact("conditions", "file-change", "Cover()", 151, ("change", "deletes"), ("method", "File.Delete"));
        var flow = Walk("Cover()");
        Assert.Equal(["writes a file through Image.WriteToFile", "deletes a file through File.Delete"], flow.Steps.Select(s => s.What).ToList());
        Assert.All(flow.Steps, s => Assert.Equal("file", s.Kind));
    }

    [Fact]
    public void A_known_outside_services_client_is_an_outside_call_and_a_project_can_add_its_own()
    {
        // D302: nopCommerce's Azure Blob calls were package calls.
        Method("Upload()");
        Package("Upload()", 10, "Azure.Storage.Blobs", "BlobContainerClient.CreateIfNotExistsAsync", "Azure.Storage.Blobs");
        Package("Upload()", 11, "Acme.Pay", "PayClient.ChargeAsync", "Acme.Pay");
        Package("Upload()", 12, "Newtonsoft.Json", "JsonConvert.SerializeObject", "Newtonsoft.Json");
        var plain = Walk("Upload()").Steps.Select(s => s.Kind).ToList();
        Assert.Equal(["outside-call", "package", "package"], plain);
        var own = Walk("Upload()", new FlowSettings { OutsideLibraries = ["Acme.Pay"] }).Steps.Select(s => s.Kind).ToList();
        Assert.Equal(["outside-call", "outside-call", "package"], own);
        Assert.True(new FlowSettings().IsOutside("System.Net.Mail.SmtpClient"));
        Assert.False(new FlowSettings().IsOutside("AzureFoo.Bar"));   // a name, not a prefix of letters
    }

    [Fact]
    public void A_thrown_error_a_callers_catch_takes_is_handled_and_a_rethrow_or_another_type_still_stops()
    {
        // D300: Sonarr's indexer parsers throw IndexerException, caught per indexer in HttpIndexerBase.cs.
        Method("Fetch()", "App/Base.cs");
        Method("Parse()", "App/Parser.cs");
        Method("Guard()", "App/Guard.cs");
        Fact("conditions", "try", "Fetch()", 10, ("from", "11"), ("to", "14"),
            ("catches", """[{"type":"App.IndexerException","line":15},{"type":"App.ApiKeyException","line":17,"rethrows":true}]"""));
        Call("Fetch()", "Parse()", 12);
        Call("Fetch()", "Guard()", 30);   // outside the try
        Fact("conditions", "exit", "Parse()", 5, ("kind", "throw"), ("text", "throw new UnsupportedFeed()"), ("exception", "UnsupportedFeed"),
            ("exceptionTypes", "App.UnsupportedFeed|App.IndexerException|System.Exception|System.Object"), ("conditions", "[]"));
        Fact("conditions", "exit", "Parse()", 6, ("kind", "throw"), ("text", "throw new ApiKeyException()"), ("exception", "ApiKeyException"),
            ("exceptionTypes", "App.ApiKeyException|System.Exception|System.Object"), ("conditions", "[]"));
        Fact("conditions", "exit", "Parse()", 7, ("kind", "throw"), ("text", "throw new ArgumentException()"), ("exception", "ArgumentException"),
            ("exceptionTypes", "System.ArgumentException|System.Exception|System.Object"), ("conditions", "[]"));
        Fact("conditions", "exit", "Guard()", 3, ("kind", "throw"), ("text", "throw new IndexerException()"), ("exception", "IndexerException"),
            ("exceptionTypes", "App.IndexerException|System.Exception|System.Object"), ("conditions", "[]"));
        var flow = Walk("Fetch()");
        var handled = Assert.Single(flow.Handled);
        Assert.Equal("throws UnsupportedFeed", handled.What);
        Assert.Equal("App/Svc.cs:15", handled.CaughtBy!.ToString());
        Assert.Equal(1, handled.Step);
        // The rethrown one, the untaken type and the one outside any try stop.
        Assert.Equal(["throws ApiKeyException", "throws ArgumentException", "throws IndexerException"], flow.Exits.Select(e => e.What).Order(StringComparer.Ordinal).ToList());
        // A bare catch takes everything; one whose type can't be worked out leaves a stop marked ❓.
        fragment.Facts.Add(new Fact("conditions", "try", P + "Fetch()", "App/Base.cs", 29, new Dictionary<string, string>
            { ["from"] = "29", ["to"] = "31", ["catches"] = """[{"type":"?","line":32},{"type":"*","line":34}]""" }));
        var again = Walk("Fetch()");
        Assert.Contains(again.Handled, h => h.What == "throws IndexerException" && h.CaughtBy!.Line == 34);
        Assert.DoesNotContain(again.Exits, e => e.What == "throws IndexerException");
    }

    [Fact]
    public void A_catch_whose_type_cant_be_told_leaves_the_error_a_stop_marked_unsure()
    {
        Method("Run()");
        Fact("conditions", "try", "Run()", 1, ("from", "2"), ("to", "4"), ("catches", """[{"type":"?","line":5}]"""));
        Fact("conditions", "exit", "Run()", 3, ("kind", "throw"), ("text", "throw new X()"), ("exception", "X"), ("exceptionTypes", "App.X|System.Exception"), ("conditions", "[]"));
        var exit = Assert.Single(Walk("Run()").Exits);
        Assert.Contains("can't tell", exit.Unsure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_browser_timer_that_calls_out_is_a_runs_later_link_and_one_that_only_tweaks_the_screen_is_not()
    {
        // D295: Kavita's login starts a setInterval that refreshes the token.
        Method("Login()");
        Method("Refresh()");
        Method("Focus()");
        Call("Login()", "Refresh()", 507, ("deferred", "timer:setInterval"));
        Call("Login()", "Focus()", 510, ("deferred", "timer:setTimeout"));
        Package("Refresh()", 3, "Azure.Storage.Blobs", "BlobClient.UploadAsync", "Azure.Storage.Blobs");
        var link = Assert.Single(Walk("Login()").Links, l => l.Kind == "runs-later");
        Assert.Equal("Refresh", link.To);
        Assert.StartsWith("Runs Refresh later, on a timer (setInterval)", link.Sentence, StringComparison.Ordinal);
    }

    // ---- the C# facts they need --------------------------------------------------------------------------------------

    static List<Fact> Facts(string code)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code, path: "/r/P/Svc.cs", cancellationToken: TestContext.Current.CancellationToken);
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p));
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("P", [tree], refs);
        var fragment = new ProjectFragment { ProjectId = "P", Fingerprint = "" };
        var ctx = new ExtractContext("/r", "P", "/r/P", compilation, [new(tree, compilation.GetSemanticModel(tree), "P/Svc.cs")], fragment, new Dictionary<string, List<string>>());
        new Stangent.Graph.Detectors.ConditionsDetector().Extract(ctx);
        return fragment.Facts;
    }

    [Fact]
    public void Try_blocks_their_catches_thrown_types_and_file_writes_are_read_from_csharp()
    {
        const string code = """
            using System;
            using System.IO;
            public class IndexerException : Exception { }
            public class Feed : IndexerException { }
            public class Svc
            {
                void Run(Stream s, string path)
                {
                    try
                    {
                        throw new Feed();
                    }
                    catch (IndexerException e) { throw; }
                    catch (ArgumentException e) when (e.Message != "") { }
                    catch { }
                    File.WriteAllText(path, "x");
                    Directory.Delete(path);
                    using var f = new FileStream(path, FileMode.Create);
                    s.CopyTo(f);
                    s.CopyTo(new MemoryStream());
                }
            }
            """;
        var facts = Facts(code);
        var t = Assert.Single(facts, f => f.Kind == "try");
        Assert.Equal(("10", "12"), (t["from"], t["to"]));
        Assert.Equal("""[{"type":"IndexerException","line":13,"rethrows":true},{"type":"System.ArgumentException","line":14,"filtered":true},{"type":"*","line":15}]""", t["catches"]);
        var thrown = Assert.Single(facts, f => f.Kind == "exit" && f["exception"] == "Feed");
        Assert.Equal("Feed|IndexerException|System.Exception|System.Object", thrown["exceptionTypes"]);
        Assert.Equal(["16 writes File.WriteAllText", "17 deletes Directory.Delete", "19 writes Stream.CopyTo"],
            facts.Where(f => f.Kind == "file-change").Select(f => $"{f.Line} {f["change"]} {f["method"]}").ToList());
    }

    [Fact]
    public void A_filter_that_sets_any_result_on_its_context_is_a_stop_with_its_status()
    {
        // D301: Kavita's DisallowRoleAttribute refuses with a ContentResult whose StatusCode is 400.
        const string code = """
            namespace Microsoft.AspNetCore.Mvc { public class ContentResult { public int? StatusCode { get; set; } public string Content { get; set; } } }
            namespace Microsoft.AspNetCore.Mvc.Filters { public class AuthorizationFilterContext { public object Result { get; set; } } }
            public static class StatusCodes { public const int Status400BadRequest = 400; }
            public class DisallowRole
            {
                public void OnAuthorization(Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext context, bool readOnly)
                {
                    if (readOnly)
                        context.Result = new Microsoft.AspNetCore.Mvc.ContentResult { StatusCode = StatusCodes.Status400BadRequest, Content = "denied" };
                }
            }
            """;
        var exit = Assert.Single(Facts(code), f => f.Kind == "exit");
        Assert.Equal(("StatusCode", "400", "ContentResult"), (exit["kind"], exit["status"], exit["result"]));
        Assert.Contains("readOnly", exit["conditions"], StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_libraries_in_the_config_must_be_namespaces()
    {
        var root = Directory.CreateTempSubdirectory("stangent-cfg-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, "stangent.yaml"), "version: 1\nflows:\n  outsideLibraries: [Acme.Billing]\n");
            Assert.Equal(["Acme.Billing"], StangentRepo.Discover(root).Config.Flows.OutsideLibraries);
            File.WriteAllText(Path.Combine(root, "stangent.yaml"), "version: 1\nflows:\n  outsideLibraries: [\"Acme Billing*\"]\n");
            var e = Assert.Throws<StangentException>(() => StangentRepo.Discover(root));
            Assert.Equal("invalid_config", e.Code);
        }
        finally { Directory.Delete(root, true); }
    }

    // ---- D304: fixes from fresh check 2 ------------------------------------------------------------------------------

    [Fact]
    public void A_catch_that_throws_a_new_error_lets_it_go_on_as_that_error()
    {
        // D304(1): Sonarr's RecycleBinProvider.cs:125 catches IOException and throws RecycleBinException: never "carries on".
        Method("Job()", "App/Job.cs");
        Method("Delete()", "App/Recycle.cs");
        Method("Move()", "App/Disk.cs");
        Fact("conditions", "try", "Job()", 1, ("from", "2"), ("to", "4"), ("catches", """[{"type":"App.RecycleBinException","line":5}]"""));
        Call("Job()", "Delete()", 3);
        Fact("conditions", "try", "Delete()", 10, ("from", "11"), ("to", "13"),
            ("catches", """[{"type":"System.IO.IOException","line":14,"rethrows":true,"becomes":"App.RecycleBinException|System.Exception|System.Object"}]"""));
        Call("Delete()", "Move()", 12);
        Fact("conditions", "exit", "Move()", 3, ("kind", "throw"), ("text", "throw new IOException()"), ("exception", "IOException"),
            ("exceptionTypes", "System.IO.IOException|System.Exception|System.Object"), ("conditions", "[]"));
        var flow = Walk("Job()");
        var handled = Assert.Single(flow.Handled);
        Assert.Equal("throws IOException, turned into RecycleBinException at App/Svc.cs:14", handled.What);
        Assert.Equal(5, handled.CaughtBy!.Line);
        // With no catch of the new error above it, the original error is a stop that says what it became.
        fragment.Facts.RemoveAll(f => f.Kind == "try" && f.Owner == P + "Job()");
        var exit = Assert.Single(Walk("Job()").Exits);
        Assert.Equal("throws IOException, turned into RecycleBinException at App/Svc.cs:14", exit.What);
    }

    [Fact]
    public void A_typed_catch_facing_an_error_of_unknown_type_leaves_a_stop_marked_unsure()
    {
        // D304(10): a bare `throw;` (or any error whose type isn't known) under a typed catch: can't tell.
        Method("Run()");
        Fact("conditions", "try", "Run()", 1, ("from", "2"), ("to", "4"), ("catches", """[{"type":"App.X","line":5}]"""));
        Fact("conditions", "exit", "Run()", 3, ("kind", "throw"), ("text", "throw;"), ("exception", ""), ("exceptionTypes", ""), ("conditions", "[]"));
        var exit = Assert.Single(Walk("Run()").Exits);
        Assert.Contains("the error's type isn't known", exit.Unsure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sign_in_check_may_save_since_lockout_writes_the_user_store()
    {
        // D304(3): Kavita's AccountController.cs:264 calls CheckPasswordSignInAsync with lockoutOnFailure: true.
        Assert.Null(Walker.IdentityVerb("SignInManager.CheckPasswordSignInAsync"));
        Assert.Equal("reads", Walker.IdentityVerb("UserManager.CheckPasswordAsync"));
    }

    [Fact]
    public void A_filters_stop_names_the_endpoints_attribute_and_a_redirect_or_challenge_says_where_the_user_goes()
    {
        // D304(5): Kavita's [DisallowRole(PolicyConstants.ReadOnlyRole)]; D304(8): nopCommerce's https/www redirects, sign-in challenge.
        var at = new At("App/F.cs", 36, "");
        Assert.Equal("answers 400 for DisallowRole(roles = [\"Read Only\"])",
            Walker.FilterStop(new Exit("StatusCode", "answers 400", at, [], null, "p"), "DisallowRole(roles = [\"Read Only\"])"));
        Assert.Equal("sends the user to another address (RedirectResult)", Walker.FilterStop(new Exit("Redirect", "sends the user elsewhere (RedirectResult)", at, [], null, "p"), null));
        Assert.Equal("sends the user to sign in (Challenge)", Walker.FilterStop(new Exit("Challenge", "answers Challenge", at, [], null, "p"), null));
        Assert.Null(Walker.FilterStop(new Exit("throw", "throws X", at, [], null, "p"), "A(b)"));
        Assert.Null(Walker.FilterStop(new Exit("StatusCode", "answers 400", at, [], null, "p"), null));
    }

    [Fact]
    public void A_client_librarys_local_builder_is_not_an_outside_call()
    {
        // D304(9): nopCommerce's AzureThumbService.cs:76 GetBlobContainerClient only makes an object.
        Method("Upload()");
        Package("Upload()", 10, "Azure.Storage.Blobs", "BlobServiceClient.GetBlobContainerClient", "Azure.Storage.Blobs");
        Package("Upload()", 11, "Azure.Storage.Blobs", "BlobContainerClient.CreateIfNotExistsAsync", "Azure.Storage.Blobs");
        Assert.Equal(["package", "outside-call"], Walk("Upload()").Steps.Select(s => s.Kind).ToList());
        Assert.True(Walker.LocalBuilder("TableServiceClient.CreateTableClient"));
        Assert.False(Walker.LocalBuilder("SmtpClient.ConnectAsync"));
    }

    [Fact]
    public void A_catch_that_rewraps_a_file_write_with_its_condition_and_what_a_catch_returns_are_read_from_csharp()
    {
        const string code = """
            using System;
            using System.IO;
            public class WrapException : Exception { public WrapException(string m, Exception e) : base(m, e) { } }
            public class Svc
            {
                string Run(string path)
                {
                    try { File.Delete(path); }
                    catch (IOException e) { throw new WrapException("x", e); }
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);
                    try { File.Delete(path); }
                    catch (Exception) { return Fail("x"); }
                    try { File.Delete(path); }
                    catch (Exception) { Console.WriteLine(); }
                    return View(path);
                }
                string Fail(string m) => m;
                string View(string m) => m;
            }
            """;
        var facts = Facts(code);
        var tries = facts.Where(f => f.Kind == "try").OrderBy(f => f.Line).Select(f => f["catches"]).ToList();
        Assert.Equal([
            """[{"type":"System.IO.IOException","line":9,"rethrows":true,"becomes":"WrapException|System.Exception|System.Object"}]""",
            """[{"type":"System.Exception","line":13,"ends":"Fail(\u2026)"}]""",
            """[{"type":"System.Exception","line":15,"ends":"View(\u2026)"}]""",
        ], tries);
        // D304(2): the .NET directory create keeps its condition.
        var cond = Assert.Single(facts, f => f.Kind == "condition" && f.Line == 11);
        Assert.Equal("Directory.CreateDirectory", cond["call"]);
        Assert.Contains("Directory.Exists", cond["conditions"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_handled_error_carries_what_its_catch_returns()
    {
        // D304(7): nopCommerce's place-order catch falls out to `return View(model)`: the flow file says so.
        Method("Place()");
        Fact("conditions", "try", "Place()", 1, ("from", "2"), ("to", "4"), ("catches", """[{"type":"System.Exception","line":5,"ends":"View(…)"}]"""));
        Fact("conditions", "exit", "Place()", 3, ("kind", "throw"), ("text", "throw new X()"), ("exception", "X"), ("exceptionTypes", "App.X|System.Exception"), ("conditions", "[]"));
        Assert.Equal("View(…)", Assert.Single(Walk("Place()").Handled).CatchEnds);
    }

    [Fact]
    public void An_interface_call_into_a_plugin_lists_the_plugins_outside_services_as_guessed_outside_calls()
    {
        // D305(1): nopCommerce's tax service calls ITaxProvider; the Avalara plugin's code calls Avalara's client through a manager.
        const string Plugin = "project:src/Plugins/Tax.Avalara/Tax.Avalara.csproj";
        const string Q = "Tax.Avalara|M:Tax.Avalara.";
        Method("GetTax()");
        Method("ITaxProvider.Rate()", "App/ITaxProvider.cs");
        fragment.Nodes.Add(new Node(Q + "AvalaraTaxProvider.Rate()", NodeKind.Method, "AvalaraTaxProvider.Rate", Plugin, "src/Plugins/Tax.Avalara/AvalaraTaxProvider.cs", 10, 20));
        fragment.Nodes.Add(new Node(Q + "AvalaraManager.Create()", NodeKind.Method, "AvalaraManager.Create", Plugin, "src/Plugins/Tax.Avalara/AvalaraManager.cs", 30, 60));
        fragment.Edges.Add(new Edge(P + "ITaxProvider.Rate()", Q + "AvalaraTaxProvider.Rate()", EdgeKind.DispatchesTo, Label.Certain));
        Call("GetTax()", "ITaxProvider.Rate()", 5);
        fragment.Edges.Add(new Edge(Q + "AvalaraTaxProvider.Rate()", Q + "AvalaraManager.Create()", EdgeKind.Calls, Label.Certain));
        fragment.Facts.Add(new Fact("call-sites", "call-site", Q + "AvalaraTaxProvider.Rate()", "src/Plugins/Tax.Avalara/AvalaraTaxProvider.cs", 12,
            new Dictionary<string, string> { ["target"] = Q + "AvalaraManager.Create()", ["label"] = "0", ["kind"] = "calls" }));
        foreach (var (line, method) in new[] { (40, "AvaTaxClient.CreateTransaction"), (41, "AvaTaxClient.Ping"), (42, "AvaTaxClient.GetAvaTaxClient") })
            fragment.Facts.Add(new Fact("call-sites", "call-site", Q + "AvalaraManager.Create()", "src/Plugins/Tax.Avalara/AvalaraManager.cs", line,
                new Dictionary<string, string> { ["target"] = "", ["label"] = "0", ["kind"] = "package", ["package"] = "Avalara.AvaTax.RestClient", ["method"] = method, ["ns"] = "Avalara.AvaTax.RestClient" }));
        var flow = Walk("GetTax()");
        var may = Assert.Single(flow.Steps, s => s.What.StartsWith("may call ", StringComparison.Ordinal));
        Assert.Equal("may call Avalara.AvaTax.RestClient outside the app through the plugin Tax.Avalara (AvalaraTaxProvider): AvaTaxClient.CreateTransaction, AvaTaxClient.Ping", may.What);
        Assert.Equal(("outside-call", true, "App/Svc.cs", 5), (may.Kind, may.Guessed, may.At.File, may.At.Line));
        Assert.True(PageChart.PluginCall(may.What));
        Assert.True(FlowGraph.IsPluginProject(Plugin));
        Assert.True(FlowGraph.IsPluginProject("project:addons/Shop.Pay/Shop.Pay.csproj"));
        Assert.False(FlowGraph.IsPluginProject("project:src/Libraries/Nop.Services/Nop.Services.csproj"));
        Assert.False(FlowGraph.IsPluginProject("project:Plugins.csproj"));   // a folder, not the project's own name
    }

    [Fact]
    public void Code_kept_for_a_later_call_is_one_visible_stop_line_not_walked()
    {
        // D306: Kavita's selection click (BulkSelectionService.getActions) wires each bulk action's callback; the saves, downloads and
        // emails run only when a menu item is clicked later. The kept calls are not walked; one step per keeper says so.
        Method("Select()");
        Method("Actions.Wire()", "App/Actions.cs");
        Method("Bulk.MarkRead()", "App/Bulk.cs");
        Method("Bulk.Download()", "App/Bulk.cs");
        Fact("conditions", "condition", "Select()", 12, ("target", ""), ("kind", "if"), ("code", "hasSeries"), ("at", "App/Svc.cs:11"));
        Call("Select()", "Actions.Wire()", 10);
        Call("Select()", "Bulk.MarkRead()", 12, ("deferred", "kept:wireBulkCallback"));
        Call("Select()", "Bulk.Download()", 13, ("deferred", "kept:wireBulkCallback"));
        Fact("call-sites", "call-site", "Bulk.MarkRead()", 3, ("target", ""), ("label", "0"), ("kind", "package"), ("package", "Db"), ("method", "Db.Save"), ("ns", "Db"));
        var flow = Walk("Select()");
        Assert.DoesNotContain(flow.Steps, s => s.Point == P + "Bulk.MarkRead()" || s.Point == P + "Bulk.Download()");
        var wait = Assert.Single(flow.Steps, s => s.End?.Kind == "waits");
        Assert.Equal("leaves code for later (wireBulkCallback): it runs only when something calls it afterwards, such as a later click, so it isn't walked here", wait.What);
        Assert.Equal(12, wait.At.Line);
        Assert.Equal("kept by wireBulkCallback, run by a later call", wait.End!.Detail);
    }
}

using System.Text.Json;
using Stangent.Graph;

namespace Stangent.Core.Tests;

/// <summary>Layer 2 additions for flows (r3 step 1): jobs, queues, conditions and early exits, on-load, the ✅ URL rule.</summary>
public class FlowStartsTests(ShopGraph shop) : IClassFixture<ShopGraph>
{
    const string Core = "Shop.Core|M:Shop.Core.";
    const string Angular = "ts|Shop.Web/ClientApp/src/app/";

    List<Fact> Facts(string detector, string kind) => [.. shop.Graph.AllFacts.Where(f => f.Detector == detector && f.Kind == kind)];

    static List<string> Conditions(Fact f) =>
        [.. JsonDocument.Parse(f["conditions"]).RootElement.EnumerateArray()
            .Select(c => $"{c.GetProperty("kind").GetString()} {c.GetProperty("text").GetString()}{(c.TryGetProperty("case", out var k) ? " = " + k.GetString() : "")}")];

    [Fact]
    public void A_job_interface_marks_the_method_it_runs_a_system_job_and_a_recurring_registration_does_too()
    {
        var jobs = Facts("jobs", "is-job").Select(f => $"{f.Owner} {f["kind"]} {f["command"]}").Order(StringComparer.Ordinal).ToList();
        Assert.Contains($"{Core}RefreshStockService.Execute(Shop.Core.RefreshStockCommand) command Shop.Core|T:Shop.Core.RefreshStockCommand", jobs);
        Assert.Contains($"{Core}CleanupTask.ExecuteAsync scheduled ", jobs);
        Assert.Contains(Facts("jobs", "is-job"), f => f["kind"] == "recurring" && f["id"] == "recount" && f["schedule"] == "0 3 * * *");
    }

    [Fact]
    public void A_command_made_and_handed_to_a_queue_draws_a_queues_line_to_its_handler_and_one_made_at_runtime_is_unknown()
    {
        var execute = $"{Core}RefreshStockService.Execute(Shop.Core.RefreshStockCommand)";
        var queues = shop.Graph.AllEdges.Where(e => e.Kind == EdgeKind.Queues).ToList();
        Assert.Contains(queues, e => e.Source == $"{Core}ShelfAddedHandler.Added(System.Int32,System.Boolean)" && e.Target == execute && e.Label == Label.Guessed);
        // Passing an existing command along (a parameter, a type parameter's value) isn't queuing it here.
        Assert.DoesNotContain(queues, e => e.Source == execute || e.Source.Contains("CommandQueue.PushMany", StringComparison.Ordinal));
        Assert.Equal(2, Facts("jobs", "queue").Count(f => f.Owner.Contains("ShelfAddedHandler.Added", StringComparison.Ordinal)));
        Assert.Contains(shop.Graph.AllBlindSpots, b => b.Detector == "jobs" && b.Owner.Contains("ShelfAddedHandler.FromApi", StringComparison.Ordinal)
            && b.Reason.StartsWith("queues work through CommandQueue.Push whose type is known only at runtime", StringComparison.Ordinal));
        // A background job's lambda → its method, ✅.
        Assert.Contains(queues, e => e.Source.EndsWith("ShelfNotifier.Later", StringComparison.Ordinal) && e.Target.EndsWith("Recount", StringComparison.Ordinal) && e.Label == Label.Certain);
        // Through an app's own wrapper that hands its expression parameter to Hangfire, with a job fact saying how.
        Assert.Contains(queues, e => e.Source.EndsWith("ShelfSweeper.Later", StringComparison.Ordinal) && e.Target.EndsWith("ShelfSweeper.Sweep", StringComparison.Ordinal) && e.Label == Label.Certain);
        Assert.Contains(Facts("background-jobs", "job"), f => f.Owner.EndsWith("ShelfSweeper.Later", StringComparison.Ordinal) && f["via"] == "BackgroundJob.Enqueue through ShelfJobs.Enqueue");
        Assert.DoesNotContain(shop.Graph.AllBlindSpots, b => b.Detector == "background-jobs" && b.Owner.Contains("ShelfJobs", StringComparison.Ordinal));
        // Y2: a job queuing itself again keeps its line; a schedule that isn't text keeps its code.
        Assert.Contains(queues, e => e.Source.EndsWith("ShelfSweeper.Retry", StringComparison.Ordinal) && e.Target == e.Source);
        Assert.Contains(Facts("jobs", "is-job"), f => f["id"] == "sweep" && f["schedule"] == "" && f["scheduleCode"] == "Hangfire.Cron.Daily()");
    }

    [Fact]
    public void Each_call_carries_its_conditions_innermost_first_quoted_exactly()
    {
        var recounts = Facts("conditions", "condition").Where(f => f["call"] == "RefreshStockService.Recount").Select(Conditions).ToList();
        Assert.Equal(
        [
            ["case id = 0", "loop foreach (var id in new[] { command.ShelfId })"],
            ["case id = default", "loop foreach (var id in new[] { command.ShelfId })"],
            ["catch (InvalidOperationException e) when (e.Message.Length > 0)", "case id = default", "loop foreach (var id in new[] { command.ShelfId })"],
        ], recounts);
        var pushes = Facts("conditions", "condition").Where(f => f.Owner.Contains("ShelfAddedHandler.Added", StringComparison.Ordinal) && f["call"].StartsWith("CommandQueue.", StringComparison.Ordinal)).ToList();
        Assert.Equal(["if !quiet"], Conditions(pushes.Single(f => f["call"] == "CommandQueue.Push")));
        Assert.Equal(["else !quiet"], Conditions(pushes.Single(f => f["call"] == "CommandQueue.PushMany")));
    }

    [Fact]
    public void Early_exits_are_the_frameworks_own_refusals_and_redirects_and_every_throw_with_their_conditions()
    {
        var exits = Facts("conditions", "exit").Where(f => f.File is "Shop.Web/Guards.cs" or "Shop.Web/Controllers/AccountController.cs")
            .Select(f => $"{f["kind"]} {f["status"]} {f["text"]} | {string.Join(" < ", Conditions(f))}").ToList();
        // D187 (ruling R5): a filter setting context.Result to a listed result stops the request: an exit.
        Assert.Contains(exits, e => e.StartsWith("Redirect  context.Result = new RedirectToActionResult", StringComparison.Ordinal));
        exits = [.. exits.Where(e => !e.Contains("context.Result", StringComparison.Ordinal))];
        Assert.Equal(
        [
            "Redirect  RedirectToAction(nameof(Welcome)) | unless !ModelState.IsValid",
            "NotFound  Results.NotFound() | if id is null",
            "StatusCode 503 Results.StatusCode(503) | if closed < unless id is null",
            "BadRequest  Results.BadRequest() | if id > 100 < unless closed < unless id is null",
            "throw  throw new ArgumentNullException(nameof(name)) | null name",
            "NotFound  Results.NotFound() | if id is null < case mode = \"one\"",
            "BadRequest  Results.BadRequest() | unless id is null < case mode = \"one\"",
        ], exits);
        // `return View(model)` and `Results.Ok(id)` aren't exits.
        Assert.DoesNotContain(Facts("conditions", "exit"), f => f["text"].StartsWith("View(", StringComparison.Ordinal) || f["text"].Contains("Ok(", StringComparison.Ordinal));
    }

    [Fact]
    public void An_if_that_only_reports_a_problem_and_leaves_rejects_the_input_with_its_message()
    {
        const string code = """
            using System;
            using System.Collections.Generic;
            class Result { public void AddError(string e) { } }
            class Svc
            {
                void Save() { }
                object Json(object o) => o;
                Result Register(string email, string name, int qty, List<string> warnings)
                {
                    var result = new Result();
                    if (string.IsNullOrEmpty(email)) { result.AddError("Email is not provided"); return result; }
                    if (qty <= 0) { warnings.Add("Quantity should be positive"); warnings.Add("again"); return result; }
                    if (name == "x") throw new InvalidOperationException("Name taken");
                    if (name == null) throw new ArgumentNullException(nameof(name));
                    if (qty > 9) return result;
                    if (qty > 8) { Save(); return result; }
                    if (qty > 7) { result.AddError(email); return result; }
                    if (qty > 6) { var min = qty * 2; throw new Exception(string.Format("At least {0}", min)); }
                    Save();
                    return result;
                }
                object Add(int id)
                {
                    if (id < 0) return Json(new { success = false, message = "No such product" });
                    if (id == 0) return Json(new { redirect = "/" });
                    if (id > 5) { warnings(); }
                    return Json(new { success = true });
                    void warnings() { }
                }
                void ValidateId(int id)
                {
                    if (id <= 0) { throw new Exception(id + " is not a valid ID"); }
                }
            }
            """;
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code, path: "/r/P/Svc.cs", cancellationToken: TestContext.Current.CancellationToken);
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p));
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("P", [tree], refs);
        var fragment = new ProjectFragment { ProjectId = "P", Fingerprint = "" };
        var ctx = new Stangent.Graph.Extraction.ExtractContext("/r", "P", "/r/P", compilation, [new(tree, compilation.GetSemanticModel(tree), "P/Svc.cs")], fragment, new Dictionary<string, List<string>>());
        new Stangent.Graph.Detectors.ConditionsDetector().Extract(ctx);
        var rejects = fragment.Facts.Where(f => f.Kind == "rejects").Select(f => $"{f.Line} {f["condition"]} → \"{f["message"]}\"").ToList();
        Assert.Equal(
        [
            "11 string.IsNullOrEmpty(email) → \"Email is not provided\"",
            "12 qty <= 0 → \"Quantity should be positive\"",
            "13 name == \"x\" → \"Name taken\"",
            // 14: the caller's bug, not the input; 15: only leaves; 16: does other work first
            "17 qty > 7 → \"\"",                       // reports what it was given: no message of its own
            "18 qty > 6 → \"At least {0}\"",            // the message made in a local first
            "24 id < 0 → \"No such product\"",          // an answer saying it failed
            // 25: a redirect; 26: doesn't leave
            "32 id <= 0 → \" is not a valid ID\"",      // nothing after it: still a check
        ], rejects);
    }

    [Fact]
    public void An_MVC_action_serving_GET_that_returns_a_view_runs_as_its_page_opens()
    {
        var loads = Facts("on-load", "on-load").Where(f => f["framework"] == "mvc").Select(f => $"{f.Owner[(f.Owner.IndexOf("AccountController.", StringComparison.Ordinal) + 18)..]} {f["view"]} {f["verbs"]}").ToList();
        Assert.Contains("Register Register GET", loads); // the default view: the action's name
        Assert.Contains("Welcome Done any", loads);
        Assert.Contains("Settings Settings GET", loads);
        // Y1: a view chosen at runtime still opens a page; viewFrom says so.
        Assert.Contains(Facts("on-load", "on-load"), f => f.Owner.Contains("AccountController.Themed", StringComparison.Ordinal) && f["viewFrom"] == "runtime" && f["view"] == "");
        Assert.DoesNotContain(loads, l => l.StartsWith("Register(Shop.Web.Models.RegisterModel)", StringComparison.Ordinal) || l.StartsWith("Close", StringComparison.Ordinal));
    }

    [Fact]
    public void A_constant_url_that_one_endpoint_fits_is_proved_and_every_guess_says_which_test_failed()
    {
        var calls = shop.Graph.AllEdges.Where(e => e.Kind == EdgeKind.CallsHttp && e.Detector == "frontend-http").ToList();
        Assert.Contains(calls, e => e.Source == Angular + "order.service.ts#OrderService.place" && e.Target == "endpoint:Shop.Web|POST /api/orders" && e.Label == Label.Certain);
        var matches = Facts("frontend-http", "http-match").Concat(Facts("razor-views", "http-match")).ToList();
        Assert.All(matches, m => Assert.Equal(m["label"] == "guessed", m["reason"].Length > 0));
        Assert.Contains(matches, m => m["url"] == "/api/stock/{*}" && m["reason"].StartsWith("2 endpoints fit (GET /api/stock/summary and GET /api/stock/{id:int})", StringComparison.Ordinal));
        Assert.Contains(matches, m => m["reason"].StartsWith("matched by nswag's naming", StringComparison.Ordinal));
        Assert.Contains(matches, m => m.Detector == "razor-views" && m["reason"] == "controller or action taken from the view's own folder and name");
        // An edge is ✅ only when a call behind it is.
        Assert.All(calls.Where(e => e.Label == Label.Certain), e => Assert.Contains(matches, m => m.Owner == e.Source && m["endpoint"] == e.Target && m["label"] == "certain"));
    }
}

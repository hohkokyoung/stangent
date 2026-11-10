using Stangent.Flows;

namespace Stangent.Core.Tests;

/// <summary>Layer 3 links built after the walk, on hand-made flows (D205–).</summary>
public class FlowLinksTests
{
    static Flow NewFlow(string id, string kind, params Step[] steps)
    {
        var f = new Flow
        {
            Id = id, Name = id, Kind = kind, Start = new StartInfo("start", new At("a.cs", 1, ""), id, []),
            Actor = new Actor("system", "test", null, false),
        };
        f.Steps.AddRange(steps);
        return f;
    }

    static Step Db(int n, int? from, string what, string table, string code = "", int line = 0) =>
        new() { N = n, From = from, What = $"{what} {table}", At = new At("s.cs", line == 0 ? n * 10 : line, code), Kind = "database", End = new End("database", table) };

    static Step Call(int n, int? from, string code, int line) => new() { N = n, From = from, What = "calls x", At = new At("c.cs", line, code), Kind = "call" };

    [Fact]
    public void A_save_to_a_queue_table_a_job_works_through_links_runs_later_on_the_call_that_made_it()
    {
        // The send job reads its queue before it saves anything: that's its work list.
        var send = NewFlow("job-send", "job", Db(1, null, "reads", "Store"), Db(2, null, "reads", "QueuedEmail"), Db(3, null, "saves", "QueuedEmail"));
        // The cancel job reads the queue only after saving an order: looking up what it changes, not its work.
        var cancel = NewFlow("job-cancel", "job", Db(1, null, "saves", "Order"), Db(2, null, "reads", "QueuedEmail"));
        // Shared code that queues an email, reached through a hub.
        var notify = NewFlow("shared-notify", "shared", Call(1, null, "_queued.Insert(email);", 70), Db(2, 1, "saves", "QueuedEmail"));
        var register = NewFlow("click-register", "click",
            Call(1, null, "_workflow.SendWelcome(customer);", 1315), Call(2, 1, "return SendNotification(...);", 768),
            Db(3, null, "saves", "QueuedEmail", "_queued.DeleteQueuedEmails(emails);", 2119));
        register.Links.Add(new Link("continues", "SendNotification", "shared-notify", register.Steps[1].At, "", 2, false));
        var flows = new List<Flow> { send, cancel, notify, register };

        FlowBuilder.QueuedWork(flows);

        // On the call that entered the method continuing into the shared code that saves, not on the hub stop.
        var link = Assert.Single(register.Links, l => l.Kind == "runs-later");
        Assert.Equal(("job-send", 1315, 1, true), (link.ToFlow, link.At.Line, link.Step, link.Guessed));
        Assert.Contains(register.Guesses, g => g.Step == 1 && g.Reason.Contains("queue", StringComparison.Ordinal));
        // In the shared flow itself: on the call into the method making the save.
        Assert.Equal([70], notify.Links.Where(l => l.Kind == "runs-later").Select(l => l.At.Line));
        // The job's own saves (marking work done) and a removal leave no work; the cancel job isn't a reader of the queue.
        Assert.DoesNotContain(send.Links, l => l.Kind == "runs-later");
        Assert.DoesNotContain(flows.SelectMany(f => f.Links), l => l.ToFlow == "job-cancel");
    }

    [Theory]
    [InlineData("QueuedEmail", true)]
    [InlineData("PendingReleases", true)]
    [InlineData("outbox_messages", true)]
    [InlineData("JOB_QUEUE", true)]
    [InlineData("Queue", true)]
    [InlineData("Customer", false)]
    [InlineData("QueuesConfigured", true)]
    [InlineData("Unqueued", false)]
    [InlineData("PendingOrderNote", true)]
    public void A_queue_table_is_named_as_one(string table, bool queue) => Assert.Equal(queue, FlowBuilder.IsQueueTable(table));
}

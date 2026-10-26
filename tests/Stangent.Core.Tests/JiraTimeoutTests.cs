using Stangent.Tickets.Jira;

namespace Stangent.Core.Tests;

/// <summary>Jira reads that never answer are tried again under a short limit; writes are tried once.</summary>
public class JiraTimeoutTests
{
    static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    /// <summary>A call that never answers, whatever the token says (as a stalled server reply looks).</summary>
    static Task<string> Hang(CancellationToken _) => new TaskCompletionSource<string>().Task;

    [Fact]
    public async Task A_read_that_stalls_once_is_answered_on_the_next_try()
    {
        var calls = 0;
        var retried = new List<int>();
        var answer = await TimedTries.RunAsync(ct => ++calls == 1 ? Hang(ct) : Task.FromResult("SONB-7"), Short, 3, retried.Add, CancellationToken.None);
        Assert.Equal("SONB-7", answer);
        Assert.Equal(2, calls);
        Assert.Equal([2], retried);
    }

    [Fact]
    public async Task A_read_that_never_answers_times_out_after_the_last_try()
    {
        var calls = 0;
        await Assert.ThrowsAsync<TimeoutException>(() => TimedTries.RunAsync(ct => { calls++; return Hang(ct); }, Short, 3, null, CancellationToken.None));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task One_try_means_no_retry()
    {
        var calls = 0;
        await Assert.ThrowsAsync<TimeoutException>(() => TimedTries.RunAsync(ct => { calls++; return Hang(ct); }, Short, 1, null, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_a_timeout()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TimedTries.RunAsync(Hang, TimeSpan.FromSeconds(30), 3, null, cancel.Token));
    }

    [Fact]
    public async Task An_error_from_the_call_is_not_retried()
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => TimedTries.RunAsync<string>(_ => { calls++; throw new InvalidOperationException("401"); }, Short, 3, null, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_call_that_honours_the_token_is_retried_too()
    {
        var calls = 0;
        var answer = await TimedTries.RunAsync(async ct => { if (++calls == 1) await Task.Delay(Timeout.Infinite, ct); return "ok"; },
            Short, 3, null, CancellationToken.None);
        Assert.Equal("ok", answer);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Cancelling_during_a_retry_stops_without_another_try()
    {
        using var cancel = new CancellationTokenSource();
        var calls = 0;
        var retried = new List<int>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TimedTries.RunAsync(ct =>
        {
            if (++calls == 2) cancel.CancelAfter(20);
            return Hang(ct);
        }, Short, 3, retried.Add, cancel.Token));
        Assert.Equal(2, calls);
        Assert.Equal([2], retried);
    }

    [Fact]
    public async Task The_limit_waits_while_a_person_signs_in()
    {
        // The answer comes after 400 ms, past the 100 ms limit, but a sign-in is under way until then: one try, no retry.
        var gate = new SignInGate();
        var signing = gate.Enter();
        var calls = 0;
        var answer = await TimedTries.RunAsync(async ct =>
        {
            calls++;
            await Task.Delay(400, ct);
            signing.Dispose();
            return "signed in";
        }, Short, 3, null, CancellationToken.None, hold: () => gate.Active);
        Assert.Equal("signed in", answer);
        Assert.Equal(1, calls);
        Assert.False(gate.Active);
    }

    [Theory]
    [InlineData("getJiraIssue", true)]
    [InlineData("executeRead", true)]
    [InlineData("atlassianUserInfo", true)]
    [InlineData("searchJiraIssuesUsingJql", true)]
    [InlineData("addOrEditJiraIssueComment", false)]
    [InlineData("createJiraIssue", false)]
    [InlineData("executeWrite", false)]
    [InlineData("executeDestructive", false)]
    [InlineData("search", true)]
    [InlineData("discover", true)]
    [InlineData("getOrCreateSomething", false)]
    [InlineData("listenForChanges", false)]
    public void Only_reads_are_tried_again(string tool, bool read) => Assert.Equal(read, JiraConnection.IsRead(tool));
}

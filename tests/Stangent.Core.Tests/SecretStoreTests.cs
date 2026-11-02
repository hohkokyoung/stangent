using ModelContextProtocol.Authentication;
using Stangent.Core;
using Stangent.Tickets.Jira;
using Stangent.Tickets.Secrets;

namespace Stangent.Core.Tests;

/// <summary>D92: several stangent processes reading and saving the same sign-in at once can see a busy keychain.</summary>
public class SecretStoreTests
{
    [Fact]
    public void A_keychain_call_that_fails_for_a_passing_reason_is_tried_again()
    {
        var results = new Queue<int>([-25308, -1, 0]);           // busy, a race, then fine
        var waits = new List<TimeSpan>();
        Assert.Equal(0, MacKeychain.Retry(() => results.Dequeue(), waits.Add));
        Assert.Equal(2, waits.Count);
        Assert.True(waits[1] > waits[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25300)]                                          // not found: an answer, not a failure
    public void Success_and_not_found_are_not_retried(int status)
    {
        var calls = 0;
        Assert.Equal(status, MacKeychain.Retry(() => { calls++; return status; }, _ => { }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void After_a_few_tries_the_last_failure_stands()
    {
        var calls = 0;
        Assert.Equal(-25308, MacKeychain.Retry(() => { calls++; return -25308; }, _ => { }, tries: 4));
        Assert.Equal(4, calls);
    }

    [Theory]
    [InlineData(-128)]      // the person clicked Deny
    [InlineData(-25293)]    // access denied
    [InlineData(-50)]       // a bad call
    public void A_refusal_is_an_answer_and_is_not_retried(int status)
    {
        var calls = 0;
        Assert.Equal(status, MacKeychain.Retry(() => { calls++; return status; }, _ => { }));
        Assert.Equal(1, calls);
    }

    sealed class BrokenStore(string? value) : ISecretStore
    {
        public string? Get(string service, string account) => value ?? throw new StangentException("keychain_failed", "Couldn't read the macOS Keychain item (OSStatus -25308).");
        public void Set(string service, string account, string v) => throw new StangentException("keychain_failed", "Couldn't save the macOS Keychain item (OSStatus -25308).");
        public bool Remove(string service, string account) => false;
    }

    [Fact]
    public async Task A_keychain_that_wont_save_a_refreshed_sign_in_is_a_warning_not_a_failed_call()
    {
        var log = new StringWriter();
        var cache = new KeychainTokenCache(new BrokenStore(null), "svc", "acct", log);
        var tokens = new TokenContainer { AccessToken = "a", TokenType = "Bearer", ObtainedAt = DateTimeOffset.UtcNow };
        await cache.StoreTokensAsync(tokens, TestContext.Current.CancellationToken);   // doesn't throw
        Assert.Contains("wasn't saved", log.ToString());
        Assert.Same(tokens, await cache.GetTokensAsync(TestContext.Current.CancellationToken));   // read fails: the held one is used
    }

    [Fact]
    public async Task The_cache_reads_the_keychain_each_time_so_another_process_refresh_is_seen()
    {
        var store = new MemorySecretStore();
        var cache = new KeychainTokenCache(store, "svc", "acct");
        Assert.Null(await cache.GetTokensAsync(TestContext.Current.CancellationToken));
        store.Set("svc", "acct", System.Text.Json.JsonSerializer.Serialize(new TokenContainer { AccessToken = "fresh", TokenType = "Bearer", ObtainedAt = DateTimeOffset.UtcNow },
            ModelContextProtocol.McpJsonUtilities.DefaultOptions));                                    // saved by another process
        Assert.Equal("fresh", (await cache.GetTokensAsync(TestContext.Current.CancellationToken))!.AccessToken);
    }

    [Fact]
    public async Task One_process_at_a_time_holds_the_sign_in_and_a_second_waits_then_gives_up()
    {
        var name = "test-" + Guid.NewGuid();
        await using (await SignInLock.AcquireAsync(name, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
        {
            var e = await Assert.ThrowsAsync<StangentException>(() => SignInLock.AcquireAsync(name, TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken));
            Assert.Equal("jira_signin_busy", e.Code);
        }
        await using var again = await SignInLock.AcquireAsync(name, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);   // free once released
    }
}

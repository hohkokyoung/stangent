using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Stangent.Core;
using Stangent.Tickets.Secrets;

namespace Stangent.Tickets.Jira;

/// <summary>How stangent signs in to Atlassian's MCP server (D73).</summary>
public enum JiraAuth
{
    /// <summary>Browser sign-in (OAuth 2.1 with dynamic client registration); the refresh token stays in the OS keychain.</summary>
    OAuth,
    /// <summary>Email + API token, Basic auth. For unattended runs; an organization admin must allow API tokens on the MCP server.</summary>
    ApiToken,
}

public sealed record JiraConnectionOptions
{
    /// <summary>MCP v2: the full tool set, incl. downloadJiraIssueAttachment behind discover/executeRead. v1 serves an older set without it.</summary>
    public const string DefaultEndpoint = "https://mcp.atlassian.com/v2/mcp";
    /// <summary>The keychain service every stangent Jira secret lives under.</summary>
    public const string KeychainService = "stangent.jira";

    public Uri Endpoint { get; init; } = new(DefaultEndpoint);
    public JiraAuth Auth { get; init; } = JiraAuth.OAuth;
    /// <summary>Fixed so the registered redirect URI stays valid across runs.</summary>
    public int CallbackPort { get; init; } = 47813;
    /// <summary>API-token sign-in: the Atlassian account's email. The token comes from the keychain or <c>STANGENT_JIRA_API_TOKEN</c>.</summary>
    public string? Email { get; init; }
    /// <summary>Opens the browser for the OAuth sign-in. Null: no browser (sign-in fails if a token is needed).</summary>
    public Action<Uri>? OpenBrowser { get; init; } = Browser.Open;
    public TimeSpan SignInTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public string OAuthAccount => "oauth:" + Endpoint.Host + Endpoint.AbsolutePath;
    public string TokenAccount => "token:" + Email;
}

/// <summary>
/// One connection to Atlassian's MCP server. Code calls its tools by name with named arguments (no AI in the path, D73).
/// </summary>
/// <summary>What the Jira backend needs from a connection: so tests can stand in for Atlassian with canned results.</summary>
public interface IJiraTools : IAsyncDisposable
{
    Task<ToolResult> CallAsync(string tool, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default);
    Task<DownloadedAttachment> DownloadAttachmentAsync(string cloudId, string attachmentId, string destination, CancellationToken ct = default);
}

public sealed class JiraConnection : IJiraTools
{
    readonly McpClient client;
    JiraConnection(McpClient client) => this.client = client;

    public static async Task<JiraConnection> OpenAsync(JiraConnectionOptions options, ISecretStore secrets, TextWriter log, CancellationToken ct = default)
    {
        // The endpoint comes from the repo's stangent.yaml; a token or sign-in is only ever sent to Atlassian.
        if (options.Endpoint.Scheme != Uri.UriSchemeHttps || !IsAtlassianHost(options.Endpoint.Host))
            throw new StangentException("untrusted_endpoint", $"tickets.endpoint must be an https Atlassian address, not {options.Endpoint}.", ExitCodes.Usage);
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = options.Endpoint,
            Name = "stangent-jira",
            TransportMode = HttpTransportMode.StreamableHttp,
        };
        if (options.Auth == JiraAuth.ApiToken)
        {
            if (string.IsNullOrWhiteSpace(options.Email))
                throw new StangentException("jira_no_email", "API-token sign-in needs the account's email (--email or tickets.email).", ExitCodes.Usage);
            var token = Environment.GetEnvironmentVariable("STANGENT_JIRA_API_TOKEN") is { Length: > 0 } env ? env
                : secrets.Get(JiraConnectionOptions.KeychainService, options.TokenAccount)
                ?? throw new StangentException("jira_not_signed_in",
                    $"No API token for {options.Email}. Run `stangent jira login --api-token --email {options.Email}` (prompts for the token, or reads it from stdin) or set STANGENT_JIRA_API_TOKEN.");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Email}:{token}"));
            transportOptions.AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Basic " + basic };
        }
        else
        {
            var redirect = new Uri($"http://127.0.0.1:{options.CallbackPort}/callback");
            transportOptions.OAuth = new ClientOAuthOptions
            {
                RedirectUri = redirect,
                TokenCache = new KeychainTokenCache(secrets, JiraConnectionOptions.KeychainService, options.OAuthAccount),
                DynamicClientRegistration = new DynamicClientRegistrationOptions { ClientName = "stangent" },
                // The server offers every product's scopes (delete, Bitbucket, Loom…); stangent asks only for Jira's.
                ScopeSelector = offered => offered?.Where(JiraScopes.Wanted).ToList(),
                AuthorizationCallbackHandler = (context, token) => BrowserSignIn.RunAsync(context, options, log, token),
            };
        }

        var transport = new HttpClientTransport(transportOptions);
        try
        {
            var client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "stangent", Version = typeof(JiraConnection).Assembly.GetName().Version?.ToString() ?? "0" },
                // The browser sign-in happens inside initialization, so the wait must cover a person at the consent page.
                InitializationTimeout = options.SignInTimeout + TimeSpan.FromSeconds(30),
            }, cancellationToken: ct);
            return new JiraConnection(client);
        }
        catch (Exception e) when (e is not StangentException and not OperationCanceledException)
        {
            throw new StangentException("jira_connect_failed", $"Couldn't connect to {options.Endpoint} ({options.Auth}): {e.Message}", ExitCodes.Failed,
                [.. Chain(e)]);
        }
    }

    public async Task<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken ct = default) =>
        [.. (await client.ListToolsAsync(cancellationToken: ct)).Select(t => t.ProtocolTool)];

    /// <summary>Calls one tool. A tool-level error (IsError) is raised as <c>jira_tool_failed</c> with the server's text.</summary>
    public async Task<ToolResult> CallAsync(string tool, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        CallToolResult result;
        try { result = await client.CallToolAsync(tool, args, cancellationToken: ct); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new StangentException("jira_call_failed", $"{tool}: {e.Message}", ExitCodes.Failed, [.. Chain(e)]);
        }
        var raw = JsonSerializer.SerializeToNode(result, McpJsonOptions)!.AsObject();
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));
        if (result.IsError == true)
            throw new StangentException("jira_tool_failed", $"{tool}: {Truncate(text, 2000)}", ExitCodes.Failed);
        return new ToolResult(tool, raw, text, result.StructuredContent is { } s ? JsonNode.Parse(s.GetRawText()) : null);
    }

    /// <summary>
    /// Downloads one attachment (D72, D73): <c>downloadJiraIssueAttachment</c> (an <c>executeRead</c> operation on MCP v2)
    /// mints a short-lived URL, and code fetches it. Only an https URL on an Atlassian host is followed.
    /// </summary>
    public async Task<DownloadedAttachment> DownloadAttachmentAsync(string cloudId, string attachmentId, string destination, CancellationToken ct = default)
    {
        var result = await CallAsync("executeRead", new Dictionary<string, object?>
        {
            ["cloudId"] = cloudId, ["name"] = "downloadJiraIssueAttachment",
            ["inputs"] = new Dictionary<string, object?> { ["cloudId"] = cloudId, ["attachmentId"] = attachmentId },
        }, ct);
        var url = result.Json?["data"]?["downloadUrl"]?.GetValue<string>()
            ?? throw new StangentException("jira_unexpected_output", $"downloadJiraIssueAttachment returned no data.downloadUrl for attachment {attachmentId}.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !IsAtlassianHost(uri.Host))
            throw new StangentException("jira_unexpected_output", $"Refusing to download attachment {attachmentId} from a non-Atlassian URL ({uri?.Host}).");

        // Redirects are followed by hand, each checked to stay on an Atlassian host.
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
        var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        for (var hops = 0; (int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next; hops++)
        {
            var target = next.IsAbsoluteUri ? next : new Uri(uri, next);
            response.Dispose();
            if (hops >= 5 || target.Scheme != Uri.UriSchemeHttps || !IsAtlassianHost(target.Host))
                throw new StangentException("jira_unexpected_output", $"Attachment {attachmentId}: refusing a redirect to {target.Host}.");
            uri = target;
            response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        using var _ = response;
        if (!response.IsSuccessStatusCode)
            throw new StangentException("jira_download_failed", $"Attachment {attachmentId}: HTTP {(int)response.StatusCode} from {uri.Host}.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        await using (var file = File.Create(destination))
            await response.Content.CopyToAsync(file, ct);
        var bytes = await File.ReadAllBytesAsync(destination, ct);
        return new DownloadedAttachment(attachmentId, destination, bytes.LongLength,
            "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Attaches a local file to an issue (used to copy the benchmark's frozen issues into Jira, D77): the MCP's
    /// <c>uploadAttachmentToJiraIssue</c> mints an upload URL and token, code posts the bytes, then attaches the file id.
    /// </summary>
    public async Task<string> UploadAttachmentAsync(string cloudId, string key, string file, CancellationToken ct = default)
    {
        Dictionary<string, object?> Args(Dictionary<string, object?> inputs) => new()
        {
            ["cloudId"] = cloudId, ["name"] = "uploadAttachmentToJiraIssue", ["inputs"] = new Dictionary<string, object?>(inputs) { ["cloudId"] = cloudId, ["issueIdOrKey"] = key },
        };
        var phase1 = await CallAsync("executeWrite", Args(new() { ["filePath"] = Path.GetFullPath(file) }), ct);
        var command = phase1.Json?["data"]?["uploadCommand"]?.GetValue<string>()
            ?? throw new StangentException("jira_unexpected_output", "uploadAttachmentToJiraIssue returned no data.uploadCommand.");
        var url = System.Text.RegularExpressions.Regex.Match(command, @"--location '([^']+)'").Groups[1].Value;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !IsAtlassianHost(uri.Host))
            throw new StangentException("jira_unexpected_output", "uploadAttachmentToJiraIssue gave no Atlassian upload URL.");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(file, ct)) };
        foreach (System.Text.RegularExpressions.Match h in System.Text.RegularExpressions.Regex.Matches(command, @"--header '([^:']+): ([^']*)'"))
            request.Headers.TryAddWithoutValidation(h.Groups[1].Value, h.Groups[2].Value);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new StangentException("jira_upload_failed", $"Uploading {Path.GetFileName(file)}: HTTP {(int)response.StatusCode}.");
        var fileId = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))?["data"]?["id"]?.GetValue<string>()
            ?? throw new StangentException("jira_unexpected_output", "The upload returned no data.id.");
        var phase2 = await CallAsync("executeWrite", Args(new() { ["fileId"] = fileId }), ct);
        return phase2.Json?["data"]?["attachment"]?["attachmentId"]?.ToString()
            ?? throw new StangentException("jira_unexpected_output", "uploadAttachmentToJiraIssue returned no attachment id.");
    }

    public static bool IsAtlassianHost(string host) =>
        host.Equals("atlassian.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".atlassian.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".atl-paas.net", StringComparison.OrdinalIgnoreCase);

    public ValueTask DisposeAsync() => client.DisposeAsync();

    static readonly JsonSerializerOptions McpJsonOptions = ModelContextProtocol.McpJsonUtilities.DefaultOptions;

    static IEnumerable<string> Chain(Exception e)
    {
        for (var x = e.InnerException; x is not null; x = x.InnerException) yield return $"{x.GetType().Name}: {x.Message}";
    }

    static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}

/// <summary>A tool's result: the raw MCP result (saved and hashed by callers), its text, and its JSON if the text is JSON.</summary>
public sealed record ToolResult(string Tool, JsonObject Raw, string Text, JsonNode? Structured)
{
    /// <summary>The result as JSON: the structured content if the server sent it, else the text parsed as JSON.</summary>
    public JsonNode? Json => Structured ?? TryParse(Text);

    static JsonNode? TryParse(string text)
    {
        try { return JsonNode.Parse(text); } catch (JsonException) { return null; }
    }
}

/// <summary>
/// The SDK's token cache, kept in the OS keychain. The cached container holds the access and refresh tokens and the
/// dynamically registered client, so a restart refreshes silently instead of registering and signing in again.
/// </summary>
public sealed class KeychainTokenCache(ISecretStore secrets, string service, string account) : ITokenCache
{
    TokenContainer? current;
    bool loaded;

    public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        if (!loaded)
        {
            loaded = true;
            if (secrets.Get(service, account) is { } json)
            {
                try { current = JsonSerializer.Deserialize<TokenContainer>(json, ModelContextProtocol.McpJsonUtilities.DefaultOptions); }
                catch (JsonException) { current = null; }
            }
        }
        return ValueTask.FromResult(current);
    }

    public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        current = tokens;
        loaded = true;
        secrets.Set(service, account, JsonSerializer.Serialize(tokens, ModelContextProtocol.McpJsonUtilities.DefaultOptions));
        return ValueTask.CompletedTask;
    }
}

/// <summary>The OAuth redirect: a one-shot listener on localhost that waits for the browser to come back.</summary>
static class BrowserSignIn
{
    public static async Task<AuthorizationResult?> RunAsync(AuthorizationCallbackContext context, JiraConnectionOptions options, TextWriter log, CancellationToken ct)
    {
        if (options.OpenBrowser is null)
            throw new StangentException("jira_not_signed_in", "Not signed in to Jira. Run `stangent jira login` first.");
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{options.CallbackPort}/");
        try { listener.Start(); }
        catch (HttpListenerException e)
        {
            throw new StangentException("jira_callback_port_busy", $"Can't listen on 127.0.0.1:{options.CallbackPort} for the sign-in redirect: {e.Message}");
        }
        log.WriteLine($"Opening the browser to sign in to Atlassian. If it doesn't open, visit:\n{context.AuthorizationUri}");
        options.OpenBrowser(context.AuthorizationUri);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.SignInTimeout);
        while (true)
        {
            var http = await listener.GetContextAsync().WaitAsync(timeout.Token);
            if (http.Request.Url?.AbsolutePath != "/callback")
            {
                http.Response.StatusCode = 404;
                http.Response.Close();
                continue;
            }
            var q = HttpUtility.ParseQueryString(http.Request.Url.Query);
            var ok = q["code"] is not null;
            var page = ok ? "Signed in to Jira. You can close this tab and go back to stangent."
                : $"Sign-in failed: {WebUtility.HtmlEncode(q["error"])} {WebUtility.HtmlEncode(q["error_description"])}";
            var bytes = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>stangent</title><p>{page}</p>");
            http.Response.ContentType = "text/html; charset=utf-8";
            await http.Response.OutputStream.WriteAsync(bytes, ct);
            http.Response.Close();
            if (!ok)
                throw new StangentException("jira_sign_in_failed", $"Atlassian refused the sign-in: {q["error"]} {q["error_description"]}");
            return new AuthorizationResult { Code = q["code"]!, State = q["state"], Iss = q["iss"] };
        }
    }
}

static class Browser
{
    public static void Open(Uri uri)
    {
        var (file, args) = OperatingSystem.IsMacOS() ? ("open", new[] { uri.AbsoluteUri })
            : OperatingSystem.IsWindows() ? ("cmd", new[] { "/c", "start", "", uri.AbsoluteUri.Replace("&", "^&") })
            : ("xdg-open", new[] { uri.AbsoluteUri });
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception) { /* the URL was printed; the user can open it */ }
    }
}

/// <summary>The OAuth scopes stangent requests: Jira read, search and write (comments), the user's identity, and a refresh token.</summary>
public static class JiraScopes
{
    static readonly HashSet<string> Exact = ["read:jira:agent-interface", "write:jira:agent-interface", "search:jira:agent-interface",
        "read:me", "read:account", "email", "offline_access"];

    public static bool Wanted(string scope) => Exact.Contains(scope);
}

public sealed record DownloadedAttachment(string AttachmentId, string Path, long Bytes, string Hash);

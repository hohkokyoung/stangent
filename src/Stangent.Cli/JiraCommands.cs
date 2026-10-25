using System.CommandLine;
using System.Text.Json.Nodes;
using Stangent.Core;
using Stangent.Tickets.Jira;
using Stangent.Tickets.Secrets;

/// <summary><c>stangent jira …</c>: signing in to Atlassian's MCP server and calling its tools from code (D73).</summary>
static class JiraCommands
{
    public static void Add(RootCommand root)
    {
        var jira = new Command("jira", "Jira through Atlassian's MCP server: sign-in and raw tool calls (D73).");
        root.Subcommands.Add(jira);

        var endpointOption = new Option<string?>("--endpoint") { Description = $"MCP endpoint (default {JiraConnectionOptions.DefaultEndpoint}).", Recursive = true };
        var apiTokenOption = new Option<bool>("--api-token") { Description = "Sign in with email + API token (Basic auth) instead of the browser.", Recursive = true };
        var emailOption = new Option<string?>("--email") { Description = "The Atlassian account's email, for --api-token.", Recursive = true };
        jira.Options.Add(endpointOption);
        jira.Options.Add(apiTokenOption);
        jira.Options.Add(emailOption);

        JiraConnectionOptions Options(ParseResult pr) => new()
        {
            Endpoint = new Uri(pr.GetValue(endpointOption) ?? JiraConnectionOptions.DefaultEndpoint),
            Auth = pr.GetValue(apiTokenOption) ? JiraAuth.ApiToken : JiraAuth.OAuth,
            Email = pr.GetValue(emailOption),
        };

        var login = new Command("login", "Sign in. Browser (OAuth) by default; with --api-token, reads the token from stdin. Secrets go to the OS keychain.");
        login.SetAction(pr => Cli.Run(pr, "jira login", _ =>
        {
            var options = Options(pr);
            var secrets = SecretStores.Os();
            if (options.Auth == JiraAuth.ApiToken)
            {
                if (string.IsNullOrWhiteSpace(options.Email))
                    throw new StangentException("jira_no_email", "--api-token needs --email.", ExitCodes.Usage);
                var token = (Console.IsInputRedirected ? Console.In.ReadToEnd() : ReadHidden($"API token for {options.Email}: ")).Trim();
                if (token.Length == 0) throw new StangentException("jira_no_token", "Pipe the API token on stdin.", ExitCodes.Usage);
                secrets.Set(JiraConnectionOptions.KeychainService, options.TokenAccount, token);
            }
            var user = Run(options, secrets, async c => (await c.CallAsync("atlassianUserInfo", new Dictionary<string, object?>())).Json);
            return new JsonObject
            {
                ["auth"] = options.Auth.ToString(), ["endpoint"] = options.Endpoint.ToString(),
                ["keychain"] = new JsonObject { ["service"] = JiraConnectionOptions.KeychainService, ["account"] = options.Auth == JiraAuth.OAuth ? options.OAuthAccount : options.TokenAccount },
                ["user"] = user?.DeepClone(),
            };
        }));
        jira.Subcommands.Add(login);

        var logout = new Command("logout", "Remove the saved sign-in from the OS keychain.");
        logout.SetAction(pr => Cli.Run(pr, "jira logout", _ =>
        {
            var options = Options(pr);
            var account = options.Auth == JiraAuth.OAuth ? options.OAuthAccount : options.TokenAccount;
            return new JsonObject { ["removed"] = SecretStores.Os().Remove(JiraConnectionOptions.KeychainService, account) };
        }));
        jira.Subcommands.Add(logout);

        var tools = new Command("tools", "List the MCP server's tools as this client sees them.");
        tools.SetAction(pr => Cli.Run(pr, "jira tools", _ => Run(Options(pr), SecretStores.Os(), async c =>
            (JsonNode)new JsonArray([.. (await c.ListToolsAsync()).OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name, ["description"] = t.Description, ["inputSchema"] = JsonNode.Parse(t.InputSchema.GetRawText()),
            })]))));
        jira.Subcommands.Add(tools);

        var toolArg = new Argument<string>("tool") { Description = "The MCP tool's name." };
        var argsOption = new Option<string?>("--args") { Description = "The tool's arguments as one JSON object." };
        var call = new Command("call", "Call one tool with JSON arguments and print the raw MCP result (for spikes and debugging).") { toolArg, argsOption };
        call.SetAction(pr => Cli.Run(pr, "jira call", _ =>
        {
            var args = JsonNode.Parse(pr.GetValue(argsOption) ?? "{}") as JsonObject
                ?? throw new StangentException("invalid_json", "--args must be a JSON object.", ExitCodes.Usage);
            var dict = args.ToDictionary(kv => kv.Key, kv => (object?)kv.Value?.DeepClone());
            return Run(Options(pr), SecretStores.Os(), async c =>
            {
                var r = await c.CallAsync(pr.GetValue(toolArg)!, dict);
                return (JsonNode)new JsonObject { ["json"] = r.Json?.DeepClone(), ["raw"] = r.Raw.DeepClone() };
            });
        }));
        jira.Subcommands.Add(call);

        var attachmentArg = new Argument<string>("attachment") { Description = "The numeric attachment id (from getJiraIssue's attachment field)." };
        var cloudOption = new Option<string>("--cloud-id") { Description = "The Atlassian site's cloudId.", Required = true };
        var outOption = new Option<string>("--out") { Description = "Where to save the file.", Required = true };
        var download = new Command("download", "Download one attachment through downloadJiraIssueAttachment's short-lived URL (D72, D73).") { attachmentArg, cloudOption, outOption };
        download.SetAction(pr => Cli.Run(pr, "jira download", _ => Run(Options(pr), SecretStores.Os(), async c =>
        {
            var a = await c.DownloadAttachmentAsync(pr.GetValue(cloudOption)!, pr.GetValue(attachmentArg)!, pr.GetValue(outOption)!);
            return (JsonNode)new JsonObject { ["attachmentId"] = a.AttachmentId, ["path"] = a.Path, ["bytes"] = a.Bytes, ["hash"] = a.Hash };
        })));
        jira.Subcommands.Add(download);
    }

    /// <summary>Reads a line from the terminal without echoing it; the prompt goes to stderr (stdout holds only the envelope).</summary>
    static string ReadHidden(string prompt)
    {
        Console.Error.Write(prompt);
        var text = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; continue; }
            if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return text.ToString();
    }

    static JsonNode? Run(JiraConnectionOptions options, ISecretStore secrets, Func<JiraConnection, Task<JsonNode?>> body)
    {
        return Task.Run(async () =>
        {
            await using var c = await JiraConnection.OpenAsync(options, secrets, Console.Error);
            return await body(c);
        }).GetAwaiter().GetResult();
    }
}

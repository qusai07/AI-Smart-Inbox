// Smart Inbox MCP server.
//   stdio (Claude Desktop / Claude Code):  SMARTINBOX_URL=... SMARTINBOX_API_KEY=sik_... smartinbox-mcp
//   HTTP (shared server):                   SMARTINBOX_URL=... smartinbox-mcp --http   → clients send their own "Authorization: Bearer sik_..."
using SmartInbox.Client;
using SmartInbox.Mcp;

var options = SmartInboxMcpOptions.FromEnvironment(Environment.GetEnvironmentVariable);

if (args.Contains("--http"))
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls($"http://0.0.0.0:{options.HttpPort}");
    builder.Logging.UseMcpAuditLevels();
    builder.Services.AddSmartInboxMcpHttp(options);

    var app = builder.Build();
    app.UseSmartInboxMcpAuth();
    app.MapMcp("/mcp");
    app.MapGet("/", () => $"Smart Inbox MCP server {McpSetup.Version}. MCP endpoint: /mcp (Authorization: Bearer sik_...). Writes {(options.AllowWrites ? "enabled" : "disabled")}.");
    app.Logger.LogInformation("Smart Inbox MCP (HTTP) on port {Port} → {BaseUrl}; writes {Writes}", options.HttpPort, options.BaseUrl, options.AllowWrites ? "enabled" : "disabled");
    await app.RunAsync();
}
else
{
    if (string.IsNullOrWhiteSpace(options.ApiKey) || !options.ApiKey.StartsWith("sik_", StringComparison.Ordinal))
    {
        await Console.Error.WriteLineAsync("Set SMARTINBOX_API_KEY to a Smart Inbox API key (sik_...). Create one in Administration > API keys.");
        return 1;
    }

    var builder = Host.CreateApplicationBuilder(args);
    // stdout carries the MCP protocol: every log line must go to stderr.
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace).UseMcpAuditLevels();
    builder.Services.AddSmartInboxClient(o =>
    {
        o.BaseUrl = options.BaseUrl;
        o.ApiKey = options.ApiKey;
    });
    builder.Services.AddSingleton<IInboxProvider>(sp => new FixedInboxProvider(sp.GetRequiredService<ISmartInboxClient>(), options.ApiKey));
    builder.Services.AddSmartInboxMcp(options).WithStdioServerTransport();
    await builder.Build().RunAsync();
}
return 0;

/// <summary>HTTP-mode plumbing, shared with the tests.</summary>
public static class SmartInboxMcpHttp
{
    /// <summary>Stateless Streamable HTTP: each request's own API key, nothing kept between requests.</summary>
    public static IServiceCollection AddSmartInboxMcpHttp(this IServiceCollection services, SmartInboxMcpOptions options)
    {
        services.AddHttpContextAccessor();
        // No automatic retries here: actions such as route or close must never run twice (the MCP client can retry reads).
        services.AddHttpClient("smart-inbox");
        services.AddSingleton<InboxClientFactory>(sp =>
        {
            var clients = sp.GetRequiredService<IHttpClientFactory>();
            return key => new SmartInboxClient(Configure(clients.CreateClient("smart-inbox"), options.BaseUrl, key));
        });
        services.AddScoped<IInboxProvider, RequestInboxProvider>();
        services.AddSmartInboxMcp(options).WithHttpTransport(o => o.Stateless = true);
        return services;
    }

    public static HttpClient Configure(HttpClient http, Uri baseUrl, string apiKey)
    {
        http.BaseAddress = baseUrl;
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"SmartInbox.Mcp/{McpSetup.Version}");
        return http;
    }

    /// <summary>Framework chatter (HTTP client, retries, MCP plumbing) at Warning, so the one-line tool audit stands out.</summary>
    public static ILoggingBuilder UseMcpAuditLevels(this ILoggingBuilder logging) =>
        logging.SetMinimumLevel(LogLevel.Warning).AddFilter("SmartInbox.Mcp", LogLevel.Information).AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

    /// <summary>Requests to /mcp must carry an API key; the Smart Inbox API then decides what that key may do.</summary>
    public static IApplicationBuilder UseSmartInboxMcpAuth(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/mcp") && McpSetup.BearerKey(ctx.Request) is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "Bearer";
            await ctx.Response.WriteAsync("Send your Smart Inbox API key: Authorization: Bearer sik_...");
            return;
        }
        await next(ctx);
    });
}

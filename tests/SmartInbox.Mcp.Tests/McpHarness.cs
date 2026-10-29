using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SmartInbox.Client.Tests;

namespace SmartInbox.Mcp.Tests;

/// <summary>An MCP client connected to the real MCP server, which talks to the in-process Smart Inbox through the SDK.</summary>
public sealed class McpHarness : IAsyncDisposable
{
    private readonly IAsyncDisposable _host;

    private McpHarness(McpClient client, IAsyncDisposable host)
    {
        Client = client;
        _host = host;
    }

    public McpClient Client { get; }

    /// <summary>Like stdio (one fixed key per process), over in-memory pipes.</summary>
    public static async Task<McpHarness> StdioAsync(SmartInboxFactory server, string apiKey, bool writes = true, int maxText = 8000, int maxWrites = 20)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IInboxProvider>(new FixedInboxProvider(server.Sdk(apiKey), apiKey));
        builder.Services.AddSmartInboxMcp(new SmartInboxMcpOptions { AllowWrites = writes, MaxTextChars = maxText, MaxWritesPerMinute = maxWrites })
            .WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        var host = builder.Build();
        await host.StartAsync();

        var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));
        return new McpHarness(client, new HostDisposer(host));
    }

    /// <summary>The HTTP transport exactly as Program wires it, on a TestServer; the caller's key goes in the header.</summary>
    public static async Task<(WebApplication App, HttpClient Http)> HttpServerAsync(SmartInboxFactory server, bool writes = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSmartInboxMcpHttp(new SmartInboxMcpOptions { AllowWrites = writes });
        builder.Services.AddSingleton<InboxClientFactory>(key => server.Sdk(key));
        var app = builder.Build();
        app.UseSmartInboxMcpAuth();
        app.MapMcp("/mcp");
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    public static async Task<McpHarness> HttpAsync(SmartInboxFactory server, string apiKey, bool writes = true)
    {
        var (app, http) = await HttpServerAsync(server, writes);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: false);
        var client = await McpClient.CreateAsync(transport);
        return new McpHarness(client, app);
    }

    public async Task<CallToolResult> CallAsync(string tool, object? args = null)
    {
        var arguments = args is null
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(args))!;
        return await Client.CallToolAsync(tool, arguments);
    }

    /// <summary>Calls a tool that must succeed and returns its text.</summary>
    public async Task<string> TextAsync(string tool, object? args = null)
    {
        var result = await CallAsync(tool, args);
        var text = Text(result);
        Assert.False(result.IsError == true, $"{tool} failed: {text}");
        return text;
    }

    public async Task<JsonElement> JsonAsync(string tool, object? args = null) => JsonDocument.Parse(await TextAsync(tool, args)).RootElement.Clone();

    public static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _host.DisposeAsync();
    }

    private sealed class HostDisposer(IHost host) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}

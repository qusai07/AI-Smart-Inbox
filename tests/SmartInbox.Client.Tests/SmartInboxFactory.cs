using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartInbox.Client;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.Client.Tests;

/// <summary>
/// The real API in-process: Development settings (demo users, Swagger, webhooks to localhost allowed),
/// the in-memory database and the free rule-based AI, so the whole pipeline runs without SQL Server or an LLM.
/// </summary>
public sealed class SmartInboxFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@mds.demo";
    public const string DemoPassword = "Demo#2026";

    private readonly string _data = Path.Combine(Path.GetTempPath(), "smart-inbox-tests", Guid.NewGuid().ToString("N"));

    public SmartInboxFactory()
    {
        // Read while Program registers services, before WebApplicationFactory's own configuration hooks run.
        Environment.SetEnvironmentVariable("Ai__Provider", "Rules");
        Environment.SetEnvironmentVariable("Email__Enabled", "false");
        Environment.SetEnvironmentVariable("Reminders__Enabled", "false");
        Environment.SetEnvironmentVariable("Storage__Root", Path.Combine(_data, "uploads"));
        Environment.SetEnvironmentVariable("DataProtection__KeysPath", Path.Combine(_data, "keys"));
        Environment.SetEnvironmentVariable("Webhooks__AllowHttp", "true");
        Environment.SetEnvironmentVariable("Webhooks__AllowPrivateNetworks", "true");
        // Configured but not polling: tests fill the fake mailbox and press "Check now".
        Environment.SetEnvironmentVariable("Mailbox__Host", "imap.test");
        Environment.SetEnvironmentVariable("Mailbox__Username", "correspondence@mds.demo");
    }

    /// <summary>The shared mailbox the app reads from, in memory.</summary>
    public TestMailbox Mailbox { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.Remove(services.Single(d => d.ServiceType == typeof(DbContextOptions<SmartInboxDbContext>)));
            var name = Guid.NewGuid().ToString();
            services.AddDbContext<SmartInboxDbContext>(o => o.UseInMemoryDatabase(name));
            services.AddSingleton<SmartInbox.Application.IMailboxClient>(Mailbox);
        });
    }

    /// <summary>A client signed in with the admin cookie, like the web app.</summary>
    public async Task<HttpClient> AdminAsync()
    {
        var http = CreateClient();
        (await http.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = DemoPassword })).EnsureSuccessStatusCode();
        return http;
    }

    /// <summary>Creates an API key through the admin endpoint and returns the secret.</summary>
    public async Task<(int Id, string Secret)> CreateKeyAsync(string role, int? departmentId = null)
    {
        using var admin = await AdminAsync();
        var response = await admin.PostAsJsonAsync("/api/api-keys", new { name = $"{role} test key", role, departmentId });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedKey>();
        return (created!.Key.Id, created.Secret);
    }

    /// <summary>The SDK, authenticated with an API key, talking to the in-process server.</summary>
    public SmartInboxClient Sdk(string apiKey)
    {
        var http = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return new SmartInboxClient(http);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed record CreatedKey(KeyRow Key, string Secret);

    private sealed record KeyRow(int Id);
}

[CollectionDefinition(Name)]
public class ServerCollection : ICollectionFixture<SmartInboxFactory>
{
    public const string Name = "server";
}

/// <summary>An in-memory IMAP folder: messages leave it when marked processed.</summary>
public sealed class TestMailbox : SmartInbox.Application.IMailboxClient
{
    private readonly List<SmartInbox.Application.MailMessage> _folder = [];
    private uint _next = 1;

    public Func<uint, Exception?> FetchFailure { get; set; } = _ => null;

    public uint Add(string subject, string body, string from = "sender@partner.gov", params SmartInbox.Application.MailAttachment[] attachments)
    {
        lock (_folder)
        {
            var uid = _next++;
            _folder.Add(new SmartInbox.Application.MailMessage(uid, $"<t{uid}-{Guid.NewGuid():N}@partner.gov>", "Partner Authority", from, subject,
                DateTimeOffset.UtcNow, body, false, attachments, System.Text.Encoding.UTF8.GetBytes($"Subject: {subject}{Environment.NewLine}{Environment.NewLine}{body}")));
            return uid;
        }
    }

    public Task<IReadOnlyList<SmartInbox.Application.MailboxItem>> ListPendingAsync(int max, CancellationToken ct)
    {
        lock (_folder) return Task.FromResult<IReadOnlyList<SmartInbox.Application.MailboxItem>>(_folder.Take(max).Select(m => new SmartInbox.Application.MailboxItem(m.Uid, m.MessageId)).ToList());
    }

    public Task<SmartInbox.Application.MailMessage> FetchAsync(uint uid, CancellationToken ct)
    {
        if (FetchFailure(uid) is { } ex) return Task.FromException<SmartInbox.Application.MailMessage>(ex);
        lock (_folder) return Task.FromResult(_folder.Single(m => m.Uid == uid));
    }

    public Task MarkProcessedAsync(uint uid, CancellationToken ct)
    {
        lock (_folder) _folder.RemoveAll(m => m.Uid == uid);
        return Task.CompletedTask;
    }

    public Task<SmartInbox.Application.MailboxTestResult> TestAsync(CancellationToken ct)
    {
        lock (_folder) return Task.FromResult(new SmartInbox.Application.MailboxTestResult(true, _folder.Count, $"Connected; {_folder.Count} waiting."));
    }
}

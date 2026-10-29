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
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.Remove(services.Single(d => d.ServiceType == typeof(DbContextOptions<SmartInboxDbContext>)));
            var name = Guid.NewGuid().ToString();
            services.AddDbContext<SmartInboxDbContext>(o => o.UseInMemoryDatabase(name));
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

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Api.Auth;
using SmartInbox.Api.Hubs;
using SmartInbox.Application;
using SmartInbox.Infrastructure;
using SmartInbox.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSmartInboxAuth(builder.Configuration);
builder.Services.AddSingleton<IInboxNotifier, SignalRInboxNotifier>();
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = UploadRules.MaxFileBytes);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

await MigrateAsync(app);
using (var scope = app.Services.CreateScope())
    await IdentitySeed.RunAsync(scope.ServiceProvider, app.Configuration, app.Environment, app.Logger);

app.Logger.LogInformation("Analysis engine: {Engine}", SmartInbox.Infrastructure.DependencyInjection.Describe(app.Configuration));
var usesClaude = (app.Configuration["Ai:Provider"] ?? "Hybrid").Equals("Claude", StringComparison.OrdinalIgnoreCase)
    || (app.Configuration["Ai:Llm"] ?? "Ollama").Equals("Claude", StringComparison.OrdinalIgnoreCase);
if (usesClaude && string.IsNullOrWhiteSpace(app.Configuration["Claude:ApiKey"]) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
    app.Logger.LogWarning("Claude is selected but no Anthropic API key is configured (Claude:ApiKey or ANTHROPIC_API_KEY).");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Browsers must use the declared Content-Type, never guess one from the bytes (uploaded files are untrusted).
app.Use((ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    return next(ctx);
});

// Built Vue app (copied into wwwroot by the Docker build).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<InboxHub>("/hubs/inbox");
app.MapFallbackToFile("index.html");

app.Run();

static async Task MigrateAsync(WebApplication app)
{
    // SQL Server in Docker can take a while to accept connections on first start.
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<SmartInboxDbContext>().Database;
            // Integration tests swap in the in-memory provider, which has no migrations.
            if (database.IsRelational()) await database.MigrateAsync();
            else await database.EnsureCreatedAsync();
            return;
        }
        catch (Exception ex) when (attempt < 10)
        {
            app.Logger.LogWarning("Database not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}

/// <summary>Visible to WebApplicationFactory in the integration tests.</summary>
public partial class Program;

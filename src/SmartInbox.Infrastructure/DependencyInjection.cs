using Anthropic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartInbox.Application;
using SmartInbox.Infrastructure.Ai;
using SmartInbox.Infrastructure.Ai.Rules;
using SmartInbox.Infrastructure.Email;
using SmartInbox.Infrastructure.Persistence;
using SmartInbox.Infrastructure.Processing;
using SmartInbox.Infrastructure.Storage;
using SmartInbox.Infrastructure.TextExtraction;
using SmartInbox.Infrastructure.Webhooks;
using Microsoft.AspNetCore.DataProtection;

namespace SmartInbox.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<SmartInboxDbContext>(o =>
            o.UseSqlServer(config.GetConnectionString("SmartInbox"), sql => sql.EnableRetryOnFailure()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<SmartInboxDbContext>());

        services.Configure<StorageOptions>(config.GetSection("Storage"));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddSingleton<ITextExtractor, DocxTextExtractor>();
        services.AddSingleton<ITextExtractor, EmlTextExtractor>();
        services.AddSingleton<ITextExtractor, PlainTextExtractor>();

        services.AddAiProvider(config);

        services.AddSingleton<DocumentQueue>();
        services.AddSingleton<IDocumentQueue>(sp => sp.GetRequiredService<DocumentQueue>());
        services.AddHostedService<DocumentProcessingWorker>();

        services.Configure<EmailOptions>(config.GetSection("Email"));
        services.AddSingleton(config.GetSection("Notifications").Get<NotificationOptions>() ?? new NotificationOptions());
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddHostedService<NotificationWorker>();

        services.AddSingleton(config.GetSection("Reminders").Get<ReminderOptions>() ?? new ReminderOptions());
        services.AddHostedService<ReminderWorker>();

        // Webhook signing secrets are encrypted with Data Protection; the keys must survive restarts (Docker: the /data volume).
        services.AddDataProtection()
            .SetApplicationName("SmartInbox")
            .PersistKeysToFileSystem(new DirectoryInfo(config["DataProtection:KeysPath"] ?? "App_Data/keys"));
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        var webhooks = config.GetSection("Webhooks").Get<WebhookOptions>() ?? new WebhookOptions();
        services.AddSingleton(webhooks);
        services.AddHttpClient(HttpWebhookTransport.ClientName, c => c.Timeout = HttpWebhookTransport.Timeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false, // a proxy would make the connect-time address check meaningless
                UseCookies = false,
                ConnectCallback = SsrfGuard.ConnectCallback(webhooks.AllowPrivateNetworks),
            });
        services.AddSingleton<IWebhookTransport, HttpWebhookTransport>();
        services.AddHostedService<WebhookWorker>();
        return services;
    }

    /// <summary>
    /// Registers the analysis engine chosen by Ai:Provider — Rules, Ollama, Claude or Hybrid (default).
    /// No database or hosting; also used by the AI evals runner.
    /// </summary>
    public static IServiceCollection AddAiProvider(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AiOptions>(config.GetSection("Ai"));
        services.Configure<ClaudeOptions>(config.GetSection("Claude"));
        services.Configure<OllamaOptions>(config.GetSection("Ollama"));
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<RuleBasedProvider>();
        services.AddSingleton<OllamaProvider>();
        services.AddSingleton<ClaudeProvider>();
        services.AddSingleton(sp =>
        {
            // Claude:ApiKey (user-secrets / env Claude__ApiKey) wins, then ANTHROPIC_API_KEY. Only built if Claude is used.
            var key = sp.GetRequiredService<IOptions<ClaudeOptions>>().Value.ApiKey;
            return string.IsNullOrWhiteSpace(key) ? new AnthropicClient() : new AnthropicClient { ApiKey = key };
        });

        services.AddSingleton<IAiProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            return Normalize(options.Provider) switch
            {
                "rules" => sp.GetRequiredService<RuleBasedProvider>(),
                "ollama" or "claude" => Llm(sp, options.Provider),
                _ => new GatedProvider(
                    sp.GetRequiredService<RuleBasedProvider>(),
                    Llm(sp, options.Llm),
                    options.GateConfidence,
                    sp.GetRequiredService<ILogger<GatedProvider>>()),
            };
        });
        return services;
    }

    public static string Describe(IConfiguration config)
    {
        var o = config.GetSection("Ai").Get<AiOptions>() ?? new AiOptions();
        return Normalize(o.Provider) is "rules" or "ollama" or "claude"
            ? o.Provider
            : $"Hybrid (rules first, {o.Llm} when confidence < {o.GateConfidence:0.00})";
    }

    private static IAiProvider Llm(IServiceProvider sp, string name) => Normalize(name) switch
    {
        "claude" => sp.GetRequiredService<ClaudeProvider>(),
        "ollama" => sp.GetRequiredService<OllamaProvider>(),
        _ => throw new InvalidOperationException($"Unknown AI provider '{name}'. Use Rules, Ollama, Claude or Hybrid."),
    };

    private static string Normalize(string? name) => (name ?? "").Trim().ToLowerInvariant();
}

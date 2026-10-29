using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartInbox.Application;

namespace SmartInbox.Infrastructure.Webhooks;

public class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("SmartInbox.Webhooks.Secret");

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}

/// <summary>
/// Every 5 seconds delivers due webhooks; every hour queues deadline.approaching events.
/// Both are safe to run on several instances (at-least-once delivery, dedup keys for deadlines).
/// </summary>
public class WebhookWorker(IServiceScopeFactory scopes, WebhookOptions options, ILogger<WebhookWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DeadlineInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Webhooks are disabled (Webhooks:Enabled=false); deliveries will stay queued.");
            return;
        }

        // Let startup (migrations, seeding) finish first.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        var nextDeadlineScan = DateTime.MinValue;
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (DateTime.UtcNow >= nextDeadlineScan)
                {
                    nextDeadlineScan = DateTime.UtcNow + DeadlineInterval;
                    await scope.ServiceProvider.GetRequiredService<WebhookDeadlineScanner>().RunAsync(stoppingToken);
                }
                await scope.ServiceProvider.GetRequiredService<WebhookDispatcher>().DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Webhook dispatch failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

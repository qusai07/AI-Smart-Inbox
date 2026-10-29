using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartInbox.Application;

namespace SmartInbox.Infrastructure.Email;

/// <summary>
/// Every 15 minutes, queues due digests and escalations into the outbox. ReminderService de-duplicates,
/// so frequent runs, restarts and multiple instances never send the same reminder twice.
/// </summary>
public class ReminderWorker(IServiceScopeFactory scopes, ReminderOptions options, ILogger<ReminderWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Deadline reminders are disabled (Reminders:Enabled=false).");
            return;
        }

        // Let startup (migrations, seeding) finish first.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReminderService>().RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reminder run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

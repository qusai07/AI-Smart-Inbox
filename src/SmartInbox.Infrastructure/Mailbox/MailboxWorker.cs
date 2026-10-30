using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartInbox.Application;

namespace SmartInbox.Infrastructure.Mailbox;

/// <summary>One check at a time, shared by the schedule and "Check now"; remembers the outcome for the admin page.</summary>
public sealed class MailboxRunner(IServiceScopeFactory scopes, MailboxOptions options, TimeProvider clock, ILogger<MailboxRunner> logger) : IMailboxRunner
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime? _lastCheckAt;
    private DateTime? _nextCheckAt;
    private MailboxRunResult? _lastResult;
    private string? _lastError;

    public MailboxStatus Status => new(
        options.Enabled, !string.IsNullOrWhiteSpace(options.Host), options.Address, options.Host, options.Folder,
        string.IsNullOrWhiteSpace(options.ProcessedFolder) ? null : options.ProcessedFolder, options.PollSeconds,
        _gate.CurrentCount == 0, _lastCheckAt, options.Enabled ? _nextCheckAt : null, _lastResult, _lastError);

    public async Task<MailboxRunResult> CheckNowAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
            throw new InvalidOperationException("The mailbox isn't configured: set Mailbox:Host, Mailbox:Username and Mailbox:Password.");

        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<MailboxImporter>().RunAsync(ct);
            _lastResult = result;
            _lastError = null;
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _lastError = ImapMailboxClient.Describe(ex);
            logger.LogWarning(ex, "Mailbox check failed: {Error}", _lastError);
            throw new InvalidOperationException(_lastError, ex);
        }
        finally
        {
            _lastCheckAt = clock.GetUtcNow().UtcDateTime;
            _nextCheckAt = _lastCheckAt.Value.AddSeconds(Math.Max(10, options.PollSeconds));
            _gate.Release();
        }
    }

    public async Task<MailboxTestResult> TestAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Host))
            return new MailboxTestResult(false, null, "The mailbox isn't configured: set Mailbox:Host, Mailbox:Username and Mailbox:Password.");
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMailboxClient>().TestAsync(ct);
    }
}

/// <summary>Checks the shared mailbox every Mailbox:PollSeconds when Mailbox:Enabled is true.</summary>
public class MailboxWorker(IMailboxRunner runner, MailboxOptions options, ILogger<MailboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Mailbox ingestion is off (Mailbox:Enabled=false).");
            return;
        }
        logger.LogInformation("Checking mailbox {Mailbox} on {Host} every {Seconds} s", options.Address, options.Host, options.PollSeconds);

        // Let startup (migrations, seeding) finish first.
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(10, options.PollSeconds)));
        do
        {
            try
            {
                await runner.CheckNowAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Already recorded for the admin page; keep checking.
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

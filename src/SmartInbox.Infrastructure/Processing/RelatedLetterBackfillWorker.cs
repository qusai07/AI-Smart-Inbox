using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartInbox.Application;

namespace SmartInbox.Infrastructure.Processing;

/// <summary>Once at startup: gives letters from before related-letter matching their keys and links.</summary>
public sealed class RelatedLetterBackfillWorker(IServiceScopeFactory scopes, ILogger<RelatedLetterBackfillWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // let the app start first
            await using var scope = scopes.CreateAsyncScope();
            var count = await scope.ServiceProvider.GetRequiredService<RelatedLetterBackfill>().RunAsync(stoppingToken);
            if (count > 0) logger.LogInformation("Related letters: prepared {Count} earlier letter(s)", count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Related letters: preparing earlier letters failed; it will be tried again at the next start");
        }
    }
}

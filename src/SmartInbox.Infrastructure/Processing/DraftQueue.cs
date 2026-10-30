using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.Infrastructure.Processing;

/// <summary>In-memory queue of reply drafts to generate; a draft id already queued or running is ignored.</summary>
public class DraftQueue : IDraftQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = false });
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();

    public ValueTask EnqueueAsync(Guid draftId, CancellationToken ct = default) =>
        _inFlight.TryAdd(draftId, 0) ? _channel.Writer.WriteAsync(draftId, ct) : ValueTask.CompletedTask;

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    public void Done(Guid draftId) => _inFlight.TryRemove(draftId, out _);
}

/// <summary>Generates queued drafts in the background. Re-queues drafts left unfinished by a restart.</summary>
public class DraftWorker(DraftQueue queue, IServiceScopeFactory scopes, ILogger<DraftWorker> logger) : BackgroundService
{
    private const int MaxConcurrency = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeuePendingAsync(stoppingToken);

        await Parallel.ForEachAsync(queue.ReadAllAsync(stoppingToken),
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = stoppingToken },
            async (id, ct) =>
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ReplyDraftGenerator>().GenerateAsync(id, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Unhandled error generating draft {DraftId}", id);
                }
                finally
                {
                    queue.Done(id);
                }
            });
    }

    private async Task RequeuePendingAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SmartInboxDbContext>();
            var pending = await db.ReplyDrafts
                .Where(d => d.Status == ReplyDraftStatus.Requested || d.Status == ReplyDraftStatus.Generating)
                .Select(d => d.Id)
                .ToListAsync(ct);
            foreach (var id in pending) await queue.EnqueueAsync(id, ct);
            if (pending.Count > 0) logger.LogInformation("Re-queued {Count} unfinished reply drafts", pending.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not re-queue pending reply drafts");
        }
    }
}

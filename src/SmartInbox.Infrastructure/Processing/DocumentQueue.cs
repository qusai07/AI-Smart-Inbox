using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.Infrastructure.Processing;

public class DocumentQueue : IDocumentQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = false });

    public ValueTask EnqueueAsync(Guid documentId, CancellationToken ct = default) => _channel.Writer.WriteAsync(documentId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Drains the queue with a small degree of parallelism. Re-queues unfinished work on startup.</summary>
public class DocumentProcessingWorker(DocumentQueue queue, IServiceScopeFactory scopes, ILogger<DocumentProcessingWorker> logger)
    : BackgroundService
{
    private const int MaxConcurrency = 3;

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
                    await scope.ServiceProvider.GetRequiredService<DocumentProcessor>().ProcessAsync(id, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Unhandled error processing document {DocumentId}", id);
                }
            });
    }

    private async Task RequeuePendingAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SmartInboxDbContext>();
            var pending = await db.Documents
                .Where(d => d.Status == DocumentStatus.Uploaded || d.Status == DocumentStatus.Processing)
                .Select(d => d.Id)
                .ToListAsync(ct);
            foreach (var id in pending) await queue.EnqueueAsync(id, ct);
            if (pending.Count > 0) logger.LogInformation("Re-queued {Count} unfinished documents", pending.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not re-queue pending documents");
        }
    }
}

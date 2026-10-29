using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Processing;

namespace SmartInbox.UnitTests;

/// <summary>Regression tests for reviewed issues (duplicate re-analysis, live-update audience).</summary>
public class FixesTests
{
    [Fact]
    public async Task Reanalyze_WhileProcessing_IsRejected()
    {
        using var db = TestDb.Create();
        var queue = new NullQueue();
        var service = new DocumentService(db, new MemoryStorage(), queue, new RecordingNotifier());
        var doc = await service.UploadTextAsync("letter", null, default);   // status: Processing

        Assert.Equal(DocumentService.ReanalyzeOutcome.Busy, await service.ReanalyzeAsync(doc.Id, default));
        Assert.Single(queue.Enqueued);   // only the upload

        doc.Status = DocumentStatus.Analyzed;
        await db.SaveChangesAsync();
        Assert.Equal(DocumentService.ReanalyzeOutcome.Accepted, await service.ReanalyzeAsync(doc.Id, default));
        Assert.Equal(DocumentService.ReanalyzeOutcome.Busy, await service.ReanalyzeAsync(doc.Id, default));
    }

    [Fact]
    public async Task Queue_HoldsEachDocumentOnce_UntilDone()
    {
        var queue = new DocumentQueue();
        var id = Guid.NewGuid();
        await queue.EnqueueAsync(id);
        await queue.EnqueueAsync(id);   // ignored: already queued

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var reader = queue.ReadAllAsync(cts.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(id, reader.Current);

        await queue.EnqueueAsync(id);   // ignored: still running
        queue.Done(id);
        await queue.EnqueueAsync(id);   // accepted again after it finished
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(id, reader.Current);
    }

    [Fact]
    public async Task Rerouting_NotifiesBothTheOldAndTheNewDepartment()
    {
        using var db = TestDb.Create();
        var notifier = new RecordingNotifier();
        var doc = new Document { FileName = "a.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        var service = new DocumentService(db, new MemoryStorage(), new NullQueue(), notifier);

        await service.RouteAsync(doc.Id, new RouteRequest(2, null), null, default);
        await service.RouteAsync(doc.Id, new RouteRequest(4, null), null, default);
        await service.CloseAsync(doc.Id, null, default);

        Assert.Equal([2], notifier.Audiences[0]);
        Assert.Equal([2, 4], notifier.Audiences[1].Order());
        Assert.Equal([4], notifier.Audiences[2]);
    }

    [Fact]
    public void Audience_DropsNullsAndDuplicates() =>
        Assert.Equal([3], Audience.Of(null, 3, 3));
}

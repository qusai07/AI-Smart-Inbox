using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.TextExtraction;

namespace SmartInbox.UnitTests;

public class TimelineTests
{
    [Fact]
    public async Task FullLifecycle_IsRecordedInOrder_WithWhoAndNotes()
    {
        using var db = TestDb.Create();
        var storage = new MemoryStorage();
        var notifier = new RecordingNotifier();
        var desk = new DocumentService(db, storage, new NullQueue(), notifier, new TestUser("Sara", Roles.Clerk));
        var legal = new DocumentService(db, storage, new NullQueue(), notifier, new TestUser("Omar", Roles.Department, 2));

        var doc = await desk.UploadTextAsync("Notice of hearing from the court.", null, default);
        await new DocumentProcessor(db, storage, [new PlainTextExtractor()], new FakeAi(_ => FakeAi.Result(2, 0.9)), notifier, NullLogger<DocumentProcessor>.Instance)
            .ProcessAsync(doc.Id, default);
        await desk.RouteAsync(doc.Id, new RouteRequest(2, "Court case"), null, default);
        await legal.AcknowledgeAsync(doc.Id, default);
        await legal.CloseAsync(doc.Id, "Defence memorandum filed", default);
        await legal.ReopenAsync(doc.Id, default);

        var timeline = (await desk.GetAsync(doc.Id, default))!.Timeline;

        Assert.Equal(
            [DocumentEventType.Uploaded, DocumentEventType.Analyzed, DocumentEventType.Routed,
             DocumentEventType.Acknowledged, DocumentEventType.Closed, DocumentEventType.Reopened],
            timeline.Select(e => e.Type));
        Assert.Equal("Sara", timeline[0].Actor);
        Assert.Equal("test-model", timeline[1].Actor);
        Assert.Equal(("Sara", 2, "Court case"), (timeline[2].Actor, timeline[2].DepartmentId, timeline[2].Note));
        Assert.Equal("Omar", timeline[3].Actor);
        Assert.Equal("Defence memorandum filed", timeline[4].Note);
        // History survives: the close is still visible after reopening.
        Assert.Equal(HandlingStatus.Acknowledged, db.Documents.Single().Handling);
    }

    [Fact]
    public async Task FailedAnalysis_IsRecorded()
    {
        using var db = TestDb.Create();
        var storage = new MemoryStorage();
        var service = new DocumentService(db, storage, new NullQueue(), new RecordingNotifier());
        var doc = await service.UploadTextAsync("x", null, default);

        await new DocumentProcessor(db, storage, [new PlainTextExtractor()], new FakeAi(_ => throw new AiException("Model offline")), new RecordingNotifier(), NullLogger<DocumentProcessor>.Instance)
            .ProcessAsync(doc.Id, default);

        var failed = (await service.GetAsync(doc.Id, default))!.Timeline.Last();
        Assert.Equal(DocumentEventType.AnalysisFailed, failed.Type);
        Assert.Equal("Model offline", failed.Note);
    }
}

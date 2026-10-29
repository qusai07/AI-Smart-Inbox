using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.TextExtraction;

namespace SmartInbox.UnitTests;

public class PipelineTests
{
    private readonly MemoryStorage _storage = new();
    private readonly NullQueue _queue = new();
    private readonly RecordingNotifier _notifier = new();

    private async Task<(DocumentStatus Status, Document Doc)> UploadAndProcess(IAiProvider ai, string text = "Dear IT, please fix the VPN.")
    {
        using var db = TestDb.Create();
        var service = new DocumentService(db, _storage, _queue, _notifier);
        var doc = await service.UploadTextAsync(text, "tester", default);

        var processor = new DocumentProcessor(db, _storage, [new PlainTextExtractor()], ai, _notifier, NullLogger<DocumentProcessor>.Instance);
        await processor.ProcessAsync(doc.Id, default);
        return (doc.Status, doc);
    }

    [Fact]
    public async Task HighConfidence_IsReadyToRoute_WithAnalysisAndActionItems()
    {
        var ai = new FakeAi(_ => FakeAi.Result(departmentId: 5, confidence: 0.9));
        var (status, doc) = await UploadAndProcess(ai);

        Assert.Equal(DocumentStatus.Analyzed, status);
        Assert.Equal(5, doc.Analysis!.SuggestedDepartmentId);
        Assert.Single(doc.ActionItems);
        Assert.Equal("Dear IT, please fix the VPN.", ai.LastInput!.Text);
        Assert.Contains(_notifier.Events, e => e.Id == doc.Id && e.Status == DocumentStatus.Processing);
        Assert.Equal(DocumentStatus.Analyzed, _notifier.Events.Last().Status);
    }

    [Fact]
    public async Task LowConfidence_GoesToNeedsReview()
    {
        var (status, _) = await UploadAndProcess(new FakeAi(_ => FakeAi.Result(5, 0.45)));
        Assert.Equal(DocumentStatus.NeedsReview, status);
    }

    [Fact]
    public async Task UnknownDepartment_IsDropped_AndNeedsReview()
    {
        var (status, doc) = await UploadAndProcess(new FakeAi(_ => FakeAi.Result(departmentId: 99, confidence: 0.95)));
        Assert.Equal(DocumentStatus.NeedsReview, status);
        Assert.Null(doc.Analysis!.SuggestedDepartmentId);
    }

    [Fact]
    public async Task AiFailure_MarksFailed_WithMessage()
    {
        var (status, doc) = await UploadAndProcess(new FakeAi(_ => throw new HttpRequestException("boom")));
        Assert.Equal(DocumentStatus.Failed, status);
        Assert.NotNull(doc.ErrorMessage);
        Assert.DoesNotContain("boom", doc.ErrorMessage); // internal errors are not leaked to users
    }

    [Fact]
    public async Task Refusal_ShowsRefusalMessage()
    {
        var (status, doc) = await UploadAndProcess(new FakeAi(_ => throw new AiRefusalException("Declined.")));
        Assert.Equal(DocumentStatus.Failed, status);
        Assert.Equal("Declined.", doc.ErrorMessage);
    }

    [Fact]
    public async Task ImageWithoutText_IsSentToAiAsFile()
    {
        using var db = TestDb.Create();
        var service = new DocumentService(db, _storage, _queue, _notifier);
        var doc = await service.UploadAsync(new MemoryStream([0x89, 0x50, 0x4E, 0x47]), "scan.png", "image/png", null, default);

        var ai = new FakeAi(_ => FakeAi.Result(1, 0.8) with { ExtractedText = "transcribed text" });
        var processor = new DocumentProcessor(db, _storage, [new PlainTextExtractor()], ai, _notifier, NullLogger<DocumentProcessor>.Instance);
        await processor.ProcessAsync(doc.Id, default);

        Assert.Null(ai.LastInput!.Text);
        Assert.Equal("image/png", ai.LastInput.MediaType);
        Assert.Equal("transcribed text", doc.ExtractedText);
    }

    [Fact]
    public async Task Reanalyzing_RoutedDocument_StaysRouted()
    {
        using var db = TestDb.Create();
        var service = new DocumentService(db, _storage, _queue, _notifier);
        var doc = await service.UploadTextAsync("Dear IT, please fix the VPN.", null, default);
        var processor = new DocumentProcessor(db, _storage, [new PlainTextExtractor()], new FakeAi(_ => FakeAi.Result(5, 0.9)), _notifier, NullLogger<DocumentProcessor>.Instance);
        await processor.ProcessAsync(doc.Id, default);
        await service.RouteAsync(doc.Id, new RouteRequest(5, null), null, default);

        await service.ReanalyzeAsync(doc.Id, default);
        await processor.ProcessAsync(doc.Id, default);

        Assert.Equal(DocumentStatus.Routed, doc.Status);
    }

    [Fact]
    public async Task Upload_QueuesDocument()
    {
        using var db = TestDb.Create();
        var doc = await new DocumentService(db, _storage, _queue, _notifier)
            .UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes("x")), "a.txt", "text/plain", null, default);

        Assert.Equal(DocumentStatus.Processing, doc.Status);
        Assert.Contains(doc.Id, _queue.Enqueued);
    }
}

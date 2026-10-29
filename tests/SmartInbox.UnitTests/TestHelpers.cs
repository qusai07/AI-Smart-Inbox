using Microsoft.EntityFrameworkCore;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.UnitTests;

internal static class TestDb
{
    public static SmartInboxDbContext Create()
    {
        var db = new SmartInboxDbContext(new DbContextOptionsBuilder<SmartInboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Database.EnsureCreated(); // applies the department seed
        return db;
    }
}

internal class FakeAi(Func<AnalysisInput, AiAnalysisResult> respond) : IAiProvider
{
    public AnalysisInput? LastInput { get; private set; }

    public Task<AiAnalysisResult> AnalyzeAsync(AnalysisInput input, IReadOnlyList<Department> departments, CancellationToken ct = default)
    {
        LastInput = input;
        return Task.FromResult(respond(input));
    }

    public static AiAnalysisResult Result(int departmentId, double confidence) => new(
        "Summary", "ملخص", DocumentCategory.Request, Priority.Normal, "en", "Sender", null, "REF-1",
        new DateOnly(2027, 1, 1), null, [new AiActionItem("Reply to sender", new DateOnly(2027, 2, 1))],
        departmentId, confidence, "Because", "test-model", 100);
}

internal class MemoryStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _files = new();

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var key = Guid.NewGuid().ToString("N");
        _files[key] = ms.ToArray();
        return key;
    }

    public Stream Open(string storagePath) => new MemoryStream(_files[storagePath]);
}

internal class NullQueue : IDocumentQueue
{
    public List<Guid> Enqueued { get; } = [];

    public ValueTask EnqueueAsync(Guid documentId, CancellationToken ct = default)
    {
        Enqueued.Add(documentId);
        return ValueTask.CompletedTask;
    }
}

internal class RecordingNotifier : IInboxNotifier
{
    public List<(Guid Id, DocumentStatus Status)> Events { get; } = [];

    public Task DocumentStatusChangedAsync(Guid documentId, DocumentStatus status, CancellationToken ct = default)
    {
        Events.Add((documentId, status));
        return Task.CompletedTask;
    }
}

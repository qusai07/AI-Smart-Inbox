using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.UnitTests;

public class HandlingTests
{
    private static async Task<(DocumentService Service, Guid Id, Infrastructure.Persistence.SmartInboxDbContext Db)> RoutedTo(int departmentId)
    {
        var db = TestDb.Create();
        var doc = new Document
        {
            FileName = "letter.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed,
            Analysis = new DocumentAnalysis { SuggestedDepartmentId = departmentId, Confidence = 0.9, Deadline = new DateOnly(2020, 1, 1) },
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        var service = new DocumentService(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier());
        await service.RouteAsync(doc.Id, new RouteRequest(departmentId, null), null, default);
        return (service, doc.Id, db);
    }

    [Fact]
    public async Task Routing_PutsDocumentInDepartmentInbox_AsNew()
    {
        var (service, id, _) = await RoutedTo(4);

        var inbox = await service.ListAsync(new DocumentQuery(Assigned: 4), default);

        var item = Assert.Single(inbox.Items);
        Assert.Equal(id, item.Id);
        Assert.Equal(HandlingStatus.New, item.Handling);
        Assert.Empty((await service.ListAsync(new DocumentQuery(Assigned: 3), default)).Items);
    }

    [Fact]
    public async Task Acknowledge_Then_Close_WithNote()
    {
        var (service, id, db) = await RoutedTo(4);

        Assert.Equal(DocumentService.HandlingOutcome.Ok, await service.AcknowledgeAsync(id, default));
        Assert.Equal(HandlingStatus.Acknowledged, db.Documents.Single().Handling);

        await service.CloseAsync(id, "  Nominations sent.  ", default);
        var doc = db.Documents.Single();
        Assert.Equal(HandlingStatus.Closed, doc.Handling);
        Assert.Equal("Nominations sent.", doc.HandlingNote);
        Assert.Equal(DocumentStatus.Routed, doc.Status);
    }

    [Fact]
    public async Task Rerouting_ResetsHandling_ForTheNewDepartment()
    {
        var (service, id, db) = await RoutedTo(4);
        await service.CloseAsync(id, "Not ours", default);

        await service.RouteAsync(id, new RouteRequest(2, "Legal should handle it"), null, default);

        var doc = db.Documents.Single();
        Assert.Equal(2, doc.AssignedDepartmentId);
        Assert.Equal(HandlingStatus.New, doc.Handling);
        Assert.Null(doc.HandlingNote);
    }

    [Fact]
    public async Task UnroutedDocument_CannotBeAcknowledged()
    {
        var db = TestDb.Create();
        var doc = new Document { FileName = "a.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        var service = new DocumentService(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier());

        Assert.Equal(DocumentService.HandlingOutcome.NotRouted, await service.AcknowledgeAsync(doc.Id, default));
    }

    [Fact]
    public async Task Workload_CountsNewAndOverdue()
    {
        var (service, _, _) = await RoutedTo(4);

        var w = Assert.Single(await service.GetWorkloadAsync(default));

        Assert.Equal(4, w.DepartmentId);
        Assert.Equal(1, w.New);
        Assert.Equal(1, w.Overdue); // deadline 2020-01-01, not closed
    }
}

using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.UnitTests;

public class RoutingTests
{
    private static async Task<(DocumentService Service, Guid Id, Infrastructure.Persistence.SmartInboxDbContext Db)> AnalyzedDocument(int? suggested)
    {
        var db = TestDb.Create();
        var doc = new Document
        {
            FileName = "letter.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed,
            Analysis = new DocumentAnalysis { SuggestedDepartmentId = suggested, Confidence = 0.9 },
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return (new DocumentService(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier()), doc.Id, db);
    }

    [Fact]
    public async Task Accepting_Suggestion_IsNotAnOverride()
    {
        var (service, id, db) = await AnalyzedDocument(suggested: 3);

        Assert.Equal(DocumentService.RouteOutcome.Ok, await service.RouteAsync(id, new RouteRequest(3, null), "clerk", default));

        var decision = Assert.Single(db.RoutingDecisions);
        Assert.False(decision.WasOverridden);
        Assert.Equal(DocumentStatus.Routed, db.Documents.Single().Status);
    }

    [Fact]
    public async Task Choosing_Another_Department_IsAnOverride_AndCountsAgainstAccuracy()
    {
        var (service, id, _) = await AnalyzedDocument(suggested: 3);

        await service.RouteAsync(id, new RouteRequest(2, "Contract issue, not procurement"), "clerk", default);
        var stats = await service.GetStatsAsync(default);

        Assert.Equal(1, stats.Decisions);
        Assert.Equal(0, stats.RoutingAccuracy);
    }

    [Fact]
    public async Task Latest_Decision_Wins_ForAccuracy()
    {
        var (service, id, _) = await AnalyzedDocument(suggested: 3);

        await service.RouteAsync(id, new RouteRequest(2, null), null, default);
        await Task.Delay(10);
        await service.RouteAsync(id, new RouteRequest(3, "Changed my mind"), null, default);
        var stats = await service.GetStatsAsync(default);

        Assert.Equal(1, stats.Decisions);
        Assert.Equal(1, stats.RoutingAccuracy);
    }

    [Fact]
    public async Task Manual_Routing_WithoutSuggestion_IsExcludedFromAccuracy()
    {
        var (service, id, _) = await AnalyzedDocument(suggested: null);

        await service.RouteAsync(id, new RouteRequest(4, null), null, default);
        var stats = await service.GetStatsAsync(default);

        Assert.Equal(0, stats.Decisions);
        Assert.Null(stats.RoutingAccuracy);
    }

    [Fact]
    public async Task Unknown_Department_IsRejected()
    {
        var (service, id, _) = await AnalyzedDocument(suggested: 3);
        Assert.Equal(DocumentService.RouteOutcome.UnknownDepartment, await service.RouteAsync(id, new RouteRequest(999, null), null, default));
    }

    [Fact]
    public async Task Processing_Document_CannotBeRouted()
    {
        var (service, id, db) = await AnalyzedDocument(suggested: 3);
        db.Documents.Single().Status = DocumentStatus.Processing;
        await db.SaveChangesAsync();

        Assert.Equal(DocumentService.RouteOutcome.NotReady, await service.RouteAsync(id, new RouteRequest(3, null), null, default));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, true)]
    public void RouteRequest_Validation(int departmentId, bool valid) =>
        Assert.Equal(valid, new RouteRequestValidator().Validate(new RouteRequest(departmentId, null)).IsValid);
}

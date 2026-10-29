using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.UnitTests;

internal record TestUser(string? Name, string? Role, int? DepartmentId = null, string? Email = null) : ICurrentUser
{
    public string? Id => Name;
}

public class AccessTests
{
    private static async Task<(Infrastructure.Persistence.SmartInboxDbContext Db, Guid LegalDoc, Guid HrDoc)> TwoRoutedDocuments()
    {
        var db = TestDb.Create();
        var legal = new Document { FileName = "court.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed };
        var hr = new Document { FileName = "leave.txt", ContentType = "text/plain", StoragePath = "y", Status = DocumentStatus.Analyzed };
        db.Documents.AddRange(legal, hr);
        await db.SaveChangesAsync();

        var desk = Service(db, new TestUser("Sara", Roles.Clerk));
        await desk.RouteAsync(legal.Id, new RouteRequest(2, null), null, default);
        await desk.RouteAsync(hr.Id, new RouteRequest(4, null), null, default);
        return (db, legal.Id, hr.Id);
    }

    private static DocumentService Service(Infrastructure.Persistence.SmartInboxDbContext db, ICurrentUser user) =>
        new(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier(), user);

    [Fact]
    public async Task DepartmentUser_SeesOnlyOwnDepartment()
    {
        var (db, legalDoc, hrDoc) = await TwoRoutedDocuments();
        var omar = Service(db, new TestUser("Omar", Roles.Department, DepartmentId: 2));

        var list = await omar.ListAsync(new DocumentQuery(), default);

        Assert.Equal(legalDoc, Assert.Single(list.Items).Id);
        Assert.NotNull(await omar.GetAsync(legalDoc, default));
        Assert.Null(await omar.GetAsync(hrDoc, default));          // looks like "not found", not "forbidden"
        Assert.Null(await omar.OpenFileAsync(hrDoc, default));
        Assert.Equal(2, Assert.Single(await omar.GetWorkloadAsync(default)).DepartmentId);
    }

    [Fact]
    public async Task DepartmentUser_CannotHandleAnotherDepartmentsDocument()
    {
        var (db, legalDoc, hrDoc) = await TwoRoutedDocuments();
        var omar = Service(db, new TestUser("Omar", Roles.Department, DepartmentId: 2));

        Assert.Equal(DocumentService.HandlingOutcome.Forbidden, await omar.CloseAsync(hrDoc, null, default));
        Assert.Equal(DocumentService.HandlingOutcome.Ok, await omar.CloseAsync(legalDoc, "Defence filed", default));
        Assert.Equal("Omar", db.Documents.Single(d => d.Id == legalDoc).HandlingBy);
    }

    [Fact]
    public async Task UserWithoutRole_SeesNothing()
    {
        var (db, _, _) = await TwoRoutedDocuments();
        var pending = Service(db, new TestUser("New Microsoft user", Role: null));

        Assert.Empty((await pending.ListAsync(new DocumentQuery(), default)).Items);
    }

    [Fact]
    public async Task Clerk_SeesEverything_AndIsRecordedAsDecider()
    {
        var (db, _, _) = await TwoRoutedDocuments();

        Assert.Equal(2, (await Service(db, new TestUser("Sara", Roles.Clerk)).ListAsync(new DocumentQuery(), default)).Total);
        Assert.All(db.RoutingDecisions, r => Assert.Equal("Sara", r.DecidedBy));
    }
}

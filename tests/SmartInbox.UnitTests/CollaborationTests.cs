using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.UnitTests;

public class CollaborationTests
{
    private class Directory(params DirectoryUser[] users) : IUserDirectory
    {
        public Task<DirectoryUser?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(users.FirstOrDefault(u => u.Id == id));

        public Task<IReadOnlyList<DirectoryUser>> DepartmentMembersAsync(int departmentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DirectoryUser>>(users.Where(u => u.DepartmentId == departmentId && u.Role == Roles.Department).ToList());
    }

    private static readonly DirectoryUser Omar = new("omar", "Omar", "omar@mds.demo", Roles.Department, 2);
    private static readonly DirectoryUser Huda = new("huda", "Huda", "huda@mds.demo", Roles.Department, 2);
    private static readonly DirectoryUser Mariam = new("mariam", "Mariam", "mariam@mds.demo", Roles.Department, 4);
    private static readonly IUserDirectory People = new Directory(Omar, Huda, Mariam);

    private static readonly TestUser Clerk = new("Sara", Roles.Clerk, Email: "clerk@mds.demo");
    private static TestUser AsStaff(DirectoryUser u) => new(u.DisplayName, Roles.Department, u.DepartmentId, u.Email);

    private static async Task<(Infrastructure.Persistence.SmartInboxDbContext Db, Guid Id)> RoutedToLegal()
    {
        var db = TestDb.Create();
        var doc = new Document { FileName = "court.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        await new DocumentService(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier(), Clerk)
            .RouteAsync(doc.Id, new RouteRequest(2, null), null, default);
        return (db, doc.Id);
    }

    private static CollaborationService As(Infrastructure.Persistence.SmartInboxDbContext db, ICurrentUser user) =>
        new(db, People, new RecordingNotifier(), user);

    [Fact]
    public async Task Assigning_SetsOwner_Acknowledges_EmailsAssignee_AndRecordsTimeline()
    {
        var (db, id) = await RoutedToLegal();

        Assert.Equal(CollaborationService.AssignOutcome.Ok, await As(db, Clerk).AssignAsync(id, "huda", default));

        var doc = db.Documents.Single();
        Assert.Equal(("huda", "Huda"), (doc.AssigneeId, doc.AssigneeName));
        Assert.Equal(HandlingStatus.Acknowledged, doc.Handling);
        Assert.Contains(db.Notifications, n => n.Kind == NotificationKind.Assigned && n.To == "huda@mds.demo");
        Assert.Contains(db.DocumentEvents, e => e.Type == DocumentEventType.Assigned && e.Actor == "Sara" && e.Note == "Huda");
    }

    [Fact]
    public async Task AssigningYourself_SendsNoEmail()
    {
        var (db, id) = await RoutedToLegal();

        await As(db, new SelfUser(AsStaff(Omar), "omar")).AssignAsync(id, "omar", default);

        Assert.DoesNotContain(db.Notifications, n => n.Kind == NotificationKind.Assigned);
        Assert.Equal("omar", db.Documents.Single().AssigneeId);
    }

    [Fact]
    public async Task CannotAssignSomeoneFromAnotherDepartment()
    {
        var (db, id) = await RoutedToLegal();
        Assert.Equal(CollaborationService.AssignOutcome.InvalidAssignee, await As(db, Clerk).AssignAsync(id, "mariam", default));
        Assert.Null(db.Documents.Single().AssigneeId);
    }

    [Fact]
    public async Task OtherDepartmentStaff_CannotSeeOrAssign()
    {
        var (db, id) = await RoutedToLegal();
        Assert.Equal(CollaborationService.AssignOutcome.NotFound, await As(db, AsStaff(Mariam)).AssignAsync(id, "huda", default));
        Assert.Null(await As(db, AsStaff(Mariam)).MembersAsync(2, default));
        Assert.Null(await As(db, AsStaff(Mariam)).AddCommentAsync(id, "hello", default));
    }

    [Fact]
    public async Task Unassign_And_Reroute_ClearTheOwner()
    {
        var (db, id) = await RoutedToLegal();
        await As(db, Clerk).AssignAsync(id, "huda", default);

        await As(db, Clerk).AssignAsync(id, null, default);
        Assert.Null(db.Documents.Single().AssigneeId);

        await As(db, Clerk).AssignAsync(id, "huda", default);
        await new DocumentService(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier(), Clerk)
            .RouteAsync(id, new RouteRequest(4, "HR matter"), null, default);
        Assert.Null(db.Documents.Single().AssigneeId);
    }

    [Fact]
    public async Task MineFilter_ShowsOnlyMyItems()
    {
        var (db, id) = await RoutedToLegal();
        await As(db, Clerk).AssignAsync(id, "huda", default);
        var docs = (ICurrentUser user) => new DocumentService(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier(), user);

        Assert.Single((await docs(new SelfUser(AsStaff(Huda), "huda")).ListAsync(new DocumentQuery(Mine: true), default)).Items);
        Assert.Empty((await docs(new SelfUser(AsStaff(Omar), "omar")).ListAsync(new DocumentQuery(Mine: true), default)).Items);
        Assert.Equal("Huda", (await docs(Clerk).ListAsync(new DocumentQuery(), default)).Items.Single().AssigneeName);
    }

    [Fact]
    public async Task Comments_AreStoredWithAuthor_AndReturnedInOrder()
    {
        var (db, id) = await RoutedToLegal();
        await As(db, new SelfUser(AsStaff(Omar), "omar")).AddCommentAsync(id, "  Called the court registry.  ", default);
        await As(db, Clerk).AddCommentAsync(id, "Thanks, please share the hearing date.", default);

        var detail = await new DocumentService(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier(), Clerk).GetAsync(id, default);

        Assert.Equal(["Called the court registry.", "Thanks, please share the hearing date."], detail!.Comments.Select(c => c.Text));
        Assert.Equal(("omar", "Omar"), (detail.Comments[0].AuthorId, detail.Comments[0].AuthorName));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("ok", true)]
    public void CommentValidation(string text, bool valid) =>
        Assert.Equal(valid, new CommentRequestValidator().Validate(new CommentRequest(text)).IsValid);

    /// <summary>A test user with a specific id (TestUser uses its name as id).</summary>
    private record SelfUser(TestUser Inner, string UserId) : ICurrentUser
    {
        public string? Id => UserId;
        public string? Name => Inner.Name;
        public string? Role => Inner.Role;
        public int? DepartmentId => Inner.DepartmentId;
        public string? Email => Inner.Email;
    }
}

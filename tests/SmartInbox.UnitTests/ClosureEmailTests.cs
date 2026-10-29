using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.UnitTests;

public class ClosureEmailTests
{
    private class CapturingEmail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static readonly TestUser Clerk = new("Sara", Roles.Clerk, Email: "clerk@mds.demo");
    private static readonly TestUser LegalUser = new("Omar", Roles.Department, 2, "legal.user@mds.demo");

    private static async Task<(Infrastructure.Persistence.SmartInboxDbContext Db, Guid Id)> Document()
    {
        var db = TestDb.Create();
        var doc = new Document
        {
            FileName = "court.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed,
            Analysis = new DocumentAnalysis { SuggestedDepartmentId = 2, Confidence = 0.9, Sender = "Demo City Court", ReferenceNumber = "2027/118" },
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return (db, doc.Id);
    }

    private static DocumentService As(Infrastructure.Persistence.SmartInboxDbContext db, ICurrentUser user, string? deskEmail = null) =>
        new(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier(), user, new NotificationOptions { DeskEmail = deskEmail });

    [Fact]
    public async Task Closing_EmailsThePersonWhoRoutedIt_WithTheNote()
    {
        var (db, id) = await Document();
        await As(db, Clerk).RouteAsync(id, new RouteRequest(2, null), null, default);
        await As(db, LegalUser).CloseAsync(id, "Defence memorandum <filed>", default);

        var closure = Assert.Single(db.Notifications, n => n.Kind == NotificationKind.Closed);
        Assert.Equal("clerk@mds.demo", closure.To);

        var email = new CapturingEmail();
        await new NotificationDispatcher(db, email, new NotificationOptions(), NullLogger<NotificationDispatcher>.Instance).DispatchPendingAsync(default);

        var message = Assert.Single(email.Sent, m => m.To == "clerk@mds.demo");
        Assert.Equal("Closed by Legal Affairs: Demo City Court", message.Subject);
        Assert.Contains("Omar", message.Html);
        Assert.Contains("Defence memorandum &lt;filed&gt;", message.Html);   // escaped
        Assert.Contains(db.DocumentEvents, e => e.Type == DocumentEventType.EmailSent && e.Note == "clerk@mds.demo");
    }

    [Fact]
    public async Task ClosingYourOwnRouting_SendsNothing()
    {
        var (db, id) = await Document();
        var admin = new TestUser("Admin", Roles.Admin, Email: "admin@mds.demo");
        await As(db, admin).RouteAsync(id, new RouteRequest(2, null), null, default);
        await As(db, admin).CloseAsync(id, null, default);

        Assert.DoesNotContain(db.Notifications, n => n.Kind == NotificationKind.Closed);
    }

    [Theory]
    [InlineData("desk@mds.demo", true)]
    [InlineData(null, false)]
    public async Task UnknownRouter_FallsBackToDeskMailbox(string? deskEmail, bool expectEmail)
    {
        var (db, id) = await Document();
        await As(db, SystemUser.Instance).RouteAsync(id, new RouteRequest(2, null), "legacy", default);   // no email recorded
        await As(db, LegalUser, deskEmail).CloseAsync(id, null, default);

        var closures = db.Notifications.Where(n => n.Kind == NotificationKind.Closed).ToList();
        Assert.Equal(expectEmail, closures.Count == 1);
        if (expectEmail) Assert.Equal("desk@mds.demo", closures[0].To);
    }

    [Fact]
    public async Task AcknowledgeAndReopen_DoNotEmail()
    {
        var (db, id) = await Document();
        await As(db, Clerk).RouteAsync(id, new RouteRequest(2, null), null, default);
        await As(db, LegalUser).AcknowledgeAsync(id, default);
        await As(db, LegalUser).ReopenAsync(id, default);

        Assert.DoesNotContain(db.Notifications, n => n.Kind == NotificationKind.Closed);
    }
}

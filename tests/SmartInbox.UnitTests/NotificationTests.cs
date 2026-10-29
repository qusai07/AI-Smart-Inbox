using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.UnitTests;

public class NotificationTests
{
    private class FakeEmail(bool fail = false) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (fail) throw new IOException("SMTP down");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static async Task<(Infrastructure.Persistence.SmartInboxDbContext Db, Guid Id)> RoutedDocument(string summary = "Summary")
    {
        var db = TestDb.Create();
        var doc = new Document
        {
            FileName = "letter.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed,
            Analysis = new DocumentAnalysis { SuggestedDepartmentId = 2, Confidence = 0.9, Priority = Priority.Urgent, SummaryEn = summary, Sender = "Court" },
        };
        db.Documents.Add(doc);
        db.Departments.Single(d => d.Id == 2).Email = "legal@mds.demo";
        await db.SaveChangesAsync();
        await new DocumentService(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier())
            .RouteAsync(doc.Id, new RouteRequest(2, null), null, default);
        return (db, doc.Id);
    }

    private static NotificationDispatcher Dispatcher(Infrastructure.Persistence.SmartInboxDbContext db, IEmailSender email) =>
        new(db, email, new NotificationOptions { AppBaseUrl = "http://inbox.test" }, NullLogger<NotificationDispatcher>.Instance);

    [Fact]
    public async Task Routing_QueuesEmail_AndDispatcherSendsIt()
    {
        var (db, id) = await RoutedDocument();
        var n = Assert.Single(db.Notifications);
        Assert.Equal(NotificationStatus.Pending, n.Status);

        var email = new FakeEmail();
        await Dispatcher(db, email).DispatchPendingAsync(default);

        var message = Assert.Single(email.Sent);
        Assert.Equal("legal@mds.demo", message.To);
        Assert.StartsWith("[Urgent] New correspondence for Legal Affairs: Court", message.Subject);
        Assert.Contains($"http://inbox.test/documents/{id}", message.Html);
        Assert.Equal(NotificationStatus.Sent, db.Notifications.Single().Status);
    }

    [Fact]
    public async Task DocumentText_IsHtmlEncoded()
    {
        var (db, _) = await RoutedDocument("<script>alert(1)</script>");
        var email = new FakeEmail();

        await Dispatcher(db, email).DispatchPendingAsync(default);

        Assert.DoesNotContain("<script>", email.Sent.Single().Html);
        Assert.Contains("&lt;script&gt;", email.Sent.Single().Html);
    }

    [Fact]
    public async Task Failure_IsRetriedLater_ThenMarkedFailed()
    {
        var (db, _) = await RoutedDocument();
        var dispatcher = Dispatcher(db, new FakeEmail(fail: true));

        await dispatcher.DispatchPendingAsync(default);
        var n = db.Notifications.Single();
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(1, n.Attempts);
        Assert.True(n.NextAttemptAt > DateTime.UtcNow);
        Assert.Equal("SMTP down", n.Error);

        // Not due yet: nothing is attempted.
        Assert.Equal(0, await dispatcher.DispatchPendingAsync(default));

        for (var i = 1; i < NotificationDispatcher.MaxAttempts; i++)
        {
            n.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
            await dispatcher.DispatchPendingAsync(default);
        }
        Assert.Equal(NotificationStatus.Failed, db.Notifications.Single().Status);
    }

    [Fact]
    public async Task DepartmentWithoutEmail_QueuesNothing()
    {
        var db = TestDb.Create();
        var doc = new Document { FileName = "a.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed };
        db.Documents.Add(doc);
        db.Departments.Single(d => d.Id == 3).Email = null;
        await db.SaveChangesAsync();

        await new DocumentService(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier())
            .RouteAsync(doc.Id, new RouteRequest(3, null), null, default);

        Assert.Empty(db.Notifications);
    }
}

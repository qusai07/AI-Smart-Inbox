using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.UnitTests;

public class ReminderTests
{
    private static readonly DateOnly Today = new(2027, 2, 16);

    private class CapturingEmail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Infrastructure.Persistence.SmartInboxDbContext Db()
    {
        var db = TestDb.Create();
        foreach (var d in db.Departments) { d.Email = $"dept{d.Id}@mds.demo"; d.HeadEmail = $"head{d.Id}@mds.demo"; }
        db.SaveChanges();
        return db;
    }

    private static Guid AddRouted(Infrastructure.Persistence.SmartInboxDbContext db, int department, int deadlineInDays, HandlingStatus handling = HandlingStatus.New, string sender = "Sender")
    {
        var doc = new Document
        {
            FileName = "x.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Routed,
            AssignedDepartmentId = department, Handling = handling,
            Analysis = new DocumentAnalysis { Sender = sender, Deadline = Today.AddDays(deadlineInDays), Priority = Priority.High, Confidence = 0.9 },
        };
        db.Documents.Add(doc);
        db.SaveChanges();
        return doc.Id;
    }

    private static ReminderService Reminders(Infrastructure.Persistence.SmartInboxDbContext db, int hourUtc = 9, string? deskEmail = null) =>
        new(db, new ReminderOptions { TimeZone = "UTC", DigestHour = 8, DueSoonDays = 3 },
            new NotificationOptions { AppBaseUrl = "http://inbox.test", DeskEmail = deskEmail },
            new Clock(new DateTimeOffset(Today.ToDateTime(new TimeOnly(hourUtc, 0)), TimeSpan.Zero)),
            NullLogger<ReminderService>.Instance);

    [Fact]
    public async Task Digest_IsQueuedOncePerDepartmentPerDay_OnlyWhenSomethingIsDue()
    {
        using var db = Db();
        AddRouted(db, 2, deadlineInDays: 2);    // Legal: due soon
        AddRouted(db, 4, deadlineInDays: 30);   // HR: not due soon

        var first = await Reminders(db).RunAsync(default);
        var second = await Reminders(db).RunAsync(default);

        Assert.Equal(1, first.Digests);
        Assert.Equal(0, second.Digests);
        var digest = Assert.Single(db.Notifications, n => n.Kind == NotificationKind.Digest);
        Assert.Equal("dept2@mds.demo", digest.To);
        Assert.Null(digest.DocumentId);
    }

    [Fact]
    public async Task NoDigestBeforeTheDigestHour_ButEscalationsStillRun()
    {
        using var db = Db();
        AddRouted(db, 2, deadlineInDays: -1);

        var result = await Reminders(db, hourUtc: 6).RunAsync(default);

        Assert.Equal(0, result.Digests);
        Assert.Equal(1, result.Escalations);
    }

    [Fact]
    public async Task OverdueOpenItem_EscalatesOnceToTheHead_AndShowsOnTimeline()
    {
        using var db = Db();
        var overdue = AddRouted(db, 2, deadlineInDays: -2);
        AddRouted(db, 2, deadlineInDays: -5, HandlingStatus.Closed);   // closed: never escalated

        await Reminders(db).RunAsync(default);
        await Reminders(db).RunAsync(default);

        var escalation = Assert.Single(db.Notifications, n => n.Kind == NotificationKind.Escalation);
        Assert.Equal("head2@mds.demo", escalation.To);
        Assert.Equal(overdue, escalation.DocumentId);
        var ev = Assert.Single(db.DocumentEvents, e => e.Type == DocumentEventType.Escalated);
        Assert.Equal("2 day(s) overdue → head2@mds.demo", ev.Note);
    }

    [Theory]
    [InlineData("desk@mds.demo", 1)]
    [InlineData(null, 0)]
    public async Task NoHeadEmail_FallsBackToDesk(string? desk, int expected)
    {
        using var db = Db();
        db.Departments.Single(d => d.Id == 2).HeadEmail = null;
        db.SaveChanges();
        AddRouted(db, 2, deadlineInDays: -1);

        await Reminders(db, deskEmail: desk).RunAsync(default);

        Assert.Equal(expected, db.Notifications.Count(n => n.Kind == NotificationKind.Escalation));
    }

    [Fact]
    public async Task Dispatcher_SendsDigestAndEscalationEmails()
    {
        using var db = Db();
        AddRouted(db, 2, deadlineInDays: -2, sender: "Demo City Court");
        AddRouted(db, 2, deadlineInDays: 1, sender: "Nimbus <Cloud>");
        var reminders = Reminders(db);
        await reminders.RunAsync(default);

        var email = new CapturingEmail();
        await new NotificationDispatcher(db, email, new NotificationOptions { AppBaseUrl = "http://inbox.test" }, NullLogger<NotificationDispatcher>.Instance, reminders)
            .DispatchPendingAsync(default);

        var digest = Assert.Single(email.Sent, m => m.To == "dept2@mds.demo");
        Assert.Equal("Legal Affairs: 1 overdue, 1 due soon", digest.Subject);
        Assert.Contains("Overdue 2d", digest.Html);
        Assert.Contains("Due tomorrow", digest.Html);
        Assert.Contains("Nimbus &lt;Cloud&gt;", digest.Html);

        var escalation = Assert.Single(email.Sent, m => m.To == "head2@mds.demo");
        Assert.Equal("[Overdue 2d] Legal Affairs: Demo City Court", escalation.Subject);
        Assert.All(db.Notifications, n => Assert.Equal(NotificationStatus.Sent, n.Status));
    }

    [Fact]
    public async Task DigestWithNothingLeftDue_IsNotSent()
    {
        using var db = Db();
        var id = AddRouted(db, 2, deadlineInDays: 1);
        var reminders = Reminders(db);
        await reminders.RunAsync(default);
        db.Documents.Single(d => d.Id == id).Handling = HandlingStatus.Closed;   // closed before the digest went out
        db.SaveChanges();

        var email = new CapturingEmail();
        await new NotificationDispatcher(db, email, new NotificationOptions(), NullLogger<NotificationDispatcher>.Instance, reminders)
            .DispatchPendingAsync(default);

        Assert.Empty(email.Sent);
        Assert.Equal(NotificationStatus.Sent, db.Notifications.Single().Status);
    }
}

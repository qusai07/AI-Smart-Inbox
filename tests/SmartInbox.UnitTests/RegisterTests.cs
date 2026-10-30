using Microsoft.EntityFrameworkCore;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.UnitTests;

/// <summary>
/// On SQLite (a real, transactional database in a temp file), not the in-memory provider: issuing relies on
/// all-or-nothing saves and concurrency checks, which the in-memory provider doesn't give.
/// </summary>
public class RegisterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"smartinbox-register-{Guid.NewGuid():N}.db");
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));
    private static readonly TestUser Clerk = new("Sara", Roles.Clerk);
    private static readonly TestUser Legal = new("Omar", Roles.Department, DepartmentId: 2);

    /// <summary>A separate context and connection on the same database: like a second request.</summary>
    private SmartInboxDbContext Db()
    {
        var db = new SmartInboxDbContext(new DbContextOptionsBuilder<SmartInboxDbContext>()
            .UseSqlite($"Data Source={_path};Default Timeout=30;Pooling=False").Options);
        db.Database.EnsureCreated();
        return db;
    }

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }

    private RegisterService Register(SmartInboxDbContext db, ICurrentUser? user = null, RegisterOptions? options = null) =>
        new(db, user ?? Clerk, options ?? new RegisterOptions(), _clock, new ReminderOptions { TimeZone = "Asia/Dubai" });

    private static IssueRequest Letter(int department = 2, string recipient = "Demo City Court") =>
        new(department, recipient, "Your notice ref. 2027/118", "بشأن إخطاركم رقم 2027/118");

    [Theory]
    [InlineData("{Prefix}/OUT/{Year}/{Seq:0000}", "MDS/OUT/2026/0042")]
    [InlineData("{Prefix}/{Dept}/{Year}/{Seq:000}", "MDS/LEG/2026/042")]
    [InlineData("OUT-{Year}-{Seq}", "OUT-2026-42")]
    public void Format_ReplacesTheTokens(string format, string expected) =>
        Assert.Equal(expected, OutgoingNumber.Format(format, "MDS", 2026, 42, "LEG"));

    [Fact]
    public async Task Issue_GivesConsecutiveNumbers_AndRecordsTheEntry()
    {
        using var db = Db();
        var first = await Register(db).IssueAsync(Letter(), default);
        var second = await Register(db).IssueAsync(Letter(recipient: "Nimbus Cloud"), default);

        Assert.Equal(RegisterService.Outcome.Ok, first.Outcome);
        Assert.Equal("MDS/OUT/2026/0001", first.Letter!.Number);
        Assert.Equal("MDS/OUT/2026/0002", second.Letter!.Number);
        Assert.Equal("Sara", first.Letter.IssuedBy);
        Assert.Equal(OutgoingStatus.Issued, first.Letter.Status);
        Assert.Equal("بشأن إخطاركم رقم 2027/118", first.Letter.SubjectAr);
    }

    [Fact]
    public async Task Sequence_RestartsEachYear_InTheOrganisationsTimeZone()
    {
        using var db = Db();
        await Register(db).IssueAsync(Letter(), default);
        await Register(db).IssueAsync(Letter(), default);

        _clock.Now = new DateTimeOffset(2026, 12, 31, 21, 0, 0, TimeSpan.Zero); // already 1 January 2027 in Dubai
        var newYear = await Register(db).IssueAsync(Letter(), default);

        Assert.Equal("MDS/OUT/2027/0001", newYear.Letter!.Number);
    }

    [Fact]
    public async Task PerDepartmentSequences_CountSeparately()
    {
        using var db = Db();
        var options = new RegisterOptions { Format = "{Prefix}/{Dept}/{Year}/{Seq:0000}", SequencePerDepartment = true };

        var legal1 = await Register(db, options: options).IssueAsync(Letter(2), default);
        var hr1 = await Register(db, options: options).IssueAsync(Letter(4), default);
        var legal2 = await Register(db, options: options).IssueAsync(Letter(2), default);

        Assert.Equal("MDS/LEG/2026/0001", legal1.Letter!.Number);
        Assert.Equal("MDS/HR/2026/0001", hr1.Letter!.Number);
        Assert.Equal("MDS/LEG/2026/0002", legal2.Letter!.Number);
    }

    [Fact]
    public async Task FiftyAtTheSameMoment_GetFiftyDistinctConsecutiveNumbers()
    {
        using (var db = Db()) await Register(db).IssueAsync(Letter(), default); // this year's counter exists

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            using var db = Db();
            return await Register(db).IssueAsync(Letter(), default);
        })));

        Assert.All(results, r => Assert.Equal(RegisterService.Outcome.Ok, r.Outcome));
        var sequences = results.Select(r => r.Letter!.Sequence).Order().ToList();
        Assert.Equal(Enumerable.Range(2, 50), sequences); // no duplicates, no gaps
        using var check = Db();
        Assert.Equal(51, check.OutgoingLetters.Count());
        Assert.Equal(52, check.RegisterCounters.Single().Next);
    }

    [Fact]
    public async Task AFailedSave_BurnsNoNumber()
    {
        using var db = Db();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Register(db).IssueAsync(Letter(), default, alongside: _ => throw new InvalidOperationException("approval failed")));

        using var fresh = Db();
        var next = await Register(fresh).IssueAsync(Letter(), default);
        Assert.Equal("MDS/OUT/2026/0001", next.Letter!.Number);
    }

    [Fact]
    public async Task Alongside_RunsWithTheIssuedNumber_InTheSameSave()
    {
        using var db = Db();
        var doc = new Document { FileName = "x", ContentType = "t", StoragePath = "x" };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        await Register(db).IssueAsync(Letter() with { DocumentId = doc.Id }, default, alongside: l => doc.HandlingNote = $"Replied with {l.Number}");

        using var check = Db();
        Assert.Equal("Replied with MDS/OUT/2026/0001", check.Documents.Single().HandlingNote);
        Assert.Equal(doc.Id, check.OutgoingLetters.Single().DocumentId);
    }

    [Fact]
    public async Task DepartmentStaff_IssueAndSeeOnlyTheirOwn()
    {
        using var db = Db();
        Assert.Equal(RegisterService.Outcome.Forbidden, (await Register(db, Legal).IssueAsync(Letter(4), default)).Outcome);
        await Register(db, Legal).IssueAsync(Letter(2), default);
        var hr = await Register(db).IssueAsync(Letter(4), default);

        var seen = await Register(db, Legal).ListAsync(new RegisterQuery(), default);
        Assert.All(seen.Items, l => Assert.Equal(2, l.DepartmentId));
        Assert.Null(await Register(db, Legal).GetAsync(hr.Letter!.Id, default));
        Assert.Equal(RegisterService.Outcome.NotFound, (await Register(db, Legal).CancelAsync(hr.Letter.Id, "x", default)).Outcome);
    }

    [Fact]
    public async Task UnknownDepartment_IsRefused()
    {
        using var db = Db();
        Assert.Equal(RegisterService.Outcome.UnknownDepartment, (await Register(db).IssueAsync(Letter(999), default)).Outcome);
    }

    [Fact]
    public async Task MarkSent_ThenCancel_KeepsTheNumberAndTheReason()
    {
        using var db = Db();
        var issued = (await Register(db).IssueAsync(Letter(), default)).Letter!;

        var sent = await Register(db).MarkSentAsync(issued.Id, new MarkSentRequest(SentVia.Post, Note: "Registered mail"), default);
        Assert.Equal(OutgoingStatus.Sent, sent.Letter!.Status);
        Assert.Equal(SentVia.Post, sent.Letter.SentVia);
        Assert.Equal(_clock.Now.UtcDateTime, sent.Letter.SentAt);

        var cancelled = await Register(db).CancelAsync(issued.Id, " Wrong recipient ", default);
        Assert.Equal(OutgoingStatus.Cancelled, cancelled.Letter!.Status);
        Assert.Equal("Wrong recipient", cancelled.Letter.CancelReason);
        Assert.Equal("Sara", cancelled.Letter.CancelledBy);
        Assert.Equal(RegisterService.Outcome.NotAllowedNow, (await Register(db).CancelAsync(issued.Id, "again", default)).Outcome);
        Assert.Equal(RegisterService.Outcome.NotAllowedNow, (await Register(db).MarkSentAsync(issued.Id, new MarkSentRequest(SentVia.Email), default)).Outcome);

        var next = await Register(db).IssueAsync(Letter(), default);
        Assert.Equal("MDS/OUT/2026/0002", next.Letter!.Number); // a cancelled number is never reused
    }

    [Fact]
    public async Task Search_ByNumberRecipientOrSubject_NewestFirst()
    {
        using var db = Db();
        await Register(db).IssueAsync(Letter(recipient: "Demo City Court"), default);
        await Register(db).IssueAsync(Letter(recipient: "Nimbus Cloud Services") with { SubjectEn = "Invoice NCS-88213" }, default);

        var byRecipient = await Register(db).ListAsync(new RegisterQuery(Q: "Nimbus"), default);
        var byNumber = await Register(db).ListAsync(new RegisterQuery(Q: "2026/0001"), default);
        var all = await Register(db).ListAsync(new RegisterQuery(Year: 2026), default);

        Assert.Equal("MDS/OUT/2026/0002", Assert.Single(byRecipient.Items).Number);
        Assert.Equal("MDS/OUT/2026/0001", Assert.Single(byNumber.Items).Number);
        Assert.Equal(["MDS/OUT/2026/0002", "MDS/OUT/2026/0001"], all.Items.Select(l => l.Number));
    }

    // ── Replies get their number on approval ─────────────────────────────────

    private sealed class FailingWebhooks : IWebhookPublisher
    {
        private static readonly Task<IReadOnlyList<WebhookDelivery>> None = Task.FromResult<IReadOnlyList<WebhookDelivery>>([]);
        public Task<IReadOnlyList<WebhookDelivery>> PublishAsync(string eventType, object data, IReadOnlyCollection<int> departmentIds, CancellationToken ct, string? dedupKey = null) =>
            eventType == WebhookEvents.RegisterIssued ? throw new InvalidOperationException("outbox down") : None;
        public Task<IReadOnlyList<WebhookDelivery>> PublishDocumentAsync(string eventType, Document d, string? actor, CancellationToken ct, int? previousDepartmentId = null, string? dedupKey = null) => None;
        public Task<IReadOnlyList<WebhookDelivery>> PublishDraftAsync(string eventType, ReplyDraft draft, Document d, string? actor, CancellationToken ct) => None;
    }

    private static async Task<Guid> ReadyDraftAsync(SmartInboxDbContext db, bool withMarker = true)
    {
        db.WebhookEndpoints.Add(new WebhookEndpoint { Url = "https://hooks.example.com", Secret = "p:x" });
        var doc = new Document { FileName = "notice.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Routed, AssignedDepartmentId = 2 };
        doc.Analysis = new DocumentAnalysis { DocumentId = doc.Id, Category = DocumentCategory.Legal, Sender = "Demo City Court" };
        var ours = withMarker ? ReplyDraftRules.NumberMarkerEn : "MDS/OUT/2026/0007";
        var draft = new ReplyDraft
        {
            DocumentId = doc.Id, Status = ReplyDraftStatus.Ready, Engine = "Templates",
            SubjectEn = "Your notice ref. 2027/118", BodyEn = $"Our ref: {ours}\n\nWe acknowledge receipt.",
            SubjectAr = "بشأن إخطاركم رقم 2027/118",
            BodyAr = $"رقمنا: {(withMarker ? ReplyDraftRules.NumberMarkerAr : ours)}\n\nنفيدكم بالاستلام.",
        };
        db.Documents.Add(doc);
        db.ReplyDrafts.Add(draft);
        await db.SaveChangesAsync();
        return draft.Id;
    }

    private ReplyDraftService Drafts(SmartInboxDbContext db, IWebhookPublisher? webhooks = null)
    {
        webhooks ??= new WebhookPublisher(db, new NotificationOptions { AppBaseUrl = "http://inbox.test" });
        return new ReplyDraftService(db, Clerk, new DraftingOptions(), new RecordingDraftQueue(), new RecordingNotifier(),
            webhooks: webhooks, register: new RegisterService(db, Clerk, new RegisterOptions(), _clock, null, webhooks, new NotificationOptions { AppBaseUrl = "http://inbox.test" }));
    }

    [Fact]
    public async Task Approve_IssuesTheNumber_IntoBothLanguages_AndTheRegister()
    {
        using var db = Db();
        var draftId = await ReadyDraftAsync(db);

        var result = await Drafts(db).ApproveAsync(draftId, default);

        Assert.Equal(ReplyDraftService.Outcome.Ok, result.Outcome);
        Assert.Equal("MDS/OUT/2026/0001", result.Draft!.OutgoingNumber);
        using var check = Db();
        var draft = check.ReplyDrafts.Single();
        Assert.Equal(ReplyDraftStatus.Approved, draft.Status);
        Assert.StartsWith("Our ref: MDS/OUT/2026/0001", draft.BodyEn);
        Assert.StartsWith("رقمنا: MDS/OUT/2026/0001", draft.BodyAr);
        Assert.False(ReplyDraftRules.HasNumberMarker(draft.BodyEn, draft.BodyAr));

        var entry = check.OutgoingLetters.Single();
        Assert.Equal(draft.Id, entry.ReplyDraftId);
        Assert.Equal(draft.DocumentId, entry.DocumentId);
        Assert.Equal("Demo City Court", entry.Recipient);
        Assert.Equal("Your notice ref. 2027/118", entry.SubjectEn);
        Assert.Equal(2, entry.DepartmentId);

        Assert.Contains(check.DocumentEvents, e => e.Type == DocumentEventType.DraftApproved);
        Assert.Contains(check.DocumentEvents, e => e.Type == DocumentEventType.OutgoingIssued && e.Note == "MDS/OUT/2026/0001");
        var issued = Assert.Single(check.WebhookDeliveries, w => w.EventType == WebhookEvents.RegisterIssued);
        Assert.Contains("\"number\":\"MDS/OUT/2026/0001\"", issued.Payload);
        Assert.DoesNotContain("acknowledge", issued.Payload); // thin
        Assert.Single(check.WebhookDeliveries, w => w.EventType == WebhookEvents.DraftApproved);
    }

    [Fact]
    public async Task Approve_ThatFails_BurnsNoNumber_AndLeavesTheDraftReady()
    {
        using (var db = Db())
        {
            var draftId = await ReadyDraftAsync(db);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Drafts(db, new FailingWebhooks()).ApproveAsync(draftId, default));
        }

        using var check = Db();
        Assert.Equal(ReplyDraftStatus.Ready, check.ReplyDrafts.Single().Status);
        Assert.Contains(ReplyDraftRules.NumberMarkerEn, check.ReplyDrafts.Single().BodyEn);
        Assert.Empty(check.OutgoingLetters);
        Assert.DoesNotContain(check.DocumentEvents, e => e.Type == DocumentEventType.DraftApproved);
        Assert.Equal("MDS/OUT/2026/0001", (await Register(check).IssueAsync(Letter(), default)).Letter!.Number);
    }

    [Fact]
    public async Task Approve_WithANumberTypedByHand_IssuesNone()
    {
        using var db = Db();
        var draftId = await ReadyDraftAsync(db, withMarker: false);

        var result = await Drafts(db).ApproveAsync(draftId, default);

        Assert.Equal(ReplyDraftStatus.Approved, result.Draft!.Status);
        Assert.Null(result.Draft.OutgoingNumber);
        using var check = Db();
        Assert.Empty(check.OutgoingLetters);
    }

    [Fact]
    public async Task DashboardAndListQueries_TranslateOnARealDatabase()
    {
        using var db = Db();
        var draftId = await ReadyDraftAsync(db);
        var service = new DocumentService(db, new MemoryStorage(), new NullQueue(), new RecordingNotifier(), Clerk);

        var stats = await service.GetStatsAsync(default);
        var list = await service.ListAsync(new DocumentQuery(AwaitingApproval: true), default);
        var duplicates = await service.ListAsync(new DocumentQuery(Duplicates: true, Related: false), default);

        Assert.Equal(1, stats.FollowUp!.AwaitingApproval);
        Assert.Equal(ReplyDraftStatus.Ready, Assert.Single(list.Items).Reply);
        Assert.Empty(duplicates.Items);
        Assert.NotEqual(Guid.Empty, draftId);
    }

    [Fact]
    public void Csv_QuotesEverything_AndDefusesFormulas()
    {
        var letter = new OutgoingLetterDto(Guid.NewGuid(), "MDS/OUT/2026/0001", 2026, 1, 2, "=HYPERLINK(\"http://x\")", "Re: \"notice\"", "بشأن",
            null, null, new DateTime(2026, 10, 30, 8, 0, 0), "Sara", OutgoingStatus.Issued, null, null, null, null, null, null);

        var text = System.Text.Encoding.UTF8.GetString(RegisterCsv.Write([letter], new Dictionary<int, string> { [2] = "Legal Affairs" }));

        var row = text.Split('\n')[1];
        Assert.StartsWith("﻿Number,", text);
        Assert.Contains("\"'=HYPERLINK(\"\"http://x\"\")\"", row);
        Assert.Contains("\"Re: \"\"notice\"\"\"", row);
        Assert.Contains("\"Legal Affairs\"", row);
        Assert.Contains("\"بشأن\"", row);
    }

    [Theory]
    [InlineData("Court", "Re: notice", null, true)]
    [InlineData("", "Re: notice", null, false)]
    [InlineData("Court", null, null, false)]
    [InlineData("Court", null, "بشأن", true)]
    public void Validator_NeedsARecipientAndASubject(string recipient, string? en, string? ar, bool valid) =>
        Assert.Equal(valid, new IssueRequestValidator().Validate(new IssueRequest(2, recipient, en, ar)).IsValid);
}

using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.UnitTests;

/// <summary>An in-memory mailbox: messages leave the "folder" when marked processed, like a move to Processed.</summary>
internal class FakeMailbox : IMailboxClient
{
    private readonly List<MailMessage> _folder = [];
    private uint _next = 1;

    public List<uint> Processed { get; } = [];
    public bool FailNextMove { get; set; }
    public Func<uint, Exception?> FetchFailure { get; set; } = _ => null;

    public uint Add(string? subject = "Letter", string? body = "Please find our letter.", string from = "sender@partner.gov",
        bool autoReply = false, string? messageId = null, bool listMessageId = true, params MailAttachment[] attachments)
    {
        var uid = _next++;
        var id = messageId ?? $"<m{uid}@partner.gov>";
        _folder.Add(new MailMessage(uid, id, "Partner Authority", from, subject, new DateTimeOffset(2027, 2, 10, 9, 0, 0, TimeSpan.Zero),
            body, autoReply, attachments, Encoding.UTF8.GetBytes($"Message-ID: {id}\r\nSubject: {subject}\r\n\r\n{body}")));
        if (!listMessageId) _hiddenIds.Add(uid);
        return uid;
    }

    /// <summary>A reply in a conversation: In-Reply-To and References point at earlier emails.</summary>
    public uint AddReply(string subject, string body, string inReplyTo, params string[] references)
    {
        var uid = Add(subject, body);
        var i = _folder.FindIndex(m => m.Uid == uid);
        _folder[i] = _folder[i] with { InReplyTo = inReplyTo, References = [.. references, inReplyTo] };
        return uid;
    }

    private readonly HashSet<uint> _hiddenIds = [];

    public Task<IReadOnlyList<MailboxItem>> ListPendingAsync(int max, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MailboxItem>>(_folder.Take(max).Select(m => new MailboxItem(m.Uid, _hiddenIds.Contains(m.Uid) ? null : m.MessageId)).ToList());

    public Task<MailMessage> FetchAsync(uint uid, CancellationToken ct) =>
        FetchFailure(uid) is { } ex ? Task.FromException<MailMessage>(ex) : Task.FromResult(_folder.Single(m => m.Uid == uid));

    public Task MarkProcessedAsync(uint uid, CancellationToken ct)
    {
        if (FailNextMove)
        {
            FailNextMove = false;
            throw new IOException("Server said no");
        }
        _folder.RemoveAll(m => m.Uid == uid);
        Processed.Add(uid);
        return Task.CompletedTask;
    }

    /// <summary>The same email arriving again (e.g. copied into another folder).</summary>
    public void Redeliver(uint uid, MailMessage original) => _folder.Add(original with { Uid = _next++ });

    public Task<MailboxTestResult> TestAsync(CancellationToken ct) => Task.FromResult(new MailboxTestResult(true, _folder.Count, "ok"));
}

public class MailboxImporterTests
{
    private readonly SmartInboxDbContext _db = TestDb.Create();
    private readonly FakeMailbox _mailbox = new();
    private readonly NullQueue _queue = new();
    private readonly MemoryStorage _storage = new();

    private MailboxImporter Importer(MailboxOptions? options = null) =>
        new(_db, _mailbox, _storage, new DocumentService(_db, _storage, _queue, new RecordingNotifier()),
            options ?? new MailboxOptions { Username = "correspondence@mds.demo" }, NullLogger<MailboxImporter>.Instance);

    private static MailAttachment Pdf(string name = "letter.pdf") => new(name, Encoding.ASCII.GetBytes("%PDF-1.4 fake"));

    [Fact]
    public async Task PlainEmail_BecomesOneDocument_TheEmailItself()
    {
        var uid = _mailbox.Add(subject: "Request for meeting", body: "We would like to meet next week.");

        var result = await Importer().RunAsync(default);

        Assert.Equal(1, result.Imported);
        var doc = Assert.Single(_db.Documents);
        Assert.Equal("Request for meeting.eml", doc.FileName);
        Assert.Equal("message/rfc822", doc.ContentType);
        Assert.Equal(DocumentSource.Email, doc.Source);
        Assert.Equal("Mailbox: correspondence@mds.demo", doc.UploadedBy);
        var email = Assert.Single(_db.InboundEmails);
        Assert.Equal(email.Id, doc.InboundEmailId);
        Assert.Equal(InboundEmailStatus.Imported, email.Status);
        Assert.Equal("We would like to meet next week.", email.BodyText);
        Assert.NotNull(email.StoragePath);
        Assert.Equal([doc.Id], _queue.Enqueued);
        Assert.Equal([uid], _mailbox.Processed);
        Assert.Contains(_db.DocumentEvents, e => e.DocumentId == doc.Id && e.Type == DocumentEventType.Uploaded && e.Note == "Received by email from sender@partner.gov");
    }

    [Fact]
    public async Task EmailWithLetters_BecomesOneDocumentPerLetter_WithTheEmailAsCover()
    {
        _mailbox.Add(subject: "Two letters", body: "Please see the attached.", attachments: [Pdf("notice.pdf"), Pdf("annex.pdf"), new("budget.xlsx", [1, 2, 3])]);

        await Importer().RunAsync(default);

        var docs = _db.Documents.OrderBy(d => d.FileName).ToList();
        Assert.Equal(["annex.pdf", "notice.pdf"], docs.Select(d => d.FileName));
        Assert.All(docs, d => Assert.Equal("application/pdf", d.ContentType));
        var email = _db.InboundEmails.Single();
        Assert.All(docs, d => Assert.Equal(email.Id, d.InboundEmailId));
        Assert.Equal(["budget.xlsx: unsupported type"], ReplyDraftRules.FromJson(email.SkippedAttachments));
        Assert.Equal(2, ReplyDraftRules.FromJson(email.DocumentIds).Count);
        Assert.Equal(2, _queue.Enqueued.Count);
    }

    [Fact]
    public async Task OnlyUnsupportedAttachments_ImportsTheEmailBody()
    {
        _mailbox.Add(subject: "Figures", body: "Figures attached.", attachments: [new MailAttachment("figures.xlsx", [1])]);

        await Importer().RunAsync(default);

        Assert.Equal("Figures.eml", Assert.Single(_db.Documents).FileName);
    }

    [Theory]
    [InlineData(true, null, MailboxRules.AutoReplySkipped)]
    [InlineData(false, "other@elsewhere.com", MailboxRules.SenderNotAllowed)]
    public async Task AutoRepliesAndUnknownSenders_AreSkipped_ButLeaveTheFolder(bool autoReply, string? from, string reason)
    {
        var uid = _mailbox.Add(autoReply: autoReply, from: from ?? "sender@partner.gov");

        var result = await Importer(new MailboxOptions { Username = "c@mds.demo", AllowedSenders = ["@partner.gov"] }).RunAsync(default);

        Assert.Equal(1, result.Skipped);
        Assert.Empty(_db.Documents);
        var email = _db.InboundEmails.Single();
        Assert.Equal(InboundEmailStatus.Skipped, email.Status);
        Assert.Equal(reason, email.Reason);
        Assert.Equal([uid], _mailbox.Processed);
    }

    [Fact]
    public async Task EmptyEmail_IsSkipped()
    {
        _mailbox.Add(body: " ");
        await Importer().RunAsync(default);
        Assert.Equal(MailboxRules.NothingToImport, _db.InboundEmails.Single().Reason);
    }

    [Theory]
    [InlineData("anyone@anywhere.com", new string[0], true)]
    [InlineData("a@partner.gov", new[] { "@partner.gov" }, true)]
    [InlineData("A@PARTNER.GOV", new[] { "@partner.gov" }, true)]
    [InlineData("a@partner.gov.evil.com", new[] { "@partner.gov" }, false)]
    [InlineData("boss@court.gov", new[] { "boss@court.gov" }, true)]
    [InlineData(null, new[] { "@partner.gov" }, false)]
    public void AllowedSenders(string? from, string[] allowed, bool ok) => Assert.Equal(ok, MailboxRules.IsAllowed(from, allowed));

    [Fact]
    public async Task TheSameEmailTwice_IsImportedOnce()
    {
        var uid = _mailbox.Add(attachments: [Pdf()]);
        var original = await _mailbox.FetchAsync(uid, default);
        await Importer().RunAsync(default);

        _mailbox.Redeliver(uid, original);
        var second = await Importer().RunAsync(default);

        Assert.Equal(1, second.AlreadyKnown);
        Assert.Single(_db.Documents);
        Assert.Equal(2, _mailbox.Processed.Count); // the copy still leaves the folder
    }

    [Fact]
    public async Task EmailWithoutAListedMessageId_IsStillRecognisedAfterDownload()
    {
        var uid = _mailbox.Add(messageId: "<hash-abc@derived>", listMessageId: false);
        var original = await _mailbox.FetchAsync(uid, default);
        await Importer().RunAsync(default);

        _mailbox.Redeliver(uid, original);
        await Importer().RunAsync(default);

        Assert.Single(_db.Documents);
    }

    [Fact]
    public async Task CrashBetweenSaveAndMove_NoDuplicate_MovedNextTime()
    {
        var uid = _mailbox.Add(attachments: [Pdf()]);
        _mailbox.FailNextMove = true;

        var first = await Importer().RunAsync(default);
        Assert.Equal(1, first.Imported);
        Assert.Empty(_mailbox.Processed); // still in the folder

        var second = await Importer().RunAsync(default);

        Assert.Equal(1, second.AlreadyKnown);
        Assert.Single(_db.Documents);
        Assert.Equal([uid], _mailbox.Processed);
    }

    [Fact]
    public async Task DownloadFailures_AreRetried_ThenLeftForAnAdmin()
    {
        var uid = _mailbox.Add();
        _mailbox.FetchFailure = _ => new IOException("connection reset");
        var options = new MailboxOptions { Username = "c@mds.demo", MaxAttempts = 3 };

        for (var i = 0; i < 3; i++) Assert.Equal(1, (await Importer(options).RunAsync(default)).Failed);
        var fourth = await Importer(options).RunAsync(default);

        Assert.Equal(1, fourth.AlreadyKnown); // given up: waits for an admin
        var email = _db.InboundEmails.Single();
        Assert.Equal(InboundEmailStatus.Failed, email.Status);
        Assert.Equal(3, email.Attempts);
        Assert.Equal("connection reset", email.Reason);
        Assert.Empty(_mailbox.Processed);
    }

    [Fact]
    public async Task ImportFailures_CountAttempts_AndLeaveNoHalfImportedEmail()
    {
        _mailbox.Add(attachments: [Pdf()]);
        var importer = new MailboxImporter(_db, _mailbox, new FailingStorage(_storage), new DocumentService(_db, new FailingStorage(_storage), _queue, new RecordingNotifier()),
            new MailboxOptions { Username = "c@mds.demo", MaxAttempts = 2 }, NullLogger<MailboxImporter>.Instance);

        await importer.RunAsync(default);
        await importer.RunAsync(default);
        var third = await importer.RunAsync(default);

        Assert.Equal(1, third.AlreadyKnown);
        Assert.Empty(_db.Documents);
        Assert.Empty(_queue.Enqueued);
        var email = _db.InboundEmails.Single();
        Assert.Equal(InboundEmailStatus.Failed, email.Status);
        Assert.Equal(2, email.Attempts);
    }

    [Fact]
    public async Task LargeBacklogs_AreWorkedThroughInBatches()
    {
        for (var i = 0; i < 7; i++) _mailbox.Add(subject: $"Letter {i}");
        var options = new MailboxOptions { Username = "c@mds.demo", MaxPerCheck = 3 };

        Assert.Equal(3, (await Importer(options).RunAsync(default)).Imported);
        Assert.Equal(3, (await Importer(options).RunAsync(default)).Imported);
        Assert.Equal(1, (await Importer(options).RunAsync(default)).Imported);
        Assert.Equal(7, _db.Documents.Count());
    }

    [Fact]
    public async Task Uploads_RecordTheirSource()
    {
        var web = await new DocumentService(_db, _storage, _queue, new RecordingNotifier(), new TestUser("Sara", Roles.Clerk)).UploadTextAsync("x", null, default);
        var api = await new DocumentService(_db, _storage, _queue, new RecordingNotifier(), new ApiKeyUser()).UploadTextAsync("y", null, default);

        Assert.Equal(DocumentSource.Upload, web.Source);
        Assert.Equal(DocumentSource.Api, api.Source);
    }

    private class ApiKeyUser : ICurrentUser
    {
        public string? Id => "apikey:7";
        public string? Name => "API key: ERP";
        public string? Role => Roles.Clerk;
        public int? DepartmentId => null;
    }

    /// <summary>Lets the .eml be stored, then fails on the letter itself.</summary>
    private class FailingStorage(IFileStorage inner) : IFileStorage
    {
        public Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct = default) =>
            fileName.EndsWith(".pdf") ? throw new IOException("disk full") : inner.SaveAsync(content, fileName, ct);

        public Stream Open(string storagePath) => inner.Open(storagePath);
    }
}

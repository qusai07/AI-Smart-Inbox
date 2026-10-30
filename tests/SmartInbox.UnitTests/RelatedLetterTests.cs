using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.UnitTests;

public class RelatedLetterTests
{
    private readonly SmartInboxDbContext _db = TestDb.Create();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));
    private static readonly string LongText = string.Join(' ', Enumerable.Repeat("The court notifies the Ministry of the hearing scheduled for 4 March 2027.", 5));

    private RelatedLetterFinder Finder(ThreadOptions? options = null) => new(_db, options ?? new ThreadOptions(), new RegisterOptions(), _clock);

    private async Task<IReadOnlyList<DocumentLink>> LinkAsync(Document doc)
    {
        var links = await Finder().LinkAsync(doc.Id, default);
        await _db.SaveChangesAsync();
        return links;
    }

    private Document Letter(string? text = null, string? reference = null, string? sender = null, int daysAgo = 0, string? hash = null, Guid? emailId = null)
    {
        var d = new Document
        {
            FileName = "letter.pdf", ContentType = "application/pdf", StoragePath = "x", Status = DocumentStatus.Analyzed,
            ExtractedText = text, ContentHash = hash, InboundEmailId = emailId,
            // As every analysed letter has them (the finder sets them after analysis).
            TextHash = LetterKeys.TextHash(text, new ThreadOptions().MinTextForDuplicate), ReferenceKey = LetterKeys.Reference(reference),
            UploadedAt = _clock.GetUtcNow().UtcDateTime.AddDays(-daysAgo),
        };
        d.Analysis = new DocumentAnalysis { DocumentId = d.Id, Category = DocumentCategory.Legal, ReferenceNumber = reference, Sender = sender };
        _db.Documents.Add(d);
        _db.SaveChanges();
        return d;
    }

    // ── Keys ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2027/118", "2027/118")]
    [InlineData("Ref. No. ٢٠٢٧ / ١١٨", "2027/118")]
    [InlineData("رقم: 2027-118", "2027/118")]
    [InlineData("Your ref: MDS/OUT/2026/0042", "MDS/OUT/2026/0042")]
    [InlineData("INV NCS-88213", "INV/NCS/88213")]
    [InlineData("1", null)]
    [InlineData("N/A", null)]
    [InlineData(null, null)]
    public void Reference_IsNormalised_AndTooGenericIsNull(string? input, string? expected) =>
        Assert.Equal(expected, LetterKeys.Reference(input));

    [Theory]
    [InlineData("Demo City Court", "Demo City Court of First Instance", true)]
    [InlineData("DEMO CITY COURT.", "demo city court", true)]
    [InlineData("المحكمة الابتدائية", "محكمة ابتدائية", true)]
    [InlineData("Ministry of Finance", "Ministry of Health", false)]
    [InlineData("Court", "Court of Appeal", false)]       // one word isn't enough to call it the same sender
    [InlineData("legal@court.gov", "registry@court.gov", true)]
    [InlineData("ahmed@gmail.com", "sara@gmail.com", false)] // webmail says nothing about the sender
    public void SameSender(string a, string b, bool expected) =>
        Assert.Equal(expected, LetterKeys.SameSender(
            (LetterKeys.SenderName(a), LetterKeys.Domain(a)), (LetterKeys.SenderName(b), LetterKeys.Domain(b))));

    [Theory]
    [InlineData("{Prefix}/OUT/{Year}/{Seq:0000}", "Reply to your letter MDS/OUT/2026/0042 of 1 Nov.", "MDS/OUT/2026/0042")]
    [InlineData("{Prefix}/OUT/{Year}/{Seq:0000}", "إشارة إلى كتابكم رقم mds/out/2026/0042", "mds/out/2026/0042")]
    [InlineData("{Prefix}/{Dept}/{Year}/{Seq:000}", "Ref MDS/LEG/2026/042.", "MDS/LEG/2026/042")]
    [InlineData("{Prefix}/OUT/{Year}/{Seq:0000}", "XMDS/OUT/2026/0042 is someone else's number", null)]
    public void OurNumbers_AreFoundInText(string format, string text, string? expected)
    {
        var m = LetterKeys.OurNumbers(new RegisterOptions { Format = format }).Match(text);
        Assert.Equal(expected, m.Success ? m.Value : null);
    }

    [Fact]
    public void ArabicDigits_BecomeAscii() => Assert.Equal("MDS/OUT/2026/0042", LetterKeys.AsciiDigits("MDS/OUT/٢٠٢٦/٠٠٤٢"));

    // ── Linking ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SameFile_IsADuplicate_PointingFromTheNewerLetter()
    {
        var first = Letter(hash: "abc", daysAgo: 3);
        var again = Letter(hash: "abc");

        var link = Assert.Single(await LinkAsync(again));

        Assert.Equal(LinkKind.Duplicate, link.Kind);
        Assert.Equal("Same file", link.Evidence);
        Assert.Equal(again.Id, link.DocumentId);
        Assert.Equal(first.Id, link.RelatedDocumentId);
    }

    [Fact]
    public async Task SameText_InADifferentFile_IsADuplicate_ButShortTextsAreNot()
    {
        var scanned = Letter(text: LongText, daysAgo: 1);
        var typed = Letter(text: "  " + LongText.Replace(" ", "\n  ").ToUpperInvariant());
        Letter(text: "Thank you.", daysAgo: 2);
        var shortAgain = Letter(text: "Thank you.");

        Assert.Equal("Same text", Assert.Single(await LinkAsync(typed)).Evidence);
        Assert.Equal(scanned.Id, _db.DocumentLinks.Single().RelatedDocumentId);
        Assert.Empty(await LinkAsync(shortAgain));
    }

    [Fact]
    public async Task SameReference_FromTheSameSender_IsAFollowUp()
    {
        var notice = Letter(reference: "2027/118", sender: "Demo City Court of First Instance", daysAgo: 20);
        var reminder = Letter(reference: "Ref. 2027 / 118", sender: "Demo City Court");

        var link = Assert.Single(await LinkAsync(reminder));

        Assert.Equal(LinkKind.FollowUp, link.Kind);
        Assert.Equal(notice.Id, link.RelatedDocumentId);
        Assert.Equal("Same reference Ref. 2027 / 118 from Demo City Court", link.Evidence);
        Assert.Equal("2027/118", reminder.ReferenceKey);
    }

    [Fact]
    public async Task SameReference_FromAnotherSender_IsNotLinked()
    {
        Letter(reference: "1/2026", sender: "Ministry of Finance", daysAgo: 5);
        var other = Letter(reference: "1/2026", sender: "Ministry of Health");

        Assert.Empty(await LinkAsync(other));
    }

    [Fact]
    public async Task SameReference_SameEmailDomain_IsAFollowUp()
    {
        var firstEmail = new InboundEmail { MessageId = "<1@court.gov>", Mailbox = "x", FromAddress = "registry@court.gov" };
        var secondEmail = new InboundEmail { MessageId = "<2@court.gov>", Mailbox = "x", FromAddress = "judge.office@court.gov" };
        _db.InboundEmails.AddRange(firstEmail, secondEmail);
        Letter(reference: "2027/118", sender: "Registry", daysAgo: 4, emailId: firstEmail.Id);
        var second = Letter(reference: "2027/118", sender: "Office of the Judge", emailId: secondEmail.Id);

        Assert.Equal(LinkKind.FollowUp, Assert.Single(await LinkAsync(second)).Kind);
    }

    [Fact]
    public async Task QuotingOurNumber_LinksToOurLetter_AndTheLetterItAnswered()
    {
        var incoming = Letter(reference: "2027/118", sender: "Demo City Court", daysAgo: 30);
        var ours = new OutgoingLetter { Number = "MDS/OUT/2026/0042", Year = 2026, Sequence = 42, DepartmentId = 2, DocumentId = incoming.Id };
        var manual = new OutgoingLetter { Number = "MDS/OUT/2026/0050", Year = 2026, Sequence = 50, DepartmentId = 2 };
        _db.OutgoingLetters.AddRange(ours, manual);
        var answer = Letter(text: "With reference to your letter MDS/OUT/٢٠٢٦/٠٠٤٢ and MDS/OUT/2026/0050, and MDS/OUT/2026/0999.");

        var links = await LinkAsync(answer);

        Assert.Equal(2, links.Count);
        var toIncoming = links.Single(l => l.RelatedDocumentId == incoming.Id);
        Assert.Equal(LinkKind.RepliesToOurLetter, toIncoming.Kind);
        Assert.Equal(ours.Id, toIncoming.OutgoingLetterId);
        Assert.Equal("Quotes our letter MDS/OUT/2026/0042", toIncoming.Evidence);
        var toManual = links.Single(l => l.RelatedDocumentId == null);
        Assert.Equal(manual.Id, toManual.OutgoingLetterId); // registered by hand: no incoming letter, still linked to the register
    }

    [Fact]
    public async Task ADuplicateThatIsAlsoAFollowUp_IsLinkedOnce_AsADuplicate()
    {
        Letter(hash: "same", reference: "2027/118", sender: "Demo City Court", daysAgo: 1);
        var again = Letter(hash: "same", reference: "2027/118", sender: "Demo City Court");

        Assert.Equal(LinkKind.Duplicate, Assert.Single(await LinkAsync(again)).Kind);
    }

    [Fact]
    public async Task Relinking_AddsNothingTwice_AndARemovedLinkStaysRemoved()
    {
        var first = Letter(hash: "abc", daysAgo: 3);
        var again = Letter(hash: "abc");
        var link = Assert.Single(await LinkAsync(again));
        Assert.Empty(await LinkAsync(again));
        Assert.Empty(await LinkAsync(first)); // re-analysing the earlier letter finds the same pair, already linked

        link.RemovedAt = DateTime.UtcNow;
        link.RemovedBy = "Sara";
        await _db.SaveChangesAsync();
        Assert.Empty(await LinkAsync(again));
        Assert.Single(_db.DocumentLinks);
    }

    [Fact]
    public async Task LettersOutsideTheLookback_AreNotLinked()
    {
        Letter(hash: "abc", daysAgo: 400);
        var again = Letter(hash: "abc");

        Assert.Empty(await LinkAsync(again));
        Assert.Single(await Finder(new ThreadOptions { LookbackDays = 500 }).LinkAsync(again.Id, default));
    }

    [Fact]
    public async Task TurnedOff_LinksNothing()
    {
        Letter(hash: "abc", daysAgo: 1);
        var again = Letter(hash: "abc");

        Assert.Empty(await Finder(new ThreadOptions { Enabled = false }).LinkAsync(again.Id, default));
    }

    // ── After analysis ───────────────────────────────────────────────────────

    private static readonly TestUser Clerk = new("Sara", Roles.Clerk);
    private readonly MemoryStorage _storage = new();

    /// <summary>Uploads and analyses a letter, like the real pipeline (with related-letter matching and webhooks).</summary>
    private async Task<Document> AnalysedAsync(string text, int department, double confidence, string sender = "Demo City Court", string reference = "2027/118")
    {
        var service = new DocumentService(_db, _storage, new NullQueue(), new RecordingNotifier(), Clerk);
        var doc = await service.UploadTextAsync(text, "Sara", default);
        var ai = new FakeAi(_ => FakeAi.Result(department, confidence) with { Sender = sender, ReferenceNumber = reference });
        await new DocumentProcessor(_db, _storage, [new SmartInbox.Infrastructure.TextExtraction.PlainTextExtractor()], ai, new RecordingNotifier(),
            NullLogger<DocumentProcessor>.Instance, new WebhookPublisher(_db, new NotificationOptions { AppBaseUrl = "http://inbox.test" }),
            new RelatedLetterFinder(_db, new ThreadOptions(), new RegisterOptions(), TimeProvider.System),
            new NotificationOptions { AppBaseUrl = "http://inbox.test" }).ProcessAsync(doc.Id, default);
        return doc;
    }

    private async Task RouteAsync(Document doc, int department) =>
        await new DocumentService(_db, _storage, new NullQueue(), new RecordingNotifier(), Clerk).RouteAsync(doc.Id, new RouteRequest(department, null), null, default);

    [Fact]
    public async Task AFollowUp_WhileTheAiIsUnsure_IsSuggestedToTheDepartmentThatHasTheCase()
    {
        _db.WebhookEndpoints.Add(new WebhookEndpoint { Url = "https://hooks.example.com", Secret = "p:x" });
        var notice = await AnalysedAsync("Hearing notice for case 2027/118.", department: 2, confidence: 0.9);
        await RouteAsync(notice, 2);

        var reminder = await AnalysedAsync("Reminder: please submit the memorandum.", department: 5, confidence: 0.4);

        var a = reminder.Analysis!;
        Assert.Equal(2, a.SuggestedDepartmentId);
        Assert.Equal(DocumentProcessor.CaseConfidence, a.Confidence);
        Assert.StartsWith("Earlier letters in this case went to Legal Affairs.", a.Reasoning);
        Assert.Equal(DocumentStatus.Analyzed, reminder.Status); // ready to route, no longer "needs review"
        Assert.Contains(_db.DocumentEvents, e => e.DocumentId == reminder.Id && e.Type == DocumentEventType.Linked
                                                 && e.Note == "Same reference 2027/118 from Demo City Court");
        var hook = Assert.Single(_db.WebhookDeliveries, w => w.EventType == WebhookEvents.DocumentLinked);
        Assert.Contains("\"kind\":\"FollowUp\"", hook.Payload);
        Assert.DoesNotContain("Demo City Court", hook.Payload); // thin: no content from the letters
    }

    [Fact]
    public async Task AConfidentAnalysis_KeepsItsDepartment_AndMentionsTheCase()
    {
        var notice = await AnalysedAsync("Hearing notice for case 2027/118.", department: 2, confidence: 0.9);
        await RouteAsync(notice, 2);

        var invoice = await AnalysedAsync("Invoice for the court fees.", department: 3, confidence: 0.9);

        Assert.Equal(3, invoice.Analysis!.SuggestedDepartmentId);
        Assert.EndsWith("Earlier letters in this case went to Legal Affairs.", invoice.Analysis.Reasoning);
    }

    [Fact]
    public async Task Reanalysing_DoesNotLinkOrLogTwice()
    {
        await AnalysedAsync(LongText, department: 2, confidence: 0.9, reference: "A-1");
        var again = await AnalysedAsync(LongText, department: 2, confidence: 0.9, reference: "A-1");
        var processor = new DocumentProcessor(_db, _storage, [new SmartInbox.Infrastructure.TextExtraction.PlainTextExtractor()],
            new FakeAi(_ => FakeAi.Result(2, 0.9)), new RecordingNotifier(), NullLogger<DocumentProcessor>.Instance,
            related: new RelatedLetterFinder(_db, new ThreadOptions(), new RegisterOptions(), TimeProvider.System));

        await processor.ProcessAsync(again.Id, default);

        Assert.Equal(LinkKind.Duplicate, Assert.Single(_db.DocumentLinks).Kind);
        Assert.Single(_db.DocumentEvents, e => e.Type == DocumentEventType.Linked);
    }

    [Fact]
    public async Task EarlierLetters_AreBackfilled_OldestFirst_Quietly()
    {
        foreach (var daysAgo in new[] { 3, 1 })
        {
            var path = await _storage.SaveAsync(new MemoryStream("Same scanned letter"u8.ToArray()), "scan.pdf");
            _db.Documents.Add(new Document { FileName = "scan.pdf", ContentType = "application/pdf", StoragePath = path, UploadedAt = DateTime.UtcNow.AddDays(-daysAgo) });
        }
        _db.Documents.Add(new Document { FileName = "lost.pdf", ContentType = "application/pdf", StoragePath = "missing", UploadedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var count = await new RelatedLetterBackfill(_db, _storage, new RelatedLetterFinder(_db, new ThreadOptions(), new RegisterOptions(), TimeProvider.System),
            new ThreadOptions(), TimeProvider.System, NullLogger<RelatedLetterBackfill>.Instance).RunAsync(default, batchSize: 2);

        Assert.Equal(3, count);
        var link = Assert.Single(_db.DocumentLinks);
        Assert.Equal(LinkKind.Duplicate, link.Kind);
        var newer = _db.Documents.Where(d => d.FileName == "scan.pdf").OrderByDescending(d => d.UploadedAt).First();
        Assert.Equal(newer.Id, link.DocumentId);
        Assert.Null(_db.Documents.Single(d => d.FileName == "lost.pdf").ContentHash);
        Assert.Empty(_db.DocumentEvents.Where(e => e.Type == DocumentEventType.Linked));
    }

    [Fact]
    public async Task People_SeeOnlyRelatedLettersTheyMaySee_AndCanRemoveAWrongLink()
    {
        var legalDoc = Letter(hash: "abc", daysAgo: 2);
        var hrDoc = Letter(hash: "abc", daysAgo: 1);
        var mine = Letter(hash: "abc");
        legalDoc.AssignedDepartmentId = 2;
        hrDoc.AssignedDepartmentId = 4;
        mine.AssignedDepartmentId = 2;
        await _db.SaveChangesAsync();
        await LinkAsync(mine);

        var legal = new RelatedLetterService(_db, new TestUser("Omar", Roles.Department, DepartmentId: 2));
        var seen = await legal.ListAsync(mine.Id, default);
        Assert.Equal(legalDoc.Id, Assert.Single(seen!).DocumentId); // the HR letter isn't shown to Legal
        Assert.Equal(2, (await new RelatedLetterService(_db, Clerk).ListAsync(mine.Id, default))!.Count);
        Assert.Null(await legal.ListAsync(hrDoc.Id, default));

        Assert.Equal(RelatedLetterService.RemoveOutcome.Removed, await legal.RemoveAsync(mine.Id, seen![0].LinkId, default));
        Assert.Empty((await legal.ListAsync(mine.Id, default))!);
        Assert.Equal(RelatedLetterService.RemoveOutcome.NotFound, await legal.RemoveAsync(mine.Id, seen[0].LinkId, default));
        Assert.Empty(await Finder().LinkAsync(mine.Id, default)); // stays removed
        Assert.Contains(_db.DocumentEvents, e => e.DocumentId == mine.Id && e.Note == "Link removed: Same file" && e.Actor == "Omar");
    }

    [Fact]
    public async Task TheInbox_FlagsRelatedLetters_AndPossibleDuplicates()
    {
        var first = Letter(hash: "abc", daysAgo: 1);
        var again = Letter(hash: "abc");
        Letter(hash: "other");
        await LinkAsync(again);
        var service = new DocumentService(_db, _storage, new NullQueue(), new RecordingNotifier(), Clerk);

        var related = (await service.ListAsync(new DocumentQuery(Related: true), default)).Items;

        Assert.Equal([again.Id, first.Id], related.Select(d => d.Id));
        Assert.True(related.Single(d => d.Id == again.Id).PossibleDuplicate);  // the second copy is the suspect
        Assert.False(related.Single(d => d.Id == first.Id).PossibleDuplicate);
        Assert.Single((await service.ListAsync(new DocumentQuery(Related: false), default)).Items);
    }

    // ── Upload and email ─────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_StoresTheFileHash()
    {
        var service = new DocumentService(_db, new MemoryStorage(), new NullQueue(), new RecordingNotifier());
        var a = await service.UploadTextAsync("The same letter.", "Sara", default);
        var b = await service.UploadTextAsync("The same letter.", "Sara", default);
        var c = await service.UploadTextAsync("Another letter.", "Sara", default);

        Assert.Equal(64, a.ContentHash!.Length);
        Assert.Equal(a.ContentHash, b.ContentHash);
        Assert.NotEqual(a.ContentHash, c.ContentHash);
    }

    [Fact]
    public async Task EmailReply_IsLinkedToTheEarlierEmailsLetter()
    {
        var mailbox = new FakeMailbox();
        var storage = new MemoryStorage();
        var importer = new MailboxImporter(_db, mailbox, storage, new DocumentService(_db, storage, new NullQueue(), new RecordingNotifier()),
            new MailboxOptions { Username = "correspondence@mds.demo" }, NullLogger<MailboxImporter>.Instance);
        mailbox.Add(subject: "Meeting request", body: "Could we meet next week?", messageId: "<first@partner.gov>");
        await importer.RunAsync(default);
        mailbox.AddReply("Re: Meeting request", "Any news?", "<first@partner.gov>", "<older@partner.gov>");
        await importer.RunAsync(default);

        var reply = _db.InboundEmails.Single(e => e.Subject == "Re: Meeting request");
        Assert.Equal("<first@partner.gov>", reply.InReplyTo);
        Assert.Equal("<older@partner.gov> <first@partner.gov>", reply.References);

        var firstDoc = _db.Documents.Single(d => d.FileName == "Meeting request.eml");
        var replyDoc = _db.Documents.Single(d => d.InboundEmailId == reply.Id);
        var link = Assert.Single(await LinkAsync(replyDoc));
        Assert.Equal(LinkKind.EmailThread, link.Kind);
        Assert.Equal(firstDoc.Id, link.RelatedDocumentId);
        Assert.Equal("Reply to the email \"Meeting request\"", link.Evidence);
    }
}

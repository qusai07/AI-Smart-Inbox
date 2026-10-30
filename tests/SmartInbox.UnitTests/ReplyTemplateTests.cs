using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.UnitTests;

public class ReplyTemplateTests
{
    private static readonly ReplyTemplateCatalog Catalog = new();
    private static readonly TestClock Clock = new(new DateTimeOffset(2026, 10, 30, 6, 0, 0, TimeSpan.Zero));

    private static Department Legal => DepartmentSeed.All.Single(d => d.Id == 2);

    private static DocumentAnalysis Analysis(DocumentCategory category = DocumentCategory.Legal) => new()
    {
        Category = category, Priority = Priority.High, Sender = "Demo City Court of First Instance",
        ReferenceNumber = "2027/118", DocumentDate = new DateOnly(2026, 10, 20), Deadline = new DateOnly(2027, 3, 4),
    };

    private static Task<DraftResult> Draft(DocumentAnalysis? analysis, string? templateKey = null, Department? department = null) =>
        new TemplateReplyDrafter(Catalog, new DraftingOptions(), Clock, new ReminderOptions { TimeZone = "Asia/Dubai" })
            .DraftAsync(new DraftInput(new Document { FileName = "x", ContentType = "text/plain", StoragePath = "x" }, analysis, department ?? Legal, templateKey, null), default);

    [Fact]
    public void Catalog_LoadsEveryShippedTemplate()
    {
        Assert.Equal(9, Catalog.All.Count);
        Assert.Equal(ReplyTemplateCatalog.Fallback, Catalog.List().Last().Key);
        Assert.All(Catalog.All, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.NameAr));
            Assert.Contains("تحية طيبة وبعد", t.BodyAr);
            Assert.Contains("Dear Sir/Madam", t.BodyEn);
        });
    }

    [Theory]
    [InlineData(DocumentCategory.Legal, "legal-acknowledgement")]
    [InlineData(DocumentCategory.Invoice, "invoice-received")]
    [InlineData(DocumentCategory.Complaint, "complaint-acknowledgement")]
    [InlineData(DocumentCategory.Invitation, "accept-invitation")]
    [InlineData(DocumentCategory.Circular, "circular-acknowledgement")]
    [InlineData(DocumentCategory.Request, "acknowledge-receipt")]
    [InlineData(DocumentCategory.Other, "general-reply")]
    public void Catalog_PicksTheTemplateByCategory(DocumentCategory category, string key) =>
        Assert.Equal(key, Catalog.ForCategory(category).Key);

    [Fact]
    public void Catalog_RejectsTemplatesWithUnknownSlots()
    {
        var typo = new ReplyTemplate(ReplyTemplateCatalog.Fallback, "x", "x", [], "Re {TheirRefrence}", "x", "x", "x", []);
        var ex = Assert.Throws<InvalidOperationException>(() => new ReplyTemplateCatalog([typo]));
        Assert.Contains("TheirRefrence", ex.Message);
    }

    [Fact]
    public async Task LegalNotice_IsFullyFilledInBothLanguages_ExceptOurReference()
    {
        var d = await Draft(Analysis());

        Assert.Equal("legal-acknowledgement", d.TemplateKey);
        Assert.Equal("Templates", d.Engine);
        Assert.Equal("Your notice ref. 2027/118", d.SubjectEn);
        Assert.Contains("dated 20 October 2026", d.BodyEn);
        Assert.Contains("referred to the Legal Affairs department, which will take the necessary action before 4 March 2027.", d.BodyEn);
        Assert.Contains("Demo City Court of First Instance", d.BodyEn);
        Assert.Contains("Director, Legal Affairs", d.BodyEn);
        Assert.Contains("Ministry of Digital Services – Demo", d.BodyEn);

        Assert.Equal("بشأن إخطاركم رقم 2027/118", d.SubjectAr);
        Assert.Contains("المؤرخ في 20 أكتوبر 2026", d.BodyAr);
        Assert.Contains("إلى إدارة الشؤون القانونية لاتخاذ الإجراءات اللازمة قبل 4 مارس 2027", d.BodyAr);
        Assert.Contains("مدير إدارة الشؤون القانونية", d.BodyAr);

        // Only our own outgoing number is unknown (the register isn't built yet).
        Assert.Equal(["Our outgoing reference number"], d.MissingInfo);
        Assert.Equal(1, ReplyDraftRules.CountPlaceholders(d.BodyEn));
        Assert.Equal(1, ReplyDraftRules.CountPlaceholders(d.BodyAr));
        Assert.Equal(["The department will act before the deadline stated"], d.Commitments);
    }

    [Fact]
    public async Task UnknownFacts_BecomePlaceholders_NeverGuesses()
    {
        var d = await Draft(new DocumentAnalysis { Category = DocumentCategory.Legal });

        Assert.Contains("Their reference number", d.MissingInfo);
        Assert.Contains("Date of their letter", d.MissingInfo);
        Assert.Contains("Deadline", d.MissingInfo);
        Assert.Contains("Recipient (who the reply is addressed to)", d.MissingInfo);
        Assert.Equal("Your notice ref. [●]", d.SubjectEn);
        Assert.Contains("before [●].", d.BodyEn);
    }

    [Fact]
    public async Task FreeGaps_AreListedWithTheirLabel()
    {
        var d = await Draft(Analysis(DocumentCategory.Complaint));

        Assert.Contains("Date by which we will reply", d.MissingInfo);
        Assert.Contains("no later than [●].", d.BodyEn);
        Assert.Contains("في موعد أقصاه [●].", d.BodyAr);
    }

    [Fact]
    public async Task RequestedTemplate_WinsOverTheCategory()
    {
        var d = await Draft(Analysis(DocumentCategory.Invitation), "decline-invitation");
        Assert.Equal("decline-invitation", d.TemplateKey);
        Assert.Contains("unable to take part", d.BodyEn);
    }

    [Fact]
    public async Task OptionalSignatoryName_LineDisappearsWhenEmpty_AndAppearsWhenSet()
    {
        var without = await Draft(Analysis());
        Assert.Contains("Yours sincerely,\n\nDirector, Legal Affairs\nLegal Affairs", without.BodyEn);

        var legal = Legal;
        legal.SignatoryNameEn = "A. Example";
        legal.SignatoryNameAr = "أ. مثال";
        var with = await Draft(Analysis(), department: legal);
        Assert.Contains("Yours sincerely,\n\nA. Example\nDirector, Legal Affairs", with.BodyEn);
        Assert.Contains("أ. مثال\nمدير إدارة الشؤون القانونية", with.BodyAr);
    }

    [Fact]
    public async Task MinistersOffice_ReadsNaturally()
    {
        var office = DepartmentSeed.All.Single(d => d.Id == 1);
        var d = await Draft(Analysis(DocumentCategory.Request), department: office);
        Assert.Contains("referred to the Minister's Office, which", d.BodyEn);
        Assert.Contains("إلى مكتب الوزير لدراسته", d.BodyAr);
    }

    [Fact]
    public void Dates_AreFormattedForLetters()
    {
        var date = new DateOnly(2026, 10, 30);
        Assert.Equal("30 October 2026", LetterDates.En(date));
        Assert.Equal("30 أكتوبر 2026", LetterDates.Ar(date));
        var withHijri = LetterDates.ArWithHijri(date);
        Assert.StartsWith("30 أكتوبر 2026م الموافق ", withHijri);
        Assert.EndsWith("1448هـ", withHijri);
    }

    [Fact]
    public async Task Today_UsesTheOrganisationsTimeZone()
    {
        // 22:30 UTC on 29 October is already 30 October in Dubai.
        var late = new TestClock(new DateTimeOffset(2026, 10, 29, 22, 30, 0, TimeSpan.Zero));
        var d = await new TemplateReplyDrafter(Catalog, new DraftingOptions(), late, new ReminderOptions { TimeZone = "Asia/Dubai" })
            .DraftAsync(new DraftInput(new Document { FileName = "x", ContentType = "t", StoragePath = "x" }, Analysis(), Legal, null, null), default);
        Assert.Contains("Date: 30 October 2026", d.BodyEn);
    }
}

public class ReplyDraftGeneratorTests
{
    private readonly SmartInboxDbContext _db = TestDb.Create();
    private readonly RecordingDraftQueue _queue = new();
    private static readonly TestUser Clerk = new("Sara", Roles.Clerk);

    private ReplyDraftGenerator Generator(IReplyDrafter? drafter = null) =>
        new(_db, drafter ?? new TemplateReplyDrafter(new ReplyTemplateCatalog(), new DraftingOptions(), TimeProvider.System),
            new RecordingNotifier(), NullLogger<ReplyDraftGenerator>.Instance);

    private async Task<Guid> RoutedDocumentAsync()
    {
        var d = new Document { FileName = "notice.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed };
        d.Analysis = new DocumentAnalysis
        {
            DocumentId = d.Id, Category = DocumentCategory.Legal, Sender = "Demo Court", ReferenceNumber = "2027/118",
            DocumentDate = new DateOnly(2026, 10, 20), Deadline = new DateOnly(2027, 3, 4), SuggestedDepartmentId = 2, Confidence = 0.9,
        };
        _db.Documents.Add(d);
        await _db.SaveChangesAsync();
        await new DocumentService(_db, new MemoryStorage(), new NullQueue(), new RecordingNotifier(), Clerk).RouteAsync(d.Id, new RouteRequest(2, null), null, default);
        return d.Id;
    }

    private Task<ReplyDraftService.Result> RequestAsync(Guid doc, string? template = null) =>
        new ReplyDraftService(_db, Clerk, new DraftingOptions(), _queue, new RecordingNotifier()).RequestAsync(doc, new RequestDraftRequest(template), default);

    [Fact]
    public async Task Generate_FillsTheDraft_AndMarksItReady()
    {
        var doc = await RoutedDocumentAsync();
        var draft = (await RequestAsync(doc)).Draft!;

        await Generator().GenerateAsync(draft.Id, default);

        var d = _db.ReplyDrafts.Single();
        Assert.Equal(ReplyDraftStatus.Ready, d.Status);
        Assert.Equal("legal-acknowledgement", d.TemplateKey);
        Assert.Equal("Templates", d.Engine);
        Assert.Contains("2027/118", d.SubjectEn);
        Assert.Contains("الشؤون القانونية", d.BodyAr);
        Assert.Equal(["Our outgoing reference number"], ReplyDraftRules.FromJson(d.MissingInfo));
        Assert.Contains(_db.DocumentEvents, e => e.Type == DocumentEventType.DraftReady && e.Actor == "Templates" && e.Note!.Contains("1 item(s) to fill in"));
    }

    [Fact]
    public async Task Generate_ThenFillTheGap_ThenApprove()
    {
        var doc = await RoutedDocumentAsync();
        var draft = (await RequestAsync(doc)).Draft!;
        await Generator().GenerateAsync(draft.Id, default);
        var ready = _db.ReplyDrafts.Single();
        var service = new ReplyDraftService(_db, Clerk, new DraftingOptions(), _queue, new RecordingNotifier());

        var edited = await service.UpdateAsync(ready.Id, new UpdateDraftRequest(
            ready.SubjectEn!, ready.BodyEn!.Replace("[●]", "MDS/OUT/2026/0042"),
            ready.SubjectAr!, ready.BodyAr!.Replace("[●]", "MDS/OUT/2026/0042")), default);
        var approved = await service.ApproveAsync(edited.Draft!.Id, default);

        Assert.Equal(ReplyDraftService.Outcome.Ok, approved.Outcome);
        Assert.Equal(ReplyDraftStatus.Approved, approved.Draft!.Status);
    }

    [Fact]
    public async Task Generate_WhenTheDrafterThrows_MarksItFailedWithAReadableReason()
    {
        var doc = await RoutedDocumentAsync();
        var draft = (await RequestAsync(doc)).Draft!;

        await Generator(new ThrowingDrafter()).GenerateAsync(draft.Id, default);

        var d = _db.ReplyDrafts.Single();
        Assert.Equal(ReplyDraftStatus.Failed, d.Status);
        Assert.Equal("The draft could not be prepared. Try again.", d.Error);
        Assert.Contains(_db.DocumentEvents, e => e.Type == DocumentEventType.DraftFailed);

        // A failed draft doesn't block a new request.
        Assert.Equal(ReplyDraftService.Outcome.Ok, (await RequestAsync(doc)).Outcome);
    }

    [Fact]
    public async Task Generate_SkipsDraftsThatAreAlreadyDone()
    {
        var doc = await RoutedDocumentAsync();
        var draft = (await RequestAsync(doc)).Draft!;
        await Generator().GenerateAsync(draft.Id, default);
        var first = _db.ReplyDrafts.Single().BodyEn;

        await Generator(new ThrowingDrafter()).GenerateAsync(draft.Id, default);

        Assert.Equal(ReplyDraftStatus.Ready, _db.ReplyDrafts.Single().Status);
        Assert.Equal(first, _db.ReplyDrafts.Single().BodyEn);
    }

    [Fact]
    public async Task Request_WithAnUnknownTemplate_IsRefused()
    {
        var doc = await RoutedDocumentAsync();
        Assert.Equal(ReplyDraftService.Outcome.UnknownTemplate, (await RequestAsync(doc, "no-such-template")).Outcome);
    }

    private class ThrowingDrafter : IReplyDrafter
    {
        public Task<DraftResult> DraftAsync(DraftInput input, CancellationToken ct) => throw new InvalidOperationException("boom");
    }
}

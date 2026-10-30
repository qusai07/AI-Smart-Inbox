using DocumentFormat.OpenXml.Packaging;
using Paragraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Drafting;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.UnitTests;

public class DraftExportAndWebhookTests
{
    private readonly SmartInboxDbContext _db = TestDb.Create();
    private static readonly TestUser Clerk = new("Sara", Roles.Clerk);
    private static readonly NotificationOptions Links = new() { AppBaseUrl = "http://inbox.test" };

    [Fact]
    public void Docx_HasTheEnglishLetter_ThenTheArabicOneRightToLeft()
    {
        var draft = new ReplyDraft
        {
            SubjectEn = "Your notice ref. 2027/118", BodyEn = "Dear Sir/Madam,\n\nWe acknowledge receipt.",
            SubjectAr = "بشأن إخطاركم رقم 2027/118", BodyAr = "تحية طيبة وبعد،\n\nنفيدكم باستلام إخطاركم.",
        };

        var bytes = new DocxDraftExporter(new DraftingOptions()).Export(draft);

        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var paragraphs = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().ToList();
        var texts = paragraphs.Select(p => p.InnerText).ToList();
        Assert.Equal("Ministry of Digital Services – Demo", texts[0]);
        Assert.Contains("Subject: Your notice ref. 2027/118", texts);
        Assert.Contains("We acknowledge receipt.", texts);
        Assert.Contains("الموضوع: بشأن إخطاركم رقم 2027/118", texts);

        var arabic = paragraphs.Single(p => p.InnerText == "نفيدكم باستلام إخطاركم.");
        Assert.NotNull(arabic.ParagraphProperties!.BiDi);
        var english = paragraphs.Single(p => p.InnerText == "We acknowledge receipt.");
        Assert.Null(english.ParagraphProperties!.BiDi);
        Assert.True(texts.IndexOf("We acknowledge receipt.") < texts.IndexOf("نفيدكم باستلام إخطاركم."));
    }

    private async Task<(Guid Doc, WebhookEndpoint Endpoint)> RoutedDocumentWithEndpointAsync()
    {
        var endpoint = new WebhookEndpoint { Url = "https://hooks.example.com", Secret = "p:x" };
        _db.WebhookEndpoints.Add(endpoint);
        var d = new Document { FileName = "notice.txt", ContentType = "text/plain", StoragePath = "x", Status = DocumentStatus.Analyzed };
        d.Analysis = new DocumentAnalysis { DocumentId = d.Id, Category = DocumentCategory.Legal, Sender = "Court", ReferenceNumber = "R/1", Deadline = new DateOnly(2027, 3, 4) };
        _db.Documents.Add(d);
        await _db.SaveChangesAsync();
        await new DocumentService(_db, new MemoryStorage(), new NullQueue(), new RecordingNotifier(), Clerk).RouteAsync(d.Id, new RouteRequest(2, null), null, default);
        return (d.Id, endpoint);
    }

    [Fact]
    public async Task DraftReady_AndDraftApproved_AreDeliveredAsThinWebhooks()
    {
        var (doc, _) = await RoutedDocumentWithEndpointAsync();
        var publisher = new WebhookPublisher(_db, Links);
        var service = new ReplyDraftService(_db, Clerk, new DraftingOptions(), new RecordingDraftQueue(), new RecordingNotifier(), webhooks: publisher);
        var requested = (await service.RequestAsync(doc, new RequestDraftRequest(), default)).Draft!;

        await new ReplyDraftGenerator(_db, new TemplateReplyDrafter(new ReplyTemplateCatalog(), new DraftingOptions(), TimeProvider.System),
            new RecordingNotifier(), NullLogger<ReplyDraftGenerator>.Instance, publisher).GenerateAsync(requested.Id, default);

        var ready = Assert.Single(_db.WebhookDeliveries, w => w.EventType == WebhookEvents.DraftReady);
        Assert.Contains($"\"draftId\":\"{requested.Id}\"", ready.Payload);
        Assert.Contains("\"status\":\"Ready\"", ready.Payload);
        Assert.Contains("\"actor\":\"Templates\"", ready.Payload);
        Assert.DoesNotContain("Dear Sir", ready.Payload);   // thin: never the letter text
        Assert.DoesNotContain("bodyEn", ready.Payload);

        var draft = _db.ReplyDrafts.Single();
        var filled = await service.UpdateAsync(draft.Id, new UpdateDraftRequest(draft.SubjectEn!, draft.BodyEn!.Replace("[●]", "OUT/1"), draft.SubjectAr!, draft.BodyAr!.Replace("[●]", "OUT/1")), default);
        await service.ApproveAsync(filled.Draft!.Id, default);

        var approved = Assert.Single(_db.WebhookDeliveries, w => w.EventType == WebhookEvents.DraftApproved);
        Assert.Contains("\"version\":2", approved.Payload);
        Assert.Contains("\"actor\":\"Sara\"", approved.Payload);
        Assert.Contains($"http://inbox.test/documents/{doc}", approved.Payload);
    }

    [Fact]
    public void DraftEvents_AreSubscribable() =>
        Assert.Contains(WebhookEvents.DraftReady, WebhookEvents.Subscribable);
}

using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using SmartInbox.Client.Webhooks;

namespace SmartInbox.Client.Tests;

/// <summary>Reply drafts through the SDK, the real API and its background worker.</summary>
[Collection(ServerCollection.Name)]
public class DraftsApiTests(SmartInboxFactory server)
{
    private const string CourtNotice = """
        DEMO CITY COURT OF FIRST INSTANCE
        Ref: 2027/118   Date: 20 October 2026
        Court hearing notice: a lawsuit has been filed against the Ministry. Submit the defence memorandum by 4 March 2027.
        """;

    private async Task<(ISmartInboxClient Inbox, Guid DocumentId)> RoutedNoticeAsync()
    {
        var (_, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);
        var doc = await inbox.Documents.UploadTextAsync(CourtNotice);
        await inbox.Documents.WaitForAnalysisAsync(doc.Id, TimeSpan.FromSeconds(30));
        await inbox.Documents.RouteAsync(doc.Id, 2);
        return (inbox, doc.Id);
    }

    [Fact]
    public async Task Draft_Edit_Approve_GetsItsNumber_DownloadWord()
    {
        var (inbox, docId) = await RoutedNoticeAsync();
        Assert.Contains(await inbox.Drafts.ListTemplatesAsync(), t => t.Key == "legal-acknowledgement" && t.Categories.Contains(DocumentCategory.Legal));

        var requested = await inbox.Drafts.RequestAsync(docId, instructions: "Confirm counsel has been appointed");
        var draft = await inbox.Drafts.WaitForDraftAsync(requested.Id, TimeSpan.FromSeconds(30));

        Assert.Equal(DraftStatus.Ready, draft.Status);
        Assert.Equal("Templates", draft.Engine);
        Assert.Equal("Confirm counsel has been appointed", draft.Instructions);
        Assert.Contains("Legal Affairs", draft.BodyEn);
        Assert.Contains("تحية طيبة وبعد", draft.BodyAr);
        Assert.Contains("(issued on approval)", draft.BodyEn);   // our number comes from the register, not from a person
        Assert.Equal(0, draft.Placeholders);

        var edited = await inbox.Drafts.UpdateAsync(draft.Id, new ReplyText(
            draft.SubjectEn!, draft.BodyEn + "\n\nCounsel has been appointed.", draft.SubjectAr!, draft.BodyAr!));
        Assert.Equal(2, edited.Version);
        await Assert.ThrowsAsync<SmartInboxConflictException>(() => inbox.Drafts.ApproveAsync(draft.Id)); // not the latest any more

        var approved = await inbox.Drafts.ApproveAsync(edited.Id);
        Assert.Equal(DraftStatus.Approved, approved.Status);
        Assert.Equal("API key: Clerk test key", approved.DecidedBy);
        var number = Regex.Match(approved.BodyEn!, @"MDS/OUT/\d{4}/\d{4}").Value;
        Assert.NotEmpty(number);
        Assert.Contains(number, approved.BodyAr);
        Assert.DoesNotContain("(issued on approval)", approved.BodyEn);
        Assert.Equal([2, 1], (await inbox.Drafts.ListAsync(docId)).Select(d => d.Version));

        var word = await inbox.Drafts.DownloadDocxAsync(edited.Id);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", word.ContentType);
        Assert.Equal("reply-2027-118-v2.docx", word.FileName);
        using var zip = new ZipArchive(word.Content);
        using var xml = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var body = await xml.ReadToEndAsync();
        Assert.Contains(number, body);
        Assert.Contains("الشؤون القانونية", body);

        var timeline = (await inbox.Documents.GetAsync(docId)).Timeline.Select(e => e.Type).ToList();
        Assert.Contains("DraftRequested", timeline);
        Assert.Contains("DraftReady", timeline);
        Assert.Contains("DraftEdited", timeline);
        Assert.Contains("DraftApproved", timeline);
        Assert.Contains("OutgoingIssued", timeline);
    }

    [Fact]
    public async Task Reject_ThenDraftAgainWithAnotherTemplate()
    {
        var (inbox, docId) = await RoutedNoticeAsync();
        var first = await inbox.Drafts.WaitForDraftAsync((await inbox.Drafts.RequestAsync(docId)).Id, TimeSpan.FromSeconds(30));

        Assert.Equal(DraftStatus.Rejected, (await inbox.Drafts.RejectAsync(first.Id, "Use a plain acknowledgement")).Status);
        var second = await inbox.Drafts.WaitForDraftAsync((await inbox.Drafts.RequestAsync(docId, "acknowledge-receipt")).Id, TimeSpan.FromSeconds(30));

        Assert.Equal("acknowledge-receipt", second.TemplateKey);
        Assert.Equal(2, second.Version);
    }

    [Fact]
    public async Task Errors_NotRouted_UnknownTemplate_OtherDepartment()
    {
        var (_, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);
        var unrouted = await inbox.Documents.UploadTextAsync("A letter nobody has routed yet.");
        await Assert.ThrowsAsync<SmartInboxConflictException>(() => inbox.Drafts.RequestAsync(unrouted.Id));

        var (desk, docId) = await RoutedNoticeAsync();
        var unknown = await Assert.ThrowsAsync<SmartInboxApiException>(() => desk.Drafts.RequestAsync(docId, "no-such-template"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var draft = await desk.Drafts.WaitForDraftAsync((await desk.Drafts.RequestAsync(docId)).Id, TimeSpan.FromSeconds(30));
        var (_, hrKey) = await server.CreateKeyAsync("Department", departmentId: 4);
        var hr = server.Sdk(hrKey);
        await Assert.ThrowsAsync<SmartInboxNotFoundException>(() => hr.Drafts.GetAsync(draft.Id));
        await Assert.ThrowsAsync<SmartInboxNotFoundException>(() => hr.Drafts.DownloadDocxAsync(draft.Id));
    }

    [Fact]
    public void DraftReadyWebhook_ParsesToATypedEvent()
    {
        const string body = """{"id":"evt_9","type":"draft.ready","createdAt":"2026-10-30T09:00:00Z","apiVersion":"2026-11-01","data":{"draftId":"0b7f2a7e-0000-4000-8000-000000000001","documentId":"8f3c6a1e-0000-4000-8000-000000000001","version":1,"status":"Ready","templateKey":"legal-acknowledgement","departmentId":2,"actor":"Templates","url":"https://inbox.example.com/documents/8f3c6a1e-0000-4000-8000-000000000001"}}""";

        var evt = Assert.IsType<DraftReadyEvent>(WebhookVerifier.Parse(body));

        Assert.Equal(DraftStatus.Ready, evt.Data.Status);
        Assert.Equal("legal-acknowledgement", evt.Data.TemplateKey);
        Assert.Equal(2, evt.Data.DepartmentId);
    }
}

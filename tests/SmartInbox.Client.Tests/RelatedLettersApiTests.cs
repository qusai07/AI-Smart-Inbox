using SmartInbox.Client.Webhooks;

namespace SmartInbox.Client.Tests;

/// <summary>Related letters through the SDK, the real API and its analysis pipeline.</summary>
[Collection(ServerCollection.Name)]
public class RelatedLettersApiTests(SmartInboxFactory server)
{
    [Fact]
    public async Task TheSameLetterTwice_IsFlagged_Explained_AndTheLinkCanBeRemoved()
    {
        var (_, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);
        var text = $"""
            NIMBUS CLOUD SERVICES
            Invoice NCS-{Random.Shared.Next(10000, 99999)}
            Please settle the attached invoice for cloud hosting services within 30 days.
            {string.Join(' ', Enumerable.Repeat("Line items: compute, storage and support for the quarter.", 4))}
            """;
        var first = await inbox.Documents.UploadTextAsync(text);
        await inbox.Documents.WaitForAnalysisAsync(first.Id, TimeSpan.FromSeconds(30));
        var again = await inbox.Documents.UploadTextAsync(text);
        await inbox.Documents.WaitForAnalysisAsync(again.Id, TimeSpan.FromSeconds(30));

        var link = Assert.Single(await inbox.Documents.RelatedAsync(again.Id));
        Assert.Equal(LinkKind.Duplicate, link.Kind);
        Assert.Equal("Same file", link.Evidence);
        Assert.Equal(first.Id, link.DocumentId);
        Assert.True(link.Earlier);
        Assert.Equal(again.Id, Assert.Single(await inbox.Documents.RelatedAsync(first.Id)).DocumentId);

        var flagged = await inbox.Documents.ListAsync(new DocumentListQuery { Related = true, PageSize = 100 });
        Assert.True(flagged.Items.Single(d => d.Id == again.Id).PossibleDuplicate);
        Assert.False(flagged.Items.Single(d => d.Id == first.Id).PossibleDuplicate);
        Assert.Contains((await inbox.Documents.GetAsync(again.Id)).Timeline, e => e.Type == "Linked");

        var (_, hrKey) = await server.CreateKeyAsync("Department", departmentId: 4);
        await Assert.ThrowsAsync<SmartInboxNotFoundException>(() => server.Sdk(hrKey).Documents.RelatedAsync(again.Id));

        await inbox.Documents.RemoveRelatedAsync(again.Id, link.LinkId);
        Assert.Empty(await inbox.Documents.RelatedAsync(again.Id));
        await Assert.ThrowsAsync<SmartInboxNotFoundException>(() => inbox.Documents.RemoveRelatedAsync(again.Id, link.LinkId));
    }

    [Fact]
    public void DocumentLinkedWebhook_ParsesToATypedEvent()
    {
        const string body = """{"id":"evt_21","type":"document.linked","createdAt":"2026-10-30T09:00:00Z","apiVersion":"2026-11-01","data":{"documentId":"8f3c6a1e-0000-4000-8000-000000000002","relatedDocumentId":"8f3c6a1e-0000-4000-8000-000000000001","outgoingLetterId":null,"kind":"FollowUp","url":"https://inbox.example.com/documents/8f3c6a1e-0000-4000-8000-000000000002"}}""";

        var evt = Assert.IsType<DocumentLinkedEvent>(WebhookVerifier.Parse(body));

        Assert.Equal(LinkKind.FollowUp, evt.Data.Kind);
        Assert.Equal(Guid.Parse("8f3c6a1e-0000-4000-8000-000000000001"), evt.Data.RelatedDocumentId);
    }
}

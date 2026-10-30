using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartInbox.Client.Tests;

/// <summary>Reply drafts through the real API and background worker (raw HTTP until the SDK gets a Drafts client).</summary>
[Collection(ServerCollection.Name)]
public class DraftsApiTests(SmartInboxFactory server)
{
    private const string CourtNotice = """
        DEMO CITY COURT OF FIRST INSTANCE
        Ref: 2027/118   Date: 20 October 2026
        Court hearing notice: a lawsuit has been filed against the Ministry. Submit the defence memorandum by 4 March 2027.
        """;

    [Fact]
    public async Task RequestDraft_IsGeneratedInTheBackground_FromTheTemplate()
    {
        var (_, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);
        var doc = await inbox.Documents.UploadTextAsync(CourtNotice);
        await inbox.Documents.WaitForAnalysisAsync(doc.Id, TimeSpan.FromSeconds(30));
        await inbox.Documents.RouteAsync(doc.Id, 2);

        using var http = server.CreateClient(new() { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var templates = await http.GetFromJsonAsync<JsonElement>("/api/reply-templates");
        Assert.Contains(templates.EnumerateArray(), t => t.GetProperty("key").GetString() == "legal-acknowledgement");

        var created = await http.PostAsJsonAsync($"/api/documents/{doc.Id}/drafts", new { });
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var draftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        JsonElement draft = default;
        for (var i = 0; i < 50; i++)
        {
            draft = await http.GetFromJsonAsync<JsonElement>($"/api/drafts/{draftId}");
            if (draft.GetProperty("status").GetString() is "Ready" or "Failed") break;
            await Task.Delay(200);
        }

        Assert.Equal("Ready", draft.GetProperty("status").GetString());
        Assert.Equal("Templates", draft.GetProperty("engine").GetString());
        Assert.Contains("تحية طيبة وبعد", draft.GetProperty("bodyAr").GetString());
        Assert.Contains("Legal Affairs", draft.GetProperty("bodyEn").GetString());
        Assert.True(draft.GetProperty("placeholders").GetInt32() >= 2); // at least our reference, in both languages

        var approve = await http.PostAsync($"/api/drafts/{draftId}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode); // [●] still there
    }

    [Fact]
    public async Task RequestDraft_ForAnUnroutedDocument_Is409()
    {
        var (_, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);
        var doc = await inbox.Documents.UploadTextAsync("A letter nobody has routed yet.");

        using var http = server.CreateClient(new() { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"/api/documents/{doc.Id}/drafts", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync($"/api/documents/{doc.Id}/drafts", new { templateKey = "Bad Key" })).StatusCode);
    }
}

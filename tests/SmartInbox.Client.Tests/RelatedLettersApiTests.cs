using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartInbox.Client.Tests;

/// <summary>Related letters through the real API and analysis pipeline (the SDK gets them in 0.6.0).</summary>
[Collection(ServerCollection.Name)]
public class RelatedLettersApiTests(SmartInboxFactory server)
{
    private async Task<HttpClient> HttpAsync(string role, int? departmentId = null)
    {
        var (_, key) = await server.CreateKeyAsync(role, departmentId);
        var http = server.CreateClient(new() { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return http;
    }

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

        using var desk = await HttpAsync("Clerk");
        var related = await desk.GetFromJsonAsync<JsonElement>($"api/documents/{again.Id}/related");
        var link = Assert.Single(related.EnumerateArray());
        Assert.Equal("Duplicate", link.GetProperty("kind").GetString());
        Assert.Equal("Same file", link.GetProperty("evidence").GetString());
        Assert.Equal(first.Id, link.GetProperty("documentId").GetGuid());

        var flagged = await desk.GetFromJsonAsync<JsonElement>("api/documents?related=true&pageSize=100");
        var row = flagged.GetProperty("items").EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == again.Id);
        Assert.True(row.GetProperty("possibleDuplicate").GetBoolean());
        Assert.Contains((await inbox.Documents.GetAsync(again.Id)).Timeline, e => e.Type == "Linked");

        using var hr = await HttpAsync("Department", departmentId: 4);
        Assert.Equal(HttpStatusCode.NotFound, (await hr.GetAsync($"api/documents/{again.Id}/related")).StatusCode);

        var linkId = link.GetProperty("linkId").GetInt64();
        Assert.Equal(HttpStatusCode.NoContent, (await desk.DeleteAsync($"api/documents/{again.Id}/related/{linkId}")).StatusCode);
        Assert.Empty((await desk.GetFromJsonAsync<JsonElement>($"api/documents/{again.Id}/related")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await desk.DeleteAsync($"api/documents/{again.Id}/related/{linkId}")).StatusCode);
    }
}

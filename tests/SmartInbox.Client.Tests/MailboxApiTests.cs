using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartInbox.Application;

namespace SmartInbox.Client.Tests;

/// <summary>Mailbox ingestion through the real API, with an in-memory mailbox instead of an IMAP server.</summary>
[Collection(ServerCollection.Name)]
public class MailboxApiTests(SmartInboxFactory server)
{
    private const string Notice = """
        DEMO CITY COURT OF FIRST INSTANCE
        Ref: 2027/118   Date: 20 October 2026
        Court hearing notice: a lawsuit has been filed against the Ministry. Submit the defence memorandum by 4 March 2027.
        """;

    private static MailAttachment Letter(string name = "notice.txt", string text = Notice) => new(name, Encoding.UTF8.GetBytes(text));

    private async Task<JsonElement> CheckNowAsync(HttpClient admin)
    {
        var response = await admin.PostAsync("/api/mailbox/check", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task CheckNow_TurnsAnEmailWithALetterIntoAnAnalysedDocument_WithItsOriginalEmail()
    {
        using var admin = await server.AdminAsync();
        var subject = $"Notice {Guid.NewGuid():N}";
        server.Mailbox.Add(subject, "Dear Ministry, please find the court notice attached.", attachments: [Letter()]);

        var run = await CheckNowAsync(admin);
        Assert.Equal(1, run.GetProperty("imported").GetInt32());

        var imports = await admin.GetFromJsonAsync<JsonElement>("/api/mailbox/imports");
        var row = imports.GetProperty("items").EnumerateArray().First(i => i.GetProperty("subject").GetString() == subject);
        Assert.Equal("Imported", row.GetProperty("status").GetString());
        var docId = Assert.Single(row.GetProperty("documentIds").EnumerateArray()).GetGuid();

        var (_, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);
        var doc = await inbox.Documents.WaitForAnalysisAsync(docId, TimeSpan.FromSeconds(30));
        Assert.Equal("notice.txt", doc.FileName);
        Assert.Equal(DocumentCategory.Legal, doc.Analysis!.Category);
        Assert.Contains(doc.Timeline, e => e.Type == "Uploaded" && e.Actor == "Mailbox: correspondence@mds.demo");

        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/documents/{docId}");
        Assert.Equal("Email", detail.GetProperty("source").GetString());
        var email = detail.GetProperty("sourceEmail");
        Assert.Equal("sender@partner.gov", email.GetProperty("fromAddress").GetString());
        Assert.Equal(subject, email.GetProperty("subject").GetString());
        Assert.Contains("court notice attached", email.GetProperty("bodyText").GetString());

        var original = await admin.GetAsync($"/api/documents/{docId}/source-email");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal("message/rfc822", original.Content.Headers.ContentType!.MediaType);
        Assert.Contains(subject, await original.Content.ReadAsStringAsync());

        var fromEmail = await admin.GetFromJsonAsync<JsonElement>("/api/documents?source=Email&pageSize=100");
        Assert.Contains(fromEmail.GetProperty("items").EnumerateArray(), d => d.GetProperty("id").GetGuid() == docId);
        Assert.All(fromEmail.GetProperty("items").EnumerateArray(), d => Assert.Equal("Email", d.GetProperty("source").GetString()));

        Assert.Equal(0, (await CheckNowAsync(admin)).GetProperty("imported").GetInt32()); // left the folder: nothing new

        // The SDK sees the same: source, covering email, and the source filter.
        Assert.Equal(DocumentSource.Email, doc.Source);
        Assert.Equal("sender@partner.gov", doc.SourceEmail!.FromAddress);
        var emailed = new List<Guid>();
        await foreach (var d in inbox.Documents.ListAllAsync(new DocumentListQuery { Source = DocumentSource.Email })) emailed.Add(d.Id);
        Assert.Contains(docId, emailed);
        await foreach (var d in inbox.Documents.ListAllAsync(new DocumentListQuery { Source = DocumentSource.Upload })) Assert.NotEqual(docId, d.Id);
    }

    [Fact]
    public async Task SourceEmail_FollowsTheDocumentsVisibility()
    {
        using var admin = await server.AdminAsync();
        var subject = $"Contract {Guid.NewGuid():N}";
        server.Mailbox.Add(subject, "See attached.", attachments: [Letter("contract-review.txt")]);
        await CheckNowAsync(admin);
        var imports = await admin.GetFromJsonAsync<JsonElement>("/api/mailbox/imports");
        var docId = imports.GetProperty("items").EnumerateArray().First(i => i.GetProperty("subject").GetString() == subject)
            .GetProperty("documentIds")[0].GetGuid();

        var (_, desk) = await server.CreateKeyAsync("Clerk");
        await server.Sdk(desk).Documents.WaitForAnalysisAsync(docId, TimeSpan.FromSeconds(30));
        await server.Sdk(desk).Documents.RouteAsync(docId, 2);

        var (_, hrKey) = await server.CreateKeyAsync("Department", departmentId: 4);
        using var hr = server.CreateClient(new() { HandleCookies = false });
        hr.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hrKey);
        Assert.Equal(HttpStatusCode.NotFound, (await hr.GetAsync($"/api/documents/{docId}/source-email")).StatusCode);

        var (_, legalKey) = await server.CreateKeyAsync("Department", departmentId: 2);
        using var legal = server.CreateClient(new() { HandleCookies = false });
        legal.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", legalKey);
        Assert.Equal(HttpStatusCode.OK, (await legal.GetAsync($"/api/documents/{docId}/source-email")).StatusCode);
    }

    [Fact]
    public async Task FailedEmails_ShowInTheLog_AndCanBeRetried()
    {
        using var admin = await server.AdminAsync();
        var subject = $"Broken {Guid.NewGuid():N}";
        var uid = server.Mailbox.Add(subject, "x", attachments: [Letter()]);
        server.Mailbox.FetchFailure = u => u == uid ? new IOException("connection reset") : null;
        try
        {
            Assert.Equal(1, (await CheckNowAsync(admin)).GetProperty("failed").GetInt32());

            var failed = await admin.GetFromJsonAsync<JsonElement>("/api/mailbox/imports?status=Failed");
            var row = failed.GetProperty("items").EnumerateArray().First(i => i.GetProperty("reason").GetString() == "connection reset");
            var id = row.GetProperty("id").GetGuid();

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/mailbox/imports/{id}/retry", null)).StatusCode);
            server.Mailbox.FetchFailure = _ => null;
            Assert.Equal(1, (await CheckNowAsync(admin)).GetProperty("imported").GetInt32());
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/mailbox/imports/{id}/retry", null)).StatusCode); // not failed any more
        }
        finally
        {
            server.Mailbox.FetchFailure = _ => null;
        }
    }

    [Fact]
    public async Task StatusAndTest_ForAdmins()
    {
        using var admin = await server.AdminAsync();

        var status = await admin.GetFromJsonAsync<JsonElement>("/api/mailbox/status");
        Assert.True(status.GetProperty("configured").GetBoolean());
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal("correspondence@mds.demo", status.GetProperty("address").GetString());

        var test = await (await admin.PostAsync("/api/mailbox/test", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(test.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task MailboxAdmin_IsCookieOnly_EvenForAnAdminKey()
    {
        var (_, key) = await server.CreateKeyAsync("Admin");
        using var http = server.CreateClient(new() { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/mailbox/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("/api/mailbox/check", null)).StatusCode);
    }
}

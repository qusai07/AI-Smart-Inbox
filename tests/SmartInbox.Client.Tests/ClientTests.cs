using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartInbox.Client.Webhooks;

namespace SmartInbox.Client.Tests;

[Collection(ServerCollection.Name)]
public class ClientTests(SmartInboxFactory server)
{
    private const string CourtNotice = """
        Abu Dhabi Judicial Department
        Ref: ADJD/2026/4471
        Subject: Court hearing notice — case 1123/2026
        You are hereby notified that the court hearing in the lawsuit filed against the Ministry is scheduled.
        Please appoint legal counsel and submit the defence memorandum and contract documents before the session.
        """;

    private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task UploadAnalyzeRoute_FullFlow_WithAnApiKey()
    {
        var (_, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);

        var uploaded = await inbox.Documents.UploadTextAsync(CourtNotice);
        var analyzed = await inbox.Documents.WaitForAnalysisAsync(uploaded.Id, AnalysisTimeout);

        Assert.NotNull(analyzed.Analysis);
        Assert.Contains(analyzed.Status, new[] { DocumentStatus.Analyzed, DocumentStatus.NeedsReview });
        var target = analyzed.Analysis.SuggestedDepartmentId ?? 2;

        await inbox.Documents.RouteAsync(uploaded.Id, target, note: "Routed by the SDK test");

        var routed = await inbox.Documents.GetAsync(uploaded.Id);
        Assert.Equal(DocumentStatus.Routed, routed.Status);
        Assert.Equal(target, routed.Routing!.FinalDepartmentId);
        Assert.Equal("API key: Clerk test key", routed.Routing.DecidedBy);
        Assert.Contains(routed.Timeline, e => e.Type == "Routed" && e.Actor == "API key: Clerk test key");

        var file = await inbox.Documents.DownloadAsync(uploaded.Id);
        using var reader = new StreamReader(file.Content);
        Assert.Contains("ADJD/2026/4471", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task UploadFile_ResolvesTypeFromTheName()
    {
        var (_, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);

        var uploaded = await inbox.Documents.UploadAsync(new MemoryStream("Invoice INV-77 for cloud hosting, amount due AED 12,000."u8.ToArray()), "invoice.txt");
        var doc = await inbox.Documents.GetAsync(uploaded.Id);

        Assert.Equal("invoice.txt", doc.FileName);
        Assert.Equal("text/plain", doc.ContentType);
    }

    [Fact]
    public async Task ListAllAsync_WalksEveryPage()
    {
        var (_, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);
        var mine = new HashSet<Guid>();
        var tag = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 5; i++) mine.Add((await inbox.Documents.UploadTextAsync($"Paging test {tag} letter {i}")).Id);

        var found = new List<Guid>();
        await foreach (var d in inbox.Documents.ListAllAsync(new DocumentListQuery { Search = tag, PageSize = 2 }))
            found.Add(d.Id);

        Assert.Equal(mine.Order(), found.Order());
        Assert.Equal(found.Count, found.Distinct().Count());
    }

    [Fact]
    public async Task Errors_AreMappedFromProblemDetails()
    {
        var (_, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);
        var doc = await inbox.Documents.UploadTextAsync("Please send the annual report.");
        await inbox.Documents.WaitForAnalysisAsync(doc.Id, AnalysisTimeout);

        await Assert.ThrowsAsync<SmartInboxNotFoundException>(() => inbox.Documents.GetAsync(Guid.NewGuid()));

        var unknown = await Assert.ThrowsAsync<SmartInboxApiException>(() => inbox.Documents.RouteAsync(doc.Id, 999));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("Unknown department.", unknown.Detail);

        var invalid = await Assert.ThrowsAsync<SmartInboxApiException>(() => inbox.Documents.RouteAsync(doc.Id, 0));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("DepartmentId", invalid.Errors.Keys);
    }

    [Fact]
    public async Task DepartmentKey_SeesOnlyItsDepartment_AndNoDeskEndpoints()
    {
        var (_, desk) = await server.CreateKeyAsync("Clerk");
        var (_, hr) = await server.CreateKeyAsync("Department", departmentId: 4);
        var clerk = server.Sdk(desk);
        var hrInbox = server.Sdk(hr);
        var legalDoc = await clerk.Documents.UploadTextAsync("Court summons for the Ministry.");
        var hrDoc = await clerk.Documents.UploadTextAsync("Annual leave request from an employee.");
        await clerk.Documents.WaitForAnalysisAsync(legalDoc.Id, AnalysisTimeout);
        await clerk.Documents.WaitForAnalysisAsync(hrDoc.Id, AnalysisTimeout);
        await clerk.Documents.RouteAsync(legalDoc.Id, 2);
        await clerk.Documents.RouteAsync(hrDoc.Id, 4);

        await Assert.ThrowsAsync<SmartInboxNotFoundException>(() => hrInbox.Documents.GetAsync(legalDoc.Id));
        Assert.Equal(hrDoc.Id, (await hrInbox.Documents.GetAsync(hrDoc.Id)).Id);
        await foreach (var d in hrInbox.Documents.ListAllAsync()) Assert.Equal(4, d.FinalDepartmentId);

        var stats = await Assert.ThrowsAsync<SmartInboxApiException>(() => hrInbox.Stats.GetAsync());
        Assert.Equal(HttpStatusCode.Forbidden, stats.StatusCode);
        var upload = await Assert.ThrowsAsync<SmartInboxApiException>(() => hrInbox.Documents.UploadTextAsync("x"));
        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);

        await hrInbox.Documents.AcknowledgeAsync(hrDoc.Id);
        await hrInbox.Documents.CloseAsync(hrDoc.Id, "Approved");
        Assert.Equal(HandlingStatus.Closed, (await hrInbox.Documents.GetAsync(hrDoc.Id)).Handling!.Status);
    }

    [Fact]
    public async Task CommentAssignAndMembers()
    {
        var (_, desk) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(desk);
        var doc = await inbox.Documents.UploadTextAsync("Court summons; please appoint counsel.");
        await inbox.Documents.WaitForAnalysisAsync(doc.Id, AnalysisTimeout);

        await Assert.ThrowsAsync<SmartInboxConflictException>(() => inbox.Documents.AssignAsync(doc.Id, "anyone"));
        await inbox.Documents.RouteAsync(doc.Id, 2);

        var members = await inbox.Departments.MembersAsync(2);
        var omar = Assert.Single(members, m => m.DisplayName.Contains("Omar"));
        await inbox.Documents.AssignAsync(doc.Id, omar.Id);
        var comment = await inbox.Documents.CommentAsync(doc.Id, "Counsel briefed.");

        var detail = await inbox.Documents.GetAsync(doc.Id);
        Assert.Equal(omar.Id, detail.Assignee!.Id);
        Assert.Equal("API key: Clerk test key", comment.AuthorName);
        Assert.Contains(detail.Comments, c => c.Text == "Counsel briefed.");

        var (_, hr) = await server.CreateKeyAsync("Department", departmentId: 4);
        await Assert.ThrowsAsync<SmartInboxNotFoundException>(() => server.Sdk(hr).Departments.MembersAsync(2));
    }

    [Fact]
    public async Task RevokedKey_GetsUnauthorized()
    {
        var (id, key) = await server.CreateKeyAsync("Clerk");
        var inbox = server.Sdk(key);
        Assert.NotEmpty(await inbox.Departments.ListAsync());

        using var admin = await server.AdminAsync();
        (await admin.DeleteAsync($"/api/api-keys/{id}")).EnsureSuccessStatusCode();

        var ex = await Assert.ThrowsAsync<SmartInboxApiException>(() => inbox.Departments.ListAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task ApiKeys_CannotManageApiKeysOrWebhooks()
    {
        var (_, key) = await server.CreateKeyAsync("Admin");
        using var http = server.CreateClient(new() { HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new("Bearer", key);

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/api-keys")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/api-keys", new { name = "x", role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/webhooks")).StatusCode);
    }

    [Fact]
    public async Task Departments_AndWorkload()
    {
        var (_, key) = await server.CreateKeyAsync("Admin");
        var inbox = server.Sdk(key);

        var departments = await inbox.Departments.ListAsync();
        Assert.Contains(departments, d => d.Id == 2);
        Assert.NotNull(await inbox.Departments.GetWorkloadAsync());
        Assert.True((await inbox.Stats.GetAsync()).TotalDocuments >= 0);
    }

    /// <summary>The SDK's routes must exist in the server's OpenAPI document, so the two can't drift apart.</summary>
    [Fact]
    public async Task Contract_EverySdkRouteExistsInSwagger()
    {
        using var http = server.CreateClient();
        using var swagger = JsonDocument.Parse(await http.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = swagger.RootElement.GetProperty("paths");

        foreach (var (method, template) in Routes.All)
        {
            Assert.True(paths.TryGetProperty(template, out var path), $"{template} is missing from swagger.json");
            Assert.True(path.TryGetProperty(method.ToLowerInvariant(), out _), $"{method} {template} is missing from swagger.json");
        }
    }

    /// <summary>The SDK's DTO properties must be a subset of the server's, so nothing is silently left null.</summary>
    [Theory]
    [InlineData(typeof(DocumentSummary), "DocumentListItemDto")]
    [InlineData(typeof(DocumentDetail), "DocumentDetailDto")]
    [InlineData(typeof(Analysis), "AnalysisDto")]
    [InlineData(typeof(Stats), "StatsDto")]
    [InlineData(typeof(Department), "Department")]
    [InlineData(typeof(DepartmentWorkload), "DepartmentWorkloadDto")]
    [InlineData(typeof(Member), "MemberDto")]
    [InlineData(typeof(Comment), "CommentDto")]
    [InlineData(typeof(ReplyDraft), "ReplyDraftDto")]
    [InlineData(typeof(ReplyTemplate), "ReplyTemplateDto")]
    [InlineData(typeof(SourceEmail), "SourceEmailDto")]
    [InlineData(typeof(OutgoingLetter), "OutgoingLetterDto")]
    [InlineData(typeof(OutgoingRef), "OutgoingRefDto")]
    public async Task Contract_SdkModelsMatchSwaggerSchemas(Type sdkType, string schema)
    {
        using var http = server.CreateClient();
        using var swagger = JsonDocument.Parse(await http.GetStringAsync("/swagger/v1/swagger.json"));
        var properties = swagger.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema).GetProperty("properties");

        // Positional record properties have init setters; computed helpers (IsAnalysisComplete) have none.
        foreach (var p in sdkType.GetProperties().Where(p => p.GetSetMethod() is not null))
        {
            var json = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            Assert.True(properties.TryGetProperty(json, out _), $"{sdkType.Name}.{p.Name} ({json}) is not in the server's {schema}");
        }
    }
}

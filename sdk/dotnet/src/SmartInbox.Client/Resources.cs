using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;

namespace SmartInbox.Client;

/// <summary>Correspondence: upload, read, route and handle documents.</summary>
public sealed class DocumentsClient
{
    private readonly SmartInboxClient _client;

    internal DocumentsClient(SmartInboxClient client) => _client = client;

    /// <summary>Uploads a file (PDF, DOCX, JPG, PNG, EML or TXT, max 20 MB). Analysis starts in the background.</summary>
    public Task<UploadedDocument> UploadAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        return _client.SendAsync<UploadedDocument>(HttpMethod.Post, Routes.Documents, form, ct);
    }

    /// <summary>Submits pasted text (an email body, a transcribed letter) as a document.</summary>
    public Task<UploadedDocument> UploadTextAsync(string text, CancellationToken ct = default)
    {
        var form = new MultipartFormDataContent { { new StringContent(text, Encoding.UTF8), "text" } };
        return _client.SendAsync<UploadedDocument>(HttpMethod.Post, Routes.Documents, form, ct);
    }

    /// <summary>A document with its analysis and history.</summary>
    /// <exception cref="SmartInboxNotFoundException">It doesn't exist, or the key's role can't see it.</exception>
    public Task<DocumentDetail> GetAsync(Guid id, CancellationToken ct = default) =>
        _client.GetAsync<DocumentDetail>(Routes.Document(id), ct);

    /// <summary>One page of documents, newest first.</summary>
    public Task<Page<DocumentSummary>> ListAsync(DocumentListQuery? query = null, CancellationToken ct = default) =>
        _client.GetAsync<Page<DocumentSummary>>($"{Routes.Documents}?{QueryString(query ?? new DocumentListQuery())}", ct);

    /// <summary>Every matching document, newest first, fetching pages as you enumerate.</summary>
    public async IAsyncEnumerable<DocumentSummary> ListAllAsync(DocumentListQuery? query = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        query ??= new DocumentListQuery();
        for (var page = 1; ; page++)
        {
            var result = await ListAsync(query with { Page = page }, ct).ConfigureAwait(false);
            foreach (var item in result.Items) yield return item;
            if (!result.HasMore || result.Items.Count == 0) yield break;
        }
    }

    /// <summary>The original uploaded file. Dispose <see cref="DocumentFile.Content"/> when done.</summary>
    public async Task<DocumentFile> DownloadAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await _client.GetRawAsync(Routes.DocumentFile(id), ct).ConfigureAwait(false);
        var buffer = new MemoryStream();
        await response.Content.CopyToAsync(buffer, ct).ConfigureAwait(false);
        buffer.Position = 0;
        return new DocumentFile(buffer,
            response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    /// <summary>Runs the AI analysis again. Never undoes a routing decision.</summary>
    /// <exception cref="SmartInboxConflictException">It is already being analyzed.</exception>
    public Task ReanalyzeAsync(Guid id, CancellationToken ct = default) =>
        _client.SendAsync(HttpMethod.Post, Routes.DocumentAction(id, "reanalyze"), null, ct);

    /// <summary>Routes (or re-routes) a document to a department. Needs a desk role (Admin or Clerk). Never retried automatically.</summary>
    /// <exception cref="SmartInboxConflictException">It is still being analyzed.</exception>
    public Task RouteAsync(Guid id, int departmentId, string? note = null, CancellationToken ct = default) =>
        _client.SendAsync(HttpMethod.Post, Routes.DocumentAction(id, "route"), new { departmentId, note }, ct);

    /// <summary>The receiving department confirms it is working on the document.</summary>
    public Task AcknowledgeAsync(Guid id, CancellationToken ct = default) =>
        _client.SendAsync(HttpMethod.Post, Routes.DocumentAction(id, "acknowledge"), null, ct);

    /// <summary>The receiving department finished; the note says what was done. Never retried automatically.</summary>
    public Task CloseAsync(Guid id, string? note = null, CancellationToken ct = default) =>
        _client.SendAsync(HttpMethod.Post, Routes.DocumentAction(id, "close"), new { note }, ct);

    /// <summary>Reopens a closed document.</summary>
    public Task ReopenAsync(Guid id, CancellationToken ct = default) =>
        _client.SendAsync(HttpMethod.Post, Routes.DocumentAction(id, "reopen"), null, ct);

    /// <summary>
    /// Polls until analysis has finished (Analyzed, NeedsReview, Failed or Routed) and returns the document.
    /// For event-driven code, subscribe to the <c>document.analyzed</c> webhook instead.
    /// </summary>
    /// <exception cref="TimeoutException">Analysis didn't finish within <paramref name="timeout"/>.</exception>
    public async Task<DocumentDetail> WaitForAnalysisAsync(Guid id, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        var delay = TimeSpan.FromMilliseconds(500);
        while (true)
        {
            var doc = await GetAsync(id, ct).ConfigureAwait(false);
            if (doc.IsAnalysisComplete) return doc;
            if (DateTime.UtcNow + delay > deadline) throw new TimeoutException($"Document {id} was still {doc.Status} after {timeout}.");
            await Task.Delay(delay, ct).ConfigureAwait(false);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 5000));
        }
    }

    internal static string QueryString(DocumentListQuery q)
    {
        var parts = new List<string>();
        void Add(string key, object? value)
        {
            if (value is not null) parts.Add($"{key}={Uri.EscapeDataString(value.ToString()!)}");
        }
        Add("status", q.Status);
        Add("priority", q.Priority);
        Add("department", q.Department);
        Add("assigned", q.Assigned);
        Add("handling", q.Handling);
        Add("q", string.IsNullOrWhiteSpace(q.Search) ? null : q.Search);
        Add("page", q.Page);
        Add("pageSize", q.PageSize);
        return string.Join('&', parts);
    }
}

/// <summary>Departments and their workload.</summary>
public sealed class DepartmentsClient
{
    private readonly SmartInboxClient _client;

    internal DepartmentsClient(SmartInboxClient client) => _client = client;

    /// <summary>All departments (the targets for routing).</summary>
    public Task<IReadOnlyList<Department>> ListAsync(CancellationToken ct = default) =>
        _client.GetAsync<IReadOnlyList<Department>>(Routes.Departments, ct);

    /// <summary>New / acknowledged / closed / overdue counts per department the key can see.</summary>
    public Task<IReadOnlyList<DepartmentWorkload>> GetWorkloadAsync(CancellationToken ct = default) =>
        _client.GetAsync<IReadOnlyList<DepartmentWorkload>>(Routes.Workload, ct);
}

/// <summary>Dashboard statistics.</summary>
public sealed class StatsClient
{
    private readonly SmartInboxClient _client;

    internal StatsClient(SmartInboxClient client) => _client = client;

    /// <summary>Volumes, routing accuracy and AI usage. Needs a desk role (Admin or Clerk).</summary>
    public Task<Stats> GetAsync(CancellationToken ct = default) => _client.GetAsync<Stats>(Routes.Stats, ct);
}

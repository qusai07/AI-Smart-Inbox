using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace SmartInbox.Client;

/// <summary>The Smart Inbox API. Register with <c>services.AddSmartInboxClient(...)</c> or create a <see cref="SmartInboxClient"/>.</summary>
public interface ISmartInboxClient
{
    /// <summary>Upload, read, route and handle correspondence.</summary>
    DocumentsClient Documents { get; }
    /// <summary>Departments and their workload.</summary>
    DepartmentsClient Departments { get; }
    /// <summary>Dashboard statistics (desk roles only).</summary>
    StatsClient Stats { get; }
    /// <summary>Bilingual reply drafts: request, edit, approve or reject, download as Word.</summary>
    DraftsClient Drafts { get; }
}

/// <inheritdoc />
public sealed class SmartInboxClient : ISmartInboxClient
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly string UserAgent = $"SmartInbox.Client/{typeof(SmartInboxClient).Assembly.GetName().Version?.ToString(3)}";

    private readonly HttpClient _http;

    /// <summary>For an HttpClient that is already configured, e.g. by <c>AddSmartInboxClient</c>.</summary>
    [ActivatorUtilitiesConstructor]
    public SmartInboxClient(HttpClient http)
    {
        _http = http;
        Documents = new DocumentsClient(this);
        Departments = new DepartmentsClient(this);
        Stats = new StatsClient(this);
        Drafts = new DraftsClient(this);
    }

    /// <summary>Without dependency injection. Note: no automatic retries; prefer <c>AddSmartInboxClient</c> in apps.</summary>
    public SmartInboxClient(SmartInboxClientOptions options) : this(Configure(new HttpClient(), options)) { }

    /// <inheritdoc />
    public DocumentsClient Documents { get; }
    /// <inheritdoc />
    public DepartmentsClient Departments { get; }
    /// <inheritdoc />
    public StatsClient Stats { get; }
    /// <inheritdoc />
    public DraftsClient Drafts { get; }

    internal static HttpClient Configure(HttpClient http, SmartInboxClientOptions options)
    {
        options.Validate();
        http.BaseAddress = options.BaseUrl;
        http.Timeout = options.Timeout;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    internal async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
        return await ReadAsync<T>(response, ct).ConfigureAwait(false);
    }

    internal async Task<HttpResponseMessage> GetRawAsync(string path, CancellationToken ct)
    {
        var response = await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            using (response) throw await ErrorAsync(response, ct).ConfigureAwait(false);
        }
        return response;
    }

    internal async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        return await ReadAsync<T>(response, ct).ConfigureAwait(false);
    }

    internal async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, options: Json) };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, ct).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode) throw await ErrorAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false)
               ?? throw new SmartInboxApiException(response.StatusCode, "Empty response", "The API returned an empty body.");
    }

    /// <summary>Maps an error response (ideally RFC 7807 ProblemDetails) to a typed exception.</summary>
    internal static async Task<SmartInboxApiException> ErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        Problem? problem = null;
        try
        {
            if (response.Content.Headers.ContentType?.MediaType is { } type && type.Contains("json", StringComparison.OrdinalIgnoreCase))
                problem = await response.Content.ReadFromJsonAsync<Problem>(Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Not ProblemDetails; fall back to the status code.
        }

        return response.StatusCode switch
        {
            HttpStatusCode.NotFound => new SmartInboxNotFoundException(problem?.Title, problem?.Detail),
            HttpStatusCode.Conflict => new SmartInboxConflictException(problem?.Title, problem?.Detail),
            _ => new SmartInboxApiException(response.StatusCode, problem?.Title, problem?.Detail, problem?.Errors),
        };
    }

    private sealed record Problem(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}

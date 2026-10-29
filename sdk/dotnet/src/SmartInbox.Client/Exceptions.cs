using System.Net;

namespace SmartInbox.Client;

/// <summary>The API answered with an error. Built from the response's ProblemDetails when it has one.</summary>
public class SmartInboxApiException(HttpStatusCode statusCode, string? title, string? detail, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(detail ?? title ?? $"Smart Inbox API returned {(int)statusCode} {statusCode}.")
{
    /// <summary>HTTP status code of the response.</summary>
    public HttpStatusCode StatusCode { get; } = statusCode;
    /// <summary>ProblemDetails title, if any.</summary>
    public string? Title { get; } = title;
    /// <summary>ProblemDetails detail: a human-readable explanation, if any.</summary>
    public string? Detail { get; } = detail;
    /// <summary>Validation errors by field (400 responses).</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}

/// <summary>404: the document doesn't exist, or the API key's role can't see it.</summary>
public class SmartInboxNotFoundException(string? title, string? detail)
    : SmartInboxApiException(HttpStatusCode.NotFound, title, detail);

/// <summary>409: the request conflicts with the document's state (for example, it is still being analyzed).</summary>
public class SmartInboxConflictException(string? title, string? detail)
    : SmartInboxApiException(HttpStatusCode.Conflict, title, detail);

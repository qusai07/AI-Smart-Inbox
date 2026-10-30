using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Domain;

namespace SmartInbox.Application;

public class WebhookOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Allow endpoints on loopback / private networks. Development and Docker demos only.</summary>
    public bool AllowPrivateNetworks { get; set; }
    /// <summary>Allow plain http:// endpoints. Development only; production requires HTTPS.</summary>
    public bool AllowHttp { get; set; }
    /// <summary>deadline.approaching fires when an open document's deadline is this many days away or less.</summary>
    public int DeadlineWarningDays { get; set; } = 2;
}

public static class WebhookEvents
{
    public const string DocumentReceived = "document.received";
    public const string DocumentAnalyzed = "document.analyzed";
    public const string DocumentAnalysisFailed = "document.analysis_failed";
    public const string DocumentRouted = "document.routed";
    public const string DocumentAcknowledged = "document.acknowledged";
    public const string DocumentClosed = "document.closed";
    public const string DocumentReopened = "document.reopened";
    public const string DeadlineApproaching = "deadline.approaching";
    public const string DraftReady = "draft.ready";
    public const string DraftApproved = "draft.approved";
    public const string RegisterIssued = "register.issued";
    public const string DocumentLinked = "document.linked";
    /// <summary>Sent only by "Send test"; endpoints don't subscribe to it.</summary>
    public const string Ping = "webhook.ping";

    public const string All = "*";

    public static readonly string[] Subscribable =
    [
        DocumentReceived, DocumentAnalyzed, DocumentAnalysisFailed, DocumentRouted,
        DocumentAcknowledged, DocumentClosed, DocumentReopened, DeadlineApproaching, DraftReady, DraftApproved, RegisterIssued, DocumentLinked,
    ];
}

public static class WebhookRules
{
    public const string ApiVersion = "2026-11-01";
    public const int MaxUrlLength = 500;
    public const int MaxResponseSnippet = 1024;
    /// <summary>Consecutive failed attempts (across all deliveries) before an endpoint is switched off.</summary>
    public const int DisableAfterFailures = 20;

    /// <summary>Wait before each retry: 1 min, 5 min, 30 min, 2 h, 6 h, 12 h (~21 h in total).</summary>
    public static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
    ];

    /// <summary>The first attempt plus one per backoff step.</summary>
    public static int MaxAttempts => Backoff.Length + 1;

    /// <summary>Null when the URL is acceptable. Network-level checks (private addresses) happen at send time.</summary>
    public static string? ValidateUrl(string? url, WebhookOptions options)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return "Enter a valid absolute URL.";
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && options.AllowHttp))
            return options.AllowHttp ? "The URL must start with http:// or https://." : "The URL must use HTTPS.";
        if (!string.IsNullOrEmpty(uri.UserInfo)) return "Put credentials in the receiver, not in the URL.";
        return null;
    }

    public static IReadOnlyList<string> ParseEvents(string stored) =>
        stored == WebhookEvents.All ? [WebhookEvents.All] : stored.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public static string FormatEvents(IReadOnlyCollection<string>? events) =>
        events is null || events.Count == 0 || events.Contains(WebhookEvents.All)
            ? WebhookEvents.All
            : string.Join(',', events.Distinct().Order());

    public static bool Subscribes(WebhookEndpoint endpoint, string eventType) =>
        endpoint.Events == WebhookEvents.All || endpoint.Events.Split(',').Contains(eventType);

    public static string NewEventId() => "evt_" + Guid.NewGuid().ToString("N");

    public static string NewSecret() => "whsec_" + Base64Url(RandomNumberGenerator.GetBytes(32));

    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Stripe-style signatures: "t={unix seconds},v1={hex HMAC-SHA256(secret, "{t}.{body}")}".
/// During a secret rotation there is one v1 per active secret.
/// </summary>
public static class WebhookSignature
{
    public const string Header = "SmartInbox-Signature";
    public const string EventIdHeader = "SmartInbox-Event-Id";
    public const string EventTypeHeader = "SmartInbox-Event";

    public static string Compute(string secret, long timestamp, string body)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    public static string HeaderValue(IEnumerable<string> secrets, long timestamp, string body) =>
        $"t={timestamp}," + string.Join(',', secrets.Select(s => $"v1={Compute(s, timestamp, body)}"));
}

/// <summary>What every webhook body looks like.</summary>
public record WebhookEnvelope(string Id, string Type, DateTime CreatedAt, string ApiVersion, object Data)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public string Serialize() => JsonSerializer.Serialize(this, Json);
}

/// <summary>
/// Thin payload: ids and key fields only, never summaries or extracted text. Receivers fetch details
/// with their own API key, so third-party logs hold nothing sensitive and permissions still apply.
/// </summary>
public record WebhookDocumentData(
    Guid DocumentId, DocumentStatus Status, int? DepartmentId, int? PreviousDepartmentId, int? SuggestedDepartmentId,
    Priority? Priority, DocumentCategory? Category, double? Confidence, HandlingStatus? Handling,
    DateOnly? Deadline, string? Actor, string Url, DocumentSource Source = DocumentSource.Upload);

/// <summary>Two letters were found to belong together. Thin: ids and the kind, not the reason (it quotes the letters).</summary>
public record WebhookLinkData(Guid DocumentId, Guid? RelatedDocumentId, Guid? OutgoingLetterId, LinkKind Kind, string? Url);

/// <summary>An outgoing number was issued. Thin: the number and links, not the letter.</summary>
public record WebhookRegisterData(
    Guid OutgoingLetterId, string Number, int DepartmentId, Guid? DocumentId, Guid? ReplyDraftId, string? IssuedBy, string? Url);

/// <summary>Thin like document events: no letter text, just what's needed to fetch the draft with an API key.</summary>
public record WebhookDraftData(
    Guid DraftId, Guid DocumentId, int Version, ReplyDraftStatus Status, string? TemplateKey, int? DepartmentId, string? Actor, string Url);

public interface IWebhookPublisher
{
    /// <summary>
    /// Adds one delivery per matching endpoint to the DbContext. The caller's SaveChangesAsync commits them
    /// together with the change itself (outbox), so there are no lost or phantom events.
    /// </summary>
    /// <returns>The deliveries added (none when no endpoint matches or the dedup key was already used).</returns>
    Task<IReadOnlyList<WebhookDelivery>> PublishAsync(string eventType, object data, IReadOnlyCollection<int> departmentIds, CancellationToken ct, string? dedupKey = null);

    Task<IReadOnlyList<WebhookDelivery>> PublishDocumentAsync(string eventType, Document d, string? actor, CancellationToken ct, int? previousDepartmentId = null, string? dedupKey = null);

    Task<IReadOnlyList<WebhookDelivery>> PublishDraftAsync(string eventType, ReplyDraft draft, Document d, string? actor, CancellationToken ct);
}

public sealed class NullWebhookPublisher : IWebhookPublisher
{
    public static readonly NullWebhookPublisher Instance = new();
    private static readonly Task<IReadOnlyList<WebhookDelivery>> None = Task.FromResult<IReadOnlyList<WebhookDelivery>>([]);
    public Task<IReadOnlyList<WebhookDelivery>> PublishAsync(string eventType, object data, IReadOnlyCollection<int> departmentIds, CancellationToken ct, string? dedupKey = null) => None;
    public Task<IReadOnlyList<WebhookDelivery>> PublishDocumentAsync(string eventType, Document d, string? actor, CancellationToken ct, int? previousDepartmentId = null, string? dedupKey = null) => None;
    public Task<IReadOnlyList<WebhookDelivery>> PublishDraftAsync(string eventType, ReplyDraft draft, Document d, string? actor, CancellationToken ct) => None;
}

public class WebhookPublisher(IAppDbContext db, NotificationOptions notificationOptions) : IWebhookPublisher
{
    public async Task<IReadOnlyList<WebhookDelivery>> PublishAsync(string eventType, object data, IReadOnlyCollection<int> departmentIds, CancellationToken ct, string? dedupKey = null)
    {
        // Disabled endpoints don't collect a backlog while they are off.
        var endpoints = (await db.WebhookEndpoints.AsNoTracking().Where(e => e.Enabled).ToListAsync(ct))
            .Where(e => WebhookRules.Subscribes(e, eventType) && (e.DepartmentId is null || departmentIds.Contains(e.DepartmentId.Value)))
            .ToList();
        if (endpoints.Count == 0) return [];

        var envelope = new WebhookEnvelope(WebhookRules.NewEventId(), eventType, DateTime.UtcNow, WebhookRules.ApiVersion, data);
        var payload = envelope.Serialize();
        var added = new List<WebhookDelivery>(endpoints.Count);
        foreach (var e in endpoints)
        {
            if (dedupKey is not null && await db.WebhookDeliveries.AnyAsync(x => x.EndpointId == e.Id && x.DedupKey == dedupKey, ct)) continue;
            var delivery = new WebhookDelivery { EndpointId = e.Id, EventId = envelope.Id, EventType = eventType, Payload = payload, DedupKey = dedupKey };
            db.WebhookDeliveries.Add(delivery);
            added.Add(delivery);
        }
        return added;
    }

    public Task<IReadOnlyList<WebhookDelivery>> PublishDocumentAsync(string eventType, Document d, string? actor, CancellationToken ct, int? previousDepartmentId = null, string? dedupKey = null)
    {
        var a = d.Analysis;
        var data = new WebhookDocumentData(
            d.Id, d.Status, d.AssignedDepartmentId, previousDepartmentId, a?.SuggestedDepartmentId,
            a?.Priority, a?.Category, a?.Confidence, d.Handling, a?.Deadline, actor,
            $"{notificationOptions.AppBaseUrl.TrimEnd('/')}/documents/{d.Id}", d.Source);
        return PublishAsync(eventType, data, Audience.Of(d.AssignedDepartmentId, previousDepartmentId), ct, dedupKey);
    }

    public Task<IReadOnlyList<WebhookDelivery>> PublishDraftAsync(string eventType, ReplyDraft draft, Document d, string? actor, CancellationToken ct) =>
        PublishAsync(eventType,
            new WebhookDraftData(draft.Id, d.Id, draft.Version, draft.Status, draft.TemplateKey, d.AssignedDepartmentId, actor,
                $"{notificationOptions.AppBaseUrl.TrimEnd('/')}/documents/{d.Id}"),
            Audience.Of(d.AssignedDepartmentId), ct);
}

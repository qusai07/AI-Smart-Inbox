using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartInbox.Client.Webhooks;

/// <summary>The request didn't come from Smart Inbox (bad or missing signature, stale timestamp) or isn't a valid event.</summary>
public sealed class WebhookVerificationException(string message) : Exception(message);

/// <summary>
/// Verifies the <c>SmartInbox-Signature</c> header (<c>t=...,v1=...</c>) and parses the event.
/// Pass the raw request body exactly as received: re-serialized JSON won't match the signature.
/// </summary>
public static class WebhookVerifier
{
    /// <summary>Name of the signature header.</summary>
    public const string SignatureHeader = "SmartInbox-Signature";

    /// <summary>Requests signed longer ago than this (or this far in the future) are rejected as replays.</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Verifies and parses. Throws <see cref="WebhookVerificationException"/> on any problem.</summary>
    /// <param name="rawBody">The request body, unmodified.</param>
    /// <param name="signatureHeader">The <c>SmartInbox-Signature</c> header value.</param>
    /// <param name="secret">The endpoint's signing secret (<c>whsec_...</c>).</param>
    /// <param name="tolerance">Allowed clock difference; default 5 minutes.</param>
    /// <param name="now">Current time, for tests.</param>
    public static SmartInboxEvent Verify(string rawBody, string? signatureHeader, string secret, TimeSpan? tolerance = null, DateTimeOffset? now = null)
    {
        if (string.IsNullOrEmpty(secret)) throw new ArgumentException("The webhook secret is empty.", nameof(secret));
        if (string.IsNullOrWhiteSpace(signatureHeader)) throw new WebhookVerificationException($"Missing {SignatureHeader} header.");

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in signatureHeader.Split(','))
        {
            var kv = part.Trim().Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t" && long.TryParse(kv[1], out var t)) timestamp = t;
            else if (kv[0] == "v1") signatures.Add(kv[1]);
        }
        if (timestamp is null || signatures.Count == 0) throw new WebhookVerificationException($"Malformed {SignatureHeader} header.");

        var age = (now ?? DateTimeOffset.UtcNow) - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        if (age.Duration() > (tolerance ?? DefaultTolerance))
            throw new WebhookVerificationException("Signature timestamp is outside the allowed tolerance (possible replay).");

        var expected = Encoding.ASCII.GetBytes(Sign(secret, timestamp.Value, rawBody));
        // Any v1 may match: during a secret rotation Smart Inbox signs with both the new and the old secret.
        var valid = false;
        foreach (var candidate in signatures)
            valid |= CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(candidate));
        if (!valid) throw new WebhookVerificationException("Signature doesn't match. Check the secret and that the body is unmodified.");

        return Parse(rawBody);
    }

    /// <summary>Like <see cref="Verify"/>, but returns false instead of throwing.</summary>
    public static bool TryVerify(string rawBody, string? signatureHeader, string secret, out SmartInboxEvent? evt, TimeSpan? tolerance = null)
    {
        try
        {
            evt = Verify(rawBody, signatureHeader, secret, tolerance);
            return true;
        }
        catch (WebhookVerificationException)
        {
            evt = null;
            return false;
        }
    }

    /// <summary>Hex HMAC-SHA256 of <c>"{timestamp}.{body}"</c>. Useful for testing receivers.</summary>
    public static string Sign(string secret, long timestamp, string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();

    /// <summary>Parses an event body without checking the signature. Only use on bodies you already verified.</summary>
    public static SmartInboxEvent Parse(string rawBody)
    {
        Envelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(rawBody, SmartInboxClient.Json)
                       ?? throw new WebhookVerificationException("Empty event body.");
        }
        catch (JsonException ex)
        {
            throw new WebhookVerificationException($"The body is not a valid event: {ex.Message}");
        }
        if (envelope.Id is null || envelope.Type is null) throw new WebhookVerificationException("The event has no id or type.");

        try
        {
            return envelope.Type switch
            {
                "document.received" => Document<DocumentReceivedEvent>(envelope),
                "document.analyzed" => Document<DocumentAnalyzedEvent>(envelope),
                "document.analysis_failed" => Document<DocumentAnalysisFailedEvent>(envelope),
                "document.routed" => Document<DocumentRoutedEvent>(envelope),
                "document.acknowledged" => Document<DocumentAcknowledgedEvent>(envelope),
                "document.closed" => Document<DocumentClosedEvent>(envelope),
                "document.reopened" => Document<DocumentReopenedEvent>(envelope),
                "deadline.approaching" => Document<DeadlineApproachingEvent>(envelope),
                "draft.ready" => Draft<DraftReadyEvent>(envelope),
                "draft.approved" => Draft<DraftApprovedEvent>(envelope),
                "document.linked" => new DocumentLinkedEvent
                {
                    Id = envelope.Id, Type = envelope.Type, CreatedAt = envelope.CreatedAt, ApiVersion = envelope.ApiVersion ?? "",
                    Data = envelope.Data.Deserialize<LinkEventData>(SmartInboxClient.Json) ?? throw new JsonException("data is null"),
                },
                "register.issued" => new RegisterIssuedEvent
                {
                    Id = envelope.Id, Type = envelope.Type, CreatedAt = envelope.CreatedAt, ApiVersion = envelope.ApiVersion ?? "",
                    Data = envelope.Data.Deserialize<RegisterEventData>(SmartInboxClient.Json) ?? throw new JsonException("data is null"),
                },
                "webhook.ping" => new WebhookPingEvent
                {
                    Id = envelope.Id, Type = envelope.Type, CreatedAt = envelope.CreatedAt, ApiVersion = envelope.ApiVersion ?? "", Data = envelope.Data,
                },
                _ => new UnknownEvent
                {
                    Id = envelope.Id, Type = envelope.Type, CreatedAt = envelope.CreatedAt, ApiVersion = envelope.ApiVersion ?? "", Data = envelope.Data,
                },
            };
        }
        catch (JsonException ex)
        {
            throw new WebhookVerificationException($"The {envelope.Type} data is not valid: {ex.Message}");
        }
    }

    private static T Document<T>(Envelope e) where T : DocumentEvent, new() => new()
    {
        Id = e.Id!, Type = e.Type!, CreatedAt = e.CreatedAt, ApiVersion = e.ApiVersion ?? "",
        Data = e.Data.Deserialize<DocumentEventData>(SmartInboxClient.Json) ?? throw new JsonException("data is null"),
    };

    private static T Draft<T>(Envelope e) where T : DraftEvent, new() => new()
    {
        Id = e.Id!, Type = e.Type!, CreatedAt = e.CreatedAt, ApiVersion = e.ApiVersion ?? "",
        Data = e.Data.Deserialize<DraftEventData>(SmartInboxClient.Json) ?? throw new JsonException("data is null"),
    };

    private sealed record Envelope(string? Id, string? Type, DateTimeOffset CreatedAt, string? ApiVersion, JsonElement Data);
}

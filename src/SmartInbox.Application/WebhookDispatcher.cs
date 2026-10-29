using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartInbox.Domain;

namespace SmartInbox.Application;

/// <summary>Delivers pending webhook rows. Called every few seconds by a background worker.</summary>
public class WebhookDispatcher(
    IAppDbContext db,
    IWebhookTransport transport,
    ISecretProtector secrets,
    TimeProvider clock,
    ILogger<WebhookDispatcher> logger,
    IUserDirectory? directory = null,
    IEmailSender? email = null)
{
    public const int BatchSize = 50;
    public const int MaxConcurrencyPerEndpoint = 4;
    public const string UserAgent = "SmartInbox-Webhooks/1.0";

    /// <returns>How many deliveries were attempted.</returns>
    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var batch = await db.WebhookDeliveries
            .Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAt <= now
                        && db.WebhookEndpoints.Any(e => e.Id == d.EndpointId && e.Enabled))
            .OrderBy(d => d.Id)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (batch.Count > 0) await SendAsync(batch, ct);
        return batch.Count;
    }

    /// <summary>Sends one delivery right away, whatever its schedule (test pings, manual redelivery).</summary>
    public async Task DeliverNowAsync(WebhookDelivery delivery, CancellationToken ct) => await SendAsync([delivery], ct);

    private async Task SendAsync(IReadOnlyList<WebhookDelivery> batch, CancellationToken ct)
    {
        var ids = batch.Select(d => d.EndpointId).Distinct().ToList();
        var endpoints = await db.WebhookEndpoints.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);

        // HTTP calls run in parallel (a few per endpoint); the DbContext is only touched afterwards, on this thread.
        var results = new ConcurrentDictionary<long, WebhookResponse>();
        await Task.WhenAll(batch.GroupBy(d => d.EndpointId).Select(group =>
            Parallel.ForEachAsync(group, new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrencyPerEndpoint, CancellationToken = ct },
                async (d, token) => results[d.Id] = await AttemptAsync(endpoints[group.Key], d, token))));

        var disabled = new List<WebhookEndpoint>();
        foreach (var d in batch)
            if (Apply(endpoints[d.EndpointId], d, results[d.Id])) disabled.Add(endpoints[d.EndpointId]);
        await db.SaveChangesAsync(ct);

        foreach (var e in disabled) await NotifyAdminsAsync(e, ct);
    }

    private async Task<WebhookResponse> AttemptAsync(WebhookEndpoint endpoint, WebhookDelivery d, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        List<string> signingSecrets;
        try
        {
            signingSecrets = [secrets.Unprotect(endpoint.Secret)];
            if (endpoint.PreviousSecret is not null && endpoint.PreviousSecretExpiresAt > now.UtcDateTime)
                signingSecrets.Add(secrets.Unprotect(endpoint.PreviousSecret));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook endpoint {Id}: signing secret can't be decrypted", endpoint.Id);
            return new WebhookResponse(null, null, "The signing secret can't be decrypted (were the data protection keys lost?). Rotate the secret.", 0);
        }

        var headers = new Dictionary<string, string>
        {
            [WebhookSignature.EventIdHeader] = d.EventId,
            [WebhookSignature.EventTypeHeader] = d.EventType,
            [WebhookSignature.Header] = WebhookSignature.HeaderValue(signingSecrets, now.ToUnixTimeSeconds(), d.Payload),
            ["User-Agent"] = UserAgent,
        };
        try
        {
            return await transport.SendAsync(new WebhookRequest(endpoint.Url, headers, d.Payload), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new WebhookResponse(null, null, ex.Message, 0);
        }
    }

    /// <returns>True when this failure switched the endpoint off.</returns>
    private bool Apply(WebhookEndpoint e, WebhookDelivery d, WebhookResponse r)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        d.Attempts++;
        d.LastStatusCode = r.StatusCode;
        d.LastResponseSnippet = Truncate(r.BodySnippet, WebhookRules.MaxResponseSnippet);
        d.DurationMs = r.DurationMs;

        if (r.Succeeded)
        {
            d.Status = WebhookDeliveryStatus.Delivered;
            d.DeliveredAt = now;
            d.LastError = null;
            e.ConsecutiveFailures = 0;
            return false;
        }

        d.LastError = Truncate(r.Error ?? $"HTTP {r.StatusCode}", 1000);
        logger.LogWarning("Webhook {DeliveryId} ({Event}) to endpoint {EndpointId} failed (attempt {Attempt}): {Error}",
            d.Id, d.EventType, e.Id, d.Attempts, d.LastError);
        if (d.Attempts >= WebhookRules.MaxAttempts) d.Status = WebhookDeliveryStatus.Failed;
        else d.NextAttemptAt = now + WebhookRules.Backoff[d.Attempts - 1];

        e.ConsecutiveFailures++;
        if (!e.Enabled || e.ConsecutiveFailures < WebhookRules.DisableAfterFailures) return false;
        e.Enabled = false;
        e.DisabledReason = Truncate($"Switched off on {now:yyyy-MM-dd HH:mm} UTC after {e.ConsecutiveFailures} failed attempts in a row. Last error: {d.LastError}", 500);
        logger.LogWarning("Webhook endpoint {EndpointId} ({Url}) disabled after {Failures} consecutive failures", e.Id, e.Url, e.ConsecutiveFailures);
        return true;
    }

    private async Task NotifyAdminsAsync(WebhookEndpoint e, CancellationToken ct)
    {
        if (directory is null || email is null) return;
        try
        {
            foreach (var to in await directory.AdminEmailsAsync(ct))
            {
                var text = $"Smart Inbox stopped sending webhooks to {e.Url} ({e.Description ?? "no description"}).\n\n{e.DisabledReason}\n\n" +
                           "Fix the receiver, then turn the endpoint back on in Administration → Webhooks. Failed deliveries can be redelivered there.";
                var html = $"<p>Smart Inbox stopped sending webhooks to <b>{Html(e.Url)}</b> ({Html(e.Description ?? "no description")}).</p>" +
                           $"<p>{Html(e.DisabledReason)}</p><p>Fix the receiver, then turn the endpoint back on in Administration → Webhooks. " +
                           "Failed deliveries can be redelivered there.</p>";
                await email.SendAsync(new EmailMessage(to, "Webhook endpoint disabled", html, text), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not email admins about disabled webhook endpoint {EndpointId}", e.Id);
        }
    }

    private static string Html(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}

/// <summary>
/// Fires deadline.approaching for open routed documents whose deadline is close. Safe to run often:
/// the dedup key (document, department, deadline) means once per endpoint, and again only after re-routing or a new deadline.
/// </summary>
public class WebhookDeadlineScanner(IAppDbContext db, IWebhookPublisher publisher, WebhookOptions options, TimeProvider clock, ILogger<WebhookDeadlineScanner> logger)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var until = today.AddDays(options.DeadlineWarningDays);
        var due = await db.Documents.Include(d => d.Analysis)
            .Where(d => d.AssignedDepartmentId != null && d.Handling != HandlingStatus.Closed
                        && d.Analysis != null && d.Analysis.Deadline >= today && d.Analysis.Deadline <= until)
            .ToListAsync(ct);

        var queued = 0;
        foreach (var d in due)
        {
            var key = $"deadline:{d.Id:N}:{d.AssignedDepartmentId}:{d.Analysis!.Deadline:yyyy-MM-dd}";
            var added = await publisher.PublishDocumentAsync(WebhookEvents.DeadlineApproaching, d, "system", ct, dedupKey: key);
            if (added.Count == 0) continue;
            try
            {
                await db.SaveChangesAsync(ct);
                queued += added.Count;
            }
            catch (DbUpdateException)
            {
                // Another instance queued the same key first (unique index). That's fine.
                foreach (var x in added) db.WebhookDeliveries.Entry(x).State = EntityState.Detached;
            }
        }

        if (queued > 0) logger.LogInformation("Queued {Count} deadline.approaching webhook(s)", queued);
        return queued;
    }
}

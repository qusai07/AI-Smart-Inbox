using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Domain;

namespace SmartInbox.Application;

public record WebhookEndpointRequest(string Url, string? Description, IReadOnlyList<string>? Events, int? DepartmentId, bool Enabled = true);

public record WebhookLastDelivery(WebhookDeliveryStatus Status, int? StatusCode, DateTime At);

public record WebhookEndpointDto(
    int Id, string Url, string? Description, IReadOnlyList<string> Events, int? DepartmentId, bool Enabled,
    int ConsecutiveFailures, string? DisabledReason, string? CreatedBy, DateTime CreatedAt, bool RotationInProgress,
    WebhookLastDelivery? LastDelivery, int Deliveries7d, double? SuccessRate7d);

/// <summary>The only time a signing secret is returned.</summary>
public record WebhookSecretDto(WebhookEndpointDto Endpoint, string Secret);

public record WebhookDeliveryDto(
    long Id, string EventId, string EventType, WebhookDeliveryStatus Status, int Attempts, DateTime? NextAttemptAt,
    int? LastStatusCode, string? LastError, string? LastResponseSnippet, int? DurationMs,
    DateTime CreatedAt, DateTime? DeliveredAt, string Payload)
{
    public static WebhookDeliveryDto From(WebhookDelivery d) => new(
        d.Id, d.EventId, d.EventType, d.Status, d.Attempts, d.Status == WebhookDeliveryStatus.Pending ? d.NextAttemptAt : null,
        d.LastStatusCode, d.LastError, d.LastResponseSnippet, d.DurationMs, d.CreatedAt, d.DeliveredAt, d.Payload);
}

public class WebhookEndpointRequestValidator : AbstractValidator<WebhookEndpointRequest>
{
    public WebhookEndpointRequestValidator(WebhookOptions options)
    {
        RuleFor(x => x.Url).Custom((url, ctx) =>
        {
            if (WebhookRules.ValidateUrl(url, options) is { } error) ctx.AddFailure(error);
        });
        RuleFor(x => x.Description).MaximumLength(200);
        RuleForEach(x => x.Events)
            .Must(e => e == WebhookEvents.All || WebhookEvents.Subscribable.Contains(e))
            .WithMessage(e => $"Unknown event. Use * or: {string.Join(", ", WebhookEvents.Subscribable)}.");
    }
}

/// <summary>Managing webhook endpoints from the admin page.</summary>
public class WebhookAdminService(IAppDbContext db, ISecretProtector secrets, WebhookDispatcher dispatcher, ICurrentUser user, TimeProvider clock)
{
    public static readonly TimeSpan RotationOverlap = TimeSpan.FromHours(24);
    public const int DeliveryPageSize = 25;

    public enum Outcome { Ok, NotFound, UnknownDepartment }

    public async Task<IReadOnlyList<WebhookEndpointDto>> ListAsync(CancellationToken ct)
    {
        var endpoints = await db.WebhookEndpoints.AsNoTracking().OrderBy(e => e.Id).ToListAsync(ct);
        var since = Now.AddDays(-7);
        var stats = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.CreatedAt >= since && d.Status != WebhookDeliveryStatus.Pending)
            .GroupBy(d => d.EndpointId)
            .Select(g => new { g.Key, Total = g.Count(), Delivered = g.Count(d => d.Status == WebhookDeliveryStatus.Delivered) })
            .ToDictionaryAsync(x => x.Key, ct);
        var last = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.Attempts > 0)
            .GroupBy(d => d.EndpointId)
            .Select(g => g.OrderByDescending(d => d.Id).First())
            .ToDictionaryAsync(d => d.EndpointId, ct);

        return endpoints.Select(e =>
        {
            var s = stats.GetValueOrDefault(e.Id);
            var l = last.GetValueOrDefault(e.Id);
            return ToDto(e,
                l is null ? null : new WebhookLastDelivery(l.Status, l.LastStatusCode, l.DeliveredAt ?? l.CreatedAt),
                s?.Total ?? 0, s is { Total: > 0 } ? (double)s.Delivered / s.Total : null);
        }).ToList();
    }

    /// <summary>Null when the department doesn't exist. The request must already be validated.</summary>
    public async Task<WebhookSecretDto?> CreateAsync(WebhookEndpointRequest request, CancellationToken ct)
    {
        if (!await DepartmentExistsAsync(request.DepartmentId, ct)) return null;
        var secret = WebhookRules.NewSecret();
        var e = new WebhookEndpoint
        {
            Url = request.Url.Trim(), Description = Clean(request.Description), Secret = secrets.Protect(secret),
            Events = WebhookRules.FormatEvents(request.Events), DepartmentId = request.DepartmentId,
            Enabled = request.Enabled, CreatedBy = user.Name,
        };
        db.WebhookEndpoints.Add(e);
        await db.SaveChangesAsync(ct);
        return new WebhookSecretDto(ToDto(e), secret);
    }

    public async Task<Outcome> UpdateAsync(int id, WebhookEndpointRequest request, CancellationToken ct)
    {
        var e = await db.WebhookEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return Outcome.NotFound;
        if (!await DepartmentExistsAsync(request.DepartmentId, ct)) return Outcome.UnknownDepartment;

        e.Url = request.Url.Trim();
        e.Description = Clean(request.Description);
        e.Events = WebhookRules.FormatEvents(request.Events);
        e.DepartmentId = request.DepartmentId;
        if (request.Enabled && !e.Enabled)
        {
            // Turning it back on is a fresh start.
            e.ConsecutiveFailures = 0;
            e.DisabledReason = null;
        }
        e.Enabled = request.Enabled;
        await db.SaveChangesAsync(ct);
        return Outcome.Ok;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var e = await db.WebhookEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return false;
        db.WebhookDeliveries.RemoveRange(db.WebhookDeliveries.Where(d => d.EndpointId == id));
        db.WebhookEndpoints.Remove(e);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>New secret; the old one keeps signing too for 24 h so receivers can switch without dropping events.</summary>
    public async Task<WebhookSecretDto?> RotateSecretAsync(int id, CancellationToken ct)
    {
        var e = await db.WebhookEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return null;
        var secret = WebhookRules.NewSecret();
        e.PreviousSecret = e.Secret;
        e.PreviousSecretExpiresAt = Now + RotationOverlap;
        e.Secret = secrets.Protect(secret);
        await db.SaveChangesAsync(ct);
        return new WebhookSecretDto(ToDto(e), secret);
    }

    /// <summary>Sends webhook.ping now and returns the result (works on disabled endpoints too, to test a fix).</summary>
    public async Task<WebhookDeliveryDto?> PingAsync(int id, CancellationToken ct)
    {
        var e = await db.WebhookEndpoints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return null;
        var envelope = new WebhookEnvelope(WebhookRules.NewEventId(), WebhookEvents.Ping, Now, WebhookRules.ApiVersion,
            new { message = "Test event from Smart Inbox", endpointId = e.Id, sentBy = user.Name });
        return await SendNowAsync(new WebhookDelivery { EndpointId = e.Id, EventId = envelope.Id, EventType = envelope.Type, Payload = envelope.Serialize() }, ct);
    }

    /// <summary>Sends the same event again (same id and payload) as a new delivery.</summary>
    public async Task<WebhookDeliveryDto?> RedeliverAsync(long deliveryId, CancellationToken ct)
    {
        var d = await db.WebhookDeliveries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == deliveryId, ct);
        if (d is null) return null;
        return await SendNowAsync(new WebhookDelivery { EndpointId = d.EndpointId, EventId = d.EventId, EventType = d.EventType, Payload = d.Payload }, ct);
    }

    public async Task<PagedResult<WebhookDeliveryDto>?> DeliveriesAsync(int endpointId, int page, CancellationToken ct)
    {
        if (!await db.WebhookEndpoints.AnyAsync(e => e.Id == endpointId, ct)) return null;
        var query = db.WebhookDeliveries.AsNoTracking().Where(d => d.EndpointId == endpointId);
        page = Math.Max(1, page);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(d => d.Id).Skip((page - 1) * DeliveryPageSize).Take(DeliveryPageSize).ToListAsync(ct);
        return new PagedResult<WebhookDeliveryDto>(items.Select(WebhookDeliveryDto.From).ToList(), total, page, DeliveryPageSize);
    }

    private async Task<WebhookDeliveryDto> SendNowAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        db.WebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);
        await dispatcher.DeliverNowAsync(delivery, ct);
        return WebhookDeliveryDto.From(delivery);
    }

    private async Task<bool> DepartmentExistsAsync(int? id, CancellationToken ct) =>
        id is null || await db.Departments.AnyAsync(d => d.Id == id, ct);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private WebhookEndpointDto ToDto(WebhookEndpoint e, WebhookLastDelivery? last = null, int total = 0, double? rate = null) => new(
        e.Id, e.Url, e.Description, WebhookRules.ParseEvents(e.Events), e.DepartmentId, e.Enabled, e.ConsecutiveFailures,
        e.DisabledReason, e.CreatedBy, e.CreatedAt, e.PreviousSecretExpiresAt > Now, last, total, rate);
}

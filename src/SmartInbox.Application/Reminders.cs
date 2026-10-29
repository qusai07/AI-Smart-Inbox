using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartInbox.Domain;

namespace SmartInbox.Application;

public class ReminderOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Local hour (0–23) after which the daily digest goes out.</summary>
    public int DigestHour { get; set; } = 8;
    /// <summary>Items due within this many days (and all overdue items) are listed in the digest.</summary>
    public int DueSoonDays { get; set; } = 3;
    /// <summary>IANA or Windows time zone id used to decide what "today" and "08:00" mean.</summary>
    public string TimeZone { get; set; } = "Asia/Dubai";
}

/// <summary>An open item with a deadline, as listed in reminders.</summary>
public record DueItem(Guid DocumentId, string Title, Priority Priority, DateOnly Deadline, string? ReferenceNumber);

/// <summary>
/// Schedules daily digests and overdue escalations into the notification outbox.
/// Safe to run as often as you like: each email has a dedup key, so it is queued at most once.
/// </summary>
public class ReminderService(IAppDbContext db, ReminderOptions options, NotificationOptions notificationOptions, TimeProvider clock, ILogger<ReminderService> logger)
{
    public record RunResult(int Digests, int Escalations);

    public async Task<RunResult> RunAsync(CancellationToken ct)
    {
        var (today, hour) = LocalNow();
        var departments = await db.Departments.AsNoTracking().ToListAsync(ct);
        var open = await OpenItemsWithDeadlineAsync(today.AddDays(options.DueSoonDays), ct);

        var digests = 0;
        if (hour >= options.DigestHour)
        {
            foreach (var d in departments.Where(d => !string.IsNullOrWhiteSpace(d.Email)))
            {
                if (!open.Any(i => i.DepartmentId == d.Id)) continue;
                if (await QueueOnceAsync(new Notification
                    {
                        Kind = NotificationKind.Digest, DepartmentId = d.Id, To = d.Email!,
                        DedupKey = $"digest:{d.Id}:{today:yyyy-MM-dd}",
                    }, ct)) digests++;
            }
        }

        var escalations = 0;
        foreach (var item in open.Where(i => i.Deadline < today))
        {
            var department = departments.FirstOrDefault(d => d.Id == item.DepartmentId);
            var to = department?.HeadEmail ?? notificationOptions.DeskEmail;
            if (string.IsNullOrWhiteSpace(to)) continue;

            // Once per document, department and deadline: re-routing or a new deadline escalates again.
            var queued = await QueueOnceAsync(new Notification
            {
                Kind = NotificationKind.Escalation, DocumentId = item.DocumentId, DepartmentId = item.DepartmentId, To = to,
                DedupKey = $"escalation:{item.DocumentId:N}:{item.DepartmentId}:{item.Deadline:yyyy-MM-dd}",
            }, ct, new DocumentEvent
            {
                DocumentId = item.DocumentId, Type = DocumentEventType.Escalated, Actor = "system", DepartmentId = item.DepartmentId,
                Note = $"{today.DayNumber - item.Deadline.DayNumber} day(s) overdue → {to}",
            });
            if (queued) escalations++;
        }

        if (digests + escalations > 0)
            logger.LogInformation("Reminders queued: {Digests} digest(s), {Escalations} escalation(s)", digests, escalations);
        return new RunResult(digests, escalations);
    }

    /// <summary>Open (not closed) routed items for one department with a deadline on or before <paramref name="until"/>.</summary>
    public async Task<IReadOnlyList<DueItem>> DueItemsAsync(int departmentId, CancellationToken ct)
    {
        var (today, _) = LocalNow();
        return (await OpenItemsWithDeadlineAsync(today.AddDays(options.DueSoonDays), ct))
            .Where(i => i.DepartmentId == departmentId)
            .Select(i => new DueItem(i.DocumentId, i.Title, i.Priority, i.Deadline, i.ReferenceNumber))
            .OrderBy(i => i.Deadline)
            .ToList();
    }

    public DateOnly Today() => LocalNow().Today;

    private record OpenItem(Guid DocumentId, int DepartmentId, string Title, Priority Priority, DateOnly Deadline, string? ReferenceNumber);

    private async Task<List<OpenItem>> OpenItemsWithDeadlineAsync(DateOnly until, CancellationToken ct) =>
        await db.Documents.AsNoTracking()
            .Where(d => d.AssignedDepartmentId != null && d.Handling != HandlingStatus.Closed
                        && d.Analysis != null && d.Analysis.Deadline != null && d.Analysis.Deadline <= until)
            .Select(d => new OpenItem(d.Id, d.AssignedDepartmentId!.Value, d.Analysis!.Sender ?? d.FileName,
                d.Analysis.Priority, d.Analysis.Deadline!.Value, d.Analysis.ReferenceNumber))
            .ToListAsync(ct);

    private async Task<bool> QueueOnceAsync(Notification n, CancellationToken ct, DocumentEvent? timeline = null)
    {
        if (await db.Notifications.AnyAsync(x => x.DedupKey == n.DedupKey, ct)) return false;
        db.Notifications.Add(n);
        if (timeline is not null) db.DocumentEvents.Add(timeline);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another instance queued the same key first (unique index). That's fine.
            db.Notifications.Entry(n).State = EntityState.Detached;
            if (timeline is not null) db.DocumentEvents.Entry(timeline).State = EntityState.Detached;
            return false;
        }
    }

    private (DateOnly Today, int Hour) LocalNow()
    {
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ResolveZone(options.TimeZone));
        return (DateOnly.FromDateTime(now.DateTime), now.Hour);
    }

    private static TimeZoneInfo ResolveZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }
}

/// <summary>Daily digest: a department's items that are overdue or due soon.</summary>
public static class DigestEmail
{
    public static EmailMessage Build(string to, Department department, IReadOnlyList<DueItem> items, DateOnly today, string appBaseUrl)
    {
        var overdue = items.Count(i => i.Deadline < today);
        var subject = overdue > 0
            ? $"{department.NameEn}: {overdue} overdue, {items.Count - overdue} due soon"
            : $"{department.NameEn}: {items.Count} item(s) due soon";

        static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");
        string When(DateOnly d) => (d.DayNumber - today.DayNumber) switch
        {
            < 0 and var n => $"Overdue {-n}d · متأخر",
            0 => "Due today · اليوم",
            1 => "Due tomorrow · غدًا",
            var n => $"Due in {n} days",
        };

        var text = new StringBuilder($"{department.NameEn} / {department.NameAr}: items needing attention\n\n");
        var rows = new StringBuilder();
        foreach (var i in items)
        {
            var link = $"{appBaseUrl.TrimEnd('/')}/documents/{i.DocumentId}";
            var late = i.Deadline < today;
            text.AppendLine($"- {When(i.Deadline)} | {i.Title} | {i.ReferenceNumber ?? "-"} | {link}");
            rows.Append($"""
                <tr>
                  <td style="padding:10px 12px;border-top:1px solid #e3e8ec;white-space:nowrap;font-weight:600;color:{(late ? "#dc2626" : "#d97706")}">{E(When(i.Deadline))}</td>
                  <td style="padding:10px 12px;border-top:1px solid #e3e8ec"><a href="{E(link)}" style="color:#0f766e;font-weight:600;text-decoration:none" dir="auto">{E(i.Title)}</a>
                    <div style="font-size:12px;color:#8d99a6">{E(i.ReferenceNumber)} · {i.Priority} · {i.Deadline:d MMM yyyy}</div></td>
                </tr>
                """);
        }

        var html = $"""
            <!doctype html>
            <html><body style="margin:0;padding:24px;background:#f3f5f7;font-family:Segoe UI,Arial,sans-serif;color:#15202b">
              <table role="presentation" width="100%" style="max-width:640px;margin:0 auto;background:#fff;border:1px solid #e3e8ec;border-radius:12px">
                <tr><td style="padding:20px 24px">
                  <div style="font-size:12px;color:#8d99a6;text-transform:uppercase;letter-spacing:.06em">Smart Inbox · Daily digest · الملخص اليومي</div>
                  <div style="font-size:20px;font-weight:700;margin-top:4px">{E(department.NameEn)} <span style="color:#8d99a6;font-weight:400">/</span> <span dir="rtl">{E(department.NameAr)}</span></div>
                  <div style="margin-top:6px;color:#5d6b7a">{overdue} overdue · {items.Count - overdue} due within the next days</div>
                </td></tr>
                <tr><td style="padding:0 12px 16px"><table role="presentation" width="100%" style="border-collapse:collapse;font-size:14px">{rows}</table></td></tr>
                <tr><td style="padding:14px 24px;border-top:1px solid #e3e8ec;font-size:12px;color:#8d99a6">
                  Sent once a day while your department has open items with close or past deadlines.
                </td></tr>
              </table>
            </body></html>
            """;
        return new EmailMessage(to, subject, html, text.ToString());
    }
}

/// <summary>Escalation to the department head for one overdue item.</summary>
public static class EscalationEmail
{
    public static EmailMessage Build(string to, Document doc, Department department, DateOnly today, string appBaseUrl)
    {
        var a = doc.Analysis;
        var title = a?.Sender ?? doc.FileName;
        var deadline = a?.Deadline ?? today;
        var days = today.DayNumber - deadline.DayNumber;
        var link = $"{appBaseUrl.TrimEnd('/')}/documents/{doc.Id}";
        var status = doc.Handling == HandlingStatus.Acknowledged ? "In progress (acknowledged)" : "Not yet acknowledged";
        var subject = $"[Overdue {days}d] {department.NameEn}: {title}";

        static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");
        var text = $"""
            Escalation: an item routed to {department.NameEn} is {days} day(s) past its deadline and still open.

            Document: {title}
            Reference: {a?.ReferenceNumber ?? "-"}
            Deadline: {deadline:yyyy-MM-dd}
            Status: {status}

            {a?.SummaryEn}

            Open: {link}
            """;

        var html = $"""
            <!doctype html>
            <html><body style="margin:0;padding:24px;background:#f3f5f7;font-family:Segoe UI,Arial,sans-serif;color:#15202b">
              <table role="presentation" width="100%" style="max-width:620px;margin:0 auto;background:#fff;border:1px solid #f5c2c2;border-radius:12px">
                <tr><td style="padding:20px 24px;background:#fdecec;border-radius:12px 12px 0 0">
                  <div style="font-size:12px;color:#dc2626;text-transform:uppercase;letter-spacing:.06em;font-weight:700">Escalation · تصعيد · {days} day(s) overdue</div>
                  <div style="font-size:20px;font-weight:700;margin-top:4px">{E(department.NameEn)} <span style="color:#8d99a6;font-weight:400">/</span> <span dir="rtl">{E(department.NameAr)}</span></div>
                </td></tr>
                <tr><td style="padding:20px 24px">
                  <div style="font-size:17px;font-weight:600" dir="auto">{E(title)}</div>
                  <table role="presentation" style="margin-top:14px;font-size:14px;border-collapse:collapse">
                    <tr><td style="color:#8d99a6;padding:3px 16px 3px 0">Reference</td><td>{E(a?.ReferenceNumber)}</td></tr>
                    <tr><td style="color:#8d99a6;padding:3px 16px 3px 0">Deadline</td><td style="color:#dc2626;font-weight:600">{deadline:d MMM yyyy}</td></tr>
                    <tr><td style="color:#8d99a6;padding:3px 16px 3px 0">Status</td><td>{E(status)}</td></tr>
                  </table>
                  <p style="font-size:15px;line-height:1.6;margin:16px 0" dir="ltr">{E(a?.SummaryEn)}</p>
                  <p style="font-size:15px;line-height:1.8;margin:0 0 20px" dir="rtl">{E(a?.SummaryAr)}</p>
                  <a href="{E(link)}" style="display:inline-block;background:#0f766e;color:#fff;text-decoration:none;padding:10px 18px;border-radius:8px;font-weight:600">Open document · فتح المستند</a>
                </td></tr>
                <tr><td style="padding:14px 24px;border-top:1px solid #e3e8ec;font-size:12px;color:#8d99a6">
                  You receive this as head of {E(department.NameEn)}. Sent once per deadline.
                </td></tr>
              </table>
            </body></html>
            """;
        return new EmailMessage(to, subject, html, text);
    }
}

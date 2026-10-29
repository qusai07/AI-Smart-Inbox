using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartInbox.Domain;

namespace SmartInbox.Application;

public class NotificationOptions
{
    /// <summary>Public URL of the web app, used for the "Open document" link in emails.</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>
    /// Correspondence desk mailbox. Receives "closed" emails when the person who routed a document
    /// is unknown (documents routed before sign-in existed). Optional.
    /// </summary>
    public string? DeskEmail { get; set; }
}

/// <summary>Builds the bilingual "department closed it" email for the person who routed the document.</summary>
public static class ClosedEmail
{
    public static EmailMessage Build(string to, Document doc, Department department, string appBaseUrl)
    {
        var a = doc.Analysis;
        var sender = a?.Sender ?? doc.FileName;
        var link = $"{appBaseUrl.TrimEnd('/')}/documents/{doc.Id}";
        var closedBy = doc.HandlingBy ?? department.NameEn;
        var subject = Truncate($"Closed by {department.NameEn}: {sender}", 200);

        var text = new StringBuilder()
            .AppendLine($"{department.NameEn} / {department.NameAr} closed this correspondence.")
            .AppendLine()
            .AppendLine($"Document: {sender}")
            .AppendLine($"Reference: {a?.ReferenceNumber ?? "-"}")
            .AppendLine($"Closed by: {closedBy}")
            .AppendLine($"Closed at: {doc.HandlingUpdatedAt:yyyy-MM-dd HH:mm} UTC")
            .AppendLine($"Note: {doc.HandlingNote ?? "-"}")
            .AppendLine()
            .AppendLine($"Open: {link}")
            .ToString();

        static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");

        var html = $"""
            <!doctype html>
            <html><body style="margin:0;padding:24px;background:#f3f5f7;font-family:Segoe UI,Arial,sans-serif;color:#15202b">
              <table role="presentation" width="100%" style="max-width:620px;margin:0 auto;background:#fff;border:1px solid #e3e8ec;border-radius:12px">
                <tr><td style="padding:20px 24px;border-bottom:1px solid #e3e8ec">
                  <div style="font-size:12px;color:#8d99a6;text-transform:uppercase;letter-spacing:.06em">Smart Inbox · Correspondence closed</div>
                  <div style="font-size:20px;font-weight:700;margin-top:4px">
                    <span style="display:inline-block;width:22px;height:22px;border-radius:50%;background:#e5f5ea;color:#15803d;text-align:center;line-height:22px;font-size:14px">✓</span>
                    {E(department.NameEn)} <span style="color:#8d99a6;font-weight:400">/</span> <span dir="rtl">{E(department.NameAr)}</span>
                  </div>
                </td></tr>
                <tr><td style="padding:20px 24px">
                  <div style="font-size:17px;font-weight:600" dir="auto">{E(sender)}</div>
                  <table role="presentation" style="margin-top:14px;font-size:14px;border-collapse:collapse">
                    <tr><td style="color:#8d99a6;padding:3px 16px 3px 0">Reference</td><td>{E(a?.ReferenceNumber)}</td></tr>
                    <tr><td style="color:#8d99a6;padding:3px 16px 3px 0">Closed by · أغلقه</td><td dir="auto">{E(closedBy)}</td></tr>
                    <tr><td style="color:#8d99a6;padding:3px 16px 3px 0">Closed at</td><td>{E(doc.HandlingUpdatedAt?.ToString("d MMM yyyy, HH:mm") + " UTC")}</td></tr>
                  </table>
                  <div style="margin:16px 0 20px;padding:12px 14px;border-inline-start:3px solid #15803d;background:#f6faf7;border-radius:4px;font-size:15px;line-height:1.6" dir="auto">
                    {E(doc.HandlingNote ?? "No closing note.")}
                  </div>
                  <a href="{E(link)}" style="display:inline-block;background:#0f766e;color:#fff;text-decoration:none;padding:10px 18px;border-radius:8px;font-weight:600">Open document · فتح المستند</a>
                </td></tr>
                <tr><td style="padding:14px 24px;border-top:1px solid #e3e8ec;font-size:12px;color:#8d99a6">
                  You receive this because you routed this correspondence. Nothing else is needed unless you want to re-route it.
                </td></tr>
              </table>
            </body></html>
            """;

        return new EmailMessage(to, subject, html, text);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>Builds the bilingual routing email. All document-derived text is HTML-encoded.</summary>
public static class RoutingEmail
{
    /// <param name="assigned">True for the "assigned to you" variant sent to a staff member.</param>
    public static EmailMessage Build(string to, Document doc, Department department, string appBaseUrl, bool assigned = false)
    {
        var a = doc.Analysis;
        var sender = a?.Sender ?? doc.FileName;
        var priority = a?.Priority ?? Priority.Normal;
        var link = $"{appBaseUrl.TrimEnd('/')}/documents/{doc.Id}";
        var urgentTag = priority is Priority.Urgent or Priority.High ? $"[{priority}] " : "";
        var subject = Truncate(assigned
            ? $"{urgentTag}Assigned to you: {sender}"
            : $"{urgentTag}New correspondence for {department.NameEn}: {sender}", 200);
        var eyebrow = assigned ? "Smart Inbox · Assigned to you · مُسند إليك" : "Smart Inbox · New correspondence";

        var text = new StringBuilder()
            .AppendLine(assigned
                ? $"This correspondence for {department.NameEn} was assigned to you."
                : $"New correspondence routed to {department.NameEn} / {department.NameAr}")
            .AppendLine()
            .AppendLine($"From: {sender}")
            .AppendLine($"Priority: {priority}")
            .AppendLine($"Category: {a?.Category.ToString() ?? "-"}")
            .AppendLine($"Reference: {a?.ReferenceNumber ?? "-"}")
            .AppendLine($"Deadline: {a?.Deadline?.ToString("yyyy-MM-dd") ?? "-"}")
            .AppendLine()
            .AppendLine(a?.SummaryEn ?? "")
            .AppendLine()
            .AppendLine(a?.SummaryAr ?? "")
            .AppendLine()
            .AppendLine($"Open: {link}")
            .ToString();

        static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");
        var priorityColor = priority switch
        {
            Priority.Urgent => "#dc2626", Priority.High => "#d97706", Priority.Low => "#8a96a3", _ => "#2563eb",
        };

        var html = $"""
            <!doctype html>
            <html><body style="margin:0;padding:24px;background:#f3f5f7;font-family:Segoe UI,Arial,sans-serif;color:#15202b">
              <table role="presentation" width="100%" style="max-width:620px;margin:0 auto;background:#fff;border:1px solid #e3e8ec;border-radius:12px">
                <tr><td style="padding:20px 24px;border-bottom:1px solid #e3e8ec">
                  <div style="font-size:12px;color:#8d99a6;text-transform:uppercase;letter-spacing:.06em">{eyebrow}</div>
                  <div style="font-size:20px;font-weight:700;margin-top:4px">{E(department.NameEn)} <span style="color:#8d99a6;font-weight:400">/</span> <span dir="rtl">{E(department.NameAr)}</span></div>
                </td></tr>
                <tr><td style="padding:20px 24px">
                  <div style="font-size:17px;font-weight:600" dir="auto">{E(sender)}</div>
                  <div style="margin-top:8px">
                    <span style="display:inline-block;padding:2px 10px;border-radius:999px;background:{priorityColor}1a;color:{priorityColor};font-size:12px;font-weight:600">{priority}</span>
                    <span style="display:inline-block;padding:2px 10px;border-radius:999px;background:#f2f4f6;color:#5d6b7a;font-size:12px">{E(a?.Category.ToString())}</span>
                  </div>
                  <table role="presentation" style="margin-top:16px;font-size:14px;border-collapse:collapse">
                    <tr><td style="color:#8d99a6;padding:3px 16px 3px 0">Reference</td><td>{E(a?.ReferenceNumber)}</td></tr>
                    <tr><td style="color:#8d99a6;padding:3px 16px 3px 0">Deadline</td><td style="font-weight:600">{E(a?.Deadline?.ToString("d MMM yyyy"))}</td></tr>
                  </table>
                  <p style="font-size:15px;line-height:1.6;margin:18px 0 8px" dir="ltr">{E(a?.SummaryEn)}</p>
                  <p style="font-size:15px;line-height:1.8;margin:0 0 20px" dir="rtl">{E(a?.SummaryAr)}</p>
                  <a href="{E(link)}" style="display:inline-block;background:#0f766e;color:#fff;text-decoration:none;padding:10px 18px;border-radius:8px;font-weight:600">Open document · فتح المستند</a>
                </td></tr>
                <tr><td style="padding:14px 24px;border-top:1px solid #e3e8ec;font-size:12px;color:#8d99a6">
                  Sent automatically because this correspondence was routed to your department. Please acknowledge it in Smart Inbox.
                </td></tr>
              </table>
            </body></html>
            """;

        return new EmailMessage(to, subject, html, text);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>Delivers pending outbox rows. Called repeatedly by a background worker.</summary>
public class NotificationDispatcher(
    IAppDbContext db,
    IEmailSender email,
    NotificationOptions options,
    ILogger<NotificationDispatcher> logger,
    ReminderService? reminders = null)
{
    public const int MaxAttempts = 5;
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];

    /// <returns>How many notifications were attempted.</returns>
    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var batch = await db.Notifications
            .Where(n => n.Status == NotificationStatus.Pending && n.NextAttemptAt <= now)
            .OrderBy(n => n.Id)
            .Take(20)
            .ToListAsync(ct);

        foreach (var n in batch)
        {
            n.Attempts++;
            try
            {
                var message = await BuildAsync(n, ct);
                if (message is null)
                {
                    // Digest with nothing left to report (items were closed since it was queued).
                    n.Status = NotificationStatus.Sent;
                    n.Error = "Nothing due; not sent.";
                }
                else
                {
                    await email.SendAsync(message, ct);
                    n.Status = NotificationStatus.Sent;
                    n.SentAt = DateTime.UtcNow;
                    n.Error = null;
                    if (n.DocumentId is { } documentId)
                        db.DocumentEvents.Add(new DocumentEvent
                        {
                            DocumentId = documentId, Type = DocumentEventType.EmailSent, Actor = "system", DepartmentId = n.DepartmentId, Note = n.To,
                        });
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning("Notification {Id} to {To} failed (attempt {Attempt}): {Message}", n.Id, n.To, n.Attempts, ex.Message);
                n.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                if (n.Attempts >= MaxAttempts)
                {
                    n.Status = NotificationStatus.Failed;
                    if (n.DocumentId is { } documentId)
                        db.DocumentEvents.Add(new DocumentEvent
                        {
                            DocumentId = documentId, Type = DocumentEventType.EmailFailed, Actor = "system",
                            DepartmentId = n.DepartmentId, Note = $"{n.To}: {n.Error}",
                        });
                }
                else n.NextAttemptAt = DateTime.UtcNow + Backoff[Math.Min(n.Attempts - 1, Backoff.Length - 1)];
            }
            await db.SaveChangesAsync(ct);
        }

        return batch.Count;
    }

    /// <summary>Builds the email from current data at send time. Null = nothing to send.</summary>
    private async Task<EmailMessage?> BuildAsync(Notification n, CancellationToken ct)
    {
        var department = await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == n.DepartmentId, ct)
                         ?? throw new InvalidOperationException("Department no longer exists.");

        if (n.Kind == NotificationKind.Digest)
        {
            if (reminders is null) throw new InvalidOperationException("Reminders are not configured.");
            var items = await reminders.DueItemsAsync(n.DepartmentId, ct);
            return items.Count == 0 ? null : DigestEmail.Build(n.To, department, items, reminders.Today(), options.AppBaseUrl);
        }

        var doc = await db.Documents.AsNoTracking().Include(d => d.Analysis).FirstOrDefaultAsync(d => d.Id == n.DocumentId, ct)
                  ?? throw new InvalidOperationException("Document no longer exists.");
        return n.Kind switch
        {
            NotificationKind.Closed => ClosedEmail.Build(n.To, doc, department, options.AppBaseUrl),
            NotificationKind.Escalation => EscalationEmail.Build(n.To, doc, department, reminders?.Today() ?? DateOnly.FromDateTime(DateTime.UtcNow), options.AppBaseUrl),
            NotificationKind.Assigned => RoutingEmail.Build(n.To, doc, department, options.AppBaseUrl, assigned: true),
            _ => RoutingEmail.Build(n.To, doc, department, options.AppBaseUrl),
        };
    }
}

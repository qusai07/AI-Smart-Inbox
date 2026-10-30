using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartInbox.Domain;

namespace SmartInbox.Application;

public class MailboxOptions
{
    public bool Enabled { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; } = 993;
    /// <summary>None | StartTls | SslOnConnect | Auto</summary>
    public string Security { get; set; } = "SslOnConnect";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string Folder { get; set; } = "INBOX";
    /// <summary>Imported emails are moved here (created if missing). Empty = leave them in place, only flag them.</summary>
    public string? ProcessedFolder { get; set; } = "Processed";
    public int PollSeconds { get; set; } = 60;
    public int MaxPerCheck { get; set; } = 25;
    /// <summary>Addresses or "@domain" entries; empty = anyone may send.</summary>
    public string[] AllowedSenders { get; set; } = [];
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Shown on documents ("Mailbox: …") and in the admin page.</summary>
    public string Address => Username ?? Host ?? "mailbox";
}

public static class MailboxRules
{
    public const int MaxBodyText = 4000;
    public const string AutoReplySkipped = "Automatic reply or delivery report";
    public const string SenderNotAllowed = "Sender is not in Mailbox:AllowedSenders";
    public const string NothingToImport = "No text and no supported attachment";

    public static bool IsAllowed(string? fromAddress, IReadOnlyCollection<string> allowed)
    {
        if (allowed.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(fromAddress)) return false;
        return allowed.Any(a => a.StartsWith('@')
            ? fromAddress.EndsWith(a, StringComparison.OrdinalIgnoreCase)
            : string.Equals(fromAddress, a, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>A message waiting in the mailbox: enough to decide whether to download it.</summary>
public record MailboxItem(uint Uid, string? MessageId);

public record MailAttachment(string FileName, byte[] Content);

/// <summary>
/// A downloaded email, already parsed by the mail client. <see cref="MessageId"/> is always set: when the email has
/// none, the client derives a stable one from its sender, date, subject and size.
/// </summary>
public record MailMessage(
    uint Uid, string MessageId, string? FromName, string? FromAddress, string? Subject, DateTimeOffset? Date,
    string? BodyText, bool IsAutoReply, IReadOnlyList<MailAttachment> Attachments, byte[] Raw,
    string? InReplyTo = null, IReadOnlyList<string>? References = null);

public record MailboxTestResult(bool Ok, int? Pending, string Message);

/// <summary>The shared mailbox (IMAP in production; a fake in tests).</summary>
public interface IMailboxClient
{
    /// <summary>Messages not yet processed (still in the folder, or not flagged), oldest first.</summary>
    Task<IReadOnlyList<MailboxItem>> ListPendingAsync(int max, CancellationToken ct);
    Task<MailMessage> FetchAsync(uint uid, CancellationToken ct);
    /// <summary>Moves the message to the processed folder (or flags it), so it isn't listed again.</summary>
    Task MarkProcessedAsync(uint uid, CancellationToken ct);
    Task<MailboxTestResult> TestAsync(CancellationToken ct);
}

public record MailboxRunResult(int Imported, int Skipped, int Failed, int AlreadyKnown);

/// <summary>
/// Turns new emails into documents. Exactly-once in practice: the email's record (unique Message-Id) is saved together
/// with its documents, and only then is the email moved on the server; if the move fails, the next run finds the
/// record and just moves it.
/// </summary>
public class MailboxImporter(
    IAppDbContext db, IMailboxClient mailbox, IFileStorage storage, DocumentService documents,
    MailboxOptions options, ILogger<MailboxImporter> logger)
{
    public async Task<MailboxRunResult> RunAsync(CancellationToken ct)
    {
        int imported = 0, skipped = 0, failed = 0, known = 0;
        foreach (var item in await mailbox.ListPendingAsync(Math.Max(1, options.MaxPerCheck), ct))
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await ImportOneAsync(item, ct);
            switch (outcome)
            {
                case InboundEmailStatus.Imported: imported++; break;
                case InboundEmailStatus.Skipped: skipped++; break;
                case InboundEmailStatus.Failed: failed++; break;
                default: known++; break;
            }
        }
        if (imported + skipped + failed > 0)
            logger.LogInformation("Mailbox {Mailbox}: {Imported} imported, {Skipped} skipped, {Failed} failed", options.Address, imported, skipped, failed);
        return new MailboxRunResult(imported, skipped, failed, known);
    }

    /// <summary>Null when the email was already handled (only its server-side move was repeated) or is waiting for an admin.</summary>
    private async Task<InboundEmailStatus?> ImportOneAsync(MailboxItem item, CancellationToken ct)
    {
        if (item.MessageId is { } listedId && await SettledAsync(listedId, item.Uid, ct)) return null;

        MailMessage message;
        try
        {
            message = await mailbox.FetchAsync(item.Uid, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return await RecordFailureAsync(item.MessageId ?? $"<uid-{item.Uid}@{options.Address}>", null, ex, ct);
        }
        if (item.MessageId is null && await SettledAsync(message.MessageId, item.Uid, ct)) return null;

        try
        {
            var status = await ImportAsync(message, ct);
            await MarkProcessedQuietlyAsync(item.Uid, ct);
            return status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return await RecordFailureAsync(message.MessageId, message, ex, ct);
        }
    }

    /// <summary>
    /// True when the email needs no work: already imported or skipped (then make sure it leaves the folder), or failed
    /// too often (left for an admin to retry).
    /// </summary>
    private async Task<bool> SettledAsync(string messageId, uint uid, CancellationToken ct)
    {
        var existing = await db.InboundEmails.AsNoTracking().FirstOrDefaultAsync(e => e.MessageId == messageId, ct);
        if (existing is null) return false;
        if (existing.Status is InboundEmailStatus.Imported or InboundEmailStatus.Skipped)
        {
            await MarkProcessedQuietlyAsync(uid, ct);
            return true;
        }
        return existing.Attempts >= options.MaxAttempts;
    }

    private async Task<InboundEmailStatus> ImportAsync(MailMessage m, CancellationToken ct)
    {
        var existing = await db.InboundEmails.FirstOrDefaultAsync(e => e.MessageId == m.MessageId, ct);
        var email = existing ?? new InboundEmail { MessageId = m.MessageId, Mailbox = options.Address };
        email.FromName = Limit(m.FromName, 200);
        email.FromAddress = Limit(m.FromAddress, 320);
        email.Subject = Limit(m.Subject, 500);
        email.SentAt = m.Date?.UtcDateTime;
        email.BodyText = Limit(m.BodyText?.Trim(), MailboxRules.MaxBodyText);
        email.InReplyTo = Limit(m.InReplyTo, 500);
        email.References = m.References is { Count: > 0 } refs ? Limit(string.Join(' ', refs.TakeLast(20)), 4000) : null;
        email.Attempts++;

        string? skipReason = m.IsAutoReply ? MailboxRules.AutoReplySkipped
            : !MailboxRules.IsAllowed(m.FromAddress, options.AllowedSenders) ? MailboxRules.SenderNotAllowed
            : null;

        // Letters: attachments of a type uploads accept, within the size limit. The rest is listed, not stored.
        var letters = new List<(MailAttachment File, string ContentType)>();
        var notImported = new List<string>();
        if (skipReason is null)
            foreach (var a in m.Attachments)
            {
                var type = UploadRules.ResolveContentType(a.FileName);
                if (type is null) notImported.Add($"{a.FileName}: unsupported type");
                else if (a.Content.Length == 0) notImported.Add($"{a.FileName}: empty");
                else if (a.Content.Length > UploadRules.MaxFileBytes) notImported.Add($"{a.FileName}: larger than 20 MB");
                else letters.Add((a, type));
            }
        if (skipReason is null && letters.Count == 0 && string.IsNullOrWhiteSpace(m.BodyText)) skipReason = MailboxRules.NothingToImport;

        email.StoragePath ??= await storage.SaveAsync(new MemoryStream(m.Raw), $"{SafeName(m.Subject)}.eml", ct);
        email.SkippedAttachments = notImported.Count == 0 ? null : JsonSerializer.Serialize(notImported);
        if (existing is null) db.InboundEmails.Add(email);

        if (skipReason is not null)
        {
            email.Status = InboundEmailStatus.Skipped;
            email.Reason = skipReason;
            await db.SaveChangesAsync(ct);
            return InboundEmailStatus.Skipped;
        }

        var uploadedBy = $"Mailbox: {options.Address}";
        var note = $"Received by email from {m.FromAddress ?? m.FromName ?? "unknown sender"}";
        var added = new List<Document>();
        if (letters.Count == 0)
        {
            // The email itself is the letter.
            added.Add(await documents.AddUploadAsync(new MemoryStream(m.Raw), $"{SafeName(m.Subject)}.eml", "message/rfc822", uploadedBy, ct,
                DocumentSource.Email, email.Id, note));
        }
        else
        {
            foreach (var (file, type) in letters)
                added.Add(await documents.AddUploadAsync(new MemoryStream(file.Content), file.FileName, type, uploadedBy, ct,
                    DocumentSource.Email, email.Id, note));
        }

        email.Status = InboundEmailStatus.Imported;
        email.Reason = null;
        email.DocumentIds = JsonSerializer.Serialize(added.Select(d => d.Id));
        await db.SaveChangesAsync(ct); // the email record and all its documents together
        foreach (var d in added) await documents.QueueAnalysisAsync(d.Id, ct);
        return InboundEmailStatus.Imported;
    }

    private async Task<InboundEmailStatus> RecordFailureAsync(string messageId, MailMessage? m, Exception ex, CancellationToken ct)
    {
        logger.LogWarning(ex, "Mailbox {Mailbox}: could not import {MessageId}", options.Address, messageId);
        Detach();
        var email = await db.InboundEmails.FirstOrDefaultAsync(e => e.MessageId == messageId, ct);
        if (email is null)
        {
            email = new InboundEmail
            {
                MessageId = messageId, Mailbox = options.Address, FromAddress = Limit(m?.FromAddress, 320), Subject = Limit(m?.Subject, 500),
                SentAt = m?.Date?.UtcDateTime, Attempts = 1,
            };
            db.InboundEmails.Add(email);
        }
        else email.Attempts++; // the count in ImportAsync was discarded with the rest of the failed save
        email.Status = InboundEmailStatus.Failed;
        email.Reason = Limit(ex.Message, 1000);
        await db.SaveChangesAsync(ct);
        return InboundEmailStatus.Failed;
    }

    /// <summary>After a failed save, forget the half-added email and documents so the failure can be recorded cleanly.</summary>
    private void Detach()
    {
        foreach (var e in db.InboundEmails.Local.ToList()) db.InboundEmails.Entry(e).State = EntityState.Detached;
        foreach (var d in db.Documents.Local.ToList()) db.Documents.Entry(d).State = EntityState.Detached;
        foreach (var e in db.DocumentEvents.Local.ToList()) db.DocumentEvents.Entry(e).State = EntityState.Detached;
        foreach (var w in db.WebhookDeliveries.Local.ToList()) db.WebhookDeliveries.Entry(w).State = EntityState.Detached;
    }

    private async Task MarkProcessedQuietlyAsync(uint uid, CancellationToken ct)
    {
        try
        {
            await mailbox.MarkProcessedAsync(uid, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Saved already; the next run finds the record and moves it then.
            logger.LogWarning(ex, "Mailbox {Mailbox}: could not move message {Uid} to the processed folder", options.Address, uid);
        }
    }

    private static string SafeName(string? subject)
    {
        var name = new string((subject ?? "email").Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '-').ToArray()).Trim();
        if (name.Length == 0) name = "email";
        return name.Length > 80 ? name[..80] : name;
    }

    private static string? Limit(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}

public record MailboxStatus(
    bool Enabled, bool Configured, string Address, string? Host, string Folder, string? ProcessedFolder, int PollSeconds,
    bool Running, DateTime? LastCheckAt, DateTime? NextCheckAt, MailboxRunResult? LastResult, string? LastError);

/// <summary>Runs one check at a time (the worker's schedule and "Check now" share it) and remembers how it went.</summary>
public interface IMailboxRunner
{
    MailboxStatus Status { get; }
    Task<MailboxRunResult> CheckNowAsync(CancellationToken ct);
    Task<MailboxTestResult> TestAsync(CancellationToken ct);
}

public record InboundEmailDto(
    Guid Id, string MessageId, string? FromName, string? FromAddress, string? Subject, DateTime? SentAt, DateTime ReceivedAt,
    InboundEmailStatus Status, string? Reason, int Attempts, IReadOnlyList<Guid> DocumentIds, IReadOnlyList<string> SkippedAttachments)
{
    public static InboundEmailDto From(InboundEmail e) => new(
        e.Id, e.MessageId, e.FromName, e.FromAddress, e.Subject, e.SentAt, e.ReceivedAt, e.Status, e.Reason, e.Attempts,
        string.IsNullOrEmpty(e.DocumentIds) ? [] : JsonSerializer.Deserialize<List<Guid>>(e.DocumentIds) ?? [],
        ReplyDraftRules.FromJson(e.SkippedAttachments));
}

/// <summary>The import log for admins, and putting a failed email back in line.</summary>
public class MailboxAdminService(IAppDbContext db)
{
    public const int PageSize = 25;

    public async Task<PagedResult<InboundEmailDto>> ImportsAsync(int page, InboundEmailStatus? status, CancellationToken ct)
    {
        var query = db.InboundEmails.AsNoTracking();
        if (status is { } s) query = query.Where(e => e.Status == s);
        page = Math.Max(1, page);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.ReceivedAt).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        return new PagedResult<InboundEmailDto>(items.Select(InboundEmailDto.From).ToList(), total, page, PageSize);
    }

    /// <summary>A failed email gets a fresh set of attempts on the next check. False when it doesn't exist or didn't fail.</summary>
    public async Task<bool> RetryAsync(Guid id, CancellationToken ct)
    {
        var email = await db.InboundEmails.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (email is not { Status: InboundEmailStatus.Failed }) return false;
        email.Attempts = 0;
        email.Reason = "Retry requested";
        await db.SaveChangesAsync(ct);
        return true;
    }
}

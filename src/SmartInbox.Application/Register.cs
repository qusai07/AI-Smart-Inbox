using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Domain;

namespace SmartInbox.Application;

public class RegisterOptions
{
    public string Prefix { get; set; } = "MDS";
    /// <summary>Tokens: {Prefix}, {Year}, {Seq:0000} (any number of zeros), {Dept} (the department's code).</summary>
    public string Format { get; set; } = "{Prefix}/OUT/{Year}/{Seq:0000}";
    /// <summary>Each department counts from 1 on its own (use with {Dept}); otherwise one sequence for the organisation.</summary>
    public bool SequencePerDepartment { get; set; }
}

/// <summary>"Today" in the organisation's time zone (Reminders:TimeZone), not UTC: letters are dated where they're written.</summary>
public static class OrganisationClock
{
    public static DateOnly Today(TimeProvider clock, ReminderOptions? options)
    {
        var now = clock.GetUtcNow();
        try
        {
            if (options?.TimeZone is { Length: > 0 } zone) now = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Unknown zone: UTC is close enough.
        }
        return DateOnly.FromDateTime(now.DateTime);
    }
}

public static partial class OutgoingNumber
{
    [GeneratedRegex(@"\{Seq(?::(?<pad>0+))?\}")]
    private static partial Regex Seq();

    public static string Format(string format, string prefix, int year, int sequence, string? departmentCode) =>
        Seq().Replace(format, m => sequence.ToString(new string('0', Math.Max(1, m.Groups["pad"].Length))))
            .Replace("{Prefix}", prefix)
            .Replace("{Year}", year.ToString())
            .Replace("{Dept}", departmentCode ?? "GEN");
}

public static class RegisterCsv
{
    public static byte[] Write(IEnumerable<OutgoingLetterDto> letters, IReadOnlyDictionary<int, string> departments)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Number,Issued (UTC),Issued by,Department,Recipient,Subject (EN),Subject (AR),Status,Sent (UTC),Sent via,Cancel reason,Incoming document");
        foreach (var l in letters)
            sb.AppendLine(string.Join(',', new[]
            {
                l.Number, l.IssuedAt.ToString("yyyy-MM-dd HH:mm"), l.IssuedBy, departments.GetValueOrDefault(l.DepartmentId), l.Recipient,
                l.SubjectEn, l.SubjectAr, l.Status.ToString(), l.SentAt?.ToString("yyyy-MM-dd HH:mm"), l.SentVia?.ToString(), l.CancelReason,
                l.DocumentId?.ToString(),
            }.Select(Cell)));
        // BOM so Excel opens the Arabic subjects as UTF-8.
        return [.. System.Text.Encoding.UTF8.GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(sb.ToString())];
    }

    /// <summary>Quoted, and text that a spreadsheet would run as a formula (=, +, -, @) is made plain text.</summary>
    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}

public record IssueRequest(int DepartmentId, string? Recipient, string? SubjectEn, string? SubjectAr, Guid? DocumentId = null);

public record MarkSentRequest(SentVia Via, DateTime? SentAt = null, string? Note = null);

public record CancelRequest(string Reason);

public record RegisterQuery(int? Year = null, int? Department = null, OutgoingStatus? Status = null, string? Q = null, int Page = 1, int PageSize = 50);

public record OutgoingLetterDto(
    Guid Id, string Number, int Year, int Sequence, int DepartmentId, string? Recipient, string? SubjectEn, string? SubjectAr,
    Guid? DocumentId, Guid? ReplyDraftId, DateTime IssuedAt, string? IssuedBy, OutgoingStatus Status,
    DateTime? SentAt, SentVia? SentVia, string? SentNote, DateTime? CancelledAt, string? CancelledBy, string? CancelReason)
{
    public static OutgoingLetterDto From(OutgoingLetter l) => new(
        l.Id, l.Number, l.Year, l.Sequence, l.DepartmentId, l.Recipient, l.SubjectEn, l.SubjectAr, l.DocumentId, l.ReplyDraftId,
        l.IssuedAt, l.IssuedBy, l.Status, l.SentAt, l.SentVia, l.SentNote, l.CancelledAt, l.CancelledBy, l.CancelReason);
}

public class IssueRequestValidator : AbstractValidator<IssueRequest>
{
    public IssueRequestValidator()
    {
        RuleFor(x => x.DepartmentId).GreaterThan(0);
        RuleFor(x => x.Recipient).NotEmpty().MaximumLength(300);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.SubjectEn) || !string.IsNullOrWhiteSpace(x.SubjectAr))
            .WithName("Subject").WithMessage("Give a subject in English or Arabic.");
        RuleFor(x => x.SubjectEn).MaximumLength(ReplyDraftRules.MaxSubject);
        RuleFor(x => x.SubjectAr).MaximumLength(ReplyDraftRules.MaxSubject);
    }
}

public class MarkSentRequestValidator : AbstractValidator<MarkSentRequest>
{
    public MarkSentRequestValidator() => RuleFor(x => x.Note).MaximumLength(500);
}

public class RegisterQueryValidator : AbstractValidator<RegisterQuery>
{
    public RegisterQueryValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100).When(x => x.Year is not null);
        RuleFor(x => x.Q).MaximumLength(200);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 500);
    }
}

public class CancelRequestValidator : AbstractValidator<CancelRequest>
{
    public CancelRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

/// <summary>
/// The outgoing register. Numbers are issued in the same save as the entry (and whatever the caller changes alongside,
/// e.g. an approved reply), so a failure never burns a number; a clash with someone issuing at the same moment is retried.
/// </summary>
public class RegisterService(
    IAppDbContext db, ICurrentUser user, RegisterOptions options, TimeProvider clock, ReminderOptions? reminders = null,
    IWebhookPublisher? webhooks = null, NotificationOptions? links = null)
{
    /// <summary>Plenty for real use; a burst of simultaneous issues each retry after a short random pause.</summary>
    private const int MaxTries = 60;

    public enum Outcome { Ok, NotFound, Forbidden, UnknownDepartment, NotAllowedNow }

    public record Result(Outcome Outcome, OutgoingLetterDto? Letter = null);

    /// <summary>
    /// Issues the next number and saves the entry. <paramref name="alongside"/> runs before each save attempt with the
    /// letter (its number set) to make related changes in the same transaction; it must recompute from scratch, since a
    /// clash makes it run again with the next number.
    /// </summary>
    public async Task<Result> IssueAsync(IssueRequest request, CancellationToken ct, Guid? replyDraftId = null, Action<OutgoingLetter>? alongside = null)
    {
        if (user.RestrictedToDepartment is { } own && own != request.DepartmentId) return new(Outcome.Forbidden);
        var department = await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DepartmentId, ct);
        if (department is null) return new(Outcome.UnknownDepartment);

        var year = OrganisationClock.Today(clock, reminders).Year;
        var scope = options.SequencePerDepartment ? department.Id : 0;
        for (var attempt = 1; ; attempt++)
        {
            var counter = await db.RegisterCounters.FirstOrDefaultAsync(c => c.Year == year && c.Scope == scope, ct);
            if (counter is null)
            {
                counter = new RegisterCounter { Year = year, Scope = scope, Next = 1 };
                db.RegisterCounters.Add(counter);
            }
            var sequence = counter.Next;
            counter.Next = sequence + 1;

            var letter = new OutgoingLetter
            {
                Number = OutgoingNumber.Format(options.Format, options.Prefix, year, sequence, department.Code),
                Year = year, Sequence = sequence, DepartmentId = department.Id,
                Recipient = Clean(request.Recipient), SubjectEn = Clean(request.SubjectEn), SubjectAr = Clean(request.SubjectAr),
                DocumentId = request.DocumentId, ReplyDraftId = replyDraftId, IssuedBy = user.Name,
            };
            db.OutgoingLetters.Add(letter);
            var timeline = request.DocumentId is { } docId
                ? db.DocumentEvents.Add(new DocumentEvent
                {
                    DocumentId = docId, Type = DocumentEventType.OutgoingIssued, Actor = user.Name, DepartmentId = department.Id, Note = letter.Number,
                }).Entity
                : null;
            var deliveries = webhooks is null ? [] : await webhooks.PublishAsync(WebhookEvents.RegisterIssued,
                new WebhookRegisterData(letter.Id, letter.Number, letter.DepartmentId, letter.DocumentId, letter.ReplyDraftId, letter.IssuedBy,
                    letter.DocumentId is { } d && links is not null ? $"{links.AppBaseUrl.TrimEnd('/')}/documents/{d}" : null),
                Audience.Of(department.Id), ct);
            alongside?.Invoke(letter);
            try
            {
                await db.SaveChangesAsync(ct);
                return new(Outcome.Ok, OutgoingLetterDto.From(letter));
            }
            catch (DbUpdateException) when (attempt < MaxTries)
            {
                // Someone else took this number (or created this year's counter) first: forget ours and try the next one.
                db.OutgoingLetters.Entry(letter).State = EntityState.Detached;
                db.RegisterCounters.Entry(counter).State = EntityState.Detached;
                if (timeline is not null) db.DocumentEvents.Entry(timeline).State = EntityState.Detached;
                foreach (var w in deliveries) db.WebhookDeliveries.Entry(w).State = EntityState.Detached;
                await Task.Delay(Random.Shared.Next(1, 5 + Math.Min(attempt, 10) * 5), ct);
            }
        }
    }

    public async Task<PagedResult<OutgoingLetterDto>> ListAsync(RegisterQuery q, CancellationToken ct)
    {
        var query = Visible();
        if (q.Year is { } year) query = query.Where(l => l.Year == year);
        if (q.Department is { } dep) query = query.Where(l => l.DepartmentId == dep);
        if (q.Status is { } status) query = query.Where(l => l.Status == status);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(l => l.Number.Contains(term) || (l.Recipient != null && l.Recipient.Contains(term))
                                     || (l.SubjectEn != null && l.SubjectEn.Contains(term)) || (l.SubjectAr != null && l.SubjectAr.Contains(term)));
        }
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, MaxExport);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(l => l.Year).ThenByDescending(l => l.Sequence).ThenByDescending(l => l.IssuedAt)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<OutgoingLetterDto>(items.Select(OutgoingLetterDto.From).ToList(), total, page, size);
    }

    /// <summary>Everything matching the filters (ignoring paging, up to <see cref="MaxExport"/> rows) as CSV.</summary>
    public async Task<byte[]> ExportCsvAsync(RegisterQuery q, CancellationToken ct)
    {
        var all = await ListAsync(q with { Page = 1, PageSize = MaxExport }, ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.NameEn, ct);
        return RegisterCsv.Write(all.Items, departments);
    }

    public const int MaxExport = 10_000;

    public async Task<OutgoingLetterDto?> GetAsync(Guid id, CancellationToken ct) =>
        await Visible().FirstOrDefaultAsync(l => l.Id == id, ct) is { } l ? OutgoingLetterDto.From(l) : null;

    public async Task<Result> MarkSentAsync(Guid id, MarkSentRequest request, CancellationToken ct)
    {
        var letter = await TrackedVisibleAsync(id, ct);
        if (letter is null) return new(Outcome.NotFound);
        if (letter.Status == OutgoingStatus.Cancelled) return new(Outcome.NotAllowedNow);
        letter.Status = OutgoingStatus.Sent;
        letter.SentVia = request.Via;
        letter.SentAt = request.SentAt ?? clock.GetUtcNow().UtcDateTime;
        letter.SentNote = Clean(request.Note);
        await db.SaveChangesAsync(ct);
        return new(Outcome.Ok, OutgoingLetterDto.From(letter));
    }

    /// <summary>The number stays in the register, marked cancelled with the reason; it is never issued again.</summary>
    public async Task<Result> CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var letter = await TrackedVisibleAsync(id, ct);
        if (letter is null) return new(Outcome.NotFound);
        if (letter.Status == OutgoingStatus.Cancelled) return new(Outcome.NotAllowedNow);
        letter.Status = OutgoingStatus.Cancelled;
        letter.CancelledAt = clock.GetUtcNow().UtcDateTime;
        letter.CancelledBy = user.Name;
        letter.CancelReason = reason.Trim();
        await db.SaveChangesAsync(ct);
        return new(Outcome.Ok, OutgoingLetterDto.From(letter));
    }

    /// <summary>Everything for the desk; department staff see their department's letters only.</summary>
    private IQueryable<OutgoingLetter> Visible() =>
        user.RestrictedToDepartment is { } own
            ? db.OutgoingLetters.AsNoTracking().Where(l => l.DepartmentId == own)
            : db.OutgoingLetters.AsNoTracking();

    private Task<OutgoingLetter?> TrackedVisibleAsync(Guid id, CancellationToken ct) =>
        user.RestrictedToDepartment is { } own
            ? db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == id && l.DepartmentId == own, ct)
            : db.OutgoingLetters.FirstOrDefaultAsync(l => l.Id == id, ct);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

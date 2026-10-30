using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Domain;

namespace SmartInbox.Application;

public class DraftingOptions
{
    /// <summary>Drafting uses templates only: free, offline, no AI (see docs/EVALS-drafting.md for why).</summary>
    public bool Enabled { get; set; } = true;
    public string OrganisationNameEn { get; set; } = "Ministry of Digital Services – Demo";
    public string OrganisationNameAr { get; set; } = "وزارة الخدمات الرقمية – تجريبي";
}

public static class ReplyDraftRules
{
    /// <summary>Marks something a person must still write. Approval is blocked while any remain.</summary>
    public const string Placeholder = "[●]";
    public const int MaxInstructions = 1000;
    public const int MaxSubject = 300;
    public const int MaxBody = 20_000;

    public static int CountPlaceholders(params string?[] texts) =>
        texts.Sum(t => t is null ? 0 : (t.Length - t.Replace(Placeholder, "").Length) / Placeholder.Length);

    public static string? ToJson(IReadOnlyCollection<string>? items) => items is null || items.Count == 0 ? null : JsonSerializer.Serialize(items);

    public static IReadOnlyList<string> FromJson(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
}

public record RequestDraftRequest(string? TemplateKey = null, string? Instructions = null);

public record UpdateDraftRequest(string SubjectEn, string BodyEn, string SubjectAr, string BodyAr);

public record RejectDraftRequest(string? Reason = null);

public record ReplyDraftDto(
    Guid Id, Guid DocumentId, int Version, bool IsLatest, ReplyDraftStatus Status, string? TemplateKey, string? Engine,
    string? Instructions, string? SubjectEn, string? BodyEn, string? SubjectAr, string? BodyAr,
    IReadOnlyList<string> MissingInfo, IReadOnlyList<string> Commitments, int Placeholders,
    string? RequestedBy, DateTime CreatedAt, string? EditedBy, DateTime? EditedAt, string? DecidedBy, DateTime? DecidedAt, string? Error)
{
    public static ReplyDraftDto From(ReplyDraft d, bool isLatest) => new(
        d.Id, d.DocumentId, d.Version, isLatest, d.Status, d.TemplateKey, d.Engine, d.Instructions,
        d.SubjectEn, d.BodyEn, d.SubjectAr, d.BodyAr,
        ReplyDraftRules.FromJson(d.MissingInfo), ReplyDraftRules.FromJson(d.Commitments),
        ReplyDraftRules.CountPlaceholders(d.SubjectEn, d.BodyEn, d.SubjectAr, d.BodyAr),
        d.RequestedBy, d.CreatedAt, d.EditedBy, d.EditedAt, d.DecidedBy, d.DecidedAt, d.Error);
}

public class RequestDraftRequestValidator : AbstractValidator<RequestDraftRequest>
{
    public RequestDraftRequestValidator()
    {
        RuleFor(x => x.TemplateKey).MaximumLength(50).Matches("^[a-z0-9-]+$").When(x => x.TemplateKey is not null)
            .WithMessage("Template keys are lowercase letters, digits and dashes.");
        RuleFor(x => x.Instructions).MaximumLength(ReplyDraftRules.MaxInstructions);
    }
}

public class UpdateDraftRequestValidator : AbstractValidator<UpdateDraftRequest>
{
    public UpdateDraftRequestValidator()
    {
        RuleFor(x => x.SubjectEn).NotNull().MaximumLength(ReplyDraftRules.MaxSubject);
        RuleFor(x => x.SubjectAr).NotNull().MaximumLength(ReplyDraftRules.MaxSubject);
        RuleFor(x => x.BodyEn).NotNull().MaximumLength(ReplyDraftRules.MaxBody);
        RuleFor(x => x.BodyAr).NotNull().MaximumLength(ReplyDraftRules.MaxBody);
    }
}

public class RejectDraftRequestValidator : AbstractValidator<RejectDraftRequest>
{
    public RejectDraftRequestValidator() => RuleFor(x => x.Reason).MaximumLength(1000);
}

/// <summary>Background generation of drafts (one at a time per draft id).</summary>
public interface IDraftQueue
{
    ValueTask EnqueueAsync(Guid draftId, CancellationToken ct = default);
}

/// <summary>
/// Requesting, editing, approving and rejecting reply drafts. Whoever can see and handle the document
/// (the desk, or staff of the department it is routed to) can work on its reply.
/// </summary>
public class ReplyDraftService(
    IAppDbContext db, ICurrentUser user, DraftingOptions options, IDraftQueue queue, IInboxNotifier notifier, ReplyTemplateCatalog? templates = null)
{
    private readonly ReplyTemplateCatalog _templates = templates ?? new ReplyTemplateCatalog();

    public enum Outcome { Ok, NotFound, NotRouted, Disabled, Busy, NotLatest, NotEditable, Incomplete, UnknownTemplate }

    public record Result(Outcome Outcome, ReplyDraftDto? Draft = null);

    public async Task<Result> RequestAsync(Guid documentId, RequestDraftRequest request, CancellationToken ct)
    {
        if (!options.Enabled) return new(Outcome.Disabled);
        if (Clean(request.TemplateKey) is { } key && _templates.Find(key) is null) return new(Outcome.UnknownTemplate);
        var doc = await db.Documents.VisibleTo(user).FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return new(Outcome.NotFound);
        if (doc.AssignedDepartmentId is null) return new(Outcome.NotRouted);

        var latest = await LatestAsync(documentId, ct);
        if (latest is { Status: ReplyDraftStatus.Requested or ReplyDraftStatus.Generating })
            return new(Outcome.Busy, ReplyDraftDto.From(latest, true));

        var draft = new ReplyDraft
        {
            DocumentId = documentId, Version = (latest?.Version ?? 0) + 1,
            TemplateKey = Clean(request.TemplateKey), Instructions = Clean(request.Instructions), RequestedBy = user.Name,
        };
        db.ReplyDrafts.Add(draft);
        AddEvent(doc, DocumentEventType.DraftRequested, draft.Instructions);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(draft.Id, ct);
        await notifier.DraftChangedAsync(documentId, draft.Id, draft.Status, Audience.Of(doc.AssignedDepartmentId), ct);
        return new(Outcome.Ok, ReplyDraftDto.From(draft, true));
    }

    /// <summary>All versions, newest first. Null when the document isn't visible.</summary>
    public async Task<IReadOnlyList<ReplyDraftDto>?> ListAsync(Guid documentId, CancellationToken ct)
    {
        if (!await db.Documents.VisibleTo(user).AnyAsync(d => d.Id == documentId, ct)) return null;
        var drafts = await db.ReplyDrafts.AsNoTracking().Where(d => d.DocumentId == documentId).OrderByDescending(d => d.Version).ToListAsync(ct);
        return drafts.Select((d, i) => ReplyDraftDto.From(d, i == 0)).ToList();
    }

    public async Task<ReplyDraftDto?> GetAsync(Guid draftId, CancellationToken ct)
    {
        var (draft, _) = await LoadAsync(draftId, ct);
        return draft is null ? null : ReplyDraftDto.From(draft, await IsLatestAsync(draft, ct));
    }

    /// <summary>A person's edit becomes a new version; the version it was based on stays as it was.</summary>
    public async Task<Result> UpdateAsync(Guid draftId, UpdateDraftRequest request, CancellationToken ct)
    {
        var (draft, doc) = await LoadAsync(draftId, ct);
        if (draft is null || doc is null) return new(Outcome.NotFound);
        if (!await IsLatestAsync(draft, ct)) return new(Outcome.NotLatest);
        if (draft.Status != ReplyDraftStatus.Ready) return new(Outcome.NotEditable);

        var edited = new ReplyDraft
        {
            DocumentId = draft.DocumentId, Version = draft.Version + 1, Status = ReplyDraftStatus.Ready,
            TemplateKey = draft.TemplateKey, Engine = "Edited", Instructions = draft.Instructions,
            SubjectEn = request.SubjectEn.Trim(), BodyEn = request.BodyEn.Trim(), SubjectAr = request.SubjectAr.Trim(), BodyAr = request.BodyAr.Trim(),
            MissingInfo = draft.MissingInfo, Commitments = draft.Commitments,
            RequestedBy = draft.RequestedBy, EditedBy = user.Name, EditedAt = DateTime.UtcNow,
        };
        db.ReplyDrafts.Add(edited);
        AddEvent(doc, DocumentEventType.DraftEdited, $"version {edited.Version}");
        await db.SaveChangesAsync(ct);
        await notifier.DraftChangedAsync(doc.Id, edited.Id, edited.Status, Audience.Of(doc.AssignedDepartmentId), ct);
        return new(Outcome.Ok, ReplyDraftDto.From(edited, true));
    }

    /// <summary>Only the latest, complete version: every [●] filled in, both languages present.</summary>
    public async Task<Result> ApproveAsync(Guid draftId, CancellationToken ct)
    {
        var (draft, doc) = await LoadAsync(draftId, ct);
        if (draft is null || doc is null) return new(Outcome.NotFound);
        if (!await IsLatestAsync(draft, ct)) return new(Outcome.NotLatest);
        if (draft.Status != ReplyDraftStatus.Ready) return new(Outcome.NotEditable);
        if (ReplyDraftRules.CountPlaceholders(draft.SubjectEn, draft.BodyEn, draft.SubjectAr, draft.BodyAr) > 0
            || new[] { draft.SubjectEn, draft.BodyEn, draft.SubjectAr, draft.BodyAr }.Any(string.IsNullOrWhiteSpace))
            return new(Outcome.Incomplete, ReplyDraftDto.From(draft, true));

        return await DecideAsync(draft, doc, ReplyDraftStatus.Approved, DocumentEventType.DraftApproved, $"version {draft.Version}", ct);
    }

    public async Task<Result> RejectAsync(Guid draftId, string? reason, CancellationToken ct)
    {
        var (draft, doc) = await LoadAsync(draftId, ct);
        if (draft is null || doc is null) return new(Outcome.NotFound);
        if (!await IsLatestAsync(draft, ct)) return new(Outcome.NotLatest);
        if (draft.Status != ReplyDraftStatus.Ready) return new(Outcome.NotEditable);
        return await DecideAsync(draft, doc, ReplyDraftStatus.Rejected, DocumentEventType.DraftRejected, Clean(reason), ct);
    }

    private async Task<Result> DecideAsync(ReplyDraft draft, Document doc, ReplyDraftStatus status, DocumentEventType type, string? note, CancellationToken ct)
    {
        draft.Status = status;
        draft.DecidedBy = user.Name;
        draft.DecidedAt = DateTime.UtcNow;
        AddEvent(doc, type, note);
        await db.SaveChangesAsync(ct);
        await notifier.DraftChangedAsync(doc.Id, draft.Id, draft.Status, Audience.Of(doc.AssignedDepartmentId), ct);
        return new(Outcome.Ok, ReplyDraftDto.From(draft, true));
    }

    /// <summary>The draft and its document, or nulls when either doesn't exist or isn't visible to the user.</summary>
    private async Task<(ReplyDraft? Draft, Document? Document)> LoadAsync(Guid draftId, CancellationToken ct)
    {
        var draft = await db.ReplyDrafts.FirstOrDefaultAsync(d => d.Id == draftId, ct);
        if (draft is null) return (null, null);
        var doc = await db.Documents.VisibleTo(user).FirstOrDefaultAsync(d => d.Id == draft.DocumentId, ct);
        return doc is null ? (null, null) : (draft, doc);
    }

    private Task<ReplyDraft?> LatestAsync(Guid documentId, CancellationToken ct) =>
        db.ReplyDrafts.Where(d => d.DocumentId == documentId).OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct);

    private async Task<bool> IsLatestAsync(ReplyDraft draft, CancellationToken ct) =>
        !await db.ReplyDrafts.AnyAsync(d => d.DocumentId == draft.DocumentId && d.Version > draft.Version, ct);

    private void AddEvent(Document doc, DocumentEventType type, string? note) =>
        db.DocumentEvents.Add(new DocumentEvent
        {
            DocumentId = doc.Id, Type = type, Actor = user.Name, DepartmentId = doc.AssignedDepartmentId,
            Note = note is { Length: > 1000 } ? note[..1000] : note,
        });

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

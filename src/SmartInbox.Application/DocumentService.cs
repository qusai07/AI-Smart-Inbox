using Microsoft.EntityFrameworkCore;
using SmartInbox.Domain;

namespace SmartInbox.Application;

public class DocumentService(
    IAppDbContext db,
    IFileStorage storage,
    IDocumentQueue queue,
    IInboxNotifier notifier,
    ICurrentUser? currentUser = null,
    NotificationOptions? notificationOptions = null)
{
    private readonly ICurrentUser _user = currentUser ?? SystemUser.Instance;
    private readonly NotificationOptions _notificationOptions = notificationOptions ?? new NotificationOptions();

    /// <summary>Documents the current user may see: everything, or only their department's.</summary>
    private IQueryable<Document> Visible(IQueryable<Document> query) => query.VisibleTo(_user);

    public async Task<Document> UploadAsync(Stream content, string fileName, string contentType, string? uploadedBy, CancellationToken ct)
    {
        var path = await storage.SaveAsync(content, fileName, ct);
        var doc = new Document
        {
            FileName = Path.GetFileName(fileName),
            ContentType = contentType,
            StoragePath = path,
            UploadedBy = _user.Name ?? uploadedBy,
            Status = DocumentStatus.Processing,
        };
        db.Documents.Add(doc);
        db.DocumentEvents.Add(new DocumentEvent { DocumentId = doc.Id, Type = DocumentEventType.Uploaded, Actor = doc.UploadedBy });
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(doc.Id, ct);
        return doc;
    }

    public Task<Document> UploadTextAsync(string text, string? uploadedBy, CancellationToken ct)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return UploadAsync(new MemoryStream(bytes), $"pasted-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt", "text/plain", uploadedBy, ct);
    }

    public async Task<PagedResult<DocumentListItemDto>> ListAsync(DocumentQuery q, CancellationToken ct)
    {
        var query = Visible(db.Documents.AsNoTracking());

        if (q.Status is { } status) query = query.Where(d => d.Status == status);
        if (q.Priority is { } priority) query = query.Where(d => d.Analysis != null && d.Analysis.Priority == priority);
        if (q.Department is { } dep)
            query = query.Where(d => (d.AssignedDepartmentId ?? d.Analysis!.SuggestedDepartmentId) == dep);
        if (q.Assigned is { } assigned) query = query.Where(d => d.AssignedDepartmentId == assigned);
        if (q.Handling is { } handling) query = query.Where(d => d.Handling == handling);
        if (q.Mine) query = _user.Id is { } me ? query.Where(d => d.AssigneeId == me) : query.Where(d => false);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(d =>
                d.FileName.Contains(term) ||
                (d.ExtractedText != null && d.ExtractedText.Contains(term)) ||
                (d.Analysis != null && (
                    (d.Analysis.SummaryEn != null && d.Analysis.SummaryEn.Contains(term)) ||
                    (d.Analysis.SummaryAr != null && d.Analysis.SummaryAr.Contains(term)) ||
                    (d.Analysis.Sender != null && d.Analysis.Sender.Contains(term)) ||
                    (d.Analysis.ReferenceNumber != null && d.Analysis.ReferenceNumber.Contains(term)))));
        }

        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(d => d.UploadedAt)
            .Skip((page - 1) * size)
            .Take(size)
            // Not every document has an analysis yet (processing / failed): every field checks it explicitly.
            .Select(d => new DocumentListItemDto(
                d.Id, d.FileName, d.Status, d.UploadedAt,
                d.Analysis != null ? d.Analysis.SummaryEn : null,
                d.Analysis != null ? d.Analysis.SummaryAr : null,
                d.Analysis != null ? d.Analysis.Category : null,
                d.Analysis != null ? d.Analysis.Priority : null,
                d.Analysis != null ? d.Analysis.Sender : null,
                d.Analysis != null ? d.Analysis.Deadline : null,
                d.Analysis != null ? d.Analysis.SuggestedDepartmentId : null,
                d.AssignedDepartmentId,
                d.Analysis != null ? d.Analysis.Confidence : null,
                d.Handling,
                d.AssigneeId,
                d.AssigneeName))
            .ToListAsync(ct);

        return new PagedResult<DocumentListItemDto>(items, total, page, size);
    }

    public async Task<DocumentDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var d = await Visible(db.Documents.AsNoTracking())
            .Include(x => x.Analysis)
            .Include(x => x.ActionItems)
            .Include(x => x.RoutingDecisions)
            .Include(x => x.Notifications)
            .Include(x => x.Events)
            .Include(x => x.Comments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return null;

        var a = d.Analysis;
        var r = d.RoutingDecisions.OrderByDescending(x => x.DecidedAt).FirstOrDefault();
        return new DocumentDetailDto(
            d.Id, d.FileName, d.ContentType, d.Status, d.UploadedAt, d.ExtractedText, d.ErrorMessage,
            a is null ? null : new AnalysisDto(
                a.SummaryEn, a.SummaryAr, a.Category, a.Priority, a.Language, a.Sender, a.Recipient,
                a.ReferenceNumber, a.DocumentDate, a.Deadline, a.SuggestedDepartmentId, a.Confidence,
                a.Reasoning, a.ModelUsed, a.TokensUsed),
            d.ActionItems.OrderBy(x => x.Id).Select(x => new ActionItemDto(x.Id, x.Description, x.DueDate)).ToList(),
            r is null ? null : new RoutingDecisionDto(r.SuggestedDepartmentId, r.FinalDepartmentId, r.WasOverridden, r.Note, r.DecidedBy, r.DecidedAt),
            d.AssignedDepartmentId is { } dep && d.Handling is { } h ? new HandlingDto(dep, h, d.HandlingUpdatedAt, d.HandlingNote, d.HandlingBy) : null,
            d.Notifications.OrderByDescending(n => n.CreatedAt)
                .Select(n => new NotificationDto(n.DepartmentId, n.Kind, n.To, n.Status, n.Attempts, n.Error, n.CreatedAt, n.SentAt)).ToList(),
            d.Events.OrderBy(e => e.At).ThenBy(e => e.Id)
                .Select(e => new DocumentEventDto(e.Type, e.Actor, e.DepartmentId, e.Note, e.At)).ToList(),
            d.AssigneeId is null ? null : new AssigneeDto(d.AssigneeId, d.AssigneeName ?? ""),
            d.Comments.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).Select(CommentDto.From).ToList());
    }

    public async Task<(Stream Content, string ContentType, string FileName)?> OpenFileAsync(Guid id, CancellationToken ct)
    {
        var d = await Visible(db.Documents.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id, ct);
        return d is null ? null : (storage.Open(d.StoragePath), d.ContentType, d.FileName);
    }

    public enum ReanalyzeOutcome { Accepted, NotFound, Busy }

    public async Task<ReanalyzeOutcome> ReanalyzeAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return ReanalyzeOutcome.NotFound;
        // Already queued or running (double-click, two clerks, client retry). The queue de-duplicates too.
        if (d.Status is DocumentStatus.Processing or DocumentStatus.Uploaded) return ReanalyzeOutcome.Busy;
        d.Status = DocumentStatus.Processing;
        d.ErrorMessage = null;
        await db.SaveChangesAsync(ct);
        await notifier.DocumentStatusChangedAsync(id, d.Status, Audience.Of(d.AssignedDepartmentId), ct);
        await queue.EnqueueAsync(id, ct);
        return ReanalyzeOutcome.Accepted;
    }

    public enum RouteOutcome { Ok, NotFound, UnknownDepartment, NotReady }

    /// <summary>Human decision: accept the AI suggestion or override it. Logged as feedback.</summary>
    public async Task<RouteOutcome> RouteAsync(Guid id, RouteRequest req, string? decidedBy, CancellationToken ct)
    {
        var d = await db.Documents.Include(x => x.Analysis).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return RouteOutcome.NotFound;
        if (d.Status is DocumentStatus.Processing or DocumentStatus.Uploaded) return RouteOutcome.NotReady;
        if (!await db.Departments.AnyAsync(x => x.Id == req.DepartmentId, ct)) return RouteOutcome.UnknownDepartment;

        var suggested = d.Analysis?.SuggestedDepartmentId;
        db.RoutingDecisions.Add(new RoutingDecision
        {
            DocumentId = d.Id,
            SuggestedDepartmentId = suggested,
            FinalDepartmentId = req.DepartmentId,
            WasOverridden = suggested != req.DepartmentId,
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            DecidedBy = _user.Name ?? decidedBy,
            DecidedByEmail = _user.Email,
        });
        d.Status = DocumentStatus.Routed;
        var previousDepartment = d.AssignedDepartmentId;
        // (Re)routing puts it in the receiving department's inbox as New, even if another department had closed it.
        d.AssignedDepartmentId = req.DepartmentId;
        d.Handling = HandlingStatus.New;
        d.HandlingUpdatedAt = DateTime.UtcNow;
        d.HandlingNote = null;
        d.HandlingBy = null;
        d.AssigneeId = null;      // the new department assigns its own people
        d.AssigneeName = null;
        db.DocumentEvents.Add(new DocumentEvent
        {
            DocumentId = d.Id, Type = DocumentEventType.Routed, Actor = _user.Name ?? decidedBy,
            DepartmentId = req.DepartmentId, Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
        });

        // Outbox: queued in the same transaction as the decision, delivered by the notification worker.
        var departmentEmail = await db.Departments.Where(x => x.Id == req.DepartmentId).Select(x => x.Email).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(departmentEmail))
            db.Notifications.Add(new Notification { DocumentId = d.Id, DepartmentId = req.DepartmentId, To = departmentEmail });

        await db.SaveChangesAsync(ct);
        // The old department must hear about it too: the document just left its inbox.
        await notifier.DocumentStatusChangedAsync(id, d.Status, Audience.Of(previousDepartment, d.AssignedDepartmentId), ct);
        return RouteOutcome.Ok;
    }

    public enum HandlingOutcome { Ok, NotFound, NotRouted, Forbidden }

    /// <summary>The department confirms it received the document and is working on it.</summary>
    public Task<HandlingOutcome> AcknowledgeAsync(Guid id, CancellationToken ct) =>
        SetHandlingAsync(id, HandlingStatus.Acknowledged, DocumentEventType.Acknowledged, note: null, ct);

    /// <summary>The department finished with the document. The note explains what was done.</summary>
    public Task<HandlingOutcome> CloseAsync(Guid id, string? note, CancellationToken ct) =>
        SetHandlingAsync(id, HandlingStatus.Closed, DocumentEventType.Closed, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), ct);

    public Task<HandlingOutcome> ReopenAsync(Guid id, CancellationToken ct) =>
        SetHandlingAsync(id, HandlingStatus.Acknowledged, DocumentEventType.Reopened, note: null, ct);

    private async Task<HandlingOutcome> SetHandlingAsync(Guid id, HandlingStatus status, DocumentEventType eventType, string? note, CancellationToken ct)
    {
        var d = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return HandlingOutcome.NotFound;
        if (d.AssignedDepartmentId is null) return HandlingOutcome.NotRouted;
        if (_user.RestrictedToDepartment is { } own && own != d.AssignedDepartmentId) return HandlingOutcome.Forbidden;

        d.Handling = status;
        d.HandlingUpdatedAt = DateTime.UtcNow;
        d.HandlingNote = note;
        d.HandlingBy = _user.Name;
        db.DocumentEvents.Add(new DocumentEvent
        {
            DocumentId = d.Id, Type = eventType, Actor = _user.Name, DepartmentId = d.AssignedDepartmentId, Note = note,
        });
        if (eventType == DocumentEventType.Closed) await QueueClosureEmailAsync(d, ct);
        await db.SaveChangesAsync(ct);
        await notifier.DocumentStatusChangedAsync(id, d.Status, Audience.Of(d.AssignedDepartmentId), ct);
        return HandlingOutcome.Ok;
    }

    /// <summary>
    /// Close the loop: tell whoever routed the document that the department finished it.
    /// Falls back to the desk mailbox for documents routed before the router's email was recorded.
    /// </summary>
    private async Task QueueClosureEmailAsync(Document d, CancellationToken ct)
    {
        var decision = await db.RoutingDecisions
            .Where(r => r.DocumentId == d.Id)
            .OrderByDescending(r => r.DecidedAt)
            .FirstOrDefaultAsync(ct);
        var to = decision?.DecidedByEmail ?? _notificationOptions.DeskEmail;

        // No one to tell, or the person closing it is the one who routed it.
        if (string.IsNullOrWhiteSpace(to) || string.Equals(to, _user.Email, StringComparison.OrdinalIgnoreCase)) return;

        db.Notifications.Add(new Notification
        {
            DocumentId = d.Id, DepartmentId = d.AssignedDepartmentId!.Value, Kind = NotificationKind.Closed, To = to,
        });
    }

    /// <summary>Per-department counts for the sidebar and department pages.</summary>
    public async Task<IReadOnlyList<DepartmentWorkloadDto>> GetWorkloadAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await Visible(db.Documents.AsNoTracking())
            .Where(d => d.AssignedDepartmentId != null)
            .GroupBy(d => d.AssignedDepartmentId!.Value)
            .Select(g => new DepartmentWorkloadDto(
                g.Key,
                g.Count(d => d.Handling == HandlingStatus.New),
                g.Count(d => d.Handling == HandlingStatus.Acknowledged),
                g.Count(d => d.Handling == HandlingStatus.Closed),
                g.Count(d => d.Handling != HandlingStatus.Closed && d.Analysis != null && d.Analysis.Deadline < today)))
            .ToListAsync(ct);
        return rows;
    }

    public async Task<StatsDto> GetStatsAsync(CancellationToken ct)
    {
        var byStatus = await db.Documents.GroupBy(d => d.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var byPriority = await db.Analyses.GroupBy(a => a.Priority)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var byCategory = await db.Analyses.GroupBy(a => a.Category)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        // Latest decision per document is the one that counts.
        var decisions = await db.RoutingDecisions.AsNoTracking()
            .GroupBy(r => r.DocumentId)
            .Select(g => g.OrderByDescending(r => r.DecidedAt).First())
            .ToListAsync(ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.NameEn, ct);

        // Accuracy only counts documents where the AI actually made a suggestion.
        var scored = decisions.Where(r => r.SuggestedDepartmentId != null).ToList();
        var accepted = scored.Count(r => !r.WasOverridden);
        var avgConfidence = await db.Analyses.Select(a => (double?)a.Confidence).AverageAsync(ct);
        var tokens = await db.Analyses.SumAsync(a => (long)a.TokensUsed, ct);

        int CountOf(DocumentStatus s) => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;

        return new StatsDto(
            TotalDocuments: byStatus.Sum(x => x.Count),
            NeedsReview: CountOf(DocumentStatus.NeedsReview),
            Routed: CountOf(DocumentStatus.Routed),
            Failed: CountOf(DocumentStatus.Failed),
            Decisions: scored.Count,
            AcceptedSuggestions: accepted,
            RoutingAccuracy: scored.Count == 0 ? null : (double)accepted / scored.Count,
            AverageConfidence: avgConfidence,
            TotalTokens: tokens,
            ByStatus: byStatus.Select(x => new CountByKey(x.Key.ToString(), x.Count)).ToList(),
            ByPriority: byPriority.OrderBy(x => x.Key).Select(x => new CountByKey(x.Key.ToString(), x.Count)).ToList(),
            ByCategory: byCategory.OrderByDescending(x => x.Count).Select(x => new CountByKey(x.Key.ToString(), x.Count)).ToList(),
            ByDepartment: decisions.GroupBy(r => r.FinalDepartmentId)
                .Select(g => new CountByKey(departments.GetValueOrDefault(g.Key, $"#{g.Key}"), g.Count()))
                .OrderByDescending(x => x.Count).ToList());
    }
}

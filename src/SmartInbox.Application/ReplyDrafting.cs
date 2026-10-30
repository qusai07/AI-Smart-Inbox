using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartInbox.Domain;

namespace SmartInbox.Application;

/// <summary>What a drafter gets: the letter, its analysis, the department replying, and what the requester asked for.</summary>
public record DraftInput(Document Document, DocumentAnalysis? Analysis, Department Department, string? TemplateKey, string? Instructions);

public record DraftResult(
    string TemplateKey, string Engine, string SubjectEn, string BodyEn, string SubjectAr, string BodyAr,
    IReadOnlyList<string> MissingInfo, IReadOnlyList<string> Commitments);

/// <summary>A drafting engine. Today: templates (no AI); the interface leaves room for others if measurement ever justifies one.</summary>
public interface IReplyDrafter
{
    Task<DraftResult> DraftAsync(DraftInput input, CancellationToken ct);
}

/// <summary>A message about a failed draft that is safe and useful to show to users.</summary>
public class DraftingException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Free, offline drafting: picks the template (requested, or by the letter's category) and fills its slots from the
/// analysis and the department. Anything unknown stays [●] and is listed as missing information.
/// </summary>
public class TemplateReplyDrafter(ReplyTemplateCatalog catalog, DraftingOptions options, TimeProvider clock, ReminderOptions? reminders = null) : IReplyDrafter
{
    public const string EngineName = "Templates";

    public Task<DraftResult> DraftAsync(DraftInput input, CancellationToken ct)
    {
        var template = catalog.Find(input.TemplateKey) ?? catalog.ForCategory(input.Analysis?.Category);
        var (en, ar) = Slots(input);

        var subjectEn = TemplateRenderer.Render(template.SubjectEn, en);
        var bodyEn = TemplateRenderer.Render(template.BodyEn, en);
        var subjectAr = TemplateRenderer.Render(template.SubjectAr, ar);
        var bodyAr = TemplateRenderer.Render(template.BodyAr, ar);
        var missing = subjectEn.Missing.Concat(bodyEn.Missing).Concat(subjectAr.Missing).Concat(bodyAr.Missing).Distinct().ToList();

        return Task.FromResult(new DraftResult(
            template.Key, EngineName, subjectEn.Text, bodyEn.Text, subjectAr.Text, bodyAr.Text, missing, template.Commitments));
    }

    private (SlotValues En, SlotValues Ar) Slots(DraftInput input)
    {
        var a = input.Analysis;
        var d = input.Department;
        var today = Today();
        var en = new SlotValues
        {
            ["OurReference"] = null, // the outgoing register isn't built yet: a person fills it in
            ["Today"] = LetterDates.En(today),
            ["SenderName"] = a?.Sender,
            ["TheirReference"] = a?.ReferenceNumber,
            ["TheirDate"] = a?.DocumentDate is { } date ? LetterDates.En(date) : null,
            ["TheirRef"] = a?.ReferenceNumber is { Length: > 0 } r ? $"ref. {r}" : null,
            ["TheirRefDate"] = Join(a?.ReferenceNumber is { Length: > 0 } r2 ? $"ref. {r2}" : null, a?.DocumentDate is { } d2 ? $"dated {LetterDates.En(d2)}" : null),
            ["Deadline"] = a?.Deadline is { } deadline ? LetterDates.En(deadline) : null,
            ["Department"] = d.NameEn,
            ["DepartmentTeam"] = d.NameEn.EndsWith("Office", StringComparison.OrdinalIgnoreCase) ? $"the {d.NameEn}" : $"the {d.NameEn} department",
            ["Organisation"] = options.OrganisationNameEn,
            ["SignatoryName"] = d.SignatoryNameEn,
            ["SignatoryTitle"] = d.SignatoryTitleEn,
            ["Response"] = null,
        };
        var ar = new SlotValues
        {
            ["OurReference"] = null,
            ["Today"] = LetterDates.ArWithHijri(today),
            ["SenderName"] = a?.Sender,
            ["TheirReference"] = a?.ReferenceNumber,
            ["TheirDate"] = a?.DocumentDate is { } date2 ? LetterDates.Ar(date2) : null,
            ["TheirRef"] = a?.ReferenceNumber is { Length: > 0 } r3 ? $"رقم {r3}" : null,
            // Arabic agrees with the noun: خطابكم … المؤرخ (masculine), دعوتكم … المؤرخة (feminine).
            ["TheirRefDateM"] = Join(a?.ReferenceNumber is { Length: > 0 } r4 ? $"رقم {r4}" : null, a?.DocumentDate is { } d4 ? $"المؤرخ في {LetterDates.Ar(d4)}" : null),
            ["TheirRefDateF"] = Join(a?.ReferenceNumber is { Length: > 0 } r5 ? $"رقم {r5}" : null, a?.DocumentDate is { } d5 ? $"المؤرخة في {LetterDates.Ar(d5)}" : null),
            ["Deadline"] = a?.Deadline is { } deadline2 ? LetterDates.Ar(deadline2) : null,
            ["Department"] = d.NameAr,
            ["DepartmentTeam"] = d.NameAr.StartsWith("مكتب", StringComparison.Ordinal) ? d.NameAr : $"إدارة {d.NameAr}",
            ["Organisation"] = options.OrganisationNameAr,
            ["SignatoryName"] = d.SignatoryNameAr,
            ["SignatoryTitle"] = d.SignatoryTitleAr,
            ["Response"] = null,
        };
        return (en, ar);
    }

    private static string? Join(string? a, string? b) => a is null ? b : b is null ? a : $"{a} {b}";

    /// <summary>The letter's date in the organisation's time zone (Reminders:TimeZone), not UTC.</summary>
    private DateOnly Today() => OrganisationClock.Today(clock, reminders);
}

/// <summary>Runs a queued draft: Requested → Generating → Ready (or Failed), with timeline entries and live updates.</summary>
public class ReplyDraftGenerator(
    IAppDbContext db, IReplyDrafter drafter, IInboxNotifier notifier, ILogger<ReplyDraftGenerator> logger, IWebhookPublisher? webhooks = null)
{
    public async Task GenerateAsync(Guid draftId, CancellationToken ct)
    {
        var draft = await db.ReplyDrafts.FirstOrDefaultAsync(d => d.Id == draftId, ct);
        if (draft is null || draft.Status is not (ReplyDraftStatus.Requested or ReplyDraftStatus.Generating)) return;
        var doc = await db.Documents.Include(d => d.Analysis).FirstOrDefaultAsync(d => d.Id == draft.DocumentId, ct);
        var department = doc?.AssignedDepartmentId is { } id ? await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct) : null;
        var audience = Audience.Of(doc?.AssignedDepartmentId);

        draft.Status = ReplyDraftStatus.Generating;
        await db.SaveChangesAsync(ct);
        await notifier.DraftChangedAsync(draft.DocumentId, draft.Id, draft.Status, audience, ct);

        try
        {
            if (doc is null || department is null) throw new DraftingException("The document is no longer routed to a department.");
            var result = await drafter.DraftAsync(new DraftInput(doc, doc.Analysis, department, draft.TemplateKey, draft.Instructions), ct);

            draft.TemplateKey = result.TemplateKey;
            draft.Engine = result.Engine;
            draft.SubjectEn = Limit(result.SubjectEn, ReplyDraftRules.MaxSubject);
            draft.BodyEn = Limit(result.BodyEn, ReplyDraftRules.MaxBody);
            draft.SubjectAr = Limit(result.SubjectAr, ReplyDraftRules.MaxSubject);
            draft.BodyAr = Limit(result.BodyAr, ReplyDraftRules.MaxBody);
            draft.MissingInfo = ReplyDraftRules.ToJson(result.MissingInfo);
            draft.Commitments = ReplyDraftRules.ToJson(result.Commitments);
            draft.Error = null;
            draft.Status = ReplyDraftStatus.Ready;
            db.DocumentEvents.Add(new DocumentEvent
            {
                DocumentId = draft.DocumentId, Type = DocumentEventType.DraftReady, Actor = result.Engine, DepartmentId = doc.AssignedDepartmentId,
                Note = $"{result.TemplateKey}, version {draft.Version}" + (result.MissingInfo.Count > 0 ? $", {result.MissingInfo.Count} item(s) to fill in" : ""),
            });
            if (webhooks is not null) await webhooks.PublishDraftAsync(WebhookEvents.DraftReady, draft, doc, result.Engine, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Drafting failed for draft {DraftId}", draftId);
            draft.Status = ReplyDraftStatus.Failed;
            draft.Error = ex is DraftingException ? ex.Message : "The draft could not be prepared. Try again.";
            db.DocumentEvents.Add(new DocumentEvent
            {
                DocumentId = draft.DocumentId, Type = DocumentEventType.DraftFailed, Actor = "system", DepartmentId = doc?.AssignedDepartmentId, Note = draft.Error,
            });
        }

        await db.SaveChangesAsync(ct);
        await notifier.DraftChangedAsync(draft.DocumentId, draft.Id, draft.Status, audience, ct);
    }

    private static string Limit(string s, int max) => s.Length <= max ? s : s[..max];
}

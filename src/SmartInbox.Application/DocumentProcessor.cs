using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartInbox.Domain;

namespace SmartInbox.Application;

/// <summary>Runs the pipeline for one document: extract text → AI analysis → save → notify.</summary>
public class DocumentProcessor(
    IAppDbContext db,
    IFileStorage storage,
    IEnumerable<ITextExtractor> extractors,
    IAiProvider ai,
    IInboxNotifier notifier,
    ILogger<DocumentProcessor> logger)
{
    /// <summary>Below this confidence the document goes to the Needs Review queue.</summary>
    public const double ConfidenceThreshold = 0.6;

    public async Task ProcessAsync(Guid documentId, CancellationToken ct)
    {
        var doc = await db.Documents
            .Include(d => d.Analysis)
            .Include(d => d.ActionItems)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc is null) return;

        await SetStatusAsync(doc, DocumentStatus.Processing, ct);

        try
        {
            var input = await BuildInputAsync(doc, ct);
            var departments = await db.Departments.AsNoTracking().OrderBy(d => d.Id).ToListAsync(ct);
            var result = await ai.AnalyzeAsync(input, departments, ct);

            if (doc.ExtractedText is null && result.ExtractedText is not null)
                doc.ExtractedText = result.ExtractedText;

            ApplyResult(doc, result, departments);
            doc.ErrorMessage = null;
            db.DocumentEvents.Add(new DocumentEvent
            {
                DocumentId = doc.Id, Type = DocumentEventType.Analyzed, Actor = result.ModelUsed,
                DepartmentId = doc.Analysis!.SuggestedDepartmentId, Note = $"confidence {doc.Analysis.Confidence:0.00}",
            });
            // Re-analyzing a routed document refreshes the analysis but must not undo the human decision.
            var routed = await db.RoutingDecisions.AnyAsync(r => r.DocumentId == doc.Id, ct);
            await SetStatusAsync(doc, routed ? DocumentStatus.Routed : StatusFor(doc.Analysis!), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Processing failed for document {DocumentId}", documentId);
            doc.ErrorMessage = ex is AiException ? ex.Message : "AI analysis failed. Try re-analyzing.";
            db.DocumentEvents.Add(new DocumentEvent { DocumentId = doc.Id, Type = DocumentEventType.AnalysisFailed, Actor = "system", Note = doc.ErrorMessage });
            var routed = await db.RoutingDecisions.AnyAsync(r => r.DocumentId == doc.Id, ct);
            await SetStatusAsync(doc, routed ? DocumentStatus.Routed : DocumentStatus.Failed, ct);
        }
    }

    public static DocumentStatus StatusFor(DocumentAnalysis analysis) =>
        analysis.SuggestedDepartmentId is null || analysis.Confidence < ConfidenceThreshold
            ? DocumentStatus.NeedsReview
            : DocumentStatus.Analyzed;

    private async Task<AnalysisInput> BuildInputAsync(Document doc, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(doc.ExtractedText))
            return AnalysisInput.FromText(doc.ExtractedText);

        var extractor = extractors.FirstOrDefault(e => e.CanHandle(doc.ContentType, doc.FileName));
        if (extractor is not null)
        {
            await using var stream = storage.Open(doc.StoragePath);
            var text = await extractor.ExtractAsync(stream, ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                doc.ExtractedText = text;
                return AnalysisInput.FromText(text);
            }
        }

        // No text layer (scanned PDF / image) → let the model read the file itself.
        await using var raw = storage.Open(doc.StoragePath);
        using var ms = new MemoryStream();
        await raw.CopyToAsync(ms, ct);
        return AnalysisInput.FromFile(ms.ToArray(), doc.ContentType);
    }

    private void ApplyResult(Document doc, AiAnalysisResult r, IReadOnlyList<Department> departments)
    {
        var departmentId = departments.Any(d => d.Id == r.SuggestedDepartmentId) ? r.SuggestedDepartmentId : (int?)null;

        var analysis = doc.Analysis ?? new DocumentAnalysis { DocumentId = doc.Id };
        analysis.SummaryEn = r.SummaryEn;
        analysis.SummaryAr = r.SummaryAr;
        analysis.Category = r.Category;
        analysis.Priority = r.Priority;
        analysis.Language = r.Language;
        analysis.Sender = r.Sender;
        analysis.Recipient = r.Recipient;
        analysis.ReferenceNumber = r.ReferenceNumber;
        analysis.DocumentDate = r.DocumentDate;
        analysis.Deadline = r.Deadline;
        analysis.SuggestedDepartmentId = departmentId;
        analysis.Confidence = Math.Clamp(r.Confidence, 0, 1);
        analysis.Reasoning = r.Reasoning;
        analysis.ModelUsed = r.ModelUsed;
        analysis.TokensUsed = r.TokensUsed;
        analysis.CreatedAt = DateTime.UtcNow;
        doc.Analysis = analysis;

        db.ActionItems.RemoveRange(doc.ActionItems);
        doc.ActionItems = r.ActionItems
            .Select(a => new ActionItem { DocumentId = doc.Id, Description = a.Description, DueDate = a.DueDate })
            .ToList();
    }

    private async Task SetStatusAsync(Document doc, DocumentStatus status, CancellationToken ct)
    {
        doc.Status = status;
        await db.SaveChangesAsync(ct);
        await notifier.DocumentStatusChangedAsync(doc.Id, status, ct);
    }
}

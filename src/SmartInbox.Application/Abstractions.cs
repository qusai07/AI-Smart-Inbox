using Microsoft.EntityFrameworkCore;
using SmartInbox.Domain;

namespace SmartInbox.Application;

public interface IAppDbContext
{
    DbSet<Document> Documents { get; }
    DbSet<DocumentAnalysis> Analyses { get; }
    DbSet<ActionItem> ActionItems { get; }
    DbSet<Department> Departments { get; }
    DbSet<RoutingDecision> RoutingDecisions { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<DocumentEvent> DocumentEvents { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public record EmailMessage(string To, string Subject, string Html, string Text);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Pluggable LLM backend (Claude, Azure OpenAI, ...).</summary>
public interface IAiProvider
{
    Task<AiAnalysisResult> AnalyzeAsync(AnalysisInput input, IReadOnlyList<Department> departments, CancellationToken ct = default);
}

/// <summary>
/// What the AI gets to read: extracted text when available, otherwise the raw file
/// (scanned PDF or image) so the model can read it with vision.
/// </summary>
public record AnalysisInput(string? Text, byte[]? FileBytes = null, string? MediaType = null)
{
    public static AnalysisInput FromText(string text) => new(text);
    public static AnalysisInput FromFile(byte[] bytes, string mediaType) => new(null, bytes, mediaType);
}

/// <summary>Turns an uploaded file into plain text. Returns null when the file has no text layer (needs vision).</summary>
public interface ITextExtractor
{
    bool CanHandle(string contentType, string fileName);
    Task<string?> ExtractAsync(Stream content, CancellationToken ct = default);
}

public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct = default);
    Stream Open(string storagePath);
}

public interface IDocumentQueue
{
    ValueTask EnqueueAsync(Guid documentId, CancellationToken ct = default);
}

public interface IInboxNotifier
{
    Task DocumentStatusChangedAsync(Guid documentId, DocumentStatus status, CancellationToken ct = default);
}

public record AiActionItem(string Description, DateOnly? DueDate);

public record AiAnalysisResult(
    string SummaryEn,
    string SummaryAr,
    DocumentCategory Category,
    Priority Priority,
    string Language,
    string? Sender,
    string? Recipient,
    string? ReferenceNumber,
    DateOnly? DocumentDate,
    DateOnly? Deadline,
    IReadOnlyList<AiActionItem> ActionItems,
    int SuggestedDepartmentId,
    double Confidence,
    string Reasoning,
    string ModelUsed,
    int TokensUsed,
    string? ExtractedText = null);

/// <summary>An AI failure whose message is safe and useful to show to users.</summary>
public class AiException(string message, Exception? inner = null) : Exception(message, inner);

public class AiRefusalException(string message) : AiException(message);

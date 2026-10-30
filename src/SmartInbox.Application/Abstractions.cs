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
    DbSet<DocumentComment> Comments { get; }
    DbSet<ApiKey> ApiKeys { get; }
    DbSet<WebhookEndpoint> WebhookEndpoints { get; }
    DbSet<WebhookDelivery> WebhookDeliveries { get; }
    DbSet<ReplyDraft> ReplyDrafts { get; }
    DbSet<InboundEmail> InboundEmails { get; }
    DbSet<OutgoingLetter> OutgoingLetters { get; }
    DbSet<RegisterCounter> RegisterCounters { get; }
    DbSet<DocumentLink> DocumentLinks { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public record EmailMessage(string To, string Subject, string Html, string Text);

/// <summary>A person who can be assigned work.</summary>
public record DirectoryUser(string Id, string DisplayName, string? Email, string? Role, int? DepartmentId);

/// <summary>Looks up users (backed by Identity in the API).</summary>
public interface IUserDirectory
{
    Task<DirectoryUser?> FindAsync(string id, CancellationToken ct = default);
    /// <summary>Active department staff of one department.</summary>
    Task<IReadOnlyList<DirectoryUser>> DepartmentMembersAsync(int departmentId, CancellationToken ct = default);
    /// <summary>Email addresses of active administrators.</summary>
    Task<IReadOnlyList<string>> AdminEmailsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>Encrypts secrets stored in the database (ASP.NET Data Protection in production).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}

public record WebhookRequest(string Url, IReadOnlyDictionary<string, string> Headers, string Body);

/// <summary>Result of one HTTP attempt. StatusCode is null when no response arrived (timeout, DNS, blocked address).</summary>
public record WebhookResponse(int? StatusCode, string? BodySnippet, string? Error, int DurationMs)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

/// <summary>Sends one webhook HTTP request (with SSRF protection and a timeout in production).</summary>
public interface IWebhookTransport
{
    Task<WebhookResponse> SendAsync(WebhookRequest request, CancellationToken ct = default);
}

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
    /// <summary>
    /// Tells the correspondence desk (always) and the given departments that a document changed.
    /// Department staff only ever hear about their own department's documents.
    /// </summary>
    Task DocumentStatusChangedAsync(Guid documentId, DocumentStatus status, IReadOnlyCollection<int> departmentIds, CancellationToken ct = default);

    /// <summary>A reply draft was requested, finished, edited or decided. Same audience rules as documents.</summary>
    Task DraftChangedAsync(Guid documentId, Guid draftId, ReplyDraftStatus status, IReadOnlyCollection<int> departmentIds, CancellationToken ct = default) =>
        Task.CompletedTask;
}

public static class Audience
{
    /// <summary>The distinct, non-null department ids that should receive a live update.</summary>
    public static IReadOnlyCollection<int> Of(params int?[] departmentIds) =>
        departmentIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
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

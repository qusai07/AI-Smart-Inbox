namespace SmartInbox.Domain;

public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public required string StoragePath { get; set; }
    public string? ExtractedText { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? UploadedBy { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>How it came in: the web upload, an API key, or the shared mailbox.</summary>
    public DocumentSource Source { get; set; } = DocumentSource.Upload;
    /// <summary>The email it came in, when Source is Email.</summary>
    public Guid? InboundEmailId { get; set; }

    // Department side, set once the document is routed.
    public int? AssignedDepartmentId { get; set; }
    public Department? AssignedDepartment { get; set; }
    public HandlingStatus? Handling { get; set; }
    public DateTime? HandlingUpdatedAt { get; set; }
    public string? HandlingNote { get; set; }
    public string? HandlingBy { get; set; }
    /// <summary>Staff member in the assigned department who owns the item (user id + display name snapshot).</summary>
    public string? AssigneeId { get; set; }
    public string? AssigneeName { get; set; }

    public DocumentAnalysis? Analysis { get; set; }
    public List<ActionItem> ActionItems { get; set; } = [];
    public List<RoutingDecision> RoutingDecisions { get; set; } = [];
    public List<Notification> Notifications { get; set; } = [];
    public List<DocumentEvent> Events { get; set; } = [];
    public List<DocumentComment> Comments { get; set; } = [];
}

/// <summary>
/// One email seen in the shared mailbox, imported or not. The Message-Id is unique, so an email is never imported
/// twice; the original .eml is kept so people can see the covering message.
/// </summary>
public class InboundEmail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string MessageId { get; set; }
    public required string Mailbox { get; set; }
    public string? FromName { get; set; }
    public string? FromAddress { get; set; }
    public string? Subject { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    /// <summary>The covering message as plain text (shortened).</summary>
    public string? BodyText { get; set; }
    /// <summary>The original email in file storage.</summary>
    public string? StoragePath { get; set; }
    public InboundEmailStatus Status { get; set; }
    /// <summary>Why it was skipped or failed.</summary>
    public string? Reason { get; set; }
    public int Attempts { get; set; }
    /// <summary>JSON array of the documents created from it.</summary>
    public string? DocumentIds { get; set; }
    /// <summary>JSON array of attachments that were not imported, with the reason.</summary>
    public string? SkippedAttachments { get; set; }
}

/// <summary>Internal discussion on a document. Immutable, so it doubles as an audit trail.</summary>
public class DocumentComment
{
    public long Id { get; set; }
    public Guid DocumentId { get; set; }
    public string? AuthorId { get; set; }
    public required string AuthorName { get; set; }
    public required string Text { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Append-only history: what happened to a document, who did it and when.</summary>
public class DocumentEvent
{
    public long Id { get; set; }
    public Guid DocumentId { get; set; }
    public DocumentEventType Type { get; set; }
    /// <summary>Person, AI engine or "system".</summary>
    public string? Actor { get; set; }
    public int? DepartmentId { get; set; }
    public string? Note { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public class DocumentAnalysis
{
    public int Id { get; set; }
    public Guid DocumentId { get; set; }
    public string? SummaryEn { get; set; }
    public string? SummaryAr { get; set; }
    public DocumentCategory Category { get; set; }
    public Priority Priority { get; set; }
    public string? Language { get; set; }
    public string? Sender { get; set; }
    public string? Recipient { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public DateOnly? Deadline { get; set; }
    public int? SuggestedDepartmentId { get; set; }
    public Department? SuggestedDepartment { get; set; }
    public double Confidence { get; set; }
    public string? Reasoning { get; set; }
    public string? ModelUsed { get; set; }
    public int TokensUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ActionItem
{
    public int Id { get; set; }
    public Guid DocumentId { get; set; }
    public required string Description { get; set; }
    public DateOnly? DueDate { get; set; }
}

public class Department
{
    public int Id { get; set; }
    public required string NameEn { get; set; }
    public required string NameAr { get; set; }
    public string? Description { get; set; }
    public string? Keywords { get; set; }
    /// <summary>Where routing notifications and daily digests go. Null = no email.</summary>
    public string? Email { get; set; }
    /// <summary>Receives escalations for overdue items. Null = the desk mailbox, if configured.</summary>
    public string? HeadEmail { get; set; }

    // Signature block of reply letters from this department.
    public string? SignatoryNameEn { get; set; }
    public string? SignatoryNameAr { get; set; }
    public string? SignatoryTitleEn { get; set; }
    public string? SignatoryTitleAr { get; set; }
}

/// <summary>Outbox row: an email to send to a department. A background worker delivers it with retries.</summary>
public class Notification
{
    public int Id { get; set; }
    /// <summary>Null for digests, which cover many documents.</summary>
    public Guid? DocumentId { get; set; }
    public int DepartmentId { get; set; }
    public NotificationKind Kind { get; set; } = NotificationKind.Routed;
    public required string To { get; set; }
    /// <summary>Unique per logical email (e.g. "digest:2:2027-02-16") so scheduled jobs can't send twice.</summary>
    public string? DedupKey { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}

/// <summary>
/// A proposed reply letter in English and Arabic. Every edit is a new version, so the generated text is kept
/// next to what a person changed. Nothing is ever sent: a person approves it and takes it from there.
/// </summary>
public class ReplyDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public int Version { get; set; } = 1;
    public ReplyDraftStatus Status { get; set; } = ReplyDraftStatus.Requested;
    /// <summary>Which reply template framed the letter (e.g. "legal-acknowledgement").</summary>
    public string? TemplateKey { get; set; }
    /// <summary>What wrote it: "Templates", "Hybrid:qwen2.5:7b", or "Edited" for a person's version.</summary>
    public string? Engine { get; set; }
    /// <summary>What the requester asked for, e.g. "ask for a copy of the contract".</summary>
    public string? Instructions { get; set; }
    public string? SubjectEn { get; set; }
    public string? BodyEn { get; set; }
    public string? SubjectAr { get; set; }
    public string? BodyAr { get; set; }
    /// <summary>JSON array of things a person must still supply (each also appears as [●] in the text).</summary>
    public string? MissingInfo { get; set; }
    /// <summary>JSON array of promises the reply makes, listed so the approver sees them at a glance.</summary>
    public string? Commitments { get; set; }
    public string? RequestedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? EditedBy { get; set; }
    public DateTime? EditedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>A credential for other systems (SDK, integrations). It acts as a role, like a user, but signs in without a browser.</summary>
public class ApiKey
{
    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Public part of the key ("sik_a1b2c3d4"), safe to show and used to look the key up.</summary>
    public required string Prefix { get; set; }
    /// <summary>Hex SHA-256 of the full key. The key itself is shown once and never stored.</summary>
    public required string Hash { get; set; }
    public required string Role { get; set; }
    public int? DepartmentId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

/// <summary>An external URL that receives signed event notifications.</summary>
public class WebhookEndpoint
{
    public int Id { get; set; }
    public required string Url { get; set; }
    public string? Description { get; set; }
    /// <summary>Signing secret ("whsec_..."), encrypted at rest.</summary>
    public required string Secret { get; set; }
    /// <summary>The secret before the last rotation; still signs requests until <see cref="PreviousSecretExpiresAt"/>.</summary>
    public string? PreviousSecret { get; set; }
    public DateTime? PreviousSecretExpiresAt { get; set; }
    /// <summary>"*" or a comma-separated list of event types.</summary>
    public string Events { get; set; } = "*";
    /// <summary>Only events about this department's documents. Null = all.</summary>
    public int? DepartmentId { get; set; }
    public bool Enabled { get; set; } = true;
    public int ConsecutiveFailures { get; set; }
    public string? DisabledReason { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Outbox row: one event for one endpoint. Written in the same transaction as the change that caused it.</summary>
public class WebhookDelivery
{
    public long Id { get; set; }
    public int EndpointId { get; set; }
    /// <summary>Stable across retries and redeliveries, so receivers can de-duplicate.</summary>
    public required string EventId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    /// <summary>Unique per endpoint and logical event (e.g. "deadline:{doc}:{date}") so scheduled events fire once.</summary>
    public string? DedupKey { get; set; }
    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public string? LastResponseSnippet { get; set; }
    public int? DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }
}

public class RoutingDecision
{
    public int Id { get; set; }
    public Guid DocumentId { get; set; }
    public int? SuggestedDepartmentId { get; set; }
    public int FinalDepartmentId { get; set; }
    public Department? FinalDepartment { get; set; }
    public bool WasOverridden { get; set; }
    public string? Note { get; set; }
    public string? DecidedBy { get; set; }
    /// <summary>Where to report back when the department closes the document.</summary>
    public string? DecidedByEmail { get; set; }
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}

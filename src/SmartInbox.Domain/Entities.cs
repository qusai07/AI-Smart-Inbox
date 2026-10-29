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

    // Department side, set once the document is routed.
    public int? AssignedDepartmentId { get; set; }
    public Department? AssignedDepartment { get; set; }
    public HandlingStatus? Handling { get; set; }
    public DateTime? HandlingUpdatedAt { get; set; }
    public string? HandlingNote { get; set; }
    public string? HandlingBy { get; set; }

    public DocumentAnalysis? Analysis { get; set; }
    public List<ActionItem> ActionItems { get; set; } = [];
    public List<RoutingDecision> RoutingDecisions { get; set; } = [];
    public List<Notification> Notifications { get; set; } = [];
    public List<DocumentEvent> Events { get; set; } = [];
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

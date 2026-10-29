namespace SmartInbox.Domain;

public enum DocumentStatus
{
    Uploaded,
    Processing,
    Analyzed,
    Routed,
    NeedsReview,
    Failed
}

/// <summary>What the receiving department has done with a routed document.</summary>
public enum HandlingStatus
{
    New,
    Acknowledged,
    Closed
}

/// <summary>Entries in a document's activity timeline.</summary>
public enum DocumentEventType
{
    Uploaded,
    Analyzed,
    AnalysisFailed,
    Routed,
    Acknowledged,
    Closed,
    Reopened,
    EmailSent,
    EmailFailed,
    Escalated,
    Assigned
}

public enum NotificationKind
{
    /// <summary>To the department: new correspondence was routed to you.</summary>
    Routed,
    /// <summary>Back to the desk: the department closed it.</summary>
    Closed,
    /// <summary>Daily list of a department's items that are due soon or overdue (no single document).</summary>
    Digest,
    /// <summary>To the department head: an item is past its deadline and still open.</summary>
    Escalation,
    /// <summary>To a staff member: an item was assigned to you.</summary>
    Assigned
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed
}

public enum WebhookDeliveryStatus
{
    Pending,
    Delivered,
    Failed
}

public enum Priority
{
    Low,
    Normal,
    High,
    Urgent
}

public enum DocumentCategory
{
    Request,
    Complaint,
    Invitation,
    Circular,
    Invoice,
    Legal,
    Other
}

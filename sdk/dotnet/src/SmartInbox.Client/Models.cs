using System.Text.Json.Serialization;

namespace SmartInbox.Client;

#pragma warning disable CS1591 // Enum members and record properties mirror the API; their names are the documentation.

/// <summary>Where a document is in the pipeline.</summary>
public enum DocumentStatus { Uploaded, Processing, Analyzed, Routed, NeedsReview, Failed }

/// <summary>What the receiving department has done with a routed document.</summary>
public enum HandlingStatus { New, Acknowledged, Closed }

public enum Priority { Low, Normal, High, Urgent }

public enum DocumentCategory { Request, Complaint, Invitation, Circular, Invoice, Legal, Other }

/// <summary>How a document came in: the web upload, an API key, or the shared mailbox.</summary>
public enum DocumentSource { Upload, Email, Api }

/// <summary>The email a document arrived in. Its text comes from outside the organisation.</summary>
public sealed record SourceEmail(Guid Id, string? FromName, string? FromAddress, string? Subject, DateTime? SentAt, string? BodyText,
    IReadOnlyList<string> SkippedAttachments);

/// <summary>Returned by an upload. Analysis runs in the background; see <see cref="DocumentsClient.WaitForAnalysisAsync"/>.</summary>
public sealed record UploadedDocument(Guid Id, DocumentStatus Status);

/// <summary>A row of the document list.</summary>
public sealed record DocumentSummary(
    Guid Id, string FileName, DocumentStatus Status, DateTime UploadedAt,
    string? SummaryEn, string? SummaryAr, DocumentCategory? Category, Priority? Priority, string? Sender,
    DateOnly? Deadline, int? SuggestedDepartmentId, int? FinalDepartmentId, double? Confidence,
    HandlingStatus? Handling, string? AssigneeId, string? AssigneeName, DocumentSource Source = DocumentSource.Upload,
    bool HasRelated = false, bool PossibleDuplicate = false, DraftStatus? Reply = null);

/// <summary>A document with its analysis, routing, handling and timeline.</summary>
public sealed record DocumentDetail(
    Guid Id, string FileName, string ContentType, DocumentStatus Status, DateTime UploadedAt,
    string? ExtractedText, string? ErrorMessage, Analysis? Analysis, IReadOnlyList<ActionItem> ActionItems,
    RoutingDecision? Routing, Handling? Handling, IReadOnlyList<TimelineEntry> Timeline,
    Assignee? Assignee, IReadOnlyList<Comment> Comments, DocumentSource Source = DocumentSource.Upload, SourceEmail? SourceEmail = null,
    IReadOnlyList<OutgoingRef>? Outgoing = null)
{
    /// <summary>True once analysis has finished (successfully or not).</summary>
    public bool IsAnalysisComplete => Status is not (DocumentStatus.Uploaded or DocumentStatus.Processing);
}

public sealed record Analysis(
    string? SummaryEn, string? SummaryAr, DocumentCategory Category, Priority Priority, string? Language,
    string? Sender, string? Recipient, string? ReferenceNumber, DateOnly? DocumentDate, DateOnly? Deadline,
    int? SuggestedDepartmentId, double Confidence, string? Reasoning, string? ModelUsed, int TokensUsed);

public sealed record ActionItem(int Id, string Description, DateOnly? DueDate);

public sealed record RoutingDecision(int? SuggestedDepartmentId, int FinalDepartmentId, bool WasOverridden, string? Note, string? DecidedBy, DateTime DecidedAt);

public sealed record Handling(int DepartmentId, HandlingStatus Status, DateTime? UpdatedAt, string? Note, string? By);

/// <summary>One entry of the activity timeline. <see cref="Type"/> is kept as text so new entry types never break the client.</summary>
public sealed record TimelineEntry(string Type, string? Actor, int? DepartmentId, string? Note, DateTime At);

public sealed record Assignee(string Id, string Name);

public sealed record Comment(long Id, string? AuthorId, string AuthorName, string Text, DateTime CreatedAt);

/// <summary>A staff member who can be assigned documents.</summary>
public sealed record Member(string Id, string DisplayName);

public sealed record Department(int Id, string NameEn, string NameAr, string? Description, string? Keywords, string? Email, string? HeadEmail, string? Code = null);

public sealed record DepartmentWorkload(int DepartmentId, int New, int Acknowledged, int Closed, int Overdue);

public sealed record CountByKey(string Key, int Count);

public sealed record Stats(
    int TotalDocuments, int NeedsReview, int Routed, int Failed, int Decisions, int AcceptedSuggestions,
    double? RoutingAccuracy, double? AverageConfidence, long TotalTokens,
    IReadOnlyList<CountByKey> ByStatus, IReadOnlyList<CountByKey> ByPriority,
    IReadOnlyList<CountByKey> ByCategory, IReadOnlyList<CountByKey> ByDepartment, FollowUp? FollowUp = null);

/// <summary>Work waiting on a person.</summary>
public sealed record FollowUp(int AwaitingApproval, int PossibleDuplicates, int OutgoingNotSent, int OutgoingThisYear, int MailFailed);

/// <summary>One page of results.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, int Total, [property: JsonPropertyName("page")] int PageNumber, int PageSize)
{
    public bool HasMore => PageNumber * PageSize < Total;
}

/// <summary>Where a reply draft is in its life: prepared in the background, then edited and decided by a person.</summary>
public enum DraftStatus { Requested, Generating, Ready, Approved, Rejected, Failed }

/// <summary>
/// A reply letter in English and Arabic. Every edit is a new version. <see cref="Placeholders"/> counts the [●] gaps
/// a person must fill before it can be approved. Our outgoing number shows as "(issued on approval)" and is set by the
/// register when the draft is approved (<see cref="OutgoingNumber"/>). Nothing is ever sent by Smart Inbox.
/// </summary>
public sealed record ReplyDraft(
    Guid Id, Guid DocumentId, int Version, bool IsLatest, DraftStatus Status, string? TemplateKey, string? Engine,
    string? Instructions, string? SubjectEn, string? BodyEn, string? SubjectAr, string? BodyAr,
    IReadOnlyList<string> MissingInfo, IReadOnlyList<string> Commitments, int Placeholders,
    string? RequestedBy, DateTime CreatedAt, string? EditedBy, DateTime? EditedAt, string? DecidedBy, DateTime? DecidedAt, string? Error,
    string? OutgoingNumber = null, Guid? OutgoingLetterId = null)
{
    /// <summary>True once the background preparation has finished (ready, or failed).</summary>
    public bool IsPrepared => Status is not (DraftStatus.Requested or DraftStatus.Generating);
}

/// <summary>A type of reply (template), e.g. "legal-acknowledgement".</summary>
public sealed record ReplyTemplate(string Key, string NameEn, string NameAr, IReadOnlyList<DocumentCategory> Categories);

/// <summary>New text for a draft, in both languages. Saving it creates a new version.</summary>
public sealed record ReplyText(string SubjectEn, string BodyEn, string SubjectAr, string BodyAr);

/// <summary>Why two letters are related, strongest first.</summary>
public enum LinkKind { Duplicate, RepliesToOurLetter, EmailThread, FollowUp }

/// <summary>
/// A letter related to the one asked about, and why (<see cref="Evidence"/>). <see cref="DocumentId"/> is null when the
/// letter only quotes one of our outgoing letters that was registered by hand (<see cref="OutgoingNumber"/>).
/// </summary>
public sealed record RelatedLetter(
    long LinkId, LinkKind Kind, string? Evidence, bool Earlier, DateTime LinkedAt,
    Guid? DocumentId, string? FileName, DateTime? UploadedAt, DocumentStatus? Status, int? DepartmentId, HandlingStatus? Handling, string? Sender,
    Guid? OutgoingLetterId, string? OutgoingNumber);

public enum OutgoingStatus { Issued, Sent, Cancelled }

public enum SentVia { Email, Post, Hand, Other }

/// <summary>An entry in the outgoing register: a letter with its official number. Numbers are never reused.</summary>
public sealed record OutgoingLetter(
    Guid Id, string Number, int Year, int Sequence, int DepartmentId, string? Recipient, string? SubjectEn, string? SubjectAr,
    Guid? DocumentId, Guid? ReplyDraftId, DateTime IssuedAt, string? IssuedBy, OutgoingStatus Status,
    DateTime? SentAt, SentVia? SentVia, string? SentNote, DateTime? CancelledAt, string? CancelledBy, string? CancelReason);

/// <summary>An outgoing number issued in reply to a document.</summary>
public sealed record OutgoingRef(Guid Id, string Number, OutgoingStatus Status, DateTime IssuedAt);

/// <summary>A letter to register by hand: recipient and a subject in at least one language.</summary>
public sealed record NewOutgoingLetter(int DepartmentId, string Recipient, string? SubjectEn, string? SubjectAr, Guid? DocumentId = null);

/// <summary>A downloaded original file.</summary>
public sealed record DocumentFile(Stream Content, string ContentType, string? FileName);

#pragma warning restore CS1591

/// <summary>Filters for listing documents. Unset properties don't filter.</summary>
public sealed record DocumentListQuery
{
    /// <summary>Only documents in this status.</summary>
    public DocumentStatus? Status { get; init; }
    /// <summary>Only documents the AI rated with this priority.</summary>
    public Priority? Priority { get; init; }
    /// <summary>Routed to, or (if not routed yet) suggested for, this department.</summary>
    public int? Department { get; init; }
    /// <summary>Only documents routed to this department (its inbox).</summary>
    public int? Assigned { get; init; }
    /// <summary>Only documents in this handling state.</summary>
    public HandlingStatus? Handling { get; init; }
    /// <summary>Only documents assigned to the key itself (rarely useful for keys; mirrors the web app's "Mine").</summary>
    public bool Mine { get; init; }
    /// <summary>Only documents that came in this way (e.g. <see cref="DocumentSource.Email"/>).</summary>
    public DocumentSource? Source { get; init; }
    /// <summary>Only letters with related letters (true) or without (false).</summary>
    public bool? Related { get; init; }
    /// <summary>Only letters received again (the later copy of a duplicate).</summary>
    public bool Duplicates { get; init; }
    /// <summary>Only letters whose latest reply draft is ready to approve.</summary>
    public bool AwaitingApproval { get; init; }
    /// <summary>Free-text search over file name, text, summaries, sender and reference.</summary>
    public string? Search { get; init; }
    /// <summary>1-based page for <see cref="DocumentsClient.ListAsync"/>; ignored by <see cref="DocumentsClient.ListAllAsync"/>.</summary>
    public int Page { get; init; } = 1;
    /// <summary>Items per page, 1–100.</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>Filters for the outgoing register. Unset properties don't filter.</summary>
public sealed record RegisterQuery
{
    /// <summary>Only numbers issued in this year.</summary>
    public int? Year { get; init; }
    /// <summary>Only this department's letters.</summary>
    public int? Department { get; init; }
    /// <summary>Only letters in this state.</summary>
    public OutgoingStatus? Status { get; init; }
    /// <summary>Search number, recipient and subjects.</summary>
    public string? Search { get; init; }
    /// <summary>1-based page for <see cref="RegisterClient.ListAsync"/>; ignored by <see cref="RegisterClient.ListAllAsync"/>.</summary>
    public int Page { get; init; } = 1;
    /// <summary>Items per page, 1–500.</summary>
    public int PageSize { get; init; } = 50;
}

using SmartInbox.Domain;

namespace SmartInbox.Application;

public record DocumentListItemDto(
    Guid Id,
    string FileName,
    DocumentStatus Status,
    DateTime UploadedAt,
    string? SummaryEn,
    string? SummaryAr,
    DocumentCategory? Category,
    Priority? Priority,
    string? Sender,
    DateOnly? Deadline,
    int? SuggestedDepartmentId,
    int? FinalDepartmentId,
    double? Confidence,
    HandlingStatus? Handling,
    string? AssigneeId,
    string? AssigneeName,
    DocumentSource Source = DocumentSource.Upload,
    bool HasRelated = false,
    bool PossibleDuplicate = false);

public record DocumentDetailDto(
    Guid Id,
    string FileName,
    string ContentType,
    DocumentStatus Status,
    DateTime UploadedAt,
    string? ExtractedText,
    string? ErrorMessage,
    AnalysisDto? Analysis,
    IReadOnlyList<ActionItemDto> ActionItems,
    RoutingDecisionDto? Routing,
    HandlingDto? Handling,
    IReadOnlyList<NotificationDto> Notifications,
    IReadOnlyList<DocumentEventDto> Timeline,
    AssigneeDto? Assignee,
    IReadOnlyList<CommentDto> Comments,
    DocumentSource Source = DocumentSource.Upload,
    SourceEmailDto? SourceEmail = null,
    IReadOnlyList<OutgoingRefDto>? Outgoing = null);

/// <summary>An outgoing number issued for (in reply to) this document.</summary>
public record OutgoingRefDto(Guid Id, string Number, OutgoingStatus Status, DateTime IssuedAt);

/// <summary>The email a document arrived in: who sent it and the covering message.</summary>
public record SourceEmailDto(Guid Id, string? FromName, string? FromAddress, string? Subject, DateTime? SentAt, string? BodyText,
    IReadOnlyList<string> SkippedAttachments);

public record AssigneeDto(string Id, string Name);

public record CommentDto(long Id, string? AuthorId, string AuthorName, string Text, DateTime CreatedAt)
{
    public static CommentDto From(DocumentComment c) => new(c.Id, c.AuthorId, c.AuthorName, c.Text, c.CreatedAt);
}

public record AssignRequest(string? UserId);

public record CommentRequest(string Text);

public record MemberDto(string Id, string DisplayName);

public record DocumentEventDto(DocumentEventType Type, string? Actor, int? DepartmentId, string? Note, DateTime At);

public record NotificationDto(int DepartmentId, NotificationKind Kind, string To, NotificationStatus Status, int Attempts, string? Error, DateTime CreatedAt, DateTime? SentAt);

public record HandlingDto(int DepartmentId, HandlingStatus Status, DateTime? UpdatedAt, string? Note, string? By);

public record CloseRequest(string? Note);

public record DepartmentWorkloadDto(int DepartmentId, int New, int Acknowledged, int Closed, int Overdue);

public record AnalysisDto(
    string? SummaryEn,
    string? SummaryAr,
    DocumentCategory Category,
    Priority Priority,
    string? Language,
    string? Sender,
    string? Recipient,
    string? ReferenceNumber,
    DateOnly? DocumentDate,
    DateOnly? Deadline,
    int? SuggestedDepartmentId,
    double Confidence,
    string? Reasoning,
    string? ModelUsed,
    int TokensUsed);

public record ActionItemDto(int Id, string Description, DateOnly? DueDate);

public record RoutingDecisionDto(
    int? SuggestedDepartmentId,
    int FinalDepartmentId,
    bool WasOverridden,
    string? Note,
    string? DecidedBy,
    DateTime DecidedAt);

public record RouteRequest(int DepartmentId, string? Note);

public record DocumentQuery(
    DocumentStatus? Status = null,
    Priority? Priority = null,
    int? Department = null,
    /// <summary>Department inbox: only documents routed to this department.</summary>
    int? Assigned = null,
    HandlingStatus? Handling = null,
    /// <summary>Only items assigned to the current user.</summary>
    bool Mine = false,
    string? Q = null,
    DocumentSource? Source = null,
    int Page = 1,
    int PageSize = 20,
    /// <summary>Only letters with related letters (true) or without (false).</summary>
    bool? Related = null);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record CountByKey(string Key, int Count);

public record StatsDto(
    int TotalDocuments,
    int NeedsReview,
    int Routed,
    int Failed,
    int Decisions,
    int AcceptedSuggestions,
    double? RoutingAccuracy,
    double? AverageConfidence,
    long TotalTokens,
    IReadOnlyList<CountByKey> ByStatus,
    IReadOnlyList<CountByKey> ByPriority,
    IReadOnlyList<CountByKey> ByCategory,
    IReadOnlyList<CountByKey> ByDepartment);

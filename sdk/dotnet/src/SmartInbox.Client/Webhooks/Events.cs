using System.Text.Json;

namespace SmartInbox.Client.Webhooks;

/// <summary>
/// A verified webhook event. Switch on the concrete type (<see cref="DocumentRoutedEvent"/>, ...).
/// Events can arrive more than once and out of order: de-duplicate by <see cref="Id"/> and order by <see cref="CreatedAt"/>.
/// </summary>
public abstract record SmartInboxEvent
{
    /// <summary>Stable across retries and redeliveries (<c>evt_...</c>).</summary>
    public string Id { get; init; } = "";
    /// <summary>Event type, e.g. <c>document.routed</c>.</summary>
    public string Type { get; init; } = "";
    /// <summary>When the change happened.</summary>
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>Payload version, e.g. <c>2026-11-01</c>.</summary>
    public string ApiVersion { get; init; } = "";
}

/// <summary>
/// Thin payload: ids and key fields only. Call <c>Documents.GetAsync(DocumentId)</c> for the summary, text and history.
/// </summary>
/// <param name="DocumentId">The document.</param>
/// <param name="Status">Pipeline status at the time of the event.</param>
/// <param name="DepartmentId">The department it is routed to, if any.</param>
/// <param name="PreviousDepartmentId">On re-routing: the department it came from.</param>
/// <param name="SuggestedDepartmentId">The AI's suggestion, once analyzed.</param>
/// <param name="Priority">AI-assessed priority, once analyzed.</param>
/// <param name="Category">AI-assessed category, once analyzed.</param>
/// <param name="Confidence">AI confidence 0–1, once analyzed.</param>
/// <param name="Handling">Department handling state, once routed.</param>
/// <param name="Deadline">Deadline found in the document, if any.</param>
/// <param name="Actor">Who did it: a person, "API key: ...", the AI model or "system".</param>
/// <param name="Url">Link to the document in the Smart Inbox web app.</param>
/// <param name="Source">How it came in: upload, API or the shared mailbox.</param>
public sealed record DocumentEventData(
    Guid DocumentId, DocumentStatus Status, int? DepartmentId, int? PreviousDepartmentId, int? SuggestedDepartmentId,
    Priority? Priority, DocumentCategory? Category, double? Confidence, HandlingStatus? Handling,
    DateOnly? Deadline, string? Actor, string Url, DocumentSource Source = DocumentSource.Upload);

/// <summary>An event about one document.</summary>
public abstract record DocumentEvent : SmartInboxEvent
{
    /// <summary>The document and its key fields.</summary>
    public DocumentEventData Data { get; init; } = null!;
}

/// <summary><c>document.received</c>: a document was uploaded.</summary>
public sealed record DocumentReceivedEvent : DocumentEvent;
/// <summary><c>document.analyzed</c>: analysis finished (status Analyzed or NeedsReview).</summary>
public sealed record DocumentAnalyzedEvent : DocumentEvent;
/// <summary><c>document.analysis_failed</c>: analysis failed.</summary>
public sealed record DocumentAnalysisFailedEvent : DocumentEvent;
/// <summary><c>document.routed</c>: the desk routed or re-routed it.</summary>
public sealed record DocumentRoutedEvent : DocumentEvent;
/// <summary><c>document.acknowledged</c>: the department acknowledged it.</summary>
public sealed record DocumentAcknowledgedEvent : DocumentEvent;
/// <summary><c>document.closed</c>: the department closed it.</summary>
public sealed record DocumentClosedEvent : DocumentEvent;
/// <summary><c>document.reopened</c>: the department reopened it.</summary>
public sealed record DocumentReopenedEvent : DocumentEvent;
/// <summary><c>deadline.approaching</c>: an open document is due within two days.</summary>
public sealed record DeadlineApproachingEvent : DocumentEvent;

/// <summary>A reply draft, without its text: fetch it with <c>Drafts.GetAsync(DraftId)</c>.</summary>
/// <param name="DraftId">The draft (one version).</param>
/// <param name="DocumentId">The letter it replies to.</param>
/// <param name="Version">Version number; edits create new versions.</param>
/// <param name="Status">Ready or Approved.</param>
/// <param name="TemplateKey">The type of reply, e.g. "legal-acknowledgement".</param>
/// <param name="DepartmentId">The department replying.</param>
/// <param name="Actor">Who did it: a person, "API key: ...", or the drafting engine.</param>
/// <param name="Url">Link to the document in the Smart Inbox web app.</param>
public sealed record DraftEventData(
    Guid DraftId, Guid DocumentId, int Version, DraftStatus Status, string? TemplateKey, int? DepartmentId, string? Actor, string Url);

/// <summary>An event about a reply draft.</summary>
public abstract record DraftEvent : SmartInboxEvent
{
    /// <summary>The draft and its key fields.</summary>
    public DraftEventData Data { get; init; } = null!;
}

/// <summary><c>draft.ready</c>: a reply draft has been prepared and is waiting for a person.</summary>
public sealed record DraftReadyEvent : DraftEvent;
/// <summary><c>draft.approved</c>: a person approved a reply; it's ready to be sent by the usual channel.</summary>
public sealed record DraftApprovedEvent : DraftEvent;

/// <summary>Two letters found to belong together, without their content: fetch them with <c>Documents.RelatedAsync</c>.</summary>
/// <param name="DocumentId">The newer letter.</param>
/// <param name="RelatedDocumentId">The earlier letter; null when it only quotes one of our outgoing letters registered by hand.</param>
/// <param name="OutgoingLetterId">Our outgoing letter it replies to, if any.</param>
/// <param name="Kind">Why they belong together.</param>
/// <param name="Url">Link to the letter in the web app.</param>
public sealed record LinkEventData(Guid DocumentId, Guid? RelatedDocumentId, Guid? OutgoingLetterId, LinkKind Kind, string? Url);

/// <summary><c>document.linked</c>: a letter was found to belong with another (duplicate, follow-up, reply, email thread).</summary>
public sealed record DocumentLinkedEvent : SmartInboxEvent
{
    /// <summary>The two letters and the kind of link.</summary>
    public LinkEventData Data { get; init; } = null!;
}

/// <summary>An outgoing number, without the letter: fetch it with <c>Register.GetAsync(OutgoingLetterId)</c>.</summary>
/// <param name="OutgoingLetterId">The register entry.</param>
/// <param name="Number">The official number, e.g. MDS/OUT/2026/0042.</param>
/// <param name="DepartmentId">The department it was issued for.</param>
/// <param name="DocumentId">The incoming letter it answers, if any.</param>
/// <param name="ReplyDraftId">The approved reply draft, if it came from one.</param>
/// <param name="IssuedBy">A person or "API key: ...".</param>
/// <param name="Url">Link to the incoming document in the web app, if any.</param>
public sealed record RegisterEventData(
    Guid OutgoingLetterId, string Number, int DepartmentId, Guid? DocumentId, Guid? ReplyDraftId, string? IssuedBy, string? Url);

/// <summary><c>register.issued</c>: an outgoing number was issued, on approving a reply or by hand.</summary>
public sealed record RegisterIssuedEvent : SmartInboxEvent
{
    /// <summary>The number and its links.</summary>
    public RegisterEventData Data { get; init; } = null!;
}

/// <summary><c>webhook.ping</c>: an admin clicked "Send test".</summary>
public sealed record WebhookPingEvent : SmartInboxEvent
{
    /// <summary>Free-form test data.</summary>
    public JsonElement Data { get; init; }
}

/// <summary>An event type this SDK version doesn't know yet. Upgrade the package to get a typed record.</summary>
public sealed record UnknownEvent : SmartInboxEvent
{
    /// <summary>The raw <c>data</c> object.</summary>
    public JsonElement Data { get; init; }
}

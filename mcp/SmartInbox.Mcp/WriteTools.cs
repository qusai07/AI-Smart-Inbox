using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace SmartInbox.Mcp;

public sealed record ActionResult(Guid DocumentId, string Done, string? Next = null);

/// <summary>Only registered when writes are enabled. Every call counts against the per-key rate limit.</summary>
[McpServerToolType]
public sealed class WriteTools(ToolRunner run, WriteGate gate)
{
    public const int MaxNote = 1000;
    public const int MaxComment = 2000;

    [McpServerTool(Name = "route_document", Title = "Route a document", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Routes (or re-routes) a document to a department; it lands in that department's inbox as New and they are emailed. "
                 + "Confirm with the user first. Needs a desk key (Admin or Clerk).")]
    public Task<ActionResult> RouteDocument(
        [Description("Document id (GUID)")] Guid documentId,
        [Description("Target department id (see list_departments)")] int departmentId,
        [Description("Why this department, in one or two sentences. Saved on the document's timeline.")] string note,
        CancellationToken ct = default)
    {
        var why = RequireText(note, "note", MaxNote);
        gate.Enter(run.KeyPrefix);
        return run.RunAsync("route_document", documentId, async inbox =>
        {
            await inbox.Documents.RouteAsync(documentId, departmentId, why, ct);
            return new ActionResult(documentId, $"Routed to department {departmentId}.");
        });
    }

    [McpServerTool(Name = "acknowledge_document", Title = "Acknowledge a document", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("The receiving department confirms it has the document and is working on it.")]
    public Task<ActionResult> AcknowledgeDocument([Description("Document id (GUID)")] Guid documentId, CancellationToken ct = default)
    {
        gate.Enter(run.KeyPrefix);
        return run.RunAsync("acknowledge_document", documentId, async inbox =>
        {
            await inbox.Documents.AcknowledgeAsync(documentId, ct);
            return new ActionResult(documentId, "Acknowledged.");
        });
    }

    [McpServerTool(Name = "close_document", Title = "Close a document", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("The receiving department finished the item. The note says what was done and is emailed to whoever routed it. "
                 + "Confirm with the user first.")]
    public Task<ActionResult> CloseDocument(
        [Description("Document id (GUID)")] Guid documentId,
        [Description("What was done, e.g. \"Reply sent on 2 Nov, ref OUT/2026/88\". Saved on the timeline.")] string note,
        CancellationToken ct = default)
    {
        var done = RequireText(note, "note", MaxNote);
        gate.Enter(run.KeyPrefix);
        return run.RunAsync("close_document", documentId, async inbox =>
        {
            await inbox.Documents.CloseAsync(documentId, done, ct);
            return new ActionResult(documentId, "Closed.");
        });
    }

    [McpServerTool(Name = "reopen_document", Title = "Reopen a document", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Reopens a closed document (it goes back to Acknowledged).")]
    public Task<ActionResult> ReopenDocument([Description("Document id (GUID)")] Guid documentId, CancellationToken ct = default)
    {
        gate.Enter(run.KeyPrefix);
        return run.RunAsync("reopen_document", documentId, async inbox =>
        {
            await inbox.Documents.ReopenAsync(documentId, ct);
            return new ActionResult(documentId, "Reopened.");
        });
    }

    [McpServerTool(Name = "reanalyze_document", Title = "Re-run the AI analysis", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Runs the AI analysis again (e.g. after a failure). Never undoes a routing decision. Desk keys only.")]
    public Task<ActionResult> ReanalyzeDocument([Description("Document id (GUID)")] Guid documentId, CancellationToken ct = default)
    {
        gate.Enter(run.KeyPrefix);
        return run.RunAsync("reanalyze_document", documentId, async inbox =>
        {
            await inbox.Documents.ReanalyzeAsync(documentId, ct);
            return new ActionResult(documentId, "Re-analysis started.", "Call get_document in a few seconds for the new analysis.");
        });
    }

    [McpServerTool(Name = "add_comment", Title = "Add an internal comment", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Adds an internal comment to the document's discussion (visible to staff, never sent to the sender).")]
    public Task<ActionResult> AddComment(
        [Description("Document id (GUID)")] Guid documentId,
        [Description("The comment, max 2000 characters")] string text,
        CancellationToken ct = default)
    {
        var comment = RequireText(text, "text", MaxComment);
        gate.Enter(run.KeyPrefix);
        return run.RunAsync("add_comment", documentId, async inbox =>
        {
            await inbox.Documents.CommentAsync(documentId, comment, ct);
            return new ActionResult(documentId, "Comment added.");
        });
    }

    [McpServerTool(Name = "submit_text", Title = "Submit pasted text as a document", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates a new document from text the user pasted (an email body, a transcribed letter). The AI analysis runs in the background. Desk keys only.")]
    public Task<ActionResult> SubmitText([Description("The full text of the correspondence")] string text, CancellationToken ct = default)
    {
        var body = RequireText(text, "text", 100_000);
        gate.Enter(run.KeyPrefix);
        return run.RunAsync("submit_text", null, async inbox =>
        {
            var doc = await inbox.Documents.UploadTextAsync(body, ct);
            return new ActionResult(doc.Id, "Submitted; analysis is running.", "Call get_document in a few seconds to see the analysis.");
        });
    }

    private static string RequireText(string? value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new McpException($"'{name}' is required.");
        var trimmed = value.Trim();
        if (trimmed.Length > max) throw new McpException($"'{name}' is too long (max {max} characters).");
        return trimmed;
    }
}

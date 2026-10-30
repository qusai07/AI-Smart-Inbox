using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SmartInbox.Client;

namespace SmartInbox.Mcp;

public sealed record DraftView(
    Guid DraftId, Guid DocumentId, int Version, bool IsLatest, DraftStatus Status, string? TemplateKey, string? Engine,
    string? SubjectEn, string? BodyEn, string? SubjectAr, string? BodyAr,
    int Gaps, IReadOnlyList<string> MissingInfo, IReadOnlyList<string> Commitments, string? DecidedBy, string? Error, string Next)
{
    public static DraftView From(ReplyDraft d) => new(
        d.Id, d.DocumentId, d.Version, d.IsLatest, d.Status, d.TemplateKey, d.Engine,
        d.SubjectEn, d.BodyEn, d.SubjectAr, d.BodyAr, d.Placeholders, d.MissingInfo, d.Commitments, d.DecidedBy, d.Error,
        d.Status switch
        {
            DraftStatus.Ready when d.Placeholders > 0 =>
                $"Show the user the draft and the {d.Placeholders} [●] gap(s) to fill (see missingInfo). A person edits and approves it in the Smart Inbox web app; it can't be approved here.",
            DraftStatus.Ready => "Show the user the draft. A person approves it in the Smart Inbox web app; it can't be approved here.",
            DraftStatus.Approved => "Approved by a person. Smart Inbox doesn't send it: it goes out through the usual channel.",
            DraftStatus.Failed => "The draft could not be prepared. Tell the user; they can try again.",
            _ => "Still being prepared; call get_reply_drafts in a moment.",
        });
}

public sealed record TemplateRow(string Key, string NameEn, string NameAr, IReadOnlyList<DocumentCategory> UsedFor);

/// <summary>Reading reply drafts and their templates (always available).</summary>
[McpServerToolType]
public sealed class DraftReadTools(ToolRunner run)
{
    [McpServerTool(Name = "list_reply_templates", Title = "List reply types", ReadOnly = true, OpenWorld = false)]
    [Description("The types of reply Smart Inbox can draft (e.g. legal-acknowledgement, accept-invitation), with the letter categories each is used for by default.")]
    public Task<IReadOnlyList<TemplateRow>> ListReplyTemplates(CancellationToken ct = default) =>
        run.RunAsync<IReadOnlyList<TemplateRow>>("list_reply_templates", null, async inbox =>
            (await inbox.Drafts.ListTemplatesAsync(ct)).Select(t => new TemplateRow(t.Key, t.NameEn, t.NameAr, t.Categories)).ToList());

    [McpServerTool(Name = "get_reply_drafts", Title = "Get reply drafts", ReadOnly = true, OpenWorld = false)]
    [Description("The reply drafts for a document: the latest version in full (English and Arabic, gaps, missing info, commitments) and a list of earlier versions.")]
    public Task<object> GetReplyDrafts([Description("Document id (GUID)")] Guid documentId, CancellationToken ct = default) =>
        run.RunAsync<object>("get_reply_drafts", documentId, async inbox =>
        {
            var drafts = await inbox.Drafts.ListAsync(documentId, ct);
            if (drafts.Count == 0) return new { message = "No reply drafts yet. Use draft_reply to prepare one (needs writes enabled)." };
            return new
            {
                Latest = DraftView.From(drafts[0]),
                Earlier = drafts.Skip(1).Select(d => new { DraftId = d.Id, d.Version, d.Status, d.Engine, d.EditedBy, d.CreatedAt }),
            };
        });
}

/// <summary>Preparing a draft (write). There is deliberately no approve tool: approval is a person's decision in the web app.</summary>
[McpServerToolType]
public sealed class DraftWriteTools(ToolRunner run, WriteGate gate)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    [McpServerTool(Name = "draft_reply", Title = "Draft a reply", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Prepares a formal reply to a routed document in English and Arabic from a template, and returns it. Nothing is sent and "
                 + "nothing is approved: unknown facts are [●] gaps that a person fills in and approves in the web app.")]
    public Task<DraftView> DraftReply(
        [Description("Document id (GUID); the document must be routed to a department")] Guid documentId,
        [Description("Type of reply from list_reply_templates; omit to choose by the letter's category")] string? templateKey = null,
        [Description("Notes kept with the draft for whoever finishes it, e.g. \"ask for the signed contract\"")] string? instructions = null,
        CancellationToken ct = default)
    {
        if (instructions is { Length: > 1000 }) throw new McpException("'instructions' is too long (max 1000 characters).");
        gate.Enter(run.KeyPrefix);
        return run.RunAsync("draft_reply", documentId, async inbox =>
        {
            var requested = await inbox.Drafts.RequestAsync(documentId, templateKey, instructions, ct);
            return DraftView.From(await inbox.Drafts.WaitForDraftAsync(requested.Id, Wait, ct));
        });
    }
}

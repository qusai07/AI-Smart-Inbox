using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartInbox.Api.Auth;
using SmartInbox.Application;

namespace SmartInbox.Api.Controllers;

/// <summary>Reply drafts. Anyone who can handle the document (the desk, or its department) can draft, edit and decide.</summary>
[ApiController]
[Authorize(Policy = Policies.Member)]
public class DraftsController(ReplyDraftService drafts) : ControllerBase
{
    [HttpPost("api/documents/{id:guid}/drafts")]
    public async Task<ActionResult<ReplyDraftDto>> Create(Guid id, RequestDraftRequest request, IValidator<RequestDraftRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        var result = await drafts.RequestAsync(id, request, ct);
        return result.Outcome == ReplyDraftService.Outcome.Ok
            ? AcceptedAtAction(nameof(Get), new { draftId = result.Draft!.Id }, result.Draft)
            : Failure(result);
    }

    [HttpGet("api/reply-templates")]
    public IReadOnlyList<ReplyTemplateDto> Templates([FromServices] ReplyTemplateCatalog catalog) => catalog.List();

    [HttpGet("api/documents/{id:guid}/drafts")]
    public async Task<ActionResult<IReadOnlyList<ReplyDraftDto>>> List(Guid id, CancellationToken ct) =>
        await drafts.ListAsync(id, ct) is { } list ? Ok(list) : NotFound();

    [HttpGet("api/drafts/{draftId:guid}")]
    public async Task<ActionResult<ReplyDraftDto>> Get(Guid draftId, CancellationToken ct) =>
        await drafts.GetAsync(draftId, ct) is { } draft ? draft : NotFound();

    [HttpPut("api/drafts/{draftId:guid}")]
    public async Task<ActionResult<ReplyDraftDto>> Update(Guid draftId, UpdateDraftRequest request, IValidator<UpdateDraftRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        var result = await drafts.UpdateAsync(draftId, request, ct);
        return result.Outcome == ReplyDraftService.Outcome.Ok ? result.Draft! : Failure(result);
    }

    [HttpGet("api/drafts/{draftId:guid}/docx")]
    public async Task<IActionResult> Docx(Guid draftId, [FromServices] IDraftExporter exporter, CancellationToken ct) =>
        await drafts.ExportAsync(draftId, exporter, ct) is { } file
            ? File(file.Content, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", file.FileName)
            : NotFound();

    [HttpPost("api/drafts/{draftId:guid}/approve")]
    public async Task<ActionResult<ReplyDraftDto>> Approve(Guid draftId, CancellationToken ct)
    {
        var result = await drafts.ApproveAsync(draftId, ct);
        return result.Outcome == ReplyDraftService.Outcome.Ok ? result.Draft! : Failure(result);
    }

    [HttpPost("api/drafts/{draftId:guid}/reject")]
    public async Task<ActionResult<ReplyDraftDto>> Reject(Guid draftId, RejectDraftRequest request, IValidator<RejectDraftRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        var result = await drafts.RejectAsync(draftId, request.Reason, ct);
        return result.Outcome == ReplyDraftService.Outcome.Ok ? result.Draft! : Failure(result);
    }

    private ActionResult Failure(ReplyDraftService.Result result) => result.Outcome switch
    {
        ReplyDraftService.Outcome.NotFound => NotFound(),
        ReplyDraftService.Outcome.Disabled => Problem("Reply drafting is turned off (Drafting:Enabled).", statusCode: 404),
        ReplyDraftService.Outcome.UnknownTemplate => Problem("Unknown reply template. See /api/reply-templates.", statusCode: 400),
        ReplyDraftService.Outcome.NotRouted => Problem("Route the document to a department before drafting a reply.", statusCode: 409),
        ReplyDraftService.Outcome.Busy => Problem("A draft for this document is already being prepared.", statusCode: 409),
        ReplyDraftService.Outcome.NotLatest => Problem("A newer version of this draft exists. Work on the latest version.", statusCode: 409),
        ReplyDraftService.Outcome.NotEditable => Problem("Only a ready draft can be edited, approved or rejected.", statusCode: 409),
        ReplyDraftService.Outcome.Incomplete => Problem(
            $"Fill in every {ReplyDraftRules.Placeholder} and both languages before approving ({result.Draft?.Placeholders ?? 0} left).", statusCode: 409),
        _ => Problem(statusCode: 500),
    };
}

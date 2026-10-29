using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartInbox.Api.Auth;
using SmartInbox.Application;

namespace SmartInbox.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize(Policy = Policies.Member)]
public class DocumentsController(DocumentService documents) : ControllerBase
{
    public class UploadForm
    {
        public IFormFile? File { get; set; }
        public string? Text { get; set; }
    }

    [HttpPost]
    [Authorize(Policy = Policies.Desk)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadRules.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] UploadForm form, CancellationToken ct)
    {
        var (file, text) = (form.File, form.Text);

        if (file is not null)
        {
            if (file.Length == 0) return Problem("The file is empty.", statusCode: 400);
            if (file.Length > UploadRules.MaxFileBytes) return Problem("The file is larger than 20 MB.", statusCode: 400);
            var contentType = UploadRules.ResolveContentType(file.FileName);
            if (contentType is null)
                return Problem($"Unsupported file type. Allowed: {string.Join(", ", UploadRules.AllowedExtensions.Keys)}", statusCode: 400);

            await using var stream = file.OpenReadStream();
            var doc = await documents.UploadAsync(stream, file.FileName, contentType, User.Identity?.Name, ct);
            return CreatedAtAction(nameof(Get), new { id = doc.Id }, new { id = doc.Id, status = doc.Status });
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            if (text.Length > UploadRules.MaxPastedTextLength) return Problem("The pasted text is too long.", statusCode: 400);
            var doc = await documents.UploadTextAsync(text, User.Identity?.Name, ct);
            return CreatedAtAction(nameof(Get), new { id = doc.Id }, new { id = doc.Id, status = doc.Status });
        }

        return Problem("Provide a file or text.", statusCode: 400);
    }

    [HttpGet]
    public Task<PagedResult<DocumentListItemDto>> List([FromQuery] DocumentQuery query, CancellationToken ct) =>
        documents.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDetailDto>> Get(Guid id, CancellationToken ct) =>
        await documents.GetAsync(id, ct) is { } doc ? doc : NotFound();

    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> File(Guid id, CancellationToken ct)
    {
        if (await documents.OpenFileAsync(id, ct) is not { } file) return NotFound();
        // inline so PDFs and images preview in the browser.
        Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(file.FileName)}";
        // Uploaded content is untrusted. nosniff is set globally; text, email and Word files are also sandboxed so
        // nothing in them can run script on our origin. (A sandbox would break the browser's PDF/image viewer.)
        if (file.ContentType is not ("application/pdf" or "image/png" or "image/jpeg"))
            Response.Headers.ContentSecurityPolicy = "sandbox";
        return File(file.Content, file.ContentType);
    }

    [HttpPost("{id:guid}/reanalyze")]
    [Authorize(Policy = Policies.Desk)]
    public async Task<IActionResult> Reanalyze(Guid id, CancellationToken ct) =>
        await documents.ReanalyzeAsync(id, ct) switch
        {
            DocumentService.ReanalyzeOutcome.Accepted => Accepted(),
            DocumentService.ReanalyzeOutcome.NotFound => NotFound(),
            _ => Problem("The document is already being analyzed.", statusCode: 409),
        };

    [HttpPost("{id:guid}/route")]
    [Authorize(Policy = Policies.Desk)]
    public async Task<IActionResult> Route(Guid id, RouteRequest request, IValidator<RouteRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        return await documents.RouteAsync(id, request, User.Identity?.Name, ct) switch
        {
            DocumentService.RouteOutcome.Ok => NoContent(),
            DocumentService.RouteOutcome.NotFound => NotFound(),
            DocumentService.RouteOutcome.UnknownDepartment => Problem("Unknown department.", statusCode: 400),
            _ => Problem("The document is still being processed.", statusCode: 409),
        };
    }

    [HttpPost("{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct) =>
        HandlingResult(await documents.AcknowledgeAsync(id, ct));

    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CloseRequest request, IValidator<CloseRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return HandlingResult(await documents.CloseAsync(id, request.Note, ct));
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct) =>
        HandlingResult(await documents.ReopenAsync(id, ct));

    [HttpPost("{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, AssignRequest request, [FromServices] CollaborationService collaboration, CancellationToken ct) =>
        await collaboration.AssignAsync(id, request.UserId, ct) switch
        {
            CollaborationService.AssignOutcome.Ok => NoContent(),
            CollaborationService.AssignOutcome.NotFound => NotFound(),
            CollaborationService.AssignOutcome.NotRouted => Problem("The document has not been routed to a department yet.", statusCode: 409),
            _ => Problem("That person is not staff of this document's department.", statusCode: 400),
        };

    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<CommentDto>> Comment(
        Guid id, CommentRequest request, IValidator<CommentRequest> validator, [FromServices] CollaborationService collaboration, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return await collaboration.AddCommentAsync(id, request.Text, ct) is { } comment ? comment : NotFound();
    }

    private IActionResult HandlingResult(DocumentService.HandlingOutcome outcome) => outcome switch
    {
        DocumentService.HandlingOutcome.Ok => NoContent(),
        DocumentService.HandlingOutcome.NotFound => NotFound(),
        DocumentService.HandlingOutcome.Forbidden => Problem("This document belongs to another department.", statusCode: 403),
        _ => Problem("The document has not been routed to a department yet.", statusCode: 409),
    };
}

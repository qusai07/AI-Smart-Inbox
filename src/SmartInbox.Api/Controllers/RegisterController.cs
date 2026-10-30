using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartInbox.Api.Auth;
using SmartInbox.Application;

namespace SmartInbox.Api.Controllers;

/// <summary>
/// The outgoing register. The desk sees and issues for every department, department staff for their own;
/// cancelling a number is for the desk.
/// </summary>
[ApiController]
[Route("api/register")]
[Authorize(Policy = Policies.Member)]
public class RegisterController(RegisterService register) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<OutgoingLetterDto>>> List([FromQuery] RegisterQuery query, IValidator<RegisterQuery> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(query, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return await register.ListAsync(query, ct);
    }

    [HttpGet("export.csv")]
    public async Task<IActionResult> Export([FromQuery] RegisterQuery query, IValidator<RegisterQuery> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(query with { PageSize = 1 }, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return File(await register.ExportCsvAsync(query, ct), "text/csv; charset=utf-8", $"outgoing-register-{query.Year?.ToString() ?? "all"}.csv");
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OutgoingLetterDto>> Get(Guid id, CancellationToken ct) =>
        await register.GetAsync(id, ct) is { } letter ? letter : NotFound();

    /// <summary>A letter written outside Smart Inbox (or not a reply) gets its number here.</summary>
    [HttpPost]
    public async Task<ActionResult<OutgoingLetterDto>> Issue(IssueRequest request, IValidator<IssueRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        var result = await register.IssueAsync(request, ct);
        return result.Outcome == RegisterService.Outcome.Ok
            ? CreatedAtAction(nameof(Get), new { id = result.Letter!.Id }, result.Letter)
            : Failure(result);
    }

    [HttpPost("{id:guid}/sent")]
    public async Task<ActionResult<OutgoingLetterDto>> MarkSent(Guid id, MarkSentRequest request, IValidator<MarkSentRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        var result = await register.MarkSentAsync(id, request, ct);
        return result.Outcome == RegisterService.Outcome.Ok ? result.Letter! : Failure(result);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.Desk)]
    public async Task<ActionResult<OutgoingLetterDto>> Cancel(Guid id, CancelRequest request, IValidator<CancelRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        var result = await register.CancelAsync(id, request.Reason, ct);
        return result.Outcome == RegisterService.Outcome.Ok ? result.Letter! : Failure(result);
    }

    private ActionResult Failure(RegisterService.Result result) => result.Outcome switch
    {
        RegisterService.Outcome.NotFound => NotFound(),
        RegisterService.Outcome.Forbidden => Problem("You can only issue numbers for your own department.", statusCode: 403),
        RegisterService.Outcome.UnknownDepartment => Problem("Unknown department.", statusCode: 400),
        RegisterService.Outcome.NotAllowedNow => Problem("A cancelled number can't be sent or cancelled again.", statusCode: 409),
        _ => Problem(statusCode: 500),
    };
}

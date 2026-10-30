using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartInbox.Api.Auth;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.Api.Controllers;

/// <summary>The shared mailbox: status, connection test, manual check and the import log. Signed-in admins only.</summary>
[ApiController]
[Route("api/mailbox")]
[Authorize(Policy = Policies.Admin, AuthenticationSchemes = AuthSetup.CookieScheme)]
public class MailboxController(IMailboxRunner runner, MailboxAdminService admin) : ControllerBase
{
    [HttpGet("status")]
    public MailboxStatus Status() => runner.Status;

    /// <summary>Signs in and counts waiting emails; changes nothing.</summary>
    [HttpPost("test")]
    public Task<MailboxTestResult> Test(CancellationToken ct) => runner.TestAsync(ct);

    /// <summary>Imports waiting emails now instead of at the next scheduled check.</summary>
    [HttpPost("check")]
    public async Task<ActionResult<MailboxRunResult>> Check(CancellationToken ct)
    {
        try
        {
            return await runner.CheckNowAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: 409);
        }
    }

    [HttpGet("imports")]
    public Task<PagedResult<InboundEmailDto>> Imports(int page = 1, InboundEmailStatus? status = null, CancellationToken ct = default) =>
        admin.ImportsAsync(page, status, ct);

    /// <summary>Gives a failed email a fresh set of attempts; it's picked up at the next check.</summary>
    [HttpPost("imports/{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct) =>
        await admin.RetryAsync(id, ct) ? NoContent() : NotFound();
}

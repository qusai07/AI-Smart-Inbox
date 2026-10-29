using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartInbox.Api.Auth;
using SmartInbox.Application;

namespace SmartInbox.Api.Controllers;

[ApiController]
[Route("api/stats")]
[Authorize(Policy = Policies.Desk)]
public class StatsController(DocumentService documents) : ControllerBase
{
    [HttpGet]
    public Task<StatsDto> Get(CancellationToken ct) => documents.GetStatsAsync(ct);
}

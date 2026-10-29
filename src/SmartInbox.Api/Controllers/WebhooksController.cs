using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartInbox.Api.Auth;
using SmartInbox.Application;

namespace SmartInbox.Api.Controllers;

/// <summary>Webhook endpoints, delivery log and redelivery. Signed-in admins only (cookie): these return signing secrets.</summary>
[ApiController]
[Route("api/webhooks")]
[Authorize(Policy = Policies.Admin, AuthenticationSchemes = AuthSetup.CookieScheme)]
public class WebhooksController(WebhookAdminService webhooks, IValidator<WebhookEndpointRequest> validator) : ControllerBase
{
    [HttpGet("events")]
    public IReadOnlyList<string> Events() => WebhookEvents.Subscribable;

    [HttpGet]
    public Task<IReadOnlyList<WebhookEndpointDto>> List(CancellationToken ct) => webhooks.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<WebhookSecretDto>> Create(WebhookEndpointRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return await webhooks.CreateAsync(request, ct) is { } created
            ? StatusCode(StatusCodes.Status201Created, created)
            : Problem("Unknown department.", statusCode: 400);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, WebhookEndpointRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        return await webhooks.UpdateAsync(id, request, ct) switch
        {
            WebhookAdminService.Outcome.NotFound => NotFound(),
            WebhookAdminService.Outcome.UnknownDepartment => Problem("Unknown department.", statusCode: 400),
            _ => NoContent(),
        };
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await webhooks.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [HttpPost("{id:int}/rotate-secret")]
    public async Task<ActionResult<WebhookSecretDto>> RotateSecret(int id, CancellationToken ct) =>
        await webhooks.RotateSecretAsync(id, ct) is { } rotated ? rotated : NotFound();

    [HttpPost("{id:int}/ping")]
    public async Task<ActionResult<WebhookDeliveryDto>> Ping(int id, CancellationToken ct) =>
        await webhooks.PingAsync(id, ct) is { } delivery ? delivery : NotFound();

    [HttpGet("{id:int}/deliveries")]
    public async Task<ActionResult<PagedResult<WebhookDeliveryDto>>> Deliveries(int id, int page = 1, CancellationToken ct = default) =>
        await webhooks.DeliveriesAsync(id, page, ct) is { } result ? result : NotFound();

    [HttpPost("deliveries/{deliveryId:long}/redeliver")]
    public async Task<ActionResult<WebhookDeliveryDto>> Redeliver(long deliveryId, CancellationToken ct) =>
        await webhooks.RedeliverAsync(deliveryId, ct) is { } delivery ? delivery : NotFound();
}

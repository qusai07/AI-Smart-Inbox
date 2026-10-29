using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartInbox.Api.Auth;
using SmartInbox.Application;

namespace SmartInbox.Api.Controllers;

/// <summary>
/// Managing keys needs a signed-in admin (cookie only): a leaked admin key must not be able to mint more keys.
/// </summary>
[ApiController]
[Route("api/api-keys")]
[Authorize(Policy = Policies.Admin, AuthenticationSchemes = AuthSetup.CookieScheme)]
public class ApiKeysController(ApiKeyService keys) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ApiKeyDto>> List(CancellationToken ct) => keys.ListAsync(ct);

    [HttpPost]
    public async Task<ActionResult<CreatedApiKeyDto>> Create(CreateApiKeyRequest request, IValidator<CreateApiKeyRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));

        return await keys.CreateAsync(request, ct) is { } created
            ? StatusCode(StatusCodes.Status201Created, created)
            : Problem("Department keys need a valid department.", statusCode: 400);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct) =>
        await keys.RevokeAsync(id, ct) ? NoContent() : NotFound();
}

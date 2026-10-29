using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SmartInbox.Api.Auth;
using SmartInbox.Application;
using SmartInbox.Infrastructure.Identity;

namespace SmartInbox.Api.Controllers;

public record LoginRequest(string Email, string Password);

public record MeDto(string Id, string DisplayName, string? Email, string? Role, int? DepartmentId, bool HasPassword, IReadOnlyList<string> Logins);

[ApiController]
[Route("api/auth")]
public class AuthController(SignInManager<AppUser> signIn,UserManager<AppUser> users,IOptions<EntraOptions> entra,ILogger<AuthController> logger) : ControllerBase
    {
    [AllowAnonymous]
    [HttpGet("providers")]
    public object Providers() => new { local = true, entra = entra.Value.Enabled };

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<MeDto>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) return Problem("Invalid email or password.", statusCode: 401);

        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);
        // Admins disable accounts with a permanent lockout; don't tell those users to "try again in a few minutes".
        if (result.IsLockedOut && user.LockoutEnd > DateTimeOffset.UtcNow.AddYears(50))
            return Problem("This account has been disabled. Contact your administrator.", statusCode: 403);
        if (result.IsLockedOut) return Problem("Too many failed attempts. Try again in a few minutes.", statusCode: 429);
        if (!result.Succeeded) return Problem("Invalid email or password.", statusCode: 401);

        user.LastSignInAt = DateTime.UtcNow;
        await users.UpdateAsync(user);
        return await MeFor(user);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> Me() =>
        await users.GetUserAsync(User) is { } user ? await MeFor(user) : Unauthorized();

    /// <summary>Starts "Sign in with Microsoft". The browser navigates here (not fetch).</summary>
    [AllowAnonymous]
    [HttpGet("entra")]
    public IActionResult Entra(string? returnUrl)
    {
        if (!entra.Value.Enabled) return NotFound();
        var callback = Url.Action(nameof(EntraCallback), new { returnUrl = SafeReturnUrl(returnUrl) });
        return Challenge(signIn.ConfigureExternalAuthenticationProperties(AuthSetup.EntraScheme, callback), AuthSetup.EntraScheme);
    }

    [AllowAnonymous]
    [HttpGet("entra/callback")]
    public async Task<IActionResult> EntraCallback(string? returnUrl)
    {
        var target = SafeReturnUrl(returnUrl);
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null) return LocalRedirect("/login?error=entra");

        var signedIn = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
        if (signedIn.IsLockedOut) return LocalRedirect("/login?error=locked");

        if (!signedIn.Succeeded)
        {
            var email = info.Principal.FindFirstValue(ClaimTypes.Email) ?? info.Principal.FindFirstValue("preferred_username");
            if (string.IsNullOrWhiteSpace(email)) return LocalRedirect("/login?error=entra");

            // First Microsoft sign-in: link to an existing account with the same email, or create a pending one.
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new AppUser
                {
                    UserName = email, Email = email, EmailConfirmed = true,
                    DisplayName = info.Principal.FindFirstValue("name") ?? email,
                };
                var created = await users.CreateAsync(user);
                if (!created.Succeeded) return LocalRedirect("/login?error=entra");
                logger.LogInformation("New Microsoft user {Email} is pending role assignment", email);
            }
            await users.AddLoginAsync(user, info);
            await signIn.SignInAsync(user, isPersistent: true, info.LoginProvider);
        }

        if (await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey) is { } signedInUser)
        {
            signedInUser.LastSignInAt = DateTime.UtcNow;
            await users.UpdateAsync(signedInUser);
        }
        return LocalRedirect(target);
    }

    private async Task<MeDto> MeFor(AppUser user)
    {
        var role = (await users.GetRolesAsync(user)).FirstOrDefault();
        var logins = (await users.GetLoginsAsync(user)).Select(l => l.ProviderDisplayName ?? l.LoginProvider).ToList();
        return new MeDto(user.Id, user.DisplayName, user.Email, role, user.DepartmentId, await users.HasPasswordAsync(user), logins);
    }

    private string SafeReturnUrl(string? url) => !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? url : "/";
}

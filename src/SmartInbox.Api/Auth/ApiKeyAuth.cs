using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SmartInbox.Application;

namespace SmartInbox.Api.Auth;

/// <summary>
/// "Authorization: Bearer sik_..." for the SDK and other systems. A key becomes a principal with the key's role
/// and department, so every existing policy and department restriction applies unchanged.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ApiKeyService keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "ApiKey";
    private const string BearerPrefix = "Bearer ";

    /// <summary>True when the request presents an API key (as opposed to the browser cookie).</summary>
    public static bool HasKey(HttpRequest request) => PresentedKey(request) is not null;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (PresentedKey(Request) is not { } presented) return AuthenticateResult.NoResult();

        var key = await keys.AuthenticateAsync(presented, Context.RequestAborted);
        if (key is null) return AuthenticateResult.Fail("Invalid, expired or revoked API key.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"apikey:{key.Id}"),
            new(ClaimTypes.Name, key.Prefix),
            new(ClaimNames.DisplayName, $"API key: {key.Name}"),
            new(ClaimTypes.Role, key.Role),
        };
        if (key.DepartmentId is { } dep) claims.Add(new Claim(ClaimNames.DepartmentId, dep.ToString()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    private static string? PresentedKey(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var token = header[BearerPrefix.Length..].Trim();
        return token.StartsWith(ApiKeyFormat.Marker, StringComparison.Ordinal) ? token : null;
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SmartInbox.Application;
using SmartInbox.Infrastructure.Identity;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.Api.Auth;

public class EntraOptions
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string CallbackPath { get; set; } = "/signin-oidc";
    public bool Enabled => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public static class Policies
{
    /// <summary>Any signed-in user who has been given a role (pending Entra users have none).</summary>
    public const string Member = "Member";
    /// <summary>Correspondence desk: upload, analyze, route, see stats.</summary>
    public const string Desk = "Desk";
    public const string Admin = "Admin";
}

public static class ClaimNames
{
    public const string DisplayName = "display_name";
    public const string DepartmentId = "department_id";
}

public static class AuthSetup
{
    public const string EntraScheme = "Entra";
    public const string CookieOrApiKeyScheme = "CookieOrApiKey";
    /// <summary>Same as IdentityConstants.ApplicationScheme, but usable in attributes.</summary>
    public const string CookieScheme = "Identity.Application";

    public static IServiceCollection AddSmartInboxAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddIdentity<AppUser, IdentityRole>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            })
            .AddEntityFrameworkStores<SmartInboxDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<AppClaimsFactory>();

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "smartinbox.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = true;
            // An API answers with status codes, not redirects to a login page.
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });
        // Role / department changes made by an admin reach active sessions within a minute.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

        // Every request is authenticated by the cookie, unless it carries an API key. Must come after
        // AddIdentity, which sets the cookie as the default; sign-in still goes through Identity's own schemes.
        services.AddAuthentication(o =>
            {
                o.DefaultAuthenticateScheme = CookieOrApiKeyScheme;
                o.DefaultChallengeScheme = CookieOrApiKeyScheme;
                o.DefaultForbidScheme = CookieOrApiKeyScheme;
            })
            .AddPolicyScheme(CookieOrApiKeyScheme, "Cookie or API key", o => o.ForwardDefaultSelector = ctx =>
                ApiKeyAuthenticationHandler.HasKey(ctx.Request) ? ApiKeyAuthenticationHandler.Scheme : IdentityConstants.ApplicationScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.Scheme, null);

        var entra = config.GetSection("Auth:Entra").Get<EntraOptions>() ?? new EntraOptions();
        services.Configure<EntraOptions>(config.GetSection("Auth:Entra"));
        if (entra.Enabled)
        {
            services.AddAuthentication().AddOpenIdConnect(EntraScheme, "Microsoft", o =>
            {
                o.SignInScheme = IdentityConstants.ExternalScheme;
                o.Authority = $"https://login.microsoftonline.com/{entra.TenantId}/v2.0";
                o.ClientId = entra.ClientId;
                o.ClientSecret = entra.ClientSecret;
                o.CallbackPath = entra.CallbackPath;
                o.ResponseType = "code";
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.Scope.Add("email");
                o.SaveTokens = false;
                o.TokenValidationParameters.NameClaimType = "name";
            });
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Member, p => p.RequireAuthenticatedUser().RequireRole(Roles.All))
            .AddPolicy(Policies.Desk, p => p.RequireAuthenticatedUser().RequireRole(Roles.Admin, Roles.Clerk))
            .AddPolicy(Policies.Admin, p => p.RequireAuthenticatedUser().RequireRole(Roles.Admin));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        return services;
    }
}

/// <summary>Adds display name and department to the auth cookie so every request knows them without a DB hit.</summary>
public class AppClaimsFactory(UserManager<AppUser> users, RoleManager<IdentityRole> roles, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(ClaimNames.DisplayName, string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? user.UserName ?? "" : user.DisplayName));
        if (user.DepartmentId is { } dep) identity.AddClaim(new Claim(ClaimNames.DepartmentId, dep.ToString()));
        return identity;
    }
}

public class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    private ClaimsPrincipal? Principal => http.HttpContext?.User is { Identity.IsAuthenticated: true } p ? p : null;

    public string? Id => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? Name => Principal?.FindFirstValue(ClaimNames.DisplayName) ?? Principal?.Identity?.Name;
    public string? Role => Roles.All.FirstOrDefault(r => Principal?.IsInRole(r) == true);
    public int? DepartmentId => int.TryParse(Principal?.FindFirstValue(ClaimNames.DepartmentId), out var d) ? d : null;
    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);
}

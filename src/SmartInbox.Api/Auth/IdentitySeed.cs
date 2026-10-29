using Microsoft.AspNetCore.Identity;
using SmartInbox.Application;
using SmartInbox.Infrastructure.Identity;

namespace SmartInbox.Api.Auth;

/// <summary>
/// Creates the roles, then either demo users (Development, or Auth:SeedDemoUsers=true) or a first admin
/// from Auth:AdminEmail / Auth:AdminPassword. Existing users are never modified.
/// </summary>
public static class IdentitySeed
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration config, IHostEnvironment env, ILogger logger)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));

        var users = services.GetRequiredService<UserManager<AppUser>>();

        if (env.IsDevelopment() || config.GetValue<bool>("Auth:SeedDemoUsers"))
        {
            var password = config["Auth:DemoPassword"] ?? "Demo#2026";
            (string Email, string Name, string Role, int? Dept)[] demo =
            [
                ("admin@mds.demo", "System Admin", Roles.Admin, null),
                ("clerk@mds.demo", "Sara (Correspondence Desk)", Roles.Clerk, null),
                ("legal.user@mds.demo", "Omar (Legal Affairs)", Roles.Department, 2),
                ("hr.user@mds.demo", "Mariam (Human Resources)", Roles.Department, 4),
            ];
            foreach (var u in demo) await EnsureUserAsync(users, u.Email, u.Name, password, u.Role, u.Dept, logger);
            logger.LogWarning("Demo users are enabled (password from Auth:DemoPassword). Do not use this in production.");
        }
        else if (config["Auth:AdminEmail"] is { Length: > 0 } email && config["Auth:AdminPassword"] is { Length: > 0 } adminPassword)
        {
            await EnsureUserAsync(users, email, "Administrator", adminPassword, Roles.Admin, null, logger);
        }
        else if (!users.Users.Any())
        {
            logger.LogError("No users exist. Set Auth:AdminEmail and Auth:AdminPassword to create the first administrator.");
        }
    }

    private static async Task EnsureUserAsync(UserManager<AppUser> users, string email, string name, string password, string role, int? department, ILogger logger)
    {
        if (await users.FindByEmailAsync(email) is not null) return;
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name, DepartmentId = department };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError("Could not create {Email}: {Errors}", email, string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await users.AddToRoleAsync(user, role);
        logger.LogInformation("Created {Role} user {Email}", role, email);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Api.Auth;
using SmartInbox.Application;
using SmartInbox.Infrastructure.Identity;

namespace SmartInbox.Api.Controllers;

public record UserDto(
    string Id, string DisplayName, string? Email, string? Role, int? DepartmentId,
    bool HasPassword, IReadOnlyList<string> Logins, bool Disabled, DateTime CreatedAt, DateTime? LastSignInAt);

public record CreateUserRequest(string DisplayName, string Email, string? Password, string Role, int? DepartmentId);

public record UpdateUserRequest(string DisplayName, string? Role, int? DepartmentId, bool Disabled);

public record SetPasswordRequest(string Password);

/// <summary>User administration. New Microsoft (Entra) users appear here with no role until an admin assigns one.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.Admin)]
public class UsersController(UserManager<AppUser> users, IAppDbContext db, ICurrentUser me) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<UserDto>> List()
    {
        var all = await users.Users.OrderBy(u => u.DisplayName).ToListAsync();
        var result = new List<UserDto>(all.Count);
        foreach (var u in all) result.Add(await ToDto(u));
        return result;
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request)
    {
        if (await ValidateRole(request.Role, request.DepartmentId) is { } problem) return problem;

        var user = new AppUser
        {
            UserName = request.Email.Trim(), Email = request.Email.Trim(), EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(), DepartmentId = request.Role == Roles.Department ? request.DepartmentId : null,
        };
        // No password = the person signs in with Microsoft only.
        var created = string.IsNullOrEmpty(request.Password) ? await users.CreateAsync(user) : await users.CreateAsync(user, request.Password);
        if (!created.Succeeded) return IdentityProblem(created);

        await users.AddToRoleAsync(user, request.Role);
        return await ToDto(user);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<UserDto>> Update(string id, UpdateUserRequest request)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (request.Role is not null && await ValidateRole(request.Role, request.DepartmentId) is { } problem) return problem;
        if (id == me.Id && (request.Disabled || request.Role != Roles.Admin))
            return Problem("You can't remove your own admin access or disable yourself.", statusCode: 400);

        user.DisplayName = request.DisplayName.Trim();
        user.DepartmentId = request.Role == Roles.Department ? request.DepartmentId : null;
        await users.SetLockoutEndDateAsync(user, request.Disabled ? DateTimeOffset.MaxValue : null);

        var current = await users.GetRolesAsync(user);
        await users.RemoveFromRolesAsync(user, current);
        if (request.Role is not null) await users.AddToRoleAsync(user, request.Role);

        // Invalidates existing sessions so the new role/department applies right away.
        await users.UpdateSecurityStampAsync(user);
        return await ToDto(user);
    }

    [HttpPost("{id}/password")]
    public async Task<IActionResult> SetPassword(string id, SetPasswordRequest request)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (await users.HasPasswordAsync(user)) await users.RemovePasswordAsync(user);
        var result = await users.AddPasswordAsync(user, request.Password);
        return result.Succeeded ? NoContent() : IdentityProblem(result);
    }

    private async Task<ActionResult?> ValidateRole(string role, int? departmentId)
    {
        if (!Roles.All.Contains(role)) return Problem($"Role must be one of: {string.Join(", ", Roles.All)}.", statusCode: 400);
        if (role == Roles.Department && (departmentId is null || !await db.Departments.AnyAsync(d => d.Id == departmentId)))
            return Problem("Department users need a valid department.", statusCode: 400);
        return null;
    }

    private async Task<UserDto> ToDto(AppUser u) => new(
        u.Id, u.DisplayName, u.Email, (await users.GetRolesAsync(u)).FirstOrDefault(), u.DepartmentId,
        await users.HasPasswordAsync(u),
        (await users.GetLoginsAsync(u)).Select(l => l.ProviderDisplayName ?? l.LoginProvider).ToList(),
        u.LockoutEnd > DateTimeOffset.UtcNow.AddYears(50), u.CreatedAt, u.LastSignInAt);

    private ObjectResult IdentityProblem(IdentityResult result) =>
        Problem(string.Join(" ", result.Errors.Select(e => e.Description)), statusCode: 400);
}

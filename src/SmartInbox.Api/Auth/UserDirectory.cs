using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Application;
using SmartInbox.Infrastructure.Identity;

namespace SmartInbox.Api.Auth;

public class UserDirectory(UserManager<AppUser> users) : IUserDirectory
{
    public async Task<DirectoryUser?> FindAsync(string id, CancellationToken ct = default)
    {
        var u = await users.FindByIdAsync(id);
        if (u is null || IsDisabled(u)) return null;
        return new DirectoryUser(u.Id, u.DisplayName, u.Email, (await users.GetRolesAsync(u)).FirstOrDefault(), u.DepartmentId);
    }

    public async Task<IReadOnlyList<DirectoryUser>> DepartmentMembersAsync(int departmentId, CancellationToken ct = default)
    {
        var staff = await users.GetUsersInRoleAsync(Roles.Department);
        return staff
            .Where(u => u.DepartmentId == departmentId && !IsDisabled(u))
            .Select(u => new DirectoryUser(u.Id, u.DisplayName, u.Email, Roles.Department, u.DepartmentId))
            .ToList();
    }

    private static bool IsDisabled(AppUser u) => u.LockoutEnd > DateTimeOffset.UtcNow.AddYears(50);
}

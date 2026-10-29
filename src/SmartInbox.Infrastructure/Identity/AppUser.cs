using Microsoft.AspNetCore.Identity;

namespace SmartInbox.Infrastructure.Identity;

/// <summary>
/// A person who can sign in, with a local password, a Microsoft Entra ID account, or both.
/// Role membership uses Identity roles (see <see cref="Application.Roles"/>).
/// </summary>
public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    /// <summary>Required for the Department role: the inbox this user works.</summary>
    public int? DepartmentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSignInAt { get; set; }
}

namespace SmartInbox.Application;

public static class Roles
{
    /// <summary>Manages users and departments; full access.</summary>
    public const string Admin = "Admin";
    /// <summary>Correspondence desk: uploads, reviews and routes everything.</summary>
    public const string Clerk = "Clerk";
    /// <summary>Works one department's inbox only.</summary>
    public const string Department = "Department";

    public static readonly string[] All = [Admin, Clerk, Department];
}

/// <summary>Who is making the request. Implemented from the auth cookie in the API.</summary>
public interface ICurrentUser
{
    string? Id { get; }
    string? Name { get; }
    string? Role { get; }
    int? DepartmentId { get; }
    string? Email => null;

    /// <summary>Admins and clerks see every document; department users only their own.</summary>
    bool SeesEverything => Role is Roles.Admin or Roles.Clerk;

    /// <summary>The department a department user is restricted to (null = no restriction).</summary>
    int? RestrictedToDepartment => SeesEverything ? null : DepartmentId ?? -1;
}

public static class DocumentAccess
{
    /// <summary>Documents a user may see: everything for the desk, only their department's for department staff.</summary>
    public static IQueryable<Domain.Document> VisibleTo(this IQueryable<Domain.Document> query, ICurrentUser user) =>
        user.RestrictedToDepartment is { } own ? query.Where(d => d.AssignedDepartmentId == own) : query;
}

/// <summary>Background work and tests: full access, no person attached.</summary>
public sealed class SystemUser : ICurrentUser
{
    public static readonly SystemUser Instance = new();
    public string? Id => null;
    public string? Name => null;
    public string? Role => Roles.Admin;
    public int? DepartmentId => null;
}

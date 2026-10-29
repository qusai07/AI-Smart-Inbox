using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Api.Auth;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.Api.Controllers;

public record UpdateDepartmentContactsRequest(string? Email, string? HeadEmail);

[ApiController]
[Route("api/departments")]
[Authorize(Policy = Policies.Member)]
public class DepartmentsController(IAppDbContext db, DocumentService documents) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<Department>> Get(CancellationToken ct) =>
        await db.Departments.AsNoTracking().OrderBy(d => d.Id).ToListAsync(ct);

    /// <summary>New / acknowledged / closed / overdue counts per department (own department only for department users).</summary>
    [HttpGet("workload")]
    public Task<IReadOnlyList<DepartmentWorkloadDto>> Workload(CancellationToken ct) => documents.GetWorkloadAsync(ct);

    /// <summary>
    /// Email = routing notifications and daily digest; HeadEmail = escalations for overdue items. Empty = none.
    /// </summary>
    [HttpPut("{id:int}/contacts")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> SetContacts(int id, UpdateDepartmentContactsRequest request, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        var head = Normalize(request.HeadEmail);
        if (!IsValid(email) || !IsValid(head)) return Problem("Enter a valid email address.", statusCode: 400);

        var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null) return NotFound();
        department.Email = email;
        department.HeadEmail = head;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool IsValid(string? email) => email is null || (email.Length <= 320 && new EmailAddressAttribute().IsValid(email));
}

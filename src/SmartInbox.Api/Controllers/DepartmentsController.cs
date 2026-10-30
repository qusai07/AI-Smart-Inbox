using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Api.Auth;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.Api.Controllers;

public record UpdateDepartmentContactsRequest(string? Email, string? HeadEmail);

public record UpdateSignatoryRequest(string? NameEn, string? NameAr, string? TitleEn, string? TitleAr);

[ApiController]
[Route("api/departments")]
[Authorize(Policy = Policies.Member)]
public class DepartmentsController(IAppDbContext db, DocumentService documents) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<Department>> Get(CancellationToken ct) =>
        await db.Departments.AsNoTracking().OrderBy(d => d.Id).ToListAsync(ct);

    [HttpGet("workload")]
    public Task<IReadOnlyList<DepartmentWorkloadDto>> Workload(CancellationToken ct) => documents.GetWorkloadAsync(ct);

    [HttpGet("{id:int}/members")]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> Members(int id, [FromServices] CollaborationService collaboration, CancellationToken ct) =>
        await collaboration.MembersAsync(id, ct) is { } members ? Ok(members) : NotFound();


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

    /// <summary>Who signs reply letters from this department. Names are optional; titles appear on every letter.</summary>
    [HttpPut("{id:int}/signatory")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> SetSignatory(int id, UpdateSignatoryRequest request, CancellationToken ct)
    {
        if (new[] { request.NameEn, request.NameAr, request.TitleEn, request.TitleAr }.Any(v => v is { Length: > 150 }))
            return Problem("Names and titles are at most 150 characters.", statusCode: 400);

        var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null) return NotFound();
        department.SignatoryNameEn = Normalize(request.NameEn);
        department.SignatoryNameAr = Normalize(request.NameAr);
        department.SignatoryTitleEn = Normalize(request.TitleEn);
        department.SignatoryTitleAr = Normalize(request.TitleAr);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool IsValid(string? email) => email is null || (email.Length <= 320 && new EmailAddressAttribute().IsValid(email));
}

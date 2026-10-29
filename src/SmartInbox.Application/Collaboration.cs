using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SmartInbox.Domain;

namespace SmartInbox.Application;

/// <summary>Work inside a department after routing: who owns an item, and the internal discussion about it.</summary>
public class CollaborationService(IAppDbContext db, IUserDirectory directory, IInboxNotifier notifier, ICurrentUser user)
{
    public const int MaxCommentLength = 2000;

    public enum AssignOutcome { Ok, NotFound, NotRouted, InvalidAssignee }

    /// <summary>
    /// Assigns a routed document to a staff member of its department (or unassigns with null).
    /// Department staff can only see — and so only assign — their own department's documents.
    /// </summary>
    public async Task<AssignOutcome> AssignAsync(Guid id, string? userId, CancellationToken ct)
    {
        var d = await db.Documents.VisibleTo(user).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return AssignOutcome.NotFound;
        if (d.AssignedDepartmentId is not { } department) return AssignOutcome.NotRouted;

        if (string.IsNullOrEmpty(userId))
        {
            if (d.AssigneeId is null) return AssignOutcome.Ok;
            AddEvent(d, $"unassigned ({d.AssigneeName})");
            d.AssigneeId = null;
            d.AssigneeName = null;
            await SaveAsync(d, ct);
            return AssignOutcome.Ok;
        }

        var member = await directory.FindAsync(userId, ct);
        if (member is null || member.Role != Roles.Department || member.DepartmentId != department)
            return AssignOutcome.InvalidAssignee;
        if (d.AssigneeId == member.Id) return AssignOutcome.Ok;

        d.AssigneeId = member.Id;
        d.AssigneeName = member.DisplayName;
        // Someone now owns it, so the department has effectively acknowledged it.
        if (d.Handling == HandlingStatus.New)
        {
            d.Handling = HandlingStatus.Acknowledged;
            d.HandlingUpdatedAt = DateTime.UtcNow;
            d.HandlingBy = user.Name;
        }
        AddEvent(d, member.DisplayName);

        if (!string.IsNullOrWhiteSpace(member.Email) && member.Id != user.Id)
            db.Notifications.Add(new Notification
            {
                DocumentId = d.Id, DepartmentId = department, Kind = NotificationKind.Assigned, To = member.Email,
            });

        await SaveAsync(d, ct);
        return AssignOutcome.Ok;
    }

    /// <summary>People the current user may assign this department's work to.</summary>
    public async Task<IReadOnlyList<MemberDto>?> MembersAsync(int departmentId, CancellationToken ct)
    {
        if (user.RestrictedToDepartment is { } own && own != departmentId) return null;
        return (await directory.DepartmentMembersAsync(departmentId, ct))
            .OrderBy(m => m.DisplayName)
            .Select(m => new MemberDto(m.Id, m.DisplayName))
            .ToList();
    }

    public async Task<CommentDto?> AddCommentAsync(Guid id, string text, CancellationToken ct)
    {
        var d = await db.Documents.VisibleTo(user).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return null;

        var comment = new DocumentComment
        {
            DocumentId = d.Id, AuthorId = user.Id, AuthorName = user.Name ?? "System", Text = text.Trim(),
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        await notifier.DocumentStatusChangedAsync(d.Id, d.Status, Audience.Of(d.AssignedDepartmentId), ct);   // live refresh for other viewers
        return CommentDto.From(comment);
    }

    private void AddEvent(Document d, string note) =>
        db.DocumentEvents.Add(new DocumentEvent
        {
            DocumentId = d.Id, Type = DocumentEventType.Assigned, Actor = user.Name, DepartmentId = d.AssignedDepartmentId, Note = note,
        });

    private async Task SaveAsync(Document d, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await notifier.DocumentStatusChangedAsync(d.Id, d.Status, Audience.Of(d.AssignedDepartmentId), ct);
    }
}

public class CommentRequestValidator : AbstractValidator<CommentRequest>
{
    public CommentRequestValidator() =>
        RuleFor(x => x.Text).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Write a comment.")
            .MaximumLength(CollaborationService.MaxCommentLength);
}

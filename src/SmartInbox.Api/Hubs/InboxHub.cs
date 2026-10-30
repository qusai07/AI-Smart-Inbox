using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SmartInbox.Api.Auth;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.Api.Hubs;

/// <summary>
/// Live updates, scoped like the API: the desk (Admin, Clerk) hears about every document;
/// department staff only about documents routed to their department. Users without a role get nothing.
/// </summary>
[Authorize]
public class InboxHub : Hub
{
    public const string DeskGroup = "desk";
    public static string DepartmentGroup(int departmentId) => $"dept-{departmentId}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user is not null)
        {
            if (user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Clerk))
                await Groups.AddToGroupAsync(Context.ConnectionId, DeskGroup);
            else if (user.IsInRole(Roles.Department) && int.TryParse(user.FindFirstValue(ClaimNames.DepartmentId), out var dep))
                await Groups.AddToGroupAsync(Context.ConnectionId, DepartmentGroup(dep));
        }
        await base.OnConnectedAsync();
    }
}

public class SignalRInboxNotifier(IHubContext<InboxHub> hub) : IInboxNotifier
{
    public Task DocumentStatusChangedAsync(Guid documentId, DocumentStatus status, IReadOnlyCollection<int> departmentIds, CancellationToken ct = default)
    {
        var groups = new List<string>(departmentIds.Count + 1) { InboxHub.DeskGroup };
        groups.AddRange(departmentIds.Select(InboxHub.DepartmentGroup));
        return hub.Clients.Groups(groups).SendAsync("DocumentStatusChanged", new { documentId, status }, ct);
    }

    public Task DraftChangedAsync(Guid documentId, Guid draftId, ReplyDraftStatus status, IReadOnlyCollection<int> departmentIds, CancellationToken ct = default)
    {
        var groups = new List<string>(departmentIds.Count + 1) { InboxHub.DeskGroup };
        groups.AddRange(departmentIds.Select(InboxHub.DepartmentGroup));
        return hub.Clients.Groups(groups).SendAsync("DraftChanged", new { documentId, draftId, status }, ct);
    }
}

using Microsoft.AspNetCore.SignalR;
using SmartInbox.Application;
using SmartInbox.Domain;

namespace SmartInbox.Api.Hubs;

[Microsoft.AspNetCore.Authorization.Authorize]
public class InboxHub : Hub;

public class SignalRInboxNotifier(IHubContext<InboxHub> hub) : IInboxNotifier
{
    public Task DocumentStatusChangedAsync(Guid documentId, DocumentStatus status, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync("DocumentStatusChanged", new { documentId, status }, ct);
}

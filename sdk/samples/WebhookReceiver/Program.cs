// Logs every Smart Inbox event. Add a webhook endpoint pointing at http://<this host>:5090/hooks/smart-inbox,
// copy its secret, and run:  SmartInbox__WebhookSecret=whsec_... dotnet run
using SmartInbox.Client.Webhooks;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://0.0.0.0:5090");
var app = builder.Build();

var secret = app.Configuration["SmartInbox:WebhookSecret"]
             ?? throw new InvalidOperationException("Set SmartInbox:WebhookSecret (env SmartInbox__WebhookSecret) to the endpoint's signing secret.");
var seen = new HashSet<string>(); // at-least-once delivery: ignore repeats (use a database in real receivers)

app.MapSmartInboxWebhooks("/hooks/smart-inbox", secret, (evt, ct) =>
{
    lock (seen)
        if (!seen.Add(evt.Id)) return Task.CompletedTask;

    var line = evt switch
    {
        DocumentRoutedEvent e => $"routed to department {e.Data.DepartmentId} by {e.Data.Actor} (was {e.Data.PreviousDepartmentId?.ToString() ?? "-"})",
        DocumentAnalyzedEvent e => $"analyzed: {e.Data.Category}/{e.Data.Priority}, suggests {e.Data.SuggestedDepartmentId} ({e.Data.Confidence:P0}), {e.Data.Status}",
        DocumentClosedEvent e => $"closed by {e.Data.Actor}",
        DeadlineApproachingEvent e => $"deadline {e.Data.Deadline} approaching (department {e.Data.DepartmentId})",
        DocumentEvent e => $"{e.Data.Status} ({e.Data.Actor})",
        WebhookPingEvent => "ping ✓",
        _ => "(unknown event type; upgrade the SDK)",
    };
    var doc = evt is DocumentEvent d ? $" {d.Data.DocumentId}" : "";
    app.Logger.LogInformation("{Type}{Doc}: {Line}", evt.Type, doc, line);
    return Task.CompletedTask;
});
app.MapGet("/", () => "Smart Inbox webhook receiver. POST events to /hooks/smart-inbox.");

app.Run();

// Routes confident AI suggestions automatically and leaves the rest for people.
// Needs a desk key (Clerk or Admin):  SmartInbox__BaseUrl=http://localhost:5080 SmartInbox__ApiKey=sik_... dotnet run
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartInbox.Client;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSmartInboxClient(o =>
{
    o.BaseUrl = new Uri(builder.Configuration["SmartInbox:BaseUrl"] ?? "http://localhost:5080");
    o.ApiKey = builder.Configuration["SmartInbox:ApiKey"];
});
builder.Services.AddHostedService<AutoRouter>();
builder.Build().Run();

internal sealed class AutoRouter(ISmartInboxClient inbox, ILogger<AutoRouter> logger) : BackgroundService
{
    /// <summary>Above the app's own review threshold (0.6): only route what the AI is sure about.</summary>
    private const double MinConfidence = 0.85;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await RouteConfidentAsync(stoppingToken);
            }
            catch (SmartInboxApiException ex)
            {
                logger.LogError("Smart Inbox API error {Status}: {Message}", (int)ex.StatusCode, ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RouteConfidentAsync(CancellationToken ct)
    {
        // "Analyzed" = the AI made a suggestion with enough confidence; NeedsReview stays with people.
        await foreach (var d in inbox.Documents.ListAllAsync(new DocumentListQuery { Status = DocumentStatus.Analyzed }, ct))
        {
            if (d.SuggestedDepartmentId is not { } department || d.Confidence < MinConfidence)
            {
                logger.LogInformation("Leaving {File} for the desk (confidence {Confidence:P0})", d.FileName, d.Confidence);
                continue;
            }
            try
            {
                await inbox.Documents.RouteAsync(d.Id, department, note: $"Auto-routed ({d.Confidence:P0} confidence)", ct);
                logger.LogInformation("Routed {File} → department {Department}", d.FileName, department);
            }
            catch (SmartInboxConflictException)
            {
                // Re-analysis started in the meantime; we'll see it again next round.
            }
        }
    }
}

using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartInbox.Client.Webhooks;

// Extension methods live in the ASP.NET Core namespace so they show up next to MapGet/MapPost.
namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps a Smart Inbox webhook receiver.</summary>
public static class SmartInboxWebhookEndpointExtensions
{
    /// <summary>Largest body accepted. Smart Inbox payloads are thin (well under 4 KB).</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>
    /// POST endpoint that verifies the signature and timestamp, then calls <paramref name="handler"/> with the typed event.
    /// Answers 400 when verification fails and 200 when the handler completes. If the handler throws, the answer is 500
    /// and Smart Inbox retries later, so handlers must be idempotent (de-duplicate by <see cref="SmartInboxEvent.Id"/>).
    /// </summary>
    public static RouteHandlerBuilder MapSmartInboxWebhooks(
        this IEndpointRouteBuilder endpoints, string pattern, string secret, Func<SmartInboxEvent, CancellationToken, Task> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        return endpoints.MapPost(pattern, async (HttpContext http) =>
        {
            if (http.Request.ContentLength > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            string body;
            using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8))
                body = await reader.ReadToEndAsync(http.RequestAborted);

            SmartInboxEvent evt;
            try
            {
                evt = WebhookVerifier.Verify(body, http.Request.Headers[WebhookVerifier.SignatureHeader], secret);
            }
            catch (WebhookVerificationException ex)
            {
                http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("SmartInbox.Webhooks")
                    .LogWarning("Rejected Smart Inbox webhook: {Reason}", ex.Message);
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }

            await handler(evt, http.RequestAborted);
            return Results.Ok();
        }).DisableAntiforgery();
    }
}

# SmartInbox.Client

Typed .NET client for the Smart Inbox API, plus webhook signature verification.

| Package | What it's for |
|---|---|
| `SmartInbox.Client` | Call the API with an API key; verify webhooks anywhere (`WebhookVerifier`) |
| `SmartInbox.Client.AspNetCore` | `MapSmartInboxWebhooks(...)`: a verified webhook endpoint in one line |

Targets .NET 8.

## Quickstart

1. In Smart Inbox, open **Administration → API keys**, create a key with the role you need (Clerk for upload and routing), and copy it.
2. `dotnet add package SmartInbox.Client`

```csharp
builder.Services.AddSmartInboxClient(o =>
{
    o.BaseUrl = new Uri("https://inbox.example.com");
    o.ApiKey  = builder.Configuration["SmartInbox:ApiKey"];
});

// Anywhere (inject ISmartInboxClient):
var doc    = await inbox.Documents.UploadAsync(File.OpenRead("letter.pdf"), "letter.pdf", ct);
var detail = await inbox.Documents.WaitForAnalysisAsync(doc.Id, TimeSpan.FromMinutes(2), ct);
if (detail.Analysis is { Confidence: >= 0.8, SuggestedDepartmentId: int dept })
    await inbox.Documents.RouteAsync(doc.Id, dept, note: "Auto-routed by ERP", ct);
```

No DI? `new SmartInboxClient(new SmartInboxClientOptions { BaseUrl = ..., ApiKey = ... })`.

## What's in the client

| Area | Methods |
|---|---|
| `Documents` | `UploadAsync`, `UploadTextAsync`, `GetAsync`, `ListAsync`, `ListAllAsync` (`IAsyncEnumerable`, pages for you), `DownloadAsync`, `ReanalyzeAsync`, `RouteAsync`, `AcknowledgeAsync`, `CloseAsync`, `ReopenAsync`, `WaitForAnalysisAsync` |
| `Departments` | `ListAsync`, `GetWorkloadAsync` |
| `Stats` | `GetAsync` |

**Permissions:** the key acts as its role. A Department key only sees its department's documents (others are `404`), and desk-only calls (upload, route, stats) return `403`.

**Errors:** non-success responses throw `SmartInboxApiException` (`StatusCode`, `Title`, `Detail`, `Errors` for validation), or the more specific `SmartInboxNotFoundException` (404) and `SmartInboxConflictException` (409, e.g. routing a document that's still being analyzed).

**Retries:** with `AddSmartInboxClient`, reads (`GET`/`PUT`/`DELETE`) are retried on 5xx, 408, 429 and network errors with exponential backoff. Actions (`RouteAsync`, `CloseAsync`, uploads, ...) are **never** retried automatically, so they can't happen twice by accident.

## Webhooks

Create an endpoint in **Administration → Webhooks** and copy its signing secret (`whsec_...`).

```csharp
// dotnet add package SmartInbox.Client.AspNetCore
app.MapSmartInboxWebhooks("/hooks/smart-inbox", builder.Configuration["SmartInbox:WebhookSecret"]!,
    async (SmartInboxEvent evt, CancellationToken ct) =>
    {
        switch (evt)
        {
            case DocumentRoutedEvent routed:
                Console.WriteLine($"Routed {routed.Data.DocumentId} to {routed.Data.DepartmentId}");
                break;
            case DeadlineApproachingEvent due:
                Console.WriteLine($"{due.Data.DocumentId} is due {due.Data.Deadline}");
                break;
        }
    });
```

It verifies the signature and timestamp (answers `400` if either is wrong) and calls your handler. If the handler throws, the answer is `500` and Smart Inbox retries later.

Outside ASP.NET Core, pass the **raw** body:

```csharp
var evt = WebhookVerifier.Verify(rawBody, request.Headers["SmartInbox-Signature"], secret); // throws WebhookVerificationException
```

Things to know:
- **At-least-once:** an event can arrive more than once. De-duplicate by `evt.Id`.
- **No ordering guarantee:** order by `evt.CreatedAt`, and treat the API as the source of truth.
- **Thin payloads:** events carry ids and key fields only. Call `Documents.GetAsync(evt.Data.DocumentId)` for the summary, text and history.
- **Unknown types** arrive as `UnknownEvent`, so a newer server never breaks an older receiver.

Full event catalog and signature details: [docs/WEBHOOKS.md](../../docs/WEBHOOKS.md).

## Samples

In [`sdk/samples`](../samples): `ConsoleQuickstart` (upload, wait, print the analysis), `WebhookReceiver` (logs every event) and `AutoRouter` (routes confident suggestions, leaves the rest for people).

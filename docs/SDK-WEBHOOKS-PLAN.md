# 🔌 Smart Inbox SDK + Webhooks — Project Plan

> Open Smart Inbox to other systems: **API keys** so code can call it, **signed webhooks** so it can call code, and a **typed .NET SDK** that makes both easy. This is the foundation for the MCP server, the correspondence agent, and every future connector.

**Duration:** ~1 week (after Step 0) · **Repo:** same repo as Smart Inbox (`sdk/` folder), SDK published to NuGet

---

## Table of Contents
0. [Step 0 — Ship Smart Inbox first](#step-0--ship-smart-inbox-first)
1. [Goals & Non-Goals](#1-goals--non-goals)
2. [Architecture](#2-architecture)
3. [Part A — API Keys](#part-a--api-keys)
4. [Part B — Webhooks](#part-b--webhooks)
5. [Part C — .NET SDK](#part-c--net-sdk)
6. [Testing](#6-testing)
7. [Timeline](#7-timeline)
8. [Definition of Done](#8-definition-of-done)
9. [LinkedIn](#9-linkedin)

---

## Step 0 — Ship Smart Inbox first

Do this before writing any new code.

- [ ] Move the machine-specific connection string out of `src/SmartInbox.Api/appsettings.json` (into `appsettings.Development.json` / user-secrets); default to `localhost`
- [ ] First commit + push to GitHub
- [ ] Record the hero GIF (upload → analysis → route → department closes) and a 60–90 s demo video
- [ ] Add the GIF to the top of `README.md`
- [ ] Publish the launch post on LinkedIn (lead with the evals: 90% routing accuracy, 100% safe routing)

---

## 1. Goals & Non-Goals

### Goals
- External systems can authenticate **without a browser cookie** (API keys)
- External systems are **notified in real time** when documents change (webhooks)
- Webhooks are **secure** (signed, replay-protected, SSRF-safe) and **reliable** (outbox, retries, redelivery)
- A **.NET SDK** that wraps the API and verifies webhooks in a few lines
- Everything visible and manageable in the existing `/admin` UI

### Non-Goals (later)
- TypeScript SDK
- OAuth client-credentials flow (API keys are enough for v1)
- Webhook payload transformations / filtering by expression
- Rate limiting per key (log usage now, enforce later)

---

## 2. Architecture

```
 External system                     Smart Inbox
 ───────────────                     ───────────────────────────────────────────────
                                     ┌──────────────────────────────────────────────┐
  SDK ── Bearer sik_... ───────────▶ │ ApiKeyAuthHandler → same roles & policies    │
                                     │                                              │
                                     │ DocumentService / DocumentProcessor          │
                                     │   └─ IWebhookPublisher.Publish(event)        │
                                     │        → WebhookDelivery rows                │
                                     │          (same transaction = outbox)         │
                                     │                                              │
  Receiver ◀── POST + signature ──── │ WebhookDispatcher (BackgroundService)        │
  (SDK verifies)                     │   retries · backoff · auto-disable           │
                                     └──────────────────────────────────────────────┘
```

**Key decision:** webhook deliveries are written **in the same `SaveChangesAsync`** as the change that caused them, exactly like the existing email `Notification` outbox. A crash can never produce a routed document without its event, or an event for a change that was rolled back.

---

## Part A — API Keys

Today the API only accepts the auth cookie. The SDK, MCP server, and agent need a non-browser credential.

### Model

```
ApiKey
  Id, Name ("ERP integration"), Prefix ("sik_a1b2c3d4"),
  Hash (SHA-256 of the full key), Role (Admin | Clerk | Department),
  DepartmentId?, CreatedBy, CreatedAt, ExpiresAt?, LastUsedAt?, RevokedAt?
```

### Rules
- Format: `sik_` + 8-char public prefix + `_` + 32 random bytes (base64url). Example: `sik_a1b2c3d4_Xy...`
- **Shown once** at creation; only the SHA-256 hash is stored
- Sent as `Authorization: Bearer sik_...`
- A key **acts as a role**, so existing policies (`Member`, `Desk`, `Admin`) and department restrictions apply with no changes to controllers
- `LastUsedAt` updated at most once per minute (avoid a DB write on every request)
- Revoked / expired keys → `401`

### Implementation
- `ApiKeyAuthenticationHandler : AuthenticationHandler<...>` registered as a second scheme
- Default policy accepts **either** cookie or API key (`AddPolicyScheme` that forwards by the presence of `Authorization: Bearer sik_`)
- `ICurrentUser.Name` = `"API key: {Name}"` so the timeline shows who acted
- Admin endpoints: `GET/POST /api/api-keys`, `DELETE /api/api-keys/{id}` (revoke)
- Admin UI tab: list, create (copy-once dialog), revoke

---

## Part B — Webhooks

### Event Catalog (v1)

| Event | Fired when | Source |
|---|---|---|
| `document.received` | Upload saved | `DocumentService.UploadAsync` |
| `document.analyzed` | Analysis finished (Analyzed or NeedsReview) | `DocumentProcessor` |
| `document.analysis_failed` | Analysis failed | `DocumentProcessor` |
| `document.routed` | Clerk routes / re-routes | `DocumentService.RouteAsync` |
| `document.acknowledged` | Department acknowledges | `SetHandlingAsync` |
| `document.closed` | Department closes | `SetHandlingAsync` |
| `document.reopened` | Department reopens | `SetHandlingAsync` |
| `deadline.approaching` | Open document, deadline in ≤ 2 days (once per document per deadline) | `DeadlineScanner` (hourly) |
| `webhook.ping` | Admin clicks "Send test" | Admin UI |

### Payload Envelope

```json
{
  "id": "evt_01JB...",
  "type": "document.routed",
  "createdAt": "2026-11-02T09:14:00Z",
  "apiVersion": "2026-11-01",
  "data": {
    "documentId": "8f3c...",
    "status": "Routed",
    "departmentId": 2,
    "previousDepartmentId": null,
    "priority": "High",
    "category": "Legal",
    "actor": "Sara (Correspondence Desk)",
    "url": "https://inbox.example.com/documents/8f3c..."
  }
}
```

- **Thin payloads:** IDs + key fields only, no summaries or extracted text. Receivers call the API (with their key) for full details. This keeps sensitive content out of third-party logs and respects permissions.
- `apiVersion` lets the payload evolve without breaking receivers.

### Model

```
WebhookEndpoint
  Id, Url, Description, Secret (encrypted with ASP.NET Data Protection),
  Events (string[], or "*"), DepartmentId? (only events for this department),
  Enabled, ConsecutiveFailures, DisabledReason?, CreatedBy, CreatedAt

WebhookDelivery
  Id, EndpointId, EventId, EventType, Payload (json),
  Status (Pending | Delivered | Failed), Attempts, NextAttemptAt,
  LastStatusCode?, LastError?, LastResponseSnippet? (≤ 1 KB), DurationMs?,
  CreatedAt, DeliveredAt?
```

### Signing (Stripe-style)

Headers on every request:

```
SmartInbox-Event-Id:  evt_01JB...
SmartInbox-Event:     document.routed
SmartInbox-Signature: t=1730538840,v1=5f2b...e9
```

- `v1 = hex(HMAC-SHA256(secret, "{t}.{rawBody}"))`
- Receivers must reject if `|now - t| > 5 minutes` (replay protection) and compare with constant-time equality
- Secret format `whsec_...`, shown once, can be **rotated**: during rotation both old and new signatures are sent (`v1=old,v1=new`) for 24 h

### Delivery Rules
- Worker polls every 5 s, batch of 50, max 4 concurrent per endpoint
- **No ordering guarantee:** concurrent deliveries and retries mean events can arrive out of order. Receivers order by `createdAt` and treat the API as the source of truth (thin payloads make this natural)
- Success = any `2xx` within **10 s** timeout
- Retry backoff: 1 min → 5 min → 30 min → 2 h → 6 h → 12 h (6 retries, ~21 h total)
- After **all retries fail** → delivery `Failed`, can be redelivered manually
- After **20 consecutive failed attempts** (any delivery, counted per HTTP attempt, reset by any `2xx`) → endpoint auto-disabled + email to admins. Counting attempts, not exhausted deliveries, means a dead endpoint is disabled within hours, not days
- Event `id` is stable across retries so receivers can de-duplicate (at-least-once delivery)

### Security
- **SSRF protection:** resolve the host before each request and block loopback, private (10/8, 172.16/12, 192.168/16), link-local (169.254/16, incl. cloud metadata), and IPv6 equivalents. Allow-list override only via config for development (`Webhooks:AllowPrivateNetworks=true`)
- HTTPS required outside Development
- No redirects followed
- Response body truncated to 1 KB before storing

### Admin UI (`/admin` → Webhooks tab)
- Endpoint list: URL, events, status, last delivery, success rate (7 days)
- Create / edit / disable / delete; rotate secret
- **Send test** (`webhook.ping`)
- Delivery log per endpoint with status code, duration, payload, response snippet, and **Redeliver** button

### Implementation Notes
- `IWebhookPublisher` (Application) → `Publish(eventType, data, departmentIds)` adds one `WebhookDelivery` per matching endpoint to the `DbContext`; the caller's existing `SaveChangesAsync` commits it
- Call it next to the existing `IInboxNotifier` calls (same places, same `Audience`)
- `WebhookDispatcher : BackgroundService` in Infrastructure, `IHttpClientFactory` named client `"webhooks"` with a custom `SocketsHttpHandler.ConnectCallback` for the SSRF check
- `DeadlineScanner : BackgroundService` runs hourly, fires `deadline.approaching` once per document (flag `DeadlineNotifiedAt` on the document; cleared when the deadline changes or the document is reopened, so it can fire again)

---

## Part C — .NET SDK

### Package

`SmartInbox.Client` (net8.0) on NuGet, source in `sdk/dotnet/`.

### Usage

```csharp
// Program.cs
builder.Services.AddSmartInboxClient(o =>
{
    o.BaseUrl = new Uri("https://inbox.example.com");
    o.ApiKey  = builder.Configuration["SmartInbox:ApiKey"];
});

// Anywhere
public class ErpSync(ISmartInboxClient inbox)
{
    public async Task Run(CancellationToken ct)
    {
        var doc = await inbox.Documents.UploadAsync(File.OpenRead("letter.pdf"), "letter.pdf", ct);
        var detail = await inbox.Documents.WaitForAnalysisAsync(doc.Id, TimeSpan.FromMinutes(2), ct);

        if (detail.Analysis!.Confidence >= 0.8)
            await inbox.Documents.RouteAsync(doc.Id, detail.Analysis.SuggestedDepartmentId!.Value, note: "Auto-routed by ERP", ct);

        await foreach (var d in inbox.Documents.ListAllAsync(new() { Status = DocumentStatus.NeedsReview }, ct))
            Console.WriteLine($"{d.FileName} → needs review");
    }
}
```

### Webhook Receiver Helper

```csharp
app.MapSmartInboxWebhooks("/hooks/smart-inbox", builder.Configuration["SmartInbox:WebhookSecret"]!,
    async (SmartInboxEvent evt, CancellationToken ct) =>
    {
        switch (evt)
        {
            case DocumentRoutedEvent routed:
                Console.WriteLine($"Routed {routed.Data.DocumentId} to {routed.Data.DepartmentId}");
                break;
            case DeadlineApproachingEvent deadline:
                // ...
                break;
        }
    });
```

- Verifies signature + timestamp, returns `400` on failure, `200` on success
- Also usable without ASP.NET: `WebhookVerifier.Verify(rawBody, signatureHeader, secret)` → `SmartInboxEvent`

### Surface

| Area | Methods |
|---|---|
| `Documents` | `UploadAsync`, `UploadTextAsync`, `GetAsync`, `ListAsync`, `ListAllAsync` (`IAsyncEnumerable`), `DownloadAsync`, `ReanalyzeAsync`, `RouteAsync`, `AcknowledgeAsync`, `CloseAsync`, `ReopenAsync`, `WaitForAnalysisAsync` |
| `Departments` | `ListAsync`, `GetWorkloadAsync` |
| `Stats` | `GetAsync` |
| `Webhooks` | `WebhookVerifier`, typed event records, `MapSmartInboxWebhooks` |

### Design Decisions
- **Hand-written thin client**, not generated: better naming, `IAsyncEnumerable` paging, typed events. A contract test checks it against the API's OpenAPI document so it can't drift
- Errors: API `ProblemDetails` → `SmartInboxApiException` (`StatusCode`, `Title`, `Detail`, `Errors`), plus `SmartInboxNotFoundException`, `SmartInboxConflictException`
- Resilience via `Microsoft.Extensions.Http.Resilience` (retry on 5xx/429 for idempotent calls only, **never** retry `RouteAsync`/`CloseAsync` automatically)
- No dependency on Smart Inbox server projects: the SDK has its own DTOs
- SemVer; `CHANGELOG.md`; XML docs on every public member

### Samples (`sdk/samples/`)
- `ConsoleQuickstart`: upload a sample letter, wait, print the analysis
- `WebhookReceiver`: minimal API that logs every event
- `AutoRouter`: routes high-confidence documents automatically, leaves the rest for humans

---

## 6. Testing

| Layer | Tests |
|---|---|
| API keys | Hash/verify, revoked/expired → 401, department key can't see other departments, timeline actor name |
| Webhook publishing | Each event fires from the right place; delivery row in the same transaction; department filter; `*` subscription |
| Signing | Known-vector test; timestamp tolerance; constant-time compare; rotation (two signatures) |
| Dispatcher | 2xx → Delivered; 500/timeout → retry with backoff; max retries → Failed; 20 failures → endpoint disabled; redirects not followed |
| SSRF | Loopback, private, link-local, IPv6, DNS that resolves to private IP → all blocked |
| SDK | Against `WebApplicationFactory<Program>`: full upload → analyze (Rules provider) → route flow; `ListAllAsync` paging; `ProblemDetails` mapping |
| Contract | SDK routes and DTOs match `swagger.json` |
| End-to-end | `WebhookReceiver` sample in Docker Compose receives a real `document.routed` |

---

## 7. Timeline

| Day | Task | Done |
|---|---|---|
| 1 | API keys: entity, migration, auth handler, policy scheme, admin endpoints + UI tab | ☑ |
| 2 | Webhooks: entities, migration, `IWebhookPublisher`, wire into all event sources | ☑ |
| 3 | `WebhookDispatcher`: signing, retries, auto-disable, SSRF guard; `DeadlineScanner` | ☑ |
| 4 | Admin UI: endpoints, test ping, delivery log, redeliver, rotate secret | ☑ |
| 5 | SDK: client, resources, errors, paging, resilience | ☑ |
| 6 | SDK: webhook verifier + typed events + `MapSmartInboxWebhooks`; samples | ☑ |
| 7 | Tests, contract test, docs (`docs/WEBHOOKS.md`, SDK README), NuGet publish via GitHub Actions | ☑ |

### As built (differences from the plan above)

- **Two packages:** `SmartInbox.Client` (no ASP.NET dependency, usable from console apps) and `SmartInbox.Client.AspNetCore` (`MapSmartInboxWebhooks`).
- **Deadline de-duplication** uses a per-endpoint dedup key (`deadline:{doc}:{department}:{deadline}`) on the delivery row instead of a `DeadlineNotifiedAt` column, so a new deadline or re-routing fires again without extra state. The scan runs hourly inside `WebhookWorker`.
- **Disabled endpoints** don't queue new events; turning one back on resets its failure count.
- **API keys and webhooks are managed with the admin cookie only**, so a leaked admin key can't mint keys or read secrets.
- **Send test and Redeliver** send immediately and return the result to the UI.
- The demo receiver listens on **5090** (5080 is the API's dev port).

---

## 8. Definition of Done

- [x] All tests green (162 unit + 30 SDK/integration locally; CI runs both)
- [ ] `SmartInbox.Client` published on NuGet (`0.1.0`): packages build; push tag `sdk-v0.1.0` with the `NUGET_API_KEY` secret set
- [x] `docs/WEBHOOKS.md`: event catalog, payload examples, signature verification in C#, Node and Python
- [x] SDK README with a 10-line quickstart
- [x] Main README updated: new "Integrations" section + diagram
- [x] Demo (verified with the samples against SQL Server): upload in the UI → `WebhookReceiver` console prints the event within seconds

---

## 9. LinkedIn

**Post angle:** *"Making a system integration-ready: signed webhooks, API keys, and a typed SDK."*

Talking points:
- Why webhooks go through an **outbox** (no lost or phantom events)
- Why payloads are **thin** (security + permissions)
- The **SSRF** trap most webhook implementations miss
- 10-line SDK quickstart screenshot
- Teaser: *"Next: letting Claude operate the inbox through an MCP server."*

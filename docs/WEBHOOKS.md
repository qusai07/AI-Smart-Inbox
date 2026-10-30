# Webhooks

Smart Inbox sends an HTTP `POST` to your URL when something happens to a document, so other systems can react in real time instead of polling.

- [Setting up an endpoint](#setting-up-an-endpoint)
- [Event catalog](#event-catalog)
- [Payload](#payload)
- [Verifying signatures](#verifying-signatures) (C#, Node, Python)
- [Delivery, retries and ordering](#delivery-retries-and-ordering)
- [Security](#security)
- [Troubleshooting](#troubleshooting)

## Setting up an endpoint

1. Sign in as an admin and open **Administration → Webhooks → Add endpoint**.
2. Enter the URL (HTTPS in production), optionally a description, the events you want (or all), and optionally one department (only events about documents routed to, or re-routed away from, that department).
3. Copy the **signing secret** (`whsec_...`). It is shown once.
4. Click **Send test**: your endpoint receives a `webhook.ping` and the result shows immediately.

Every delivery appears in the endpoint's **Deliveries** log with the status code, duration, payload and the first 1 KB of your response. Any delivery can be sent again with **Redeliver**.

## Event catalog

| Event | Fired when |
|---|---|
| `document.received` | A document was uploaded (file or pasted text) |
| `document.analyzed` | AI analysis finished; `status` is `Analyzed` (confident) or `NeedsReview`, or `Routed` on re-analysis |
| `document.analysis_failed` | AI analysis failed |
| `document.routed` | The desk routed or re-routed the document (`previousDepartmentId` is set on re-routing) |
| `document.acknowledged` | The department acknowledged it |
| `document.closed` | The department closed it |
| `document.reopened` | The department reopened it |
| `deadline.approaching` | An open, routed document's deadline is today or within the next 2 days (`Webhooks:DeadlineWarningDays`). Once per document, department and deadline: it fires again after re-routing or a new deadline |
| `draft.ready` | A reply draft has been prepared and waits for a person. `data`: `draftId`, `documentId`, `version`, `status`, `templateKey`, `departmentId`, `actor`, `url` — never the letter text |
| `draft.approved` | A person approved a reply (same `data`; fetch the text with `GET /api/drafts/{draftId}`) |
| `register.issued` | An outgoing number was issued — on approving a reply, or by hand. `data`: `outgoingLetterId`, `number`, `departmentId`, `documentId`, `replyDraftId`, `issuedBy`, `url` |
| `webhook.ping` | An admin clicked **Send test** (not subscribable) |

Unrouted documents (`received`, `analyzed`, `analysis_failed` before routing) only go to endpoints that are not limited to a department.

## Payload

```http
POST /hooks/smart-inbox HTTP/1.1
Content-Type: application/json; charset=utf-8
User-Agent: SmartInbox-Webhooks/1.0
SmartInbox-Event-Id: evt_6f1c2d0b4e9a4c8f9b1d2e3f4a5b6c7d
SmartInbox-Event: document.routed
SmartInbox-Signature: t=1793611440,v1=5f2b...e9
```

```json
{
  "id": "evt_6f1c2d0b4e9a4c8f9b1d2e3f4a5b6c7d",
  "type": "document.routed",
  "createdAt": "2026-11-02T09:14:00Z",
  "apiVersion": "2026-11-01",
  "data": {
    "documentId": "8f3c6a1e-4b2d-4c8e-9f1a-2b3c4d5e6f70",
    "status": "Routed",
    "departmentId": 2,
    "previousDepartmentId": null,
    "suggestedDepartmentId": 2,
    "priority": "High",
    "category": "Legal",
    "confidence": 0.91,
    "handling": "New",
    "deadline": "2026-11-05",
    "actor": "Sara (Correspondence Desk)",
    "url": "https://inbox.example.com/documents/8f3c6a1e-4b2d-4c8e-9f1a-2b3c4d5e6f70"
  }
}
```

| Field | |
|---|---|
| `id` | Event id. **Stable across retries and redeliveries**: use it to de-duplicate |
| `type` | One of the catalog events |
| `createdAt` | When the change happened (UTC) |
| `apiVersion` | Payload version. New fields can appear without a new version; removing or renaming one gets a new version |
| `data.departmentId` | Department it is routed to (null before routing) |
| `data.suggestedDepartmentId` / `confidence` / `priority` / `category` | The AI analysis, once there is one |
| `data.handling` | `New`, `Acknowledged` or `Closed` once routed |
| `data.actor` | A person's name, `API key: <name>`, the AI model, `Mailbox: <address>`, or `system` |
| `data.source` | How the document came in: `Upload`, `Api` or `Email` |

Payloads are **thin**: ids and key fields only, never summaries, extracted text, notes or comments. To get details, call `GET /api/documents/{documentId}` with an API key; this keeps document content out of third-party logs and applies the key's permissions.

`webhook.ping` carries `{ "message", "endpointId", "sentBy" }` in `data`.

## Verifying signatures

`SmartInbox-Signature` is `t=<unix seconds>,v1=<signature>`, where

```
signature = hex( HMAC-SHA256( key = secret, message = "{t}.{raw request body}" ) )
```

To verify:

1. Use the **raw body bytes** exactly as received. Parsing and re-serializing the JSON changes it and the signature won't match.
2. Reject if `|now - t| > 5 minutes` (replay protection).
3. Compute the signature and compare it with **each** `v1` using a constant-time comparison. Accept if any matches: while a secret is being rotated, there are two `v1` values for 24 hours (new and old secret).

### C#

With the SDK ([`sdk/dotnet`](../sdk/dotnet)):

```csharp
// ASP.NET Core: verified endpoint in one line (package SmartInbox.Client.AspNetCore)
app.MapSmartInboxWebhooks("/hooks/smart-inbox", secret, async (SmartInboxEvent evt, CancellationToken ct) =>
{
    if (evt is DocumentRoutedEvent routed) await Handle(routed.Data.DocumentId, ct);
});

// Anywhere else (package SmartInbox.Client)
SmartInboxEvent evt = WebhookVerifier.Verify(rawBody, signatureHeader, secret); // throws WebhookVerificationException
```

### Node.js (21+)

```js
const crypto = require('node:crypto')

function verifySmartInbox(rawBody, header, secret, toleranceSeconds = 300) {
  const parts = Object.groupBy(header.split(','), (p) => p.split('=')[0])
  const t = Number(parts.t?.[0]?.slice(2))
  const signatures = (parts.v1 ?? []).map((p) => p.slice(3))
  if (!t || signatures.length === 0) throw new Error('Malformed signature header')
  if (Math.abs(Date.now() / 1000 - t) > toleranceSeconds) throw new Error('Timestamp outside tolerance')

  const expected = crypto.createHmac('sha256', secret).update(`${t}.${rawBody}`).digest()
  const ok = signatures.some((s) => {
    const candidate = Buffer.from(s, 'hex')
    return candidate.length === expected.length && crypto.timingSafeEqual(candidate, expected)
  })
  if (!ok) throw new Error('Signature mismatch')
  return JSON.parse(rawBody)
}

// Express: keep the raw body
app.post('/hooks/smart-inbox', express.raw({ type: 'application/json' }), (req, res) => {
  try {
    const event = verifySmartInbox(req.body.toString('utf8'), req.get('SmartInbox-Signature'), process.env.WEBHOOK_SECRET)
    console.log(event.type, event.data.documentId)
    res.sendStatus(200)
  } catch {
    res.sendStatus(400)
  }
})
```

### Python

```python
import hashlib, hmac, json, time

def verify_smart_inbox(raw_body: bytes, header: str, secret: str, tolerance: int = 300) -> dict:
    pairs = [p.split("=", 1) for p in header.split(",") if "=" in p]
    t = next((int(v) for k, v in pairs if k == "t"), None)
    signatures = [v for k, v in pairs if k == "v1"]
    if t is None or not signatures:
        raise ValueError("Malformed signature header")
    if abs(time.time() - t) > tolerance:
        raise ValueError("Timestamp outside tolerance")

    expected = hmac.new(secret.encode(), f"{t}.".encode() + raw_body, hashlib.sha256).hexdigest()
    if not any(hmac.compare_digest(expected, s) for s in signatures):
        raise ValueError("Signature mismatch")
    return json.loads(raw_body)

# Flask
@app.post("/hooks/smart-inbox")
def smart_inbox():
    try:
        event = verify_smart_inbox(request.get_data(), request.headers.get("SmartInbox-Signature", ""), os.environ["WEBHOOK_SECRET"])
    except ValueError:
        return "", 400
    print(event["type"], event["data"].get("documentId"))
    return "", 200
```

**Test vector:** secret `whsec_test`, `t = 1730538840`, body `{"id":"evt_1"}` → `v1 = 75bae9ab5519a2aa8092ccf0216135045ae428b883621dcd44435d9a1c71632e`.

## Delivery, retries and ordering

- **Outbox:** a delivery row is written in the **same database transaction** as the change that caused it. A routed document always has its `document.routed` event, and a change that was rolled back never produces one.
- **Success** is any `2xx` within **10 seconds**. Respond fast and do slow work in the background.
- **Retries** after a failure: 1 min, 5 min, 30 min, 2 h, 6 h, 12 h (7 attempts over ~21 hours). After that the delivery is `Failed` and can be redelivered from the admin page.
- **At-least-once:** the same event can arrive more than once (a retry after a timeout, a manual redelivery). De-duplicate by `id`.
- **No ordering guarantee:** up to 4 deliveries per endpoint run in parallel, and retries arrive later. Order by `createdAt`, and use the API as the source of truth for current state.
- **Auto-disable:** after **20 failed attempts in a row** (across all deliveries; any success resets the count) the endpoint is switched off and admins are emailed. While it is off, new events are not queued. Fix the receiver, use **Send test**, then turn it back on; redeliver anything you missed from the log.

## Security

- **Signed:** HMAC-SHA256 with a per-endpoint secret, plus a timestamp for replay protection. Secrets are encrypted at rest (ASP.NET Data Protection; keys in `DataProtection:KeysPath`, which must persist across restarts).
- **Rotation:** **Rotate secret** creates a new secret; for 24 hours both secrets sign every request, so receivers can switch without dropping events.
- **SSRF protection:** before each connection the host is resolved and the request is refused if **any** address is loopback, private (10/8, 172.16/12, 192.168/16), carrier-grade NAT (100.64/10), link-local including cloud metadata (169.254/16), multicast/reserved, or the IPv6 equivalents (`::1`, `fc00::/7`, `fe80::/10`, IPv4-mapped and NAT64 forms). Because the check runs on the resolved address at connect time, DNS names pointing inside (and DNS rebinding) are caught. No proxy is used, so the check can't be bypassed through one.
- **No redirects** are followed (a `3xx` is a failure), so a public URL can't bounce the request to an internal one.
- **HTTPS required** outside Development. For local demos, `Webhooks:AllowHttp` and `Webhooks:AllowPrivateNetworks` can be set to `true` (Development and the Docker demo do this).
- Only the first **1 KB** of a response is stored.

## Troubleshooting

| In the delivery log | Meaning |
|---|---|
| `Blocked: host resolves to a non-public address` | The URL points inside the network. Use a public URL, or enable `Webhooks:AllowPrivateNetworks` for local testing |
| `HTTP 3xx: redirects are not followed` | Use the final URL (often `http://` → `https://` or a missing trailing slash) |
| `Timed out after 10 s` | Respond `200` first, then process asynchronously |
| `HTTP 400` from your receiver | Usually signature verification: check the secret, and that you verify the raw body |
| `The signing secret can't be decrypted` | The Data Protection keys were lost (e.g. a container without a volume). Rotate the secret |

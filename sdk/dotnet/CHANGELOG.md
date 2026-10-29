# Changelog

All notable changes to `SmartInbox.Client` and `SmartInbox.Client.AspNetCore`. The packages follow [Semantic Versioning](https://semver.org/).

## 0.2.0 — 2026-10-29

- `Documents.CommentAsync` and `Documents.AssignAsync`
- `Departments.MembersAsync`
- `DocumentListQuery.Mine`

## 0.1.0 — 2026-10-29

First release.

- `ISmartInboxClient` with `Documents`, `Departments` and `Stats`, authenticated with an API key (`sik_...`)
- `ListAllAsync` pages through results as an `IAsyncEnumerable`
- `WaitForAnalysisAsync` polls until AI analysis has finished
- ProblemDetails mapped to `SmartInboxApiException`, `SmartInboxNotFoundException`, `SmartInboxConflictException`
- `AddSmartInboxClient` with retries for reads only; actions are never retried automatically
- `WebhookVerifier`: HMAC-SHA256 signature check, 5-minute replay window, both signatures accepted during secret rotation
- Typed events for the v1 catalog (`DocumentRoutedEvent`, `DeadlineApproachingEvent`, ...) and `UnknownEvent` for newer types
- `SmartInbox.Client.AspNetCore`: `MapSmartInboxWebhooks`

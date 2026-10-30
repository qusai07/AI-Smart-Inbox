# Changelog

All notable changes to `SmartInbox.Client` and `SmartInbox.Client.AspNetCore`. The packages follow [Semantic Versioning](https://semver.org/).

## 0.6.0 — 2026-10-30

- `Documents.RelatedAsync` / `RemoveRelatedAsync`: letters a document belongs with (duplicates, follow-ups, replies to our letters, email threads), each with its reason
- `DocumentSummary.HasRelated` / `PossibleDuplicate`, `DocumentListQuery.Related`
- Webhook event `DocumentLinkedEvent` (`document.linked`)

## 0.5.0 — 2026-10-30

- `Register`: `ListAsync`, `ListAllAsync`, `GetAsync`, `IssueAsync`, `MarkSentAsync`, `CancelAsync`, `ExportCsvAsync` (outgoing register)
- `ReplyDraft.OutgoingNumber` / `OutgoingLetterId`: set when approving issued our number
- `DocumentDetail.Outgoing`, `Department.Code`
- Webhook event `RegisterIssuedEvent` (`register.issued`)

## 0.4.0 — 2026-10-30

- `DocumentSource` (Upload, Email, Api) on `DocumentSummary`, `DocumentDetail` and webhook `DocumentEventData`
- `DocumentDetail.SourceEmail`: sender, subject, date and covering message of documents that came by email
- `DocumentListQuery.Source` filter

## 0.3.0 — 2026-10-30

- `Drafts`: `RequestAsync`, `WaitForDraftAsync`, `GetAsync`, `ListAsync`, `UpdateAsync`, `ApproveAsync`, `RejectAsync`, `DownloadDocxAsync`, `ListTemplatesAsync`
- Webhook events `DraftReadyEvent` and `DraftApprovedEvent` (`draft.ready`, `draft.approved`)

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

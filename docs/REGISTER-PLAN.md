# 📒 Smart Inbox Outgoing Register — Project Plan

> Every letter that leaves the ministry gets an **official outgoing number** (e.g. `MDS/OUT/2026/0042`), recorded in a searchable **register**. Reply drafts get their number automatically **when they are approved**, so the one `[●]` every draft has today disappears. Free, no AI.

**Duration:** ~4 days · **Branch:** `dev` · **Cost:** none

---

## 1. Goals & Non-Goals

### Goals
- **Official numbers**: unique, sequential per year, never reused — even if a letter is cancelled
- **Automatic for replies**: approving a reply draft issues its number and writes it into both the English and Arabic letter (and the Word file)
- **Manual numbers too**: the desk can register any outgoing letter (not only replies), e.g. a letter written outside Smart Inbox
- **The register**: one page to search and filter everything sent (number, date, recipient, subject, department, linked incoming letter), export to CSV
- **Audit**: who issued, when; cancelled numbers stay visible with the reason

### Non-Goals (later)
- Sending the letter by email from Smart Inbox (v1 records *how* it was sent: email, post, hand delivery)
- Electronic signatures / stamps
- Per-department numbering formats beyond the configurable pattern below

---

## 2. Numbering

`Register:Format` (default `{Prefix}/OUT/{Year}/{Seq:0000}`), `Register:Prefix` (default `MDS`):

| Token | Example |
|---|---|
| `{Prefix}` | `MDS` |
| `{Year}` | `2026` (organisation's time zone) |
| `{Seq:0000}` | `0042` — restarts at 1 every year |
| `{Dept}` | `LEG` — the department's short code (new field), optional |

- **One sequence per year** by default; `Register:SequencePerDepartment=true` gives each department its own (use with `{Dept}`)
- **Concurrency-safe and gap-free**: a counter row per (year[, department]) updated in the same transaction as the register entry, plus a unique index on the number; a clash is retried
- Numbers are **never reused or edited**; a mistake is **cancelled** (with a reason) and a new number issued

---

## 3. How replies get their number

Today a reply draft shows `Our ref: [●]` and can't be approved until someone types a number. New flow:

1. The draft shows **`Our ref: (issued on approval)`** — a marker, not a gap, so it doesn't block approval
2. **Approve** issues the next number in the same transaction, replaces the marker in the English and Arabic text, and records the register entry (recipient, subjects, department, links to the incoming letter and the draft)
3. The Word file and the timeline show the number (*"Reply approved · MDS/OUT/2026/0042"*)

Drafts approved before this feature keep the number someone typed.

---

## 4. Data Model

```
OutgoingLetter
  Id, Number (unique), Year, Sequence, DepartmentId,
  Recipient, SubjectEn, SubjectAr,
  DocumentId? (the incoming letter it answers), ReplyDraftId?,
  IssuedAt, IssuedBy,
  Status (Issued | Sent | Cancelled), SentAt?, SentVia? (Email | Post | Hand | Other), SentNote?,
  CancelledAt?, CancelledBy?, CancelReason?

RegisterCounter
  Year, DepartmentId? (null = shared), Next   -- unique (Year, DepartmentId)

Department (+)
  Code (e.g. LEG, FIN, HR) — for {Dept}
```

---

## 5. Web UI

- **Register** page (desk + admin; department staff see their department's entries):
  - table: number, date, recipient, subject (EN/AR toggle), department, status, links to the incoming letter and the reply
  - search (number, recipient, subject), filters (year, department, status), **Export CSV**
  - **New outgoing letter** (manual): recipient, subjects, department, optional link to an incoming document → issues a number
  - per entry: **Mark as sent** (how, when, note) and **Cancel** (reason required)
- **Reply panel**: "Our ref will be issued on approval"; after approval the number is shown with a link to the register
- **Document page**: "Replied with MDS/OUT/2026/0042" in the routing box
- **Administration → Departments**: short code field
- **What's new**: one item

---

## 6. API, Webhooks, SDK, MCP

- `GET /api/register` (filters, paging), `GET /api/register/{id}`, `POST /api/register` (manual), `POST /api/register/{id}/sent`, `POST /api/register/{id}/cancel`, `GET /api/register/export.csv`
- Webhook `register.issued` (thin: id, number, department, document, draft)
- SDK 0.5.0 `Register` client; MCP read tool `search_register` (no issuing through MCP)

---

## 7. Testing

| Layer | Tests |
|---|---|
| Numbering | format tokens; yearly restart; per-department sequences; 50 parallel issues → 50 distinct consecutive numbers; never reused after cancel |
| Replies | marker doesn't block approval; approve issues the number into EN + AR + Word; approval and number in one transaction (a failure issues no number) |
| Register | manual entry, mark sent, cancel (reason required), permissions (department sees own), CSV export |
| API/SDK/MCP | end to end through the real API; contract tests |

---

## 8. Timeline

| Day | Task | Done |
|---|---|---|
| 1 | `OutgoingLetter`, `RegisterCounter`, department code + migration; `RegisterService` (numbering, issue, send, cancel); unit tests incl. concurrency | ☑ |
| 2 | Reply integration (marker, number on approval, Word, timeline); API + CSV; webhook | ☑ |
| 3 | Web UI: Register page, manual entry, sent/cancel, reply panel + document page changes, department codes, What's new | ☐ |
| 4 | SDK 0.5.0, MCP `search_register`; docs (`docs/REGISTER.md`), README | ☐ |

---

## 9. Definition of Done

- [ ] All tests green in CI
- [ ] Approving a reply issues its number into both languages and the Word file; no `[●]` left for our reference
- [ ] Register page: search, filter, export, manual entry, mark sent, cancel
- [ ] Parallel issuing never produces duplicates or gaps (tested)
- [ ] Docs + README

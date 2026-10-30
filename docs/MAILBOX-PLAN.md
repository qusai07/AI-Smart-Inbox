# 📬 Smart Inbox Mailbox Ingestion — Project Plan

> Correspondence arrives by email. Instead of someone saving attachments and uploading them, Smart Inbox reads a **shared mailbox** (e.g. `correspondence@ministry`) and takes in every new email and its letters automatically — then analysis, routing, reply drafting and webhooks work exactly as for an upload. **Free:** standard IMAP via MailKit (already used for sending email); no paid service.

**Duration:** ~4 days · **Repo:** same repo · **Cost:** none

---

## Table of Contents
1. [Goals & Non-Goals](#1-goals--non-goals)
2. [What becomes a document](#2-what-becomes-a-document)
3. [Architecture](#3-architecture)
4. [Data Model & Configuration](#4-data-model--configuration)
5. [Safety](#5-safety)
6. [Web UI](#6-web-ui)
7. [API, Webhooks, SDK, MCP](#7-api-webhooks-sdk-mcp)
8. [Testing & Demo](#8-testing--demo)
9. [Timeline](#9-timeline)
10. [Definition of Done](#10-definition-of-done)
11. [LinkedIn](#11-linkedin)

---

## 1. Goals & Non-Goals

### Goals
- Poll one shared mailbox over **IMAP** (Gmail, Outlook.com, most ministry mail servers; Microsoft 365 see below) and import new emails without anyone uploading
- **Never import twice** (restarts, several app instances, the same email in two folders), never lose one (failures are kept and retried)
- Keep the **original email** with every document it produced, so people can see who sent it and the covering message
- Admins can see that it works: last check, last error, recent imports, **Check now**, **Test connection**
- Works offline/free; off by default until configured

### Non-Goals (later)
- Sending the approved reply by email (with the outgoing register)
- **Microsoft 365 / Exchange Online with OAuth** (Microsoft has turned off password sign-in for IMAP there): v1 supports username + password / app password; OAuth (still free, needs an Entra app registration) is the first follow-up
- Several mailboxes (one per department) — v1 has one; the model allows more later
- Spam filtering beyond the basics below (the mail server already does this)

---

## 2. What becomes a document

| The email has… | Smart Inbox creates |
|---|---|
| No supported attachment (the email *is* the letter) | **One document**: the `.eml` itself (text extracted as today: from, to, date, subject, body) |
| One or more supported attachments (PDF, DOCX, JPG/PNG, EML) | **One document per attachment** — that's the actual letter, and that's what gets analysed. The email is kept as its **cover note** |
| Only unsupported attachments (e.g. `.xlsx`, `.zip`) | One document from the email body; the unsupported files are listed on the import record (not stored) |

Every document from email shows on its page: **"Received by email"** with sender, subject, date, the covering message, and a link to download the original `.eml`. Its timeline starts with *"Received by email from X"*, and `UploadedBy` is `Mailbox: correspondence@…`.

Supported types, size limits (20 MB per file) and content-type rules are the **same as uploads** (`UploadRules`), so nothing new can get in through email.

---

## 3. Architecture

```
 Mail server (IMAP)                     Smart Inbox
 ──────────────────                     ──────────────────────────────────────────────────────
                                        MailboxWorker (every Mailbox:PollSeconds)
  INBOX ◀── fetch new (UID > last) ───    └─ MailboxImporter.RunAsync
                                              1. IMailboxClient: list new messages (UID, Message-Id, size)
                                              2. skip known Message-Ids (InboundEmail table, unique)
                                              3. download, apply safety rules (§5)
                                              4. DocumentService.UploadAsync per letter  ← same path as the web upload
                                                 (analysis queue, webhooks document.received, timeline)
                                              5. InboundEmail row = Imported (+ document ids)
  INBOX ──── move to "Processed" ──────▶  6. mark done on the server (move, or flag \Seen + keyword)
```

- `IMailboxClient` (Application) / `ImapMailboxClient` (Infrastructure, MailKit) — the importer is tested against a fake client, no server needed
- **Exactly-once in practice:** the `InboundEmail` row (unique `MessageId`) is written in the same save as the documents; the server-side move happens after. A crash between the two means the email is seen again next time and skipped by its Message-Id
- Emails without a Message-Id get a stable one from a SHA-256 of From + Date + Subject + size
- Original `.eml` files go to the existing file storage

---

## 4. Data Model & Configuration

```
InboundEmail
  Id, MessageId (unique), Mailbox, Uid, From, Subject, SentAt, ReceivedAt,
  StoragePath (original .eml), Status (Imported | Skipped | Failed), Reason?,
  Attempts, DocumentIds (json), SkippedAttachments (json)

Document (+)
  Source (Upload | Email | Api), InboundEmailId?
```

| Setting | Default | |
|---|---|---|
| `Mailbox:Enabled` | `false` | Off until configured |
| `Mailbox:Host` / `Port` / `Security` | – / `993` / `SslOnConnect` | IMAP server |
| `Mailbox:Username` / `Password` | – | Use user-secrets / env vars; an **app password** for Gmail/Outlook.com |
| `Mailbox:Folder` | `INBOX` | Where new mail arrives |
| `Mailbox:ProcessedFolder` | `Processed` | Moved here after import (created if missing); empty = leave in place, flag only |
| `Mailbox:PollSeconds` | `60` | |
| `Mailbox:MaxPerCheck` | `25` | Large backlogs are worked through in batches |
| `Mailbox:AllowedSenders` | – (anyone) | Optional list of addresses or `@domain.gov` |
| `Mailbox:MaxAttempts` | `5` | After that the email is `Failed` and shown to admins |

Docker demo: the bundled **smtp4dev** test mail server also speaks IMAP, so `docker compose up` can show the whole flow: send an email → it appears in Smart Inbox (to verify on Day 1; fallback: GreenMail test container).

---

## 5. Safety

- **Same rules as uploads:** allowed types by extension, 20 MB per file, content type never trusted from the email
- **No double import:** unique Message-Id; reprocessing is skipped and logged
- **Skipped, not imported:** auto-replies and bounces (`Auto-Submitted`, `X-Autoreply`, delivery-status reports), emails from senders outside `AllowedSenders` (if set), emails with nothing usable — each kept as `Skipped` with the reason, visible to admins
- **Nothing is executed or opened** beyond text extraction (the existing extractors); HTML bodies are reduced to text
- **Credentials** only in configuration secrets, never in the database or logs; connection errors are logged without them
- **Prompt injection:** email text is analysed by the same engines and shown through MCP as untrusted, exactly like uploads
- **Failures don't block:** one bad email is retried up to `MaxAttempts`, then marked `Failed`; the rest continue

---

## 6. Web UI

- **Administration → Mailbox** (admin):
  - status: on/off, mailbox address, last check, next check, last error
  - **Test connection** (logs in, counts messages, changes nothing) and **Check now**
  - recent imports table: when, from, subject, documents created (links), status, reason; **Retry** for failed ones
- **Document page:** a "Received by email" box (sender, subject, date, covering message, **Download original email**)
- **Inbox:** a small ✉ icon on documents that came by email; filter "Source: email"
- **Integrations page:** a Mailbox card with live status
- **What's new:** one more item for this release

---

## 7. API, Webhooks, SDK, MCP

- `GET /api/mailbox/status`, `POST /api/mailbox/test`, `POST /api/mailbox/check`, `GET /api/mailbox/imports`, `POST /api/mailbox/imports/{id}/retry` (admin)
- `GET /api/documents/{id}/source-email` (download the original `.eml`; same visibility as the document)
- Documents gain `source` (`Upload`/`Email`/`Api`) in list and detail; filter `?source=Email`
- Webhook `document.received` gets `source` in its data (additive, no new version)
- SDK 0.4.0: `DocumentSummary.Source`, `DocumentListQuery.Source`; MCP `list_documents` accepts `source`

---

## 8. Testing & Demo

| Layer | Tests |
|---|---|
| Importer (fake mailbox) | plain email → 1 document; email + 2 PDFs → 2 documents with the email as cover; unsupported attachment skipped and listed; auto-reply skipped; sender not allowed skipped; same Message-Id twice → once; missing Message-Id → stable hash; crash after save, before move → no duplicate; failure → retried → `Failed` after max |
| IMAP client | against smtp4dev (or GreenMail) in Docker: fetch, move to Processed, folder created |
| End to end | email the 20 sample letters into the test mailbox → same analysis results as uploading them (`docs/EVALS-mailbox.md`) |
| Permissions | department users see only their documents' source email; mailbox admin endpoints are admin-only |

**Demo:** send the court notice as a PDF attachment to the test mailbox → within a minute it's in the inbox, analysed as Legal → route → reply draft → Word.

---

## 9. Timeline

| Day | Task | Done |
|---|---|---|
| 1 | `InboundEmail` + `Document.Source` + migration; `IMailboxClient`; `MailboxImporter` with the rules in §2 and §5; unit tests with a fake mailbox; check smtp4dev IMAP (moved to Day 2: Docker wasn't running) | ☑ |
| 2 | `ImapMailboxClient` (MailKit), `MailboxWorker`, move/flag, retries; admin API (status, test, check, imports, retry); Docker demo wiring; integration test against the test mail server | ☐ |
| 3 | Web UI: Administration → Mailbox, "Received by email" box + original download, inbox icon/filter, Integrations card, What's new item | ☐ |
| 4 | Webhook/SDK/MCP `source`; end-to-end eval with the 20 samples by email; `docs/MAILBOX.md`; README | ☐ |

---

## 10. Definition of Done

- [ ] All tests green in CI (no real mail server needed for unit tests)
- [ ] With the Docker demo, an email sent to the test mailbox appears in Smart Inbox analysed, with its original email attached
- [ ] No duplicates after restarts, re-runs or a crash between save and move (tested)
- [ ] Admin can see status, test the connection, check now, and retry failures
- [ ] 20 samples by email give the same analysis as uploads
- [ ] `docs/MAILBOX.md` (setup for Gmail / Outlook.com / own server, app passwords, troubleshooting) + README

---

## 11. LinkedIn

**Post angle:** *"From inbox to analysed, routed and drafted — without anyone touching an attachment."*

Talking points:
- Exactly-once import with plain IMAP: Message-Id as the idempotency key, save before move
- One email, three letters → three documents, one cover note
- Same safety rules for every way in (upload, API, email)
- Still free: MailKit + the same rule engine and templates

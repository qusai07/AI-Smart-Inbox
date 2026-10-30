# ✍️ Smart Inbox Correspondence Agent — Project Plan

> Draft the formal reply to a letter, in **English and Arabic**, in seconds — grounded in the letter and its analysis, never sent without a person editing and approving it. **Free by default:** human-written bilingual templates + the free local LLM (Ollama). No paid service is needed.

**Duration:** ~5 days · **Repo:** same repo · **Cost:** none (templates + Ollama run locally; Claude is an optional switch, off by default)

---

## Table of Contents
1. [Goals & Non-Goals](#1-goals--non-goals)
2. [Design Decisions](#2-design-decisions)
3. [Drafting Engines](#3-drafting-engines)
4. [Architecture](#4-architecture)
5. [Data Model & API](#5-data-model--api)
6. [Templates](#6-templates)
7. [Web UI](#7-web-ui)
8. [SDK, Webhooks & MCP](#8-sdk-webhooks--mcp)
9. [Safety](#9-safety)
10. [Evals](#10-evals)
11. [Timeline](#11-timeline)
12. [Definition of Done](#12-definition-of-done)
13. [Later: the autonomous agent](#13-later-the-autonomous-agent)
14. [LinkedIn](#14-linkedin)

---

## 1. Goals & Non-Goals

### Goals
- **"Draft reply"** on any routed document: a formal reply letter in English and Arabic, following government correspondence conventions
- **Works with zero cost:** every part runs on the free engines (templates, rules, local Ollama) and open-source libraries
- **Grounded:** filled from the letter's analysis (sender, reference, dates, subject, action items, deadline) and the handling department; never invents facts — anything unknown is a visible `[●]` placeholder plus a "missing information" item
- **Human in the loop:** drafts are editable, versioned, and need an explicit **Approve**; nothing is ever sent automatically
- **Export** the approved reply as a Word document (open-source `DocumentFormat.OpenXml`)
- Available in the web app, REST API + SDK, webhooks and the MCP server

### Non-Goals (later)
- Sending the reply (email/postal) and the official **outgoing register** with numbering
- Learning from edits — v1 only records the diff between draft and approved text
- A template editor in the UI (v1: templates are files in the repo)

---

## 2. Design Decisions

| Question | Decision | Why |
|---|---|---|
| How do we get good Arabic for free? | **Human-written bilingual templates** carry the letter (salutation, reference line, acknowledgement, closing, signature) | Template Arabic is always correct and formal; a small local model's Arabic is not reliable enough to be the whole letter |
| What does the AI do then? | The **free local LLM (Ollama)** writes only the *response paragraph* that answers the specific request, in English and Arabic, when it's available | That's the part templates can't know; if Ollama is off or unsure, the paragraph becomes `[●]` for a person to write |
| Agent loop or single step? | **Workflow:** code picks the template and fills the slots, then (optionally) one Ollama call for the paragraph | Predictable, fast, testable; no loop needed for one letter |
| Where does it run? | **Inside Smart Inbox** (Application service + Infrastructure engines), on the existing background queue | Reuses config, permissions, timeline, SignalR; nothing extra to host |
| Paid models? | **Not required.** `Drafting:Engine=Claude` exists as an optional switch for people who already have a key, off by default | Same rule as analysis: free by default |

---

## 3. Drafting Engines

Chosen by `Drafting:Engine`, mirroring `Ai:Provider`:

| Engine | Cost | What it does | When |
|---|---|---|---|
| **`Templates`** | Free, offline | Picks the template by category + intent, fills slots from the analysis; the response paragraph is `[●]` | Always works; no AI at all |
| **`Hybrid`** (default) | Free (local) | Templates + Ollama writes the response paragraph (EN + AR). Falls back to `Templates` if Ollama is down, slow (> 60 s) or returns invalid output | Default, like analysis |
| `Ollama` | Free (local) | Ollama writes the whole letter body inside the template frame | Experiments; quality measured in evals |
| `Claude` | Paid, optional | Same as Hybrid with Claude for the paragraph | Only if the owner sets a key and opts in |

**Ollama model:** the current `qwen2.5:3b` for English; the evals compare it with `qwen2.5:7b` (still free and local, needs ~8 GB RAM) for the Arabic paragraph. The configured model is `Drafting:OllamaModel`, separate from analysis. If Arabic quality from the local model isn't good enough, the Arabic paragraph stays `[●]` and only the English one is suggested — the evals decide (`Drafting:LocalArabic=true|false`).

Ollama is called with a JSON schema (`format`) so output is `{ paragraphEn, paragraphAr, missingInfo[], commitments[] }`, validated before use.

---

## 4. Architecture

```
 Web app / SDK / MCP                  Smart Inbox
 ───────────────────                  ───────────────────────────────────────────────────────
  "Draft reply" ── POST drafts ────▶  ReplyDraftService.RequestAsync   (permissions, Draft = Requested)
                                         └─ queue
                                      DraftWorker
                                        1. TemplateSelector   category + intent (+ requester's choice)
                                        2. SlotFiller         sender, ref, dates, subject, action items,
                                                              deadline, department, signatory
                                        3. IReplyDrafter      Hybrid: Ollama → response paragraph (EN/AR)
                                                              Templates: [●]
                                        4. Validator          placeholders, no unknown numbers/dates
                                      Draft = Ready · timeline · SignalR · webhook draft.ready
  person edits ── PUT draft ───────▶  new version
  Approve ─────────────────────────▶  Draft = Approved · timeline · webhook draft.approved
  Download ────────────────────────▶  DOCX (letterhead, ref, date, EN + AR, signature)
```

- `IReplyDrafter` (Application) with `TemplateReplyDrafter`, `HybridReplyDrafter` (Ollama) and optional `ClaudeReplyDrafter` (Infrastructure) — same shape as `IAiProvider`
- **Organisation and signatory** come from configuration and department settings, never from a model

---

## 5. Data Model & API

```
ReplyDraft
  Id, DocumentId, Version, Status (Requested | Generating | Ready | Approved | Rejected | Failed),
  TemplateKey, Engine ("Templates" | "Hybrid:qwen2.5:7b" | ...),
  Instructions? ("decline politely", "ask for the contract copy"),
  SubjectEn, BodyEn, SubjectAr, BodyAr,
  MissingInfo (json), Commitments (json),
  RequestedBy, CreatedAt, EditedBy?, EditedAt?, ApprovedBy?, ApprovedAt?, Error?

Department (+)
  SignatoryNameEn/Ar, SignatoryTitleEn/Ar

Drafting options (config)
  Enabled (true), Engine (Hybrid), OllamaModel, LocalArabic, OrganisationNameEn/Ar, LetterheadImage?
```

| Method | Route | Who | |
|---|---|---|---|
| `POST` | `/api/documents/{id}/drafts` | Desk, or staff of the assigned department | `{ templateKey?, instructions? }` → `202` + draft id |
| `GET` | `/api/documents/{id}/drafts` | Same | All versions |
| `GET` | `/api/drafts/{id}` | Same | One draft |
| `PUT` | `/api/drafts/{id}` | Same | Edit → new version (original kept) |
| `POST` | `/api/drafts/{id}/approve` · `/reject` | Desk, or the assigned department | Approve needs no `[●]` left |
| `GET` | `/api/drafts/{id}/docx` | Same | Word export |
| `GET` | `/api/reply-templates` | Member | Template catalog (for the "kind of reply" picker) |

Timeline: `DraftRequested`, `DraftReady`, `DraftFailed`, `DraftApproved`, `DraftRejected`.

---

## 6. Templates

Files in `templates/replies/` (versioned with the code), one per reply type, each with English and Arabic text and named slots:

| Template | Used for |
|---|---|
| `acknowledge-receipt` | Any letter: received, under review by {Department}, reference {OurRef} |
| `request-information` | Missing documents or details: lists `[●]` items |
| `accept-invitation` / `decline-invitation` | Invitations (date, venue from the analysis) |
| `complaint-acknowledgement` | Complaints: apology, investigation, expected response date |
| `invoice-received` | Invoices: received, forwarded to Finance, payment terms `[●]` |
| `legal-acknowledgement` | Court and legal notices: received, Legal Affairs appointed, deadline noted |
| `circular-acknowledgement` | Circulars: noted and distributed |
| `general-reply` | Fallback: frame + response paragraph |

- **Selection:** by the document's category (from analysis) with a sensible default; the user can pick another type in the UI
- **Slots:** `{SenderName}`, `{TheirReference}`, `{TheirDate}`, `{Subject}`, `{Deadline}`, `{ActionItems}`, `{Department}`, `{OurReference}`, `{Today}` (Gregorian + Hijri), `{Signatory}`; a slot without a value becomes `[●]`
- Arabic text is written and reviewed by a native speaker once; the code never machine-translates it

---

## 7. Web UI

On the document page, a **Reply** panel:
- **Draft reply** with a "Type of reply" picker (pre-selected) and an optional "What should the reply say?" box
- Side-by-side **English | العربية** editors (RTL for Arabic), subject + body
- `[●]` placeholders highlighted; **Missing information** and **Commitments** lists beside the editors
- **Approve** (disabled while placeholders remain), **Reject**, **Regenerate**, **Download Word**
- Badge showing what wrote it: "Template" or "Template + local AI"
- Version history with a simple diff

---

## 8. SDK, Webhooks & MCP

- **SDK 0.3.0:** `inbox.Drafts.RequestAsync(documentId, templateKey?, instructions?)`, `WaitForDraftAsync`, `GetAsync`, `ListAsync`, `UpdateAsync`, `ApproveAsync`, `RejectAsync`, `DownloadDocxAsync`, `ListTemplatesAsync`
- **Webhooks:** `draft.ready`, `draft.approved` (thin payload)
- **MCP** (`smartinbox-mcp` 0.2.0): `draft_reply(documentId, templateKey?, instructions?)` (write tool), `get_reply_drafts(documentId)`, `list_reply_templates` (read tools). **No approve tool** — approval stays a human action in the web app

---

## 9. Safety

- **Never auto-sent; approval is human-only** (UI/API; not exposed to MCP)
- **No invented facts:** every name, number and date in a draft must come from the analysis or the instructions; the validator flags any other number/date and the approver sees it; unknowns are `[●]` and block approval
- **Prompt injection (Hybrid/Ollama):** the letter is passed as delimited, untrusted data; the model writes one paragraph only and can't change the template, recipients or signature; output must match the JSON schema or it's discarded (fallback to `[●]`); the planted letters in `samples/mcp/` are eval cases
- **Permissions:** same as handling — desk, or staff of the assigned department; Department API keys only for their department
- **Privacy:** with the default engines nothing leaves the machine (templates and Ollama run locally)

---

## 10. Evals

`docs/EVALS-drafting.md`, run on the 20 sample letters + the 2 planted ones, for `Templates`, `Hybrid (qwen2.5:3b)` and `Hybrid (qwen2.5:7b)`:

| Check | How (all free) |
|---|---|
| Right template chosen | Expected reply type per sample in `samples/labels.json` |
| Slots filled correctly | Reference, sender, dates, deadline compared with the labels |
| No invented facts | Automatic: every number/date in the draft appears in the source or instructions |
| Paragraph answers the request | Rubric scored by a person (and, optionally, the local model as a rough judge) |
| Arabic quality of the local paragraph | Native-speaker review on 10 drafts → decides `LocalArabic` default |
| Injection resistance | Planted letters: paragraph must not obey them; template frame unchanged |
| Speed | Seconds per draft on a normal laptop |

---

## 11. Timeline

| Day | Task | Done |
|---|---|---|
| 1 | `ReplyDraft` entity + migration, department signatory fields, `ReplyDraftService`, permissions, timeline, API endpoints | ☑ |
| 2 | Bilingual template files + selector + slot filler; `TemplateReplyDrafter`; background worker; template catalog endpoint; unit tests | ☑ |
| 3 | Web UI: Reply panel (templates only) | ☑ |
| 4 | **Measure templates** on the 20 samples (right template? how much `[●]` left?) → `docs/EVALS-drafting.md` | ☐ |
| 5 | **Only if step 4 shows a gap:** `HybridReplyDrafter` (Ollama paragraph, 3b vs 7b, Arabic on/off); then Word export, webhooks `draft.*`, SDK 0.3.0, MCP tools | ☐ |

Order changed after review: templates first, measured, and the local model added only where it proves it helps.

---

## 12. Definition of Done

- [ ] All tests green in CI (no network or paid service needed)
- [ ] With **no AI running at all** (`Templates`), every sample letter gets a correct bilingual draft with `[●]` where a person must write
- [ ] With Ollama running (`Hybrid`), the response paragraph is filled for most samples; failures fall back cleanly
- [ ] Evals recorded: template accuracy, no invented facts, injection letters handled, local Arabic decision
- [ ] `draft_reply` works from Claude Code via MCP; approval impossible outside the web app
- [ ] `docs/DRAFTING.md` + README + roadmap ticked
- [ ] Demo: court notice → Draft reply → placeholders → edit → approve → Word file

---

## 13. Later: the autonomous agent

Free version, inside the app: a background job that, on `document.routed`, prepares a draft automatically for configured categories (e.g. acknowledge every complaint), so a draft is waiting when the department opens the item. Uses the same engines (templates + Ollama), never approves or sends. A tool-using agent loop (local model via Ollama tool calling) can come after, only if the evals show the workflow isn't enough.

---

## 14. LinkedIn

**Post angle:** *"Bilingual government replies in seconds — with zero AI bill: human-written templates for the parts that must be right, a free local model for the part that's different every time."*

Talking points:
- Why templates beat a small model for formal Arabic, and where the model still helps
- `[●]` placeholders instead of hallucinations; approval blocked until they're filled
- Everything runs on a laptop: rules, templates, Ollama — nothing leaves the building
- Workflow, not agent: the simplest thing that works

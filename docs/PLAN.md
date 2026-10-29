# 📥 AI Smart Inbox — Project Plan

> An AI-powered inbox for official correspondence. Upload a letter, PDF, image, or email — the AI reads it, summarizes it (Arabic & English), classifies it, extracts key data, and suggests which department it should be routed to. A human always makes the final call.

**Pitch:** *Turning hours of manual mail sorting into seconds — with a human always in the loop.*

---

## ✨ Features

### MVP (v1 — ~2 weeks)

| # | Feature | Details |
|---|---|---|
| 1 | **Document upload** | PDF, DOCX, images (JPG/PNG), `.eml`, or pasted text |
| 2 | **Text extraction** | Native PDFs via PdfPig; scanned PDFs & images via AI vision |
| 3 | **AI analysis** | Summary, category, priority, sender, date, reference number, deadlines, action items |
| 4 | **Routing suggestion** | Suggested department + confidence score + reasoning |
| 5 | **Human-in-the-loop** | User accepts or overrides; every decision is logged as feedback |
| 6 | **Bilingual (AR/EN)** | Documents in either language; summaries in both; full RTL UI support |
| 7 | **Inbox UI** | Filterable list with priority badges; detail view with original document side by side with AI insights |

### v2 (after first release)

- 💬 **Chat with document** — Q&A with quoted citations from the source
- ✍️ **Draft reply** — AI-generated formal reply draft
- 🔎 **Semantic search** — search by meaning, not just keywords
- 📊 **Dashboard** — volume by department/priority, AI routing accuracy over time
- ⚡ **Live status** — background processing with real-time SignalR updates
- 📧 **Email ingestion** — auto-pull from a mailbox via IMAP

---

## 🏗️ Architecture

```
┌─────────────┐      ┌──────────────────────────────┐      ┌──────────────┐
│  Vue 3 SPA  │─────▶│   ASP.NET Core 8 Web API     │─────▶│  SQL Server  │
│  (Vite+TS)  │◀─────│                              │      └──────────────┘
└─────────────┘ SignalR  ┌────────────────────────┐ │      ┌──────────────┐
                      │  │ Background Queue       │─┼─────▶│ File Storage │
                      │  │ (Channels / Hangfire)  │ │      └──────────────┘
                      │  └──────────┬─────────────┘ │
                      │             ▼               │      ┌──────────────┐
                      │  ┌────────────────────────┐ │─────▶│  Claude API  │
                      │  │ IAiProvider (pluggable)│ │      │ (or Azure    │
                      │  └────────────────────────┘ │      │  OpenAI)     │
                      └──────────────────────────────┘      └──────────────┘
```

### Processing Flow

```
Upload → Save file → Queue job → Extract text → AI analyze
       → Save results → SignalR notify → User reviews
       → Accept / Override → Log feedback
```

---

## 🧰 Tech Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 8, Clean Architecture, EF Core, FluentValidation |
| Frontend | Vue 3, Vite, TypeScript, Pinia, PrimeVue (or Vuetify), vue-i18n (AR/EN) |
| Database | SQL Server (or PostgreSQL for free hosting) |
| AI | Claude API — `claude-sonnet-5-5` for analysis, `claude-haiku-5-5` for fast classification — behind an `IAiProvider` interface so Azure OpenAI can be swapped in |
| PDF | UglyToad.PdfPig |
| Background jobs | `System.Threading.Channels` (MVP) → Hangfire (v2) |
| Realtime | SignalR |
| DevOps | Docker Compose, GitHub Actions |

---

## 🗃️ Data Model

```
Document
  Id, FileName, ContentType, StoragePath, ExtractedText,
  Status (Uploaded | Processing | Analyzed | Routed | NeedsReview | Failed),
  UploadedAt, UploadedBy

DocumentAnalysis
  Id, DocumentId, SummaryEn, SummaryAr, Category,
  Priority (Low | Normal | High | Urgent),
  Language, Sender, Recipient, ReferenceNumber, DocumentDate, Deadline,
  SuggestedDepartmentId, Confidence (0–1), Reasoning,
  ModelUsed, TokensUsed, CreatedAt

ActionItem
  Id, DocumentId, Description, DueDate

Department
  Id, NameEn, NameAr, Description, Keywords

RoutingDecision
  Id, DocumentId, SuggestedDepartmentId, FinalDepartmentId,
  WasOverridden (bool), Note, DecidedBy, DecidedAt
```

`RoutingDecision.WasOverridden` powers the **AI routing accuracy** metric on the dashboard.

---

## 🔌 API Endpoints

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/documents` | Upload (multipart) → returns `id`, `status=Processing` |
| `GET` | `/api/documents?status=&priority=&department=&q=&page=` | List / filter |
| `GET` | `/api/documents/{id}` | Document + analysis + action items |
| `GET` | `/api/documents/{id}/file` | Original file |
| `POST` | `/api/documents/{id}/reanalyze` | Re-run AI analysis |
| `POST` | `/api/documents/{id}/route` | `{ departmentId, note }` → accept / override |
| `GET` | `/api/departments` | Department list |
| `GET` | `/api/stats` | Dashboard numbers |
| `POST` | `/api/documents/{id}/chat` | *(v2)* Chat with document |
| `POST` | `/api/documents/{id}/draft-reply` | *(v2)* Generate reply draft |
| `HUB` | `/hubs/inbox` | `DocumentStatusChanged` events |

---

## 🤖 AI Design

### 1. Structured Output
Use **structured outputs (`output_config.format` JSON schema)** so the response is guaranteed valid JSON — no fragile text parsing. (Forced `tool_choice` is rejected by `claude-sonnet-5-5`, so structured outputs replace the tool-use approach.)

```json
{
  "summary_en": "string (max 3 sentences)",
  "summary_ar": "string",
  "category": "Request | Complaint | Invitation | Circular | Invoice | Legal | Other",
  "priority": "Low | Normal | High | Urgent",
  "language": "ar | en | mixed",
  "sender": "string | null",
  "reference_number": "string | null",
  "document_date": "YYYY-MM-DD | null",
  "deadline": "YYYY-MM-DD | null",
  "action_items": [{ "description": "string", "due_date": "YYYY-MM-DD | null" }],
  "suggested_department_id": "int",
  "confidence": "0.0–1.0",
  "reasoning": "string — why this department"
}
```

### 2. System Prompt (draft)

```
You are an assistant for a government correspondence office.
Analyze the incoming document and route it to ONE department from the list.

Rules:
- Never invent data. If a field is not in the document, return null.
- Dates must be ISO format. Convert Hijri dates to Gregorian.
- Priority = Urgent only if an explicit deadline is within 3 days
  or the text explicitly says urgent.
- If unsure about routing, set confidence below 0.6.

Departments:
{{departments_json}}
```

### 3. Prompt Caching
Static parts (instructions + department list) go first in the prompt and are cached → lower cost, faster responses.

### 4. Confidence Threshold
If `confidence < 0.6`, the document goes to a **Needs Review** queue instead of being auto-suggested.

### 5. AI Evals
- 20–30 synthetic documents with known correct answers (category, priority, department).
- `SmartInbox.AiEvals` runs them through the pipeline and reports accuracy per field.
- Results table published in the README.

---

## 🔐 Ground Rules

- ❌ No code, data, or client names from employer or client projects. Built 100% from scratch.
- ✅ Synthetic documents only — fictional entity *"Ministry of Digital Services – Demo"*.
- 🔑 API keys in `dotnet user-secrets` / `.env` — never committed.
- ✅ License: MIT.

---

## 📅 Timeline

| Days | Task | Done |
|---|---|---|
| 1–2 | Solution structure, Domain, EF Core, migrations, department seed | ☑ |
| 3–4 | Upload API, file storage, PDF text extraction | ☑ |
| 5–6 | `ClaudeProvider` with structured output, background worker | ☑ |
| 7 | Routing + user decision endpoint, SignalR | ☑ |
| 8–10 | Vue: Inbox, detail view, review screen, i18n + RTL | ☑ |
| 11 | Simple dashboard, AI evals | ☑ (eval results pending an API key) |
| 12 | Docker Compose, README, screenshots | ☑ (screenshots / GIF pending) |
| 13 | 60–90 second demo video | ☐ |
| 14 | Publish on GitHub 🚀 | ☐ |

---

## 📝 README Checklist

- [ ] Hero GIF at the top: upload → AI analysis result
- [ ] Feature list
- [ ] Architecture diagram
- [ ] Tech stack badges
- [ ] Quick start: `docker compose up` — nothing else
- [ ] AI evals results table
- [ ] Roadmap (v2 features)

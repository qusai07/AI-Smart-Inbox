# AI Smart Inbox: Launch Kit

A LinkedIn launch post, interview talking points, and a short summary of the architecture and technology choices.

---

## 1. LinkedIn launch post

```
I just open-sourced AI Smart Inbox 📥

Official correspondence still gets sorted by hand: read the letter, decide who handles it, chase the deadline. I built a system that does the first pass in seconds, with a human always making the final call.

How it works:
📄 Upload a letter, PDF, scan or email
🤖 It's summarized in Arabic and English, classified, and routed to a suggested department, with a confidence score and the reason for its choice
👤 A clerk accepts or overrides, and every decision feeds a real accuracy metric
🗂️ The department gets it in its inbox, assigns it, discusses it, and closes it
⏰ Deadline digests and automatic escalation when something is overdue

What I'm most proud of isn't the AI, it's the trust design:
✅ Uncertain documents go to human review instead of a confident guess
✅ Free by default: a rule engine plus a local LLM, with a "cost gate" that only calls a model when the rules are unsure
✅ Department-level access enforced by the API, and a full audit timeline

Built with .NET 8 (Clean Architecture), Vue 3, SQL Server, SignalR, and Claude or Ollama behind one interface.

All data is synthetic. Code is MIT licensed 👇
[GitHub link]

#dotnet #AI #VueJS #SoftwareArchitecture #OpenSource
```

**Tips**
- Attach the demo GIF or a 60-second video; it matters more than the text.
- Don't quote the 95% accuracy figure yet: it was measured on the same letters the rules were written from.

---

## 2. Presenting it in interviews

**30-second pitch**

> An AI-assisted correspondence inbox. AI does the first pass, humans decide, and everything is auditable. It runs free by default and can switch to Claude.

**Three stories, each with a decision and its trade-off**

1. **Trust over automation.** Uncertain results go to review. When the rules and the LLM disagree, a person decides. The small local model was often confidently wrong, so I measured that and designed around it.
2. **Cost control.** A hybrid gate sends only unsure documents to an LLM. The provider sits behind an interface, so you can swap engines without touching the pipeline.
3. **Reliability.** Emails go through a transactional outbox with retries and duplicate protection, so a mail outage doesn't lose notifications.

**Be honest about limits**
- Accuracy numbers come from synthetic samples.
- No OCR without a cloud model.
- Background jobs are in-memory (Hangfire is on the roadmap).

Interviewers trust candidates who know the weak points of their own system.

---

## 3. Architecture choices

| Choice | Why |
|---|---|
| **Clean Architecture** (Domain / Application / Infrastructure / Api) | Business rules don't depend on EF, SMTP or the AI vendor, so they're easy to test (89 unit tests) and easy to swap. |
| **`IAiProvider` interface** | Rules, Ollama, Claude and Hybrid are interchangeable through configuration. |
| **Background queue** | Uploads return instantly while analysis happens asynchronously. Each document is in the queue at most once. |
| **Transactional outbox** | An email is saved in the same transaction as the action that caused it, then sent with retries. |
| **Append-only timeline** | Status fields show *now*; the event log shows *how it got there*, for audit. |
| **Access rules in the application layer** | One place enforces them for the REST API and SignalR alike; the UI only hides what a role can't use. |

---

## 4. Technology: what, how, why

| Tech | Used for | Why |
|---|---|---|
| **ASP.NET Core 8** | REST API, auth, background workers | Mature and fast |
| **EF Core + SQL Server** | Data and migrations | Common in enterprise; migrations are versioned |
| **ASP.NET Identity + Entra ID** | Local accounts and Microsoft sign-in | Secure defaults (hashing, lockout); single sign-on for organizations |
| **SignalR** | Live status updates | Real-time UI without polling; scoped by role |
| **Vue 3 + TypeScript + Pinia** | Front end | Light and typed, with a clean state store |
| **PrimeVue + vue-i18n** | UI components, Arabic/English with right-to-left | Polished components; full bilingual support |
| **Rule engine (custom)** | Free, offline analysis | Explainable and deterministic, at zero cost |
| **Ollama / Claude** | LLM analysis | Local and free, or best-quality cloud, with JSON-schema output |
| **PdfPig / MimeKit / MailKit** | Reading PDFs and emails, sending email | Reliable, open-source .NET libraries |
| **Docker Compose + smtp4dev** | One-command setup with a test mail server | Anyone can run the demo in minutes |
| **xUnit + AI evals runner** | Code tests and AI accuracy measurement | Measures AI quality per engine instead of guessing |

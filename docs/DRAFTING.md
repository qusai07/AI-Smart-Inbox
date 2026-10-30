# Reply drafting

Smart Inbox prepares the **formal reply** to a routed letter in **English and Arabic**, from human-written templates. It is free, offline and uses no AI: the letter's analysis fills in the facts, and anything unknown is left as a visible `[●]` gap for a person. **Nothing is ever sent**: a person edits, approves and sends the reply through the usual channel.

## How it works

1. Open a routed document and click **Draft reply** (Reply panel). Pick a type of reply, or leave **Automatic** to choose by the letter's category. Optional notes stay with the draft.
2. In about a second the draft appears: English and Arabic side by side.
3. Fill every `[●]` (the panel counts them and lists what's missing) and **Save changes** — each save is a new version; the original is kept.
4. **Approve** (only possible when no `[●]` is left and both languages are filled). Approving issues the **outgoing number** from the register and writes it into both letters, then **Download Word** or copy the text.

Who can draft and approve: the correspondence desk, or staff of the department the letter is routed to — the same people who handle the document. Every step is on the document's timeline.

## Types of reply

| Template | Chosen automatically for | Gaps a person fills |
|---|---|---|
| Acknowledge receipt | Requests | — |
| Acknowledge a legal notice | Legal | (deadline, if the letter had none) |
| Acknowledge a complaint | Complaints | Date by which we will reply |
| Acknowledge an invoice | Invoices | Expected payment date |
| Accept an invitation | Invitations | Name and title of the representative |
| Acknowledge a circular | Circulars | — |
| General reply | Other | The reply to what they asked |
| Decline an invitation · Ask for more information · Reply to a thank-you letter | (pick manually) | Information needed (request info) |

**Our outgoing reference number** is not a gap: the draft shows *(issued on approval)* / *(يصدر عند الاعتماد)* and the register replaces it with the next number (e.g. `MDS/OUT/2026/0042`) in the same save as the approval. If a person types a number there instead, none is issued.

**Filled in automatically:** sender, their reference number and date (worded naturally when either is missing: "your letter dated 3 February 2027", «خطابكم المؤرخ في 3 فبراير 2027»), deadline, the replying department and its signatory title (*Administration → departments*, `PUT /api/departments/{id}/signatory`), the organisation name (`Drafting:OrganisationNameEn/Ar`), and today's date — in Arabic with the Hijri equivalent.

Templates live in [`src/SmartInbox.Application/ReplyTemplates/`](../src/SmartInbox.Application/ReplyTemplates) (one JSON file each: `{Slot}` is filled from the analysis, `{Slot?}` is optional, `{?Label}` is a gap a person fills). Unknown slots fail at startup, so a typo can't silently become `[●]`.

## Why templates and not AI

Measured on the 20 sample letters ([EVALS-drafting.md](EVALS-drafting.md)): the right type of reply **90%** of the time, half the letters complete except our own reference, **0.5** other gaps per letter, and **100%** of the references and dates in drafts found in the letters.

Most remaining gaps are **decisions only a person can make** (when we'll reply, who represents the Ministry, the payment date). No model can know those, and guessing would be worse than `[●]` — so no local or paid model is used. Formal Arabic from templates is always correct.

Rerun the measurement: `dotnet run --project tests/SmartInbox.AiEvals -c Release -- --drafting`.

## API, SDK, webhooks, MCP

| | |
|---|---|
| REST | `POST/GET /api/documents/{id}/drafts`, `GET/PUT /api/drafts/{id}`, `POST /api/drafts/{id}/approve` · `/reject`, `GET /api/drafts/{id}/docx`, `GET /api/reply-templates` |
| SDK (0.3.0) | `inbox.Drafts.RequestAsync`, `WaitForDraftAsync`, `UpdateAsync`, `ApproveAsync`, `RejectAsync`, `DownloadDocxAsync`, `ListTemplatesAsync` |
| Webhooks | `draft.ready`, `draft.approved` (ids only, never the letter text) |
| MCP | `draft_reply` (write), `get_reply_drafts`, `list_reply_templates` — **no approve tool**: approval is a person's decision in the web app |

Settings: `Drafting:Enabled` (default `true`), `Drafting:OrganisationNameEn`, `Drafting:OrganisationNameAr`.

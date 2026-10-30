# Outgoing register

Every letter that leaves the ministry gets an **official outgoing number** (e.g. `MDS/OUT/2026/0042`), recorded in a searchable **register**. Replies get their number automatically **when they are approved**; other letters can be registered by hand. Free, no AI.

- [How a reply gets its number](#how-a-reply-gets-its-number)
- [The register page](#the-register-page)
- [Numbering](#numbering)
- [Guarantees](#guarantees)
- [API, webhook, SDK and MCP](#api-webhook-sdk-and-mcp)

## How a reply gets its number

1. A reply draft shows **Our ref: (issued on approval)** / **رقمنا: (يصدر عند الاعتماد)**. This is a marker, not a `[●]` gap, so it doesn't block approval.
2. **Approve** issues the next number and writes it into the English and Arabic letters, in the same save as the approval. The Word file has it too.
3. The register entry records the recipient (the letter's sender), both subjects, the department, and links to the incoming letter and the draft.
4. The document page shows **Replied with MDS/OUT/2026/0042**. The timeline gets an *Outgoing number issued* entry, and a `register.issued` webhook is sent.

If someone types a number over the marker before approving, no number is issued. Drafts approved before the register existed keep the number someone typed.

## The register page

**Outgoing register** in the sidebar. Desk staff (Admin, Clerk) see every department; department staff see their own.

| | |
|---|---|
| Search and filter | Number, recipient or subject; year, department, status |
| **Export CSV** | Everything matching the filters (up to 10,000 rows), UTF-8 so Excel shows the Arabic. Text a spreadsheet would run as a formula (`=…`) is exported as plain text |
| **New outgoing letter** | For a letter written outside Smart Inbox, or one that isn't a reply: department, recipient, subject (English and/or Arabic) |
| **Mark as sent** | How (email, post, by hand, other), when, and a note such as the registered-mail number |
| **Cancel number** | Desk only, reason required. The number stays in the register, crossed out, and is never issued again |

## Numbering

| Setting | Default | |
|---|---|---|
| `Register:Prefix` | `MDS` | |
| `Register:Format` | `{Prefix}/OUT/{Year}/{Seq:0000}` | Tokens: `{Prefix}`, `{Year}`, `{Seq:0000}` (any number of zeros), `{Dept}` |
| `Register:SequencePerDepartment` | `false` | `true` gives each department its own sequence; use it with `{Dept}`, e.g. `{Prefix}/{Dept}/{Year}/{Seq:0000}` → `MDS/LEG/2026/0042` |

- The sequence **restarts at 1 every year**. The year is taken in the organisation's time zone (`Reminders:TimeZone`), so a letter approved at 1 a.m. on 1 January in Dubai belongs to the new year.
- Department codes (`{Dept}`) are set in **Administration → Department codes** (letters and digits, unique). A department without a code uses `GEN`.

## Guarantees

- **Unique and gap-free**: a counter row per year (and department) is updated in the same transaction as the register entry, and numbers have a unique index. If two people approve at the same moment, the second save fails and only the number is retried. Tested with 50 simultaneous issues on a real database: 50 distinct, consecutive numbers.
- **A failure burns nothing**: if the approval can't be saved, no number is used and the draft stays *Ready*.
- **Never reused or edited**: a mistake is cancelled with a reason, and the next letter gets a new number.
- **Audit**: who issued, sent and cancelled each number, and when.

## API, webhook, SDK and MCP

| Method | Route | Who | |
|---|---|---|---|
| `GET` | `/api/register?year=&department=&status=&q=&page=&pageSize=` | Member | List / search (department users: own department) |
| `GET` | `/api/register/{id}` | Member | One entry |
| `POST` | `/api/register` | Member | `{ departmentId, recipient, subjectEn?, subjectAr?, documentId? }` issues a number by hand |
| `POST` | `/api/register/{id}/sent` | Member | `{ via: Email\|Post\|Hand\|Other, sentAt?, note? }` |
| `POST` | `/api/register/{id}/cancel` | Desk | `{ reason }` |
| `GET` | `/api/register/export.csv` | Member | Same filters as the list |
| `PUT` | `/api/departments/{id}/code` | Admin | `{ code }` for `{Dept}` |

- **Webhook** `register.issued`: `outgoingLetterId`, `number`, `departmentId`, `documentId`, `replyDraftId`, `issuedBy`, `url`. Ids only, never the letter text ([WEBHOOKS.md](WEBHOOKS.md)).
- **SDK** 0.5.0: `inbox.Register.ListAsync / ListAllAsync / GetAsync / IssueAsync / MarkSentAsync / CancelAsync / ExportCsvAsync`; `ReplyDraft.OutgoingNumber`; `RegisterIssuedEvent`.
- **MCP**: `search_register` (read-only) answers "did we reply to X?" or "what did Legal send this year?". Numbers can't be issued or cancelled through MCP: a person approves the reply in the web app.

```csharp
var draft = await inbox.Drafts.ApproveAsync(draftId);
Console.WriteLine(draft.OutgoingNumber);   // MDS/OUT/2026/0042

await foreach (var l in inbox.Register.ListAllAsync(new RegisterQuery { Year = 2026, Status = OutgoingStatus.Issued }))
    Console.WriteLine($"{l.Number}  {l.Recipient}  not sent yet");
```

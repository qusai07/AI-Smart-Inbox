# Related letters

When a letter is analysed, Smart Inbox finds the earlier letters it belongs with and links them, always saying why. Duplicates are caught before two departments work on the same letter. Reminders reach the department that already has the case, and a reply to one of our letters points back to it. Free, no AI: exact, explainable rules only.

- [How letters are linked](#how-letters-are-linked)
- [What people see](#what-people-see)
- [Routing follows the case](#routing-follows-the-case)
- [Settings](#settings)
- [Accuracy](#accuracy)
- [API, webhook, SDK and MCP](#api-webhook-sdk-and-mcp)

## How letters are linked

| Link | Found when | Reason shown |
|---|---|---|
| **Same letter** (duplicate) | The same file (SHA-256), or the same text after evening out spacing and capitals (letters of at least 200 characters) | *Same file* / *Same text* |
| **Replies to our letter** | The text quotes one of our outgoing numbers that is in the [register](REGISTER.md), in Latin or Arabic digits | *Quotes our letter MDS/OUT/2026/0042* |
| **Same email conversation** | The email's `In-Reply-To` / `References` point to an email already imported from the [shared mailbox](MAILBOX.md) | *Reply to the email "Meeting request"* |
| **Same case** (follow-up) | The same reference number **and** the same sender | *Same reference 2027/118 from Demo City Court* |

- **A reference number alone is never enough**, because different senders reuse numbers like "1/2026". Neither is a sender alone.
- Reference numbers match in any form: "Ref. No. ٢٠٢٧ / ١١٨", "2027-118" and "2027/118" are the same.
- "Same sender" means the same name after evening out case, punctuation, "the" / "ال" and Arabic letter forms (أ/إ/آ, ة/ه, ى/ي). A shorter name of two or more words that starts the fuller one also counts ("Demo City Court" = "Demo City Court of First Instance"). The same organisation email domain counts too. Webmail domains such as gmail.com or outlook.com don't.
- Each pair of letters gets one link with the strongest reason (same letter > reply to our letter > email conversation > same case). Only letters from the last `Threads:LookbackDays` (365) are considered, and every lookup uses an index.
- Letters received before this feature are fingerprinted and linked once, at the next start of the API (oldest first, with no timeline entries or webhooks for the past).

## What people see

- **Document page**:
  - A **Related letters** box shows the kind of link, the sender, the date, the department, the handling status, our outgoing number (linking to the register) and the reason.
  - **✕ Not related** removes a wrong link. It never comes back, and the removal is on the timeline.
- **Duplicate warning** at the top: *"This looks like a letter you already have. The same letter was received on 20 Oct 2026. It was routed to Legal Affairs."* with **Open the earlier copy**. Nothing is closed or merged automatically.
- **Inbox and department lists**: ⚠ on possible duplicates (the later copy), 🔗 on letters with related letters. The inbox filter **Related letters** shows letters with or without them.
- **Timeline**: *Related letter found* with the reason.

People only see related letters they are allowed to see. A Legal Affairs user is not shown a related letter that went to HR.

## Routing follows the case

If the case already has a department, the routing suggestion uses it. That department is where the most recent related letter was routed or, for a reply to one of our letters, the department that sent our letter.

| The analysis… | Then |
|---|---|
| is unsure or has no suggestion | The case's department is suggested at 0.8 confidence, so the letter is *Ready to route* rather than *Needs review*. The reason starts with *"Earlier letters in this case went to Legal Affairs."* |
| suggests the same department | The reason is added |
| is confident about another department | Its suggestion stays and the reason mentions the case; the desk decides |

## Settings

| Setting | Default | |
|---|---|---|
| `Threads:Enabled` | `true` | Turn related-letter matching off |
| `Threads:LookbackDays` | `365` | How far back to look |
| `Threads:MinTextForDuplicate` | `200` | Shorter texts (a one-line email) are too generic to call two letters the same by text alone; identical files are still caught |

## Accuracy

The eval (`dotnet run --project tests/SmartInbox.AiEvals -- --threads`) builds 80 labelled cases from the 20 sample letters and runs each through the real upload → analysis → matching pipeline: sent twice, retyped, a reminder from the same sender, a reply quoting our number, and a **look-alike** (another sender quoting the same reference) that must not be linked. Results: [EVALS-threads.md](EVALS-threads.md). The cases use fixed wording, so the numbers show the matching works as designed, not real-world accuracy.

## API, webhook, SDK and MCP

| Method | Route | Who | |
|---|---|---|---|
| `GET` | `/api/documents/{id}/related` | Member | Related letters the user may see, with kind and reason |
| `DELETE` | `/api/documents/{id}/related/{linkId}` | Member | Remove a wrong link (it stays removed) |
| `GET` | `/api/documents?related=true\|false` | Member | Filter; every row has `hasRelated` and `possibleDuplicate` |

- **Webhook** `document.linked`: `documentId` (the newer letter), `relatedDocumentId`, `outgoingLetterId`, `kind`, `url`. Ids only, never the reason, which quotes the letters ([WEBHOOKS.md](WEBHOOKS.md)).
- **SDK** 0.6.0: `inbox.Documents.RelatedAsync(id)` / `RemoveRelatedAsync(id, linkId)`, `DocumentListQuery.Related`, `DocumentSummary.PossibleDuplicate`, `DocumentLinkedEvent`.
- **MCP**: `get_document` lists related letters. The reason and sender are under `fromDocument`, as data and never instructions.

```csharp
foreach (var r in await inbox.Documents.RelatedAsync(documentId))
    Console.WriteLine($"{r.Kind}: {r.Evidence}");   // FollowUp: Same reference 2027/118 from Demo City Court
```

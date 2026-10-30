# 🔗 Smart Inbox Related Letters — Project Plan

> When a letter arrives, Smart Inbox finds the letters it belongs with: an **exact duplicate** (the same letter sent twice), a **follow-up** in the same case (same reference number from the same sender), a **reply to one of our letters** (it quotes our outgoing number), or the **next email in a thread**. The document page shows them, the inbox flags them, and the routing suggestion follows the case. Free, no AI.

**Duration:** ~4 days · **Branch:** `dev` · **Cost:** none

---

## 1. Goals & Non-Goals

### Goals
- **Catch duplicates** before two departments work on the same letter
- **Keep a case together**: a follow-up or reminder links to the earlier letters and goes to the department that already has the case
- **Close the loop with the register**: a letter quoting `MDS/OUT/2026/0042` links to our outgoing letter and the incoming letter it answered
- **Explainable**: every link says why ("same reference 2027/118 from Demo City Court"); a person can remove a wrong link
- **Measured**: an eval of found / missed / wrong links on labelled sample pairs

### Non-Goals (later)
- Fuzzy "similar content" matching (embeddings): v1 only links on exact, explainable signals
- Merging documents or automatic closing: people decide
- A separate "case" entity with its own page: v1 links letters to each other

---

## 2. How letters are linked

Checked once analysis finishes (and again after re-analysis), against letters from the last `Threads:LookbackDays` (default 365):

| Link | Signal | Shown as |
|---|---|---|
| **Duplicate** | Same file (SHA-256 of the bytes), or the same extracted text after normalising whitespace | ⚠ "Same letter as one received on 20 Oct" |
| **Follow-up** | Same **reference number** (normalised: case, spaces, `/`, `-`, Arabic-Indic digits) **and** same sender (normalised name or email domain) | "Same case: ref. 2027/118 from Demo City Court" |
| **Reply to our letter** | The text quotes one of **our outgoing numbers** that exists in the register | "Replies to our letter MDS/OUT/2026/0042" (links to the register entry and its incoming letter) |
| **Email thread** | `In-Reply-To` / `References` of the email match an email we imported | "Next email in the same conversation" |

- A reference number alone is **not** enough (different senders reuse "1/2026"); a sender alone is not enough either
- Links are symmetric and stored once; a person can **remove** a wrong link (it won't come back for that pair)

---

## 3. What people see

- **Document page**: a **Related letters** box (type, date, sender, status, department, link). A duplicate shows a warning at the top: *"This looks like the same letter as … (already routed to Legal Affairs)"*
- **Inbox**: a 🔗 icon on letters with related letters; ⚠ on suspected duplicates. Filter: *Has related letters*
- **Routing suggestion**: if the case already went to a department, the suggestion says *"Earlier letters in this case went to Legal Affairs"* and uses that department when the AI's own suggestion is unsure. The desk still decides
- **Timeline**: *"Linked to … (follow-up)"*
- **What's new**: one item

---

## 4. Data Model

```
Document (+)
  ContentHash (SHA-256, indexed)

InboundEmail (+)
  InReplyTo, References (Message-Ids)

DocumentLink
  Id, DocumentId, RelatedDocumentId (stored with the smaller id first, unique pair),
  Kind (Duplicate | FollowUp | RepliesToOurLetter | EmailThread),
  Evidence (e.g. "ref 2027/118 · Demo City Court"), OutgoingLetterId?,
  CreatedAt, RemovedAt?, RemovedBy?
```

Matching uses indexed columns (hash, normalised reference, Message-Id). No full-table scans.

---

## 5. API, Webhooks, SDK, MCP

- `GET /api/documents/{id}/related`, `DELETE /api/documents/{id}/related/{linkId}` (remove a wrong link)
- Document list: `hasRelated`, `possibleDuplicate` flags + `related=true` filter
- Webhook `document.linked` (thin: ids, kind)
- SDK 0.6.0 `Documents.RelatedAsync`; MCP: `get_document` includes related letters (read-only)

---

## 6. Testing & eval

| Layer | Tests |
|---|---|
| Matching | each signal; reference/sender normalisation (incl. Arabic digits and names); same reference from a different sender is **not** linked; lookback window; re-analysis doesn't duplicate links; removed links stay removed |
| Register | a letter quoting our number links to the register entry and its incoming letter; an unknown number links to nothing |
| Email | `In-Reply-To` threads through the real IMAP path (GreenMail in CI) |
| API/SDK/MCP | end to end; permissions (a department only sees related letters it may see) |
| **Eval** (`--threads`) | ~30 labelled pairs built from the sample letters (duplicates, follow-ups, replies to us, and look-alikes that must **not** link): precision and recall per link type → `docs/EVALS-threads.md` |

---

## 7. Timeline

| Day | Task | Done |
|---|---|---|
| 1 | Content hash, email headers, `DocumentLink` + migration; matcher (4 signals, normalisation); unit tests | ☑ |
| 2 | Run after analysis / re-analysis; routing suggestion from the case; timeline; API, list flags, webhook; eval | ☑ |
| 3 | Web UI: Related letters box, duplicate warning, inbox icons + filter, remove link, What's new | ☑ |
| 4 | SDK 0.6.0, MCP; docs (`docs/THREADS.md`), README | ☑ |

---

## 8. Definition of Done

- [ ] All tests green in CI
- [x] Duplicates, follow-ups, replies to our letters and email threads are linked, each with its reason
- [x] No link on a reference number alone (tested with look-alikes)
- [x] Eval published with precision/recall per link type
- [x] Docs + README

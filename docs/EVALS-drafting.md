# Drafting Evals

Reply drafting with **templates only (no AI, no cost)**. Analysis engine: **Rules** · 20 synthetic letters (EN + AR) · run 2026-10-30 · "today" pinned to 2027-02-16.

Each letter is analysed, routed to its correct department (as a clerk would), and drafted. `Expected` lists the templates a clerk could reasonably choose (a judgement recorded in `samples/labels.json`).

| Measure | Result |
|---|---|
| Right template, end to end (from the analysis's category) | **90%** |
| Right template if the category were correct (template mapping alone) | 90% |
| Complete (our number comes from the register) | 50% |
| Average gaps for a person | 0.5 |
| Every reference and date in the draft found in the letter | 100% |
| Time per draft | 0.4 ms |

Why gaps remain:

- Date by which we will reply: 3 letter(s)
- Name and title of the representative: 2 letter(s)
- Expected payment date: 2 letter(s)
- The reply to what they asked: 2 letter(s)
- Deadline: 1 letter(s)

<details><summary>Per-letter results</summary>

| Letter | Category (expected → got) | Template (expected → chosen) | Gaps | Not found in the letter |
|---|---|---|---|---|
| ar-01-complaint-delay.txt | ✅ Complaint → Complaint | ✅ complaint-acknowledgement → complaint-acknowledgement | Date by which we will reply | — |
| ar-02-urgent-minister-meeting.txt | ✅ Invitation → Invitation | ✅ accept-invitation / decline-invitation → accept-invitation | Name and title of the representative | — |
| ar-03-invoice-maintenance.txt | ✅ Invoice → Invoice | ✅ invoice-received → invoice-received | Expected payment date | — |
| ar-04-legal-contract-review.txt | ✅ Legal → Legal | ❌ acknowledge-receipt → legal-acknowledgement | Deadline | — |
| ar-05-circular-flexible-work.txt | ✅ Circular → Circular | ✅ circular-acknowledgement → circular-acknowledgement | — | — |
| ar-06-training-request.txt | ✅ Request → Request | ✅ acknowledge-receipt → acknowledge-receipt | — | — |
| ar-07-strategy-report.txt | ✅ Request → Request | ✅ acknowledge-receipt / general-reply → acknowledge-receipt | — | — |
| en-01-invitation-forum.txt | ✅ Invitation → Invitation | ✅ accept-invitation / decline-invitation → accept-invitation | Name and title of the representative | — |
| en-02-invoice-cloud.txt | ✅ Invoice → Invoice | ✅ invoice-received → invoice-received | Expected payment date | — |
| en-03-complaint-licence.txt | ✅ Complaint → Complaint | ✅ complaint-acknowledgement → complaint-acknowledgement | Date by which we will reply | — |
| en-04-court-notice.txt | ✅ Legal → Legal | ✅ legal-acknowledgement → legal-acknowledgement | — | — |
| en-05-secondment-request.txt | ✅ Request → Request | ✅ acknowledge-receipt → acknowledge-receipt | — | — |
| en-06-api-integration.txt | ✅ Request → Request | ✅ acknowledge-receipt / general-reply → acknowledge-receipt | — | — |
| en-07-circular-kpi.txt | ✅ Circular → Circular | ✅ circular-acknowledgement → circular-acknowledgement | — | — |
| en-08-urgent-cyber.txt | ✅ Other → Other | ✅ acknowledge-receipt / general-reply → general-reply | The reply to what they asked | — |
| en-09-thank-you.txt | ✅ Other → Other | ✅ thank-you-reply / general-reply → general-reply | The reply to what they asked | — |
| en-10-tender-clarification.txt | ✅ Request → Request | ❌ general-reply → acknowledge-receipt | — | — |
| en-11-employee-grievance.txt | ❌ Complaint → Request | ✅ complaint-acknowledgement / acknowledge-receipt → acknowledge-receipt | — | — |
| en-12-ambiguous-data-sharing.txt | ✅ Request → Request | ✅ acknowledge-receipt / general-reply → acknowledge-receipt | — | — |
| en-13-email-outage.eml | ✅ Complaint → Complaint | ✅ complaint-acknowledgement / acknowledge-receipt → complaint-acknowledgement | Date by which we will reply | — |

</details>

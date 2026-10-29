# AI Evals

Engine: **Hybrid (rules first, Ollama when confidence < 0.75)** · 20 synthetic documents (EN + AR) · run 2026-10-29 · "today" pinned to 2027-02-16

Answered by: `rules-v1` ×16, `qwen2.5:3b` ×4

| Field | Accuracy |
|---|---|
| Department (routing) | **90%** |
| Category | 90% |
| Priority | 90% |
| Safe routing (correct, or flagged < 0.6 confidence) | 100% |

Average tokens per document: 290. Errors: 0.

<details><summary>Per-document results</summary>

| Document | Department (expected → got, confidence) | Category | Priority |
|---|---|---|---|
| ar-01-complaint-delay.txt | ✅ 6 → 6 (0.94) | ✅ Complaint → Complaint | ✅ Normal → Normal |
| ar-02-urgent-minister-meeting.txt | ✅ 1 → 1 (0.94) | ✅ Invitation → Invitation | ✅ Urgent → Urgent |
| ar-03-invoice-maintenance.txt | ✅ 3 → 3 (0.97) | ✅ Invoice → Invoice | ✅ Normal → Normal |
| ar-04-legal-contract-review.txt | ✅ 2 → 2 (0.80) | ✅ Legal → Legal | ✅ High → High |
| ar-05-circular-flexible-work.txt | ❌ 4 → 7 (0.50) | ✅ Circular → Circular | ✅ Normal → Normal |
| ar-06-training-request.txt | ✅ 4 → 4 (0.98) | ✅ Request → Request | ✅ Normal → Normal |
| ar-07-strategy-report.txt | ✅ 7 → 7 (0.95) | ✅ Request → Request | ✅ Normal → Normal |
| en-01-invitation-forum.txt | ✅ 1 → 1 (0.98) | ✅ Invitation → Invitation | ❌ Normal → High |
| en-02-invoice-cloud.txt | ✅ 3 → 3 (0.98) | ✅ Invoice → Invoice | ✅ Normal → Normal |
| en-03-complaint-licence.txt | ✅ 6 → 6 (0.83) | ✅ Complaint → Complaint | ✅ High → High |
| en-04-court-notice.txt | ✅ 2 → 2 (0.97) | ✅ Legal → Legal | ✅ High → High |
| en-05-secondment-request.txt | ✅ 4 → 4 (0.82) | ✅ Request → Request | ✅ High → High |
| en-06-api-integration.txt | ✅ 5 → 5 (0.98) | ✅ Request → Request | ✅ Normal → Normal |
| en-07-circular-kpi.txt | ✅ 7 → 7 (1.00) | ✅ Circular → Circular | ✅ Normal → Normal |
| en-08-urgent-cyber.txt | ✅ 5 → 5 (0.96) | ✅ Other → Other | ✅ Urgent → Urgent |
| en-09-thank-you.txt | ✅ 1 → 1 (0.81) | ✅ Other → Other | ✅ Low → Low |
| en-10-tender-clarification.txt | ❌ 3 → 7 (0.50) | ✅ Request → Request | ✅ Normal → Normal |
| en-11-employee-grievance.txt | ✅ 4 → 4 (0.97) | ❌ Complaint → Request | ✅ Normal → Normal |
| en-12-ambiguous-data-sharing.txt | ✅ 7 → 7 (0.78) | ✅ Request → Request | ✅ Normal → Normal |
| en-13-email-outage.eml | ✅ 5 → 5 (0.50) | ❌ Complaint → Invoice | ❌ Urgent → Normal |

</details>

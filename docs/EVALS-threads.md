# Related letters eval

Engine: **Rules** · 80 labelled cases built from 20 sample letters (EN + AR) · run 2026-10-30

Each case runs through the real upload → analysis → matching pipeline on a fresh database. Free, no AI beyond the analysis engine.

| | |
|---|---|
| **Precision**: links made that were right | **100%** (70 links) |
| **Recall**: letters that belong together and were linked | **100%** (70 cases) |
| Look-alikes linked (same reference, other sender; must be 0) | 0 of 10 |

| Case | Cases | Linked with the right reason | Linked, other reason | Missed | Wrong link |
|---|---|---|---|---|---|
| Sent twice | 20 | 20 | 0 | 0 | 0 |
| Retyped | 20 | 20 | 0 | 0 | 0 |
| Reminder, same sender | 10 | 10 | 0 | 0 | 0 |
| Look-alike, other sender | 10 | – | – | – | 0 |
| Replies to our letter | 20 | 20 | 0 | 0 | 0 |

The cases are built from the same samples with fixed wording, so these numbers show the matching works as designed, not real-world accuracy: real reminders are worded and laid out in more ways. A reminder is only built when the sample states a reference number. Email threads are matched on message headers alone (no analysis involved) and are covered by unit tests.

<details><summary>Per-case results</summary>

| Sample | Case | Expected | Found | Reason given |
|---|---|---|---|---|
| en-01-invitation-forum.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-01-invitation-forum.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-01-invitation-forum.txt | Reminder, same sender | FollowUp | ✅ FollowUp | Same reference GDGF/INV/2027/031 from Gulf Digital Government Forum |
| en-01-invitation-forum.txt | Look-alike, other sender | no link | ✅ no link | — |
| en-01-invitation-forum.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-02-invoice-cloud.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-02-invoice-cloud.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-02-invoice-cloud.txt | Reminder, same sender | FollowUp | ✅ FollowUp | Same reference NCS-INV-88213 from NIMBUS CLOUD SERVICES LLC |
| en-02-invoice-cloud.txt | Look-alike, other sender | no link | ✅ no link | — |
| en-02-invoice-cloud.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-03-complaint-licence.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-03-complaint-licence.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-03-complaint-licence.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-04-court-notice.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-04-court-notice.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-04-court-notice.txt | Reminder, same sender | FollowUp | ✅ FollowUp | Same reference 2027/118 from DEMO CITY COURT OF FIRST INSTANCE |
| en-04-court-notice.txt | Look-alike, other sender | no link | ✅ no link | — |
| en-04-court-notice.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-05-secondment-request.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-05-secondment-request.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-05-secondment-request.txt | Reminder, same sender | FollowUp | ✅ FollowUp | Same reference FAD/HR/2027/12 from Federal Authority for Data – Demo |
| en-05-secondment-request.txt | Look-alike, other sender | no link | ✅ no link | — |
| en-05-secondment-request.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-06-api-integration.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-06-api-integration.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-06-api-integration.txt | Reminder, same sender | FollowUp | ✅ FollowUp | Same reference MOHD/DH/2027/044 from Ministry of Health – Demo |
| en-06-api-integration.txt | Look-alike, other sender | no link | ✅ no link | — |
| en-06-api-integration.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-07-circular-kpi.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-07-circular-kpi.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-07-circular-kpi.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-08-urgent-cyber.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-08-urgent-cyber.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-08-urgent-cyber.txt | Reminder, same sender | FollowUp | ✅ FollowUp | Same reference NCSC-ADV-2027-009 from National Cybersecurity Centre – Demo |
| en-08-urgent-cyber.txt | Look-alike, other sender | no link | ✅ no link | — |
| en-08-urgent-cyber.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-09-thank-you.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-09-thank-you.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-09-thank-you.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-10-tender-clarification.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-10-tender-clarification.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-10-tender-clarification.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-11-employee-grievance.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-11-employee-grievance.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-11-employee-grievance.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-12-ambiguous-data-sharing.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-12-ambiguous-data-sharing.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-12-ambiguous-data-sharing.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| en-13-email-outage.eml | Sent twice | Duplicate | ✅ Duplicate | Same file |
| en-13-email-outage.eml | Retyped | Duplicate | ✅ Duplicate | Same text |
| en-13-email-outage.eml | Reminder, same sender | FollowUp | ✅ FollowUp | Same reference DC-INC-30117 from Service Desk - Demo Customs |
| en-13-email-outage.eml | Look-alike, other sender | no link | ✅ no link | — |
| en-13-email-outage.eml | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| ar-01-complaint-delay.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| ar-01-complaint-delay.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| ar-01-complaint-delay.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| ar-02-urgent-minister-meeting.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| ar-02-urgent-minister-meeting.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| ar-02-urgent-minister-meeting.txt | Reminder, same sender | FollowUp | ✅ FollowUp | Same reference م.و/2027/215 from مكتب وزير الاقتصاد – تجريبي |
| ar-02-urgent-minister-meeting.txt | Look-alike, other sender | no link | ✅ no link | — |
| ar-02-urgent-minister-meeting.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| ar-03-invoice-maintenance.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| ar-03-invoice-maintenance.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| ar-03-invoice-maintenance.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| ar-04-legal-contract-review.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| ar-04-legal-contract-review.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| ar-04-legal-contract-review.txt | Reminder, same sender | FollowUp | ✅ FollowUp | Same reference إ.م/2027/77 from إدارة المالية والمشتريات |
| ar-04-legal-contract-review.txt | Look-alike, other sender | no link | ✅ no link | — |
| ar-04-legal-contract-review.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| ar-05-circular-flexible-work.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| ar-05-circular-flexible-work.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| ar-05-circular-flexible-work.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| ar-06-training-request.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| ar-06-training-request.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| ar-06-training-request.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |
| ar-07-strategy-report.txt | Sent twice | Duplicate | ✅ Duplicate | Same file |
| ar-07-strategy-report.txt | Retyped | Duplicate | ✅ Duplicate | Same text |
| ar-07-strategy-report.txt | Reminder, same sender | FollowUp | ✅ FollowUp | Same reference أ.ع/2027/58 from الأمانة العامة لمجلس الوزراء – تجريبي |
| ar-07-strategy-report.txt | Look-alike, other sender | no link | ✅ no link | — |
| ar-07-strategy-report.txt | Replies to our letter | RepliesToOurLetter | ✅ RepliesToOurLetter | Quotes our letter MDS/OUT/2026/0042 |

</details>

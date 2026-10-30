# Mailbox Evals

Do letters that arrive by email get the same analysis as uploads? Engine: **Rules** · 20 synthetic letters · run 2026-10-30 · "today" pinned to 2027-02-16.

Each letter goes through the real MIME parser and text extractors three ways: uploaded, attached to an email, and pasted into an email body (text letters only; the email's From/To/Date/Subject lines come along). Test emails are dated on the pinned "today".

Known behaviour: for a letter pasted into an email body, the email's Date line is taken as the document date, so relative deadlines ("within 30 days") count from when the email was sent. Attach letters as files to keep their own date.

| Way in | Department | Category | Priority | Same result as upload |
|---|---|---|---|---|
| Upload | 95% (20) | 95% (20) | 90% (20) | — |
| Email attachment | 95% (20) | 95% (20) | 90% (20) | 100% (20) |
| Email body | 100% (19) | 95% (19) | 95% (19) | 100% (19) |

Percentages are over the letters each way applies to (count in brackets).

<details><summary>Letters whose result changed</summary>

None.

</details>

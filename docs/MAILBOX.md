# Shared mailbox

Smart Inbox can read a **shared mailbox** (e.g. `correspondence@ministry`) and take in every new email and its letters automatically — no saving attachments and uploading them. Imported letters are analysed, routed and drafted exactly like uploads. It uses standard **IMAP** (MailKit) and is **free**. Off by default.

- [What happens to an email](#what-happens-to-an-email)
- [Set it up](#set-it-up)
- [See it working](#see-it-working)
- [Try it with Docker](#try-it-with-docker)
- [Safety](#safety)
- [Troubleshooting](#troubleshooting)

## What happens to an email

| The email has… | Smart Inbox creates |
|---|---|
| No attachment (the email *is* the letter) | One document: the email itself |
| Letters attached (PDF, Word, JPG/PNG, forwarded emails) | **One document per letter**; the email is kept as its cover note |
| Only other files (e.g. Excel, zip) | One document from the email text; the files are listed as "not imported" |
| An auto-reply, bounce, or a sender not on the allowed list | Nothing — logged as *Skipped* with the reason |

Every document from email shows **Received by email** on its page (sender, subject, date, covering message) with a download of the original email, a ✉ icon in the inbox, and *"Received by email from …"* at the start of its timeline. After import, the email is moved to the **Processed** folder.

Measured on the 20 sample letters ([EVALS-mailbox.md](EVALS-mailbox.md)): letters attached to an email get **the same analysis as uploads, 20 of 20**; pasted into the email body, 19 of 19.

## Set it up

Settings go in user-secrets, environment variables or `.env` (Docker) — never commit a password.

| Setting | Default | |
|---|---|---|
| `Mailbox:Enabled` | `false` | Check automatically (*Check now* works either way once configured) |
| `Mailbox:Host` / `Port` / `Security` | – / `993` / `SslOnConnect` | IMAP server (`143` + `StartTls` for most internal servers) |
| `Mailbox:Username` / `Password` | – | The shared mailbox's sign-in |
| `Mailbox:Folder` | `INBOX` | Where new mail arrives |
| `Mailbox:ProcessedFolder` | `Processed` | Imported mail moves here (created if missing). Empty = leave in place and flag |
| `Mailbox:PollSeconds` | `60` | How often to check |
| `Mailbox:MaxPerCheck` | `25` | Backlogs are worked through in batches |
| `Mailbox:AllowedSenders` | – (anyone) | e.g. `["@court.gov", "minister@gov.ae"]` |
| `Mailbox:MaxAttempts` | `5` | Then the email is *Failed* and waits for an admin |

**Gmail:** turn on 2-step verification, create an **app password**, then `Host=imap.gmail.com`, `Port=993`, `Security=SslOnConnect`, `Username=you@gmail.com`, `Password=<app password>`.

**Outlook.com / Hotmail:** `Host=outlook.office365.com`, `Port=993`, with an **app password**.

**Your organisation's mail server:** ask IT for the IMAP host and port, and a service account for the shared mailbox.

**Microsoft 365 / Exchange Online (work accounts):** Microsoft has turned off password sign-in for IMAP there, so it needs OAuth. That's the next step for this feature (still free, needs an Entra app registration).

```powershell
cd src/SmartInbox.Api
dotnet user-secrets set "Mailbox:Enabled" "true"
dotnet user-secrets set "Mailbox:Host" "imap.gmail.com"
dotnet user-secrets set "Mailbox:Username" "correspondence@example.com"
dotnet user-secrets set "Mailbox:Password" "<app password>"
```

## See it working

**Administration → Shared mailbox** (admins):
- whether automatic checks are on, the mailbox, server and folders, last and next check with the result, last error
- **Test connection** (signs in and counts waiting mail; changes nothing) and **Check now**
- **Recent emails**: sender, subject, result, links to the documents created, reasons, and **Retry** for failed ones

Also on **Integrations** (status card). API: `GET /api/mailbox/status`, `POST /api/mailbox/test`, `POST /api/mailbox/check`, `GET /api/mailbox/imports`, `POST /api/mailbox/imports/{id}/retry` (admin, signed in); `GET /api/documents/{id}/source-email`; `?source=Email` on the document list. Webhook `document.received` carries `source`; SDK 0.4.0 and MCP `list_documents` can filter by it.

## Try it with Docker

```powershell
# .env
MAILBOX_ENABLED=true
MAILBOX_HOST=greenmail
MAILBOX_PORT=3143
MAILBOX_SECURITY=None
MAILBOX_USERNAME=correspondence@mds.demo
MAILBOX_PASSWORD=demo
```

```powershell
docker compose --profile mailbox up -d
Send-MailMessage -SmtpServer localhost -Port 3025 -From sender@partner.gov -To correspondence@mds.demo `
  -Subject "Court notice" -Body "Please find the notice attached." -Attachments samples\en-04-court-notice.txt
```

Within a minute the letter is in the inbox, analysed as Legal, with ✉ and its cover email. (GreenMail is a free test mail server that accepts any login; `Send-MailMessage` is fine for this local test.)

## Safety

- **Same rules as uploads:** allowed file types by extension, 20 MB per file, content type never taken from the email
- **Never twice:** the email's Message-Id is recorded together with its documents, and only then is the email moved; after a crash or restart it's recognised and just moved
- **Skipped:** auto-replies and bounces, senders outside `AllowedSenders`, empty emails — all logged with the reason
- **Nothing runs:** only text is extracted; HTML is reduced to text; the original email downloads as a file (never shown inline)
- **Prompt injection:** email text is analysed like any letter, and shown to Claude (MCP) under `fromDocument`, as untrusted
- **Permissions:** the original email is visible to whoever can see the document; mailbox settings and logs are admin-only (signed in, not API keys)
- **Credentials** stay in configuration secrets; errors never include them

## Troubleshooting

| Message | Fix |
|---|---|
| *Sign-in failed* | Wrong username/password; Gmail and Outlook.com need an **app password** |
| *Secure connection failed* | Port and security don't match: 993 = `SslOnConnect`, 143 = `StartTls` |
| *Can't reach the mail server* | Host name, firewall, or VPN |
| *Folder not found* | Check `Mailbox:Folder` (names are case-sensitive on some servers) |
| An email stays *Failed* | Read the reason in Recent emails, fix it, then **Retry** |
| Emails aren't imported automatically | `Mailbox:Enabled` must be `true` (Check now works without it) |

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using SmartInbox.Application;

namespace SmartInbox.Infrastructure.Mailbox;

/// <summary>
/// The shared mailbox over IMAP (MailKit). One connection per check: opened on first use, closed when the scope ends.
/// Processed messages are moved to <see cref="MailboxOptions.ProcessedFolder"/> (created if missing), or flagged when
/// no folder is configured.
/// </summary>
public sealed class ImapMailboxClient(MailboxOptions options) : IMailboxClient, IAsyncDisposable
{
    /// <summary>Marks processed messages when they are left in place (no processed folder).</summary>
    public const string ProcessedKeyword = "SmartInboxImported";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private ImapClient? _client;
    private IMailFolder? _folder;

    public async Task<IReadOnlyList<MailboxItem>> ListPendingAsync(int max, CancellationToken ct)
    {
        var folder = await FolderAsync(ct);
        var query = string.IsNullOrWhiteSpace(options.ProcessedFolder)
            ? SearchQuery.Not(SearchQuery.HasKeyword(ProcessedKeyword))
            : SearchQuery.All;
        var uids = (await folder.SearchAsync(query, ct)).OrderBy(u => u.Id).Take(max).ToList();
        if (uids.Count == 0) return [];

        var summaries = await folder.FetchAsync(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope, ct);
        return summaries.OrderBy(s => s.UniqueId.Id)
            .Select(s => new MailboxItem(s.UniqueId.Id, Normalize(s.Envelope?.MessageId)))
            .ToList();
    }

    public async Task<MailMessage> FetchAsync(uint uid, CancellationToken ct)
    {
        var folder = await FolderAsync(ct);
        var message = await folder.GetMessageAsync(new UniqueId(uid), ct);
        return Parse(uid, message);
    }

    public async Task MarkProcessedAsync(uint uid, CancellationToken ct)
    {
        var folder = await FolderAsync(ct);
        var id = new UniqueId(uid);
        if (string.IsNullOrWhiteSpace(options.ProcessedFolder))
        {
            await folder.StoreAsync(id, new StoreFlagsRequest(StoreAction.Add, MessageFlags.Seen) { Keywords = { ProcessedKeyword } }, ct);
            return;
        }
        var target = await ProcessedFolderAsync(ct);
        await folder.MoveToAsync(id, target, ct);
    }

    public async Task<MailboxTestResult> TestAsync(CancellationToken ct)
    {
        try
        {
            var folder = await FolderAsync(ct);
            var pending = (await ListPendingAsync(1000, ct)).Count;
            return new MailboxTestResult(true, pending, $"Connected to {options.Host} as {options.Username}; {folder.FullName} has {folder.Count} message(s), {pending} waiting.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new MailboxTestResult(false, null, Describe(ex));
        }
    }

    /// <summary>A failure message that helps an admin, without credentials.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        AuthenticationException => "Sign-in failed: check the username and password (Gmail and Outlook.com need an app password).",
        SslHandshakeException => "Secure connection failed: check Mailbox:Port and Mailbox:Security (993 = SslOnConnect, 143 = StartTls).",
        System.Net.Sockets.SocketException s => $"Can't reach the mail server: {s.Message}",
        FolderNotFoundException f => $"Folder not found: {f.FolderName}",
        InvalidOperationException { Message: var m } => m,
        _ => ex.Message,
    };

    public static MailMessage Parse(uint uid, MimeMessage message)
    {
        // Read before writing: serialising must never stand in for the sender's own Message-Id.
        var originalId = Normalize(message.MessageId);
        using var raw = new MemoryStream();
        message.WriteTo(raw);
        var bytes = raw.ToArray();

        var from = message.From.Mailboxes.FirstOrDefault();
        var attachments = new List<MailAttachment>();
        foreach (var entity in message.Attachments)
        {
            switch (entity)
            {
                case MessagePart { Message: { } forwarded }: // an email attached to the email
                    using (var s = new MemoryStream())
                    {
                        forwarded.WriteTo(s);
                        attachments.Add(new MailAttachment($"{FileSafe(forwarded.Subject ?? "attached email")}.eml", s.ToArray()));
                    }
                    break;
                case MimePart part when part.Content is not null:
                    using (var s = new MemoryStream())
                    {
                        part.Content.DecodeTo(s);
                        attachments.Add(new MailAttachment(part.FileName ?? part.ContentType.Name ?? "attachment", s.ToArray()));
                    }
                    break;
            }
        }

        var messageId = originalId ?? DerivedId(from?.Address, message.Date, message.Subject, bytes.Length);
        return new MailMessage(
            uid, messageId, from?.Name, from?.Address, message.Subject,
            message.Date == DateTimeOffset.MinValue ? null : message.Date,
            message.TextBody ?? HtmlToText(message.HtmlBody),
            IsAutoReply(message), attachments, bytes,
            Normalize(message.InReplyTo), message.References.Select(Normalize).OfType<string>().ToList());
    }

    /// <summary>Out-of-office replies, bounces and other machine-generated mail (RFC 3834 and common conventions).</summary>
    public static bool IsAutoReply(MimeMessage m)
    {
        var autoSubmitted = m.Headers["Auto-Submitted"];
        if (!string.IsNullOrEmpty(autoSubmitted) && !autoSubmitted.Trim().Equals("no", StringComparison.OrdinalIgnoreCase)) return true;
        if (m.Headers.Contains("X-Autoreply") || m.Headers.Contains("X-Autorespond")) return true;
        var precedence = m.Headers["Precedence"];
        if (precedence is not null && Regex.IsMatch(precedence, "^(bulk|junk|auto_reply|list)$", RegexOptions.IgnoreCase)) return true;
        return m.Body is MultipartReport;
    }

    private async Task<IMailFolder> FolderAsync(CancellationToken ct)
    {
        if (_folder is { IsOpen: true }) return _folder;
        if (string.IsNullOrWhiteSpace(options.Host)) throw new InvalidOperationException("The mailbox isn't configured: set Mailbox:Host, Mailbox:Username and Mailbox:Password.");

        _client = new ImapClient { Timeout = (int)Timeout.TotalMilliseconds };
        await _client.ConnectAsync(options.Host, options.Port, Security(options.Security), ct);
        if (!string.IsNullOrEmpty(options.Username)) await _client.AuthenticateAsync(options.Username, options.Password ?? "", ct);

        _folder = (options.Folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? _client.Inbox : await _client.GetFolderAsync(options.Folder, ct))
                  ?? throw new InvalidOperationException($"Folder {options.Folder} isn't available.");
        await _folder.OpenAsync(FolderAccess.ReadWrite, ct);
        return _folder;
    }

    private async Task<IMailFolder> ProcessedFolderAsync(CancellationToken ct)
    {
        var name = options.ProcessedFolder!;
        try
        {
            return await _client!.GetFolderAsync(name, ct);
        }
        catch (FolderNotFoundException)
        {
            var root = _client!.GetFolder(_client.PersonalNamespaces[0]);
            return await root.CreateAsync(name, true, ct)
                   ?? throw new InvalidOperationException($"The mail server didn't create the folder {name}.");
        }
    }

    private static SecureSocketOptions Security(string value) => value.Trim().ToLowerInvariant() switch
    {
        "none" => SecureSocketOptions.None,
        "starttls" => SecureSocketOptions.StartTls,
        "sslonconnect" => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto,
    };

    private static string? Normalize(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : id.Trim().StartsWith('<') ? id.Trim() : $"<{id.Trim()}>";

    /// <summary>For emails without a Message-Id: the same email always gets the same id.</summary>
    private static string DerivedId(string? from, DateTimeOffset date, string? subject, int size)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{from}|{date:O}|{subject}|{size}"));
        return $"<{Convert.ToHexString(hash)[..32].ToLowerInvariant()}@smartinbox.derived>";
    }

    private static string? HtmlToText(string? html) =>
        html is null ? null : Regex.Replace(Regex.Replace(html, "<(br|/p|/div)[^>]*>", "\n", RegexOptions.IgnoreCase), "<[^>]+>", " ").Trim();

    private static string FileSafe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '-').ToArray()).Trim();

    public async ValueTask DisposeAsync()
    {
        if (_client is null) return;
        try
        {
            if (_client.IsConnected) await _client.DisconnectAsync(true);
        }
        catch (Exception)
        {
            // Closing a connection that's already broken: nothing to do.
        }
        _client.Dispose();
    }
}

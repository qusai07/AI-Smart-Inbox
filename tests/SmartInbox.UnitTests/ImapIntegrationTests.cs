using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SmartInbox.Application;
using SmartInbox.Infrastructure.Mailbox;

namespace SmartInbox.UnitTests;

/// <summary>
/// Runs only against a real test mail server (GreenMail): set SMARTINBOX_TEST_IMAP_HOST (CI does).
/// Locally: docker run -p 3025:3025 -p 3143:3143 greenmail/standalone:2.1.14
/// </summary>
public sealed class ImapFactAttribute : FactAttribute
{
    public static readonly string? Host = Environment.GetEnvironmentVariable("SMARTINBOX_TEST_IMAP_HOST");

    public ImapFactAttribute()
    {
        if (string.IsNullOrEmpty(Host)) Skip = "No test mail server (set SMARTINBOX_TEST_IMAP_HOST to run against GreenMail).";
    }
}

public class ImapIntegrationTests
{
    private const int SmtpPort = 3025;
    private const int ImapPort = 3143;

    /// <summary>A fresh mailbox per test: GreenMail creates users on first delivery / sign-in.</summary>
    private static string NewAddress() => $"inbox-{Guid.NewGuid():N}@mds.demo";

    private static async Task SendAsync(string to, string subject, params (string Name, byte[] Content)[] attachments)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Partner Authority", "sender@partner.gov"));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        var body = new BodyBuilder { TextBody = "Please find our letter attached." };
        foreach (var (name, content) in attachments) body.Attachments.Add(name, content);
        message.Body = body.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(ImapFactAttribute.Host!, SmtpPort, SecureSocketOptions.None);
        await smtp.SendAsync(message);
        await smtp.DisconnectAsync(true);
    }

    private static MailboxOptions Options(string address, string folder = "INBOX", int port = ImapPort) => new()
    {
        Host = ImapFactAttribute.Host, Port = port, Security = "None", Username = address, Password = address, Folder = folder, ProcessedFolder = "Processed",
    };

    private static async Task<IReadOnlyList<MailboxItem>> WaitForMailAsync(ImapMailboxClient client, int expected)
    {
        for (var i = 0; ; i++)
        {
            var items = await client.ListPendingAsync(10, default);
            if (items.Count >= expected || i == 20) return items;
            await Task.Delay(250);
        }
    }

    [ImapFact]
    public async Task ListsFetchesAndMovesToProcessed()
    {
        var address = NewAddress();
        await SendAsync(address, "Court notice", ("notice.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 notice")));

        await using (var client = new ImapMailboxClient(Options(address)))
        {
            var item = Assert.Single(await WaitForMailAsync(client, 1));
            Assert.NotNull(item.MessageId);

            var message = await client.FetchAsync(item.Uid, default);
            Assert.Equal("Court notice", message.Subject);
            Assert.Equal("sender@partner.gov", message.FromAddress);
            Assert.Equal(item.MessageId, message.MessageId);
            Assert.Equal("notice.pdf", Assert.Single(message.Attachments).FileName);

            await client.MarkProcessedAsync(item.Uid, default);
            Assert.Empty(await client.ListPendingAsync(10, default));
        }

        // The message now lives in the Processed folder (created on first use).
        await using var processed = new ImapMailboxClient(Options(address, folder: "Processed"));
        Assert.Single(await processed.ListPendingAsync(10, default));
    }

    [ImapFact]
    public async Task TestConnection_ReportsWaitingMail_AndBadServersClearly()
    {
        var address = NewAddress();
        await SendAsync(address, "One");
        await using var client = new ImapMailboxClient(Options(address));
        await WaitForMailAsync(client, 1);

        var ok = await client.TestAsync(default);
        Assert.True(ok.Ok, ok.Message);
        Assert.Equal(1, ok.Pending);

        await using var wrongPort = new ImapMailboxClient(Options(address, port: 1));
        var bad = await wrongPort.TestAsync(default);
        Assert.False(bad.Ok);
        Assert.Contains("Can't reach the mail server", bad.Message);
    }
}

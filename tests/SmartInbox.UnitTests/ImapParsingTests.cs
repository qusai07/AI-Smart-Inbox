using System.Text;
using MimeKit;
using SmartInbox.Infrastructure.Mailbox;

namespace SmartInbox.UnitTests;

/// <summary>Turning real MIME emails into what the importer works with, without a mail server.</summary>
public class ImapParsingTests
{
    private static MimeMessage Email(string? messageId = "<abc@partner.gov>", Action<BodyBuilder>? body = null, Action<MimeMessage>? headers = null)
    {
        var m = new MimeMessage();
        m.From.Add(new MailboxAddress("Partner Authority", "sender@partner.gov"));
        m.To.Add(new MailboxAddress("Correspondence", "correspondence@mds.demo"));
        m.Subject = "Court notice 2027/118";
        m.Date = new DateTimeOffset(2027, 2, 10, 9, 0, 0, TimeSpan.FromHours(4));
        if (messageId is not null) m.MessageId = messageId.Trim('<', '>');
        else m.Headers.Remove(HeaderId.MessageId); // MimeKit gives new messages one; real ones can lack it
        var builder = new BodyBuilder { TextBody = "Please find the notice attached." };
        body?.Invoke(builder);
        m.Body = builder.ToMessageBody();
        headers?.Invoke(m);
        return m;
    }

    [Fact]
    public void Parse_ReadsSenderSubjectBodyAndAttachments()
    {
        var m = Email(body: b =>
        {
            b.Attachments.Add("notice.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 notice"), new ContentType("application", "pdf"));
            b.Attachments.Add("figures.xlsx", [1, 2, 3]);
        });

        var parsed = ImapMailboxClient.Parse(7, m);

        Assert.Equal(7u, parsed.Uid);
        Assert.Equal("<abc@partner.gov>", parsed.MessageId);
        Assert.Equal("Partner Authority", parsed.FromName);
        Assert.Equal("sender@partner.gov", parsed.FromAddress);
        Assert.Equal("Court notice 2027/118", parsed.Subject);
        Assert.Equal("Please find the notice attached.", parsed.BodyText?.Trim());
        Assert.False(parsed.IsAutoReply);
        Assert.Equal(["notice.pdf", "figures.xlsx"], parsed.Attachments.Select(a => a.FileName));
        Assert.Equal("%PDF-1.4 notice", Encoding.ASCII.GetString(parsed.Attachments[0].Content));
        Assert.Contains("Court notice 2027/118", Encoding.UTF8.GetString(parsed.Raw));
    }

    [Fact]
    public void Parse_AForwardedEmail_BecomesAnEmlAttachment()
    {
        var inner = Email("<inner@partner.gov>");
        inner.Subject = "Original letter";
        var m = Email(body: b => b.Attachments.Add(new MessagePart { Message = inner, ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) }));

        var parsed = ImapMailboxClient.Parse(1, m);

        var eml = Assert.Single(parsed.Attachments);
        Assert.Equal("Original letter.eml", eml.FileName);
        Assert.Contains("Original letter", Encoding.UTF8.GetString(eml.Content));
    }

    [Fact]
    public void Parse_HtmlOnlyEmail_GetsPlainText()
    {
        var m = Email(body: b =>
        {
            b.TextBody = null;
            b.HtmlBody = "<p>Dear team,</p><p>Please <b>review</b> the contract.</p>";
        });

        var text = ImapMailboxClient.Parse(1, m).BodyText!;

        Assert.Contains("Dear team,", text);
        Assert.Contains("review", text);
        Assert.DoesNotContain("<b>", text);
    }

    [Fact]
    public void Parse_WithoutMessageId_DerivesAStableOne()
    {
        var first = ImapMailboxClient.Parse(1, Email(messageId: null));
        var again = ImapMailboxClient.Parse(2, Email(messageId: null));

        Assert.EndsWith("@smartinbox.derived>", first.MessageId);
        Assert.Equal(first.MessageId, again.MessageId);
    }

    [Theory]
    [InlineData("Auto-Submitted", "auto-replied", true)]
    [InlineData("Auto-Submitted", "no", false)]
    [InlineData("X-Autoreply", "yes", true)]
    [InlineData("Precedence", "bulk", true)]
    [InlineData("Precedence", "first-class", false)]
    public void AutoReplies_AreRecognised(string header, string value, bool expected) =>
        Assert.Equal(expected, ImapMailboxClient.IsAutoReply(Email(headers: m => m.Headers.Add(header, value))));

    [Fact]
    public void DeliveryReports_AreRecognised()
    {
        var m = Email();
        m.Body = new MultipartReport("delivery-status") { new TextPart("plain") { Text = "Delivery failed" } };
        Assert.True(ImapMailboxClient.IsAutoReply(m));
    }
}

using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SmartInbox.Application;

namespace SmartInbox.Infrastructure.Email;

public class EmailOptions
{
    /// <summary>When false, notifications stay queued (Pending) and are sent once email is enabled.</summary>
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 2525;
    /// <summary>None | StartTls | SslOnConnect | Auto</summary>
    public string Security { get; set; } = "None";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "smart-inbox@mds.demo";
    public string FromName { get; set; } = "Smart Inbox";
}

public class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions _o = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_o.FromName, _o.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();

        using var smtp = new SmtpClient();
        var security = Enum.TryParse<SecureSocketOptions>(_o.Security, true, out var s) ? s : SecureSocketOptions.Auto;
        await smtp.ConnectAsync(_o.Host, _o.Port, security, ct);
        if (!string.IsNullOrEmpty(_o.Username))
            await smtp.AuthenticateAsync(_o.Username, _o.Password ?? "", ct);
        await smtp.SendAsync(mime, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}

/// <summary>Polls the notification outbox and delivers due emails.</summary>
public class NotificationWorker(IServiceScopeFactory scopes, IOptions<EmailOptions> options, ILogger<NotificationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Email notifications are disabled (Email:Enabled=false); routing emails will stay queued.");
            return;
        }

        logger.LogInformation("Email notifications via {Host}:{Port}", options.Value.Host, options.Value.Port);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification dispatch failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

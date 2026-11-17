using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Vroo.Services;

// Transactional email over SMTP (adapted from Mynt). Config: Email:Smtp:Host/Port/Username/Password,
// Email:From, Email:FromName. Without SMTP settings the link is logged instead, which is what dev uses.
public class EmailService(IConfiguration config, ILogger<EmailService> logger)
{
    public Task SendVerificationAsync(string toEmail, string link) => SendAsync(toEmail,
        "Bekräfta din e-postadress – vroo",
        Card("Bekräfta e-post",
            "Klicka på knappen för att bekräfta din e-postadress. Sedan loggar du in med lösenordet du valde. Länken är giltig i 24 timmar.",
            "Bekräfta e-postadress", link,
            "Om du inte skapade ett konto kan du ignorera mailet."),
        link);

    public Task SendPasswordResetAsync(string toEmail, string link) => SendAsync(toEmail,
        "Återställ ditt lösenord – vroo",
        Card("Lösenordsåterställning",
            "Klicka på knappen för att välja lösenord till ditt vroo-konto. Länken är giltig i 1 timme.",
            "Välj nytt lösenord", link,
            "Om du inte bad om detta kan du ignorera mailet."),
        link);

    private static string Card(string preheader, string paragraph, string buttonLabel, string link, string footer) => $"""
        <div style="font-family:sans-serif;max-width:480px;margin:0 auto;padding:32px 24px;background:#fff;border:1px solid #e8e8e8;border-radius:8px">
          <p style="font-size:20px;font-weight:700;margin:0 0 4px">vroo</p>
          <p style="font-size:12px;color:#888;margin:0 0 24px">{preheader}</p>
          <p style="font-size:14px;color:#333">{paragraph}</p>
          <a href="{link}" style="display:inline-block;margin:20px 0;padding:10px 24px;background:#111;color:#fff;border-radius:4px;text-decoration:none;font-size:13px;font-weight:600">{buttonLabel}</a>
          <p style="font-size:11px;color:#aaa;margin-top:24px">{footer}</p>
        </div>
        """;

    private async Task SendAsync(string toEmail, string subject, string html, string link)
    {
        var c = config.GetSection("Email");
        var host = c["Smtp:Host"];
        var user = c["Smtp:Username"];
        var pass = c["Smtp:Password"];
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            logger.LogWarning("Email not configured — {Subject} for {To}: {Link}", subject, toEmail, link);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(c["FromName"] ?? "vroo", c["From"] ?? user));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = html };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(host, int.Parse(c["Smtp:Port"] ?? "465"), SecureSocketOptions.Auto);
        await smtp.AuthenticateAsync(user, pass);
        await smtp.SendAsync(message);
        await smtp.DisconnectAsync(true);
    }
}

using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using WordApp.Auth;

namespace WordApp.Services;

public class AccountEmailOptions
{
    public bool Enabled { get; set; }
    public string PublicBaseUrl { get; set; } = "https://lexiflow.duckdns.org/";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";

    public bool ValidOrigin => Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/";
}

public interface IAccountEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string to, string subject, string text, CancellationToken cancellation);
}

public class AccountEmailSender(IOptions<AccountEmailOptions> settings) : IAccountEmailSender
{
    private readonly AccountEmailOptions _options = settings.Value;
    public bool IsConfigured => _options.ValidOrigin && !string.IsNullOrWhiteSpace(_options.Host)
        && _options.Port is 587 or 465 && !string.IsNullOrEmpty(_options.Username)
        && !string.IsNullOrEmpty(_options.Password) && RecoveryEmail.TryParse(_options.From, out _, out _);

    public async Task SendAsync(string to, string subject, string text, CancellationToken cancellation)
    {
        if (!IsConfigured) throw new InvalidOperationException("Account email is not configured.");
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("LexiFlow", _options.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = text };
        using var client = new SmtpClient(); // No protocol logger or TLS validation bypass.
        client.Timeout = 20000;
        await client.ConnectAsync(_options.Host, _options.Port,
            _options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancellation);
        await client.AuthenticateAsync(_options.Username, _options.Password, cancellation);
        await client.SendAsync(message, cancellation);
        await client.DisconnectAsync(true, cancellation);
    }
}

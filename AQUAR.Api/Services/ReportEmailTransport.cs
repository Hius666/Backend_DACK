using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AQUAR.Api;

// Not a record: do not generate a ToString containing SMTP credentials.
public sealed class SmtpSettings
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required MailboxAddress From { get; init; }
    public string? User { get; init; }
    public string? Password { get; init; }
    public SecureSocketOptions Security { get; init; }
    public int TimeoutSeconds { get; init; }

    public static SmtpSettings Read(IConfiguration configuration)
    {
        var host = configuration["Smtp:Host"]?.Trim();
        var from = configuration["Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new ApiException(503, "SMTP chưa cấu hình Host và From. Liên hệ quản trị viên.");
        if (host.Any(char.IsWhiteSpace) || host.Contains('/') || host.Contains(':') ||
            !MailboxAddress.TryParse(from, out var sender) ||
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(sender.Address))
            throw new ApiException(503, "Cấu hình Host hoặc From SMTP không hợp lệ.");
        if (!int.TryParse(configuration["Smtp:Port"] ?? "587", out var port) || port is < 1 or > 65535)
            throw new ApiException(503, "Cổng SMTP không hợp lệ.");
        if (!int.TryParse(configuration["Smtp:TimeoutSeconds"] ?? "20", out var timeout) || timeout is < 1 or > 120)
            throw new ApiException(503, "Timeout SMTP phải từ 1 đến 120 giây.");
        var security = configuration["Smtp:Security"] ?? (port == 465 ? "SslOnConnect" : "StartTls");
        var mode = security.ToLowerInvariant() switch {
            "starttls" => SecureSocketOptions.StartTls,
            "sslonconnect" => SecureSocketOptions.SslOnConnect,
            _ => throw new ApiException(503, "SMTP chỉ cho phép StartTls hoặc SslOnConnect.")
        };
        var user = configuration["Smtp:User"];
        var password = configuration["Smtp:Password"];
        if (string.IsNullOrWhiteSpace(user) != string.IsNullOrEmpty(password))
            throw new ApiException(503, "Cấu hình tài khoản SMTP cần đủ User và Password.");
        return new SmtpSettings { Host = host, Port = port, From = sender, User = user,
            Password = password, Security = mode, TimeoutSeconds = timeout };
    }
}

public interface IReportEmailTransport
{
    Task Send(MimeMessage message, SmtpSettings settings, CancellationToken cancellationToken);
}

public sealed class ReportEmailTransport : IReportEmailTransport
{
    public async Task Send(MimeMessage message, SmtpSettings settings, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
        // Keep MailKit's default certificate verification. Never downgrade to plaintext.
        await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.User))
            await client.AuthenticateAsync(settings.User, settings.Password!, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        // SMTP already accepted the message; a failed QUIT must not encourage duplicate delivery.
        try { await client.DisconnectAsync(true, cancellationToken); }
        catch (Exception) { /* Dispose closes the connection; do not re-send. */ }
    }
}

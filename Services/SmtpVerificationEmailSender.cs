using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;

namespace MaximoROWeb.Services;

public interface IVerificationEmailSender
{
    bool IsConfigured { get; }

    Task<EmailDeliveryResult> SendVerificationAsync(
        string email,
        string username,
        string token,
        CancellationToken cancellationToken = default);
}

public sealed record EmailDeliveryResult(bool Succeeded, string? Error)
{
    public static EmailDeliveryResult Success() => new(true, null);

    public static EmailDeliveryResult Failure(string error) =>
        new(false, error);
}

public sealed class SmtpVerificationEmailSender : IVerificationEmailSender
{
    private readonly IOptionsMonitor<EmailVerificationOptions> _options;
    private readonly ILogger<SmtpVerificationEmailSender> _logger;

    public SmtpVerificationEmailSender(
        IOptionsMonitor<EmailVerificationOptions> options,
        ILogger<SmtpVerificationEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsConfigured =>
        TryGetConfiguration(out _, out _, out _);

    public async Task<EmailDeliveryResult> SendVerificationAsync(
        string email,
        string username,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetConfiguration(
                out var options,
                out var publicBaseUri,
                out var fromAddress))
        {
            return EmailDeliveryResult.Failure(
                "Email delivery is not configured.");
        }

        try
        {
            var verificationUri = new Uri(
                publicBaseUri,
                $"account/verify-email?token={Uri.EscapeDataString(token)}");

            var encodedUsername = HtmlEncoder.Default.Encode(username);
            var encodedUrl = HtmlEncoder.Default.Encode(
                verificationUri.AbsoluteUri);

            using var message = new MailMessage
            {
                From = new MailAddress(
                    fromAddress,
                    options.Smtp.FromName),
                Subject = "Verify your MaximoRO account",
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = true,
                Body = $"""
                    <!doctype html>
                    <html lang="en">
                    <head>
                        <meta charset="utf-8">
                        <meta name="viewport" content="width=device-width, initial-scale=1">
                        <title>Verify your MaximoRO account</title>
                    </head>
                    <body style="margin:0;background:#0b0f16;color:#e9e4db;font-family:Arial,sans-serif;">
                        <div style="max-width:620px;margin:0 auto;padding:38px 22px;">
                            <div style="border:1px solid #5f5036;background:#111722;padding:32px;">
                                <p style="margin:0 0 10px;color:#d5ad5d;font-size:12px;font-weight:700;text-transform:uppercase;">MaximoRO Account Security</p>
                                <h1 style="margin:0 0 18px;color:#f2d796;font-size:26px;">Verify your email</h1>
                                <p style="margin:0 0 16px;line-height:1.65;">Hello {encodedUsername},</p>
                                <p style="margin:0 0 24px;line-height:1.65;">Confirm this email address to unlock your new MaximoRO game account.</p>
                                <p style="margin:0 0 26px;">
                                    <a href="{encodedUrl}" style="display:inline-block;padding:13px 20px;background:#d5ad5d;color:#161108;font-weight:700;text-decoration:none;">Verify Account</a>
                                </p>
                                <p style="margin:0 0 10px;color:#a8a195;font-size:13px;line-height:1.6;">This link expires in {GetTokenLifetimeMinutes(options)} minutes. If you did not create this account, you can ignore this email.</p>
                                <p style="margin:0;color:#777d87;font-size:12px;line-height:1.6;word-break:break-all;">Or open: {encodedUrl}</p>
                            </div>
                        </div>
                    </body>
                    </html>
                    """
            };

            message.To.Add(new MailAddress(email));

            using var smtpClient = new SmtpClient(
                options.Smtp.Host,
                options.Smtp.Port)
            {
                EnableSsl = options.Smtp.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            if (!string.IsNullOrWhiteSpace(options.Smtp.Username))
            {
                smtpClient.Credentials = new NetworkCredential(
                    options.Smtp.Username,
                    options.Smtp.Password);
            }

            await smtpClient.SendMailAsync(message, cancellationToken);
            return EmailDeliveryResult.Success();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is SmtpException
                  or InvalidOperationException
                  or FormatException)
        {
            _logger.LogError(
                exception,
                "Failed to send an account verification email.");

            return EmailDeliveryResult.Failure(
                "The verification email could not be delivered.");
        }
    }

    private bool TryGetConfiguration(
        out EmailVerificationOptions options,
        out Uri publicBaseUri,
        out string fromAddress)
    {
        options = _options.CurrentValue;
        publicBaseUri = null!;
        fromAddress = string.Empty;

        if (!options.Smtp.Enabled
            || string.IsNullOrWhiteSpace(options.Smtp.Host)
            || options.Smtp.Port is < 1 or > 65535
            || string.IsNullOrWhiteSpace(options.Smtp.FromAddress)
            || !TryGetPublicBaseUri(options.PublicBaseUrl, out publicBaseUri))
        {
            return false;
        }

        var hasUsername = !string.IsNullOrWhiteSpace(options.Smtp.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(options.Smtp.Password);

        if (hasUsername != hasPassword)
        {
            return false;
        }

        try
        {
            fromAddress = new MailAddress(
                options.Smtp.FromAddress).Address;

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryGetPublicBaseUri(
        string configuredUrl,
        out Uri publicBaseUri)
    {
        publicBaseUri = null!;

        if (!Uri.TryCreate(
                configuredUrl.TrimEnd('/') + "/",
                UriKind.Absolute,
                out var parsedUri)
            || (parsedUri.Scheme != Uri.UriSchemeHttps
                && parsedUri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        publicBaseUri = parsedUri;
        return true;
    }

    private static int GetTokenLifetimeMinutes(
        EmailVerificationOptions options) =>
        Math.Clamp(options.TokenLifetimeMinutes, 15, 10_080);
}

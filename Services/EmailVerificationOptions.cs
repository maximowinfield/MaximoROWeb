namespace MaximoROWeb.Services;

public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";

    public int TokenLifetimeMinutes { get; set; } = 1440;

    public int ResendCooldownSeconds { get; set; } = 60;

    public string PublicBaseUrl { get; set; } = string.Empty;

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "MaximoRO";
}

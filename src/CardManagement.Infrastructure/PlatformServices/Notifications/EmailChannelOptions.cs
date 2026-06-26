namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Configuration options for the Email (SMTP) notification channel.
/// </summary>
public sealed class EmailChannelOptions
{
    public const string SectionName = "Notifications:Email";

    /// <summary>
    /// SMTP server host.
    /// </summary>
    public string SmtpHost { get; set; } = "localhost";

    /// <summary>
    /// SMTP server port.
    /// </summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    /// Whether to use TLS/SSL for the SMTP connection.
    /// </summary>
    public bool UseTls { get; set; } = true;

    /// <summary>
    /// SMTP authentication username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// SMTP authentication password.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// The sender email address used in the "From" header.
    /// </summary>
    public string FromAddress { get; set; } = "noreply@cardmanagement.com";

    /// <summary>
    /// The sender display name.
    /// </summary>
    public string FromName { get; set; } = "CardManagement";

    /// <summary>
    /// Timeout in seconds for SMTP operations.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

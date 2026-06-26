namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Configuration options for the WhatsApp (WAHA) notification channel.
/// Uses the self-hosted WAHA API for sending WhatsApp messages.
/// </summary>
public sealed class WhatsAppChannelOptions
{
    public const string SectionName = "Notifications:WhatsApp";

    /// <summary>
    /// Base URL for the WAHA API (e.g., "http://waha:3000").
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authenticating with the WAHA API (if configured).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The WAHA session name to use for sending messages.
    /// </summary>
    public string Session { get; set; } = "default";

    /// <summary>
    /// HTTP timeout in seconds for WAHA API calls.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to perform a session health check on startup.
    /// </summary>
    public bool HealthCheckOnStartup { get; set; } = true;
}

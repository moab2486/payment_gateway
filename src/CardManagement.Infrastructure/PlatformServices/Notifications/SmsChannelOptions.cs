namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Configuration options for the SMS notification channel.
/// </summary>
public sealed class SmsChannelOptions
{
    public const string SectionName = "Notifications:Sms";

    /// <summary>
    /// Base URL for the SMS provider API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authenticating with the SMS provider.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Sender ID or short code displayed to recipients.
    /// </summary>
    public string SenderId { get; set; } = "CardMgmt";

    /// <summary>
    /// HTTP timeout in seconds for SMS API calls.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

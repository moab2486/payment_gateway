namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Configuration options for the NIBSS Instant Payment (NIP) adapter.
/// Bound from the "Nip" configuration section.
/// </summary>
public class NipOptions
{
    public const string SectionName = "Nip";

    /// <summary>
    /// Base URL for the NIBSS NIP API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authenticating with NIBSS NIP.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Timeout in milliseconds for the synchronous transfer request.
    /// </summary>
    public int TimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// Delay in milliseconds before initiating a status inquiry after timeout.
    /// </summary>
    public int StatusInquiryDelayMs { get; set; } = 2_000;
}

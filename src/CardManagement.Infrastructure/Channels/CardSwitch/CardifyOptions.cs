namespace CardManagement.Infrastructure.Channels.CardSwitch;

/// <summary>
/// Configuration options for connecting to the Cardify card switch (Visa/Mastercard).
/// </summary>
public class CardifyOptions
{
    public const string SectionName = "Cardify";

    /// <summary>
    /// The hostname or IP address of the Cardify switch endpoint.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// The TCP port number for the Cardify switch endpoint.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Timeout in milliseconds for waiting on a response from Cardify.
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Delay in milliseconds before attempting to reconnect after a disconnection.
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 5000;

    /// <summary>
    /// Whether TLS is enabled for the connection to the Cardify switch.
    /// </summary>
    public bool TlsEnabled { get; set; } = true;

    /// <summary>
    /// The terminal ID assigned to this institution by Cardify.
    /// </summary>
    public string TerminalId { get; set; } = string.Empty;

    /// <summary>
    /// The merchant ID assigned to this institution by Cardify.
    /// </summary>
    public string MerchantId { get; set; } = string.Empty;
}

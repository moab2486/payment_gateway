namespace CardManagement.Infrastructure.Channels.CardSwitch;

/// <summary>
/// Configuration options for connecting to the Interswitch card switch.
/// </summary>
public class InterswitchOptions
{
    public const string SectionName = "Interswitch";

    /// <summary>
    /// The hostname or IP address of the Interswitch switch endpoint.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// The TCP port number for the Interswitch switch endpoint.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Timeout in milliseconds for waiting on a response from Interswitch.
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Delay in milliseconds before attempting to reconnect after a disconnection.
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 5000;

    /// <summary>
    /// Whether TLS is enabled for the connection to the Interswitch switch.
    /// </summary>
    public bool TlsEnabled { get; set; } = true;

    /// <summary>
    /// The terminal ID assigned to this institution by Interswitch.
    /// </summary>
    public string TerminalId { get; set; } = string.Empty;

    /// <summary>
    /// The merchant ID assigned to this institution by Interswitch.
    /// </summary>
    public string MerchantId { get; set; } = string.Empty;
}

namespace CardManagement.Infrastructure.Tcp;

/// <summary>
/// Configuration options for the TCP listener service.
/// Values are read from environment variables with the TCP__ prefix.
/// </summary>
public class TcpListenerOptions
{
    /// <summary>
    /// The TCP port to listen on for inbound card processor connections.
    /// Environment variable: TCP__LISTENER_PORT
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Maximum number of concurrent TCP connections allowed.
    /// Environment variable: TCP__MAX_CONNECTIONS
    /// </summary>
    public int MaxConnections { get; set; } = 100;

    /// <summary>
    /// Read timeout in seconds. If no complete frame is received within this period,
    /// the connection is closed and a read-timeout event is logged.
    /// Environment variable: TCP__READ_TIMEOUT_SECONDS
    /// Default: 30 seconds.
    /// </summary>
    public int ReadTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum allowed frame size in bytes. Frames exceeding this size are rejected
    /// and the connection is closed gracefully.
    /// Environment variable: TCP__MAX_FRAME_SIZE
    /// Default: 9999 bytes.
    /// </summary>
    public int MaxFrameSize { get; set; } = 9999;
}

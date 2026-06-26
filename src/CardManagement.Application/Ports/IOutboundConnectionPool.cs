using System.IO.Pipelines;
using CardManagement.Application.DTOs;

namespace CardManagement.Application.Ports;

/// <summary>
/// Represents a leased bidirectional connection from the outbound connection pool.
/// Provides both read and write access to the underlying TCP/TLS stream.
/// Must be returned to the pool after use.
/// </summary>
public interface IPooledConnection : IAsyncDisposable
{
    /// <summary>
    /// The underlying stream for reading and writing data.
    /// This may be a plain NetworkStream or an SslStream depending on TLS configuration.
    /// </summary>
    Stream Stream { get; }

    /// <summary>
    /// Whether the underlying connection is still alive.
    /// </summary>
    bool IsConnected { get; }
}

/// <summary>
/// Port interface for managing a pool of persistent outbound TCP connections
/// to card processor endpoints. Supports connection reuse and graceful
/// recovery with exponential backoff on disconnect.
/// </summary>
public interface IOutboundConnectionPool
{
    /// <summary>
    /// Retrieves a PipeWriter for sending data to the specified processor endpoint.
    /// May reuse an existing connection or establish a new one from the pool.
    /// </summary>
    /// <param name="endpoint">The target processor endpoint.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A PipeWriter connected to the processor endpoint.</returns>
    Task<PipeWriter> GetWriterAsync(ProcessorEndpoint endpoint, CancellationToken ct);

    /// <summary>
    /// Returns a PipeWriter back to the pool for reuse after a response has been sent.
    /// </summary>
    /// <param name="endpoint">The processor endpoint the writer belongs to.</param>
    /// <param name="writer">The PipeWriter to return to the pool.</param>
    Task ReturnAsync(ProcessorEndpoint endpoint, PipeWriter writer);

    /// <summary>
    /// Acquires a bidirectional pooled connection to the specified processor endpoint.
    /// Supports both reading and writing over a persistent TCP/TLS connection.
    /// The connection is automatically reconnected with exponential backoff if disconnected.
    /// </summary>
    /// <param name="endpoint">The target processor endpoint.</param>
    /// <param name="useTls">Whether to establish a TLS-secured connection.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A pooled connection that provides bidirectional stream access.</returns>
    Task<IPooledConnection> AcquireConnectionAsync(ProcessorEndpoint endpoint, bool useTls, CancellationToken ct);

    /// <summary>
    /// Returns a pooled connection back to the pool for reuse.
    /// If the connection is no longer healthy, it will be discarded and a new one created on next acquire.
    /// </summary>
    /// <param name="endpoint">The processor endpoint the connection belongs to.</param>
    /// <param name="connection">The pooled connection to return.</param>
    Task ReleaseConnectionAsync(ProcessorEndpoint endpoint, IPooledConnection connection);
}

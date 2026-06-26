using System.IO.Pipelines;

namespace CardManagement.Application.Ports;

/// <summary>
/// Port interface for handling inbound TCP connections from card processor switches.
/// Implementations read length-prefixed message frames using System.IO.Pipelines
/// and dispatch them to the ISO 8583 gateway for processing.
/// </summary>
public interface ITcpConnectionHandler
{
    /// <summary>
    /// Handles an established TCP connection by reading ISO 8583 message frames
    /// and writing response frames back to the client.
    /// </summary>
    /// <param name="reader">The pipe reader for receiving data from the connection.</param>
    /// <param name="writer">The pipe writer for sending data to the connection.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A task that completes when the connection is closed or an error occurs.</returns>
    Task HandleConnectionAsync(PipeReader reader, PipeWriter writer, CancellationToken ct);
}

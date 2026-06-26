using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using CardManagement.Application.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.Tcp;

/// <summary>
/// Background service that listens for inbound TCP connections from card processor switches.
/// Uses System.IO.Pipelines for high-performance, zero-copy I/O and implements the 2-byte
/// big-endian length prefix framing protocol for ISO 8583 messages.
/// </summary>
public class TcpListenerService : BackgroundService
{
    private readonly ITcpConnectionHandler _connectionHandler;
    private readonly ILogger<TcpListenerService> _logger;
    private readonly TcpListenerOptions _options;
    private readonly SemaphoreSlim _connectionSemaphore;
    private readonly ConcurrentDictionary<string, TcpClient> _activeConnections = new();
    private TcpListener? _listener;

    public TcpListenerService(
        ITcpConnectionHandler connectionHandler,
        IConfiguration configuration,
        ILogger<TcpListenerService> logger)
    {
        _connectionHandler = connectionHandler;
        _logger = logger;
        _options = BindOptions(configuration);
        _connectionSemaphore = new SemaphoreSlim(_options.MaxConnections, _options.MaxConnections);
    }

    /// <summary>
    /// The configured options for this TCP listener instance. Exposed for testing.
    /// </summary>
    internal TcpListenerOptions Options => _options;

    /// <summary>
    /// The number of currently active connections.
    /// </summary>
    public int ActiveConnectionCount => _activeConnections.Count;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new TcpListener(IPAddress.Any, _options.Port);
        _listener.Start();

        _logger.LogInformation(
            "TCP Listener started on port {Port}. Max connections: {MaxConnections}, Read timeout: {ReadTimeoutSeconds}s, Max frame size: {MaxFrameSize} bytes",
            _options.Port, _options.MaxConnections, _options.ReadTimeoutSeconds, _options.MaxFrameSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait for a connection slot to become available
                await _connectionSemaphore.WaitAsync(stoppingToken);

                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(stoppingToken);
                    var connectionId = Guid.NewGuid().ToString("N");

                    _activeConnections.TryAdd(connectionId, client);

                    var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                    _logger.LogInformation(
                        "TCP connection accepted from {RemoteEndpoint}. Connection ID: {ConnectionId}. Active connections: {ActiveCount}",
                        remoteEndpoint, connectionId, _activeConnections.Count);

                    // Handle the connection in the background without blocking the accept loop
                    _ = HandleConnectionAsync(client, connectionId, remoteEndpoint, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _connectionSemaphore.Release();
                    break;
                }
                catch (Exception ex)
                {
                    _connectionSemaphore.Release();
                    _logger.LogError(ex, "Error accepting TCP connection");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        finally
        {
            _listener.Stop();
            _logger.LogInformation("TCP Listener stopped");
        }
    }

    private async Task HandleConnectionAsync(
        TcpClient client,
        string connectionId,
        string remoteEndpoint,
        CancellationToken stoppingToken)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var pipe = new Pipe();

                // Create PipeReader from the network stream
                var pipeReader = PipeReader.Create(stream);
                var pipeWriter = PipeWriter.Create(stream);

                try
                {
                    // Read frames in a loop until timeout, disconnect, or cancellation
                    await ProcessConnectionAsync(pipeReader, pipeWriter, remoteEndpoint, stoppingToken);
                }
                finally
                {
                    await pipeReader.CompleteAsync();
                    await pipeWriter.CompleteAsync();
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                "Unexpected disconnect from {RemoteEndpoint}. Connection ID: {ConnectionId}. Error: {Error}",
                remoteEndpoint, connectionId, ex.Message);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(
                "Unexpected disconnect from {RemoteEndpoint}. Connection ID: {ConnectionId}. SocketError: {SocketError}",
                remoteEndpoint, connectionId, ex.SocketErrorCode);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Connection {ConnectionId} from {RemoteEndpoint} closed due to service shutdown",
                connectionId, remoteEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error handling connection {ConnectionId} from {RemoteEndpoint}",
                connectionId, remoteEndpoint);
        }
        finally
        {
            _activeConnections.TryRemove(connectionId, out _);
            _connectionSemaphore.Release();

            _logger.LogDebug(
                "Connection {ConnectionId} from {RemoteEndpoint} cleaned up. Active connections: {ActiveCount}",
                connectionId, remoteEndpoint, _activeConnections.Count);
        }
    }

    private async Task ProcessConnectionAsync(
        PipeReader pipeReader,
        PipeWriter pipeWriter,
        string remoteEndpoint,
        CancellationToken stoppingToken)
    {
        var readTimeout = TimeSpan.FromSeconds(_options.ReadTimeoutSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var frameResult = await FrameReader.ReadFrameAsync(
                pipeReader, _options.MaxFrameSize, readTimeout, stoppingToken);

            switch (frameResult.Status)
            {
                case FrameReadStatus.Success:
                    // Delegate to the connection handler via a pipe
                    await DelegateToHandlerAsync(frameResult.Payload!, pipeWriter, stoppingToken);
                    break;

                case FrameReadStatus.OversizedFrame:
                    _logger.LogWarning(
                        "Oversized frame received from {RemoteEndpoint}. Max allowed: {MaxFrameSize} bytes. Closing connection",
                        remoteEndpoint, _options.MaxFrameSize);
                    return; // Close connection gracefully

                case FrameReadStatus.Timeout:
                    _logger.LogWarning(
                        "Read timeout ({TimeoutSeconds}s) for connection from {RemoteEndpoint}. No complete frame received. Closing connection",
                        _options.ReadTimeoutSeconds, remoteEndpoint);
                    return; // Close connection

                case FrameReadStatus.ConnectionClosed:
                    _logger.LogInformation(
                        "Connection from {RemoteEndpoint} closed by remote endpoint",
                        remoteEndpoint);
                    return;
            }
        }
    }

    private async Task DelegateToHandlerAsync(
        byte[] payload,
        PipeWriter networkWriter,
        CancellationToken ct)
    {
        // Create an in-memory pipe to pass the framed message to the handler
        var requestPipe = new Pipe();
        var responsePipe = new Pipe();

        // Write the payload to the request pipe for the handler to read
        await requestPipe.Writer.WriteAsync(payload, ct);
        await requestPipe.Writer.CompleteAsync();

        // Let the handler process the message and produce a response
        await _connectionHandler.HandleConnectionAsync(requestPipe.Reader, responsePipe.Writer, ct);
        await responsePipe.Writer.CompleteAsync();

        // Read the response from the handler and write it as a framed message to the network
        var responseResult = await responsePipe.Reader.ReadAsync(ct);
        if (responseResult.Buffer.Length > 0)
        {
            var responsePayload = responseResult.Buffer.ToArray();
            responsePipe.Reader.AdvanceTo(responseResult.Buffer.End);
            await FrameReader.WriteFrameAsync(networkWriter, responsePayload, ct);
        }
        else
        {
            responsePipe.Reader.AdvanceTo(responseResult.Buffer.End);
        }

        await responsePipe.Reader.CompleteAsync();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TCP Listener shutting down. Closing {ActiveCount} active connections", _activeConnections.Count);

        // Close all active connections
        foreach (var kvp in _activeConnections)
        {
            try
            {
                kvp.Value.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error closing connection {ConnectionId} during shutdown", kvp.Key);
            }
        }

        _activeConnections.Clear();

        await base.StopAsync(cancellationToken);
    }

    private static TcpListenerOptions BindOptions(IConfiguration configuration)
    {
        var options = new TcpListenerOptions();

        var port = configuration["TCP__LISTENER_PORT"];
        if (!string.IsNullOrEmpty(port) && int.TryParse(port, out var portValue))
        {
            options.Port = portValue;
        }

        var maxConnections = configuration["TCP__MAX_CONNECTIONS"];
        if (!string.IsNullOrEmpty(maxConnections) && int.TryParse(maxConnections, out var maxConnsValue))
        {
            options.MaxConnections = maxConnsValue;
        }

        var readTimeout = configuration["TCP__READ_TIMEOUT_SECONDS"];
        if (!string.IsNullOrEmpty(readTimeout) && int.TryParse(readTimeout, out var timeoutValue))
        {
            options.ReadTimeoutSeconds = timeoutValue;
        }

        var maxFrameSize = configuration["TCP__MAX_FRAME_SIZE"];
        if (!string.IsNullOrEmpty(maxFrameSize) && int.TryParse(maxFrameSize, out var frameSizeValue))
        {
            options.MaxFrameSize = frameSizeValue;
        }

        return options;
    }

}

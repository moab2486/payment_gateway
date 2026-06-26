using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using CardManagement.Application.Ports;
using CardManagement.Infrastructure.Tcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for TcpListenerService verifying connection acceptance, timeout handling,
/// oversized frame rejection, and graceful disconnect behavior.
/// </summary>
public class TcpListenerServiceTests : IAsyncLifetime
{
    private TcpListenerService? _service;
    private CancellationTokenSource? _cts;
    private int _port;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        if (_service != null)
        {
            await _service.StopAsync(CancellationToken.None);
            _service.Dispose();
        }
    }

    private TcpListenerService CreateService(
        ITcpConnectionHandler? handler = null,
        int? port = null,
        int maxConnections = 10,
        int readTimeoutSeconds = 30,
        int maxFrameSize = 9999)
    {
        _port = port ?? GetFreePort();
        handler ??= new NoOpConnectionHandler();

        var configValues = new Dictionary<string, string?>
        {
            ["TCP__LISTENER_PORT"] = _port.ToString(),
            ["TCP__MAX_CONNECTIONS"] = maxConnections.ToString(),
            ["TCP__READ_TIMEOUT_SECONDS"] = readTimeoutSeconds.ToString(),
            ["TCP__MAX_FRAME_SIZE"] = maxFrameSize.ToString()
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var logger = NullLogger<TcpListenerService>.Instance;

        return new TcpListenerService(handler, configuration, logger);
    }

    private async Task StartServiceAsync(TcpListenerService service)
    {
        _cts = new CancellationTokenSource();
        await service.StartAsync(_cts.Token);
        // Give the listener a moment to start accepting connections
        await Task.Delay(100);
    }

    [Fact]
    public async Task ServiceStartsAndListens_OnConfiguredPort()
    {
        // Arrange & Act
        _service = CreateService();
        await StartServiceAsync(_service);

        // Assert - can connect to the configured port
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        Assert.True(client.Connected);
    }

    [Fact]
    public async Task AcceptsConnection_WhenClientConnects()
    {
        // Arrange
        _service = CreateService();
        await StartServiceAsync(_service);

        // Act
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        await Task.Delay(100); // Allow accept loop to process

        // Assert
        Assert.True(client.Connected);
        Assert.True(_service.ActiveConnectionCount >= 1);
    }

    [Fact]
    public async Task AcceptsMultipleConcurrentConnections()
    {
        // Arrange
        _service = CreateService(maxConnections: 5);
        await StartServiceAsync(_service);

        var clients = new List<TcpClient>();

        // Act - connect 3 clients
        for (int i = 0; i < 3; i++)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            clients.Add(client);
        }

        await Task.Delay(200);

        // Assert
        Assert.True(_service.ActiveConnectionCount >= 3);

        // Cleanup
        foreach (var client in clients)
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task ClosesConnection_WhenReadTimeoutExpires()
    {
        // Arrange - 1 second timeout for fast test
        _service = CreateService(readTimeoutSeconds: 1);
        await StartServiceAsync(_service);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);

        // Act - Don't send any data, wait for timeout
        await Task.Delay(2000);

        // Assert - Connection should be closed by server
        Assert.Equal(0, _service.ActiveConnectionCount);
    }

    [Fact]
    public async Task ClosesConnection_WhenOversizedFrameReceived()
    {
        // Arrange - max frame size 100 bytes
        _service = CreateService(maxFrameSize: 100);
        await StartServiceAsync(_service);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        var stream = client.GetStream();

        // Act - Send a frame declaring 200 bytes (exceeds max of 100)
        var header = new byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(header, 200);
        await stream.WriteAsync(header);
        await stream.FlushAsync();

        await Task.Delay(500);

        // Assert - Connection should be closed by server
        Assert.Equal(0, _service.ActiveConnectionCount);
    }

    [Fact]
    public async Task ProcessesValidFrame_ViaConnectionHandler()
    {
        // Arrange
        var handler = new EchoConnectionHandler();
        _service = CreateService(handler: handler);
        await StartServiceAsync(_service);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        var stream = client.GetStream();

        // Act - Send a valid frame
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var frame = FrameReader.EncodeFrame(payload);
        await stream.WriteAsync(frame);
        await stream.FlushAsync();

        // Read response - wait for response with timeout
        var responseBuffer = new byte[1024];
        stream.ReadTimeout = 5000;
        var bytesRead = await stream.ReadAsync(responseBuffer);

        // Assert - Should get back a framed response
        Assert.True(bytesRead > 2); // At least length prefix + some data
    }

    [Fact]
    public async Task ReleasesResources_WhenClientDisconnectsUnexpectedly()
    {
        // Arrange
        _service = CreateService(readTimeoutSeconds: 30);
        await StartServiceAsync(_service);

        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        await Task.Delay(100);

        Assert.True(_service.ActiveConnectionCount >= 1);

        // Act - Abruptly close the connection
        client.Client.LingerState = new LingerOption(true, 0); // Force RST
        client.Dispose();

        await Task.Delay(500);

        // Assert - Resources should be released
        Assert.Equal(0, _service.ActiveConnectionCount);
    }

    [Fact]
    public async Task StopAsync_ClosesAllActiveConnections()
    {
        // Arrange
        _service = CreateService();
        await StartServiceAsync(_service);

        using var client1 = new TcpClient();
        using var client2 = new TcpClient();
        await client1.ConnectAsync(IPAddress.Loopback, _port);
        await client2.ConnectAsync(IPAddress.Loopback, _port);
        await Task.Delay(200);

        // Act
        await _service.StopAsync(CancellationToken.None);
        await Task.Delay(200);

        // Assert
        Assert.Equal(0, _service.ActiveConnectionCount);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// A no-op connection handler that does nothing (simulates slow/no response).
    /// Used for testing timeout and connection management behavior.
    /// </summary>
    private class NoOpConnectionHandler : ITcpConnectionHandler
    {
        public async Task HandleConnectionAsync(PipeReader reader, PipeWriter writer, CancellationToken ct)
        {
            // Read everything but don't respond
            try
            {
                var readResult = await reader.ReadAsync(ct);
                reader.AdvanceTo(readResult.Buffer.End);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }
    }

    /// <summary>
    /// An echo connection handler that reads the payload and writes it back.
    /// Used for testing successful frame processing.
    /// </summary>
    private class EchoConnectionHandler : ITcpConnectionHandler
    {
        public async Task HandleConnectionAsync(PipeReader reader, PipeWriter writer, CancellationToken ct)
        {
            var readResult = await reader.ReadAsync(ct);
            var buffer = readResult.Buffer;
            var payload = new byte[buffer.Length];
            buffer.CopyTo(payload);
            reader.AdvanceTo(buffer.End);

            // Echo back the payload
            await writer.WriteAsync(payload, ct);
            await writer.CompleteAsync();
        }
    }
}

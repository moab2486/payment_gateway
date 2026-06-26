using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using CardManagement.Application.DTOs;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Networking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class OutboundConnectionPoolTests : IAsyncLifetime
{
    private TcpListener? _testServer;
    private int _serverPort;
    private OutboundConnectionPool? _pool;
    private readonly CancellationTokenSource _cts = new();

    public Task InitializeAsync()
    {
        // Start a local TCP server for testing
        _testServer = new TcpListener(IPAddress.Loopback, 0);
        _testServer.Start();
        _serverPort = ((IPEndPoint)_testServer.LocalEndpoint).Port;

        // Accept connections in the background
        _ = AcceptConnectionsAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TCP__POOL_SIZE_PER_ENDPOINT"] = "3"
            })
            .Build();

        _pool = new OutboundConnectionPool(
            NullLogger<OutboundConnectionPool>.Instance,
            config);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        if (_pool != null)
            await _pool.DisposeAsync();
        _testServer?.Stop();
    }

    private async Task AcceptConnectionsAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _testServer!.AcceptTcpClientAsync(_cts.Token);
                // Keep accepting connections
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during cleanup
        }
        catch (ObjectDisposedException)
        {
            // Expected during cleanup
        }
    }

    private ProcessorEndpoint CreateEndpoint() => new()
    {
        Address = "127.0.0.1",
        Port = _serverPort,
        ProcessorType = ProcessorType.Interswitch
    };

    [Fact]
    public async Task GetWriterAsync_ReturnsValidPipeWriter()
    {
        var endpoint = CreateEndpoint();
        var writer = await _pool!.GetWriterAsync(endpoint, CancellationToken.None);

        Assert.NotNull(writer);
    }

    [Fact]
    public async Task GetWriterAsync_MultipleCallsReturnDifferentWriters()
    {
        var endpoint = CreateEndpoint();

        var writer1 = await _pool!.GetWriterAsync(endpoint, CancellationToken.None);
        var writer2 = await _pool!.GetWriterAsync(endpoint, CancellationToken.None);

        Assert.NotNull(writer1);
        Assert.NotNull(writer2);
        Assert.NotSame(writer1, writer2);
    }

    [Fact]
    public async Task ReturnAsync_AllowsConnectionReuse()
    {
        var endpoint = CreateEndpoint();

        var writer1 = await _pool!.GetWriterAsync(endpoint, CancellationToken.None);
        await _pool.ReturnAsync(endpoint, writer1);

        // After returning, the same writer should be reusable
        var writer2 = await _pool!.GetWriterAsync(endpoint, CancellationToken.None);
        Assert.NotNull(writer2);
    }

    [Fact]
    public async Task GetWriterAsync_RespectsPoolSizeLimit()
    {
        var endpoint = CreateEndpoint();

        // Pool size is 3, so acquire all 3
        var writer1 = await _pool!.GetWriterAsync(endpoint, CancellationToken.None);
        var writer2 = await _pool!.GetWriterAsync(endpoint, CancellationToken.None);
        var writer3 = await _pool!.GetWriterAsync(endpoint, CancellationToken.None);

        // Fourth call should block (we use a short timeout to prove it)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _pool.GetWriterAsync(endpoint, timeoutCts.Token));
    }

    [Fact]
    public async Task GetWriterAsync_CancellationToken_ThrowsWhenCancelled()
    {
        var endpoint = new ProcessorEndpoint
        {
            Address = "192.0.2.1", // Non-routable address to force timeout
            Port = 9999,
            ProcessorType = ProcessorType.CardFi
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _pool!.GetWriterAsync(endpoint, cts.Token));
    }

    [Fact]
    public async Task DisposeAsync_PreventsNewAcquisitions()
    {
        var endpoint = CreateEndpoint();
        await _pool!.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _pool.GetWriterAsync(endpoint, CancellationToken.None));
    }

    [Fact]
    public async Task GetWriterAsync_DifferentEndpoints_UsesSeparatePools()
    {
        var endpoint1 = new ProcessorEndpoint
        {
            Address = "127.0.0.1",
            Port = _serverPort,
            ProcessorType = ProcessorType.Interswitch
        };
        var endpoint2 = new ProcessorEndpoint
        {
            Address = "127.0.0.1",
            Port = _serverPort,
            ProcessorType = ProcessorType.CardFi
        };

        var writer1 = await _pool!.GetWriterAsync(endpoint1, CancellationToken.None);
        var writer2 = await _pool!.GetWriterAsync(endpoint2, CancellationToken.None);

        Assert.NotNull(writer1);
        Assert.NotNull(writer2);
        Assert.NotSame(writer1, writer2);
    }
}

using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CardManagement.Infrastructure.Networking;

/// <summary>
/// Maintains a pool of persistent outbound TCP connections to card processor endpoints.
/// Connections are reused via a ConcurrentDictionary keyed by endpoint.
/// Uses SemaphoreSlim to limit pool size per endpoint and exponential backoff
/// reconnection (1s, 2s, 4s, ..., up to 60s) on disconnect.
/// Supports both plain TCP and TLS-secured connections.
/// </summary>
public sealed class OutboundConnectionPool : IOutboundConnectionPool, IAsyncDisposable
{
    private readonly ConcurrentDictionary<ProcessorEndpoint, EndpointPool> _pools = new();
    private readonly ConcurrentDictionary<string, BidirectionalEndpointPool> _bidirectionalPools = new();
    private readonly ILogger<OutboundConnectionPool> _logger;
    private readonly int _defaultPoolSize;
    private readonly IConfiguration _configuration;
    private bool _disposed;

    private const int MinBackoffSeconds = 1;
    private const int MaxBackoffSeconds = 60;

    public OutboundConnectionPool(
        ILogger<OutboundConnectionPool> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _defaultPoolSize = configuration.GetValue("TCP__POOL_SIZE_PER_ENDPOINT", 5);
    }

    public async Task<PipeWriter> GetWriterAsync(ProcessorEndpoint endpoint, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var pool = _pools.GetOrAdd(endpoint, ep => new EndpointPool(
            ep,
            GetPoolSizeForEndpoint(ep),
            _logger));

        return await pool.AcquireWriterAsync(ct);
    }

    public Task ReturnAsync(ProcessorEndpoint endpoint, PipeWriter writer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pools.TryGetValue(endpoint, out var pool))
        {
            pool.ReleaseWriter(writer);
        }

        return Task.CompletedTask;
    }

    public async Task<IPooledConnection> AcquireConnectionAsync(
        ProcessorEndpoint endpoint, bool useTls, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var poolKey = $"{endpoint.Address}:{endpoint.Port}:{endpoint.ProcessorType}:{useTls}";
        var pool = _bidirectionalPools.GetOrAdd(poolKey, _ => new BidirectionalEndpointPool(
            endpoint,
            useTls,
            GetPoolSizeForEndpoint(endpoint),
            _logger));

        return await pool.AcquireAsync(ct);
    }

    public Task ReleaseConnectionAsync(ProcessorEndpoint endpoint, IPooledConnection connection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var poolKey = $"{endpoint.Address}:{endpoint.Port}:{endpoint.ProcessorType}:{connection is TlsPooledConnection}";

        // Try both TLS and non-TLS pool keys since the caller may not know
        if (!_bidirectionalPools.TryGetValue(poolKey, out var pool))
        {
            // Try alternate key
            var altTls = !(connection is TlsPooledConnection);
            var altKey = $"{endpoint.Address}:{endpoint.Port}:{endpoint.ProcessorType}:{altTls}";
            _bidirectionalPools.TryGetValue(altKey, out pool);
        }

        pool?.Release(connection);
        return Task.CompletedTask;
    }

    private int GetPoolSizeForEndpoint(ProcessorEndpoint endpoint)
    {
        var configKey = $"TCP__POOL_SIZE_{endpoint.ProcessorType.ToString().ToUpperInvariant()}";
        return _configuration.GetValue(configKey, _defaultPoolSize);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var kvp in _pools)
        {
            await kvp.Value.DisposeAsync();
        }
        _pools.Clear();

        foreach (var kvp in _bidirectionalPools)
        {
            await kvp.Value.DisposeAsync();
        }
        _bidirectionalPools.Clear();
    }

    /// <summary>
    /// Manages the bidirectional connection pool for a single processor endpoint.
    /// Supports both plain TCP and TLS connections with automatic reconnection.
    /// </summary>
    private sealed class BidirectionalEndpointPool : IAsyncDisposable
    {
        private readonly ProcessorEndpoint _endpoint;
        private readonly bool _useTls;
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentBag<IPooledConnection> _available = new();
        private readonly ConcurrentDictionary<IPooledConnection, bool> _inUse = new();
        private readonly ILogger _logger;
        private bool _disposed;

        public BidirectionalEndpointPool(
            ProcessorEndpoint endpoint, bool useTls, int poolSize, ILogger logger)
        {
            _endpoint = endpoint;
            _useTls = useTls;
            _semaphore = new SemaphoreSlim(poolSize, poolSize);
            _logger = logger;
        }

        public async Task<IPooledConnection> AcquireAsync(CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);

            try
            {
                // Try to reuse an existing connection
                while (_available.TryTake(out var pooled))
                {
                    if (pooled.IsConnected)
                    {
                        _inUse[pooled] = true;
                        return pooled;
                    }

                    // Connection is dead, dispose and try next
                    await pooled.DisposeAsync();
                }

                // Create a new connection with exponential backoff
                var connection = await ConnectWithBackoffAsync(ct);
                _inUse[connection] = true;
                return connection;
            }
            catch
            {
                _semaphore.Release();
                throw;
            }
        }

        public void Release(IPooledConnection connection)
        {
            if (_inUse.TryRemove(connection, out _))
            {
                if (!_disposed && connection.IsConnected)
                {
                    _available.Add(connection);
                }
                else
                {
                    _ = connection.DisposeAsync();
                }

                _semaphore.Release();
            }
        }

        private async Task<IPooledConnection> ConnectWithBackoffAsync(CancellationToken ct)
        {
            var backoffSeconds = MinBackoffSeconds;
            var attempt = 0;

            while (true)
            {
                attempt++;
                try
                {
                    var client = new TcpClient();
                    client.NoDelay = true;
                    client.ReceiveTimeout = 0; // Non-blocking reads handled at higher level
                    client.SendTimeout = 0;

                    await client.ConnectAsync(_endpoint.Address, _endpoint.Port, ct);

                    _logger.LogInformation(
                        "Connected to processor endpoint {ProcessorType} at {Address}:{Port} (attempt {Attempt}, TLS: {UseTls})",
                        _endpoint.ProcessorType, _endpoint.Address, _endpoint.Port, attempt, _useTls);

                    Stream stream = client.GetStream();

                    if (_useTls)
                    {
                        var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
                        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                        {
                            TargetHost = _endpoint.Address,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        }, ct);

                        _logger.LogInformation(
                            "TLS handshake completed with {ProcessorType} at {Address}:{Port}. Protocol: {Protocol}",
                            _endpoint.ProcessorType, _endpoint.Address, _endpoint.Port, sslStream.SslProtocol);

                        return new TlsPooledConnection(client, sslStream);
                    }

                    return new PlainPooledConnection(client, client.GetStream());
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to connect to processor endpoint {ProcessorType} at {Address}:{Port} (attempt {Attempt}, TLS: {UseTls}). " +
                        "Retrying in {BackoffSeconds}s",
                        _endpoint.ProcessorType, _endpoint.Address, _endpoint.Port, attempt, _useTls, backoffSeconds);

                    await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);

                    // Exponential backoff: 1, 2, 4, 8, 16, 32, 60 cap
                    backoffSeconds = Math.Min(backoffSeconds * 2, MaxBackoffSeconds);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (var kvp in _inUse)
            {
                await kvp.Key.DisposeAsync();
            }
            _inUse.Clear();

            while (_available.TryTake(out var pooled))
            {
                await pooled.DisposeAsync();
            }

            _semaphore.Dispose();
        }
    }

    /// <summary>
    /// Manages the connection pool for a single processor endpoint (write-only, legacy).
    /// </summary>
    private sealed class EndpointPool : IAsyncDisposable
    {
        private readonly ProcessorEndpoint _endpoint;
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentBag<PooledConnection> _available = new();
        private readonly ConcurrentDictionary<PipeWriter, PooledConnection> _inUse = new();
        private readonly ILogger _logger;
        private bool _disposed;

        public EndpointPool(ProcessorEndpoint endpoint, int poolSize, ILogger logger)
        {
            _endpoint = endpoint;
            _semaphore = new SemaphoreSlim(poolSize, poolSize);
            _logger = logger;
        }

        public async Task<PipeWriter> AcquireWriterAsync(CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);

            try
            {
                // Try to reuse an existing connection from the pool
                while (_available.TryTake(out var pooled))
                {
                    if (pooled.IsConnected)
                    {
                        _inUse[pooled.Writer] = pooled;
                        return pooled.Writer;
                    }

                    // Connection is dead, dispose and try next
                    await pooled.DisposeAsync();
                }

                // No reusable connection found; create a new one with backoff
                var connection = await ConnectWithBackoffAsync(_endpoint, ct);
                _inUse[connection.Writer] = connection;
                return connection.Writer;
            }
            catch
            {
                _semaphore.Release();
                throw;
            }
        }

        public void ReleaseWriter(PipeWriter writer)
        {
            if (_inUse.TryRemove(writer, out var pooled))
            {
                if (!_disposed && pooled.IsConnected)
                {
                    _available.Add(pooled);
                }
                else
                {
                    _ = pooled.DisposeAsync();
                }

                _semaphore.Release();
            }
        }

        private async Task<PooledConnection> ConnectWithBackoffAsync(
            ProcessorEndpoint endpoint, CancellationToken ct)
        {
            var backoffSeconds = MinBackoffSeconds;
            var attempt = 0;

            while (true)
            {
                attempt++;
                try
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(endpoint.Address, endpoint.Port, ct);

                    _logger.LogInformation(
                        "Connected to processor endpoint {ProcessorType} at {Address}:{Port} (attempt {Attempt})",
                        endpoint.ProcessorType, endpoint.Address, endpoint.Port, attempt);

                    var stream = client.GetStream();
                    var writer = PipeWriter.Create(stream);

                    return new PooledConnection(client, stream, writer);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to connect to processor endpoint {ProcessorType} at {Address}:{Port} (attempt {Attempt}). " +
                        "Retrying in {BackoffSeconds}s",
                        endpoint.ProcessorType, endpoint.Address, endpoint.Port, attempt, backoffSeconds);

                    await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);

                    // Exponential backoff: 1, 2, 4, 8, 16, 32, 60 cap
                    backoffSeconds = Math.Min(backoffSeconds * 2, MaxBackoffSeconds);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (var kvp in _inUse)
            {
                await kvp.Value.DisposeAsync();
            }
            _inUse.Clear();

            while (_available.TryTake(out var pooled))
            {
                await pooled.DisposeAsync();
            }

            _semaphore.Dispose();
        }
    }

    /// <summary>
    /// Represents a single pooled TCP connection with its associated PipeWriter (legacy).
    /// </summary>
    private sealed class PooledConnection : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;

        public PipeWriter Writer { get; }

        public bool IsConnected
        {
            get
            {
                try
                {
                    var socket = _client.Client;
                    if (socket == null || !_client.Connected)
                        return false;

                    // Poll with SelectRead: if true and Available == 0, the connection is closed
                    if (socket.Poll(0, SelectMode.SelectRead))
                    {
                        return socket.Available > 0;
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public PooledConnection(TcpClient client, NetworkStream stream, PipeWriter writer)
        {
            _client = client;
            _stream = stream;
            Writer = writer;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Writer.CompleteAsync();
            }
            catch
            {
                // Suppress errors during cleanup
            }

            try
            {
                _stream.Dispose();
            }
            catch
            {
                // Suppress errors during cleanup
            }

            try
            {
                _client.Dispose();
            }
            catch
            {
                // Suppress errors during cleanup
            }
        }
    }
}

/// <summary>
/// A pooled connection over a plain (non-TLS) TCP stream.
/// </summary>
internal sealed class PlainPooledConnection : IPooledConnection
{
    private readonly TcpClient _client;
    private readonly NetworkStream _networkStream;
    private bool _disposed;

    public Stream Stream => _networkStream;

    public bool IsConnected
    {
        get
        {
            if (_disposed) return false;
            try
            {
                var socket = _client.Client;
                if (socket == null || !_client.Connected)
                    return false;

                if (socket.Poll(0, SelectMode.SelectRead))
                {
                    return socket.Available > 0;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public PlainPooledConnection(TcpClient client, NetworkStream stream)
    {
        _client = client;
        _networkStream = stream;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        try { _networkStream.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A pooled connection over a TLS-secured stream.
/// </summary>
internal sealed class TlsPooledConnection : IPooledConnection
{
    private readonly TcpClient _client;
    private readonly SslStream _sslStream;
    private bool _disposed;

    public Stream Stream => _sslStream;

    public bool IsConnected
    {
        get
        {
            if (_disposed) return false;
            try
            {
                var socket = _client.Client;
                if (socket == null || !_client.Connected)
                    return false;

                if (socket.Poll(0, SelectMode.SelectRead))
                {
                    return socket.Available > 0;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public TlsPooledConnection(TcpClient client, SslStream sslStream)
    {
        _client = client;
        _sslStream = sslStream;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        try { _sslStream.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }

        return ValueTask.CompletedTask;
    }
}

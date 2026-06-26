using System.Collections.Concurrent;
using NSubstitute;
using StackExchange.Redis;

namespace CardManagement.PlatformServices.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory fake Redis implementation for integration testing.
/// Uses NSubstitute for IConnectionMultiplexer and IDatabase, backed by a
/// ConcurrentDictionary to simulate real Redis string operations.
/// </summary>
public sealed class FakeRedisConnectionMultiplexer
{
    private readonly ConcurrentDictionary<string, (RedisValue Value, DateTime? Expiry)> _store = new();
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;

    public FakeRedisConnectionMultiplexer()
    {
        _database = Substitute.For<IDatabase>();
        _multiplexer = Substitute.For<IConnectionMultiplexer>();

        _multiplexer.IsConnected.Returns(true);
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(_database);

        // StringGetAsync
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = ((RedisKey)callInfo[0]).ToString();
                if (_store.TryGetValue(key, out var entry))
                {
                    if (entry.Expiry.HasValue && DateTime.UtcNow > entry.Expiry.Value)
                    {
                        _store.TryRemove(key, out _);
                        return RedisValue.Null;
                    }
                    return entry.Value;
                }
                return RedisValue.Null;
            });

        // StringSetAsync (with optional TimeSpan? expiry, bool keepTtl, When, CommandFlags)
        _database.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(), Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = ((RedisKey)callInfo[0]).ToString();
                var value = (RedisValue)callInfo[1];
                var expiry = (TimeSpan?)callInfo[2];
                var expiryTime = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : (DateTime?)null;
                _store[key] = (value, expiryTime);
                return true;
            });

        // KeyDeleteAsync (single key)
        _database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = ((RedisKey)callInfo[0]).ToString();
                return _store.TryRemove(key, out _);
            });

        // KeyExistsAsync
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = ((RedisKey)callInfo[0]).ToString();
                return _store.ContainsKey(key);
            });
    }

    /// <summary>
    /// Exposes the backing store for test assertions.
    /// </summary>
    public ConcurrentDictionary<string, (RedisValue Value, DateTime? Expiry)> Store => _store;

    /// <summary>
    /// Returns the NSubstitute-backed IConnectionMultiplexer for injection.
    /// </summary>
    public IConnectionMultiplexer Multiplexer => _multiplexer;

}

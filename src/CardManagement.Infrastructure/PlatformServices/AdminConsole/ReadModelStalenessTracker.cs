using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole;

/// <summary>
/// Tracks the last projection timestamp for each read model via Redis.
/// Key format: readmodel:{name}:last_projected — value is a UTC ISO-8601 timestamp.
/// </summary>
public sealed class ReadModelStalenessTracker
{
    private const string KeyPrefix = "readmodel:";
    private const string KeySuffix = ":last_projected";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ReadModelStalenessTracker> _logger;

    public ReadModelStalenessTracker(
        IConnectionMultiplexer redis,
        ILogger<ReadModelStalenessTracker> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Records the current UTC time as the last projection time for a read model.
    /// </summary>
    public async Task RecordProjectionAsync(string readModelName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(readModelName))
            return;

        try
        {
            var db = _redis.GetDatabase();
            var key = FormatKey(readModelName);
            var value = DateTime.UtcNow.ToString("O");

            await db.StringSetAsync(key, value).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex,
                "Redis connection failed during staleness recording for read model '{ReadModelName}'.",
                readModelName);
        }
    }

    /// <summary>
    /// Gets the last projection time for a read model.
    /// Returns null if no projection has been recorded.
    /// </summary>
    public async Task<DateTime?> GetLastProjectedAtAsync(string readModelName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(readModelName))
            return null;

        try
        {
            var db = _redis.GetDatabase();
            var key = FormatKey(readModelName);
            var value = await db.StringGetAsync(key).ConfigureAwait(false);

            if (value.IsNullOrEmpty)
                return null;

            if (DateTime.TryParse(value!, out var result))
                return DateTime.SpecifyKind(result, DateTimeKind.Utc);

            return null;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex,
                "Redis connection failed during staleness lookup for read model '{ReadModelName}'.",
                readModelName);
            return null;
        }
    }

    /// <summary>
    /// Determines whether a read model is stale given a threshold.
    /// </summary>
    public async Task<(bool IsStale, TimeSpan? StaleDuration)> CheckStalenessAsync(
        string readModelName, TimeSpan threshold, CancellationToken ct)
    {
        var lastProjected = await GetLastProjectedAtAsync(readModelName, ct);

        if (lastProjected is null)
            return (true, null);

        var elapsed = DateTime.UtcNow - lastProjected.Value;

        if (elapsed > threshold)
            return (true, elapsed);

        return (false, null);
    }

    private static string FormatKey(string readModelName) =>
        $"{KeyPrefix}{readModelName}{KeySuffix}";
}

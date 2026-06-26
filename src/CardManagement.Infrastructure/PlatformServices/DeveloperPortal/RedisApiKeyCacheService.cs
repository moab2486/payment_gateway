using System.Text.Json;
using System.Text.Json.Serialization;
using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CardManagement.Infrastructure.PlatformServices.DeveloperPortal;

/// <summary>
/// Redis-backed implementation of IApiKeyCacheService.
/// Stores API key validation data with configurable TTL for fast lookup.
/// Key format: apikey:{sha256_hash}, value: JSON-serialized CachedKeyInfo.
/// </summary>
public class RedisApiKeyCacheService : IApiKeyCacheService
{
    private const string KeyPrefix = "apikey:";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisApiKeyCacheService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisApiKeyCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisApiKeyCacheService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <inheritdoc/>
    public async Task<CachedKeyInfo?> GetAsync(string apiKeyHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKeyHash))
            return null;

        try
        {
            var db = _redis.GetDatabase();
            var key = FormatKey(apiKeyHash);
            var value = await db.StringGetAsync(key).ConfigureAwait(false);

            if (value.IsNullOrEmpty)
                return null;

            return JsonSerializer.Deserialize<CachedKeyInfo>(value!, _jsonOptions);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection failed during cache lookup for key hash {KeyHashPrefix}. Returning cache miss.", apiKeyHash[..Math.Min(8, apiKeyHash.Length)]);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize cached key info for key hash {KeyHashPrefix}.", apiKeyHash[..Math.Min(8, apiKeyHash.Length)]);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync(string apiKeyHash, CachedKeyInfo info, TimeSpan ttl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKeyHash))
            throw new ArgumentException("API key hash is required.", nameof(apiKeyHash));

        if (info is null)
            throw new ArgumentNullException(nameof(info));

        if (ttl <= TimeSpan.Zero)
            throw new ArgumentException("TTL must be a positive duration.", nameof(ttl));

        try
        {
            var db = _redis.GetDatabase();
            var key = FormatKey(apiKeyHash);
            var json = JsonSerializer.Serialize(info, _jsonOptions);

            await db.StringSetAsync(key, json, ttl).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection failed during cache set for key hash {KeyHashPrefix}. Cache not populated.", apiKeyHash[..Math.Min(8, apiKeyHash.Length)]);
            // Do not throw — cache miss is non-fatal; the DB fallback will handle requests
        }
    }

    /// <inheritdoc/>
    public async Task InvalidateAsync(string apiKeyHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKeyHash))
            return;

        try
        {
            var db = _redis.GetDatabase();
            var key = FormatKey(apiKeyHash);

            await db.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection failed during cache invalidation for key hash {KeyHashPrefix}.", apiKeyHash[..Math.Min(8, apiKeyHash.Length)]);
            // Invalidation failure is logged but not thrown — the TTL will eventually expire the stale entry
        }
    }

    private static string FormatKey(string apiKeyHash) => $"{KeyPrefix}{apiKeyHash}";
}

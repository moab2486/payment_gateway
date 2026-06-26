using CardManagement.Domain.PlatformServices.DeveloperPortal;

namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Redis cache abstraction for API key validation data.
/// Provides fast lookup of key information without hitting the database on every request.
/// Cache key format: apikey:{sha256_hash}
/// </summary>
public interface IApiKeyCacheService
{
    Task<CachedKeyInfo?> GetAsync(string apiKeyHash, CancellationToken ct);
    Task SetAsync(string apiKeyHash, CachedKeyInfo info, TimeSpan ttl, CancellationToken ct);
    Task InvalidateAsync(string apiKeyHash, CancellationToken ct);
}

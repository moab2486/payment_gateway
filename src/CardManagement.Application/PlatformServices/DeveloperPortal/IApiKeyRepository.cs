using CardManagement.Domain.PlatformServices.DeveloperPortal;

namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Repository for persisting and querying API keys.
/// Only key hashes are stored — raw keys are never persisted.
/// </summary>
public interface IApiKeyRepository
{
    Task<ApiKey> CreateAsync(ApiKey key, CancellationToken ct);
    Task<ApiKey?> GetByIdAsync(Guid keyId, CancellationToken ct);
    Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken ct);
    Task<int> CountActiveByDeveloperAsync(Guid developerId, CancellationToken ct);
    Task UpdateAsync(ApiKey key, CancellationToken ct);
    Task<IReadOnlyList<ApiKey>> GetByDeveloperAsync(Guid developerId, CancellationToken ct);
}

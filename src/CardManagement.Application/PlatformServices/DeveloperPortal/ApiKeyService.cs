using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.DeveloperPortal;

namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Implementation of API key lifecycle management.
/// - Create: generates cryptographically random key, hashes with SHA-256, stores hash only, returns raw value once
/// - Rotate: creates new key with grace period for old key
/// - Revoke: immediately invalidates key and evicts from cache
/// - Validate: cache-first with DB fallback
/// </summary>
public class ApiKeyService : IApiKeyService
{
    private const int MaxActiveKeysPerDeveloper = 10;
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(15);

    private readonly IApiKeyRepository _repository;
    private readonly IApiKeyCacheService _cacheService;
    private readonly IAuditStore _auditStore;

    public ApiKeyService(
        IApiKeyRepository repository,
        IApiKeyCacheService cacheService,
        IAuditStore auditStore)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
    }

    /// <inheritdoc/>
    public async Task<ApiKeyCreateResult> CreateKeyAsync(Guid developerId, string[] scopes, CancellationToken ct)
    {
        if (developerId == Guid.Empty)
            throw new ArgumentException("Developer ID is required.", nameof(developerId));

        if (scopes is null || scopes.Length == 0)
            throw new ArgumentException("At least one scope is required.", nameof(scopes));

        var activeCount = await _repository.CountActiveByDeveloperAsync(developerId, ct);
        if (activeCount >= MaxActiveKeysPerDeveloper)
            throw new InvalidOperationException(
                $"Developer has reached the maximum of {MaxActiveKeysPerDeveloper} active API keys.");

        var apiKey = ApiKey.Create(developerId, scopes, isSandbox: false, expiresAtUtc: null, out var rawKey);
        await _repository.CreateAsync(apiKey, ct);

        await _auditStore.AppendAsync(AuditEntry.Create(
            transactionReference: apiKey.Id.ToString(),
            actorIdentity: developerId.ToString(),
            action: "ApiKey.Created",
            previousState: null,
            newState: $"prefix={apiKey.KeyPrefix};scopes=[{string.Join(",", scopes)}]",
            correlationId: apiKey.Id.ToString(),
            previousEntryHash: null), ct);

        return new ApiKeyCreateResult(apiKey.Id, rawKey, apiKey.KeyPrefix, scopes);
    }

    /// <inheritdoc/>
    public async Task<ApiKeyRotateResult> RotateKeyAsync(Guid keyId, TimeSpan gracePeriod, CancellationToken ct)
    {
        if (keyId == Guid.Empty)
            throw new ArgumentException("Key ID is required.", nameof(keyId));

        if (gracePeriod <= TimeSpan.Zero)
            throw new ArgumentException("Grace period must be positive.", nameof(gracePeriod));

        var existingKey = await _repository.GetByIdAsync(keyId, ct)
            ?? throw new InvalidOperationException($"API key '{keyId}' not found.");

        // Check active key limit before creating new key
        var activeCount = await _repository.CountActiveByDeveloperAsync(existingKey.DeveloperId, ct);
        if (activeCount >= MaxActiveKeysPerDeveloper)
            throw new InvalidOperationException(
                $"Developer has reached the maximum of {MaxActiveKeysPerDeveloper} active API keys.");

        // Mark existing key as rotated with grace period
        existingKey.Rotate(gracePeriod);
        await _repository.UpdateAsync(existingKey, ct);

        // Create new replacement key
        var newKey = ApiKey.Create(existingKey.DeveloperId, existingKey.Scopes, existingKey.IsSandbox, expiresAtUtc: null, out var newRawKey);
        await _repository.CreateAsync(newKey, ct);

        // Invalidate old key cache so the rotated status is picked up
        await _cacheService.InvalidateAsync(existingKey.KeyHash, ct);

        await _auditStore.AppendAsync(AuditEntry.Create(
            transactionReference: keyId.ToString(),
            actorIdentity: existingKey.DeveloperId.ToString(),
            action: "ApiKey.Rotated",
            previousState: $"prefix={existingKey.KeyPrefix};status=Active",
            newState: $"oldPrefix={existingKey.KeyPrefix};newPrefix={newKey.KeyPrefix};gracePeriodEnds={existingKey.GracePeriodEndsAtUtc:O}",
            correlationId: newKey.Id.ToString(),
            previousEntryHash: null), ct);

        return new ApiKeyRotateResult(newKey.Id, newRawKey, newKey.KeyPrefix, existingKey.GracePeriodEndsAtUtc!.Value);
    }

    /// <inheritdoc/>
    public async Task RevokeKeyAsync(Guid keyId, CancellationToken ct)
    {
        if (keyId == Guid.Empty)
            throw new ArgumentException("Key ID is required.", nameof(keyId));

        var apiKey = await _repository.GetByIdAsync(keyId, ct)
            ?? throw new InvalidOperationException($"API key '{keyId}' not found.");

        apiKey.Revoke();
        await _repository.UpdateAsync(apiKey, ct);

        // Immediate cache eviction ensures revoked keys cannot pass validation
        await _cacheService.InvalidateAsync(apiKey.KeyHash, ct);

        await _auditStore.AppendAsync(AuditEntry.Create(
            transactionReference: keyId.ToString(),
            actorIdentity: apiKey.DeveloperId.ToString(),
            action: "ApiKey.Revoked",
            previousState: $"prefix={apiKey.KeyPrefix};status=Active",
            newState: $"prefix={apiKey.KeyPrefix};status=Revoked",
            correlationId: keyId.ToString(),
            previousEntryHash: null), ct);
    }

    /// <inheritdoc/>
    public async Task<ApiKeyValidationResult> ValidateKeyAsync(string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return ApiKeyValidationResult.Invalid();

        // Hash the raw key using SHA-256
        var keyHash = ApiKey.ComputeHash(apiKey);

        // Cache-first: check Redis
        var cached = await _cacheService.GetAsync(keyHash, ct);
        if (cached is not null)
        {
            return EvaluateCachedKey(cached);
        }

        // Cache miss: query database
        var keyEntity = await _repository.GetByHashAsync(keyHash, ct);
        if (keyEntity is null)
            return ApiKeyValidationResult.Invalid();

        // Populate cache for future lookups
        var cacheInfo = new CachedKeyInfo(
            keyEntity.Id,
            keyEntity.DeveloperId,
            keyEntity.Scopes,
            keyEntity.Status,
            keyEntity.IsSandbox,
            keyEntity.ExpiresAtUtc);

        await _cacheService.SetAsync(keyHash, cacheInfo, DefaultCacheTtl, ct);

        if (!keyEntity.IsValid())
            return ApiKeyValidationResult.Invalid();

        return ApiKeyValidationResult.Valid(keyEntity.DeveloperId, keyEntity.Scopes, keyEntity.IsSandbox);
    }

    private static ApiKeyValidationResult EvaluateCachedKey(CachedKeyInfo cached)
    {
        if (cached.Status == KeyStatus.Revoked || cached.Status == KeyStatus.Expired)
            return ApiKeyValidationResult.Invalid();

        if (cached.Status == KeyStatus.Active)
        {
            if (cached.ExpiresAtUtc.HasValue && DateTime.UtcNow >= cached.ExpiresAtUtc.Value)
                return ApiKeyValidationResult.Invalid();

            return ApiKeyValidationResult.Valid(cached.DeveloperId, cached.Scopes, cached.IsSandbox);
        }

        // Rotated: valid only within grace period (grace period tracked on the entity;
        // for cache we accept Active or Rotated entries as valid if cached TTL hasn't expired)
        if (cached.Status == KeyStatus.Rotated)
        {
            // If it's in cache with Rotated status, the cache entry was populated while still in grace period
            return ApiKeyValidationResult.Valid(cached.DeveloperId, cached.Scopes, cached.IsSandbox);
        }

        return ApiKeyValidationResult.Invalid();
    }
}

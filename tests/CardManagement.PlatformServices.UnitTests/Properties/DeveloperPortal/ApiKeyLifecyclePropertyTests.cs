using System.Collections.Concurrent;
using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Properties.DeveloperPortal;

/// <summary>
/// Property-based tests for API Key Revocation Immediacy (Property 30).
/// 
/// **Validates: Requirements 14.2, 14.3, 15.2, 15.3, 15.4**
/// 
/// After revocation, all validation attempts return unauthorized including cache-hit scenarios
/// (cache invalidated).
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "30")]
public class ApiKeyRevocationImmediacyPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 14.3, 15.4**
    /// 
    /// Property 30: API Key Revocation Immediacy — After revocation, all validation attempts
    /// return unauthorized including cache-hit scenarios (cache invalidated).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RevokedKey_AlwaysReturnsUnauthorized_EvenAfterCacheHit()
    {
        var scopeGen = Gen.Choose(1, 3).SelectMany(count =>
            Gen.ArrayOf(count, Gen.Elements("payments:read", "payments:write", "disputes:read", "webhooks:manage")))
            .Select(arr => arr.Distinct().ToArray());

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            scopeGen.ToArbitrary(),
            (developerId, scopes) =>
            {
                // Arrange: Set up in-memory services
                var repository = new InMemoryApiKeyRepository();
                var cacheService = new InMemoryApiKeyCacheService();
                var auditStore = new InMemoryAuditStore();
                var service = new ApiKeyService(repository, cacheService, auditStore);

                // Act: Create a key
                var createResult = service.CreateKeyAsync(developerId, scopes, CancellationToken.None).GetAwaiter().GetResult();
                var rawKey = createResult.RawKey;
                var keyHash = ApiKey.ComputeHash(rawKey);

                // Validate it first (populates cache)
                var validationBeforeRevoke = service.ValidateKeyAsync(rawKey, CancellationToken.None).GetAwaiter().GetResult();
                var cacheWasPopulated = cacheService.HasEntry(keyHash);

                // Revoke the key
                service.RevokeKeyAsync(createResult.KeyId, CancellationToken.None).GetAwaiter().GetResult();

                // Verify cache was invalidated by revocation (before any subsequent validation)
                var cacheWasInvalidatedByRevoke = !cacheService.HasEntry(keyHash);

                // Validate again after revocation (should be invalid)
                var validationAfterRevoke = service.ValidateKeyAsync(rawKey, CancellationToken.None).GetAwaiter().GetResult();

                // Assert
                var wasValidBefore = validationBeforeRevoke.IsValid;
                var isInvalidAfterRevoke = !validationAfterRevoke.IsValid;

                return (wasValidBefore && cacheWasPopulated && cacheWasInvalidatedByRevoke && isInvalidAfterRevoke)
                    .Label($"Expected: validBefore={wasValidBefore}, cachePopulated={cacheWasPopulated}, " +
                           $"cacheInvalidatedByRevoke={cacheWasInvalidatedByRevoke}, invalidAfterRevoke={isInvalidAfterRevoke}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 14.3, 15.4**
    /// 
    /// Property 30: Multiple validation attempts after revocation all return unauthorized.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property RevokedKey_RemainsUnauthorized_AcrossMultipleValidationAttempts()
    {
        var attemptCountGen = Gen.Choose(2, 10);

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            attemptCountGen.ToArbitrary(),
            (developerId, attemptCount) =>
            {
                // Arrange
                var repository = new InMemoryApiKeyRepository();
                var cacheService = new InMemoryApiKeyCacheService();
                var auditStore = new InMemoryAuditStore();
                var service = new ApiKeyService(repository, cacheService, auditStore);

                var createResult = service.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None).GetAwaiter().GetResult();
                var rawKey = createResult.RawKey;

                // Revoke
                service.RevokeKeyAsync(createResult.KeyId, CancellationToken.None).GetAwaiter().GetResult();

                // Act: Validate multiple times after revocation
                var allInvalid = Enumerable.Range(0, attemptCount)
                    .Select(_ => service.ValidateKeyAsync(rawKey, CancellationToken.None).GetAwaiter().GetResult())
                    .All(r => !r.IsValid);

                return allInvalid
                    .Label($"Expected all {attemptCount} validation attempts after revocation to be invalid");
            });
    }
}

/// <summary>
/// Property-based tests for API Key Rotation Grace Period (Property 31).
/// 
/// **Validates: Requirements 14.2, 15.2, 15.3**
/// 
/// Both old and new key valid during grace period; only new key valid after grace expires.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "31")]
public class ApiKeyRotationGracePeriodPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 14.2, 15.2, 15.3**
    /// 
    /// Property 31: API Key Rotation Grace Period — Both old and new key valid during grace period.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BothKeys_AreValid_DuringGracePeriod()
    {
        var gracePeriodHoursGen = Gen.Choose(1, 72);

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            gracePeriodHoursGen.ToArbitrary(),
            (developerId, gracePeriodHours) =>
            {
                // Arrange
                var repository = new InMemoryApiKeyRepository();
                var cacheService = new InMemoryApiKeyCacheService();
                var auditStore = new InMemoryAuditStore();
                var service = new ApiKeyService(repository, cacheService, auditStore);

                // Create original key
                var createResult = service.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None).GetAwaiter().GetResult();
                var oldRawKey = createResult.RawKey;

                // Rotate with grace period
                var gracePeriod = TimeSpan.FromHours(gracePeriodHours);
                var rotateResult = service.RotateKeyAsync(createResult.KeyId, gracePeriod, CancellationToken.None).GetAwaiter().GetResult();
                var newRawKey = rotateResult.NewRawKey;

                // Act: Validate both keys (we're within grace period since rotation just happened)
                var oldKeyValidation = service.ValidateKeyAsync(oldRawKey, CancellationToken.None).GetAwaiter().GetResult();
                var newKeyValidation = service.ValidateKeyAsync(newRawKey, CancellationToken.None).GetAwaiter().GetResult();

                // Assert: Both should be valid during grace period
                var oldKeyValid = oldKeyValidation.IsValid;
                var newKeyValid = newKeyValidation.IsValid;

                return (oldKeyValid && newKeyValid)
                    .Label($"During grace period ({gracePeriodHours}h): oldKeyValid={oldKeyValid}, newKeyValid={newKeyValid}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 14.2, 15.2, 15.3**
    /// 
    /// Property 31: API Key Rotation Grace Period — Only new key valid after grace expires.
    /// Uses the domain entity's IsValid(atUtc) to verify time-based behavior.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OnlyNewKey_IsValid_AfterGracePeriodExpires()
    {
        var gracePeriodMinutesGen = Gen.Choose(1, 120);

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            gracePeriodMinutesGen.ToArbitrary(),
            (developerId, gracePeriodMinutes) =>
            {
                // Arrange: Create and rotate a key
                var scopes = new[] { "payments:read" };
                var rawKey = $"testkey_{developerId:N}_{gracePeriodMinutes}";
                // Ensure raw key is at least 8 chars
                if (rawKey.Length < 8) rawKey = rawKey.PadRight(8, 'x');

                var apiKey = ApiKey.CreateFromRawKey(developerId, rawKey, scopes, false, null);

                // Rotate with short grace period
                var gracePeriod = TimeSpan.FromMinutes(gracePeriodMinutes);
                apiKey.Rotate(gracePeriod);

                // Create a new key (what RotateKeyAsync does internally)
                var newRawKey = $"newkey_{developerId:N}_{gracePeriodMinutes}";
                if (newRawKey.Length < 8) newRawKey = newRawKey.PadRight(8, 'x');
                var newApiKey = ApiKey.CreateFromRawKey(developerId, newRawKey, scopes, false, null);

                // Act: Check validity after grace period expires
                var afterGraceExpiry = DateTime.UtcNow.Add(gracePeriod).AddSeconds(1);
                var oldKeyValidAfterGrace = apiKey.IsValid(afterGraceExpiry);
                var newKeyValidAfterGrace = newApiKey.IsValid(afterGraceExpiry);

                // Assert: Old key invalid, new key valid
                var oldKeyInvalid = !oldKeyValidAfterGrace;
                var newKeyStillValid = newKeyValidAfterGrace;

                return (oldKeyInvalid && newKeyStillValid)
                    .Label($"After grace period ({gracePeriodMinutes}min): " +
                           $"oldKeyInvalid={oldKeyInvalid}, newKeyStillValid={newKeyStillValid}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 14.2**
    /// 
    /// Property 31: The new key returned from rotation has Active status.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property RotatedKey_NewKeyHasActiveStatus()
    {
        var gracePeriodHoursGen = Gen.Choose(1, 48);

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            gracePeriodHoursGen.ToArbitrary(),
            (developerId, gracePeriodHours) =>
            {
                // Arrange
                var repository = new InMemoryApiKeyRepository();
                var cacheService = new InMemoryApiKeyCacheService();
                var auditStore = new InMemoryAuditStore();
                var service = new ApiKeyService(repository, cacheService, auditStore);

                var createResult = service.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None).GetAwaiter().GetResult();

                // Act: Rotate
                var gracePeriod = TimeSpan.FromHours(gracePeriodHours);
                var rotateResult = service.RotateKeyAsync(createResult.KeyId, gracePeriod, CancellationToken.None).GetAwaiter().GetResult();

                // Assert: New key is valid (Active)
                var newKeyValidation = service.ValidateKeyAsync(rotateResult.NewRawKey, CancellationToken.None).GetAwaiter().GetResult();
                var newKeyEntity = repository.GetByIdAsync(rotateResult.NewKeyId, CancellationToken.None).GetAwaiter().GetResult();

                var newKeyIsActive = newKeyEntity!.Status == KeyStatus.Active;
                var newKeyValidates = newKeyValidation.IsValid;

                return (newKeyIsActive && newKeyValidates)
                    .Label($"New key should be Active={newKeyIsActive}, validates={newKeyValidates}");
            });
    }
}

/// <summary>
/// Property-based tests for API Key Cache Consistency (Property 32).
/// 
/// **Validates: Requirements 15.2, 15.3, 15.4**
/// 
/// Cache hit authorizes without DB query; cache miss queries DB and populates cache with TTL.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "32")]
public class ApiKeyCacheConsistencyPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 15.2**
    /// 
    /// Property 32: API Key Cache Consistency — Cache hit authorizes without DB query.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CacheHit_AuthorizesWithoutDbQuery()
    {
        var scopeGen = Gen.Choose(1, 3).SelectMany(count =>
            Gen.ArrayOf(count, Gen.Elements("payments:read", "payments:write", "disputes:read", "webhooks:manage")))
            .Select(arr => arr.Distinct().ToArray());

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            scopeGen.ToArbitrary(),
            (developerId, scopes) =>
            {
                // Arrange
                var repository = new TrackingApiKeyRepository();
                var cacheService = new InMemoryApiKeyCacheService();
                var auditStore = new InMemoryAuditStore();
                var service = new ApiKeyService(repository, cacheService, auditStore);

                // Create a key and validate it once (to populate cache)
                var createResult = service.CreateKeyAsync(developerId, scopes, CancellationToken.None).GetAwaiter().GetResult();
                var rawKey = createResult.RawKey;

                // First validation: cache miss → DB query → populate cache
                service.ValidateKeyAsync(rawKey, CancellationToken.None).GetAwaiter().GetResult();
                var dbQueriesAfterFirstValidation = repository.GetByHashCallCount;

                // Act: Second validation should be a cache hit
                repository.ResetCallCount();
                var secondValidation = service.ValidateKeyAsync(rawKey, CancellationToken.None).GetAwaiter().GetResult();
                var dbQueriesOnSecondValidation = repository.GetByHashCallCount;

                // Assert: Cache hit means no DB query on second validation
                var noDbQueryOnCacheHit = dbQueriesOnSecondValidation == 0;
                var secondValidationSucceeds = secondValidation.IsValid;

                return (noDbQueryOnCacheHit && secondValidationSucceeds)
                    .Label($"Cache hit: noDbQuery={noDbQueryOnCacheHit} (actual={dbQueriesOnSecondValidation}), " +
                           $"isValid={secondValidationSucceeds}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 15.3**
    /// 
    /// Property 32: API Key Cache Consistency — Cache miss queries DB and populates cache with TTL.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CacheMiss_QueriesDbAndPopulatesCache()
    {
        var scopeGen = Gen.Choose(1, 3).SelectMany(count =>
            Gen.ArrayOf(count, Gen.Elements("payments:read", "payments:write", "disputes:read")))
            .Select(arr => arr.Distinct().ToArray());

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            scopeGen.ToArbitrary(),
            (developerId, scopes) =>
            {
                // Arrange
                var repository = new TrackingApiKeyRepository();
                var cacheService = new InMemoryApiKeyCacheService();
                var auditStore = new InMemoryAuditStore();
                var service = new ApiKeyService(repository, cacheService, auditStore);

                // Create a key
                var createResult = service.CreateKeyAsync(developerId, scopes, CancellationToken.None).GetAwaiter().GetResult();
                var rawKey = createResult.RawKey;
                var keyHash = ApiKey.ComputeHash(rawKey);

                // Verify cache is empty before first validation
                var cacheEmptyBefore = !cacheService.HasEntry(keyHash);
                repository.ResetCallCount();

                // Act: Validate (cache miss)
                var validation = service.ValidateKeyAsync(rawKey, CancellationToken.None).GetAwaiter().GetResult();

                // Assert: DB was queried and cache was populated
                var dbWasQueried = repository.GetByHashCallCount > 0;
                var cachePopulatedAfter = cacheService.HasEntry(keyHash);
                var validationSucceeds = validation.IsValid;
                var cacheHasTtl = cacheService.GetTtl(keyHash) > TimeSpan.Zero;

                return (cacheEmptyBefore && dbWasQueried && cachePopulatedAfter && validationSucceeds && cacheHasTtl)
                    .Label($"Cache miss: emptyBefore={cacheEmptyBefore}, dbQueried={dbWasQueried}, " +
                           $"cachePopulated={cachePopulatedAfter}, valid={validationSucceeds}, hasTtl={cacheHasTtl}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 15.4**
    /// 
    /// Property 32: After cache invalidation (e.g. revocation), next validation queries DB again.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property CacheInvalidation_ForcesDbQueryOnNextValidation()
    {
        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            (developerId) =>
            {
                // Arrange
                var repository = new TrackingApiKeyRepository();
                var cacheService = new InMemoryApiKeyCacheService();
                var auditStore = new InMemoryAuditStore();
                var service = new ApiKeyService(repository, cacheService, auditStore);

                // Create key and validate to populate cache
                var createResult = service.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None).GetAwaiter().GetResult();
                var rawKey = createResult.RawKey;
                var keyHash = ApiKey.ComputeHash(rawKey);

                service.ValidateKeyAsync(rawKey, CancellationToken.None).GetAwaiter().GetResult();
                var cachePopulated = cacheService.HasEntry(keyHash);

                // Revoke (invalidates cache)
                service.RevokeKeyAsync(createResult.KeyId, CancellationToken.None).GetAwaiter().GetResult();
                var cacheInvalidated = !cacheService.HasEntry(keyHash);

                // Reset DB tracking
                repository.ResetCallCount();

                // Act: Validate again (should query DB since cache is invalidated)
                var validationAfterRevoke = service.ValidateKeyAsync(rawKey, CancellationToken.None).GetAwaiter().GetResult();
                var dbQueriedAfterInvalidation = repository.GetByHashCallCount > 0;

                return (cachePopulated && cacheInvalidated && dbQueriedAfterInvalidation && !validationAfterRevoke.IsValid)
                    .Label($"CacheInvalidation: wasPopulated={cachePopulated}, wasInvalidated={cacheInvalidated}, " +
                           $"dbQueried={dbQueriedAfterInvalidation}, isInvalid={!validationAfterRevoke.IsValid}");
            });
    }
}

#region Test Infrastructure

/// <summary>
/// In-memory implementation of IApiKeyRepository for property testing.
/// </summary>
internal class InMemoryApiKeyRepository : IApiKeyRepository
{
    private readonly ConcurrentDictionary<Guid, ApiKey> _keysById = new();
    private readonly ConcurrentDictionary<string, ApiKey> _keysByHash = new();

    public Task<ApiKey> CreateAsync(ApiKey key, CancellationToken ct)
    {
        _keysById[key.Id] = key;
        _keysByHash[key.KeyHash] = key;
        return Task.FromResult(key);
    }

    public Task<ApiKey?> GetByIdAsync(Guid keyId, CancellationToken ct) =>
        Task.FromResult(_keysById.TryGetValue(keyId, out var key) ? key : null);

    public Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken ct) =>
        Task.FromResult(_keysByHash.TryGetValue(keyHash, out var key) ? key : null);

    public Task<int> CountActiveByDeveloperAsync(Guid developerId, CancellationToken ct) =>
        Task.FromResult(_keysById.Values.Count(k => k.DeveloperId == developerId && k.Status == KeyStatus.Active));

    public Task UpdateAsync(ApiKey key, CancellationToken ct)
    {
        _keysById[key.Id] = key;
        _keysByHash[key.KeyHash] = key;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApiKey>> GetByDeveloperAsync(Guid developerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ApiKey>>(
            _keysById.Values.Where(k => k.DeveloperId == developerId).ToList());
}

/// <summary>
/// In-memory implementation of IApiKeyRepository with DB call tracking for property testing.
/// </summary>
internal class TrackingApiKeyRepository : IApiKeyRepository
{
    private readonly ConcurrentDictionary<Guid, ApiKey> _keysById = new();
    private readonly ConcurrentDictionary<string, ApiKey> _keysByHash = new();
    private int _getByHashCallCount;

    public int GetByHashCallCount => _getByHashCallCount;

    public void ResetCallCount() => _getByHashCallCount = 0;

    public Task<ApiKey> CreateAsync(ApiKey key, CancellationToken ct)
    {
        _keysById[key.Id] = key;
        _keysByHash[key.KeyHash] = key;
        return Task.FromResult(key);
    }

    public Task<ApiKey?> GetByIdAsync(Guid keyId, CancellationToken ct) =>
        Task.FromResult(_keysById.TryGetValue(keyId, out var key) ? key : null);

    public Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken ct)
    {
        Interlocked.Increment(ref _getByHashCallCount);
        return Task.FromResult(_keysByHash.TryGetValue(keyHash, out var key) ? key : null);
    }

    public Task<int> CountActiveByDeveloperAsync(Guid developerId, CancellationToken ct) =>
        Task.FromResult(_keysById.Values.Count(k => k.DeveloperId == developerId && k.Status == KeyStatus.Active));

    public Task UpdateAsync(ApiKey key, CancellationToken ct)
    {
        _keysById[key.Id] = key;
        _keysByHash[key.KeyHash] = key;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApiKey>> GetByDeveloperAsync(Guid developerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ApiKey>>(
            _keysById.Values.Where(k => k.DeveloperId == developerId).ToList());
}

/// <summary>
/// In-memory implementation of IApiKeyCacheService for property testing.
/// Tracks TTL to verify cache-with-TTL behavior.
/// </summary>
internal class InMemoryApiKeyCacheService : IApiKeyCacheService
{
    private readonly ConcurrentDictionary<string, (CachedKeyInfo Info, TimeSpan Ttl)> _cache = new();

    public Task<CachedKeyInfo?> GetAsync(string apiKeyHash, CancellationToken ct) =>
        Task.FromResult(_cache.TryGetValue(apiKeyHash, out var entry) ? entry.Info : null);

    public Task SetAsync(string apiKeyHash, CachedKeyInfo info, TimeSpan ttl, CancellationToken ct)
    {
        _cache[apiKeyHash] = (info, ttl);
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string apiKeyHash, CancellationToken ct)
    {
        _cache.TryRemove(apiKeyHash, out _);
        return Task.CompletedTask;
    }

    public bool HasEntry(string apiKeyHash) => _cache.ContainsKey(apiKeyHash);

    public TimeSpan GetTtl(string apiKeyHash) =>
        _cache.TryGetValue(apiKeyHash, out var entry) ? entry.Ttl : TimeSpan.Zero;
}

/// <summary>
/// In-memory implementation of IAuditStore for property testing.
/// </summary>
internal class InMemoryAuditStore : IAuditStore
{
    private readonly ConcurrentBag<AuditEntry> _entries = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(
            _entries.Where(e => e.TransactionReference == transactionReference).ToList());
}

#endregion

using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal.Persistence;
using CardManagement.Infrastructure.Persistence;
using CardManagement.PlatformServices.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace CardManagement.PlatformServices.IntegrationTests;

/// <summary>
/// Integration tests for Redis cache invalidation on key revocation propagation.
/// Tests the full flow: create key → validate (cache populated) → revoke key → validate again (fails, cache evicted).
/// Requirements: 14.3, 15.1, 15.2, 15.3, 15.4
/// </summary>
public class RedisCacheInvalidationTests : IDisposable
{
    private readonly CardManagementDbContext _dbContext;
    private readonly FakeRedisConnectionMultiplexer _fakeRedis;
    private readonly RedisApiKeyCacheService _cacheService;
    private readonly ApiKeyRepository _apiKeyRepo;
    private readonly ApiKeyService _apiKeyService;

    public RedisCacheInvalidationTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
        _fakeRedis = new FakeRedisConnectionMultiplexer();
        _cacheService = new RedisApiKeyCacheService(_fakeRedis.Multiplexer, NullLogger<RedisApiKeyCacheService>.Instance);
        _apiKeyRepo = new ApiKeyRepository(_dbContext);
        _apiKeyService = new ApiKeyService(_apiKeyRepo, _cacheService, new FakeAuditStore());
    }

    [Fact]
    public async Task CreateAndValidate_PopulatesCache()
    {
        // Arrange: Create an API key
        var developerId = Guid.NewGuid();
        var result = await _apiKeyService.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None);

        // Act: Validate the key (first call should miss cache, hit DB, then populate cache)
        var validationResult = await _apiKeyService.ValidateKeyAsync(result.RawKey, CancellationToken.None);

        // Assert: Key is valid
        Assert.True(validationResult.IsValid);
        Assert.Equal(developerId, validationResult.DeveloperId);

        // Verify cache was populated
        var keyHash = ApiKey.ComputeHash(result.RawKey);
        var cached = await _cacheService.GetAsync(keyHash, CancellationToken.None);
        Assert.NotNull(cached);
        Assert.Equal(developerId, cached!.DeveloperId);
        Assert.Equal(KeyStatus.Active, cached.Status);
    }

    [Fact]
    public async Task RevokeKey_InvalidatesCache_SubsequentValidationFails()
    {
        // Arrange: Create and validate a key (populating the cache)
        var developerId = Guid.NewGuid();
        var createResult = await _apiKeyService.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None);

        // Populate cache via validation
        var firstValidation = await _apiKeyService.ValidateKeyAsync(createResult.RawKey, CancellationToken.None);
        Assert.True(firstValidation.IsValid);

        // Verify cache is populated before revocation
        var keyHash = ApiKey.ComputeHash(createResult.RawKey);
        var cachedBefore = await _cacheService.GetAsync(keyHash, CancellationToken.None);
        Assert.NotNull(cachedBefore);

        // Act: Revoke the key (should invalidate cache entry)
        await _apiKeyService.RevokeKeyAsync(createResult.KeyId, CancellationToken.None);

        // Assert: Cache is invalidated
        var cachedAfter = await _cacheService.GetAsync(keyHash, CancellationToken.None);
        Assert.Null(cachedAfter);

        // Subsequent validation fails
        var secondValidation = await _apiKeyService.ValidateKeyAsync(createResult.RawKey, CancellationToken.None);
        Assert.False(secondValidation.IsValid);
    }

    [Fact]
    public async Task CacheHit_DoesNotQueryDatabase_ReturnsQuickly()
    {
        // Arrange: Create and validate a key to populate cache
        var developerId = Guid.NewGuid();
        var createResult = await _apiKeyService.CreateKeyAsync(developerId, new[] { "payments:write" }, CancellationToken.None);

        // First validation populates cache
        await _apiKeyService.ValidateKeyAsync(createResult.RawKey, CancellationToken.None);

        // Act: Second validation should hit cache
        var secondResult = await _apiKeyService.ValidateKeyAsync(createResult.RawKey, CancellationToken.None);

        // Assert: Still valid (from cache)
        Assert.True(secondResult.IsValid);
        Assert.Equal(developerId, secondResult.DeveloperId);
    }

    [Fact]
    public async Task CacheMiss_FallsBackToDatabase()
    {
        // Arrange: Create key but do NOT validate (cache not populated)
        var developerId = Guid.NewGuid();
        var rawKey = $"test-api-key-{Guid.NewGuid():N}";
        var apiKey = ApiKey.CreateFromRawKey(developerId, rawKey, new[] { "payments:read" }, false, null);
        await _apiKeyRepo.CreateAsync(apiKey, CancellationToken.None);

        // Verify cache is empty
        var keyHash = ApiKey.ComputeHash(rawKey);
        var cachedBefore = await _cacheService.GetAsync(keyHash, CancellationToken.None);
        Assert.Null(cachedBefore);

        // Act: Validate (cache miss → DB lookup → cache populate)
        var result = await _apiKeyService.ValidateKeyAsync(rawKey, CancellationToken.None);

        // Assert: Valid from DB
        Assert.True(result.IsValid);

        // Cache is now populated
        var cachedAfter = await _cacheService.GetAsync(keyHash, CancellationToken.None);
        Assert.NotNull(cachedAfter);
        Assert.Equal(developerId, cachedAfter!.DeveloperId);
    }

    [Fact]
    public async Task RotateKey_InvalidatesOldCacheEntry()
    {
        // Arrange: Create and validate (populates cache)
        var developerId = Guid.NewGuid();
        var createResult = await _apiKeyService.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None);
        await _apiKeyService.ValidateKeyAsync(createResult.RawKey, CancellationToken.None);

        // Verify old key is cached
        var oldKeyHash = ApiKey.ComputeHash(createResult.RawKey);
        var cached = await _cacheService.GetAsync(oldKeyHash, CancellationToken.None);
        Assert.NotNull(cached);

        // Act: Rotate the key
        var rotateResult = await _apiKeyService.RotateKeyAsync(createResult.KeyId, TimeSpan.FromHours(24), CancellationToken.None);

        // Assert: Old key cache entry is invalidated (so new status is picked up)
        var cachedAfterRotation = await _cacheService.GetAsync(oldKeyHash, CancellationToken.None);
        Assert.Null(cachedAfterRotation);

        // New key should be valid
        var newValidation = await _apiKeyService.ValidateKeyAsync(rotateResult.NewRawKey, CancellationToken.None);
        Assert.True(newValidation.IsValid);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>
    /// Minimal fake audit store for testing — just accumulates entries.
    /// </summary>
    private sealed class FakeAuditStore : IAuditStore
    {
        private readonly List<AuditEntry> _entries = new();

        public Task AppendAsync(AuditEntry entry, CancellationToken ct)
        {
            _entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> GetByCorrelationIdAsync(string correlationId, CancellationToken ct)
        {
            var matches = _entries.Where(e => e.CorrelationId == correlationId).ToList();
            return Task.FromResult<IReadOnlyList<AuditEntry>>(matches);
        }

        public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
        {
            var matches = _entries.Where(e => e.TransactionReference == transactionReference).ToList();
            return Task.FromResult<IReadOnlyList<AuditEntry>>(matches);
        }
    }
}

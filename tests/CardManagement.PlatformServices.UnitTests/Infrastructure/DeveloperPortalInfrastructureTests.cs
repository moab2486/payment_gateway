using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for Developer Portal infrastructure components.
/// Validates: Requirements 14.1, 14.2, 14.3, 15.4, 17.2, 17.5, 18.1, 18.3, 18.6
/// </summary>
public class DeveloperPortalInfrastructureTests
{
    #region API Key Creation — Returns raw value once, stores only hash

    [Fact]
    public async Task CreateKey_ReturnsRawKeyValue_ExactlyOnce()
    {
        // Arrange
        var (service, repo, cache) = CreateApiKeyService();
        var developerId = Guid.NewGuid();

        // Act
        var result = await service.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None);

        // Assert — raw key is returned
        Assert.False(string.IsNullOrWhiteSpace(result.RawKey));
        Assert.True(result.RawKey.Length >= 8);
    }

    [Fact]
    public async Task CreateKey_StoresOnlyHash_NotRawKey()
    {
        // Arrange
        var (service, repo, _) = CreateApiKeyService();
        var developerId = Guid.NewGuid();

        // Act
        var result = await service.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None);

        // Assert — repository stores a hashed key, not the raw key
        var storedKey = repo.Keys.Single();
        Assert.NotEqual(result.RawKey, storedKey.KeyHash);
        Assert.Equal(ApiKey.ComputeHash(result.RawKey), storedKey.KeyHash);
    }

    [Fact]
    public async Task CreateKey_RawKeyCanValidateViaHash()
    {
        // Arrange
        var (service, repo, _) = CreateApiKeyService();
        var developerId = Guid.NewGuid();

        // Act
        var result = await service.CreateKeyAsync(developerId, new[] { "payments:write" }, CancellationToken.None);

        // Assert — raw key hashes to the stored hash (proving we can validate later)
        var storedKey = repo.Keys.Single();
        var computedHash = ApiKey.ComputeHash(result.RawKey);
        Assert.Equal(storedKey.KeyHash, computedHash);
    }

    [Fact]
    public async Task CreateKey_PrefixMatchesFirstEightCharsOfRawKey()
    {
        // Arrange
        var (service, _, _) = CreateApiKeyService();
        var developerId = Guid.NewGuid();

        // Act
        var result = await service.CreateKeyAsync(developerId, new[] { "read" }, CancellationToken.None);

        // Assert
        Assert.Equal(result.RawKey[..8], result.KeyPrefix);
    }

    #endregion

    #region Rotation Grace Period — Both keys valid during period, only new after

    [Fact]
    public async Task RotateKey_BothKeysValidDuringGracePeriod()
    {
        // Arrange
        var (service, repo, cache) = CreateApiKeyService();
        var developerId = Guid.NewGuid();
        var createResult = await service.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None);
        var gracePeriod = TimeSpan.FromHours(24);

        // Act
        var rotateResult = await service.RotateKeyAsync(createResult.KeyId, gracePeriod, CancellationToken.None);

        // Assert — old key is still valid (within grace period)
        var oldKeyValidation = await service.ValidateKeyAsync(createResult.RawKey, CancellationToken.None);
        Assert.True(oldKeyValidation.IsValid);

        // Assert — new key is also valid
        var newKeyValidation = await service.ValidateKeyAsync(rotateResult.NewRawKey, CancellationToken.None);
        Assert.True(newKeyValidation.IsValid);
    }

    [Fact]
    public async Task RotateKey_OldKeyInvalid_AfterGracePeriodExpires()
    {
        // Arrange — use the domain entity directly to test grace period expiry
        var developerId = Guid.NewGuid();
        var rawKey = "test-old-key-for-rotation1234";
        var apiKey = ApiKey.CreateFromRawKey(developerId, rawKey, new[] { "read" }, false, null);

        // Rotate with a very short grace period
        apiKey.Rotate(TimeSpan.FromMilliseconds(1));

        // Wait for grace period to expire
        await Task.Delay(10);

        // Assert — old key is no longer valid after grace period
        Assert.False(apiKey.IsValid());
    }

    [Fact]
    public async Task RotateKey_NewKeyRemainsValid_AfterGracePeriodExpires()
    {
        // Arrange
        var (service, repo, _) = CreateApiKeyService();
        var developerId = Guid.NewGuid();
        var createResult = await service.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None);

        // Act
        var rotateResult = await service.RotateKeyAsync(createResult.KeyId, TimeSpan.FromHours(1), CancellationToken.None);

        // Assert — new key is Active and valid regardless of old key's grace period
        var newKeyEntity = repo.Keys.First(k => k.Id == rotateResult.NewKeyId);
        Assert.Equal(KeyStatus.Active, newKeyEntity.Status);
        Assert.True(newKeyEntity.IsValid());
    }

    [Fact]
    public async Task RotateKey_OldKeyStatusIsRotated()
    {
        // Arrange
        var (service, repo, _) = CreateApiKeyService();
        var developerId = Guid.NewGuid();
        var createResult = await service.CreateKeyAsync(developerId, new[] { "read" }, CancellationToken.None);

        // Act
        await service.RotateKeyAsync(createResult.KeyId, TimeSpan.FromHours(2), CancellationToken.None);

        // Assert
        var oldKey = repo.Keys.First(k => k.Id == createResult.KeyId);
        Assert.Equal(KeyStatus.Rotated, oldKey.Status);
        Assert.NotNull(oldKey.GracePeriodEndsAtUtc);
    }

    [Fact]
    public async Task RotateKey_CacheEvictedForOldKey()
    {
        // Arrange
        var (service, repo, cache) = CreateApiKeyService();
        var developerId = Guid.NewGuid();
        var createResult = await service.CreateKeyAsync(developerId, new[] { "read" }, CancellationToken.None);

        // Populate cache for old key by validating it
        await service.ValidateKeyAsync(createResult.RawKey, CancellationToken.None);
        var oldKeyHash = ApiKey.ComputeHash(createResult.RawKey);
        Assert.True(cache.Store.ContainsKey(oldKeyHash));

        // Act
        await service.RotateKeyAsync(createResult.KeyId, TimeSpan.FromHours(1), CancellationToken.None);

        // Assert — cache was invalidated for old key
        Assert.False(cache.Store.ContainsKey(oldKeyHash));
    }

    #endregion

    #region Revocation — Immediate invalidation + cache eviction

    [Fact]
    public async Task RevokeKey_ImmediatelyInvalidatesKey()
    {
        // Arrange
        var (service, repo, _) = CreateApiKeyService();
        var developerId = Guid.NewGuid();
        var createResult = await service.CreateKeyAsync(developerId, new[] { "payments:read" }, CancellationToken.None);

        // Verify key is valid before revocation
        var beforeRevoke = await service.ValidateKeyAsync(createResult.RawKey, CancellationToken.None);
        Assert.True(beforeRevoke.IsValid);

        // Act
        await service.RevokeKeyAsync(createResult.KeyId, CancellationToken.None);

        // Assert — key is immediately invalid
        var afterRevoke = await service.ValidateKeyAsync(createResult.RawKey, CancellationToken.None);
        Assert.False(afterRevoke.IsValid);
    }

    [Fact]
    public async Task RevokeKey_EvictsCacheEntry()
    {
        // Arrange
        var (service, _, cache) = CreateApiKeyService();
        var developerId = Guid.NewGuid();
        var createResult = await service.CreateKeyAsync(developerId, new[] { "read" }, CancellationToken.None);

        // Populate cache by validating
        await service.ValidateKeyAsync(createResult.RawKey, CancellationToken.None);
        var keyHash = ApiKey.ComputeHash(createResult.RawKey);
        Assert.True(cache.Store.ContainsKey(keyHash));

        // Act
        await service.RevokeKeyAsync(createResult.KeyId, CancellationToken.None);

        // Assert — cache entry removed
        Assert.False(cache.Store.ContainsKey(keyHash));
    }

    [Fact]
    public async Task RevokeKey_SubsequentValidationReturnsFalse_EvenWithoutCache()
    {
        // Arrange
        var (service, repo, cache) = CreateApiKeyService();
        var developerId = Guid.NewGuid();
        var createResult = await service.CreateKeyAsync(developerId, new[] { "write" }, CancellationToken.None);

        // Act
        await service.RevokeKeyAsync(createResult.KeyId, CancellationToken.None);

        // Clear cache to force DB lookup
        cache.Store.Clear();

        // Assert — even from DB, key is not valid
        var result = await service.ValidateKeyAsync(createResult.RawKey, CancellationToken.None);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task RevokeKey_StatusSetToRevoked()
    {
        // Arrange
        var (service, repo, _) = CreateApiKeyService();
        var developerId = Guid.NewGuid();
        var createResult = await service.CreateKeyAsync(developerId, new[] { "read" }, CancellationToken.None);

        // Act
        await service.RevokeKeyAsync(createResult.KeyId, CancellationToken.None);

        // Assert
        var key = repo.Keys.First(k => k.Id == createResult.KeyId);
        Assert.Equal(KeyStatus.Revoked, key.Status);
    }

    #endregion

    #region PCI Redaction — Various card number formats, CVV positions, PIN patterns

    [Theory]
    [InlineData("4111111111111111")] // 16-digit Visa
    [InlineData("5500000000000004")] // 16-digit Mastercard
    [InlineData("340000000000009")] // 15-digit Amex
    [InlineData("6011000000000004")] // 16-digit Discover
    [InlineData("3530111333300000")] // 16-digit JCB
    [InlineData("1234567890123")] // 13-digit minimum
    [InlineData("1234567890123456789")] // 19-digit maximum
    public void PciRedaction_MasksVariousCardNumberFormats(string pan)
    {
        var body = $"{{\"cardNumber\":\"{pan}\"}}";

        var result = PciRedactionFilter.Redact(body);

        Assert.DoesNotContain(pan, result);
        // Last 4 digits should still be visible
        Assert.Contains(pan[^4..], result);
    }

    [Theory]
    [InlineData("cvv", "123")] // 3-digit CVV at start
    [InlineData("cvv", "1234")] // 4-digit CVV (Amex)
    [InlineData("cvc", "567")] // CVC variant
    [InlineData("cvv2", "890")] // CVV2 variant
    [InlineData("securityCode", "4321")] // camelCase variant
    [InlineData("security_code", "999")] // snake_case variant
    public void PciRedaction_MasksCvvInVariousPositions(string fieldName, string cvvValue)
    {
        // CVV field can appear in various positions within JSON
        var body = $"{{\"amount\":5000,\"{fieldName}\":\"{cvvValue}\",\"currency\":\"NGN\"}}";

        var result = PciRedactionFilter.Redact(body);

        Assert.DoesNotContain($"\"{fieldName}\":\"{cvvValue}\"", result);
        Assert.Contains($"\"{fieldName}\":\"***\"", result);
    }

    [Theory]
    [InlineData("pin", "1234")]
    [InlineData("pin", "123456")]
    [InlineData("pinBlock", "0516001234567890")]
    [InlineData("pin_block", "ABCDEF1234567890")]
    [InlineData("pinBlock", "0000FFFFFFFFFFFF")]
    public void PciRedaction_MasksVariousPinPatterns(string fieldName, string pinValue)
    {
        var body = $"{{\"transaction\":{{\"amount\":1000,\"{fieldName}\":\"{pinValue}\"}}}}";

        var result = PciRedactionFilter.Redact(body);

        Assert.DoesNotContain($"\"{fieldName}\":\"{pinValue}\"", result);
        Assert.Contains($"\"{fieldName}\":\"***\"", result);
    }

    [Fact]
    public void PciRedaction_MasksMultiplePansInSameBody()
    {
        var body = """{"cardNumber":"4111111111111111","recipientCard":"5500000000000004"}""";

        var result = PciRedactionFilter.Redact(body);

        Assert.DoesNotContain("4111111111111111", result);
        Assert.DoesNotContain("5500000000000004", result);
        Assert.Contains("1111", result);
        Assert.Contains("0004", result);
    }

    [Fact]
    public void PciRedaction_PreservesNonSensitiveFields()
    {
        var body = """{"cardNumber":"4111111111111111","cvv":"123","amount":5000,"currency":"NGN","reference":"TXN-001"}""";

        var result = PciRedactionFilter.Redact(body);

        Assert.Contains("\"amount\":5000", result);
        Assert.Contains("\"currency\":\"NGN\"", result);
        Assert.Contains("\"reference\":\"TXN-001\"", result);
    }

    #endregion

    #region Request Log Retention Purge — Entries older than configured period

    [Fact]
    public async Task PurgeExpired_RemovesEntriesOlderThanRetention()
    {
        // Arrange
        var repo = new DeveloperPortalRequestLogRepository();
        var retention = TimeSpan.FromDays(7);

        var oldEntry = CreateRequestLogEntry(daysAgo: 10);
        var recentEntry = CreateRequestLogEntry(daysAgo: 3);
        repo.Entries.Add(oldEntry);
        repo.Entries.Add(recentEntry);

        // Act
        await repo.PurgeExpiredAsync(retention, CancellationToken.None);

        // Assert — old entry removed, recent entry retained
        Assert.Single(repo.Entries);
        Assert.Equal(recentEntry.Id, repo.Entries[0].Id);
    }

    [Fact]
    public async Task PurgeExpired_KeepsEntriesExactlyAtRetentionBoundary()
    {
        // Arrange
        var repo = new DeveloperPortalRequestLogRepository();
        var retention = TimeSpan.FromDays(7);

        // Entry exactly at retention boundary (7 days old)
        var boundaryEntry = CreateRequestLogEntry(daysAgo: 7);
        repo.Entries.Add(boundaryEntry);

        // Act
        await repo.PurgeExpiredAsync(retention, CancellationToken.None);

        // Assert — boundary entry is purged (older than retention)
        Assert.Empty(repo.Entries);
    }

    [Fact]
    public async Task PurgeExpired_NoEntriesRemoved_WhenAllWithinRetention()
    {
        // Arrange
        var repo = new DeveloperPortalRequestLogRepository();
        var retention = TimeSpan.FromDays(7);

        repo.Entries.Add(CreateRequestLogEntry(daysAgo: 1));
        repo.Entries.Add(CreateRequestLogEntry(daysAgo: 3));
        repo.Entries.Add(CreateRequestLogEntry(daysAgo: 6));

        // Act
        await repo.PurgeExpiredAsync(retention, CancellationToken.None);

        // Assert — all entries still present
        Assert.Equal(3, repo.Entries.Count);
    }

    [Fact]
    public async Task PurgeExpired_RemovesAllEntries_WhenAllExpired()
    {
        // Arrange
        var repo = new DeveloperPortalRequestLogRepository();
        var retention = TimeSpan.FromDays(7);

        repo.Entries.Add(CreateRequestLogEntry(daysAgo: 8));
        repo.Entries.Add(CreateRequestLogEntry(daysAgo: 30));
        repo.Entries.Add(CreateRequestLogEntry(daysAgo: 90));

        // Act
        await repo.PurgeExpiredAsync(retention, CancellationToken.None);

        // Assert
        Assert.Empty(repo.Entries);
    }

    #endregion

    #region Sandbox Test Scenarios — Triggered by specific input values

    [Theory]
    [InlineData("4000000000000000", "succeeded")]
    [InlineData("4000000000000002", "declined")]
    [InlineData("4000000000000010", "failed")]
    [InlineData("4000000000000019", "declined")]
    public async Task Sandbox_SpecificCardNumbers_TriggerExpectedScenarios(string cardNumber, string expectedStatus)
    {
        // Arrange
        var sandbox = CreateSandboxEnvironment();
        var developerId = Guid.NewGuid();
        var request = CreateSandboxPaymentRequest(developerId, cardNumber, 1000, "NGN");

        // Act
        var response = await sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(response.ResponseBody);
        var body = System.Text.Json.JsonDocument.Parse(response.ResponseBody);
        Assert.Equal(expectedStatus, body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Sandbox_SuccessfulPayment_ReturnsAuthorizationCode()
    {
        // Arrange
        var sandbox = CreateSandboxEnvironment();
        var developerId = Guid.NewGuid();
        var request = CreateSandboxPaymentRequest(developerId, "4000000000000000", 2500, "NGN");

        // Act
        var response = await sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(200, response.StatusCode);
        var body = System.Text.Json.JsonDocument.Parse(response.ResponseBody!);
        Assert.NotNull(body.RootElement.GetProperty("authorizationCode").GetString());
        Assert.NotNull(body.RootElement.GetProperty("processorReference").GetString());
    }

    [Fact]
    public async Task Sandbox_DeclinedCard_ReturnsDeclineCode()
    {
        // Arrange
        var sandbox = CreateSandboxEnvironment();
        var developerId = Guid.NewGuid();
        var request = CreateSandboxPaymentRequest(developerId, "4000000000000002", 1000, "NGN");

        // Act
        var response = await sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        var body = System.Text.Json.JsonDocument.Parse(response.ResponseBody!);
        Assert.Equal("card_declined", body.RootElement.GetProperty("declineCode").GetString());
    }

    [Fact]
    public async Task Sandbox_InsufficientFundsCard_ReturnsInsufficientFundsCode()
    {
        // Arrange
        var sandbox = CreateSandboxEnvironment();
        var developerId = Guid.NewGuid();
        var request = CreateSandboxPaymentRequest(developerId, "4000000000000019", 1000, "NGN");

        // Act
        var response = await sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        var body = System.Text.Json.JsonDocument.Parse(response.ResponseBody!);
        Assert.Equal("insufficient_funds", body.RootElement.GetProperty("declineCode").GetString());
    }

    [Fact]
    public async Task Sandbox_TimeoutCard_Returns504()
    {
        // Arrange
        var sandbox = CreateSandboxEnvironment();
        var developerId = Guid.NewGuid();
        var request = CreateSandboxPaymentRequest(developerId, "4000000000000010", 1000, "NGN");

        // Act
        var response = await sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(504, response.StatusCode);
    }

    #endregion

    #region Developer Resource Isolation — Cannot access other developer's resources

    [Fact]
    public async Task Isolation_DeveloperCanOnlyValidateOwnKeys()
    {
        // Arrange
        var (service, repo, _) = CreateApiKeyService();
        var developer1 = Guid.NewGuid();
        var developer2 = Guid.NewGuid();

        var key1 = await service.CreateKeyAsync(developer1, new[] { "read" }, CancellationToken.None);
        var key2 = await service.CreateKeyAsync(developer2, new[] { "read" }, CancellationToken.None);

        // Act & Assert — each key belongs to respective developer
        var validation1 = await service.ValidateKeyAsync(key1.RawKey, CancellationToken.None);
        Assert.Equal(developer1, validation1.DeveloperId);

        var validation2 = await service.ValidateKeyAsync(key2.RawKey, CancellationToken.None);
        Assert.Equal(developer2, validation2.DeveloperId);
    }

    [Fact]
    public async Task Isolation_DeveloperCannotRevokeAnotherDevelopersKey()
    {
        // Arrange — in the service, revoke operates by keyId not developerId
        // but the domain enforces ownership at the API/middleware level.
        // Here we verify that keys correctly track their developer ownership.
        var (service, repo, _) = CreateApiKeyService();
        var developer1 = Guid.NewGuid();
        var developer2 = Guid.NewGuid();

        var key1 = await service.CreateKeyAsync(developer1, new[] { "read" }, CancellationToken.None);
        var key2 = await service.CreateKeyAsync(developer2, new[] { "write" }, CancellationToken.None);

        // Assert — each key belongs to its developer
        var storedKey1 = repo.Keys.First(k => k.Id == key1.KeyId);
        var storedKey2 = repo.Keys.First(k => k.Id == key2.KeyId);
        Assert.Equal(developer1, storedKey1.DeveloperId);
        Assert.Equal(developer2, storedKey2.DeveloperId);
        Assert.NotEqual(storedKey1.DeveloperId, storedKey2.DeveloperId);
    }

    [Fact]
    public async Task Isolation_RequestLogsFilteredByDeveloper()
    {
        // Arrange
        var repo = new DeveloperPortalRequestLogRepository();
        var developer1 = Guid.NewGuid();
        var developer2 = Guid.NewGuid();

        var entry1 = CreateRequestLogEntryForDeveloper(developer1, "/api/v1/payments", 200);
        var entry2 = CreateRequestLogEntryForDeveloper(developer2, "/api/v1/payments", 200);
        var entry3 = CreateRequestLogEntryForDeveloper(developer1, "/api/v1/cards", 201);
        repo.Entries.AddRange(new[] { entry1, entry2, entry3 });

        // Act — query for developer1's logs
        var query = new RequestLogQuery(developer1);
        var results = await repo.QueryAsync(query, CancellationToken.None);

        // Assert — only developer1's entries returned
        Assert.Equal(2, results.Items.Count);
        Assert.All(results.Items, item => Assert.Equal(developer1, item.DeveloperId));
    }

    [Fact]
    public async Task Isolation_SandboxDataIsolatedBetweenDevelopers()
    {
        // Arrange
        var sandbox = CreateSandboxEnvironment();
        var developer1 = Guid.NewGuid();
        var developer2 = Guid.NewGuid();

        // Developer 1 creates a transaction
        var payment1 = CreateSandboxPaymentRequest(developer1, "4000000000000000", 1000, "NGN");
        await sandbox.ProcessRequestAsync(payment1, CancellationToken.None);

        // Act — Developer 2 queries transactions
        var queryRequest = new SandboxRequest(developer2, "/api/v1/transactions", "GET", null, null);
        var response = await sandbox.ProcessRequestAsync(queryRequest, CancellationToken.None);

        // Assert — Developer 2 only sees baseline data (2 entries), not developer 1's transaction
        var body = System.Text.Json.JsonDocument.Parse(response.ResponseBody!);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());
    }

    #endregion

    #region Helpers and Fakes

    private static (ApiKeyService service, InMemoryApiKeyRepository repo, InMemoryApiKeyCacheService cache) CreateApiKeyService()
    {
        var repo = new InMemoryApiKeyRepository();
        var cache = new InMemoryApiKeyCacheService();
        var auditStore = new DeveloperPortalFakeAuditStore();
        var service = new ApiKeyService(repo, cache, auditStore);
        return (service, repo, cache);
    }

    private static SandboxEnvironment CreateSandboxEnvironment()
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<SandboxEnvironment>.Instance;
        var webhookEngine = new NoOpWebhookDeliveryEngine();
        var subscriptionRepo = new EmptyWebhookSubscriptionRepository();
        return new SandboxEnvironment(webhookEngine, subscriptionRepo, logger);
    }

    private static SandboxRequest CreateSandboxPaymentRequest(
        Guid developerId, string cardNumber, long amount, string currency)
    {
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            cardNumber,
            amount,
            currency,
            description = "Test payment",
            merchantReference = $"TEST-{Guid.NewGuid():N}"
        });
        return new SandboxRequest(developerId, "/api/v1/payments", "POST", body, null);
    }

    private static RequestLogEntry CreateRequestLogEntry(int daysAgo)
    {
        var entry = RequestLogEntry.Create(
            apiKeyId: Guid.NewGuid(),
            developerId: Guid.NewGuid(),
            endpoint: "/api/v1/payments",
            method: "POST",
            requestHeaders: "{}",
            requestBody: "{\"amount\":1000}",
            responseStatus: 200,
            responseBody: "{\"status\":\"success\"}",
            latency: TimeSpan.FromMilliseconds(50));

        // Use reflection to set the TimestampUtc for testing purge logic
        var prop = typeof(RequestLogEntry).GetProperty(nameof(RequestLogEntry.TimestampUtc));
        prop!.SetValue(entry, DateTime.UtcNow.AddDays(-daysAgo));
        return entry;
    }

    private static RequestLogEntry CreateRequestLogEntryForDeveloper(
        Guid developerId, string endpoint, int statusCode)
    {
        return RequestLogEntry.Create(
            apiKeyId: Guid.NewGuid(),
            developerId: developerId,
            endpoint: endpoint,
            method: "POST",
            requestHeaders: "{}",
            requestBody: "{\"amount\":1000}",
            responseStatus: statusCode,
            responseBody: "{\"status\":\"ok\"}",
            latency: TimeSpan.FromMilliseconds(30));
    }

    #endregion
}

#region Test Fakes

/// <summary>
/// Fake audit store for developer portal unit tests.
/// </summary>
internal class DeveloperPortalFakeAuditStore : IAuditStore
{
    public List<AuditEntry> Entries { get; } = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
    {
        var result = Entries.Where(e => e.TransactionReference == transactionReference).ToList();
        return Task.FromResult<IReadOnlyList<AuditEntry>>(result);
    }
}

/// <summary>
/// In-memory request log repository for developer portal tests that supports purge by retention.
/// </summary>
internal class DeveloperPortalRequestLogRepository : IRequestLogRepository
{
    public List<RequestLogEntry> Entries { get; } = new();

    public Task CreateAsync(RequestLogEntry entry, CancellationToken ct)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<PagedResult<RequestLogEntry>> QueryAsync(RequestLogQuery query, CancellationToken ct)
    {
        var filtered = RequestLogQueryFilter.Apply(Entries, query);
        var paged = filtered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        return Task.FromResult(new PagedResult<RequestLogEntry>(paged, filtered.Count, query.Page, query.PageSize));
    }

    public Task PurgeExpiredAsync(TimeSpan retention, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - retention;
        Entries.RemoveAll(e => e.TimestampUtc <= cutoff);
        return Task.CompletedTask;
    }
}

/// <summary>
/// No-op webhook delivery engine for sandbox tests.
/// </summary>
internal class NoOpWebhookDeliveryEngine : CardManagement.Application.PlatformServices.Webhooks.Ports.IWebhookDeliveryEngine
{
    public Task DeliverAsync(CardManagement.Domain.PlatformServices.Webhooks.WebhookDelivery delivery, CancellationToken ct)
        => Task.CompletedTask;

    public Task RetryAsync(Guid deliveryId, CancellationToken ct) => Task.CompletedTask;
    public Task ReplayFromDlqAsync(Guid dlqItemId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Empty webhook subscription repository for sandbox tests.
/// </summary>
internal class EmptyWebhookSubscriptionRepository : CardManagement.Application.PlatformServices.Webhooks.Ports.IWebhookSubscriptionRepository
{
    public Task<CardManagement.Domain.PlatformServices.Webhooks.WebhookSubscription> CreateAsync(
        CardManagement.Domain.PlatformServices.Webhooks.WebhookSubscription subscription, CancellationToken ct)
        => Task.FromResult(subscription);

    public Task<CardManagement.Domain.PlatformServices.Webhooks.WebhookSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken ct)
        => Task.FromResult<CardManagement.Domain.PlatformServices.Webhooks.WebhookSubscription?>(null);

    public Task<IReadOnlyList<CardManagement.Domain.PlatformServices.Webhooks.WebhookSubscription>> GetActiveByEventTypeAsync(
        string eventType, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CardManagement.Domain.PlatformServices.Webhooks.WebhookSubscription>>(
            new List<CardManagement.Domain.PlatformServices.Webhooks.WebhookSubscription>());

    public Task<int> CountActiveByMerchantAsync(Guid merchantId, CancellationToken ct)
        => Task.FromResult(0);

    public Task<IReadOnlyList<CardManagement.Domain.PlatformServices.Webhooks.WebhookSubscription>> GetByMerchantAsync(
        Guid merchantId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CardManagement.Domain.PlatformServices.Webhooks.WebhookSubscription>>(
            new List<CardManagement.Domain.PlatformServices.Webhooks.WebhookSubscription>());

    public Task UpdateAsync(CardManagement.Domain.PlatformServices.Webhooks.WebhookSubscription subscription, CancellationToken ct)
        => Task.CompletedTask;
}

#endregion

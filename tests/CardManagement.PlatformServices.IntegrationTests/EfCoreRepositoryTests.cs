using CardManagement.Domain.PlatformServices.AdminConsole;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.Notifications;
using CardManagement.Domain.PlatformServices.Reconciliation;
using CardManagement.Domain.PlatformServices.Webhooks;
using CardManagement.Domain.ValueObjects;
using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Infrastructure.PlatformServices.AdminConsole.Persistence;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal.Persistence;
using CardManagement.Infrastructure.PlatformServices.Notifications.Persistence;
using CardManagement.Infrastructure.PlatformServices.Reconciliation;
using CardManagement.Infrastructure.PlatformServices.Webhooks.Persistence;
using CardManagement.Infrastructure.Persistence;
using CardManagement.PlatformServices.IntegrationTests.Infrastructure;
using Xunit;

using ProcessorType = CardManagement.Domain.PlatformServices.Reconciliation.ProcessorType;

namespace CardManagement.PlatformServices.IntegrationTests;

/// <summary>
/// Integration tests for EF Core repository operations with in-memory database provider.
/// Tests CRUD operations for all 5 platform services:
/// Reconciliation, Webhook, Notification, AdminConsole, DeveloperPortal.
/// Requirements: All (end-to-end validation)
/// </summary>
public class EfCoreRepositoryTests : IDisposable
{
    private readonly CardManagementDbContext _dbContext;

    public EfCoreRepositoryTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
    }

    #region Reconciliation Repository

    [Fact]
    public async Task ReconciliationRepository_CreateBatch_PersistsAndRetrieves()
    {
        var repo = new ReconciliationRepository(_dbContext);
        var batch = ReconciliationBatch.Create(
            ProcessorType.NIBSS,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "sha256hashvalue123",
            100,
            "operator-1");

        // Act
        var created = await repo.CreateBatchAsync(batch, CancellationToken.None);
        var retrieved = await repo.GetBatchAsync(created.Id, CancellationToken.None);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(ProcessorType.NIBSS, retrieved!.Processor);
        Assert.Equal("sha256hashvalue123", retrieved.FileHash);
        Assert.Equal(BatchStatus.Pending, retrieved.Status);
    }

    [Fact]
    public async Task ReconciliationRepository_FileHashExists_ReturnsTrueForDuplicate()
    {
        var repo = new ReconciliationRepository(_dbContext);
        var batch = ReconciliationBatch.Create(
            ProcessorType.Interswitch,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "unique-file-hash-abc",
            50,
            "operator-1");

        await repo.CreateBatchAsync(batch, CancellationToken.None);

        // Act
        var exists = await repo.FileHashExistsAsync("unique-file-hash-abc", CancellationToken.None);
        var notExists = await repo.FileHashExistsAsync("different-hash", CancellationToken.None);

        // Assert
        Assert.True(exists);
        Assert.False(notExists);
    }

    [Fact]
    public async Task ReconciliationRepository_AddExceptions_PersistsAndQueries()
    {
        var repo = new ReconciliationRepository(_dbContext);
        var batch = ReconciliationBatch.Create(
            ProcessorType.Cardify,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "batch-hash-for-exceptions",
            75,
            "operator-1");
        await repo.CreateBatchAsync(batch, CancellationToken.None);

        var exception = ReconciliationException.CreateMismatch(
            batchId: batch.Id,
            settlementLineItemId: Guid.NewGuid(),
            paymentRequestId: Guid.NewGuid(),
            externalAmount: new Money(5000, "NGN"),
            internalAmount: new Money(4500, "NGN"),
            externalStatus: "settled",
            internalStatus: "completed");

        // Act
        await repo.AddExceptionsAsync(new[] { exception }, CancellationToken.None);
        var exceptions = await repo.ListExceptionsByBatchAsync(batch.Id, 10, 0, CancellationToken.None);

        // Assert
        Assert.Single(exceptions);
        Assert.Equal(ExceptionType.Mismatch, exceptions[0].Type);
    }

    [Fact]
    public async Task ReconciliationRepository_AddAdjustment_PersistsAndLists()
    {
        var repo = new ReconciliationRepository(_dbContext);
        var exceptionId = Guid.NewGuid();
        var adjustment = Adjustment.CreateAutomatic(exceptionId, new Money(500, "NGN"), "Auto-rule: small-diff", "SmallDifferenceRule");

        // Act
        await repo.AddAdjustmentAsync(adjustment, CancellationToken.None);
        var adjustments = await repo.ListAdjustmentsAsync(exceptionId, 10, 0, CancellationToken.None);

        // Assert
        Assert.Single(adjustments);
        Assert.Equal(AdjustmentType.AutoRule, adjustments[0].Type);
        Assert.Equal(new Money(500, "NGN"), adjustments[0].Amount);
    }

    #endregion

    #region Webhook Repository

    [Fact]
    public async Task WebhookSubscriptionRepository_CreateAndQuery_ByEventType()
    {
        var repo = new WebhookSubscriptionRepository(_dbContext);
        var merchantId = Guid.NewGuid();

        var sub = WebhookSubscription.Create(merchantId, "https://example.com/hook",
            new[] { "payment.completed", "payment.failed" }, "secret-123");
        await repo.CreateAsync(sub, CancellationToken.None);

        // Act
        var activeByEventType = await repo.GetActiveByEventTypeAsync("payment.completed", CancellationToken.None);
        var noMatch = await repo.GetActiveByEventTypeAsync("refund.processed", CancellationToken.None);

        // Assert
        Assert.Single(activeByEventType);
        Assert.Empty(noMatch);
    }

    [Fact]
    public async Task WebhookSubscriptionRepository_CountActiveByMerchant()
    {
        var repo = new WebhookSubscriptionRepository(_dbContext);
        var merchantId = Guid.NewGuid();

        // Create 3 subscriptions for the same merchant
        for (int i = 0; i < 3; i++)
        {
            var sub = WebhookSubscription.Create(merchantId, $"https://example.com/hook{i}",
                new[] { "payment.completed" }, $"secret-{i}");
            await repo.CreateAsync(sub, CancellationToken.None);
        }

        // Act
        var count = await repo.CountActiveByMerchantAsync(merchantId, CancellationToken.None);

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task WebhookDeliveryRepository_CreateAndQueryBySubscription()
    {
        var deliveryRepo = new WebhookDeliveryRepository(_dbContext);
        var subscriptionId = Guid.NewGuid();

        var delivery = WebhookDelivery.Create(subscriptionId, "payment.completed", """{"data":"test"}""");
        await deliveryRepo.CreateAsync(delivery, CancellationToken.None);

        // Act
        var deliveries = await deliveryRepo.GetBySubscriptionAsync(subscriptionId, 10, 0, CancellationToken.None);

        // Assert
        Assert.Single(deliveries);
        Assert.Equal(DeliveryStatus.Pending, deliveries[0].Status);
    }

    [Fact]
    public async Task WebhookDeliveryRepository_MoveToDlq_CreatesEntry()
    {
        var deliveryRepo = new WebhookDeliveryRepository(_dbContext);
        var subscriptionId = Guid.NewGuid();

        var delivery = WebhookDelivery.Create(subscriptionId, "payment.completed", """{"data":"dlq-test"}""");
        // Must fail the delivery before moving to DLQ
        delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(200), "Internal Server Error");
        await deliveryRepo.CreateAsync(delivery, CancellationToken.None);

        // Act: Move to DLQ
        await deliveryRepo.MoveToDlqAsync(delivery.Id, CancellationToken.None);

        // Assert: DLQ item exists
        var dlqItems = await deliveryRepo.GetDlqItemsAsync(subscriptionId, CancellationToken.None);
        Assert.Single(dlqItems);
        Assert.Equal(delivery.Id, dlqItems[0].DeliveryId);
    }

    #endregion

    #region Notification Repository

    [Fact]
    public async Task NotificationTemplateRepository_CreateAndRetrieveByName()
    {
        var repo = new NotificationTemplateRepository(_dbContext);
        var template = NotificationTemplate.Create(
            name: "payment-success",
            category: "payment",
            requiredVariables: new[] { "status", "reference", "amount" },
            emailSubjectTemplate: "Payment {{status}} - {{reference}}",
            emailBodyTemplate: "Your payment of {{amount}} was {{status}}.",
            smsBodyTemplate: "Payment {{status}}: {{amount}}",
            whatsAppBodyTemplate: "Payment {{status}}: {{amount}}");

        await repo.CreateAsync(template, CancellationToken.None);

        // Act
        var retrieved = await repo.GetByNameAsync("payment-success", CancellationToken.None);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("payment-success", retrieved!.Name);
        Assert.Equal("payment", retrieved.Category);
        Assert.Contains("status", retrieved.RequiredVariables);
    }

    [Fact]
    public async Task NotificationPreferenceRepository_CreateAndRetrieve()
    {
        var repo = new NotificationPreferenceRepository(_dbContext);
        var preference = NotificationPreference.Create(
            recipientId: "user-123",
            primaryChannel: NotificationChannel.Email,
            fallbackChannel: NotificationChannel.Sms);

        await repo.SaveAsync(preference, CancellationToken.None);

        // Act
        var retrieved = await repo.GetByRecipientAsync("user-123", CancellationToken.None);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(NotificationChannel.Email, retrieved!.PrimaryChannel);
        Assert.Equal(NotificationChannel.Sms, retrieved.FallbackChannel);
    }

    [Fact]
    public async Task DeliveryLogRepository_CreateAndQuery()
    {
        var repo = new DeliveryLogRepository(_dbContext);
        var templateId = Guid.NewGuid();

        var logEntry = DeliveryLogEntry.Create("user-456", NotificationChannel.Sms, templateId);
        await repo.CreateAsync(logEntry, CancellationToken.None);

        // Act
        var entries = await repo.GetByRecipientAsync("user-456", 10, CancellationToken.None);

        // Assert
        Assert.Single(entries);
        Assert.Equal(NotificationChannel.Sms, entries[0].Channel);
    }

    #endregion

    #region Admin Console Repository

    [Fact]
    public async Task AdminCommandRepository_CreateAndRetrieve()
    {
        var repo = new AdminCommandRepository(_dbContext);
        var command = PendingCommand.Create(
            commandType: "adjustment.create",
            serializedParameters: """{"amount":5000,"reason":"test"}""",
            makerId: "operator-1",
            expiresAtUtc: DateTime.UtcNow.AddHours(24));

        await repo.CreateAsync(command, CancellationToken.None);

        // Act
        var retrieved = await repo.GetByIdAsync(command.Id, CancellationToken.None);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("adjustment.create", retrieved!.CommandType);
        Assert.Equal(CommandStatus.Pending, retrieved.Status);
        Assert.Equal("operator-1", retrieved.MakerId);
    }

    [Fact]
    public async Task AdminCommandRepository_GetExpired_ReturnsOnlyExpiredCommands()
    {
        var repo = new AdminCommandRepository(_dbContext);

        // Create a command that will expire immediately (set very short expiry and wait)
        var expiredCommand = PendingCommand.Create(
            commandType: "refund.execute",
            serializedParameters: """{"refundId":"123"}""",
            makerId: "operator-1",
            expiresAtUtc: DateTime.UtcNow.AddMilliseconds(50));

        // Create a command that's not expired
        var activeCommand = PendingCommand.Create(
            commandType: "config.update",
            serializedParameters: """{"key":"value"}""",
            makerId: "operator-2",
            expiresAtUtc: DateTime.UtcNow.AddHours(24));

        await repo.CreateAsync(expiredCommand, CancellationToken.None);
        await repo.CreateAsync(activeCommand, CancellationToken.None);

        // Wait for the expired command to actually expire
        await Task.Delay(100);

        // Act
        var expired = await repo.GetExpiredAsync(TimeSpan.Zero, CancellationToken.None);

        // Assert: Only the expired command is returned
        Assert.Single(expired);
        Assert.Equal("refund.execute", expired[0].CommandType);
    }

    [Fact]
    public async Task AdminRoleRepository_AssignAndRetrieveRoles()
    {
        var repo = new AdminRoleRepository(_dbContext);
        var userId = "admin-user-1";

        var role = AdminRole.Create(userId, "operations", "security-admin-1");
        await repo.CreateAsync(role, CancellationToken.None);

        // Act
        var roles = await repo.GetActiveByUserAsync(userId, CancellationToken.None);

        // Assert
        Assert.Single(roles);
        Assert.Equal("operations", roles[0].Role);
        Assert.True(roles[0].IsActive);
    }

    #endregion

    #region Developer Portal Repository

    [Fact]
    public async Task ApiKeyRepository_CreateAndGetByHash()
    {
        var repo = new ApiKeyRepository(_dbContext);
        var developerId = Guid.NewGuid();
        var rawKey = "test-raw-key-for-hash-lookup-12345678";
        var apiKey = ApiKey.CreateFromRawKey(developerId, rawKey, new[] { "payments:read" }, false, null);

        await repo.CreateAsync(apiKey, CancellationToken.None);

        // Act
        var keyHash = ApiKey.ComputeHash(rawKey);
        var retrieved = await repo.GetByHashAsync(keyHash, CancellationToken.None);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(developerId, retrieved!.DeveloperId);
        Assert.Equal(KeyStatus.Active, retrieved.Status);
    }

    [Fact]
    public async Task ApiKeyRepository_CountActiveByDeveloper_RespectsLimit()
    {
        var repo = new ApiKeyRepository(_dbContext);
        var developerId = Guid.NewGuid();

        // Create 5 active keys
        for (int i = 0; i < 5; i++)
        {
            var key = ApiKey.CreateFromRawKey(developerId, $"key-{i}-{Guid.NewGuid():N}",
                new[] { "read" }, false, null);
            await repo.CreateAsync(key, CancellationToken.None);
        }

        // Act
        var count = await repo.CountActiveByDeveloperAsync(developerId, CancellationToken.None);

        // Assert
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task RequestLogRepository_CreateAndQuery()
    {
        var repo = new RequestLogRepository(_dbContext);
        var developerId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();

        var entry = RequestLogEntry.Create(
            apiKeyId: apiKeyId,
            developerId: developerId,
            endpoint: "/api/v1/payments",
            method: "POST",
            requestHeaders: """{"Content-Type":"application/json"}""",
            requestBody: """{"amount":5000}""",
            responseStatus: 200,
            responseBody: """{"id":"txn-123"}""",
            latency: TimeSpan.FromMilliseconds(45));

        await repo.CreateAsync(entry, CancellationToken.None);

        // Act
        var query = new RequestLogQuery(
            DeveloperId: developerId,
            Page: 1,
            PageSize: 10);
        var results = await repo.QueryAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(1, results.TotalCount);
        Assert.Single(results.Items);
        Assert.Equal("/api/v1/payments", results.Items[0].Endpoint);
    }

    #endregion

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}

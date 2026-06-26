using CardManagement.Application.DTOs;
using CardManagement.Infrastructure.Idempotency;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class IdempotencyGuardTests : IDisposable
{
    private readonly CardManagementDbContext _dbContext;
    private readonly IdempotencyGuard _guard;

    public IdempotencyGuardTests()
    {
        var options = new DbContextOptionsBuilder<CardManagementDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new CardManagementDbContext(options);
        _guard = new IdempotencyGuard(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CheckAsync_NewKey_ReturnsNotFound()
    {
        // Arrange
        var key = "new-unique-key-" + Guid.NewGuid();

        // Act
        var result = await _guard.CheckAsync(key, CancellationToken.None);

        // Assert
        Assert.IsType<IdempotencyCheckResult.NotFound>(result);
    }

    [Fact]
    public async Task CheckAsync_CompletedKey_ReturnsStoredResponse()
    {
        // Arrange
        var key = "completed-key-" + Guid.NewGuid();
        var paymentRequestId = Guid.NewGuid();
        var response = new { Status = "Success", Amount = 100.50m };

        await _guard.StoreAsync(key, paymentRequestId, response, CancellationToken.None);

        // Act
        var result = await _guard.CheckAsync(key, CancellationToken.None);

        // Assert
        var completed = Assert.IsType<IdempotencyCheckResult.Completed>(result);
        Assert.NotNull(completed.CachedResponse);
    }

    [Fact]
    public async Task CheckAsync_InProgressKey_ReturnsConflict()
    {
        // Arrange
        var key = "in-progress-key-" + Guid.NewGuid();
        var paymentRequestId = Guid.NewGuid();

        // Register as in-progress (no response stored yet)
        await _guard.RegisterInProgressAsync(key, paymentRequestId, CancellationToken.None);

        // Act
        var result = await _guard.CheckAsync(key, CancellationToken.None);

        // Assert
        Assert.IsType<IdempotencyCheckResult.InProgress>(result);
    }

    [Fact]
    public async Task CheckAsync_MissingKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _guard.CheckAsync("", CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(
            () => _guard.CheckAsync("   ", CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(
            () => _guard.CheckAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_ExpiredRecord_ReturnsNotFound()
    {
        // Arrange
        var key = "expired-key-" + Guid.NewGuid();
        var expiredRecord = new IdempotencyRecordEntity
        {
            Key = key,
            RequestId = Guid.NewGuid(),
            ResponsePayload = "{\"Status\":\"Success\"}",
            IsCompleted = true,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-48),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(-24) // Already expired
        };

        _dbContext.IdempotencyRecords.Add(expiredRecord);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _guard.CheckAsync(key, CancellationToken.None);

        // Assert
        Assert.IsType<IdempotencyCheckResult.NotFound>(result);
    }

    [Fact]
    public async Task StoreAsync_CreatesCompletedRecord()
    {
        // Arrange
        var key = "store-key-" + Guid.NewGuid();
        var paymentRequestId = Guid.NewGuid();
        var response = new { TransactionRef = "TX123" };

        // Act
        await _guard.StoreAsync(key, paymentRequestId, response, CancellationToken.None);

        // Assert
        var record = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key);

        Assert.NotNull(record);
        Assert.True(record.IsCompleted);
        Assert.NotNull(record.ResponsePayload);
        Assert.Equal(paymentRequestId, record.RequestId);
        Assert.True(record.ExpiresAtUtc > DateTime.UtcNow.AddHours(23));
    }

    [Fact]
    public async Task StoreAsync_UpdatesInProgressToCompleted()
    {
        // Arrange
        var key = "update-key-" + Guid.NewGuid();
        var paymentRequestId = Guid.NewGuid();
        var response = new { Status = "Done" };

        // First register as in-progress
        await _guard.RegisterInProgressAsync(key, paymentRequestId, CancellationToken.None);

        // Act - complete the request
        await _guard.StoreAsync(key, paymentRequestId, response, CancellationToken.None);

        // Assert
        var record = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key);

        Assert.NotNull(record);
        Assert.True(record.IsCompleted);
        Assert.NotNull(record.ResponsePayload);
    }

    [Fact]
    public async Task StoreAsync_MissingKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _guard.StoreAsync("", Guid.NewGuid(), new { }, CancellationToken.None));
    }

    [Fact]
    public async Task RegisterInProgressAsync_CreatesInProgressRecord()
    {
        // Arrange
        var key = "register-key-" + Guid.NewGuid();
        var paymentRequestId = Guid.NewGuid();

        // Act
        await _guard.RegisterInProgressAsync(key, paymentRequestId, CancellationToken.None);

        // Assert
        var record = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key);

        Assert.NotNull(record);
        Assert.False(record.IsCompleted);
        Assert.Null(record.ResponsePayload);
        Assert.Equal(paymentRequestId, record.RequestId);
    }
}

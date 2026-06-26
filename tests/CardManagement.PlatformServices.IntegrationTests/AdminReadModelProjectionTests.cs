using System.Text.Json;
using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using CardManagement.Infrastructure.PlatformServices.AdminConsole;
using CardManagement.Infrastructure.Persistence;
using CardManagement.PlatformServices.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace CardManagement.PlatformServices.IntegrationTests;

/// <summary>
/// Integration tests for Admin read model projection from Kafka events to query responses.
/// Simulates Kafka event → projector updates read model → query returns projected data.
/// Requirements: 11.1, 11.2
/// </summary>
public class AdminReadModelProjectionTests : IDisposable
{
    private readonly CardManagementDbContext _dbContext;
    private readonly FakeRedisConnectionMultiplexer _fakeRedis;
    private readonly ReadModelStalenessTracker _stalenessTracker;
    private readonly TransactionSummaryProjector _transactionProjector;
    private readonly AdminReadModelProjector _compositeProjector;

    public AdminReadModelProjectionTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
        _fakeRedis = new FakeRedisConnectionMultiplexer();
        _stalenessTracker = new ReadModelStalenessTracker(_fakeRedis.Multiplexer, NullLogger<ReadModelStalenessTracker>.Instance);

        _transactionProjector = new TransactionSummaryProjector(
            _dbContext, _stalenessTracker, NullLogger<TransactionSummaryProjector>.Instance);

        var reconciliationProjector = new ReconciliationStatusProjector(
            _dbContext, _stalenessTracker, NullLogger<ReconciliationStatusProjector>.Instance);

        var disputeProjector = new DisputeMetricsProjector(
            _dbContext, _stalenessTracker, NullLogger<DisputeMetricsProjector>.Instance);

        var channelHealthProjector = new ChannelHealthProjector(
            _dbContext, _stalenessTracker, NullLogger<ChannelHealthProjector>.Instance);

        _compositeProjector = new AdminReadModelProjector(
            _transactionProjector,
            reconciliationProjector,
            disputeProjector,
            channelHealthProjector,
            NullLogger<AdminReadModelProjector>.Instance);
    }

    [Fact]
    public async Task ProjectPaymentCompleted_CreatesTransactionSummary()
    {
        // Arrange: Simulate a payment.completed Kafka event
        var eventPayload = JsonSerializer.Serialize(new
        {
            transactionDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            channel = "card",
            amountKobo = 500000L,
            transactionReference = "TXN-001",
            status = "completed"
        });

        // Act: Project the event
        await _compositeProjector.ProjectEventAsync("payment.completed", eventPayload, CancellationToken.None);

        // Assert: Read model is updated
        var summaries = await _dbContext.AdminTransactionSummaries.ToListAsync();
        Assert.Single(summaries);
        Assert.Equal("card", summaries[0].Channel);
        Assert.Equal(1, summaries[0].TotalCount);
        Assert.Equal(1, summaries[0].SuccessCount);
        Assert.Equal(0, summaries[0].FailedCount);
        Assert.Equal(500000L, summaries[0].TotalAmountKobo);
    }

    [Fact]
    public async Task ProjectMultipleEvents_AggregatesCorrectly()
    {
        // Arrange: Project multiple events for the same date/channel
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = new[]
        {
            ("payment.completed", new { transactionDate = date.ToString("yyyy-MM-dd"), channel = "card", amountKobo = 100000L, transactionReference = "TXN-001", status = "completed" }),
            ("payment.completed", new { transactionDate = date.ToString("yyyy-MM-dd"), channel = "card", amountKobo = 200000L, transactionReference = "TXN-002", status = "completed" }),
            ("payment.failed", new { transactionDate = date.ToString("yyyy-MM-dd"), channel = "card", amountKobo = 50000L, transactionReference = "TXN-003", status = "failed" }),
        };

        // Act
        foreach (var (eventType, payload) in events)
        {
            var json = JsonSerializer.Serialize(payload);
            await _compositeProjector.ProjectEventAsync(eventType, json, CancellationToken.None);
        }

        // Assert
        var summaries = await _dbContext.AdminTransactionSummaries.ToListAsync();
        Assert.Single(summaries);
        Assert.Equal(3, summaries[0].TotalCount);
        Assert.Equal(2, summaries[0].SuccessCount);
        Assert.Equal(1, summaries[0].FailedCount);
        Assert.Equal(350000L, summaries[0].TotalAmountKobo);
    }

    [Fact]
    public async Task ProjectEvent_UpdatesStalenessTracker()
    {
        // Arrange
        var eventPayload = JsonSerializer.Serialize(new
        {
            transactionDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            channel = "ussd",
            amountKobo = 10000L,
            transactionReference = "TXN-010",
            status = "completed"
        });

        // Act
        await _compositeProjector.ProjectEventAsync("payment.completed", eventPayload, CancellationToken.None);

        // Assert: Staleness tracker should have been updated
        var lastProjected = await _stalenessTracker.GetLastProjectedAtAsync(
            TransactionSummaryProjector.ReadModelName, CancellationToken.None);
        Assert.NotNull(lastProjected);
        Assert.True((DateTime.UtcNow - lastProjected.Value).TotalSeconds < 5);
    }

    [Fact]
    public async Task ProjectEvent_DifferentChannels_CreatesSeparateSummaries()
    {
        // Arrange: Events from different channels
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var cardEvent = JsonSerializer.Serialize(new
        {
            transactionDate = date.ToString("yyyy-MM-dd"),
            channel = "card",
            amountKobo = 100000L,
            transactionReference = "TXN-C1",
            status = "completed"
        });
        var ussdEvent = JsonSerializer.Serialize(new
        {
            transactionDate = date.ToString("yyyy-MM-dd"),
            channel = "ussd",
            amountKobo = 50000L,
            transactionReference = "TXN-U1",
            status = "completed"
        });

        // Act
        await _compositeProjector.ProjectEventAsync("payment.completed", cardEvent, CancellationToken.None);
        await _compositeProjector.ProjectEventAsync("payment.completed", ussdEvent, CancellationToken.None);

        // Assert: Two separate summaries
        var summaries = await _dbContext.AdminTransactionSummaries.ToListAsync();
        Assert.Equal(2, summaries.Count);
        Assert.Contains(summaries, s => s.Channel == "card" && s.TotalAmountKobo == 100000L);
        Assert.Contains(summaries, s => s.Channel == "ussd" && s.TotalAmountKobo == 50000L);
    }

    [Fact]
    public async Task ProjectEvent_UnknownEventType_NoProjection()
    {
        // Arrange
        var eventPayload = """{"someField":"someValue"}""";

        // Act
        await _compositeProjector.ProjectEventAsync("unknown.event.type", eventPayload, CancellationToken.None);

        // Assert: No data projected
        var summaries = await _dbContext.AdminTransactionSummaries.ToListAsync();
        Assert.Empty(summaries);
    }

    [Fact]
    public async Task StalenessCheck_StaleModel_ReportsStale()
    {
        // Arrange: Don't project any events (read model never updated)

        // Act
        var (isStale, _) = await _stalenessTracker.CheckStalenessAsync(
            TransactionSummaryProjector.ReadModelName, TimeSpan.FromMinutes(5), CancellationToken.None);

        // Assert: Model is stale because it was never projected
        Assert.True(isStale);
    }

    [Fact]
    public async Task StalenessCheck_FreshModel_ReportsNotStale()
    {
        // Arrange: Project an event to update staleness
        var eventPayload = JsonSerializer.Serialize(new
        {
            transactionDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            channel = "card",
            amountKobo = 10000L,
            transactionReference = "TXN-S1",
            status = "completed"
        });
        await _compositeProjector.ProjectEventAsync("payment.completed", eventPayload, CancellationToken.None);

        // Act: Check staleness with a generous threshold
        var (isStale, _) = await _stalenessTracker.CheckStalenessAsync(
            TransactionSummaryProjector.ReadModelName, TimeSpan.FromMinutes(5), CancellationToken.None);

        // Assert: Not stale since it was just projected
        Assert.False(isStale);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}

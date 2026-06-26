using System.Text.Json;
using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole;

/// <summary>
/// Projects reconciliation batch-completed events into the <see cref="ReconciliationStatusReadModel"/>.
/// Handles event type: platform.reconciliation.batch-completed.
/// </summary>
public sealed class ReconciliationStatusProjector
{
    public const string ReadModelName = "reconciliation-status";

    private static readonly HashSet<string> HandledEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "platform.reconciliation.batch-completed"
    };

    private readonly CardManagementDbContext _dbContext;
    private readonly ReadModelStalenessTracker _stalenessTracker;
    private readonly ILogger<ReconciliationStatusProjector> _logger;

    public ReconciliationStatusProjector(
        CardManagementDbContext dbContext,
        ReadModelStalenessTracker stalenessTracker,
        ILogger<ReconciliationStatusProjector> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _stalenessTracker = stalenessTracker ?? throw new ArgumentNullException(nameof(stalenessTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(string eventType) => HandledEventTypes.Contains(eventType);

    public async Task ProjectAsync(string eventType, string eventPayload, CancellationToken ct)
    {
        if (!CanHandle(eventType))
            return;

        BatchCompletedEvent? batchEvent;
        try
        {
            batchEvent = JsonSerializer.Deserialize<BatchCompletedEvent>(eventPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize batch-completed event payload.");
            return;
        }

        if (batchEvent is null || batchEvent.BatchId == Guid.Empty)
        {
            _logger.LogDebug("Deserialized batch-completed event is null or missing BatchId. Skipping.");
            return;
        }

        var existing = await _dbContext.AdminReconciliationStatuses
            .FirstOrDefaultAsync(s => s.BatchId == batchEvent.BatchId, ct);

        if (existing is null)
        {
            existing = new ReconciliationStatusReadModel
            {
                BatchId = batchEvent.BatchId,
                Processor = batchEvent.Processor ?? "unknown",
                SettlementDate = batchEvent.SettlementDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                TotalRecords = batchEvent.TotalRecords,
                MatchedRecords = batchEvent.MatchedRecords,
                ExceptionCount = batchEvent.ExceptionCount,
                ResolvedCount = batchEvent.ResolvedCount,
                ProjectedAtUtc = DateTime.UtcNow
            };
            _dbContext.AdminReconciliationStatuses.Add(existing);
        }
        else
        {
            existing.TotalRecords = batchEvent.TotalRecords;
            existing.MatchedRecords = batchEvent.MatchedRecords;
            existing.ExceptionCount = batchEvent.ExceptionCount;
            existing.ResolvedCount = batchEvent.ResolvedCount;
            existing.ProjectedAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(ct);
        await _stalenessTracker.RecordProjectionAsync(ReadModelName, ct);

        _logger.LogDebug(
            "Projected reconciliation status: BatchId={BatchId}, Processor={Processor}.",
            batchEvent.BatchId, batchEvent.Processor);
    }

    /// <summary>
    /// Internal DTO for deserializing batch-completed event payloads.
    /// </summary>
    internal sealed class BatchCompletedEvent
    {
        public Guid BatchId { get; set; }
        public string? Processor { get; set; }
        public DateOnly? SettlementDate { get; set; }
        public int TotalRecords { get; set; }
        public int MatchedRecords { get; set; }
        public int ExceptionCount { get; set; }
        public int ResolvedCount { get; set; }
    }
}

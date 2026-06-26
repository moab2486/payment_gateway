using System.Text.Json;
using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole;

/// <summary>
/// Projects dispute lifecycle events into the <see cref="DisputeMetricsReadModel"/>.
/// Handles event types: dispute.created, dispute.resolved, dispute.escalated.
/// </summary>
public sealed class DisputeMetricsProjector
{
    public const string ReadModelName = "dispute-metrics";

    private static readonly HashSet<string> HandledEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "dispute.created",
        "dispute.resolved",
        "dispute.escalated"
    };

    private readonly CardManagementDbContext _dbContext;
    private readonly ReadModelStalenessTracker _stalenessTracker;
    private readonly ILogger<DisputeMetricsProjector> _logger;

    public DisputeMetricsProjector(
        CardManagementDbContext dbContext,
        ReadModelStalenessTracker stalenessTracker,
        ILogger<DisputeMetricsProjector> logger)
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

        DisputeEvent? disputeEvent;
        try
        {
            disputeEvent = JsonSerializer.Deserialize<DisputeEvent>(eventPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize dispute event payload for type '{EventType}'.", eventType);
            return;
        }

        if (disputeEvent is null)
        {
            _logger.LogDebug("Deserialized dispute event is null. Skipping.");
            return;
        }

        var date = disputeEvent.DisputeDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var disputeType = disputeEvent.DisputeType ?? "unknown";

        var existing = await _dbContext.AdminDisputeMetrics
            .FirstOrDefaultAsync(s => s.Date == date && s.DisputeType == disputeType, ct);

        if (existing is null)
        {
            existing = new DisputeMetricsReadModel
            {
                Date = date,
                DisputeType = disputeType,
                OpenedCount = 0,
                ResolvedCount = 0,
                EscalatedCount = 0,
                TotalDisputedAmountKobo = 0,
                AverageResolutionHours = 0,
                ProjectedAtUtc = DateTime.UtcNow
            };
            _dbContext.AdminDisputeMetrics.Add(existing);
        }

        existing.ProjectedAtUtc = DateTime.UtcNow;

        switch (eventType.ToLowerInvariant())
        {
            case "dispute.created":
                existing.OpenedCount++;
                existing.TotalDisputedAmountKobo += disputeEvent.AmountKobo;
                break;
            case "dispute.resolved":
                existing.ResolvedCount++;
                if (disputeEvent.ResolutionHours > 0)
                {
                    // Compute running average
                    var totalResolved = existing.ResolvedCount;
                    existing.AverageResolutionHours =
                        ((existing.AverageResolutionHours * (totalResolved - 1)) + disputeEvent.ResolutionHours) / totalResolved;
                }
                break;
            case "dispute.escalated":
                existing.EscalatedCount++;
                break;
        }

        await _dbContext.SaveChangesAsync(ct);
        await _stalenessTracker.RecordProjectionAsync(ReadModelName, ct);

        _logger.LogDebug(
            "Projected dispute metrics: Date={Date}, Type={DisputeType}, EventType={EventType}.",
            date, disputeType, eventType);
    }

    /// <summary>
    /// Internal DTO for deserializing dispute event payloads.
    /// </summary>
    internal sealed class DisputeEvent
    {
        public DateOnly? DisputeDate { get; set; }
        public string? DisputeType { get; set; }
        public long AmountKobo { get; set; }
        public double ResolutionHours { get; set; }
        public Guid? DisputeId { get; set; }
    }
}

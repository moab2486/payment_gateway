using System.Text.Json;
using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole;

/// <summary>
/// Projects payment state change events into the <see cref="TransactionSummaryReadModel"/>.
/// Aggregates payment counts and amounts by date and channel.
/// Handles event types: payment.completed, payment.failed, payment.reversed.
/// </summary>
public sealed class TransactionSummaryProjector
{
    public const string ReadModelName = "transaction-summary";

    private static readonly HashSet<string> HandledEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "payment.completed",
        "payment.failed",
        "payment.reversed"
    };

    private readonly CardManagementDbContext _dbContext;
    private readonly ReadModelStalenessTracker _stalenessTracker;
    private readonly ILogger<TransactionSummaryProjector> _logger;

    public TransactionSummaryProjector(
        CardManagementDbContext dbContext,
        ReadModelStalenessTracker stalenessTracker,
        ILogger<TransactionSummaryProjector> logger)
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

        TransactionEvent? txEvent;
        try
        {
            txEvent = JsonSerializer.Deserialize<TransactionEvent>(eventPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize transaction event payload for type '{EventType}'.", eventType);
            return;
        }

        if (txEvent is null)
        {
            _logger.LogDebug("Deserialized transaction event is null. Skipping.");
            return;
        }

        var date = txEvent.TransactionDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var channel = txEvent.Channel ?? "unknown";

        var existing = await _dbContext.AdminTransactionSummaries
            .FirstOrDefaultAsync(s => s.Date == date && s.Channel == channel, ct);

        if (existing is null)
        {
            existing = new TransactionSummaryReadModel
            {
                Date = date,
                Channel = channel,
                TotalCount = 0,
                SuccessCount = 0,
                FailedCount = 0,
                TotalAmountKobo = 0,
                ProjectedAtUtc = DateTime.UtcNow
            };
            _dbContext.AdminTransactionSummaries.Add(existing);
        }

        existing.TotalCount++;
        existing.TotalAmountKobo += txEvent.AmountKobo;
        existing.ProjectedAtUtc = DateTime.UtcNow;

        switch (eventType.ToLowerInvariant())
        {
            case "payment.completed":
                existing.SuccessCount++;
                break;
            case "payment.failed":
            case "payment.reversed":
                existing.FailedCount++;
                break;
        }

        await _dbContext.SaveChangesAsync(ct);
        await _stalenessTracker.RecordProjectionAsync(ReadModelName, ct);

        _logger.LogDebug(
            "Projected transaction summary: Date={Date}, Channel={Channel}, EventType={EventType}.",
            date, channel, eventType);
    }

    /// <summary>
    /// Internal DTO for deserializing payment event payloads.
    /// </summary>
    internal sealed class TransactionEvent
    {
        public DateOnly? TransactionDate { get; set; }
        public string? Channel { get; set; }
        public long AmountKobo { get; set; }
        public string? TransactionReference { get; set; }
        public string? Status { get; set; }
    }
}

using System.Text.Json;
using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole;

/// <summary>
/// Projects delivery success/failure events into the <see cref="ChannelHealthReadModel"/>.
/// Tracks delivery health metrics per channel (webhook, email, sms, whatsapp).
/// Handles event types: delivery.success, delivery.failure.
/// </summary>
public sealed class ChannelHealthProjector
{
    public const string ReadModelName = "channel-health";

    private static readonly HashSet<string> HandledEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivery.success",
        "delivery.failure"
    };

    private readonly CardManagementDbContext _dbContext;
    private readonly ReadModelStalenessTracker _stalenessTracker;
    private readonly ILogger<ChannelHealthProjector> _logger;

    public ChannelHealthProjector(
        CardManagementDbContext dbContext,
        ReadModelStalenessTracker stalenessTracker,
        ILogger<ChannelHealthProjector> logger)
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

        DeliveryEvent? deliveryEvent;
        try
        {
            deliveryEvent = JsonSerializer.Deserialize<DeliveryEvent>(eventPayload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize delivery event payload for type '{EventType}'.", eventType);
            return;
        }

        if (deliveryEvent is null)
        {
            _logger.LogDebug("Deserialized delivery event is null. Skipping.");
            return;
        }

        var date = deliveryEvent.DeliveryDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var channelName = deliveryEvent.ChannelName ?? "unknown";

        var existing = await _dbContext.AdminChannelHealth
            .FirstOrDefaultAsync(s => s.Date == date && s.ChannelName == channelName, ct);

        if (existing is null)
        {
            existing = new ChannelHealthReadModel
            {
                Date = date,
                ChannelName = channelName,
                TotalDeliveries = 0,
                SuccessfulDeliveries = 0,
                FailedDeliveries = 0,
                AverageLatencyMs = 0,
                UptimePercentage = 100.0,
                ProjectedAtUtc = DateTime.UtcNow
            };
            _dbContext.AdminChannelHealth.Add(existing);
        }

        existing.TotalDeliveries++;
        existing.ProjectedAtUtc = DateTime.UtcNow;

        switch (eventType.ToLowerInvariant())
        {
            case "delivery.success":
                existing.SuccessfulDeliveries++;
                // Update running average latency
                if (deliveryEvent.LatencyMs > 0)
                {
                    existing.AverageLatencyMs =
                        ((existing.AverageLatencyMs * (existing.TotalDeliveries - 1)) + deliveryEvent.LatencyMs) /
                        existing.TotalDeliveries;
                }
                break;
            case "delivery.failure":
                existing.FailedDeliveries++;
                break;
        }

        // Recalculate uptime percentage
        if (existing.TotalDeliveries > 0)
        {
            existing.UptimePercentage = (double)existing.SuccessfulDeliveries / existing.TotalDeliveries * 100.0;
        }

        await _dbContext.SaveChangesAsync(ct);
        await _stalenessTracker.RecordProjectionAsync(ReadModelName, ct);

        _logger.LogDebug(
            "Projected channel health: Date={Date}, Channel={ChannelName}, EventType={EventType}.",
            date, channelName, eventType);
    }

    /// <summary>
    /// Internal DTO for deserializing delivery event payloads.
    /// </summary>
    internal sealed class DeliveryEvent
    {
        public DateOnly? DeliveryDate { get; set; }
        public string? ChannelName { get; set; }
        public double LatencyMs { get; set; }
        public string? ErrorMessage { get; set; }
    }
}

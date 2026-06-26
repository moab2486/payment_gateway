using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole;

/// <summary>
/// Composite implementation of <see cref="IAdminReadModelProjector"/> that delegates
/// domain events to the appropriate individual projector(s).
/// </summary>
public sealed class AdminReadModelProjector : IAdminReadModelProjector
{
    private readonly TransactionSummaryProjector _transactionSummaryProjector;
    private readonly ReconciliationStatusProjector _reconciliationStatusProjector;
    private readonly DisputeMetricsProjector _disputeMetricsProjector;
    private readonly ChannelHealthProjector _channelHealthProjector;
    private readonly ILogger<AdminReadModelProjector> _logger;

    public AdminReadModelProjector(
        TransactionSummaryProjector transactionSummaryProjector,
        ReconciliationStatusProjector reconciliationStatusProjector,
        DisputeMetricsProjector disputeMetricsProjector,
        ChannelHealthProjector channelHealthProjector,
        ILogger<AdminReadModelProjector> logger)
    {
        _transactionSummaryProjector = transactionSummaryProjector ?? throw new ArgumentNullException(nameof(transactionSummaryProjector));
        _reconciliationStatusProjector = reconciliationStatusProjector ?? throw new ArgumentNullException(nameof(reconciliationStatusProjector));
        _disputeMetricsProjector = disputeMetricsProjector ?? throw new ArgumentNullException(nameof(disputeMetricsProjector));
        _channelHealthProjector = channelHealthProjector ?? throw new ArgumentNullException(nameof(channelHealthProjector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task ProjectEventAsync(string eventType, string eventPayload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            _logger.LogDebug("Received empty event type. Skipping projection.");
            return;
        }

        var projected = false;

        if (_transactionSummaryProjector.CanHandle(eventType))
        {
            await _transactionSummaryProjector.ProjectAsync(eventType, eventPayload, ct);
            projected = true;
        }

        if (_reconciliationStatusProjector.CanHandle(eventType))
        {
            await _reconciliationStatusProjector.ProjectAsync(eventType, eventPayload, ct);
            projected = true;
        }

        if (_disputeMetricsProjector.CanHandle(eventType))
        {
            await _disputeMetricsProjector.ProjectAsync(eventType, eventPayload, ct);
            projected = true;
        }

        if (_channelHealthProjector.CanHandle(eventType))
        {
            await _channelHealthProjector.ProjectAsync(eventType, eventPayload, ct);
            projected = true;
        }

        if (!projected)
        {
            _logger.LogDebug("No projector handled event type '{EventType}'.", eventType);
        }
    }
}

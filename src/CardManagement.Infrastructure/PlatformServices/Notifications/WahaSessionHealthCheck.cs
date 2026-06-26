using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Health check that verifies the WAHA WhatsApp session is active on startup.
/// Can also be used in the application's health check endpoint for ongoing monitoring.
/// </summary>
public sealed class WahaSessionHealthCheck : IHealthCheck
{
    private readonly WhatsAppChannelAdapter _adapter;
    private readonly WhatsAppChannelOptions _options;
    private readonly ILogger<WahaSessionHealthCheck> _logger;

    public WahaSessionHealthCheck(
        WhatsAppChannelAdapter adapter,
        IOptions<WhatsAppChannelOptions> options,
        ILogger<WahaSessionHealthCheck> logger)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_options.HealthCheckOnStartup)
        {
            return HealthCheckResult.Healthy("WAHA health check is disabled via configuration.");
        }

        try
        {
            var isHealthy = await _adapter.CheckSessionHealthAsync(cancellationToken);

            if (isHealthy)
            {
                return HealthCheckResult.Healthy(
                    $"WAHA session '{_options.Session}' is active and healthy.");
            }

            return HealthCheckResult.Degraded(
                $"WAHA session '{_options.Session}' is not in a healthy state.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WAHA health check failed with an exception.");
            return HealthCheckResult.Unhealthy(
                $"WAHA session health check failed: {ex.Message}", ex);
        }
    }
}

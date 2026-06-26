using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole;

/// <summary>
/// Background service that periodically checks for stale pending admin commands
/// and marks them as expired. This enforces the time-bounded nature of the
/// maker-checker workflow — commands that are not approved or rejected within
/// the configured expiry period are automatically expired by this service.
/// </summary>
public sealed class CommandExpiryBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommandExpiryBackgroundService> _logger;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _expiryThreshold;

    public CommandExpiryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CommandExpiryBackgroundService> logger,
        TimeSpan? checkInterval = null,
        TimeSpan? expiryThreshold = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _checkInterval = checkInterval ?? TimeSpan.FromMinutes(5);
        _expiryThreshold = expiryThreshold ?? TimeSpan.FromHours(24);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CommandExpiryBackgroundService started. Check interval: {Interval}, Expiry threshold: {Threshold}",
            _checkInterval, _expiryThreshold);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
                await ExpireStaleCommandsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during command expiry check");
            }
        }

        _logger.LogInformation("CommandExpiryBackgroundService stopped.");
    }

    /// <summary>
    /// Expires stale pending commands by delegating to IMakerCheckerWorkflow.ExpireStaleCommandsAsync.
    /// Uses a scoped service provider to resolve dependencies.
    /// </summary>
    internal async Task<int> ExpireStaleCommandsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IMakerCheckerWorkflow>();

        await workflow.ExpireStaleCommandsAsync(ct);

        _logger.LogDebug("Command expiry check completed.");
        // Note: The actual count is not returned by the workflow interface,
        // but the workflow handles auditing each expiry internally.
        return 0;
    }
}

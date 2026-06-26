namespace CardManagement.Application.PlatformServices.AdminConsole.Ports;

/// <summary>
/// Event publisher for admin console domain events to Kafka.
/// Publishes to platform.admin.* topics for downstream consumption (e.g., notification to checkers).
/// </summary>
public interface IAdminConsoleEventPublisher
{
    /// <summary>
    /// Publishes a command-pending-approval event to notify eligible checkers.
    /// Topic: platform.admin.command-pending-approval
    /// </summary>
    Task PublishCommandPendingApprovalAsync(
        Guid commandId,
        string commandType,
        string makerId,
        DateTime expiresAtUtc,
        CancellationToken ct);
}

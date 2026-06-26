namespace CardManagement.Application.PlatformServices.AdminConsole.Ports;

/// <summary>
/// Projects domain events into denormalized read models for admin dashboard queries.
/// </summary>
public interface IAdminReadModelProjector
{
    /// <summary>
    /// Projects a domain event into the appropriate read model(s).
    /// </summary>
    /// <param name="eventType">The type/category of the domain event (e.g., "payment.completed").</param>
    /// <param name="eventPayload">The JSON-serialized event payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ProjectEventAsync(string eventType, string eventPayload, CancellationToken ct);
}

using CardManagement.Domain.PlatformServices.AdminConsole;

namespace CardManagement.Application.PlatformServices.AdminConsole.Ports;

/// <summary>
/// Repository for persisting and querying admin commands in the maker-checker workflow.
/// </summary>
public interface IAdminCommandRepository
{
    /// <summary>
    /// Persists a new pending command.
    /// </summary>
    Task<PendingCommand> CreateAsync(PendingCommand command, CancellationToken ct);

    /// <summary>
    /// Retrieves a pending command by its unique identifier.
    /// </summary>
    Task<PendingCommand?> GetByIdAsync(Guid commandId, CancellationToken ct);

    /// <summary>
    /// Updates an existing command (status changes, checker assignment, etc.).
    /// </summary>
    Task UpdateAsync(PendingCommand command, CancellationToken ct);

    /// <summary>
    /// Returns all pending commands that have exceeded the given expiry threshold.
    /// </summary>
    Task<IReadOnlyList<PendingCommand>> GetExpiredAsync(TimeSpan expiryThreshold, CancellationToken ct);

    /// <summary>
    /// Returns all commands currently in Pending status, ordered by creation date descending.
    /// Used by checkers to view commands awaiting approval.
    /// </summary>
    Task<IReadOnlyList<PendingCommand>> GetPendingAsync(int limit, int offset, CancellationToken ct);
}

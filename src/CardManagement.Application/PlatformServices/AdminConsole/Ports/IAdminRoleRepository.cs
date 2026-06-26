using CardManagement.Domain.PlatformServices.AdminConsole;

namespace CardManagement.Application.PlatformServices.AdminConsole.Ports;

/// <summary>
/// Repository for persisting and querying admin role assignments.
/// </summary>
public interface IAdminRoleRepository
{
    /// <summary>
    /// Creates a new role assignment.
    /// </summary>
    Task<AdminRole> CreateAsync(AdminRole role, CancellationToken ct);

    /// <summary>
    /// Returns all active roles for a given user.
    /// </summary>
    Task<IReadOnlyList<AdminRole>> GetActiveByUserAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Finds a specific active role assignment for a user and role combination.
    /// </summary>
    Task<AdminRole?> GetByUserAndRoleAsync(string userId, string role, CancellationToken ct);

    /// <summary>
    /// Updates an existing role assignment (e.g., deactivation).
    /// </summary>
    Task UpdateAsync(AdminRole role, CancellationToken ct);
}

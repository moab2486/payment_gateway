namespace CardManagement.Application.PlatformServices.AdminConsole.Ports;

/// <summary>
/// Role-based access control service for the admin console.
/// Enforces permission checks against the role-operation matrix and manages role assignments with audit.
/// </summary>
public interface IRbacService
{
    /// <summary>
    /// Checks whether a user has permission to perform a specific operation based on their assigned roles.
    /// </summary>
    Task<bool> HasPermissionAsync(string userId, string operation, CancellationToken ct);

    /// <summary>
    /// Returns all active roles assigned to a user.
    /// </summary>
    Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct);

    /// <summary>
    /// Assigns a role to a user with audit trail. Only security-admin role can assign roles.
    /// </summary>
    Task AssignRoleAsync(string targetUserId, string role, string assignedBy, CancellationToken ct);

    /// <summary>
    /// Revokes a role from a user with audit trail. Only security-admin role can revoke roles.
    /// </summary>
    Task RevokeRoleAsync(string targetUserId, string role, string revokedBy, CancellationToken ct);
}

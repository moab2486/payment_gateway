using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.AdminConsole;

namespace CardManagement.Application.PlatformServices.AdminConsole.Services;

/// <summary>
/// Implements role-based access control for the admin console.
/// Checks permissions against the role-operation matrix and manages role assignments with audit trail.
/// </summary>
public class RbacService : IRbacService
{
    private readonly IAdminRoleRepository _roleRepository;
    private readonly IAuditStore _auditStore;

    public RbacService(IAdminRoleRepository roleRepository, IAuditStore auditStore)
    {
        _roleRepository = roleRepository ?? throw new ArgumentNullException(nameof(roleRepository));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(string userId, string operation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        if (string.IsNullOrWhiteSpace(operation))
            return false;

        var roles = await _roleRepository.GetActiveByUserAsync(userId, ct);

        foreach (var role in roles)
        {
            if (RolePermissionMatrix.HasPermission(role.Role, operation))
                return true;
        }

        // Permission denied — record audit entry per Requirement 12.3
        await _auditStore.AppendAsync(
            AuditEntry.Create(
                transactionReference: $"rbac-denial:{userId}",
                actorIdentity: userId,
                action: "permission.denied",
                previousState: null,
                newState: $"{{\"operation\":\"{operation}\",\"roles\":[{string.Join(",", roles.Select(r => $"\"{r.Role}\""))}]}}",
                correlationId: Guid.NewGuid().ToString(),
                previousEntryHash: null),
            ct);

        return false;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        var roles = await _roleRepository.GetActiveByUserAsync(userId, ct);
        return roles.Select(r => r.Role).ToList();
    }

    /// <inheritdoc />
    public async Task AssignRoleAsync(string targetUserId, string role, string assignedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
            throw new ArgumentException("Target user ID is required.", nameof(targetUserId));

        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role is required.", nameof(role));

        if (string.IsNullOrWhiteSpace(assignedBy))
            throw new ArgumentException("AssignedBy identity is required.", nameof(assignedBy));

        if (!AdminRoleType.IsValid(role))
            throw new ArgumentException(
                $"Invalid role '{role}'. Valid roles are: {string.Join(", ", AdminRoleType.All)}.",
                nameof(role));

        // Check if the user already has this role active
        var existingRole = await _roleRepository.GetByUserAndRoleAsync(targetUserId, role, ct);
        if (existingRole is not null && existingRole.IsActive)
            throw new InvalidOperationException(
                $"User '{targetUserId}' already has an active '{role}' role assignment.");

        // Get current roles for audit record
        var previousRoles = await _roleRepository.GetActiveByUserAsync(targetUserId, ct);
        var previousRoleSet = previousRoles.Select(r => r.Role).ToList();

        // Create new role assignment
        var adminRole = AdminRole.Create(targetUserId, role, assignedBy);
        await _roleRepository.CreateAsync(adminRole, ct);

        var newRoleSet = previousRoleSet.Append(role).ToList();

        // Record audit trail with previous and new role sets
        await _auditStore.AppendAsync(
            AuditEntry.Create(
                transactionReference: $"role:{targetUserId}",
                actorIdentity: assignedBy,
                action: "role.assigned",
                previousState: $"{{\"roles\":[{string.Join(",", previousRoleSet.Select(r => $"\"{r}\""))}]}}",
                newState: $"{{\"roles\":[{string.Join(",", newRoleSet.Select(r => $"\"{r}\""))}]}}",
                correlationId: adminRole.Id.ToString(),
                previousEntryHash: null),
            ct);
    }

    /// <inheritdoc />
    public async Task RevokeRoleAsync(string targetUserId, string role, string revokedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
            throw new ArgumentException("Target user ID is required.", nameof(targetUserId));

        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role is required.", nameof(role));

        if (string.IsNullOrWhiteSpace(revokedBy))
            throw new ArgumentException("RevokedBy identity is required.", nameof(revokedBy));

        if (!AdminRoleType.IsValid(role))
            throw new ArgumentException(
                $"Invalid role '{role}'. Valid roles are: {string.Join(", ", AdminRoleType.All)}.",
                nameof(role));

        // Find the active role assignment
        var existingRole = await _roleRepository.GetByUserAndRoleAsync(targetUserId, role, ct);
        if (existingRole is null || !existingRole.IsActive)
            throw new InvalidOperationException(
                $"User '{targetUserId}' does not have an active '{role}' role assignment to revoke.");

        // Get current roles for audit record
        var previousRoles = await _roleRepository.GetActiveByUserAsync(targetUserId, ct);
        var previousRoleSet = previousRoles.Select(r => r.Role).ToList();

        // Deactivate the role
        existingRole.Deactivate();
        await _roleRepository.UpdateAsync(existingRole, ct);

        var newRoleSet = previousRoleSet.Where(r => !string.Equals(r, role, StringComparison.OrdinalIgnoreCase)).ToList();

        // Record audit trail with previous and new role sets
        await _auditStore.AppendAsync(
            AuditEntry.Create(
                transactionReference: $"role:{targetUserId}",
                actorIdentity: revokedBy,
                action: "role.revoked",
                previousState: $"{{\"roles\":[{string.Join(",", previousRoleSet.Select(r => $"\"{r}\""))}]}}",
                newState: $"{{\"roles\":[{string.Join(",", newRoleSet.Select(r => $"\"{r}\""))}]}}",
                correlationId: existingRole.Id.ToString(),
                previousEntryHash: null),
            ct);
    }
}

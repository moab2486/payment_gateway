namespace CardManagement.Domain.PlatformServices.AdminConsole;

/// <summary>
/// Represents an admin role assignment for a user. Roles control access to
/// Admin Console operations via RBAC (operations, compliance, risk, support, security-admin).
/// </summary>
public class AdminRole
{
    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public string AssignedBy { get; private set; } = string.Empty;
    public DateTime AssignedAtUtc { get; private set; }
    public bool IsActive { get; private set; }

    private AdminRole() { }

    /// <summary>
    /// Creates a new active role assignment for a user.
    /// </summary>
    public static AdminRole Create(string userId, string role, string assignedBy)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role is required.", nameof(role));

        if (!AdminRoleType.IsValid(role))
            throw new ArgumentException(
                $"Invalid role '{role}'. Valid roles are: {string.Join(", ", AdminRoleType.All)}.",
                nameof(role));

        if (string.IsNullOrWhiteSpace(assignedBy))
            throw new ArgumentException("AssignedBy identity is required.", nameof(assignedBy));

        return new AdminRole
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Role = role,
            AssignedBy = assignedBy,
            AssignedAtUtc = DateTime.UtcNow,
            IsActive = true
        };
    }

    /// <summary>
    /// Deactivates this role assignment.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
            throw new InvalidOperationException("Role assignment is already inactive.");

        IsActive = false;
    }

    /// <summary>
    /// Reactivates a previously deactivated role assignment.
    /// </summary>
    public void Reactivate(string reactivatedBy)
    {
        if (IsActive)
            throw new InvalidOperationException("Role assignment is already active.");

        if (string.IsNullOrWhiteSpace(reactivatedBy))
            throw new ArgumentException("ReactivatedBy identity is required.", nameof(reactivatedBy));

        IsActive = true;
        AssignedBy = reactivatedBy;
        AssignedAtUtc = DateTime.UtcNow;
    }
}

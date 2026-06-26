using CardManagement.Domain.PlatformServices.AdminConsole;

namespace CardManagement.Application.PlatformServices.AdminConsole;

/// <summary>
/// Defines the role-operation permission matrix for the admin console.
/// Maps which roles can perform which operations.
/// </summary>
public static class RolePermissionMatrix
{
    /// <summary>
    /// Operation categories that can be controlled via RBAC.
    /// </summary>
    public static class Operations
    {
        // Reconciliation operations
        public const string ReconciliationImport = "reconciliation.import";
        public const string ReconciliationView = "reconciliation.view";
        public const string ReconciliationAdjust = "reconciliation.adjust";

        // Refund operations
        public const string RefundExecute = "refund.execute";
        public const string RefundView = "refund.view";

        // Configuration operations
        public const string ConfigView = "config.view";
        public const string ConfigUpdate = "config.update";

        // User/role management operations
        public const string RoleAssign = "role.assign";
        public const string RoleRevoke = "role.revoke";
        public const string RoleView = "role.view";

        // Transaction operations
        public const string TransactionView = "transaction.view";
        public const string TransactionReverse = "transaction.reverse";

        // Dispute operations
        public const string DisputeView = "dispute.view";
        public const string DisputeResolve = "dispute.resolve";

        // Audit operations
        public const string AuditView = "audit.view";

        // Merchant operations
        public const string MerchantView = "merchant.view";
        public const string MerchantSuspend = "merchant.suspend";
        public const string MerchantDeactivate = "merchant.deactivate";

        // Command approval operations
        public const string CommandApprove = "command.approve";
        public const string CommandReject = "command.reject";
        public const string CommandView = "command.view";

        // Dashboard operations
        public const string DashboardView = "dashboard.view";
    }

    /// <summary>
    /// The permission matrix mapping roles to their allowed operations.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> PermissionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [AdminRoleType.Operations] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Operations.ReconciliationImport,
            Operations.ReconciliationView,
            Operations.ReconciliationAdjust,
            Operations.RefundExecute,
            Operations.RefundView,
            Operations.TransactionView,
            Operations.TransactionReverse,
            Operations.DisputeView,
            Operations.DisputeResolve,
            Operations.MerchantView,
            Operations.CommandApprove,
            Operations.CommandReject,
            Operations.CommandView,
            Operations.DashboardView,
        },

        [AdminRoleType.Compliance] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Operations.ReconciliationView,
            Operations.RefundView,
            Operations.TransactionView,
            Operations.DisputeView,
            Operations.DisputeResolve,
            Operations.AuditView,
            Operations.MerchantView,
            Operations.CommandApprove,
            Operations.CommandReject,
            Operations.CommandView,
            Operations.DashboardView,
        },

        [AdminRoleType.Risk] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Operations.ReconciliationView,
            Operations.RefundView,
            Operations.TransactionView,
            Operations.TransactionReverse,
            Operations.DisputeView,
            Operations.DisputeResolve,
            Operations.MerchantView,
            Operations.MerchantSuspend,
            Operations.CommandApprove,
            Operations.CommandReject,
            Operations.CommandView,
            Operations.DashboardView,
        },

        [AdminRoleType.Support] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Operations.TransactionView,
            Operations.RefundView,
            Operations.DisputeView,
            Operations.MerchantView,
            Operations.CommandView,
            Operations.DashboardView,
        },

        [AdminRoleType.SecurityAdmin] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Operations.RoleAssign,
            Operations.RoleRevoke,
            Operations.RoleView,
            Operations.AuditView,
            Operations.ConfigView,
            Operations.ConfigUpdate,
            Operations.CommandApprove,
            Operations.CommandReject,
            Operations.CommandView,
            Operations.DashboardView,
            Operations.MerchantSuspend,
            Operations.MerchantDeactivate,
        },
    };

    /// <summary>
    /// Returns true if the given role has permission for the specified operation.
    /// </summary>
    public static bool HasPermission(string role, string operation)
    {
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(operation))
            return false;

        return PermissionMap.TryGetValue(role, out var permissions) && permissions.Contains(operation);
    }

    /// <summary>
    /// Returns all operations permitted for a given role.
    /// </summary>
    public static IReadOnlySet<string> GetPermissions(string role)
    {
        if (PermissionMap.TryGetValue(role, out var permissions))
            return permissions;

        return new HashSet<string>();
    }

    /// <summary>
    /// Returns all roles that have permission for a given operation.
    /// </summary>
    public static IReadOnlyList<string> GetRolesForOperation(string operation)
    {
        var roles = new List<string>();

        foreach (var (role, permissions) in PermissionMap)
        {
            if (permissions.Contains(operation))
                roles.Add(role);
        }

        return roles;
    }
}

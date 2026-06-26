namespace CardManagement.Domain.PlatformServices.AdminConsole;

/// <summary>
/// Enumerates the available admin roles for the platform console.
/// </summary>
public static class AdminRoleType
{
    public const string Operations = "operations";
    public const string Compliance = "compliance";
    public const string Risk = "risk";
    public const string Support = "support";
    public const string SecurityAdmin = "security-admin";

    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        Operations,
        Compliance,
        Risk,
        Support,
        SecurityAdmin
    };

    /// <summary>
    /// Returns true if the given role string is a valid admin role.
    /// </summary>
    public static bool IsValid(string role) => ValidRoles.Contains(role);

    /// <summary>
    /// Returns all valid role names.
    /// </summary>
    public static IReadOnlyCollection<string> All => ValidRoles;
}

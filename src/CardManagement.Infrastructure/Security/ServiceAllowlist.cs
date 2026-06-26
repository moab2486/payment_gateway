using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Security;

/// <summary>
/// Defines which services can access raw cardholder data.
/// Loaded from configuration and rejects unauthorized access attempts.
/// </summary>
public class ServiceAllowlist
{
    private readonly HashSet<string> _allowedServices;

    public ServiceAllowlist(IOptions<PciSecurityOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options?.Value);
        _allowedServices = new HashSet<string>(
            options.Value.AllowedServices ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether the specified service is authorized to access raw cardholder data.
    /// </summary>
    public bool IsAuthorized(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return false;

        return _allowedServices.Contains(serviceName);
    }

    /// <summary>
    /// Gets the set of all allowed service names (for diagnostics only).
    /// </summary>
    public IReadOnlyCollection<string> GetAllowedServices() => _allowedServices;
}

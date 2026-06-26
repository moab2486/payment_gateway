namespace CardManagement.Infrastructure.PlatformServices.DeveloperPortal;

/// <summary>
/// Configuration options for the API key validation middleware.
/// </summary>
public class ApiKeyValidationOptions
{
    public const string SectionName = "DeveloperPortal:ApiKeyValidation";

    /// <summary>
    /// Whether API key validation is enabled. Set to false in test environments.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Time-to-live for cached API key entries after a DB lookup populates the cache.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Request path prefixes that skip API key validation entirely.
    /// Defaults include health checks and swagger documentation endpoints.
    /// </summary>
    public string[] ExcludedPaths { get; set; } =
    [
        "/health",
        "/swagger"
    ];
}

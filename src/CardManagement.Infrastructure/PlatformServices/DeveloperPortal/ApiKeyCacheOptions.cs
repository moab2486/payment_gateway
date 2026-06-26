namespace CardManagement.Infrastructure.PlatformServices.DeveloperPortal;

/// <summary>
/// Configuration options for the Redis API key cache.
/// </summary>
public class ApiKeyCacheOptions
{
    public const string SectionName = "DeveloperPortal:ApiKeyCache";

    /// <summary>
    /// Default time-to-live for cached API key entries. Default: 5 minutes.
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Redis connection string. Default: localhost:6379.
    /// </summary>
    public string RedisConnectionString { get; set; } = "localhost:6379";
}

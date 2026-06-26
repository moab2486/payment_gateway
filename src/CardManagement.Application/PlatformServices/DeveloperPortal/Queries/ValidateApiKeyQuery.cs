namespace CardManagement.Application.PlatformServices.DeveloperPortal.Queries;

/// <summary>
/// Query to validate an API key and retrieve the associated developer context.
/// Uses Redis cache-first strategy with database fallback.
/// </summary>
public record ValidateApiKeyQuery(string ApiKey);

namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Result of API key creation. Contains the raw key value which must be presented
/// to the developer exactly once — it is never stored or retrievable again.
/// </summary>
public record ApiKeyCreateResult(
    Guid KeyId,
    string RawKey,
    string KeyPrefix,
    string[] Scopes);

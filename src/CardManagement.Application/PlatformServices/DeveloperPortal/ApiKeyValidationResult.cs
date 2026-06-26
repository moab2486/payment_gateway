namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Result of API key validation. Indicates whether the key is valid and provides
/// the associated developer context for authorized requests.
/// </summary>
public record ApiKeyValidationResult(
    bool IsValid,
    Guid? DeveloperId,
    string[]? Scopes,
    bool IsSandbox)
{
    public static ApiKeyValidationResult Invalid() =>
        new(false, null, null, false);

    public static ApiKeyValidationResult Valid(Guid developerId, string[] scopes, bool isSandbox) =>
        new(true, developerId, scopes, isSandbox);
}

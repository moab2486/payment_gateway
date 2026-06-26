namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Service for managing API key lifecycle operations.
/// Handles creation, rotation, revocation, and validation of developer API keys.
/// </summary>
public interface IApiKeyService
{
    Task<ApiKeyCreateResult> CreateKeyAsync(Guid developerId, string[] scopes, CancellationToken ct);
    Task<ApiKeyRotateResult> RotateKeyAsync(Guid keyId, TimeSpan gracePeriod, CancellationToken ct);
    Task RevokeKeyAsync(Guid keyId, CancellationToken ct);
    Task<ApiKeyValidationResult> ValidateKeyAsync(string apiKey, CancellationToken ct);
}

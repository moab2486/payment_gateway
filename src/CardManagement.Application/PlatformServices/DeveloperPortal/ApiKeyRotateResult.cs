namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Result of API key rotation. Contains the new raw key value (presented once)
/// and the grace period end for the old key.
/// </summary>
public record ApiKeyRotateResult(
    Guid NewKeyId,
    string NewRawKey,
    string NewKeyPrefix,
    DateTime GracePeriodEndsAtUtc);

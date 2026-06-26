namespace CardManagement.Domain.PlatformServices.DeveloperPortal;

/// <summary>
/// Represents the lifecycle state of an API key.
/// </summary>
public enum KeyStatus
{
    Active,
    Rotated,
    Expired,
    Revoked
}

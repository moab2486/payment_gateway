namespace CardManagement.Application.PlatformServices.DeveloperPortal.Commands;

/// <summary>
/// Command to rotate an existing API key. The old key remains valid during the grace period.
/// </summary>
public record RotateApiKeyCommand(Guid KeyId, TimeSpan GracePeriod);

namespace CardManagement.Application.PlatformServices.DeveloperPortal.Commands;

/// <summary>
/// Command to immediately revoke an API key, invalidating it and evicting from cache.
/// </summary>
public record RevokeApiKeyCommand(Guid KeyId);

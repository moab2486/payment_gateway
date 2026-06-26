namespace CardManagement.Application.PlatformServices.DeveloperPortal.Commands;

/// <summary>
/// Command to create a new API key for a developer.
/// </summary>
public record CreateApiKeyCommand(Guid DeveloperId, string[] Scopes);

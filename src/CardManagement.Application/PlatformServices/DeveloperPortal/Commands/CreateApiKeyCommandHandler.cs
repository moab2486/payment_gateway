namespace CardManagement.Application.PlatformServices.DeveloperPortal.Commands;

/// <summary>
/// Handles the CreateApiKeyCommand by delegating to the IApiKeyService.
/// Generates a cryptographically random key, stores only the hash, and returns the raw value once.
/// </summary>
public class CreateApiKeyCommandHandler
{
    private readonly IApiKeyService _apiKeyService;

    public CreateApiKeyCommandHandler(IApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService ?? throw new ArgumentNullException(nameof(apiKeyService));
    }

    public async Task<ApiKeyCreateResult> HandleAsync(CreateApiKeyCommand command, CancellationToken ct)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        return await _apiKeyService.CreateKeyAsync(command.DeveloperId, command.Scopes, ct);
    }
}

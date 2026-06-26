namespace CardManagement.Application.PlatformServices.DeveloperPortal.Commands;

/// <summary>
/// Handles the RevokeApiKeyCommand by delegating to the IApiKeyService.
/// Immediately invalidates the key and evicts it from the Redis cache.
/// </summary>
public class RevokeApiKeyCommandHandler
{
    private readonly IApiKeyService _apiKeyService;

    public RevokeApiKeyCommandHandler(IApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService ?? throw new ArgumentNullException(nameof(apiKeyService));
    }

    public async Task HandleAsync(RevokeApiKeyCommand command, CancellationToken ct)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        await _apiKeyService.RevokeKeyAsync(command.KeyId, ct);
    }
}

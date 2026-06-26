namespace CardManagement.Application.PlatformServices.DeveloperPortal.Commands;

/// <summary>
/// Handles the RotateApiKeyCommand by delegating to the IApiKeyService.
/// Creates a new key and marks the old key as rotated with the specified grace period.
/// </summary>
public class RotateApiKeyCommandHandler
{
    private readonly IApiKeyService _apiKeyService;

    public RotateApiKeyCommandHandler(IApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService ?? throw new ArgumentNullException(nameof(apiKeyService));
    }

    public async Task<ApiKeyRotateResult> HandleAsync(RotateApiKeyCommand command, CancellationToken ct)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        return await _apiKeyService.RotateKeyAsync(command.KeyId, command.GracePeriod, ct);
    }
}

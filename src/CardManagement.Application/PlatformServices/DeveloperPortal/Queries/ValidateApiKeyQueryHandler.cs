namespace CardManagement.Application.PlatformServices.DeveloperPortal.Queries;

/// <summary>
/// Handles the ValidateApiKeyQuery by delegating to the IApiKeyService.
/// Implements Redis cache-first strategy: hash the raw key → check Redis cache → if miss, check DB → populate cache → return result.
/// </summary>
public class ValidateApiKeyQueryHandler
{
    private readonly IApiKeyService _apiKeyService;

    public ValidateApiKeyQueryHandler(IApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService ?? throw new ArgumentNullException(nameof(apiKeyService));
    }

    public async Task<ApiKeyValidationResult> HandleAsync(ValidateApiKeyQuery query, CancellationToken ct)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        return await _apiKeyService.ValidateKeyAsync(query.ApiKey, ct);
    }
}

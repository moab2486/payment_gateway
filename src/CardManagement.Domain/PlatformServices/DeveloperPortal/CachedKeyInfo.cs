namespace CardManagement.Domain.PlatformServices.DeveloperPortal;

/// <summary>
/// Value object representing API key information cached in Redis for fast validation.
/// Contains the minimum data needed to authorize a request without querying the database.
/// Serialized as JSON in Redis with key format: apikey:{sha256_hash}.
/// </summary>
public record CachedKeyInfo(
    Guid KeyId,
    Guid DeveloperId,
    string[] Scopes,
    KeyStatus Status,
    bool IsSandbox,
    DateTime? ExpiresAtUtc);

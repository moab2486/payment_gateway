using System.Security.Cryptography;
using System.Text;

namespace CardManagement.Domain.PlatformServices.DeveloperPortal;

/// <summary>
/// Represents an API key issued to a developer for authenticating requests against the payment gateway.
/// Tracks the full lifecycle: Active → Rotated, Active → Expired, Active → Revoked.
/// The raw key is never stored — only the SHA-256 hash is persisted.
/// </summary>
public class ApiKey
{
    public Guid Id { get; private set; }
    public Guid DeveloperId { get; private set; }
    public string KeyHash { get; private set; } = string.Empty;
    public string KeyPrefix { get; private set; } = string.Empty;
    public string[] Scopes { get; private set; } = Array.Empty<string>();
    public KeyStatus Status { get; private set; }
    public bool IsSandbox { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? GracePeriodEndsAtUtc { get; private set; }

    private ApiKey() { }

    /// <summary>
    /// Creates a new API key for a developer. Computes the SHA-256 hash of the raw key
    /// and stores only the hash. The raw key is returned via the out parameter and must
    /// be presented to the developer exactly once.
    /// </summary>
    public static ApiKey Create(
        Guid developerId,
        string[] scopes,
        bool isSandbox,
        DateTime? expiresAtUtc,
        out string rawKey)
    {
        if (developerId == Guid.Empty)
            throw new ArgumentException("Developer ID is required.", nameof(developerId));

        if (scopes is null || scopes.Length == 0)
            throw new ArgumentException("At least one scope is required.", nameof(scopes));

        rawKey = GenerateRawKey();
        var keyHash = ComputeHash(rawKey);
        var keyPrefix = rawKey[..8];

        return new ApiKey
        {
            Id = Guid.NewGuid(),
            DeveloperId = developerId,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            Scopes = scopes,
            Status = KeyStatus.Active,
            IsSandbox = isSandbox,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    /// <summary>
    /// Creates an API key from a known raw key value (used in testing or controlled scenarios).
    /// </summary>
    public static ApiKey CreateFromRawKey(
        Guid developerId,
        string rawKey,
        string[] scopes,
        bool isSandbox,
        DateTime? expiresAtUtc)
    {
        if (developerId == Guid.Empty)
            throw new ArgumentException("Developer ID is required.", nameof(developerId));

        if (string.IsNullOrWhiteSpace(rawKey))
            throw new ArgumentException("Raw key is required.", nameof(rawKey));

        if (rawKey.Length < 8)
            throw new ArgumentException("Raw key must be at least 8 characters.", nameof(rawKey));

        if (scopes is null || scopes.Length == 0)
            throw new ArgumentException("At least one scope is required.", nameof(scopes));

        var keyHash = ComputeHash(rawKey);
        var keyPrefix = rawKey[..8];

        return new ApiKey
        {
            Id = Guid.NewGuid(),
            DeveloperId = developerId,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            Scopes = scopes,
            Status = KeyStatus.Active,
            IsSandbox = isSandbox,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    /// <summary>
    /// Marks this key as rotated with a grace period during which it remains valid.
    /// </summary>
    public void Rotate(TimeSpan gracePeriod)
    {
        if (Status != KeyStatus.Active)
            throw new InvalidOperationException(
                $"Cannot rotate key in status '{Status}'. Key must be Active.");

        if (gracePeriod <= TimeSpan.Zero)
            throw new ArgumentException("Grace period must be positive.", nameof(gracePeriod));

        Status = KeyStatus.Rotated;
        GracePeriodEndsAtUtc = DateTime.UtcNow.Add(gracePeriod);
    }

    /// <summary>
    /// Immediately revokes this key, making it permanently invalid.
    /// </summary>
    public void Revoke()
    {
        if (Status != KeyStatus.Active)
            throw new InvalidOperationException(
                $"Cannot revoke key in status '{Status}'. Key must be Active.");

        Status = KeyStatus.Revoked;
    }

    /// <summary>
    /// Marks this key as expired. Called when the key reaches its configured expiry date.
    /// </summary>
    public void Expire()
    {
        if (Status != KeyStatus.Active)
            throw new InvalidOperationException(
                $"Cannot expire key in status '{Status}'. Key must be Active.");

        Status = KeyStatus.Expired;
    }

    /// <summary>
    /// Determines whether this key is currently valid for authentication.
    /// A key is valid if it is Active and not past its expiry date, or if it is 
    /// in Rotated status and still within its grace period.
    /// </summary>
    public bool IsValid()
    {
        return IsValid(DateTime.UtcNow);
    }

    /// <summary>
    /// Determines whether this key is valid at the specified point in time.
    /// </summary>
    public bool IsValid(DateTime atUtc)
    {
        if (Status == KeyStatus.Active)
        {
            if (ExpiresAtUtc.HasValue && atUtc >= ExpiresAtUtc.Value)
                return false;

            return true;
        }

        if (Status == KeyStatus.Rotated)
        {
            // During grace period, the old rotated key is still valid
            if (GracePeriodEndsAtUtc.HasValue && atUtc < GracePeriodEndsAtUtc.Value)
                return true;

            return false;
        }

        // Expired and Revoked keys are never valid
        return false;
    }

    /// <summary>
    /// Computes the SHA-256 hash of a raw API key.
    /// </summary>
    public static string ComputeHash(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            throw new ArgumentException("Raw key cannot be empty.", nameof(rawKey));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateRawKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "");
    }
}

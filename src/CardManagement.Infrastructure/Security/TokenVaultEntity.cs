namespace CardManagement.Infrastructure.Security;

/// <summary>
/// EF Core persistence entity for token vault entries.
/// Stored in a dedicated PostgreSQL schema (pci) with restricted access.
/// </summary>
public class TokenVaultEntity
{
    /// <summary>
    /// The UUID-based token (primary key).
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of the original PAN (for deterministic token lookup).
    /// </summary>
    public string PanHash { get; set; } = string.Empty;

    /// <summary>
    /// AES-256 encrypted PAN data.
    /// </summary>
    public byte[] EncryptedPan { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// AES-256 encrypted CVV data (nullable).
    /// </summary>
    public byte[]? EncryptedCvv { get; set; }

    /// <summary>
    /// Timestamp when the token was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Timestamp when the token was last accessed.
    /// </summary>
    public DateTime? LastAccessedAtUtc { get; set; }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace CardManagement.Infrastructure.Security;

/// <summary>
/// Stores the mapping between tokens and encrypted cardholder data.
/// Uses UUID-based tokens that are deterministic for the same PAN.
/// In-memory implementation backed by ConcurrentDictionary; production use should persist via EF Core.
/// </summary>
public class TokenVault
{
    private readonly ConcurrentDictionary<string, TokenEntry> _tokenToData = new();
    private readonly ConcurrentDictionary<string, string> _panHashToToken = new();

    /// <summary>
    /// Stores encrypted cardholder data and returns a deterministic token for the given PAN.
    /// If the PAN has been tokenized before, returns the same token.
    /// </summary>
    public string StoreToken(string pan, byte[] encryptedPan, byte[]? encryptedCvv = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pan);
        ArgumentNullException.ThrowIfNull(encryptedPan);

        var panHash = ComputePanHash(pan);

        // Return existing token if PAN was already tokenized (deterministic tokens)
        if (_panHashToToken.TryGetValue(panHash, out var existingToken))
        {
            // Update encrypted data in case key rotated
            _tokenToData[existingToken] = new TokenEntry(encryptedPan, encryptedCvv);
            return existingToken;
        }

        // Generate a new UUID-based token deterministically from the PAN hash
        var token = GenerateDeterministicToken(panHash);

        _tokenToData[token] = new TokenEntry(encryptedPan, encryptedCvv);
        _panHashToToken[panHash] = token;

        return token;
    }

    /// <summary>
    /// Retrieves encrypted cardholder data by token.
    /// Returns null if the token is not found.
    /// </summary>
    public TokenEntry? GetByToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        _tokenToData.TryGetValue(token, out var entry);
        return entry;
    }

    /// <summary>
    /// Checks whether a token exists in the vault.
    /// </summary>
    public bool TokenExists(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        return _tokenToData.ContainsKey(token);
    }

    /// <summary>
    /// Generates a deterministic UUID-based token from a PAN hash.
    /// Uses a version-5-style UUID derived from SHA-256.
    /// </summary>
    private static string GenerateDeterministicToken(string panHash)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"token-ns:{panHash}"));
        // Take first 16 bytes and format as UUID
        var guidBytes = new byte[16];
        Array.Copy(hashBytes, guidBytes, 16);
        // Set version 4 and variant bits for UUID format compliance
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x40); // version 4
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // variant
        return new Guid(guidBytes).ToString();
    }

    /// <summary>
    /// Computes a SHA-256 hash of the PAN for lookup purposes.
    /// </summary>
    private static string ComputePanHash(string pan)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pan));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>
/// Represents encrypted cardholder data stored in the token vault.
/// </summary>
public record TokenEntry(byte[] EncryptedPan, byte[]? EncryptedCvv);

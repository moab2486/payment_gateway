using System.Security.Cryptography;
using System.Text;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Security;

/// <summary>
/// Main PCI data security boundary class.
/// Provides AES-256 encryption/decryption, tokenization of PANs on entry,
/// and de-tokenization restricted to allowlisted services.
/// All access attempts are logged to the AuditStore.
/// </summary>
public class PciBoundary
{
    private readonly TokenVault _tokenVault;
    private readonly ServiceAllowlist _allowlist;
    private readonly IAuditStore _auditStore;
    private readonly byte[] _encryptionKey;

    public PciBoundary(
        TokenVault tokenVault,
        ServiceAllowlist allowlist,
        IAuditStore auditStore,
        IOptions<PciSecurityOptions> options)
    {
        ArgumentNullException.ThrowIfNull(tokenVault);
        ArgumentNullException.ThrowIfNull(allowlist);
        ArgumentNullException.ThrowIfNull(auditStore);
        ArgumentNullException.ThrowIfNull(options?.Value);

        _tokenVault = tokenVault;
        _allowlist = allowlist;
        _auditStore = auditStore;

        var keyString = options.Value.EncryptionKey;
        if (string.IsNullOrWhiteSpace(keyString))
            throw new InvalidOperationException("PCI encryption key is not configured.");

        _encryptionKey = Convert.FromBase64String(keyString);
        if (_encryptionKey.Length != 32) // 256 bits
            throw new InvalidOperationException("PCI encryption key must be exactly 256 bits (32 bytes).");
    }

    /// <summary>
    /// Tokenizes a PAN immediately on entry. Encrypts the PAN data at rest and stores
    /// the mapping in the TokenVault. Returns a UUID-based token.
    /// </summary>
    public async Task<string> TokenizeAsync(string pan, string? cvv, string correlationId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pan);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var encryptedPan = Encrypt(pan);
        byte[]? encryptedCvv = cvv != null ? Encrypt(cvv) : null;

        var token = _tokenVault.StoreToken(pan, encryptedPan, encryptedCvv);

        await LogAccessAsync("PciBoundary", "Tokenize", true, correlationId, ct);

        return token;
    }

    /// <summary>
    /// De-tokenizes a token back to the raw PAN. Only allowed for services in the allowlist.
    /// Logs all access attempts (success and denied).
    /// </summary>
    public async Task<DetokenizeResult> DetokenizeAsync(string token, string requestingService, string correlationId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestingService);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (!_allowlist.IsAuthorized(requestingService))
        {
            await LogAccessAsync(requestingService, "Detokenize-Denied", false, correlationId, ct);
            return DetokenizeResult.Unauthorized(requestingService);
        }

        var entry = _tokenVault.GetByToken(token);
        if (entry == null)
        {
            await LogAccessAsync(requestingService, "Detokenize-NotFound", false, correlationId, ct);
            return DetokenizeResult.NotFound();
        }

        var pan = Decrypt(entry.EncryptedPan);
        string? cvv = entry.EncryptedCvv != null ? Decrypt(entry.EncryptedCvv) : null;

        await LogAccessAsync(requestingService, "Detokenize-Success", true, correlationId, ct);

        return DetokenizeResult.Success(pan, cvv);
    }

    /// <summary>
    /// Returns a masked representation of the PAN showing only the last 4 digits.
    /// Does not require allowlist membership — safe for external-facing responses.
    /// </summary>
    public async Task<string?> GetMaskedPanAsync(string token, string requestingService, string correlationId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var entry = _tokenVault.GetByToken(token);
        if (entry == null)
        {
            await LogAccessAsync(requestingService ?? "unknown", "GetMaskedPan-NotFound", false, correlationId, ct);
            return null;
        }

        var pan = Decrypt(entry.EncryptedPan);
        var masked = MaskPan(pan);

        await LogAccessAsync(requestingService ?? "unknown", "GetMaskedPan", true, correlationId, ct);

        return masked;
    }

    /// <summary>
    /// Encrypts plaintext data using AES-256-CBC with a random IV prepended to the ciphertext.
    /// </summary>
    public byte[] Encrypt(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = _encryptionKey;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Prepend IV to ciphertext for storage
        var result = new byte[aes.IV.Length + ciphertext.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(ciphertext, 0, result, aes.IV.Length, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Decrypts data that was encrypted with Encrypt (IV prepended to ciphertext).
    /// </summary>
    public string Decrypt(byte[] encryptedData)
    {
        ArgumentNullException.ThrowIfNull(encryptedData);
        if (encryptedData.Length < 17) // Minimum: 16 byte IV + at least 1 byte ciphertext
            throw new ArgumentException("Encrypted data is too short.", nameof(encryptedData));

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = _encryptionKey;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Extract IV from the first 16 bytes
        var iv = new byte[16];
        Buffer.BlockCopy(encryptedData, 0, iv, 0, 16);
        aes.IV = iv;

        var ciphertext = new byte[encryptedData.Length - 16];
        Buffer.BlockCopy(encryptedData, 16, ciphertext, 0, ciphertext.Length);

        using var decryptor = aes.CreateDecryptor();
        var plaintextBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    /// <summary>
    /// Masks a PAN showing only the last 4 digits, replacing all others with asterisks.
    /// </summary>
    public static string MaskPan(string pan)
    {
        if (string.IsNullOrWhiteSpace(pan))
            return string.Empty;

        if (pan.Length <= 4)
            return pan;

        var masked = new string('*', pan.Length - 4) + pan[^4..];
        return masked;
    }

    /// <summary>
    /// Logs an access attempt to the AuditStore without including cardholder data values.
    /// </summary>
    private async Task LogAccessAsync(string actor, string action, bool success, string correlationId, CancellationToken ct)
    {
        var entry = AuditEntry.Create(
            transactionReference: $"PCI-{correlationId}",
            actorIdentity: actor,
            action: action,
            previousState: null,
            newState: success ? "AccessGranted" : "AccessDenied",
            correlationId: correlationId,
            previousEntryHash: null);

        await _auditStore.AppendAsync(entry, ct);
    }
}

/// <summary>
/// Result of a de-tokenization attempt.
/// </summary>
public class DetokenizeResult
{
    public bool IsSuccess { get; private init; }
    public bool IsUnauthorized { get; private init; }
    public bool IsNotFound { get; private init; }
    public string? Pan { get; private init; }
    public string? Cvv { get; private init; }
    public string? DeniedService { get; private init; }

    private DetokenizeResult() { }

    public static DetokenizeResult Success(string pan, string? cvv) => new()
    {
        IsSuccess = true,
        Pan = pan,
        Cvv = cvv
    };

    public static DetokenizeResult Unauthorized(string serviceName) => new()
    {
        IsUnauthorized = true,
        DeniedService = serviceName
    };

    public static DetokenizeResult NotFound() => new()
    {
        IsNotFound = true
    };
}

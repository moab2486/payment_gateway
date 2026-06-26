using CardManagement.Application.DTOs;
using CardManagement.Domain.ValueObjects;

namespace CardManagement.Application.Ports;

/// <summary>
/// Port interface for Hardware Security Module (HSM) operations.
/// All cryptographic operations (PAN generation, CVV2 computation, encryption)
/// are performed exclusively within the HSM boundary via PKCS#11.
/// Key material never leaves the HSM — only opaque key handles are used.
/// </summary>
public interface IHsmService
{
    /// <summary>
    /// Generates a PAN within the specified BIN range using HSM-generated random digits.
    /// The resulting PAN includes a valid Luhn check digit.
    /// </summary>
    /// <param name="binRange">The BIN range defining the prefix and expected PAN length.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A result containing the generated PAN string or an error (e.g., HSM timeout).</returns>
    Task<Result<string>> GeneratePanAsync(BinRange binRange, CancellationToken ct);

    /// <summary>
    /// Computes a 3-digit CVV2 value using the card scheme's cryptographic algorithm via the HSM.
    /// </summary>
    /// <param name="pan">The PAN for which to compute the CVV2.</param>
    /// <param name="expiryDate">The card expiry date in MM/YY format.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A result containing the 3-digit CVV2 string or an error.</returns>
    Task<Result<string>> ComputeCvv2Async(string pan, string expiryDate, CancellationToken ct);

    /// <summary>
    /// Encrypts plaintext data using an HSM-managed key identified by handle.
    /// Used for encrypting PAN and CVV2 at rest.
    /// </summary>
    /// <param name="plaintext">The data to encrypt.</param>
    /// <param name="keyHandle">The opaque handle identifying the HSM key to use.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A result containing the ciphertext bytes or an error.</returns>
    Task<Result<byte[]>> EncryptAsync(byte[] plaintext, string keyHandle, CancellationToken ct);

    /// <summary>
    /// Decrypts ciphertext using an HSM-managed AES-256 key identified by handle.
    /// Used for decrypting PAN and CVV2 when needed for processing.
    /// </summary>
    /// <param name="ciphertext">The encrypted data to decrypt.</param>
    /// <param name="keyHandle">The opaque handle identifying the HSM key to use.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A result containing the plaintext bytes or an error.</returns>
    Task<Result<byte[]>> DecryptAsync(byte[] ciphertext, string keyHandle, CancellationToken ct);
}

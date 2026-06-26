using System.Security.Cryptography;
using System.Text;
using CardManagement.Application.PlatformServices.Webhooks.Ports;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Computes and verifies HMAC-SHA256 signatures for webhook payloads.
/// Signature format: sha256={hex_encoded_hmac}
/// </summary>
public sealed class HmacSigner : IHmacSigner
{
    private const string SignaturePrefix = "sha256=";

    /// <inheritdoc />
    public string ComputeSignature(string payload, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(payload);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();

        return $"{SignaturePrefix}{hex}";
    }

    /// <inheritdoc />
    public bool VerifySignature(string payload, string secret, string signature)
    {
        ArgumentException.ThrowIfNullOrEmpty(payload);
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentException.ThrowIfNullOrEmpty(signature);

        var expectedSignature = ComputeSignature(payload, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(signature));
    }
}

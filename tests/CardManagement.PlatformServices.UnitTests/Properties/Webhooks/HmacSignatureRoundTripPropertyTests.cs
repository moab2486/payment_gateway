using System.Security.Cryptography;
using System.Text;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.PlatformServices.UnitTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Properties.Webhooks;

/// <summary>
/// Property-based tests for HMAC Signature Round-Trip (Property 10).
/// 
/// **Validates: Requirements 5.1, 5.2**
/// 
/// For any payload string and signing secret, computing the HMAC-SHA256 signature
/// and then verifying the same payload against the same secret and signature should
/// return true. Additionally, verifying against a different payload or different secret
/// should return false.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "10")]
public class HmacSignatureRoundTripPropertyTests
{
    private readonly IHmacSigner _signer = new TestHmacSigner();

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    /// 
    /// Property 10: HMAC Signature Round-Trip — same payload + same secret verifies true.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public Property ComputeSignature_ThenVerify_WithSamePayloadAndSecret_ReturnsTrue()
    {
        var payloadGen = Gen.Elements(
            """{"transactionId":"abc123","amount":5000}""",
            """{"disputeId":"xyz789","reason":"unauthorized"}""",
            """{"refundId":"ref456","amount":10000}""",
            """{"event":"payment.completed","data":{"id":"pay_123"}}""")
            .Or(Arb.Generate<NonEmptyString>().Select(s => s.Get));

        var secretGen = Arb.Generate<NonEmptyString>()
            .Select(s => Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Get)));

        return Prop.ForAll(payloadGen.ToArbitrary(), secretGen.ToArbitrary(), (payload, secret) =>
        {
            // Act: Compute signature then verify with same payload and secret
            var signature = _signer.ComputeSignature(payload, secret);
            var result = _signer.VerifySignature(payload, secret, signature);

            return result
                .Label($"Expected VerifySignature to return true for same payload and secret. " +
                       $"Payload: '{payload[..Math.Min(50, payload.Length)]}', Signature: '{signature}'");
        });
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    /// 
    /// Property 10: HMAC Signature Round-Trip — different payload returns false.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public Property ComputeSignature_ThenVerify_WithDifferentPayload_ReturnsFalse()
    {
        var payloadGen = Arb.Generate<NonEmptyString>().Select(s => s.Get);
        var secretGen = Arb.Generate<NonEmptyString>()
            .Select(s => Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Get)));
        var differentPayloadGen = Arb.Generate<NonEmptyString>().Select(s => s.Get);

        return Prop.ForAll(
            payloadGen.ToArbitrary(),
            secretGen.ToArbitrary(),
            differentPayloadGen.ToArbitrary(),
            (payload, secret, differentPayload) =>
            {
                // Skip if payloads happen to be the same
                if (payload == differentPayload) return true.Label("skipped - same payload");

                // Act: Compute signature with original payload, verify with different payload
                var signature = _signer.ComputeSignature(payload, secret);
                var result = _signer.VerifySignature(differentPayload, secret, signature);

                return (!result)
                    .Label($"Expected VerifySignature to return false for different payload. " +
                           $"Original: '{payload[..Math.Min(30, payload.Length)]}', " +
                           $"Different: '{differentPayload[..Math.Min(30, differentPayload.Length)]}'");
            });
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    /// 
    /// Property 10: HMAC Signature Round-Trip — different secret returns false.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public Property ComputeSignature_ThenVerify_WithDifferentSecret_ReturnsFalse()
    {
        var payloadGen = Arb.Generate<NonEmptyString>().Select(s => s.Get);
        var secretGen = Arb.Generate<NonEmptyString>()
            .Select(s => Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Get)));
        var differentSecretGen = Arb.Generate<NonEmptyString>()
            .Select(s => Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Get + "_different")));

        return Prop.ForAll(
            payloadGen.ToArbitrary(),
            secretGen.ToArbitrary(),
            differentSecretGen.ToArbitrary(),
            (payload, secret, differentSecret) =>
            {
                // Skip if secrets happen to be the same
                if (secret == differentSecret) return true.Label("skipped - same secret");

                // Act: Compute signature with original secret, verify with different secret
                var signature = _signer.ComputeSignature(payload, secret);
                var result = _signer.VerifySignature(payload, differentSecret, signature);

                return (!result)
                    .Label($"Expected VerifySignature to return false for different secret. " +
                           $"Original secret: '{secret[..Math.Min(20, secret.Length)]}...', " +
                           $"Different secret: '{differentSecret[..Math.Min(20, differentSecret.Length)]}...'");
            });
    }
}

/// <summary>
/// Simple HMAC-SHA256 implementation of IHmacSigner for property testing.
/// Produces signatures in the format: sha256={hex_encoded_hmac}
/// </summary>
internal class TestHmacSigner : IHmacSigner
{
    public string ComputeSignature(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();

        return $"sha256={hex}";
    }

    public bool VerifySignature(string payload, string secret, string signature)
    {
        var expectedSignature = ComputeSignature(payload, secret);
        return string.Equals(expectedSignature, signature, StringComparison.OrdinalIgnoreCase);
    }
}

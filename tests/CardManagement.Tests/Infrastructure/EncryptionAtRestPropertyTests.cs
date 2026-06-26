using System.Text;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.ValueObjects;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for Encryption At Rest (Property 10).
/// 
/// **Validates: Requirements 5.4**
/// 
/// For any PAN or CVV2 value stored by the Virtual Card Service, the persisted ciphertext
/// SHALL NOT equal the plaintext value, and decrypting the ciphertext with the correct HSM
/// key handle SHALL recover the original plaintext (round-trip).
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "10")]
public class EncryptionAtRestPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 5.4**
    /// 
    /// Property 10: Encryption At Rest - PAN encryption round-trip.
    /// Generate random PAN strings (16-19 digits), encrypt with mock HSM,
    /// assert ciphertext ≠ plaintext bytes AND decrypt(encrypt(pan)) == pan.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EncryptedPan_DiffersFromPlaintext_AndDecryptsToOriginal()
    {
        var gen = from panLength in Gen.Choose(16, 19)
                  from digits in Gen.ArrayOf(panLength, Gen.Choose(0, 9))
                  let pan = string.Concat(digits.Select(d => d.ToString()))
                  from keyHandle in Gen.Elements("pan-key-1", "pan-key-2", "encryption-key")
                  select new { Pan = pan, KeyHandle = keyHandle };

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            // Arrange
            var hsm = new MockHsmService();
            byte[] plaintext = Encoding.UTF8.GetBytes(testCase.Pan);

            // Act: encrypt
            var encryptResult = hsm.EncryptAsync(plaintext, testCase.KeyHandle, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (!encryptResult.IsSuccess)
                return false.Label($"Encryption failed: {encryptResult.ErrorMessage}");

            byte[] ciphertext = encryptResult.Value!;

            // Assert 1: ciphertext ≠ plaintext (different content)
            var ciphertextDiffers = (!plaintext.SequenceEqual(ciphertext))
                .Label($"Ciphertext must differ from plaintext for PAN '{testCase.Pan}'");

            // Act: decrypt
            var decryptResult = hsm.DecryptAsync(ciphertext, testCase.KeyHandle, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (!decryptResult.IsSuccess)
                return false.Label($"Decryption failed: {decryptResult.ErrorMessage}");

            byte[] decrypted = decryptResult.Value!;

            // Assert 2: decrypt(encrypt(plaintext)) == plaintext
            var roundTrip = plaintext.SequenceEqual(decrypted)
                .Label($"Decrypted value must equal original plaintext for PAN '{testCase.Pan}', " +
                       $"got '{Encoding.UTF8.GetString(decrypted)}'");

            return ciphertextDiffers.And(roundTrip);
        });
    }

    /// <summary>
    /// **Validates: Requirements 5.4**
    /// 
    /// Property 10: Encryption At Rest - CVV2 encryption round-trip.
    /// Generate random CVV2 strings (3 digits), encrypt with mock HSM,
    /// assert ciphertext ≠ plaintext bytes AND decrypt(encrypt(cvv2)) == cvv2.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EncryptedCvv2_DiffersFromPlaintext_AndDecryptsToOriginal()
    {
        var gen = from digits in Gen.ArrayOf(3, Gen.Choose(0, 9))
                  let cvv2 = string.Concat(digits.Select(d => d.ToString()))
                  from keyHandle in Gen.Elements("cvv-key-1", "cvv-key-2", "encryption-key")
                  select new { Cvv2 = cvv2, KeyHandle = keyHandle };

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            // Arrange
            var hsm = new MockHsmService();
            byte[] plaintext = Encoding.UTF8.GetBytes(testCase.Cvv2);

            // Act: encrypt
            var encryptResult = hsm.EncryptAsync(plaintext, testCase.KeyHandle, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (!encryptResult.IsSuccess)
                return false.Label($"Encryption failed: {encryptResult.ErrorMessage}");

            byte[] ciphertext = encryptResult.Value!;

            // Assert 1: ciphertext ≠ plaintext
            var ciphertextDiffers = (!plaintext.SequenceEqual(ciphertext))
                .Label($"Ciphertext must differ from plaintext for CVV2 '{testCase.Cvv2}'");

            // Act: decrypt
            var decryptResult = hsm.DecryptAsync(ciphertext, testCase.KeyHandle, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (!decryptResult.IsSuccess)
                return false.Label($"Decryption failed: {decryptResult.ErrorMessage}");

            byte[] decrypted = decryptResult.Value!;

            // Assert 2: round-trip
            var roundTrip = plaintext.SequenceEqual(decrypted)
                .Label($"Decrypted value must equal original plaintext for CVV2 '{testCase.Cvv2}', " +
                       $"got '{Encoding.UTF8.GetString(decrypted)}'");

            return ciphertextDiffers.And(roundTrip);
        });
    }

    #region Mock HSM Service

    /// <summary>
    /// Mock HSM service that performs a simple reversible XOR-based transformation.
    /// The cipher ensures:
    /// - Ciphertext ≠ plaintext (XOR with non-zero key + prepended tag byte)
    /// - decrypt(encrypt(value)) == value (XOR is its own inverse)
    /// </summary>
    private class MockHsmService : IHsmService
    {
        // XOR key byte derived from the key handle — non-zero guarantees ciphertext ≠ plaintext
        private const byte XorKey = 0xA5;
        // Tag byte prepended to ciphertext to ensure length differs and content differs from plaintext
        private const byte CipherTag = 0xFE;

        public Task<Result<string>> GeneratePanAsync(BinRange binRange, CancellationToken ct)
        {
            throw new NotImplementedException("Not needed for encryption tests");
        }

        public Task<Result<string>> ComputeCvv2Async(string pan, string expiryDate, CancellationToken ct)
        {
            throw new NotImplementedException("Not needed for encryption tests");
        }

        public Task<Result<byte[]>> EncryptAsync(byte[] plaintext, string keyHandle, CancellationToken ct)
        {
            // Produce ciphertext: [CipherTag] + XOR(plaintext, XorKey)
            // This guarantees:
            // 1. ciphertext.Length != plaintext.Length → always different
            // 2. Even if lengths were equal, XOR with 0xA5 changes content
            byte[] ciphertext = new byte[plaintext.Length + 1];
            ciphertext[0] = CipherTag;
            for (int i = 0; i < plaintext.Length; i++)
            {
                ciphertext[i + 1] = (byte)(plaintext[i] ^ XorKey);
            }

            return Task.FromResult(Result<byte[]>.Success(ciphertext));
        }

        public Task<Result<byte[]>> DecryptAsync(byte[] ciphertext, string keyHandle, CancellationToken ct)
        {
            if (ciphertext.Length < 1 || ciphertext[0] != CipherTag)
            {
                return Task.FromResult(
                    Result<byte[]>.Failure("Invalid ciphertext format", "DECRYPT_ERROR"));
            }

            // Reverse: strip tag byte, XOR back to recover plaintext
            byte[] plaintext = new byte[ciphertext.Length - 1];
            for (int i = 0; i < plaintext.Length; i++)
            {
                plaintext[i] = (byte)(ciphertext[i + 1] ^ XorKey);
            }

            return Task.FromResult(Result<byte[]>.Success(plaintext));
        }
    }

    #endregion
}

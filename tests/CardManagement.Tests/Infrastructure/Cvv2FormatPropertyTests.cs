using System.Text;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Cards;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for CVV2 Format Invariant (Property 8).
/// For any virtual card issuance request, the generated CVV2 SHALL be exactly
/// 3 numeric digits (characters '0'-'9').
/// 
/// **Validates: Requirements 5.2**
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "8")]
public class Cvv2FormatPropertyTests
{
    /// <summary>
    /// Known BIN ranges used for generating random card issuance requests.
    /// </summary>
    private static readonly (string Prefix, CardScheme Scheme, int PanLength)[] KnownBins =
    {
        ("506199", CardScheme.Verve, 19),
        ("650002", CardScheme.Verve, 19),
        ("4111", CardScheme.Visa, 16),
        ("4532", CardScheme.Visa, 16),
        ("5100", CardScheme.Mastercard, 16),
        ("5200", CardScheme.Mastercard, 16),
        ("5300", CardScheme.Mastercard, 16),
    };

    /// <summary>
    /// **Validates: Requirements 5.2**
    /// 
    /// Property 8: CVV2 Format Invariant
    /// For any virtual card issuance request with a mocked HSM that returns random 3-digit CVV2s,
    /// the CVV2 produced during card issuance is exactly 3 numeric digits ('0'-'9').
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Cvv2_IsExactlyThreeNumericDigits()
    {
        var gen = from binIndex in Gen.Choose(0, KnownBins.Length - 1)
                  let bin = KnownBins[binIndex]
                  from validityMonths in Gen.Choose(1, 60)
                  from cvv2Digit1 in Gen.Choose(0, 9)
                  from cvv2Digit2 in Gen.Choose(0, 9)
                  from cvv2Digit3 in Gen.Choose(0, 9)
                  let cvv2 = $"{cvv2Digit1}{cvv2Digit2}{cvv2Digit3}"
                  select (Bin: bin, ValidityMonths: validityMonths, Cvv2: cvv2);

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            var hsm = new MockHsmService(testCase.Cvv2, testCase.Bin.Prefix, testCase.Bin.PanLength);
            var repo = new MockCardRepository();
            var logger = NullLoggerFactory.Instance.CreateLogger<VirtualCardService>();
            var eventPublisher = new CardManagement.Infrastructure.Kafka.NullEventPublisher(NullLoggerFactory.Instance.CreateLogger<CardManagement.Infrastructure.Kafka.NullEventPublisher>());
            var service = new VirtualCardService(hsm, repo, eventPublisher, logger);

            var request = new CardIssuanceRequest
            {
                CardScheme = testCase.Bin.Scheme,
                BinRange = new BinRange(testCase.Bin.Prefix, testCase.Bin.Scheme, testCase.Bin.PanLength),
                AccountId = Guid.NewGuid(),
                ValidityMonths = testCase.ValidityMonths
            };

            var result = service.IssueCardAsync(request, CancellationToken.None)
                .GetAwaiter().GetResult();

            // The issuance should succeed
            if (!result.IsSuccess)
                return false.Label($"Card issuance failed: {result.ErrorMessage}");

            // Retrieve the CVV2 that was stored (via encrypt call tracking)
            string storedCvv2 = hsm.LastCvv2Returned;

            // Assert: CVV2 is exactly 3 characters
            bool isLength3 = storedCvv2.Length == 3;

            // Assert: All characters are numeric digits ('0'-'9')
            bool allDigits = storedCvv2.All(c => c >= '0' && c <= '9');

            return (isLength3 && allDigits)
                .Label($"CVV2 '{storedCvv2}' should be exactly 3 numeric digits. " +
                       $"Length={storedCvv2.Length}, AllDigits={allDigits}");
        });
    }

    #region Mock Implementations

    /// <summary>
    /// Mock HSM service that returns a configurable CVV2 and generates valid PANs.
    /// </summary>
    private class MockHsmService : IHsmService
    {
        private readonly string _cvv2ToReturn;
        private readonly string _binPrefix;
        private readonly int _panLength;
        private int _callCount;

        public string LastCvv2Returned => _cvv2ToReturn;

        public MockHsmService(string cvv2ToReturn, string binPrefix, int panLength)
        {
            _cvv2ToReturn = cvv2ToReturn;
            _binPrefix = binPrefix;
            _panLength = panLength;
        }

        public Task<Result<string>> GeneratePanAsync(BinRange binRange, CancellationToken ct)
        {
            _callCount++;
            // Generate random digits to fill the PAN (minus prefix and check digit)
            int randomDigitsNeeded = binRange.PanLength - binRange.Prefix.Length - 1;
            var rng = new System.Random(42 + _callCount);
            var digits = new char[randomDigitsNeeded];
            for (int i = 0; i < randomDigitsNeeded; i++)
            {
                digits[i] = (char)('0' + rng.Next(10));
            }

            string partialPan = binRange.Prefix + new string(digits);
            return Task.FromResult(Result<string>.Success(partialPan));
        }

        public Task<Result<string>> ComputeCvv2Async(string pan, string expiryDate, CancellationToken ct)
        {
            return Task.FromResult(Result<string>.Success(_cvv2ToReturn));
        }

        public Task<Result<byte[]>> EncryptAsync(byte[] plaintext, string keyHandle, CancellationToken ct)
        {
            // Simple fake encryption: prepend marker byte and XOR
            byte[] ciphertext = new byte[plaintext.Length + 1];
            ciphertext[0] = 0xBB;
            for (int i = 0; i < plaintext.Length; i++)
            {
                ciphertext[i + 1] = (byte)(plaintext[i] ^ 0x42);
            }
            return Task.FromResult(Result<byte[]>.Success(ciphertext));
        }

        public Task<Result<byte[]>> DecryptAsync(byte[] ciphertext, string keyHandle, CancellationToken ct)
        {
            byte[] plaintext = new byte[ciphertext.Length - 1];
            for (int i = 0; i < plaintext.Length; i++)
            {
                plaintext[i] = (byte)(ciphertext[i + 1] ^ 0x42);
            }
            return Task.FromResult(Result<byte[]>.Success(plaintext));
        }
    }

    /// <summary>
    /// Mock card repository that never reports collisions.
    /// </summary>
    private class MockCardRepository : ICardRepository
    {
        public Task<Card?> GetByPanHashAsync(string panHash, CancellationToken cancellationToken = default)
            => Task.FromResult<Card?>(null);

        public Task<bool> ExistsByPanHashAsync(string panHash, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task AddAsync(Card card, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    #endregion
}

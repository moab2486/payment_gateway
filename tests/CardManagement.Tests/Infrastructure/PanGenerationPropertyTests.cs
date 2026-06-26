using System.Text;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Services;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Cards;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for PAN Generation Invariant (Property 7).
/// 
/// **Validates: Requirements 5.1, 5.5**
/// 
/// For any BIN range and card scheme configuration, a generated PAN SHALL:
/// - Start with the BIN prefix
/// - Have a total length between 16 and 19 digits
/// - Pass the Luhn algorithm check
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "7")]
public class PanGenerationPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 5.1, 5.5**
    /// 
    /// Property 7: PAN Generation Invariant
    /// Generate BIN ranges with various prefix lengths (1-6 digits) and PAN lengths (16-19).
    /// Mock HSM to return deterministic random digits.
    /// Assert: PAN starts with BIN prefix, length matches BinRange.PanLength (16-19), passes Luhn check.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GeneratedPan_StartsWithBinPrefix_HasCorrectLength_PassesLuhn()
    {
        var gen = from prefixLength in Gen.Choose(1, 6)
                  from prefixDigits in Gen.ArrayOf(prefixLength, Gen.Choose(0, 9))
                  let prefix = string.Concat(prefixDigits.Select(d => d.ToString()))
                  from panLength in Gen.Choose(16, 19)
                  where prefixLength < panLength
                  from scheme in Gen.Elements(CardScheme.Visa, CardScheme.Mastercard, CardScheme.Verve)
                  from seed in Gen.Choose(0, int.MaxValue - 1)
                  select new PanGenerationTestCase(prefix, panLength, scheme, seed);

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            // Arrange: create a mock HSM that generates deterministic random digits
            var binRange = new BinRange(testCase.Prefix, testCase.Scheme, testCase.PanLength);
            var hsm = new DeterministicHsmService(testCase.Seed);
            var repo = new NoCollisionCardRepository();
            var logger = NullLoggerFactory.Instance.CreateLogger<VirtualCardService>();
            var eventPublisher = new CardManagement.Infrastructure.Kafka.NullEventPublisher(NullLoggerFactory.Instance.CreateLogger<CardManagement.Infrastructure.Kafka.NullEventPublisher>());
            var service = new VirtualCardService(hsm, repo, eventPublisher, logger);

            var request = new CardIssuanceRequest
            {
                CardScheme = testCase.Scheme,
                BinRange = binRange,
                AccountId = Guid.NewGuid(),
                ValidityMonths = 12
            };

            // Act: issue a card
            var result = service.IssueCardAsync(request, CancellationToken.None).GetAwaiter().GetResult();

            // The HSM captured the generated PAN for assertion
            string generatedPan = hsm.LastGeneratedFullPan!;

            // Assert 1: PAN starts with BIN prefix
            var startsWithPrefix = generatedPan.StartsWith(testCase.Prefix, StringComparison.Ordinal)
                .Label($"PAN '{generatedPan}' should start with prefix '{testCase.Prefix}'");

            // Assert 2: PAN length matches expected (16-19)
            var hasCorrectLength = (generatedPan.Length == testCase.PanLength)
                .Label($"PAN '{generatedPan}' should have length {testCase.PanLength} but has {generatedPan.Length}");

            // Assert 3: PAN passes Luhn check
            var passesLuhn = LuhnValidator.IsValid(generatedPan)
                .Label($"PAN '{generatedPan}' should pass Luhn validation");

            return startsWithPrefix.And(hasCorrectLength).And(passesLuhn);
        });
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.5**
    /// 
    /// Property 7 (direct): Directly test PAN generation via LuhnValidator.ComputeCheckDigit.
    /// For any partial PAN (BIN prefix + random digits), appending the Luhn check digit
    /// produces a full PAN that starts with the prefix, has the expected length, and passes Luhn.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DirectPanGeneration_AlwaysProducesValidPan()
    {
        var gen = from prefixLength in Gen.Choose(1, 6)
                  from prefixDigits in Gen.ArrayOf(prefixLength, Gen.Choose(0, 9))
                  let prefix = string.Concat(prefixDigits.Select(d => d.ToString()))
                  from panLength in Gen.Choose(16, 19)
                  where prefixLength < panLength
                  let randomDigitsNeeded = panLength - prefixLength - 1
                  from randomDigits in Gen.ArrayOf(randomDigitsNeeded, Gen.Choose(0, 9))
                  let partialPan = prefix + string.Concat(randomDigits.Select(d => d.ToString()))
                  select new { Prefix = prefix, PanLength = panLength, PartialPan = partialPan };

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            // Act: compute Luhn check digit and build full PAN
            char checkDigit = LuhnValidator.ComputeCheckDigit(testCase.PartialPan);
            string fullPan = testCase.PartialPan + checkDigit;

            // Assert 1: PAN starts with BIN prefix
            var startsWithPrefix = fullPan.StartsWith(testCase.Prefix, StringComparison.Ordinal)
                .Label($"PAN '{fullPan}' should start with prefix '{testCase.Prefix}'");

            // Assert 2: PAN has correct length
            var hasCorrectLength = (fullPan.Length == testCase.PanLength)
                .Label($"PAN '{fullPan}' should have length {testCase.PanLength} but has {fullPan.Length}");

            // Assert 3: PAN passes Luhn check
            var passesLuhn = LuhnValidator.IsValid(fullPan)
                .Label($"PAN '{fullPan}' should pass Luhn validation");

            return startsWithPrefix.And(hasCorrectLength).And(passesLuhn);
        });
    }

    #region Test Data Types and Fakes

    private record PanGenerationTestCase(string Prefix, int PanLength, CardScheme Scheme, int Seed);

    /// <summary>
    /// Deterministic HSM service that generates random digits using a seeded Random.
    /// Captures the full PAN (after Luhn check digit appended by VirtualCardService) for assertion.
    /// </summary>
    private class DeterministicHsmService : IHsmService
    {
        private readonly int _seed;
        public string? LastGeneratedFullPan { get; private set; }

        public DeterministicHsmService(int seed)
        {
            _seed = seed;
        }

        public Task<Result<string>> GeneratePanAsync(BinRange binRange, CancellationToken ct)
        {
            // Generate deterministic random digits for the middle portion of the PAN
            int randomDigitsNeeded = binRange.PanLength - binRange.Prefix.Length - 1;
            var rng = new System.Random(_seed);
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
            return Task.FromResult(Result<string>.Success("123"));
        }

        public Task<Result<byte[]>> EncryptAsync(byte[] plaintext, string keyHandle, CancellationToken ct)
        {
            // Capture the PAN when it's being encrypted (first encryption call is the PAN)
            string value = Encoding.UTF8.GetString(plaintext);
            if (value.All(char.IsDigit) && value.Length >= 16 && value.Length <= 19)
            {
                LastGeneratedFullPan = value;
            }

            // Simple fake encryption
            byte[] ciphertext = new byte[plaintext.Length + 1];
            ciphertext[0] = 0xAA;
            for (int i = 0; i < plaintext.Length; i++)
            {
                ciphertext[i + 1] = (byte)(plaintext[i] ^ 0x55);
            }

            return Task.FromResult(Result<byte[]>.Success(ciphertext));
        }

        public Task<Result<byte[]>> DecryptAsync(byte[] ciphertext, string keyHandle, CancellationToken ct)
        {
            byte[] plaintext = new byte[ciphertext.Length - 1];
            for (int i = 0; i < plaintext.Length; i++)
            {
                plaintext[i] = (byte)(ciphertext[i + 1] ^ 0x55);
            }

            return Task.FromResult(Result<byte[]>.Success(plaintext));
        }
    }

    /// <summary>
    /// Card repository that never reports PAN collisions.
    /// </summary>
    private class NoCollisionCardRepository : ICardRepository
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

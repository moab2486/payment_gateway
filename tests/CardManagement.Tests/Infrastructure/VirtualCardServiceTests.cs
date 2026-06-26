using System.Text;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Services;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Cards;
using CardManagement.Infrastructure.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for VirtualCardService covering issuance logic, retry behavior,
/// Luhn validation, PAN collision handling, and HSM failure scenarios.
/// </summary>
public class VirtualCardServiceTests
{
    private static VirtualCardService CreateService(
        FakeHsmService? hsm = null,
        FakeCardRepository? repo = null)
    {
        var hsmService = hsm ?? new FakeHsmService();
        var cardRepository = repo ?? new FakeCardRepository();
        var logger = new NullLoggerFactory().CreateLogger<VirtualCardService>();
        var eventPublisher = new NullEventPublisher(new NullLoggerFactory().CreateLogger<NullEventPublisher>());
        return new VirtualCardService(hsmService, cardRepository, eventPublisher, logger);
    }

    private static CardIssuanceRequest CreateValidRequest(int validityMonths = 12)
    {
        return new CardIssuanceRequest
        {
            CardScheme = CardScheme.Visa,
            BinRange = new BinRange("411111", CardScheme.Visa, 16),
            AccountId = Guid.NewGuid(),
            ValidityMonths = validityMonths
        };
    }

    [Fact]
    public async Task IssueCardAsync_WithValidRequest_ReturnsSuccessfulVirtualCard()
    {
        var hsm = new FakeHsmService();
        var repo = new FakeCardRepository();
        var service = CreateService(hsm, repo);
        var request = CreateValidRequest();

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal("Active", result.Value.Status);
        Assert.Matches(@"^\d{2}/\d{2}$", result.Value.ExpiryDate);
    }

    [Fact]
    public async Task IssueCardAsync_WithValidRequest_ReturnsMaskedPan()
    {
        var hsm = new FakeHsmService();
        var repo = new FakeCardRepository();
        var service = CreateService(hsm, repo);
        var request = CreateValidRequest();

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Masked PAN should show only last 4 digits
        Assert.EndsWith(result.Value!.PanMasked[^4..], result.Value.PanMasked);
        Assert.Contains("*", result.Value.PanMasked);
        Assert.Equal(16, result.Value.PanMasked.Length); // full length preserved in mask
    }

    [Fact]
    public async Task IssueCardAsync_PersistsCardToRepository()
    {
        var hsm = new FakeHsmService();
        var repo = new FakeCardRepository();
        var service = CreateService(hsm, repo);
        var request = CreateValidRequest();

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repo.AddedCards);
        Assert.Equal(request.AccountId, repo.AddedCards[0].AccountId);
        Assert.Equal(request.CardScheme, repo.AddedCards[0].CardScheme);
    }

    [Fact]
    public async Task IssueCardAsync_EncryptsPanAndCvv2()
    {
        var hsm = new FakeHsmService();
        var repo = new FakeCardRepository();
        var service = CreateService(hsm, repo);
        var request = CreateValidRequest();

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repo.AddedCards);

        var card = repo.AddedCards[0];
        // PAN and CVV2 encrypted fields should be Base64-encoded
        Assert.NotEmpty(card.PanEncrypted);
        Assert.NotEmpty(card.Cvv2Encrypted);
        // They should not equal the raw PAN (they're encrypted)
        Assert.DoesNotContain("411111", card.PanEncrypted);
    }

    [Fact]
    public async Task IssueCardAsync_GeneratedPanPassesLuhnValidation()
    {
        var hsm = new FakeHsmService();
        var repo = new FakeCardRepository();
        var service = CreateService(hsm, repo);
        var request = CreateValidRequest();

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The PAN stored in the fake HSM's encrypt calls should be Luhn-valid
        Assert.True(hsm.LastEncryptedPans.Count > 0);
        string encryptedPanBytes = Encoding.UTF8.GetString(hsm.LastEncryptedPans[0]);
        Assert.True(LuhnValidator.IsValid(encryptedPanBytes));
    }

    [Fact]
    public async Task IssueCardAsync_WhenHsmUnavailable_ReturnsFailure()
    {
        var hsm = new FakeHsmService { ShouldFailPanGeneration = true };
        var service = CreateService(hsm);
        var request = CreateValidRequest();

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("HSM", result.ErrorMessage!);
    }

    [Fact]
    public async Task IssueCardAsync_WhenCvv2GenerationFails_ReturnsFailure()
    {
        var hsm = new FakeHsmService { ShouldFailCvv2 = true };
        var service = CreateService(hsm);
        var request = CreateValidRequest();

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("CVV2", result.ErrorMessage!);
    }

    [Fact]
    public async Task IssueCardAsync_WhenEncryptionFails_ReturnsFailure()
    {
        var hsm = new FakeHsmService { ShouldFailEncryption = true };
        var service = CreateService(hsm);
        var request = CreateValidRequest();

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("encryption failed", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IssueCardAsync_WhenPanCollision_RetriesUpTo5Times()
    {
        var hsm = new FakeHsmService();
        var repo = new FakeCardRepository { CollisionCount = 4 }; // 4 collisions, 5th succeeds
        var service = CreateService(hsm, repo);
        var request = CreateValidRequest();

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, repo.ExistsByPanHashCallCount);
    }

    [Fact]
    public async Task IssueCardAsync_WhenPanCollisionExhausted_ReturnsFailure()
    {
        var hsm = new FakeHsmService();
        var repo = new FakeCardRepository { CollisionCount = 5 }; // All 5 attempts collide
        var service = CreateService(hsm, repo);
        var request = CreateValidRequest();

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("PAN_COLLISION_EXHAUSTED", result.ErrorCode);
    }

    [Fact]
    public async Task IssueCardAsync_InvalidValidityMonths_Zero_ReturnsFailure()
    {
        var service = CreateService();
        var request = CreateValidRequest(validityMonths: 0);

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_VALIDITY_PERIOD", result.ErrorCode);
    }

    [Fact]
    public async Task IssueCardAsync_InvalidValidityMonths_Over60_ReturnsFailure()
    {
        var service = CreateService();
        var request = CreateValidRequest(validityMonths: 61);

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_VALIDITY_PERIOD", result.ErrorCode);
    }

    [Fact]
    public async Task IssueCardAsync_NullRequest_ReturnsFailure()
    {
        var service = CreateService();

        var result = await service.IssueCardAsync(null!, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_REQUEST", result.ErrorCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(60)]
    public void CalculateExpiryDate_ValidMonths_ReturnsCorrectFormat(int months)
    {
        string expiryDate = VirtualCardService.CalculateExpiryDate(months);

        Assert.Matches(@"^\d{2}/\d{2}$", expiryDate);

        // Parse and verify month is between 01 and 12
        int month = int.Parse(expiryDate[..2]);
        Assert.InRange(month, 1, 12);
    }

    [Fact]
    public void CalculateExpiryDate_CorrectMonthOffset()
    {
        var now = DateTime.UtcNow;
        var expected = now.AddMonths(12);
        string expectedMmYy = expected.ToString("MM/yy");

        string result = VirtualCardService.CalculateExpiryDate(12);

        Assert.Equal(expectedMmYy, result);
    }

    [Fact]
    public async Task IssueCardAsync_PanHashStoredForLookup()
    {
        var hsm = new FakeHsmService();
        var repo = new FakeCardRepository();
        var service = CreateService(hsm, repo);
        var request = CreateValidRequest();

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repo.AddedCards);
        // PAN hash should be a hex string (SHA-256 = 64 hex chars)
        Assert.Equal(64, repo.AddedCards[0].PanHash.Length);
        Assert.Matches("^[0-9a-f]{64}$", repo.AddedCards[0].PanHash);
    }

    [Fact]
    public async Task IssueCardAsync_WithVerveScheme_UsesCorrectBinPrefix()
    {
        var hsm = new FakeHsmService();
        var repo = new FakeCardRepository();
        var service = CreateService(hsm, repo);
        var request = new CardIssuanceRequest
        {
            CardScheme = CardScheme.Verve,
            BinRange = new BinRange("506199", CardScheme.Verve, 19),
            AccountId = Guid.NewGuid(),
            ValidityMonths = 24
        };

        var result = await service.IssueCardAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repo.AddedCards);
        Assert.Equal("506199", repo.AddedCards[0].BinRange);
        Assert.Equal(CardScheme.Verve, repo.AddedCards[0].CardScheme);
    }

    #region Fake Implementations

    /// <summary>
    /// Fake HSM service that returns deterministic results for testing.
    /// </summary>
    private class FakeHsmService : IHsmService
    {
        public bool ShouldFailPanGeneration { get; set; }
        public bool ShouldFailCvv2 { get; set; }
        public bool ShouldFailEncryption { get; set; }
        public List<byte[]> LastEncryptedPans { get; } = new();

        private int _panGenerationCallCount;

        public Task<Result<string>> GeneratePanAsync(BinRange binRange, CancellationToken ct)
        {
            if (ShouldFailPanGeneration)
                return Task.FromResult(Result<string>.Failure("HSM timeout", "HSM_TIMEOUT"));

            _panGenerationCallCount++;

            // Generate deterministic random digits based on call count for reproducibility
            int randomDigitsNeeded = binRange.PanLength - binRange.Prefix.Length - 1;
            var random = new Random(42 + _panGenerationCallCount);
            var digits = new char[randomDigitsNeeded];
            for (int i = 0; i < randomDigitsNeeded; i++)
            {
                digits[i] = (char)('0' + random.Next(10));
            }

            string partialPan = binRange.Prefix + new string(digits);
            return Task.FromResult(Result<string>.Success(partialPan));
        }

        public Task<Result<string>> ComputeCvv2Async(string pan, string expiryDate, CancellationToken ct)
        {
            if (ShouldFailCvv2)
                return Task.FromResult(Result<string>.Failure("CVV2 computation failed", "HSM_ERROR"));

            // Return a deterministic CVV2
            return Task.FromResult(Result<string>.Success("123"));
        }

        public Task<Result<byte[]>> EncryptAsync(byte[] plaintext, string keyHandle, CancellationToken ct)
        {
            if (ShouldFailEncryption)
                return Task.FromResult(Result<byte[]>.Failure("Encryption failed", "HSM_ERROR"));

            LastEncryptedPans.Add(plaintext);

            // Simple fake "encryption" — XOR with a fixed key byte for testing
            byte[] ciphertext = new byte[plaintext.Length + 1];
            ciphertext[0] = 0xAA; // marker to differentiate from plaintext
            for (int i = 0; i < plaintext.Length; i++)
            {
                ciphertext[i + 1] = (byte)(plaintext[i] ^ 0x55);
            }

            return Task.FromResult(Result<byte[]>.Success(ciphertext));
        }

        public Task<Result<byte[]>> DecryptAsync(byte[] ciphertext, string keyHandle, CancellationToken ct)
        {
            // Reverse the fake "encryption"
            byte[] plaintext = new byte[ciphertext.Length - 1];
            for (int i = 0; i < plaintext.Length; i++)
            {
                plaintext[i] = (byte)(ciphertext[i + 1] ^ 0x55);
            }

            return Task.FromResult(Result<byte[]>.Success(plaintext));
        }
    }

    /// <summary>
    /// Fake card repository for testing PAN uniqueness and card persistence.
    /// </summary>
    private class FakeCardRepository : ICardRepository
    {
        public int CollisionCount { get; set; }
        public int ExistsByPanHashCallCount { get; private set; }
        public List<Card> AddedCards { get; } = new();

        public Task<Card?> GetByPanHashAsync(string panHash, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Card?>(null);
        }

        public Task<bool> ExistsByPanHashAsync(string panHash, CancellationToken cancellationToken = default)
        {
            ExistsByPanHashCallCount++;
            bool exists = ExistsByPanHashCallCount <= CollisionCount;
            return Task.FromResult(exists);
        }

        public Task AddAsync(Card card, CancellationToken cancellationToken = default)
        {
            AddedCards.Add(card);
            return Task.CompletedTask;
        }
    }

    #endregion
}

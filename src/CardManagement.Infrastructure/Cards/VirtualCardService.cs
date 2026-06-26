using System.Security.Cryptography;
using System.Text;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Services;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.Cards;

/// <summary>
/// Implements virtual card issuance logic: PAN generation with Luhn validation,
/// CVV2 computation, expiry date calculation, encryption at rest, and persistence.
/// </summary>
public sealed class VirtualCardService : IVirtualCardService
{
    private const int MaxLuhnRetries = 3;
    private const int MaxCollisionRetries = 5;
    private const string EncryptionKeyHandle = "card-data-key";

    private readonly IHsmService _hsmService;
    private readonly ICardRepository _cardRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<VirtualCardService> _logger;

    public VirtualCardService(
        IHsmService hsmService,
        ICardRepository cardRepository,
        IEventPublisher eventPublisher,
        ILogger<VirtualCardService> logger)
    {
        _hsmService = hsmService ?? throw new ArgumentNullException(nameof(hsmService));
        _cardRepository = cardRepository ?? throw new ArgumentNullException(nameof(cardRepository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<VirtualCard>> IssueCardAsync(CardIssuanceRequest request, CancellationToken ct)
    {
        if (request == null)
            return Result<VirtualCard>.Failure("Card issuance request cannot be null.", "INVALID_REQUEST");

        if (request.ValidityMonths < 1 || request.ValidityMonths > 60)
            return Result<VirtualCard>.Failure(
                $"Validity months must be between 1 and 60. Got {request.ValidityMonths}.",
                "INVALID_VALIDITY_PERIOD");

        // Step 1-3: Generate a unique, Luhn-valid PAN
        var panResult = await GenerateUniquePanAsync(request, ct);
        if (!panResult.IsSuccess)
            return Result<VirtualCard>.Failure(panResult.ErrorMessage!, panResult.ErrorCode);

        string pan = panResult.Value!;

        // Step 4: Calculate expiry date
        string expiryDate = CalculateExpiryDate(request.ValidityMonths);

        // Step 5: Generate CVV2 via HSM
        var cvv2Result = await _hsmService.ComputeCvv2Async(pan, expiryDate, ct);
        if (!cvv2Result.IsSuccess)
        {
            _logger.LogError("CVV2 generation failed: {Error}", cvv2Result.ErrorMessage);
            return Result<VirtualCard>.Failure(
                $"CVV2 generation failed: {cvv2Result.ErrorMessage}",
                cvv2Result.ErrorCode ?? "CVV2_GENERATION_FAILED");
        }

        string cvv2 = cvv2Result.Value!;

        // Step 6: Encrypt PAN and CVV2 at rest using HSM-managed keys
        var panEncryptResult = await _hsmService.EncryptAsync(
            Encoding.UTF8.GetBytes(pan), EncryptionKeyHandle, ct);
        if (!panEncryptResult.IsSuccess)
        {
            _logger.LogError("PAN encryption failed: {Error}", panEncryptResult.ErrorMessage);
            return Result<VirtualCard>.Failure(
                $"PAN encryption failed: {panEncryptResult.ErrorMessage}",
                panEncryptResult.ErrorCode ?? "ENCRYPTION_FAILED");
        }

        var cvv2EncryptResult = await _hsmService.EncryptAsync(
            Encoding.UTF8.GetBytes(cvv2), EncryptionKeyHandle, ct);
        if (!cvv2EncryptResult.IsSuccess)
        {
            _logger.LogError("CVV2 encryption failed: {Error}", cvv2EncryptResult.ErrorMessage);
            return Result<VirtualCard>.Failure(
                $"CVV2 encryption failed: {cvv2EncryptResult.ErrorMessage}",
                cvv2EncryptResult.ErrorCode ?? "ENCRYPTION_FAILED");
        }

        // Step 7: Persist the card record
        string panEncryptedBase64 = Convert.ToBase64String(panEncryptResult.Value!);
        string cvv2EncryptedBase64 = Convert.ToBase64String(cvv2EncryptResult.Value!);
        string panHash = ComputePanHash(pan);

        var card = Card.Create(
            panEncrypted: panEncryptedBase64,
            panHash: panHash,
            cvv2Encrypted: cvv2EncryptedBase64,
            expiryDate: expiryDate,
            accountId: request.AccountId,
            cardScheme: request.CardScheme,
            binRange: request.BinRange.Prefix);

        await _cardRepository.AddAsync(card, ct);

        _logger.LogInformation(
            "Virtual card issued successfully. CardId={CardId}, Scheme={Scheme}, BIN={BinPrefix}",
            card.Id, request.CardScheme, request.BinRange.Prefix);

        // Publish card issued event (fire-and-forget; implementations handle exceptions internally)
        await _eventPublisher.PublishCardIssuedAsync(new CardIssuedEvent
        {
            CardId = card.Id,
            CardScheme = request.CardScheme.ToString(),
            AccountId = request.AccountId,
            IssuanceTimestamp = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString()
        }, ct);

        // Step 8: Return VirtualCard DTO with masked PAN
        var virtualCard = new VirtualCard
        {
            Id = card.Id,
            PanMasked = MaskPan(pan),
            ExpiryDate = expiryDate,
            Status = card.Status.ToString()
        };

        return Result<VirtualCard>.Success(virtualCard);
    }

    /// <summary>
    /// Generates a unique PAN that passes Luhn validation.
    /// Retries up to 3 times on Luhn failure and up to 5 times on PAN collision.
    /// </summary>
    private async Task<Result<string>> GenerateUniquePanAsync(
        CardIssuanceRequest request, CancellationToken ct)
    {
        for (int collisionAttempt = 0; collisionAttempt < MaxCollisionRetries; collisionAttempt++)
        {
            var panResult = await GenerateLuhnValidPanAsync(request, ct);
            if (!panResult.IsSuccess)
                return panResult;

            string pan = panResult.Value!;
            string panHash = ComputePanHash(pan);

            // Check PAN uniqueness via repository
            bool exists = await _cardRepository.ExistsByPanHashAsync(panHash, ct);
            if (!exists)
            {
                return Result<string>.Success(pan);
            }

            _logger.LogWarning(
                "PAN collision detected on attempt {Attempt}/{MaxAttempts}. Regenerating.",
                collisionAttempt + 1, MaxCollisionRetries);
        }

        _logger.LogError("PAN generation exhausted after {MaxAttempts} collision retries.", MaxCollisionRetries);
        return Result<string>.Failure(
            $"PAN generation exhausted after {MaxCollisionRetries} collision retries.",
            "PAN_COLLISION_EXHAUSTED");
    }

    /// <summary>
    /// Generates a PAN that passes Luhn validation, retrying up to 3 times on Luhn failure.
    /// </summary>
    private async Task<Result<string>> GenerateLuhnValidPanAsync(
        CardIssuanceRequest request, CancellationToken ct)
    {
        for (int luhnAttempt = 0; luhnAttempt < MaxLuhnRetries; luhnAttempt++)
        {
            // Call HSM to generate random digits (returns partial PAN without check digit)
            var hsmResult = await _hsmService.GeneratePanAsync(request.BinRange, ct);
            if (!hsmResult.IsSuccess)
            {
                _logger.LogError("HSM PAN generation failed: {Error}", hsmResult.ErrorMessage);
                return Result<string>.Failure(
                    $"HSM unavailable: {hsmResult.ErrorMessage}",
                    hsmResult.ErrorCode ?? "HSM_UNAVAILABLE");
            }

            string partialPan = hsmResult.Value!;

            // Compute and append Luhn check digit
            char checkDigit = LuhnValidator.ComputeCheckDigit(partialPan);
            string fullPan = partialPan + checkDigit;

            // Validate the full PAN passes Luhn
            if (LuhnValidator.IsValid(fullPan))
            {
                return Result<string>.Success(fullPan);
            }

            _logger.LogWarning(
                "Generated PAN failed Luhn validation on attempt {Attempt}/{MaxAttempts}. Retrying.",
                luhnAttempt + 1, MaxLuhnRetries);
        }

        _logger.LogError("PAN generation failed after {MaxAttempts} Luhn validation retries.", MaxLuhnRetries);
        return Result<string>.Failure(
            $"PAN generation failed after {MaxLuhnRetries} Luhn validation retries.",
            "LUHN_VALIDATION_EXHAUSTED");
    }

    /// <summary>
    /// Calculates the expiry date as issuance date + validity months, in MM/YY format.
    /// </summary>
    internal static string CalculateExpiryDate(int validityMonths)
    {
        var expiryDate = DateTime.UtcNow.AddMonths(validityMonths);
        return expiryDate.ToString("MM/yy");
    }

    /// <summary>
    /// Masks a PAN showing only the last 4 digits (e.g., "****1234").
    /// </summary>
    private static string MaskPan(string pan)
    {
        if (pan.Length <= 4)
            return pan;

        string lastFour = pan[^4..];
        string masked = new string('*', pan.Length - 4) + lastFour;
        return masked;
    }

    /// <summary>
    /// Computes a SHA-256 hash of the PAN for uniqueness lookup (non-reversible).
    /// </summary>
    private static string ComputePanHash(string pan)
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(pan));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

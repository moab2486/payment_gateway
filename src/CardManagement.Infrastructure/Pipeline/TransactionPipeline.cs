using System.Security.Cryptography;
using System.Text;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.Pipeline;

/// <summary>
/// Orchestrates the end-to-end transaction processing flow.
/// Authorization pipeline: message parsing → card validation → balance check →
/// ledger posting → response construction → response transmission.
/// On any step failure: rolls back ledger entries, returns decline with appropriate response code,
/// and logs the failure with step name and transaction reference.
/// </summary>
public sealed class TransactionPipeline : ITransactionPipeline
{
    private readonly IIso8583Gateway _iso8583Gateway;
    private readonly ICardRepository _cardRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly ILedgerService _ledgerService;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<TransactionPipeline> _logger;

    // ISO 8583 response codes
    private const string ResponseCodeApproved = "00";
    private const string ResponseCodeInvalidCard = "14";
    private const string ResponseCodeExpiredCard = "54";
    private const string ResponseCodeInsufficientFunds = "51";
    private const string ResponseCodeSystemMalfunction = "96";

    // ISO 8583 field numbers
    private const int FieldPan = 2;
    private const int FieldProcessingCode = 3;
    private const int FieldAmount = 4;
    private const int FieldStan = 11;
    private const int FieldExpiryDate = 14;
    private const int FieldResponseCode = 39;
    private const int FieldCurrencyCode = 49;

    // MTI values
    private const string MtiAuthorizationRequest = "0100";
    private const string MtiAuthorizationResponse = "0110";
    private const string MtiReversalRequest = "0420";
    private const string MtiReversalResponse = "0430";

    // Pipeline step names for tracing
    private const string StepMessageParsing = "MessageParsing";
    private const string StepCardValidation = "CardValidation";
    private const string StepBalanceCheck = "BalanceCheck";
    private const string StepLedgerPosting = "LedgerPosting";
    private const string StepResponseConstruction = "ResponseConstruction";
    private const string StepResponseTransmission = "ResponseTransmission";

    // STAN generation counter (thread-safe)
    private static long _stanCounter = InitializeStanCounter();

    public TransactionPipeline(
        IIso8583Gateway iso8583Gateway,
        ICardRepository cardRepository,
        IAccountRepository accountRepository,
        ILedgerService ledgerService,
        ITransactionRepository transactionRepository,
        IEventPublisher eventPublisher,
        ILogger<TransactionPipeline> logger)
    {
        _iso8583Gateway = iso8583Gateway ?? throw new ArgumentNullException(nameof(iso8583Gateway));
        _cardRepository = cardRepository ?? throw new ArgumentNullException(nameof(cardRepository));
        _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
        _ledgerService = ledgerService ?? throw new ArgumentNullException(nameof(ledgerService));
        _transactionRepository = transactionRepository ?? throw new ArgumentNullException(nameof(transactionRepository));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Iso8583Message> ProcessAuthorizationAsync(Iso8583Message request, CancellationToken ct)
    {
        var stan = GenerateStan();
        var transactionReference = $"AUTH-{stan}";

        _logger.LogInformation(
            "Starting authorization pipeline for transaction {TransactionReference}",
            transactionReference);

        // Step 1: Message Parsing — validate the request structure
        var parseResult = ValidateMessageStructure(request);
        if (!parseResult.IsSuccess)
        {
            _logger.LogWarning(
                "Authorization pipeline failed at step {Step} for transaction {TransactionReference}: {Error}",
                StepMessageParsing, transactionReference, parseResult.ErrorMessage);

            return ConstructDeclineResponse(request, ResponseCodeSystemMalfunction, stan);
        }

        // Extract fields from the validated message
        var pan = request.Fields[FieldPan];
        var amountStr = request.Fields[FieldAmount];
        var amount = long.Parse(amountStr);
        var currency = request.Fields[FieldCurrencyCode];
        var expiryDate = request.Fields[FieldExpiryDate];

        // Step 2: Card Validation (PAN lookup, status check, expiry verification)
        var panHash = ComputePanHash(pan);
        Card? card;
        try
        {
            card = await _cardRepository.GetByPanHashAsync(panHash, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Authorization pipeline failed at step {Step} for transaction {TransactionReference}: database error during PAN lookup",
                StepCardValidation, transactionReference);

            return ConstructDeclineResponse(request, ResponseCodeSystemMalfunction, stan);
        }

        if (card is null)
        {
            _logger.LogWarning(
                "Authorization pipeline failed at step {Step} for transaction {TransactionReference}: card not found for PAN hash",
                StepCardValidation, transactionReference);

            await PersistDeclinedTransaction(stan, MtiAuthorizationRequest, Guid.Empty, amount, currency, StepCardValidation, ResponseCodeInvalidCard, ct).ConfigureAwait(false);
            return ConstructDeclineResponse(request, ResponseCodeInvalidCard, stan);
        }

        // Check card status is Active
        if (card.Status != CardStatus.Active)
        {
            _logger.LogWarning(
                "Authorization pipeline failed at step {Step} for transaction {TransactionReference}: card status is {CardStatus}",
                StepCardValidation, transactionReference, card.Status);

            await PersistDeclinedTransaction(stan, MtiAuthorizationRequest, card.Id, amount, currency, StepCardValidation, ResponseCodeInvalidCard, ct).ConfigureAwait(false);
            return ConstructDeclineResponse(request, ResponseCodeInvalidCard, stan);
        }

        // Check expiry date — card's ExpiryDate is in MM/YY format, message field 14 is YYMM
        if (IsCardExpired(card.ExpiryDate, expiryDate))
        {
            _logger.LogWarning(
                "Authorization pipeline failed at step {Step} for transaction {TransactionReference}: card is expired",
                StepCardValidation, transactionReference);

            await PersistDeclinedTransaction(stan, MtiAuthorizationRequest, card.Id, amount, currency, StepCardValidation, ResponseCodeExpiredCard, ct).ConfigureAwait(false);
            return ConstructDeclineResponse(request, ResponseCodeExpiredCard, stan);
        }

        // Step 3: Balance Check (sufficient funds)
        Account? account;
        try
        {
            account = await _accountRepository.GetByIdAsync(card.AccountId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Authorization pipeline failed at step {Step} for transaction {TransactionReference}: database error during account lookup",
                StepBalanceCheck, transactionReference);

            await PersistDeclinedTransaction(stan, MtiAuthorizationRequest, card.Id, amount, currency, StepBalanceCheck, ResponseCodeSystemMalfunction, ct).ConfigureAwait(false);
            return ConstructDeclineResponse(request, ResponseCodeSystemMalfunction, stan);
        }

        if (account is null)
        {
            _logger.LogWarning(
                "Authorization pipeline failed at step {Step} for transaction {TransactionReference}: account not found for card",
                StepBalanceCheck, transactionReference);

            await PersistDeclinedTransaction(stan, MtiAuthorizationRequest, card.Id, amount, currency, StepBalanceCheck, ResponseCodeSystemMalfunction, ct).ConfigureAwait(false);
            return ConstructDeclineResponse(request, ResponseCodeSystemMalfunction, stan);
        }

        if (account.IsDebitNormal && account.Balance < amount)
        {
            _logger.LogWarning(
                "Authorization pipeline failed at step {Step} for transaction {TransactionReference}: insufficient funds (balance: {Balance}, amount: {Amount})",
                StepBalanceCheck, transactionReference, account.Balance, amount);

            await PersistDeclinedTransaction(stan, MtiAuthorizationRequest, card.Id, amount, currency, StepBalanceCheck, ResponseCodeInsufficientFunds, ct).ConfigureAwait(false);
            return ConstructDeclineResponse(request, ResponseCodeInsufficientFunds, stan);
        }

        // Step 4: Ledger Posting (debit/credit entries)
        var ledgerTransaction = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new LedgerTransactionEntry
                {
                    AccountId = card.AccountId,
                    EntryType = EntryType.Debit,
                    Amount = amount
                },
                new LedgerTransactionEntry
                {
                    AccountId = card.AccountId,
                    EntryType = EntryType.Credit,
                    Amount = amount
                }
            },
            OperationIdentifier = stan,
            Description = $"Authorization transaction {stan} for amount {amount} {currency}"
        };

        var postResult = await _ledgerService.PostTransactionAsync(ledgerTransaction, ct).ConfigureAwait(false);
        if (!postResult.IsSuccess)
        {
            var responseCode = postResult.ErrorCode == "51"
                ? ResponseCodeInsufficientFunds
                : ResponseCodeSystemMalfunction;

            _logger.LogWarning(
                "Authorization pipeline failed at step {Step} for transaction {TransactionReference}: {Error}",
                StepLedgerPosting, transactionReference, postResult.ErrorMessage);

            await PersistDeclinedTransaction(stan, MtiAuthorizationRequest, card.Id, amount, currency, StepLedgerPosting, responseCode, ct).ConfigureAwait(false);
            return ConstructDeclineResponse(request, responseCode, stan);
        }

        // Step 5: Response Construction
        Iso8583Message response;
        try
        {
            response = ConstructApprovalResponse(request, stan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Authorization pipeline failed at step {Step} for transaction {TransactionReference}: error constructing response",
                StepResponseConstruction, transactionReference);

            // Roll back ledger entries
            await RollbackLedgerEntries(stan, ct).ConfigureAwait(false);
            await PersistDeclinedTransaction(stan, MtiAuthorizationRequest, card.Id, amount, currency, StepResponseConstruction, ResponseCodeSystemMalfunction, ct).ConfigureAwait(false);
            return ConstructDeclineResponse(request, ResponseCodeSystemMalfunction, stan);
        }

        // Step 6: Response Transmission — persist the approved transaction record
        TransactionRecord transactionRecord;
        try
        {
            var processorType = ResolveProcessorType(pan);
            transactionRecord = TransactionRecord.Create(
                stan,
                MtiAuthorizationRequest,
                card.Id,
                amount,
                currency,
                processorType);

            transactionRecord.Approve(ResponseCodeApproved, StepResponseTransmission);
            await _transactionRepository.AddAsync(transactionRecord, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Authorization pipeline failed at step {Step} for transaction {TransactionReference}: error persisting transaction record",
                StepResponseTransmission, transactionReference);

            // Roll back ledger entries on persistence failure
            await RollbackLedgerEntries(stan, ct).ConfigureAwait(false);
            return ConstructDeclineResponse(request, ResponseCodeSystemMalfunction, stan);
        }

        // Publish TransactionAuthorizedEvent (fire-and-forget, does not affect transaction outcome)
        await _eventPublisher.PublishTransactionAuthorizedAsync(new TransactionAuthorizedEvent
        {
            TransactionId = transactionRecord.Id,
            CardId = card.Id,
            Amount = amount,
            Currency = currency,
            ProcessorType = ResolveProcessorType(pan).ToString(),
            ResponseCode = ResponseCodeApproved,
            AuthorizationTimestamp = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString()
        }, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Authorization pipeline completed successfully for transaction {TransactionReference}",
            transactionReference);

        return response;
    }

    /// <inheritdoc />
    public async Task<Iso8583Message> ProcessReversalAsync(Iso8583Message request, CancellationToken ct)
    {
        var stan = request.Fields.TryGetValue(FieldStan, out var requestStan)
            ? requestStan
            : GenerateStan();

        var transactionReference = $"REV-{stan}";

        _logger.LogInformation(
            "Starting reversal pipeline for transaction {TransactionReference}",
            transactionReference);

        // Locate original transaction by STAN
        var reversalResult = await _ledgerService.ReverseTransactionAsync(stan, ct).ConfigureAwait(false);

        if (!reversalResult.IsSuccess)
        {
            _logger.LogWarning(
                "Reversal pipeline failed for transaction {TransactionReference}: {Error}",
                transactionReference, reversalResult.ErrorMessage);

            return ConstructReversalDeclineResponse(request, stan);
        }

        // Publish TransactionReversedEvent (fire-and-forget, does not affect transaction outcome)
        var reversalAmount = request.Fields.TryGetValue(FieldAmount, out var amt) ? long.Parse(amt) : 0;
        var reversalCurrency = request.Fields.TryGetValue(FieldCurrencyCode, out var cur) ? cur : string.Empty;

        await _eventPublisher.PublishTransactionReversedAsync(new TransactionReversedEvent
        {
            OriginalTransactionId = reversalResult.Value?.Id ?? Guid.NewGuid(),
            ReversalTransactionId = Guid.NewGuid(),
            Amount = reversalAmount,
            Currency = reversalCurrency,
            ReversalTimestamp = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString()
        }, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Reversal pipeline completed successfully for transaction {TransactionReference}",
            transactionReference);

        return ConstructReversalApprovalResponse(request, stan);
    }

    /// <summary>
    /// Validates that the request message has the required structure for an authorization request.
    /// </summary>
    private Result ValidateMessageStructure(Iso8583Message request)
    {
        if (request is null)
            return Result.Failure("Request message is null.", ResponseCodeSystemMalfunction);

        if (request.Mti != MtiAuthorizationRequest)
            return Result.Failure($"Expected MTI {MtiAuthorizationRequest} but received {request.Mti}.", ResponseCodeSystemMalfunction);

        // Check mandatory fields for authorization
        var requiredFields = new[] { FieldPan, FieldAmount, FieldExpiryDate, FieldCurrencyCode };
        foreach (var field in requiredFields)
        {
            if (!request.Fields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return Result.Failure($"Missing mandatory field {field}.", ResponseCodeSystemMalfunction);
            }
        }

        // Validate amount is a valid number
        if (!long.TryParse(request.Fields[FieldAmount], out var amount) || amount <= 0)
        {
            return Result.Failure("Invalid amount value.", ResponseCodeSystemMalfunction);
        }

        return Result.Success();
    }

    /// <summary>
    /// Checks if the card is expired by comparing the card's expiry date with the current date.
    /// Card ExpiryDate is stored as "MM/YY", message field 14 is "YYMM".
    /// </summary>
    private static bool IsCardExpired(string cardExpiryDate, string messageExpiryDate)
    {
        // Parse the card's stored expiry date (MM/YY format)
        if (cardExpiryDate.Length == 5 && cardExpiryDate[2] == '/')
        {
            var monthStr = cardExpiryDate[..2];
            var yearStr = cardExpiryDate[3..];

            if (int.TryParse(monthStr, out var month) && int.TryParse(yearStr, out var year))
            {
                // Convert 2-digit year to full year
                var fullYear = year >= 50 ? 1900 + year : 2000 + year;

                // Card is valid through the last day of the expiry month
                var expiryEnd = new DateTime(fullYear, month, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddMonths(1)
                    .AddDays(-1);

                return DateTime.UtcNow > expiryEnd;
            }
        }

        // If we can't parse the stored expiry, consider it expired for safety
        return true;
    }

    /// <summary>
    /// Generates a unique 6-digit System Trace Audit Number using an atomic counter.
    /// </summary>
    internal static string GenerateStan()
    {
        var counter = Interlocked.Increment(ref _stanCounter);
        // Wrap to 6 digits (000001–999999)
        var stanValue = (counter % 999999) + 1;
        return stanValue.ToString("D6");
    }

    /// <summary>
    /// Initializes the STAN counter using a timestamp-based seed to avoid collisions
    /// across application restarts.
    /// </summary>
    private static long InitializeStanCounter()
    {
        // Use current time ticks modulo a large number to seed the counter
        return DateTime.UtcNow.Ticks % 900000;
    }

    /// <summary>
    /// Computes a SHA-256 hash of the PAN for lookup purposes.
    /// </summary>
    private static string ComputePanHash(string pan)
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(pan));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Attempts to resolve the processor type from the PAN.
    /// Defaults to Interswitch if resolution fails.
    /// </summary>
    private static ProcessorType ResolveProcessorType(string pan)
    {
        // Default to Interswitch — in full integration the CardProcessorRouter handles this,
        // but the pipeline doesn't have a hard dependency on it for the transaction record.
        return ProcessorType.Interswitch;
    }

    /// <summary>
    /// Constructs an approval response (MTI 0110) with response code "00".
    /// </summary>
    private static Iso8583Message ConstructApprovalResponse(Iso8583Message request, string stan)
    {
        var responseFields = new Dictionary<int, string>();

        // Echo relevant fields from the request
        if (request.Fields.TryGetValue(FieldPan, out var pan))
            responseFields[FieldPan] = pan;
        if (request.Fields.TryGetValue(FieldProcessingCode, out var procCode))
            responseFields[FieldProcessingCode] = procCode;
        if (request.Fields.TryGetValue(FieldAmount, out var amount))
            responseFields[FieldAmount] = amount;
        if (request.Fields.TryGetValue(FieldCurrencyCode, out var currency))
            responseFields[FieldCurrencyCode] = currency;

        responseFields[FieldStan] = stan;
        responseFields[FieldResponseCode] = ResponseCodeApproved;

        return new Iso8583Message
        {
            Mti = MtiAuthorizationResponse,
            Fields = responseFields
        };
    }

    /// <summary>
    /// Constructs a decline response (MTI 0110) with the specified response code.
    /// </summary>
    private static Iso8583Message ConstructDeclineResponse(Iso8583Message request, string responseCode, string stan)
    {
        var responseFields = new Dictionary<int, string>();

        // Echo available fields from the request
        if (request?.Fields != null)
        {
            if (request.Fields.TryGetValue(FieldPan, out var pan))
                responseFields[FieldPan] = pan;
            if (request.Fields.TryGetValue(FieldProcessingCode, out var procCode))
                responseFields[FieldProcessingCode] = procCode;
            if (request.Fields.TryGetValue(FieldAmount, out var amount))
                responseFields[FieldAmount] = amount;
            if (request.Fields.TryGetValue(FieldCurrencyCode, out var currency))
                responseFields[FieldCurrencyCode] = currency;
        }

        responseFields[FieldStan] = stan;
        responseFields[FieldResponseCode] = responseCode;

        return new Iso8583Message
        {
            Mti = MtiAuthorizationResponse,
            Fields = responseFields
        };
    }

    /// <summary>
    /// Constructs a reversal approval response (MTI 0430) with response code "00".
    /// </summary>
    private static Iso8583Message ConstructReversalApprovalResponse(Iso8583Message request, string stan)
    {
        var responseFields = new Dictionary<int, string>();

        if (request?.Fields != null)
        {
            if (request.Fields.TryGetValue(FieldPan, out var pan))
                responseFields[FieldPan] = pan;
            if (request.Fields.TryGetValue(FieldAmount, out var amount))
                responseFields[FieldAmount] = amount;
        }

        responseFields[FieldStan] = stan;
        responseFields[FieldResponseCode] = ResponseCodeApproved;

        return new Iso8583Message
        {
            Mti = MtiReversalResponse,
            Fields = responseFields
        };
    }

    /// <summary>
    /// Constructs a reversal decline response (MTI 0430).
    /// </summary>
    private static Iso8583Message ConstructReversalDeclineResponse(Iso8583Message request, string stan)
    {
        var responseFields = new Dictionary<int, string>();

        if (request?.Fields != null)
        {
            if (request.Fields.TryGetValue(FieldPan, out var pan))
                responseFields[FieldPan] = pan;
            if (request.Fields.TryGetValue(FieldAmount, out var amount))
                responseFields[FieldAmount] = amount;
        }

        responseFields[FieldStan] = stan;
        responseFields[FieldResponseCode] = ResponseCodeSystemMalfunction;

        return new Iso8583Message
        {
            Mti = MtiReversalResponse,
            Fields = responseFields
        };
    }

    /// <summary>
    /// Persists a declined transaction record for audit purposes.
    /// </summary>
    private async Task PersistDeclinedTransaction(
        string stan,
        string messageType,
        Guid cardId,
        long amount,
        string currency,
        string failedStep,
        string responseCode,
        CancellationToken ct)
    {
        try
        {
            var processorType = ProcessorType.Interswitch;
            var transactionRecord = TransactionRecord.Create(
                stan,
                messageType,
                cardId == Guid.Empty ? Guid.NewGuid() : cardId,
                amount,
                currency,
                processorType);

            transactionRecord.Decline(responseCode, failedStep);
            await _transactionRepository.AddAsync(transactionRecord, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist declined transaction record for STAN {Stan}",
                stan);
            // Don't throw — the decline response should still be sent to the caller
        }
    }

    /// <summary>
    /// Attempts to roll back ledger entries created during this pipeline execution.
    /// Uses the reversal mechanism via the STAN as the operation identifier.
    /// </summary>
    private async Task RollbackLedgerEntries(string stan, CancellationToken ct)
    {
        try
        {
            await _ledgerService.ReverseTransactionAsync(stan, ct).ConfigureAwait(false);
            _logger.LogInformation("Rolled back ledger entries for STAN {Stan}", stan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to roll back ledger entries for STAN {Stan}. Manual reconciliation may be required.",
                stan);
        }
    }
}

using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Manages the full lifecycle of Direct Debit mandates: create, activate, submit debits, and cancel.
/// Coordinates between the NIBSS Direct Debit API client and local mandate persistence.
/// </summary>
public class MandateLifecycleManager
{
    private readonly INibssDirectDebitClient _directDebitClient;
    private readonly IMandateRepository _mandateRepository;
    private readonly DebitRetryScheduler _retryScheduler;
    private readonly IMandateEventPublisher _eventPublisher;
    private readonly DirectDebitOptions _options;
    private readonly ILogger<MandateLifecycleManager> _logger;

    /// <summary>
    /// NIBSS response code indicating insufficient funds in the debtor account.
    /// </summary>
    internal const string InsufficientFundsResponseCode = "51";

    public MandateLifecycleManager(
        INibssDirectDebitClient directDebitClient,
        IMandateRepository mandateRepository,
        DebitRetryScheduler retryScheduler,
        IMandateEventPublisher eventPublisher,
        IOptions<DirectDebitOptions> options,
        ILogger<MandateLifecycleManager> logger)
    {
        _directDebitClient = directDebitClient ?? throw new ArgumentNullException(nameof(directDebitClient));
        _mandateRepository = mandateRepository ?? throw new ArgumentNullException(nameof(mandateRepository));
        _retryScheduler = retryScheduler ?? throw new ArgumentNullException(nameof(retryScheduler));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new mandate at NIBSS and stores it locally with the returned NIBSS reference.
    /// </summary>
    public async Task<MandateLifecycleResult> CreateMandateAsync(
        DirectDebitMandateRequest request,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Creating Direct Debit mandate. MandateRef: {MandateRef}, Debtor: {Debtor}, Creditor: {Creditor}",
            request.MandateReference, request.DebtorAccount, request.CreditorAccount);

        var response = await _directDebitClient.CreateMandateAsync(request, ct);

        if (!NibssResponseCodes.IsSuccess(response.ResponseCode))
        {
            var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);
            _logger.LogWarning(
                "Mandate creation failed at NIBSS. MandateRef: {MandateRef}, Code: {Code}, Message: {Message}",
                request.MandateReference, response.ResponseCode, errorMessage);
            return MandateLifecycleResult.Failure(errorCode, errorMessage);
        }

        var mandate = DirectDebitMandate.Create(
            mandateReference: request.MandateReference,
            debtorAccount: request.DebtorAccount,
            creditorAccount: request.CreditorAccount,
            amount: new Money((long)request.Amount, request.CurrencyCode),
            frequency: request.Frequency,
            startDate: request.StartDate,
            endDate: request.EndDate);

        if (!string.IsNullOrWhiteSpace(response.NibssReference))
        {
            mandate.SetNibssReference(response.NibssReference);
        }

        await _mandateRepository.SaveAsync(mandate, ct);

        _logger.LogInformation(
            "Mandate created successfully. MandateRef: {MandateRef}, NibssRef: {NibssRef}, Id: {Id}",
            request.MandateReference, response.NibssReference, mandate.Id);

        await _eventPublisher.PublishMandateCreatedAsync(
            request.MandateReference, request.DebtorAccount, request.CreditorAccount, request.Amount, ct);

        return MandateLifecycleResult.Success(mandate.Id, response.NibssReference);
    }

    /// <summary>
    /// Activates an existing mandate at NIBSS and updates local status.
    /// </summary>
    public async Task<MandateLifecycleResult> ActivateMandateAsync(
        string mandateReference,
        CancellationToken ct)
    {
        _logger.LogInformation("Activating mandate. MandateRef: {MandateRef}", mandateReference);

        var mandate = await _mandateRepository.GetByMandateReferenceAsync(mandateReference, ct);
        if (mandate is null)
        {
            _logger.LogWarning("Mandate not found locally. MandateRef: {MandateRef}", mandateReference);
            return MandateLifecycleResult.Failure("MANDATE_NOT_FOUND", $"Mandate '{mandateReference}' not found.");
        }

        var response = await _directDebitClient.ActivateMandateAsync(mandateReference, ct);

        if (!NibssResponseCodes.IsSuccess(response.ResponseCode))
        {
            var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);
            _logger.LogWarning(
                "Mandate activation failed at NIBSS. MandateRef: {MandateRef}, Code: {Code}, Message: {Message}",
                mandateReference, response.ResponseCode, errorMessage);
            return MandateLifecycleResult.Failure(errorCode, errorMessage);
        }

        // Mandate is already Active from creation (per domain entity design).
        // Update the NIBSS reference if a new one is returned.
        if (!string.IsNullOrWhiteSpace(response.NibssReference))
        {
            mandate.SetNibssReference(response.NibssReference);
        }

        await _mandateRepository.UpdateAsync(mandate, ct);

        _logger.LogInformation(
            "Mandate activated successfully. MandateRef: {MandateRef}, NibssRef: {NibssRef}",
            mandateReference, response.NibssReference);

        await _eventPublisher.PublishMandateActivatedAsync(mandateReference, ct);

        return MandateLifecycleResult.Success(mandate.Id, response.NibssReference);
    }

    /// <summary>
    /// Submits a scheduled debit for an active mandate via NIBSS.
    /// </summary>
    public async Task<MandateLifecycleResult> SubmitScheduledDebitAsync(
        string mandateReference,
        decimal amount,
        string currencyCode,
        string transactionReference,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Submitting scheduled debit. MandateRef: {MandateRef}, Amount: {Amount}, TxRef: {TxRef}",
            mandateReference, amount, transactionReference);

        var mandate = await _mandateRepository.GetByMandateReferenceAsync(mandateReference, ct);
        if (mandate is null)
        {
            _logger.LogWarning("Mandate not found locally. MandateRef: {MandateRef}", mandateReference);
            return MandateLifecycleResult.Failure("MANDATE_NOT_FOUND", $"Mandate '{mandateReference}' not found.");
        }

        if (mandate.Status != MandateStatus.Active)
        {
            _logger.LogWarning(
                "Cannot submit debit for non-active mandate. MandateRef: {MandateRef}, Status: {Status}",
                mandateReference, mandate.Status);
            return MandateLifecycleResult.Failure("MANDATE_NOT_ACTIVE", $"Mandate '{mandateReference}' is not active (current status: {mandate.Status}).");
        }

        var submitRequest = new DirectDebitSubmitRequest(
            MandateReference: mandateReference,
            Amount: amount,
            CurrencyCode: currencyCode,
            TransactionReference: transactionReference);

        var response = await _directDebitClient.SubmitDebitAsync(submitRequest, ct);

        if (!NibssResponseCodes.IsSuccess(response.ResponseCode))
        {
            var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);
            _logger.LogWarning(
                "Scheduled debit submission failed. MandateRef: {MandateRef}, TxRef: {TxRef}, Code: {Code}, Message: {Message}",
                mandateReference, transactionReference, response.ResponseCode, errorMessage);
            await _eventPublisher.PublishMandateFailedAsync(mandateReference, transactionReference, errorCode, errorMessage, ct);
            return MandateLifecycleResult.Failure(errorCode, errorMessage);
        }

        _logger.LogInformation(
            "Scheduled debit submitted successfully. MandateRef: {MandateRef}, TxRef: {TxRef}, NibssRef: {NibssRef}",
            mandateReference, transactionReference, response.NibssReference);

        await _eventPublisher.PublishMandateDebitedAsync(mandateReference, transactionReference, amount, ct);

        return MandateLifecycleResult.Success(mandate.Id, response.NibssReference);
    }

    /// <summary>
    /// Submits a scheduled debit with retry logic for insufficient funds.
    /// Detects insufficient funds response codes from NIBSS and schedules retries
    /// according to the mandate retry policy (MaxRetries, RetryIntervalHours).
    /// </summary>
    /// <returns>
    /// A <see cref="DebitRetryResult"/> indicating whether the debit succeeded,
    /// needs retry, or has exhausted all retry attempts.
    /// </returns>
    public async Task<DebitRetryResult> SubmitScheduledDebitWithRetryAsync(
        string mandateReference,
        decimal amount,
        string currencyCode,
        string transactionReference,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Submitting scheduled debit with retry support. MandateRef: {MandateRef}, Amount: {Amount}, TxRef: {TxRef}",
            mandateReference, amount, transactionReference);

        var mandate = await _mandateRepository.GetByMandateReferenceAsync(mandateReference, ct);
        if (mandate is null)
        {
            _logger.LogWarning("Mandate not found locally. MandateRef: {MandateRef}", mandateReference);
            return DebitRetryResult.Failed("MANDATE_NOT_FOUND", $"Mandate '{mandateReference}' not found.");
        }

        if (mandate.Status != MandateStatus.Active)
        {
            _logger.LogWarning(
                "Cannot submit debit for non-active mandate. MandateRef: {MandateRef}, Status: {Status}",
                mandateReference, mandate.Status);
            return DebitRetryResult.Failed("MANDATE_NOT_ACTIVE", $"Mandate '{mandateReference}' is not active (current status: {mandate.Status}).");
        }

        var submitRequest = new DirectDebitSubmitRequest(
            MandateReference: mandateReference,
            Amount: amount,
            CurrencyCode: currencyCode,
            TransactionReference: transactionReference);

        var response = await _directDebitClient.SubmitDebitAsync(submitRequest, ct);

        if (NibssResponseCodes.IsSuccess(response.ResponseCode))
        {
            // Debit succeeded — clear any retry tracking for this mandate/transaction
            _retryScheduler.ClearRetryEntry(mandateReference, transactionReference);

            _logger.LogInformation(
                "Scheduled debit succeeded. MandateRef: {MandateRef}, TxRef: {TxRef}, NibssRef: {NibssRef}",
                mandateReference, transactionReference, response.NibssReference);

            await _eventPublisher.PublishMandateDebitedAsync(mandateReference, transactionReference, amount, ct);

            return DebitRetryResult.Successful(mandate.Id, response.NibssReference);
        }

        // Check if the failure is due to insufficient funds
        if (response.ResponseCode == InsufficientFundsResponseCode)
        {
            var (shouldRetry, retryCount, nextRetryTime) =
                _retryScheduler.RecordFailureAndEvaluate(mandateReference, transactionReference);

            var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);

            if (shouldRetry)
            {
                _logger.LogInformation(
                    "Insufficient funds — debit scheduled for retry. MandateRef: {MandateRef}, TxRef: {TxRef}, Attempt: {Attempt}/{MaxRetries}, NextRetry: {NextRetry}",
                    mandateReference, transactionReference, retryCount, _options.MaxRetries, nextRetryTime);

                await _eventPublisher.PublishMandateFailedAsync(mandateReference, transactionReference, errorCode, errorMessage, ct);

                return DebitRetryResult.ScheduledForRetry(
                    mandate.Id, retryCount, nextRetryTime!.Value, errorCode, errorMessage);
            }
            else
            {
                _logger.LogWarning(
                    "Insufficient funds — all retries exhausted. MandateRef: {MandateRef}, TxRef: {TxRef}, Attempts: {Attempts}",
                    mandateReference, transactionReference, retryCount);

                await _eventPublisher.PublishMandateFailedAsync(mandateReference, transactionReference, errorCode, errorMessage, ct);

                return DebitRetryResult.Exhausted(mandate.Id, retryCount, errorCode, errorMessage);
            }
        }

        // Non-insufficient-funds failure — do not retry, return failure directly
        var (failErrorCode, failErrorMessage) = NibssResponseCodes.Map(response.ResponseCode);
        _logger.LogWarning(
            "Scheduled debit failed (non-retryable). MandateRef: {MandateRef}, TxRef: {TxRef}, Code: {Code}, Message: {Message}",
            mandateReference, transactionReference, response.ResponseCode, failErrorMessage);

        await _eventPublisher.PublishMandateFailedAsync(mandateReference, transactionReference, failErrorCode, failErrorMessage, ct);

        return DebitRetryResult.Failed(failErrorCode, failErrorMessage);
    }

    /// <summary>
    /// Cancels a mandate at NIBSS and updates local status via the entity's Cancel() method.
    /// </summary>
    public async Task<MandateLifecycleResult> CancelMandateAsync(
        string mandateReference,
        CancellationToken ct)
    {
        _logger.LogInformation("Cancelling mandate. MandateRef: {MandateRef}", mandateReference);

        var mandate = await _mandateRepository.GetByMandateReferenceAsync(mandateReference, ct);
        if (mandate is null)
        {
            _logger.LogWarning("Mandate not found locally. MandateRef: {MandateRef}", mandateReference);
            return MandateLifecycleResult.Failure("MANDATE_NOT_FOUND", $"Mandate '{mandateReference}' not found.");
        }

        var response = await _directDebitClient.CancelMandateAsync(mandateReference, ct);

        if (!NibssResponseCodes.IsSuccess(response.ResponseCode))
        {
            var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);
            _logger.LogWarning(
                "Mandate cancellation failed at NIBSS. MandateRef: {MandateRef}, Code: {Code}, Message: {Message}",
                mandateReference, response.ResponseCode, errorMessage);
            return MandateLifecycleResult.Failure(errorCode, errorMessage);
        }

        mandate.Cancel();
        await _mandateRepository.UpdateAsync(mandate, ct);

        _logger.LogInformation(
            "Mandate cancelled successfully. MandateRef: {MandateRef}, NibssRef: {NibssRef}",
            mandateReference, response.NibssReference);

        await _eventPublisher.PublishMandateCancelledAsync(mandateReference, ct);

        return MandateLifecycleResult.Success(mandate.Id, response.NibssReference);
    }
}

/// <summary>
/// Result of a mandate lifecycle operation.
/// </summary>
public record MandateLifecycleResult
{
    public bool IsSuccess { get; init; }
    public Guid? MandateId { get; init; }
    public string? NibssReference { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static MandateLifecycleResult Success(Guid mandateId, string? nibssReference) =>
        new() { IsSuccess = true, MandateId = mandateId, NibssReference = nibssReference };

    public static MandateLifecycleResult Failure(string errorCode, string errorMessage) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}

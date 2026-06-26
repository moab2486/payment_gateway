using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;

namespace CardManagement.Infrastructure.Orchestration;

/// <summary>
/// Central payment orchestrator that routes requests through fraud check, saga execution,
/// and channel dispatch. Implements the full payment lifecycle flow:
/// validate → idempotency check → assign TransactionReference → fraud check → route → saga → result.
/// </summary>
public class PaymentOrchestrator : IPaymentOrchestrator
{
    private readonly IIdempotencyGuard _idempotencyGuard;
    private readonly IFraudRiskEngine _fraudRiskEngine;
    private readonly ISagaOrchestrator _sagaOrchestrator;
    private readonly IAuditStore _auditStore;
    private readonly ICircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly IEnumerable<IChannelAdapter> _channelAdapters;
    private readonly IPaymentRequestRepository _paymentRequestRepository;

    private const string ActorIdentity = "PaymentOrchestrator";

    public PaymentOrchestrator(
        IIdempotencyGuard idempotencyGuard,
        IFraudRiskEngine fraudRiskEngine,
        ISagaOrchestrator sagaOrchestrator,
        IAuditStore auditStore,
        ICircuitBreakerRegistry circuitBreakerRegistry,
        IEnumerable<IChannelAdapter> channelAdapters,
        IPaymentRequestRepository paymentRequestRepository)
    {
        _idempotencyGuard = idempotencyGuard ?? throw new ArgumentNullException(nameof(idempotencyGuard));
        _fraudRiskEngine = fraudRiskEngine ?? throw new ArgumentNullException(nameof(fraudRiskEngine));
        _sagaOrchestrator = sagaOrchestrator ?? throw new ArgumentNullException(nameof(sagaOrchestrator));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _channelAdapters = channelAdapters ?? throw new ArgumentNullException(nameof(channelAdapters));
        _paymentRequestRepository = paymentRequestRepository ?? throw new ArgumentNullException(nameof(paymentRequestRepository));
    }

    /// <inheritdoc />
    public async Task<PaymentResult> InitiatePaymentAsync(PaymentRequest request, CancellationToken ct)
    {
        // Step 1: Validate request
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return new PaymentResult(string.Empty, PaymentStatus.Failed, false, "Idempotency key is required.", null);

        // Step 2: Check idempotency
        var idempotencyResult = await _idempotencyGuard.CheckAsync(request.IdempotencyKey, ct);

        switch (idempotencyResult)
        {
            case IdempotencyCheckResult.Completed completed:
                // Return cached response for duplicate request
                if (completed.CachedResponse is PaymentResult cachedPaymentResult)
                    return cachedPaymentResult;
                return new PaymentResult(
                    request.TransactionReference,
                    PaymentStatus.Completed,
                    true,
                    null,
                    null);

            case IdempotencyCheckResult.InProgress:
                return new PaymentResult(
                    request.TransactionReference,
                    PaymentStatus.Processing,
                    false,
                    "Request is already being processed.",
                    null);
        }

        // Step 3: Assign TransactionReference
        var transactionReference = GenerateTransactionReference();

        // Step 4: Determine channel routing
        var routingResult = ResolveChannel(request.TransactionType, request.SourceAccount);
        if (!routingResult.Success)
        {
            return new PaymentResult(transactionReference, PaymentStatus.Failed, false, routingResult.ErrorMessage, null);
        }

        var channel = routingResult.Channel;

        // Create the payment request entity with the resolved channel
        var paymentEntity = PaymentRequest.Create(
            request.IdempotencyKey,
            transactionReference,
            request.TransactionType,
            request.Amount,
            request.SourceAccount,
            request.DestinationAccount,
            channel);

        // Register idempotency in-progress
        await _idempotencyGuard.RegisterInProgressAsync(request.IdempotencyKey, paymentEntity.Id, ct);

        // Persist initial state
        await _paymentRequestRepository.SaveAsync(paymentEntity, ct);

        // Audit: Created
        await AppendAuditAsync(transactionReference, "PaymentCreated", null, PaymentStatus.Created.ToString(), ct);

        // Step 5: Fraud check
        paymentEntity.MarkPendingFraudCheck();
        await _paymentRequestRepository.SaveAsync(paymentEntity, ct);
        await AppendAuditAsync(transactionReference, "FraudCheckStarted", PaymentStatus.Created.ToString(), PaymentStatus.PendingFraudCheck.ToString(), ct);

        var riskDecision = await _fraudRiskEngine.EvaluateAsync(paymentEntity, ct);

        if (riskDecision.Decision == RiskVerdict.Deny)
        {
            paymentEntity.Fail();
            await _paymentRequestRepository.SaveAsync(paymentEntity, ct);
            await AppendAuditAsync(transactionReference, "FraudDenied", PaymentStatus.PendingFraudCheck.ToString(), PaymentStatus.Failed.ToString(), ct);

            var deniedResult = new PaymentResult(transactionReference, PaymentStatus.Failed, false, $"Transaction denied by fraud check. Score: {riskDecision.Score}", null);
            await _idempotencyGuard.StoreAsync(request.IdempotencyKey, paymentEntity.Id, deniedResult, ct);
            return deniedResult;
        }

        if (riskDecision.Decision == RiskVerdict.Review)
        {
            paymentEntity.MarkManualReview(riskDecision.Score);
            await _paymentRequestRepository.SaveAsync(paymentEntity, ct);
            await AppendAuditAsync(transactionReference, "FraudReview", PaymentStatus.PendingFraudCheck.ToString(), PaymentStatus.ManualReview.ToString(), ct);

            var reviewResult = new PaymentResult(transactionReference, PaymentStatus.ManualReview, false, "Transaction queued for manual review.", null);
            await _idempotencyGuard.StoreAsync(request.IdempotencyKey, paymentEntity.Id, reviewResult, ct);
            return reviewResult;
        }

        // Approved
        paymentEntity.Approve(riskDecision.Score);
        await _paymentRequestRepository.SaveAsync(paymentEntity, ct);
        await AppendAuditAsync(transactionReference, "FraudApproved", PaymentStatus.PendingFraudCheck.ToString(), PaymentStatus.Approved.ToString(), ct);

        // Step 6: Route to channel — check circuit breaker
        var breaker = _circuitBreakerRegistry.GetBreaker(channel);
        if (breaker.State == CircuitBreakerState.Open)
        {
            paymentEntity.Fail();
            await _paymentRequestRepository.SaveAsync(paymentEntity, ct);
            await AppendAuditAsync(transactionReference, "CircuitBreakerOpen", PaymentStatus.Approved.ToString(), PaymentStatus.Failed.ToString(), ct);

            var unavailableResult = new PaymentResult(transactionReference, PaymentStatus.Failed, false, $"Channel {channel} is currently unavailable (circuit breaker open).", null);
            await _idempotencyGuard.StoreAsync(request.IdempotencyKey, paymentEntity.Id, unavailableResult, ct);
            return unavailableResult;
        }

        paymentEntity.MarkRouting();
        await _paymentRequestRepository.SaveAsync(paymentEntity, ct);
        await AppendAuditAsync(transactionReference, "Routing", PaymentStatus.Approved.ToString(), PaymentStatus.Routing.ToString(), ct);

        // Step 7: Execute via saga
        paymentEntity.MarkProcessing();
        await _paymentRequestRepository.SaveAsync(paymentEntity, ct);
        await AppendAuditAsync(transactionReference, "Processing", PaymentStatus.Routing.ToString(), PaymentStatus.Processing.ToString(), ct);

        var adapter = _channelAdapters.FirstOrDefault(a => a.Channel == channel);
        if (adapter is null)
        {
            paymentEntity.Fail();
            await _paymentRequestRepository.SaveAsync(paymentEntity, ct);
            await AppendAuditAsync(transactionReference, "AdapterNotFound", PaymentStatus.Processing.ToString(), PaymentStatus.Failed.ToString(), ct);

            var noAdapterResult = new PaymentResult(transactionReference, PaymentStatus.Failed, false, $"No adapter found for channel {channel}.", null);
            await _idempotencyGuard.StoreAsync(request.IdempotencyKey, paymentEntity.Id, noAdapterResult, ct);
            return noAdapterResult;
        }

        ChannelResult? channelResult = null;

        var sagaSteps = new List<SagaStepDefinition>
        {
            new("ProcessPayment",
                async innerCt =>
                {
                    channelResult = await adapter.ProcessAsync(paymentEntity, innerCt);
                },
                async innerCt =>
                {
                    await adapter.ReverseAsync(transactionReference, innerCt);
                })
        };

        var sagaResult = await _sagaOrchestrator.ExecuteAsync(transactionReference, sagaSteps, ct);

        if (sagaResult.Success && channelResult is not null && channelResult.Success)
        {
            paymentEntity.Complete();
            await _paymentRequestRepository.SaveAsync(paymentEntity, ct);
            await AppendAuditAsync(transactionReference, "Completed", PaymentStatus.Processing.ToString(), PaymentStatus.Completed.ToString(), ct);

            var successResult = new PaymentResult(transactionReference, PaymentStatus.Completed, true, null, channelResult.ProcessorReference);
            await _idempotencyGuard.StoreAsync(request.IdempotencyKey, paymentEntity.Id, successResult, ct);
            return successResult;
        }
        else
        {
            paymentEntity.Fail();
            await _paymentRequestRepository.SaveAsync(paymentEntity, ct);
            await AppendAuditAsync(transactionReference, "Failed", PaymentStatus.Processing.ToString(), PaymentStatus.Failed.ToString(), ct);

            var errorMessage = sagaResult.ErrorMessage ?? channelResult?.ErrorMessage ?? "Payment processing failed.";
            var failedResult = new PaymentResult(transactionReference, PaymentStatus.Failed, false, errorMessage, channelResult?.ProcessorReference);
            await _idempotencyGuard.StoreAsync(request.IdempotencyKey, paymentEntity.Id, failedResult, ct);
            return failedResult;
        }
    }

    /// <inheritdoc />
    public async Task<PaymentStatusResult> GetStatusAsync(string transactionReference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transactionReference))
            return new PaymentStatusResult(string.Empty, PaymentStatus.Failed, PaymentChannel.NIP, DateTime.MinValue, null, false);

        var payment = await _paymentRequestRepository.GetByTransactionReferenceAsync(transactionReference, ct);
        if (payment is null)
            return new PaymentStatusResult(transactionReference, PaymentStatus.Failed, PaymentChannel.NIP, DateTime.MinValue, null, false);

        var lastStateChange = payment.CompletedAtUtc ?? payment.CreatedAtUtc;
        return new PaymentStatusResult(payment.TransactionReference, payment.Status, payment.Channel, lastStateChange, null, true);
    }

    /// <inheritdoc />
    public async Task<PaymentStatusResult> GetStatusByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return new PaymentStatusResult(string.Empty, PaymentStatus.Failed, PaymentChannel.NIP, DateTime.MinValue, null, false);

        var payment = await _paymentRequestRepository.GetByIdempotencyKeyAsync(idempotencyKey, ct);
        if (payment is null)
            return new PaymentStatusResult(string.Empty, PaymentStatus.Failed, PaymentChannel.NIP, DateTime.MinValue, null, false);

        var lastStateChange = payment.CompletedAtUtc ?? payment.CreatedAtUtc;
        return new PaymentStatusResult(payment.TransactionReference, payment.Status, payment.Channel, lastStateChange, null, true);
    }

    /// <summary>
    /// Generates a globally unique transaction reference in the format "TXN-{Guid}".
    /// </summary>
    internal static string GenerateTransactionReference()
    {
        return $"TXN-{Guid.NewGuid()}";
    }

    /// <summary>
    /// Resolves the target payment channel based on transaction type and BIN range for card transactions.
    /// </summary>
    internal static ChannelRoutingResult ResolveChannel(PaymentTransactionType transactionType, string sourceAccount)
    {
        switch (transactionType)
        {
            case PaymentTransactionType.InterbankTransfer:
                return ChannelRoutingResult.Ok(PaymentChannel.NIP);

            case PaymentTransactionType.QRPayment:
                return ChannelRoutingResult.Ok(PaymentChannel.NQR);

            case PaymentTransactionType.BillPayment:
                return ChannelRoutingResult.Ok(PaymentChannel.EBillsPay);

            case PaymentTransactionType.USSDPayment:
                return ChannelRoutingResult.Ok(PaymentChannel.MCash);

            case PaymentTransactionType.RecurringDebit:
                return ChannelRoutingResult.Ok(PaymentChannel.DirectDebit);

            case PaymentTransactionType.BulkPayment:
                return ChannelRoutingResult.Ok(PaymentChannel.GAPS);

            case PaymentTransactionType.CardAuthorization:
                return ResolveCardChannel(sourceAccount);

            default:
                return ChannelRoutingResult.Error($"Unable to route transaction type '{transactionType}'. No channel mapping defined.");
        }
    }

    /// <summary>
    /// Resolves card channel based on BIN range:
    /// - Verve BIN ranges (starts with 506 or 650) → Interswitch
    /// - All other BINs → Cardify (Visa/Mastercard)
    /// </summary>
    private static ChannelRoutingResult ResolveCardChannel(string sourceAccount)
    {
        if (string.IsNullOrWhiteSpace(sourceAccount) || sourceAccount.Length < 3)
        {
            return ChannelRoutingResult.Error("Cannot determine card scheme: source account/PAN is missing or too short for BIN resolution.");
        }

        // Check Verve BIN ranges
        if (sourceAccount.StartsWith("506") || sourceAccount.StartsWith("650"))
        {
            return ChannelRoutingResult.Ok(PaymentChannel.Interswitch);
        }

        // Default to Cardify for Visa/Mastercard and other schemes
        return ChannelRoutingResult.Ok(PaymentChannel.Cardify);
    }

    private async Task AppendAuditAsync(
        string transactionReference,
        string action,
        string? previousState,
        string? newState,
        CancellationToken ct)
    {
        try
        {
            var entry = AuditEntry.Create(
                transactionReference,
                ActorIdentity,
                action,
                previousState,
                newState,
                transactionReference, // Use transactionReference as correlationId
                null); // Previous entry hash determined by AuditStore

            await _auditStore.AppendAsync(entry, ct);
        }
        catch
        {
            // Per Requirement 6 AC6: If the Audit_Store is unavailable, continue processing
            // The DeferredAuditQueue in the AuditStore implementation handles queueing
        }
    }
}

/// <summary>
/// Result of channel routing resolution.
/// </summary>
internal record ChannelRoutingResult
{
    public bool Success { get; }
    public PaymentChannel Channel { get; }
    public string? ErrorMessage { get; }

    private ChannelRoutingResult(bool success, PaymentChannel channel, string? errorMessage)
    {
        Success = success;
        Channel = channel;
        ErrorMessage = errorMessage;
    }

    public static ChannelRoutingResult Ok(PaymentChannel channel) =>
        new(true, channel, null);

    public static ChannelRoutingResult Error(string message) =>
        new(false, default, message);
}

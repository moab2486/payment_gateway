using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Disputes;

/// <summary>
/// Implements the dispute lifecycle management service.
/// Handles creation, timeframe validation, state transitions,
/// resolution workflows, and event publishing.
/// </summary>
public sealed class DisputeService : IDisputeService
{
    private readonly IDisputeRepository _disputeRepository;
    private readonly IPaymentRequestRepository _paymentRequestRepository;
    private readonly IPaymentOrchestrator _paymentOrchestrator;
    private readonly IAuditStore _auditStore;
    private readonly IDisputeEventPublisher _eventPublisher;
    private readonly DisputeOptions _options;
    private readonly ILogger<DisputeService> _logger;

    public DisputeService(
        IDisputeRepository disputeRepository,
        IPaymentRequestRepository paymentRequestRepository,
        IPaymentOrchestrator paymentOrchestrator,
        IAuditStore auditStore,
        IDisputeEventPublisher eventPublisher,
        IOptions<DisputeOptions> options,
        ILogger<DisputeService> logger)
    {
        _disputeRepository = disputeRepository;
        _paymentRequestRepository = paymentRequestRepository;
        _paymentOrchestrator = paymentOrchestrator;
        _auditStore = auditStore;
        _eventPublisher = eventPublisher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DisputeResult> RaiseDisputeAsync(DisputeRequest request, CancellationToken ct)
    {
        // Validate that the referenced transaction exists
        var payment = await _paymentRequestRepository.GetByTransactionReferenceAsync(
            request.TransactionReference, ct);

        if (payment is null)
        {
            return new DisputeResult(Guid.Empty, DisputeStatus.Opened, false,
                $"Transaction '{request.TransactionReference}' not found.");
        }

        // Validate timeframe - dispute must be within network-mandated window
        var maxDays = GetMaxDisputeWindowDays(payment.Channel);
        var elapsedDays = (int)(DateTime.UtcNow.Date - payment.CreatedAtUtc.Date).TotalDays;
        if (elapsedDays > maxDays)
        {
            return new DisputeResult(Guid.Empty, DisputeStatus.Opened, false,
                $"Dispute window expired. Transaction is {elapsedDays} days old, " +
                $"maximum allowed is {maxDays} days.");
        }

        // Create dispute record
        var dispute = DisputeRecord.Create(
            request.TransactionReference,
            request.ReasonCode,
            request.Amount,
            request.Evidence);

        await _disputeRepository.SaveAsync(dispute, ct);

        // Mark the payment as disputed
        try
        {
            payment.Dispute();
            await _paymentRequestRepository.SaveAsync(payment, ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Could not mark payment {TxRef} as disputed: {Message}",
                request.TransactionReference, ex.Message);
        }

        // Publish dispute-opened event
        await _eventPublisher.PublishDisputeOpenedAsync(
            new DisputeOpenedEvent(
                dispute.Id,
                dispute.TransactionReference,
                dispute.ReasonCode,
                dispute.Amount.Amount,
                dispute.Amount.CurrencyCode),
            ct);

        // Audit
        await _auditStore.AppendAsync(AuditEntry.Create(
            request.TransactionReference,
            "system",
            "dispute-opened",
            "none",
            DisputeStatus.Opened.ToString(),
            dispute.Id.ToString(),
            null), ct);

        _logger.LogInformation("Dispute {DisputeId} raised for transaction {TxRef}",
            dispute.Id, request.TransactionReference);

        return new DisputeResult(dispute.Id, DisputeStatus.Opened, true, null);
    }

    public async Task<DisputeResult> ResolveDisputeAsync(
        Guid disputeId, DisputeResolution resolution, CancellationToken ct)
    {
        var dispute = await _disputeRepository.GetByIdAsync(disputeId, ct);
        if (dispute is null)
        {
            return new DisputeResult(disputeId, DisputeStatus.Opened, false,
                $"Dispute '{disputeId}' not found.");
        }

        var previousStatus = dispute.Status;

        try
        {
            if (resolution.Decision == DisputeDecision.InFavour)
            {
                dispute.ResolveInFavour();
                await _disputeRepository.UpdateAsync(dispute, ct);

                // Initiate credit to cardholder via Payment Orchestration Service
                var payment = await _paymentRequestRepository
                    .GetByTransactionReferenceAsync(dispute.TransactionReference, ct);

                if (payment is not null)
                {
                    var creditRequest = PaymentRequest.Create(
                        idempotencyKey: $"dispute-credit-{disputeId}",
                        transactionReference: $"CREDIT-{dispute.TransactionReference}",
                        transactionType: PaymentTransactionType.InterbankTransfer,
                        amount: dispute.Amount,
                        sourceAccount: payment.DestinationAccount,
                        destinationAccount: payment.SourceAccount,
                        channel: payment.Channel);

                    await _paymentOrchestrator.InitiatePaymentAsync(creditRequest, ct);
                }

                // Publish resolved event
                await _eventPublisher.PublishDisputeResolvedAsync(
                    new DisputeResolvedEvent(dispute.Id, dispute.TransactionReference, "in-favour"),
                    ct);

                // Close the dispute after resolution
                dispute.Close();
                await _disputeRepository.UpdateAsync(dispute, ct);

                await _eventPublisher.PublishDisputeClosedAsync(
                    new DisputeClosedEvent(dispute.Id, dispute.TransactionReference),
                    ct);

                await _auditStore.AppendAsync(AuditEntry.Create(
                    dispute.TransactionReference,
                    "system",
                    "dispute-resolved-in-favour",
                    previousStatus.ToString(),
                    DisputeStatus.Closed.ToString(),
                    dispute.Id.ToString(),
                    null), ct);

                return new DisputeResult(dispute.Id, DisputeStatus.Closed, true, null);
            }
            else // Against
            {
                dispute.ResolveAgainst();
                await _disputeRepository.UpdateAsync(dispute, ct);

                // Publish resolved event
                await _eventPublisher.PublishDisputeResolvedAsync(
                    new DisputeResolvedEvent(dispute.Id, dispute.TransactionReference, "against"),
                    ct);

                // Close the dispute record
                dispute.Close();
                await _disputeRepository.UpdateAsync(dispute, ct);

                await _eventPublisher.PublishDisputeClosedAsync(
                    new DisputeClosedEvent(dispute.Id, dispute.TransactionReference),
                    ct);

                await _auditStore.AppendAsync(AuditEntry.Create(
                    dispute.TransactionReference,
                    "system",
                    "dispute-resolved-against",
                    previousStatus.ToString(),
                    DisputeStatus.Closed.ToString(),
                    dispute.Id.ToString(),
                    null), ct);

                return new DisputeResult(dispute.Id, DisputeStatus.Closed, true, null);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid state transition for dispute {DisputeId}: {Message}",
                disputeId, ex.Message);
            return new DisputeResult(disputeId, dispute.Status, false, ex.Message);
        }
    }

    public async Task<DisputeRecord?> GetDisputeAsync(Guid disputeId, CancellationToken ct)
    {
        return await _disputeRepository.GetByIdAsync(disputeId, ct);
    }

    private int GetMaxDisputeWindowDays(PaymentChannel channel)
    {
        var channelName = channel.ToString();
        if (_options.ChannelTimeframeDays.TryGetValue(channelName, out var days))
            return days;

        return _options.MaxDisputeWindowDays;
    }
}
